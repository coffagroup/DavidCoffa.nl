using Coffa.Api.Analytics;
using Coffa.Api.Infrastructure;

namespace Coffa.Api.Endpoints;

public static class AnalyticsEndpoints
{
    public static void AddWebsiteAnalytics(this WebApplicationBuilder builder)
    {
        builder.Services.AddMemoryCache();
        builder.Services.AddHttpClient<IWebsiteAnalyticsProvider, GoogleWebsiteAnalyticsProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
    }

    public static void MapWebsiteAnalytics(this WebApplication app)
    {
        var api = app.MapGroup("/api/admin/analytics").WithTags("Website-statistieken").RequireAuthorization();
        api.MapGet("/status", (CurrentActor actor, IWebsiteAnalyticsProvider analytics) =>
        {
            DemandAdmin(actor);
            return Results.Ok(new { configured = analytics.Configured, searchConsoleConfigured = analytics.SearchConsoleConfigured });
        });
        api.MapGet("/report", async (string? period, CurrentActor actor, IWebsiteAnalyticsProvider analytics, CancellationToken ct) =>
        {
            DemandAdmin(actor);
            return Results.Ok(await analytics.GetReportAsync(period ?? "30d", ct));
        }).RequireRateLimiting("assistant");
    }

    private static void DemandAdmin(CurrentActor actor)
    {
        if (!actor.IsAdmin) throw new ApiException(403, "forbidden", "Website-statistieken zijn alleen beschikbaar voor de beheerder.");
    }
}
