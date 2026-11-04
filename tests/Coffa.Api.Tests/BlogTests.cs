using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Coffa.Api.Assistant;
using Coffa.Api.Data;
using Coffa.Api.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Coffa.Api.Tests;

[Collection("api")]
public sealed class BlogTests(ApiFixture fixture)
{
    [Fact]
    public async Task PublishedArticlesArePublicButDraftsAndAdministrationAreProtected()
    {
        var publishedSlug = "blog-test-" + Guid.NewGuid().ToString("N");
        var draftSlug = "blog-draft-" + Guid.NewGuid().ToString("N");
        await using (var scope = fixture.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.BlogArticles.AddRange(Article(publishedSlug, "published"), Article(draftSlug, "draft"));
            await db.SaveChangesAsync();
        }

        using var anonymous = fixture.CreateClient();
        var list = await ApiFixture.JsonAsync(await anonymous.GetAsync("/api/blog"));
        Assert.Contains(list.EnumerateArray(), article => article.GetProperty("slug").GetString() == publishedSlug);
        Assert.DoesNotContain(list.EnumerateArray(), article => article.GetProperty("slug").GetString() == draftSlug);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/blog/{publishedSlug}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/blog/{draftSlug}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/blog")).StatusCode);

        using var employee = await EmployeeAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/admin/blog")).StatusCode);

        using var admin = await fixture.AdminAsync();
        var managed = await ApiFixture.JsonAsync(await admin.GetAsync("/api/admin/blog"));
        Assert.Contains(managed.GetProperty("articles").EnumerateArray(), article => article.GetProperty("slug").GetString() == draftSlug);
    }

    [Fact]
    public async Task ResearchCanBecomeAnEditedDraftAndOnlyAnExplicitPublishMakesItPublic()
    {
        var provider = new FakeBlogProvider();
        await using var factory = WithProvider(provider);
        using var admin = await AdminAsync(factory);
        var research = await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/admin/blog/research", new
        {
            topic = "AI voor mkb-administratie",
            targetAudience = "Nederlandse mkb-ondernemers",
            targetRegion = "Nederland",
            existingArticles = new[] { new { slug = "existing-ai", title = "Bestaand AI-artikel", excerpt = "Bestaande inhoud.", category = "AI" } }
        }));
        var researchId = research.GetProperty("id").GetGuid();
        Assert.Equal(5, research.GetProperty("opportunities").GetArrayLength());
        Assert.DoesNotContain("monthlySearchVolume", research.GetRawText(), StringComparison.OrdinalIgnoreCase);

        var draft = await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/admin/blog/draft", new { researchId, keyword = "AI administratie mkb" }));
        var articleId = draft.GetProperty("id").GetGuid();
        var slug = draft.GetProperty("slug").GetString()!;
        Assert.Equal("draft", draft.GetProperty("status").GetString());
        using var anonymous = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/blog/{slug}")).StatusCode);

        var published = await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin, HttpMethod.Post, $"/api/admin/blog/{articleId}/publish"));
        Assert.Equal("published", published.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/blog/{slug}")).StatusCode);

        var unpublished = await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin, HttpMethod.Post, $"/api/admin/blog/{articleId}/unpublish"));
        Assert.Equal("draft", unpublished.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/blog/{slug}")).StatusCode);
        Assert.Equal(2, provider.Calls);
        Assert.Contains("Bestaand AI-artikel", provider.ResearchInput, StringComparison.Ordinal);
    }

    private WebApplicationFactory<Program> WithProvider(FakeBlogProvider provider) => fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
    {
        services.RemoveAll<IBlogContentProvider>();
        services.AddSingleton<IBlogContentProvider>(provider);
    }));

    private async Task<HttpClient> AdminAsync(WebApplicationFactory<Program> factory)
    {
        var email = "blog-admin-" + Guid.NewGuid().ToString("N") + "@example.com";
        const string password = "BlogAdmin-Integration-42!";
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Blog test administrator" };
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "SuperAdmin")).Succeeded);
        }
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost"), HandleCookies = true, AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.OK, (await ApiFixture.SendAsync(client, HttpMethod.Post, "/api/auth/login", new { email, password })).StatusCode);
        var setup = await client.GetFromJsonAsync<JsonElement>("/api/auth/2fa/setup");
        Assert.Equal(HttpStatusCode.OK, (await ApiFixture.SendAsync(client, HttpMethod.Post, "/api/auth/2fa/enable", new { code = ApiFixture.Totp(setup.GetProperty("sharedKey").GetString()!) })).StatusCode);
        return client;
    }

    private async Task<HttpClient> EmployeeAsync()
    {
        var email = "blog-employee-" + Guid.NewGuid().ToString("N") + "@example.com";
        const string password = "BlogEmployee-Integration-42!";
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Blog test employee" };
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Employee")).Succeeded);
        }
        var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await ApiFixture.SendAsync(client, HttpMethod.Post, "/api/auth/login", new { email, password })).StatusCode);
        return client;
    }

    private static BlogArticle Article(string slug, string status) => new()
    {
        Slug = slug,
        Category = "Test",
        FocusKeyword = "blog test",
        SearchIntent = "informational",
        Title = "Blog test article",
        MetaTitle = "Blog test article",
        MetaDescription = "A sufficiently useful blog test description.",
        Excerpt = "A blog test article.",
        Intro = "An introduction to a blog test article.",
        SectionsJson = JsonSerializer.Serialize(new[] { new { heading = "Test section", paragraphs = new[] { "A test paragraph." }, points = Array.Empty<string>() } }),
        Status = status,
        PublishedAt = status == "published" ? DateTimeOffset.UtcNow : null,
        CreatedBy = "test"
    };

    private sealed class FakeBlogProvider : IBlogContentProvider
    {
        public bool Configured => true;
        public int Calls { get; private set; }
        public string ResearchInput { get; private set; } = "";

        public Task<JsonElement> GenerateJsonAsync(string instructions, string input, JsonElement schema, string schemaName, CancellationToken cancellationToken)
        {
            Calls++;
            if (schemaName == "blog_keyword_research")
            {
                ResearchInput = input;
                return Task.FromResult(JsonDocument.Parse("""
                    {"summary":"Kwalitatieve kansen voor ondernemers.","opportunities":[
                    {"keyword":"ai administratie mkb","intent":"informational","priority":"high","rationale":"Sluit aan op het onderwerp.","suggestedTitle":"AI in de administratie","format":"Praktische gids"},
                    {"keyword":"administratie automatiseren","intent":"commercial","priority":"medium","rationale":"Praktische vraag.","suggestedTitle":"Administratie automatiseren","format":"Uitleg"},
                    {"keyword":"ai documenten controleren","intent":"informational","priority":"medium","rationale":"Concrete taak.","suggestedTitle":"Documenten controleren met AI","format":"Stappenplan"},
                    {"keyword":"mkb software integreren","intent":"commercial","priority":"low","rationale":"Gerelateerde behoefte.","suggestedTitle":"Software koppelen","format":"Checklist"},
                    {"keyword":"ai workflow mkb","intent":"informational","priority":"low","rationale":"Aanvullende invalshoek.","suggestedTitle":"Een AI-workflow kiezen","format":"Gids"}]}
                    """).RootElement.Clone());
            }
            Assert.Equal("blog_article_draft", schemaName);
            Assert.Contains("AI administratie mkb", input, StringComparison.Ordinal);
            return Task.FromResult(JsonDocument.Parse("""
                {"slug":"ai-administratie-mkb","category":"AI & automatisering","focusKeyword":"AI administratie mkb","relatedKeywords":["administratie automatiseren"],"searchIntent":"informational","title":"AI in de administratie van het mkb","metaTitle":"AI in de mkb-administratie","metaDescription":"Praktische uitleg over zorgvuldig automatiseren met AI.","excerpt":"Een praktische gids voor mkb-ondernemers.","intro":"Begin met een afgebakende taak en laat medewerkers de uitkomst controleren.","sections":[
                {"heading":"Kies een taak","paragraphs":["Breng het proces in kaart en begin klein."],"points":[]},
                {"heading":"Controleer uitkomsten","paragraphs":["Laat een medewerker uitzonderingen beoordelen."],"points":[]},
                {"heading":"Meet en verbeter","paragraphs":["Vergelijk de nieuwe werkwijze met de huidige."],"points":[]}],"internalLinks":["/ai-automatisering"]}
                """).RootElement.Clone());
        }
    }
}
