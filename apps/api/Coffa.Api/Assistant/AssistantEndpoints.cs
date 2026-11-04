using System.Globalization;
using Coffa.Api.Commercial;
using Coffa.Api.Data;
using Coffa.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Coffa.Api.Assistant;

public static class AssistantEndpoints
{
    public const long MaxAudioBytes = 15 * 1024 * 1024;
    public sealed record PlanRequest(string Text);
    public static void AddAssistant(this WebApplicationBuilder builder)
    {
        builder.Services.AddHttpClient<OpenAiAssistantProvider>(client => client.Timeout = TimeSpan.FromSeconds(50))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        builder.Services.AddScoped<IAssistantProvider>(services => services.GetRequiredService<OpenAiAssistantProvider>());
        builder.Services.AddScoped<IBlogContentProvider>(services => services.GetRequiredService<OpenAiAssistantProvider>());
        builder.Services.AddScoped<AssistantPlanner>();
    }
    public static void MapAssistant(this WebApplication app)
    {
        var api = app.MapGroup("/api/assistant").WithTags("Spraakassistent").RequireAuthorization();
        api.MapGet("/status", (CurrentActor actor, IAssistantProvider provider) =>
        {
            DemandAdmin(actor);
            return Results.Ok(new { configured = provider.Configured });
        });
        api.MapPost("/transcribe", async (HttpRequest request, CurrentActor actor, IAssistantProvider provider, AuditService audit, CancellationToken ct) =>
        {
            DemandAdmin(actor); DemandConfigured(provider);
            if (!request.HasFormContentType || request.ContentLength > MaxAudioBytes + 128 * 1024)
                throw new ApiException(400, "invalid_audio", "Kies een geluidsbestand van maximaal 15 MB.");
            var form = await request.ReadFormAsync(ct);
            if (form.Files.Count != 1 || form.Files.GetFile("file") is not { } file) throw new ApiException(400, "invalid_audio", "Kies één geluidsbestand.");
            await ValidateAudio(file, ct);
            var text = await provider.TranscribeAsync(file, ct);
            await audit.RecordAsync("assistant.transcribed", "assistant", details: $"Audiobestand verwerkt ({file.Length} bytes). De opname en tekst zijn niet bewaard.", cancellationToken: ct);
            return Results.Ok(new { text });
        }).RequireRateLimiting("assistant");
        api.MapPost("/plan", async (PlanRequest input, CurrentActor actor, IAssistantProvider provider, AssistantPlanner planner, AppDbContext db, AuditService audit, CancellationToken ct) =>
        {
            DemandAdmin(actor); DemandConfigured(provider);
            var text = Validation.Required(input.Text, "een opdracht", 12000);
            var settings = await db.BusinessSettings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, ct);
            var days = Math.Clamp(settings?.PaymentTermDays ?? 14, 1, 365);
            var today = CommercialService.Today;
            var suggestion = await provider.PlanAsync(text, today, days, ct);
            var plan = await planner.PrepareAsync(suggestion, today, days, ct);
            await audit.RecordAsync("assistant.plan_prepared", "assistant", details: $"{plan.Actions.Count} invoervoorstellen voorbereid; geen bedrijfsrecords aangemaakt.", cancellationToken: ct);
            return Results.Ok(plan);
        }).RequireRateLimiting("assistant");
    }
    private static void DemandAdmin(CurrentActor actor)
    {
        if (!actor.IsAdmin) throw new ApiException(403, "forbidden", "De spraakassistent is alleen beschikbaar voor de beheerder.");
    }
    private static void DemandConfigured(IAssistantProvider provider)
    {
        if (!provider.Configured) throw new ApiException(503, "assistant_not_configured", "De spraakassistent is nog niet ingesteld. Configureer OPENAI_API_KEY op de API-server.");
    }
    public static string AudioType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".webm" => "audio/webm", ".mp4" or ".m4a" => "audio/mp4", ".mp3" or ".mpga" => "audio/mpeg",
        ".wav" => "audio/wav", ".mpeg" => "audio/mpeg", ".ogg" => "audio/ogg", ".flac" => "audio/flac", _ => ""
    };
    private static async Task ValidateAudio(IFormFile file, CancellationToken ct)
    {
        if (file.Length < 16 || file.Length > MaxAudioBytes || AudioType(file.FileName).Length == 0)
            throw new ApiException(400, "invalid_audio", "Gebruik WebM, MP4/M4A, MP3, WAV, MPEG, OGG of FLAC van maximaal 15 MB.");
        await using var stream = file.OpenReadStream();
        var header = new byte[16]; var length = await stream.ReadAtLeastAsync(header, 12, throwOnEndOfStream: false, cancellationToken: ct);
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var valid = length >= 12 && extension switch
        {
            ".webm" => header.AsSpan(0, 4).SequenceEqual(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 }),
            ".mp4" or ".m4a" => header.AsSpan(4, 4).SequenceEqual("ftyp"u8),
            ".wav" => header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WAVE"u8),
            ".flac" => header.AsSpan(0, 4).SequenceEqual("fLaC"u8),
            ".ogg" => header.AsSpan(0, 4).SequenceEqual("OggS"u8),
            ".mp3" or ".mpga" => header.AsSpan(0, 3).SequenceEqual("ID3"u8) || header[0] == 0xff && (header[1] & 0xe0) == 0xe0,
            ".mpeg" => header[0] == 0xff && (header[1] & 0xe0) == 0xe0 || header[0] == 0 && header[1] == 0 && header[2] == 1,
            _ => false
        };
        if (!valid) throw new ApiException(400, "invalid_audio", "Dit bestand heeft geen herkenbaar audioformaat. Neem opnieuw op of kies een ander geluidsbestand.");
    }
}
