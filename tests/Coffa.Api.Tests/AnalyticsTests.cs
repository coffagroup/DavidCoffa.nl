using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coffa.Api.Analytics;
using Coffa.Api.Domain;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Coffa.Api.Tests;

[Collection("api")]
public sealed class AnalyticsTests(ApiFixture fixture)
{
    [Fact]
    public async Task GoogleAnalyticsProviderRequiresAnExplicitPropertyAndRejectsUnsupportedPeriods()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        using var http = new HttpClient(new NeverCalledHandler());
        var analytics = new GoogleWebsiteAnalyticsProvider(http, configuration, new MemoryCache(new MemoryCacheOptions()), NullLogger<GoogleWebsiteAnalyticsProvider>.Instance);
        Assert.False(analytics.Configured);
        Assert.False(analytics.SearchConsoleConfigured);
        var missing = await Assert.ThrowsAsync<Coffa.Api.Infrastructure.ApiException>(() => analytics.GetReportAsync("30d", default));
        Assert.Equal(503, missing.StatusCode);
        var invalid = await Assert.ThrowsAsync<Coffa.Api.Infrastructure.ApiException>(() => analytics.GetReportAsync("all", default));
        Assert.Equal(400, invalid.StatusCode);
    }

    [Fact]
    public async Task WebsiteAnalyticsIsAdminOnlyAndReturnsSelectedPeriodAndSearchConsoleData()
    {
        var provider = new FakeAnalyticsProvider();
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWebsiteAnalyticsProvider>();
            services.AddSingleton<IWebsiteAnalyticsProvider>(provider);
        }));
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/analytics/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/analytics/report?period=7d")).StatusCode);

        using var admin = await AdminAsync(factory);
        var status = await ApiFixture.JsonAsync(await admin.GetAsync("/api/admin/analytics/status"));
        Assert.True(status.GetProperty("configured").GetBoolean());
        Assert.True(status.GetProperty("searchConsoleConfigured").GetBoolean());

        var report = await ApiFixture.JsonAsync(await admin.GetAsync("/api/admin/analytics/report?period=90d"));
        Assert.Equal("90d", report.GetProperty("period").GetString());
        Assert.Equal(128, report.GetProperty("visitors").GetInt64());
        Assert.Equal("google / organic", report.GetProperty("topSources")[0].GetProperty("source").GetString());
        Assert.Equal("mkb software automatiseren", report.GetProperty("searchConsole").GetProperty("topQueries")[0].GetProperty("query").GetString());
        Assert.Equal(1, provider.Calls);
        Assert.Equal("90d", provider.LastPeriod);
    }

    [Fact]
    public async Task AnalyticsRejectsUnsupportedPeriodsAndEmployeeAccess()
    {
        var provider = new FakeAnalyticsProvider();
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWebsiteAnalyticsProvider>();
            services.AddSingleton<IWebsiteAnalyticsProvider>(provider);
        }));
        using var employee = await EmployeeAsync(factory);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/admin/analytics/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/admin/analytics/report?period=30d")).StatusCode);

        using var admin = await AdminAsync(factory);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/analytics/report?period=all")).StatusCode);
        Assert.Equal(0, provider.Calls);
    }

    private static async Task<HttpClient> AdminAsync(WebApplicationFactory<Program> factory)
    {
        var email = "analytics-admin-" + Guid.NewGuid().ToString("N") + "@example.com";
        const string password = "AnalyticsAdmin-Integration-42!";
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Analytics test administrator" };
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "SuperAdmin")).Succeeded);
        }
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost"), HandleCookies = true, AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.OK, (await ApiFixture.SendAsync(client, HttpMethod.Post, "/api/auth/login", new { email, password })).StatusCode);
        var setup = await client.GetFromJsonAsync<JsonElement>("/api/auth/2fa/setup");
        Assert.Equal(HttpStatusCode.OK, (await ApiFixture.SendAsync(client, HttpMethod.Post, "/api/auth/2fa/enable", new { code = ApiFixture.Totp(setup.GetProperty("sharedKey").GetString()!) })).StatusCode);
        return client;
    }

    private static async Task<HttpClient> EmployeeAsync(WebApplicationFactory<Program> factory)
    {
        var email = "analytics-employee-" + Guid.NewGuid().ToString("N") + "@example.com";
        const string password = "AnalyticsEmployee-Integration-42!";
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Analytics test employee" };
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Employee")).Succeeded);
        }
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost"), HandleCookies = true, AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.OK, (await ApiFixture.SendAsync(client, HttpMethod.Post, "/api/auth/login", new { email, password })).StatusCode);
        return client;
    }

    private sealed class FakeAnalyticsProvider : IWebsiteAnalyticsProvider
    {
        public bool Configured => true;
        public bool SearchConsoleConfigured => true;
        public int Calls { get; private set; }
        public string LastPeriod { get; private set; } = "";

        public Task<WebsiteAnalyticsReport> GetReportAsync(string period, CancellationToken cancellationToken)
        {
            if (period is not ("7d" or "30d" or "90d" or "12m"))
                throw new Coffa.Api.Infrastructure.ApiException(400, "analytics_period_invalid", "Kies een geldige periode.");
            Calls++; LastPeriod = period;
            var search = new SearchConsoleSummary(true, 23, 410, 0.056, 5.2,
                [new SearchQuery("mkb software automatiseren", 12, 150, 0.08, 3.1)],
                [new SearchPage("https://davidcoffa.nl/blog/ai", 10, 90, 0.11, 4.2)], null);
            return Task.FromResult(new WebsiteAnalyticsReport(period, "2026-10-01", "2026-11-04", 128, 180, 420,
                [new AnalyticsTrend("2026-W44", 128, 180, 420)],
                [new AnalyticsPage("/", 180, 120)],
                [new AnalyticsSource("google / organic", 80, 63)],
                search, DateTimeOffset.UtcNow));
        }
    }

    private sealed class NeverCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("No Google request should be made without explicit configuration.");
    }
}
