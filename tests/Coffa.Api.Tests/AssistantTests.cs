using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Coffa.Api.Assistant;
using Coffa.Api.Data;
using Coffa.Api.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Coffa.Api.Tests;

[Collection("api")]
public sealed class AssistantTests(ApiFixture fixture)
{
    [Fact]
    public async Task AssistantRequiresAFullAdminSessionEvenWhenEmployeeHasBusinessPermissions()
    {
        var provider = new FakeAssistant();
        await using var factory = WithProvider(provider);
        using var anonymous = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/assistant/status")).StatusCode);

        foreach (var role in new[] { "Customer", "Employee" })
        {
            using var user = await Login(factory, role);
            Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/assistant/status")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await ApiFixture.SendAsync(user, HttpMethod.Post, "/api/assistant/plan", new { text = "Maak een factuur" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Upload(user, "recording.wav", Wave())).StatusCode);
        }
        Assert.Equal(0, provider.PlanCalls);
        Assert.Equal(0, provider.TranscriptionCalls);
    }

    [Fact]
    public async Task MissingApiConfigurationIsExplicitAndDoesNotAttemptAnExternalRequest()
    {
        var provider = new FakeAssistant { Configured = false };
        await using var factory = WithProvider(provider);
        using var admin = await Login(factory);
        var status = await ApiFixture.JsonAsync(await admin.GetAsync("/api/assistant/status"));
        Assert.False(status.GetProperty("configured").GetBoolean());
        Assert.DoesNotContain("key", status.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/assistant/plan", new { text = "Maak een hostingabonnement" })).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await Upload(admin, "recording.wav", Wave())).StatusCode);
        Assert.Equal(0, provider.PlanCalls);
        Assert.Equal(0, provider.TranscriptionCalls);
    }

    [Fact]
    public async Task InvoiceAndSubscriptionPlansResolveRealRelationshipsButCannotIssueShareOrPersistRecords()
    {
        var name = "Voice organization " + Guid.NewGuid().ToString("N");
        var projectName = "Voice project " + Guid.NewGuid().ToString("N");
        var foreignId = Guid.NewGuid();
        var provider = new FakeAssistant
        {
            Plan = new("Controleer twee voorstellen.", [
                new("create_invoice", "Conceptfactuur", name.ToLowerInvariant(), projectName,
                    [Field("title", "Ontwikkelwerk"), Field("invoiceDate", "2026-11-04"), Field("dueDate", "2026-11-18"), Field("id", foreignId), Field("organizationId", foreignId), Field("projectId", foreignId), Field("role", "SuperAdmin"), Field("status", "paid"), Field("sharedWithCustomer", true)],
                    [new("Ontwikkeling", 2.5m, 85.5m, 10m, 21m)], []),
                new("create_subscription", "Hostingabonnement", name, null,
                    [Field("name", "Hosting"), Field("amount", 25m), Field("taxRate", 21m), Field("frequency", "monthly"), Field("startDate", "2026-11-04"), Field("nextInvoiceDate", "2026-12-04"), Field("automaticInvoicing", true)], [], [])
            ], [])
        };
        await using var factory = WithProvider(provider);
        using var admin = await Login(factory);
        var organizationId = await Organization(admin, name);
        var project = await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/projects", new { organizationId, name = projectName, description = "Planning test", sharedWithCustomer = false }));
        var before = await RecordCounts(factory);
        var response = await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/assistant/plan", new { text = $"Maak voor {name} een factuur en een hostingabonnement." }));
        Assert.Equal(2, response.GetProperty("actions").GetArrayLength());
        var invoice = response.GetProperty("actions")[0].GetProperty("initial");
        Assert.Equal(organizationId, invoice.GetProperty("organizationId").GetGuid());
        Assert.Equal(project.GetProperty("id").GetGuid(), invoice.GetProperty("projectId").GetGuid());
        Assert.False(invoice.TryGetProperty("id", out _));
        Assert.False(invoice.TryGetProperty("role", out _));
        Assert.False(invoice.TryGetProperty("status", out _));
        Assert.False(invoice.GetProperty("sharedWithCustomer").GetBoolean());
        Assert.Equal("2026-11-04", invoice.GetProperty("invoiceDate").GetString());
        Assert.Equal("2026-11-18", invoice.GetProperty("dueDate").GetString());
        var line = invoice.GetProperty("lines")[0];
        Assert.Equal(2.5m, line.GetProperty("quantity").GetDecimal());
        Assert.Equal(85.5m, line.GetProperty("unitPrice").GetDecimal());
        Assert.Equal(21m, line.GetProperty("taxRate").GetDecimal());
        var subscription = response.GetProperty("actions")[1].GetProperty("initial");
        Assert.False(subscription.GetProperty("automaticInvoicing").GetBoolean());
        Assert.Equal(25m, subscription.GetProperty("amount").GetDecimal());
        Assert.Equal(before, await RecordCounts(factory));
        Assert.Equal(1, provider.PlanCalls);
    }

    [Fact]
    public async Task UnknownOrAmbiguousOrganizationNamesNeverUseModelSuppliedIds()
    {
        var duplicate = "Duplicated voice name " + Guid.NewGuid().ToString("N");
        var unknown = "Unknown voice name " + Guid.NewGuid().ToString("N");
        var guessed = Guid.NewGuid();
        var provider = new FakeAssistant
        {
            Plan = new("Kies de juiste klant.", [
                new("create_invoice", "Ambigue klant", duplicate, null, [Field("organizationId", guessed), Field("title", "Website")], [new("Werk", 1m, 100m, 0m, 21m)], []),
                new("create_subscription", "Onbekende klant", unknown, null, [Field("organizationId", guessed), Field("name", "Onderhoud"), Field("amount", 20m)], [], [])
            ], [])
        };
        await using var factory = WithProvider(provider);
        using var admin = await Login(factory);
        await Organization(admin, duplicate);
        await Organization(admin, duplicate);
        var response = await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/assistant/plan", new { text = "Maak de genoemde factuur en het abonnement." }));
        foreach (var action in response.GetProperty("actions").EnumerateArray())
        {
            Assert.False(HasGuid(action.GetProperty("initial"), "organizationId"));
            Assert.NotEmpty(action.GetProperty("warnings").EnumerateArray());
        }
        Assert.DoesNotContain(guessed.ToString(), response.GetRawText());
    }

    [Fact]
    public async Task UnsupportedActionsAndInvalidInputCannotReachBusinessEndpoints()
    {
        var provider = new FakeAssistant { Plan = new("Geen toegestane actie.", [new("delete_organization", "Verwijderen", null, null, [Field("id", Guid.NewGuid())], [], [])], []) };
        await using var factory = WithProvider(provider);
        using var admin = await Login(factory);
        var before = await RecordCounts(factory);
        Assert.Equal(HttpStatusCode.BadRequest, (await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/assistant/plan", new { text = "  " })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/assistant/plan", new { text = new string('x', 50001) })).StatusCode);
        Assert.Equal(0, provider.PlanCalls);
        Assert.Equal(HttpStatusCode.BadGateway, (await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/assistant/plan", new { text = "Verwijder de klant" })).StatusCode);
        Assert.Equal(before, await RecordCounts(factory));
        Assert.Equal(1, provider.PlanCalls);
    }

    [Fact]
    public async Task TranscriptionRequiresCsrfAndRealAudioHeadersAndDoesNotSaveTheRecording()
    {
        var provider = new FakeAssistant { Transcript = "Maak voor mijn klant een conceptfactuur van honderd euro." };
        await using var factory = WithProvider(provider);
        using var admin = await Login(factory);
        var missingCsrf = await admin.PostAsJsonAsync("/api/assistant/plan", new { text = "Maak een factuur" });
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        using (var content = Audio("recording.wav", Wave()))
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/assistant/transcribe", content)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(admin, "recording.webm", Encoding.UTF8.GetBytes("<html>not an audio recording</html>"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Upload(admin, "recording.pdf", "%PDF-1.4\nnot audio"u8.ToArray())).StatusCode);
        Assert.Equal(0, provider.TranscriptionCalls);
        var result = await ApiFixture.JsonAsync(await Upload(admin, "recording.wav", Wave()));
        Assert.Equal(provider.Transcript, result.GetProperty("text").GetString());
        Assert.Equal(1, provider.TranscriptionCalls);
        Assert.Equal(0, provider.PlanCalls);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.DocumentRecords.AnyAsync(x => x.OriginalName == "recording.wav"));
    }

    private WebApplicationFactory<Program> WithProvider(FakeAssistant provider) => fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
    {
        services.RemoveAll<IAssistantProvider>();
        services.AddSingleton<IAssistantProvider>(provider);
    }));
    private static HttpClient Client(WebApplicationFactory<Program> factory) => factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost"), HandleCookies = true, AllowAutoRedirect = false });
    private static async Task<HttpClient> Login(WebApplicationFactory<Program> factory, string role = "SuperAdmin")
    {
        var email = "assistant-" + Guid.NewGuid().ToString("N") + "@example.com";
        const string password = "Assistant-Integration-42!";
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Assistant test user" };
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
            if (role == "Employee")
                foreach (var permission in new[] { "crm.read", "crm.write", "finance.read", "finance.write", "projects.read", "projects.write" })
                    Assert.True((await users.AddClaimAsync(user, new System.Security.Claims.Claim("permission", permission))).Succeeded);
        }
        var client = Client(factory);
        await ApiFixture.JsonAsync(await ApiFixture.SendAsync(client, HttpMethod.Post, "/api/auth/login", new { email, password }));
        if (role == "SuperAdmin")
        {
            var setup = await client.GetFromJsonAsync<JsonElement>("/api/auth/2fa/setup");
            await ApiFixture.JsonAsync(await ApiFixture.SendAsync(client, HttpMethod.Post, "/api/auth/2fa/enable", new { code = ApiFixture.Totp(setup.GetProperty("sharedKey").GetString()!) }));
        }
        return client;
    }
    private static AssistantField Field(string name, object? value) => new(name, JsonSerializer.SerializeToElement(value));
    private static async Task<Guid> Organization(HttpClient admin, string name)
    {
        var result = await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/organizations", new { name, email = "voice-test@example.com" }));
        return result.GetProperty("id").GetGuid();
    }
    private static async Task<(int Organizations, int Invoices, int Subscriptions)> RecordCounts(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Organizations.CountAsync(), await db.Invoices.CountAsync(), await db.Subscriptions.CountAsync());
    }
    private static bool HasGuid(JsonElement value, string key) => value.TryGetProperty(key, out var id) && id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out var parsed) && parsed != Guid.Empty;
    private static MultipartFormDataContent Audio(string name, byte[] bytes)
    {
        var content = new MultipartFormDataContent();
        var audio = new ByteArrayContent(bytes);
        audio.Headers.ContentType = new MediaTypeHeaderValue(name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? "audio/wav" : "audio/webm");
        content.Add(audio, "file", name);
        return content;
    }
    private static async Task<HttpResponseMessage> Upload(HttpClient client, string name, byte[] bytes)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/assistant/transcribe") { Content = Audio(name, bytes) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }
    private static byte[] Wave()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(38); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(2); writer.Write((short)0);
        return stream.ToArray();
    }
    private sealed class FakeAssistant : IAssistantProvider
    {
        public bool Configured { get; init; } = true;
        public string Transcript { get; init; } = "Een ingesproken opdracht.";
        public AssistantModelPlan Plan { get; init; } = new("Geen actie.", [], []);
        public int PlanCalls { get; private set; }
        public int TranscriptionCalls { get; private set; }
        public Task<string> TranscribeAsync(IFormFile file, CancellationToken ct) { TranscriptionCalls++; return Task.FromResult(Transcript); }
        public Task<AssistantModelPlan> PlanAsync(string text, DateOnly today, int paymentTermDays, CancellationToken ct) { PlanCalls++; return Task.FromResult(Plan); }
    }
}
