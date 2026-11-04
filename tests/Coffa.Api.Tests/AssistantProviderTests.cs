using System.Net;
using System.Text;
using System.Text.Json;
using Coffa.Api.Assistant;
using Coffa.Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Coffa.Api.Tests;

public sealed class AssistantProviderTests
{
    private const string TestKey = "unit-test-secret-never-a-live-api-key";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task PlanningUsesTheFixedOpenAiEndpointANonStoredStrictSchemaAndParsesStructuredOutput()
    {
        var suggestion = new AssistantModelPlan("Controleer dit voorstel.", [new("create_invoice", "Nieuwe factuur", "Voorbeeldklant", null, [new("title", JsonSerializer.SerializeToElement("Ontwikkelwerk"))], [new("Werkzaamheden", 2.5m, 85.5m, 0m, 21m)], [])], []);
        var handler = new StubHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal(TestKey, request.Headers.Authorization.Parameter);
            var raw = await request.Content!.ReadAsStringAsync();
            Assert.DoesNotContain(TestKey, raw);
            using var body = JsonDocument.Parse(raw);
            Assert.Equal("test-planning-model", body.RootElement.GetProperty("model").GetString());
            Assert.False(body.RootElement.GetProperty("store").GetBoolean());
            Assert.Equal("Maak een factuur voor Voorbeeldklant.", body.RootElement.GetProperty("input").GetString());
            var format = body.RootElement.GetProperty("text").GetProperty("format");
            Assert.Equal("json_schema", format.GetProperty("type").GetString());
            Assert.True(format.GetProperty("strict").GetBoolean());
            Assert.False(format.GetProperty("schema").GetProperty("additionalProperties").GetBoolean());
            return JsonResponse(new { status = "completed", output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = JsonSerializer.Serialize(suggestion, Json) } } } } });
        });
        using var client = new HttpClient(handler);
        var provider = new OpenAiAssistantProvider(client, Config(new() { ["OpenAI:ApiKey"] = TestKey, ["OpenAI:PlanningModel"] = "test-planning-model" }));
        Assert.True(provider.Configured);
        var result = await provider.PlanAsync("Maak een factuur voor Voorbeeldklant.", new(2026, 11, 4), 14, default);
        Assert.Equal(suggestion.Message, result.Message);
        Assert.Equal("create_invoice", Assert.Single(result.Actions).Kind);
        Assert.Equal(85.5m, Assert.Single(result.Actions[0].Lines).UnitPrice);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task BlogGenerationUsesItsConfiguredModelAndReturnsOnlyStructuredOutput()
    {
        var handler = new StubHandler(async request =>
        {
            Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri!.AbsoluteUri);
            Assert.Equal(TestKey, request.Headers.Authorization!.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal("test-blog-model", body.RootElement.GetProperty("model").GetString());
            Assert.False(body.RootElement.GetProperty("store").GetBoolean());
            Assert.Equal("Write an article draft.", body.RootElement.GetProperty("input").GetString());
            var format = body.RootElement.GetProperty("text").GetProperty("format");
            Assert.Equal("blog_test", format.GetProperty("name").GetString());
            Assert.True(format.GetProperty("strict").GetBoolean());
            Assert.False(format.GetProperty("schema").GetProperty("additionalProperties").GetBoolean());
            return JsonResponse(new { status = "completed", output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = "{\"answer\":\"draft\"}" } } } } });
        });
        using var client = new HttpClient(handler);
        var provider = new OpenAiAssistantProvider(client, Config(new() { ["OpenAI:ApiKey"] = TestKey, ["OpenAI:BlogModel"] = "test-blog-model" }));
        using var schema = JsonDocument.Parse("""{"type":"object","additionalProperties":false,"required":["answer"],"properties":{"answer":{"type":"string"}}}""");
        var result = await provider.GenerateJsonAsync("Only return JSON.", "Write an article draft.", schema.RootElement, "blog_test", default);
        Assert.Equal("draft", result.GetProperty("answer").GetString());
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task TranscriptionUsesMultipartTheConfiguredBearerAndTheCurrentTranscriptionModel()
    {
        var bytes = "RIFF1234WAVEaudio bytes used only by the fake provider"u8.ToArray();
        var handler = new StubHandler(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://api.openai.com/v1/audio/transcriptions", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal(TestKey, request.Headers.Authorization.Parameter);
            var form = Assert.IsType<MultipartFormDataContent>(request.Content);
            var parts = form.ToDictionary(part => part.Headers.ContentDisposition!.Name!.Trim('"'));
            Assert.Equal("gpt-transcribe", await parts["model"].ReadAsStringAsync());
            Assert.Equal("json", await parts["response_format"].ReadAsStringAsync());
            Assert.Equal("audio/wav", parts["file"].Headers.ContentType!.MediaType);
            Assert.Equal("opname.wav", parts["file"].Headers.ContentDisposition!.FileName!.Trim('"'));
            Assert.Equal(bytes, await parts["file"].ReadAsByteArrayAsync());
            return JsonResponse(new { text = "  Maak een onderhoudsabonnement.  " });
        });
        using var client = new HttpClient(handler);
        var provider = new OpenAiAssistantProvider(client, Config(new() { ["OPENAI_API_KEY"] = TestKey }));
        using var input = new MemoryStream(bytes);
        var file = new FormFile(input, 0, bytes.Length, "file", "private-customer-recording.wav");
        Assert.Equal("Maak een onderhoudsabonnement.", await provider.TranscribeAsync(file, default));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task AnUnconfiguredKeyNeverCreatesAnExternalHttpRequest()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("No request should be sent."));
        using var client = new HttpClient(handler);
        var provider = new OpenAiAssistantProvider(client, Config(new() { ["OpenAI:ApiKey"] = "  " }));
        Assert.False(provider.Configured);
        var error = await Assert.ThrowsAsync<ApiException>(() => provider.PlanAsync("Maak een factuur.", new(2026, 11, 4), 14, default));
        Assert.Equal(503, error.StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, 502)]
    [InlineData(HttpStatusCode.TooManyRequests, 429)]
    [InlineData(HttpStatusCode.Redirect, 502)]
    public async Task UpstreamErrorsAndRedirectsNeverExposeBodiesCredentialsOrFollowRedirects(HttpStatusCode status, int expectedStatus)
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent($"Provider echoed credential {TestKey} and confidential upstream details.") };
            response.Headers.Location = new Uri("https://untrusted-redirect.example/collect");
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        var provider = new OpenAiAssistantProvider(client, Config(new() { ["OpenAI:ApiKey"] = TestKey }));
        var error = await Assert.ThrowsAsync<ApiException>(() => provider.PlanAsync("Maak een factuur.", new(2026, 11, 4), 14, default));
        Assert.Equal(expectedStatus, error.StatusCode);
        Assert.DoesNotContain(TestKey, error.ToString());
        Assert.DoesNotContain("confidential upstream details", error.ToString());
        Assert.DoesNotContain("untrusted-redirect", error.ToString());
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("completed", "refusal", 422)]
    [InlineData("incomplete", "output_text", 502)]
    public async Task RefusedOrIncompleteResponsesCannotBeReportedAsSuccessfulPlans(string status, string partType, int expectedStatus)
    {
        var handler = new StubHandler(_ => Task.FromResult(JsonResponse(new { status, output = new[] { new { type = "message", content = new[] { new { type = partType, text = "{\"message\":\"Already done\",\"actions\":[],\"questions\":[]}", refusal = "Not processed" } } } } })));
        using var client = new HttpClient(handler);
        var provider = new OpenAiAssistantProvider(client, Config(new() { ["OpenAI:ApiKey"] = TestKey }));
        var error = await Assert.ThrowsAsync<ApiException>(() => provider.PlanAsync("Maak een factuur.", new(2026, 11, 4), 14, default));
        Assert.Equal(expectedStatus, error.StatusCode);
    }

    [Theory]
    [InlineData("[]", false)]
    [InlineData("{\"status\":\"completed\",\"output\":[1]}", false)]
    [InlineData("{\"status\":\"completed\",\"output\":[{\"content\":[1]}]}", false)]
    [InlineData("[]", true)]
    public async Task MalformedProviderShapesAreNormalizedToApiErrors(string json, bool transcription)
    {
        var handler = new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") }));
        using var client = new HttpClient(handler);
        var provider = new OpenAiAssistantProvider(client, Config(new() { ["OpenAI:ApiKey"] = TestKey }));
        using var stream = new MemoryStream("RIFF1234WAVEaudio header"u8.ToArray());
        var file = new FormFile(stream, 0, stream.Length, "file", "recording.wav");
        var error = transcription
            ? await Assert.ThrowsAsync<ApiException>(() => provider.TranscribeAsync(file, default))
            : await Assert.ThrowsAsync<ApiException>(() => provider.PlanAsync("Maak een factuur.", new(2026, 11, 4), 14, default));
        Assert.Equal(502, error.StatusCode);
    }

    private static IConfiguration Config(Dictionary<string, string?> values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    private static HttpResponseMessage JsonResponse(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value, Json), Encoding.UTF8, "application/json") };
    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return respond(request);
        }
    }
}
