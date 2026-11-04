using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Coffa.Api.Infrastructure;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Caching.Memory;

namespace Coffa.Api.Analytics;

public sealed record AnalyticsPeriod(string Key, int Days, string Granularity);
public sealed record AnalyticsMetric(string Label, long Value);
public sealed record AnalyticsTrend(string Date, long Visitors, long Sessions, long PageViews);
public sealed record AnalyticsPage(string Path, long Views, long Visitors);
public sealed record AnalyticsSource(string Source, long Sessions, long Visitors);
public sealed record SearchConsoleSummary(bool Configured, long Clicks, long Impressions, double ClickThroughRate, double AveragePosition, IReadOnlyList<SearchQuery> TopQueries, IReadOnlyList<SearchPage> TopPages, string? Message);
public sealed record SearchQuery(string Query, long Clicks, long Impressions, double ClickThroughRate, double AveragePosition);
public sealed record SearchPage(string Page, long Clicks, long Impressions, double ClickThroughRate, double AveragePosition);
public sealed record WebsiteAnalyticsReport(string Period, string StartDate, string EndDate, long Visitors, long Sessions, long PageViews, IReadOnlyList<AnalyticsTrend> Trend, IReadOnlyList<AnalyticsPage> TopPages, IReadOnlyList<AnalyticsSource> TopSources, SearchConsoleSummary SearchConsole, DateTimeOffset FetchedAt);

public interface IWebsiteAnalyticsProvider
{
    bool Configured { get; }
    bool SearchConsoleConfigured { get; }
    Task<WebsiteAnalyticsReport> GetReportAsync(string period, CancellationToken cancellationToken);
}

public sealed class GoogleWebsiteAnalyticsProvider(
    HttpClient http,
    IConfiguration configuration,
    IMemoryCache cache,
    ILogger<GoogleWebsiteAnalyticsProvider> logger) : IWebsiteAnalyticsProvider
{
    private const string AnalyticsScope = "https://www.googleapis.com/auth/analytics.readonly";
    private const string SearchConsoleScope = "https://www.googleapis.com/auth/webmasters.readonly";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, AnalyticsPeriod> Periods = new Dictionary<string, AnalyticsPeriod>
    {
        ["7d"] = new("7d", 7, "date"),
        ["30d"] = new("30d", 30, "date"),
        ["90d"] = new("90d", 90, "yearWeek"),
        ["12m"] = new("12m", 365, "yearMonth")
    };

    private string? CredentialsJson => configuration["Analytics:GoogleServiceAccountJson"];
    private string? PropertyId => configuration["Analytics:Ga4PropertyId"];
    private string? SearchConsoleSite => configuration["Analytics:SearchConsoleSiteUrl"];
    public bool Configured => !string.IsNullOrWhiteSpace(CredentialsJson) && !string.IsNullOrWhiteSpace(PropertyId) && PropertyId.All(char.IsAsciiDigit);
    public bool SearchConsoleConfigured => Configured && !string.IsNullOrWhiteSpace(SearchConsoleSite);

    public async Task<WebsiteAnalyticsReport> GetReportAsync(string period, CancellationToken cancellationToken)
    {
        if (!Periods.TryGetValue(period, out var range)) throw new ApiException(400, "analytics_period_invalid", "Kies een periode van 7 dagen, 30 dagen, 90 dagen of 12 maanden.");
        if (!Configured) throw new ApiException(503, "analytics_not_configured", "Website-statistieken zijn nog niet gekoppeld. Stel de Google Analytics-configuratie van de API in; de website zelf blijft gewoon werken.");
        return await cache.GetOrCreateAsync($"website-analytics:{range.Key}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            return await LoadReportAsync(range, cancellationToken);
        }) ?? throw new ApiException(503, "analytics_unavailable", "De website-statistieken zijn tijdelijk niet beschikbaar.");
    }

    private async Task<WebsiteAnalyticsReport> LoadReportAsync(AnalyticsPeriod range, CancellationToken cancellationToken)
    {
        var amsterdam = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, amsterdam).DateTime);
        var startDate = today.AddDays(-(range.Days - 1));
        var credential = CreateCredential();
        var token = await AccessTokenAsync(credential, cancellationToken);
        var start = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var totals = await RunAnalyticsReportAsync(token, start, end, [], [], 1, cancellationToken);
        var trend = await RunAnalyticsReportAsync(token, start, end, [range.Granularity], ["totalUsers", "sessions", "screenPageViews"], 400, cancellationToken, orderByDimension: range.Granularity);
        var pages = await RunAnalyticsReportAsync(token, start, end, ["pagePath"], ["screenPageViews", "totalUsers"], 10, cancellationToken, "screenPageViews");
        var sources = await RunAnalyticsReportAsync(token, start, end, ["sessionSourceMedium"], ["sessions", "totalUsers"], 10, cancellationToken, "sessions");
        var totalsRow = totals.Rows.FirstOrDefault();
        SearchConsoleSummary searchConsole;
        if (!SearchConsoleConfigured) searchConsole = new SearchConsoleSummary(false, 0, 0, 0, 0, [], [], "Koppel Search Console voor klikken, vertoningen en zoekopdrachten.");
        else
        {
            try { searchConsole = await LoadSearchConsoleAsync(credential, startDate, today, cancellationToken); }
            catch (ApiException exception)
            {
                logger.LogWarning("Search Console query failed; Analytics report remains available.");
                searchConsole = new SearchConsoleSummary(true, 0, 0, 0, 0, [], [], exception.Message);
            }
        }

        return new WebsiteAnalyticsReport(
            range.Key, start, end,
            Metric(totalsRow, 0), Metric(totalsRow, 1), Metric(totalsRow, 2),
            trend.Rows.Select(row => new AnalyticsTrend(
                DimensionLabel(row, range.Granularity),
                Metric(row, 0), Metric(row, 1), Metric(row, 2))).ToList(),
            pages.Rows.Select(row => new AnalyticsPage(Dimension(row, 0), Metric(row, 0), Metric(row, 1))).ToList(),
            sources.Rows.Select(row => new AnalyticsSource(Dimension(row, 0), Metric(row, 0), Metric(row, 1))).ToList(),
            searchConsole,
            DateTimeOffset.UtcNow);
    }

    private GoogleCredential CreateCredential()
    {
        try { return CredentialFactory.FromJson<ServiceAccountCredential>(CredentialsJson).ToGoogleCredential().CreateScoped(AnalyticsScope, SearchConsoleScope); }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or JsonException or FormatException)
        {
            logger.LogError("Website analytics service-account configuration is invalid.");
            throw new ApiException(503, "analytics_credentials_invalid", "De Google-serviceaccount voor statistieken is ongeldig ingesteld.");
        }
    }

    private async Task<string> AccessTokenAsync(GoogleCredential credential, CancellationToken cancellationToken)
    {
        try
        {
            if (credential.UnderlyingCredential is not ITokenAccess tokenAccess) throw new InvalidOperationException("The analytics credential cannot request access tokens.");
            return await tokenAccess.GetAccessTokenForRequestAsync(null, cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or Google.Apis.Auth.OAuth2.Responses.TokenResponseException)
        {
            logger.LogWarning("Google analytics authorization failed.");
            throw new ApiException(502, "analytics_authorization_failed", "Google kon de statistiekenconfiguratie niet autoriseren. Controleer het serviceaccount en de toegangsrechten.");
        }
    }

    private async Task<AnalyticsResult> RunAnalyticsReportAsync(
        string token, string start, string end, string[] dimensions, string[] metrics, int limit,
        CancellationToken cancellationToken, string? orderBy = null, string? orderByDimension = null)
    {
        var requestBody = new Dictionary<string, object>
        {
            ["dateRanges"] = new[] { new { startDate = start, endDate = end } },
            ["metrics"] = metrics.Select(name => new { name }).ToArray(),
            ["limit"] = limit
        };
        if (dimensions.Length > 0) requestBody["dimensions"] = dimensions.Select(name => new { name }).ToArray();
        if (!string.IsNullOrWhiteSpace(orderByDimension)) requestBody["orderBys"] = new[] { new { dimension = new { dimensionName = orderByDimension }, desc = false } };
        else if (!string.IsNullOrWhiteSpace(orderBy)) requestBody["orderBys"] = new[] { new { metric = new { metricName = orderBy }, desc = true } };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://analyticsdata.googleapis.com/v1beta/properties/{PropertyId}:runReport")
        {
            Content = JsonContent.Create(requestBody, options: Json)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAsync(request, cancellationToken, "Google Analytics");
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var rows = document.RootElement.TryGetProperty("rows", out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Select(row => new AnalyticsRow(
                row.TryGetProperty("dimensionValues", out var dim) ? dim.EnumerateArray().Select(value => value.GetProperty("value").GetString() ?? "").ToArray() : [],
                row.TryGetProperty("metricValues", out var met) ? met.EnumerateArray().Select(value => long.TryParse(value.GetProperty("value").GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0).ToArray() : [])).ToList()
            : [];
        return new AnalyticsResult(rows);
    }

    private async Task<SearchConsoleSummary> LoadSearchConsoleAsync(GoogleCredential credential, DateOnly start, DateOnly today, CancellationToken cancellationToken)
    {
        var availableUntil = today.AddDays(-3);
        if (availableUntil < start)
            return new SearchConsoleSummary(true, 0, 0, 0, 0, [], [], "Search Console rapporteert met vertraging; er zijn nog geen gegevens voor deze periode.");
        var token = await AccessTokenAsync(credential, cancellationToken);
        var url = $"https://www.googleapis.com/webmasters/v3/sites/{Uri.EscapeDataString(SearchConsoleSite!)}/searchAnalytics/query";
        var from = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var until = availableUntil.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var totals = await RunSearchConsoleAsync(token, url, from, until, [], cancellationToken);
        var queries = await RunSearchConsoleAsync(token, url, from, until, ["query"], cancellationToken);
        var pages = await RunSearchConsoleAsync(token, url, from, until, ["page"], cancellationToken);
        var top = queries.Rows.OrderByDescending(row => row.Clicks).Take(10)
            .Select(row => new SearchQuery(row.Dimensions.FirstOrDefault() ?? "", row.Clicks, row.Impressions, row.Ctr, row.Position)).ToList();
        var topPages = pages.Rows.OrderByDescending(row => row.Clicks).Take(10)
            .Select(row => new SearchPage(row.Dimensions.FirstOrDefault() ?? "", row.Clicks, row.Impressions, row.Ctr, row.Position)).ToList();
        var total = totals.Rows.FirstOrDefault();
        return new SearchConsoleSummary(true, total?.Clicks ?? 0, total?.Impressions ?? 0, total?.Ctr ?? 0, total?.Position ?? 0, top, topPages, null);
    }

    private async Task<SearchConsoleResult> RunSearchConsoleAsync(string token, string url, string start, string end, string[] dimensions, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { startDate = start, endDate = end, dimensions, rowLimit = 25 }, options: Json)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await SendAsync(request, cancellationToken, "Google Search Console");
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var rows = document.RootElement.TryGetProperty("rows", out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Select(row => new SearchConsoleRow(
                row.TryGetProperty("keys", out var keys) ? keys.EnumerateArray().Select(key => key.GetString() ?? "").ToArray() : [],
                Number(row, "clicks"), Number(row, "impressions"), Decimal(row, "ctr"), Decimal(row, "position"))).ToList()
            : [];
        return new SearchConsoleResult(rows);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken, string provider)
    {
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken); }
        catch (HttpRequestException) { throw new ApiException(503, "analytics_provider_unavailable", $"{provider} is tijdelijk niet bereikbaar."); }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new ApiException(504, "analytics_provider_timeout", $"{provider} reageert te langzaam. Probeer het later opnieuw."); }
        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();
            throw new ApiException(status == HttpStatusCode.Forbidden ? 424 : 502, "analytics_provider_error",
                status == HttpStatusCode.Forbidden
                    ? $"Het serviceaccount heeft geen toegang tot {provider}. Deel de property met het serviceaccount."
                    : $"{provider} gaf geen statistieken terug. Controleer of de API is ingeschakeld en probeer later opnieuw.");
        }
        return response;
    }

    private static string Dimension(AnalyticsRow? row, int index) => row is not null && row.Dimensions.Length > index ? row.Dimensions[index] : "";
    private static long Metric(AnalyticsRow? row, int index) => row is not null && row.Metrics.Length > index ? row.Metrics[index] : 0;
    private static string DimensionLabel(AnalyticsRow row, string granularity)
    {
        var value = Dimension(row, 0);
        if (granularity == "date" && value.Length == 8 && DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (granularity == "yearMonth" && value.Length == 6) return $"{value[..4]}-{value[4..]}";
        if (granularity == "yearWeek" && value.Length == 6) return $"{value[..4]}-W{value[4..]}";
        return value;
    }

    private static long Number(JsonElement row, string name) => row.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;
    private static double Decimal(JsonElement row, string name) => row.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : 0;
    private sealed record AnalyticsRow(string[] Dimensions, long[] Metrics);
    private sealed record AnalyticsResult(IReadOnlyList<AnalyticsRow> Rows);
    private sealed record SearchConsoleRow(string[] Dimensions, long Clicks, long Impressions, double Ctr, double Position);
    private sealed record SearchConsoleResult(IReadOnlyList<SearchConsoleRow> Rows);
}
