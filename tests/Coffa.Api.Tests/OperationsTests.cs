using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Coffa.Api.Data;
using Coffa.Api.Domain;
using Coffa.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Coffa.Api.Tests;

[Collection("api")]
public class OperationsTests(ApiFixture fixture)
{
    [Fact]
    public async Task DashboardShowsCurrentTasksAndOnlyMonthScopedFinancialInformation()
    {
        using var admin = await fixture.AdminAsync(); var organization = await Organization(admin, "Dashboard customer"); var project = await Project(admin, organization, "Dashboard project", true);
        var user = await ApiFixture.JsonAsync(await admin.GetAsync("/api/auth/me")); var assignedToUserId = user.GetProperty("id").GetString();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        async Task<Guid> CreateTask(string title, string status, DateOnly dueDate)
        {
            var task = await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/tasks", new { organizationId = organization, projectId = project, title, status, priority = "normal", dueDate, assignedToUserId, sharedWithCustomer = true }));
            return task.GetProperty("id").GetGuid();
        }
        var dueThisMonth = await CreateTask("Due this month", "todo", today);
        var activeBeyondMonth = await CreateTask("Active beyond this month", "in_progress", today.AddMonths(1));
        var overdue = await CreateTask("Overdue task", "todo", today.AddMonths(-1));
        await CreateTask("Future task", "todo", today.AddMonths(1));
        await CreateTask("Completed task", "done", today);

        var dashboard = await ApiFixture.JsonAsync(await admin.GetAsync("/api/dashboard"));
        var tasks = dashboard.GetProperty("tasks").EnumerateArray().Select(task => task.GetProperty("id").GetGuid()).ToArray();
        Assert.Contains(dueThisMonth, tasks); Assert.Contains(activeBeyondMonth, tasks); Assert.Contains(overdue, tasks);
        Assert.Equal(3, dashboard.GetProperty("kpis").GetProperty("openTasks").GetInt32());
        Assert.False(dashboard.TryGetProperty("revenueSeries", out _));
        Assert.True(dashboard.GetProperty("kpis").TryGetProperty("monthlyRevenue", out _));
        Assert.True(dashboard.GetProperty("kpis").TryGetProperty("monthlyCosts", out _));
        Assert.False(dashboard.GetProperty("kpis").TryGetProperty("outstandingInvoices", out _));

        var finance = await ApiFixture.JsonAsync(await admin.GetAsync($"/api/finance?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}"));
        Assert.True(finance.TryGetProperty("invoicedRevenue", out _));
    }

    [Fact]
    public async Task CustomersCannotReadOtherTenantsOrInternalCrmAndProjects()
    {
        using var admin = await fixture.AdminAsync();
        var first = await Organization(admin, "Operations customer A"); var second = await Organization(admin, "Operations customer B");
        var firstProject = await Project(admin, first, "Shared A", true); var hiddenProject = await Project(admin, first, "Internal A", false); var otherProject = await Project(admin, second, "Shared B", true);
        using var customer = await User(first);
        Assert.Equal(HttpStatusCode.NotFound, (await customer.GetAsync($"/api/organizations/{second}")).StatusCode);
        var org = await ApiFixture.JsonAsync(await customer.GetAsync($"/api/organizations/{first}")); Assert.False(org.TryGetProperty("internalNotes", out _));
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync($"/api/notes?organizationId={first}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/time-entries")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await customer.GetAsync($"/api/projects/{hiddenProject}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await customer.GetAsync($"/api/projects/{otherProject}")).StatusCode);
        var project = await ApiFixture.JsonAsync(await customer.GetAsync($"/api/projects/{firstProject}")); Assert.False(project.GetProperty("project").TryGetProperty("budget", out _));
        var list = await ApiFixture.JsonAsync(await customer.GetAsync("/api/projects")); Assert.All(list.GetProperty("items").EnumerateArray(), x => Assert.Equal(first, x.GetProperty("organizationId").GetGuid()));
        Assert.Equal(HttpStatusCode.Forbidden, (await ApiFixture.SendAsync(customer, HttpMethod.Post, "/api/projects", new { organizationId = first, name = "Unauthorized project" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await customer.GetAsync($"/api/documents?organizationId={second}")).StatusCode);
    }

    [Fact]
    public async Task TicketConversationHidesInternalCommentsAndNotifiesCustomers()
    {
        using var admin = await fixture.AdminAsync(); var orgId = await Organization(admin, "Support organization"); var otherId = await Organization(admin, "Support foreign organization");
        using var customer = await User(orgId); using var other = await User(otherId);
        var ticket = await ApiFixture.JsonAsync(await ApiFixture.SendAsync(customer, HttpMethod.Post, "/api/tickets", new { organizationId = orgId, title = "Website question", description = "Can you help with my website?", category = "website", priority = "normal" })); var id = ticket.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/tickets/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ApiFixture.SendAsync(customer, HttpMethod.Post, "/api/tickets", new { organizationId = otherId, title = "Cross tenant", description = "Should not be accepted" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ApiFixture.SendAsync(customer, HttpMethod.Post, $"/api/tickets/{id}/comments", new { body = "Cannot add internal notes", isInternal = true })).StatusCode);
        await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin, HttpMethod.Post, $"/api/tickets/{id}/comments", new { body = "CONFIDENTIAL INTERNAL NOTE", isInternal = true }));
        await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin, HttpMethod.Post, $"/api/tickets/{id}/comments", new { body = "I have investigated your question.", isInternal = false }));
        var detail = await ApiFixture.JsonAsync(await customer.GetAsync($"/api/tickets/{id}")); Assert.Equal("waiting_customer", detail.GetProperty("ticket").GetProperty("status").GetString()); Assert.DoesNotContain("CONFIDENTIAL", detail.GetRawText());
        var comments = await ApiFixture.JsonAsync(await customer.GetAsync($"/api/tickets/{id}/comments")); Assert.Single(comments.GetProperty("items").EnumerateArray()); Assert.DoesNotContain("isInternal", comments.GetRawText());
        await ApiFixture.JsonAsync(await ApiFixture.SendAsync(customer, HttpMethod.Post, $"/api/tickets/{id}/comments", new { body = "Thank you, another detail." }));
        detail = await ApiFixture.JsonAsync(await customer.GetAsync($"/api/tickets/{id}")); Assert.Equal("in_progress", detail.GetProperty("ticket").GetProperty("status").GetString());
        var notifications = await ApiFixture.JsonAsync(await customer.GetAsync("/api/notifications")); Assert.NotEmpty(notifications.GetProperty("items").EnumerateArray());
        var notification = notifications.GetProperty("items")[0].GetProperty("id").GetGuid(); Assert.Equal(HttpStatusCode.NotFound, (await ApiFixture.SendAsync(other, HttpMethod.Post, $"/api/notifications/{notification}/read")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ApiFixture.SendAsync(customer, HttpMethod.Post, $"/api/notifications/{notification}/read")).StatusCode);
    }

    [Fact]
    public async Task DocumentsArePrivateTenantCheckedAndSharedExplicitly()
    {
        using var admin = await fixture.AdminAsync(); var first = await Organization(admin, "Document owner"); var second = await Organization(admin, "Document stranger"); using var customer = await User(first); using var stranger = await User(second);
        var uploaded = await Upload(admin, first, "contract.pdf", "%PDF-1.4\nprivate document"u8.ToArray(), shared: true); var id = uploaded.GetProperty("id").GetGuid();
        Assert.False(uploaded.TryGetProperty("storageKey", out _));
        var download = await customer.GetAsync($"/api/documents/{id}/download"); Assert.Equal(HttpStatusCode.OK, download.StatusCode); Assert.Contains("attachment", download.Content.Headers.ContentDisposition!.ToString()); Assert.Equal("%PDF-1.4\nprivate document", await download.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/documents/{id}/download")).StatusCode);
        using var anonymous = fixture.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/documents/{id}/download")).StatusCode);
        await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin, HttpMethod.Put, $"/api/documents/{id}", new { category = "contract", sharedWithCustomer = false })); Assert.Equal(HttpStatusCode.NotFound, (await customer.GetAsync($"/api/documents/{id}/download")).StatusCode);
        var ticket = await ApiFixture.JsonAsync(await ApiFixture.SendAsync(customer, HttpMethod.Post, "/api/tickets", new { organizationId = first, title = "Attachment", description = "Here is an attachment" }));
        var attached = await Upload(customer, first, "screenshot.txt", Encoding.UTF8.GetBytes("Screenshot explanation"), ticketId: ticket.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.OK, (await customer.GetAsync($"/api/documents/{attached.GetProperty("id").GetGuid()}/download")).StatusCode);
        var invalid = await UploadResponse(admin, first, "fake.pdf", Encoding.UTF8.GetBytes("not a PDF")); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task TimersPersistHistoricalRatesAndInvoicedTimeCannotBeChanged()
    {
        using var admin = await fixture.AdminAsync(); var org = await Organization(admin, "Timed work"); var project = await Project(admin, org, "Timed project", false);
        var start = await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/time-entries/timer/start", new { organizationId = org, projectId = project, description = "Develop feature", isBillable = true, hourlyRate = 125.5m }));
        Assert.Equal(HttpStatusCode.Conflict, (await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/time-entries/timer/start", new { organizationId = org, description = "Second timer" })).StatusCode);
        using (var scope = fixture.Factory.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var timer = await db.ActiveTimers.FindAsync(start.GetProperty("id").GetGuid()); timer!.StartedAt = DateTimeOffset.UtcNow.AddMinutes(-20); await db.SaveChangesAsync(); }
        var entry = await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/time-entries/timer/stop")); var id = entry.GetProperty("id").GetGuid(); Assert.Equal(125.5m, entry.GetProperty("hourlyRate").GetDecimal()); Assert.True(entry.GetProperty("durationMinutes").GetInt32() >= 20);
        Assert.Equal(HttpStatusCode.Conflict, (await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/time-entries/timer/stop")).StatusCode);
        using (var scope = fixture.Factory.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var stored = await db.TimeEntries.FindAsync(id); stored!.InvoiceId = Guid.NewGuid(); await db.SaveChangesAsync(); }
        Assert.Equal(HttpStatusCode.Conflict, (await ApiFixture.SendAsync(admin, HttpMethod.Put, $"/api/time-entries/{id}", new { organizationId = org, projectId = project, description = "Changed", workDate = DateOnly.FromDateTime(DateTime.UtcNow), durationMinutes = 60, isBillable = true, hourlyRate = 999m })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ApiFixture.SendAsync(admin, HttpMethod.Delete, $"/api/time-entries/{id}")).StatusCode);
    }

    [Fact]
    public async Task LeadConversionPreservesContactsNotesAndIsIdempotent()
    {
        using var admin = await fixture.AdminAsync(); var lead = await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/leads", new { companyName = "Won prospect", contactName = "Contact person", email = "prospect@example.com", stage = "won", expectedValue = 2000m, notes = "Original lead context", tags = new[] { "hosting" } })); var id = lead.GetProperty("id").GetGuid();
        await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin, HttpMethod.Post, $"/api/leads/{id}/activities", new { kind = "call", body = "Discovery conversation preserved." }));
        var first = await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin, HttpMethod.Post, $"/api/leads/{id}/convert")); var second = await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin, HttpMethod.Post, $"/api/leads/{id}/convert")); Assert.Equal(first.GetProperty("organizationId").GetGuid(), second.GetProperty("organizationId").GetGuid());
        var dossier = await ApiFixture.JsonAsync(await admin.GetAsync($"/api/organizations/{first.GetProperty("organizationId").GetGuid()}/dossier")); Assert.Contains("Original lead context", dossier.GetRawText()); Assert.Contains("Discovery conversation preserved", dossier.GetRawText()); Assert.Single(dossier.GetProperty("contacts").EnumerateArray());
    }

    [Fact]
    public async Task TimeSummariesUseAllFilteredRowsAndHideFinancialValuesWithoutPermission()
    {
        using var admin = await fixture.AdminAsync(); var org = await Organization(admin, "Filtered time summary");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var billable in new[] { true, false })
            await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/time-entries", new { organizationId = org, description = "Summary work", workDate = today, durationMinutes = 60, isBillable = billable, hourlyRate = billable ? 120m : 999m }));
        var result = await ApiFixture.JsonAsync(await admin.GetAsync($"/api/time-entries?organizationId={org}&pageSize=1"));
        Assert.Single(result.GetProperty("items").EnumerateArray());
        var summary = result.GetProperty("summary");
        Assert.Equal(2m, summary.GetProperty("totalHours").GetDecimal());
        Assert.Equal(120m, summary.GetProperty("billableValue").GetDecimal());
        Assert.Equal(60m, summary.GetProperty("effectiveHourlyRate").GetDecimal());
        using var employee = await User(null, "Employee", "time.read");
        result = await ApiFixture.JsonAsync(await employee.GetAsync($"/api/time-entries?organizationId={org}"));
        Assert.Equal(2m, result.GetProperty("summary").GetProperty("totalHours").GetDecimal());
        Assert.DoesNotContain("hourlyRate", result.GetRawText());
        Assert.DoesNotContain("billableValue", result.GetRawText());
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync($"/api/organizations/{org}/commercial-summary")).StatusCode);
        var activity = await ApiFixture.JsonAsync(await admin.GetAsync($"/api/activities?organizationId={org}"));
        Assert.NotEmpty(activity.GetProperty("items").EnumerateArray());
        Assert.All(activity.GetProperty("items").EnumerateArray(), item => Assert.Equal(org, item.GetProperty("organizationId").GetGuid()));
    }

    [Fact]
    public async Task EmployeePermissionsDoNotExposeFinanceOrInternalCustomerData()
    {
        using var admin = await fixture.AdminAsync(); var org = await Organization(admin, "Employee permission company"); var project = await Project(admin, org, "Budget restricted", true);
        using var employee = await User(null, "Employee", "projects.read", "tickets.read", "tickets.write");
        var detail = await ApiFixture.JsonAsync(await employee.GetAsync($"/api/projects/{project}")); Assert.False(detail.GetProperty("project").TryGetProperty("budget", out _));
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/invoices")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/notes")).StatusCode); Assert.Equal(HttpStatusCode.OK, (await employee.GetAsync("/api/assignees")).StatusCode);
        var choices = await ApiFixture.JsonAsync(await employee.GetAsync("/api/organization-options"));
        var choice = choices.GetProperty("items").EnumerateArray().First(x => x.GetProperty("id").GetGuid() == org);
        Assert.True(choice.TryGetProperty("name", out _));
        Assert.False(choice.TryGetProperty("internalNotes", out _));
        Assert.False(choice.TryGetProperty("email", out _));
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.GetAsync("/api/expenses")).StatusCode);
        var dashboard = await ApiFixture.JsonAsync(await employee.GetAsync("/api/dashboard"));
        Assert.False(dashboard.GetProperty("kpis").TryGetProperty("monthlyRevenue", out _));
        Assert.False(dashboard.GetProperty("kpis").TryGetProperty("monthlyCosts", out _));
    }

    [Fact]
    public async Task PublicContactRequiresServiceSecretAndStoresExactlyOneLeadAcrossRetries()
    {
        const string key = "integration-test-contact-key-0123456789";
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Contact:ApiKey"] = key })));
        using var client = factory.CreateClient(); var id = Guid.NewGuid(); var payload = new { id, name = "Website prospect", email = "prospect@example.com", subject = "New website", message = "I would like to discuss a website.", source = "website" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/public/contact", payload)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Coffa-Contact-Key", key);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/public/contact", payload)).StatusCode); Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/public/contact", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/public/contact", new { id, name = "Changed", email = "prospect@example.com", message = "Different message for same ID", source = "website" })).StatusCode);
        using var scope = fixture.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Equal(1, await db.SalesLeads.CountAsync(x => x.Id == id));
    }

    [Fact]
    public async Task TicketEmailOutboxDistinguishesDevelopmentDeliveryAndCancelsRevokedRecipients()
    {
        using var admin = await fixture.AdminAsync(); var org = await Organization(admin, "Ticket email recipient"); using var customer = await User(org);
        var me = await ApiFixture.JsonAsync(await customer.GetAsync("/api/auth/me")); var userId = me.GetProperty("id").GetString()!;
        var ticket = await ApiFixture.JsonAsync(await ApiFixture.SendAsync(customer, HttpMethod.Post, "/api/tickets", new { organizationId = org, title = "Email notification", description = "A durable notification test" }));
        using var scope = fixture.Factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(x => x.Id == userId);
        var notification = new UserNotification { UserId = userId, TicketId = ticket.GetProperty("id").GetGuid(), Message = "Public ticket update", EmailRecipient = user.Email, EmailState = "pending", CreatedAt = DateTimeOffset.Parse("2000-01-01T00:00:00Z") };
        db.UserNotifications.Add(notification); await db.SaveChangesAsync();
        var email = new TestEmail(); var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Tickets:EmailNotificationsEnabled"] = "true" }).Build();
        var dispatcher = new TicketNotificationDispatcher(db, email, config, Microsoft.Extensions.Logging.Abstractions.NullLogger<TicketNotificationDispatcher>.Instance);
        Assert.True(await dispatcher.DispatchOneAsync()); Assert.Equal("development_outbox", notification.EmailState); Assert.Null(notification.EmailSentAt); Assert.Equal(1, email.Calls);
        notification.EmailState = "pending"; notification.EmailRetryAt = null; user.IsActive = false; await db.SaveChangesAsync();
        Assert.True(await dispatcher.DispatchOneAsync()); Assert.Equal("cancelled", notification.EmailState); Assert.Equal(1, email.Calls);
    }

    private sealed class TestEmail : IEmailService
    {
        public int Calls { get; private set; }
        public Task<EmailDelivery> SendAsync(string recipient, string subject, string htmlBody, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(new EmailDelivery(false, true, "test-outbox")); }
        public Task<EmailDelivery> SendAsync(EmailMessage message, CancellationToken cancellationToken = default) => SendAsync(message.Recipient, message.Subject, message.HtmlBody, cancellationToken);
    }

    private async Task<HttpClient> User(Guid? organizationId, string role = "Customer", params string[] permissions)
    {
        var email = $"operations-{Guid.NewGuid():N}@example.com"; const string password = "Operations-Password-42!";
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = "Operations user", OrganizationId = organizationId };
            Assert.True((await users.CreateAsync(user, password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
            foreach (var permission in permissions) Assert.True((await users.AddClaimAsync(user, new Claim("permission", permission))).Succeeded);
        }
        var client = fixture.CreateClient(); await ApiFixture.JsonAsync(await ApiFixture.SendAsync(client, HttpMethod.Post, "/api/auth/login", new { email, password })); return client;
    }
    private static async Task<Guid> Organization(HttpClient admin, string name)
    { var result = await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/organizations", new { name, email = "company@example.com", internalNotes = "SECRET ORGANIZATION NOTE" })); return result.GetProperty("id").GetGuid(); }
    private static async Task<Guid> Project(HttpClient admin, Guid organizationId, string name, bool shared)
    { var result = await ApiFixture.JsonAsync(await ApiFixture.SendAsync(admin, HttpMethod.Post, "/api/projects", new { organizationId, name, description = "Project description", budget = 5000m, sharedWithCustomer = shared })); return result.GetProperty("id").GetGuid(); }
    private static async Task<JsonElement> Upload(HttpClient client, Guid organizationId, string fileName, byte[] bytes, bool shared = false, Guid? ticketId = null) => await ApiFixture.JsonAsync(await UploadResponse(client, organizationId, fileName, bytes, shared, ticketId));
    private static async Task<HttpResponseMessage> UploadResponse(HttpClient client, Guid organizationId, string fileName, byte[] bytes, bool shared = false, Guid? ticketId = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf"); using var content = new MultipartFormDataContent(); content.Add(new StringContent(organizationId.ToString()), "organizationId"); content.Add(new StringContent(shared.ToString()), "sharedWithCustomer"); if (ticketId != null) content.Add(new StringContent(ticketId.ToString()!), "ticketId"); content.Add(new ByteArrayContent(bytes), "file", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/documents") { Content = content }; request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString()); return await client.SendAsync(request);
    }
}
