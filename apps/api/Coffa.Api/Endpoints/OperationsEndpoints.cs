using System.Net.Mail;
using Coffa.Api.Data;
using Coffa.Api.Domain;
using Coffa.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Coffa.Api.Endpoints;

public static partial class OperationsEndpoints
{
    public static void AddOperations(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IObjectStorage, ConfiguredObjectStorage>();
        builder.Services.AddScoped<TicketNotificationDispatcher>();
        builder.Services.AddHostedService<TicketNotificationWorker>();
    }
    public static void MapOperations(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();
        MapCrm(api); MapLeads(api); MapProjects(api); MapTime(api); MapTickets(api); MapDocuments(api); MapPublicContact(app);
        api.MapGet("/organization-options", async (AppDbContext db, CurrentActor actor, HttpRequest request, CancellationToken ct) =>
        {
            if (!actor.IsCustomer && !new[] { "crm.read", "crm.write", "finance.read", "finance.write", "projects.read", "projects.write", "tickets.read", "tickets.write", "documents.read", "documents.write", "time.read", "time.write" }.Any(actor.HasPermission)) Forbidden();
            var query = db.Organizations.AsNoTracking().Where(x => !x.IsArchived);
            if (actor.IsCustomer) query = query.Where(x => x.Id == actor.OrganizationId);
            if (Query(request, "q") is { } q) query = query.Where(x => EF.Functions.ILike(x.Name, "%" + q + "%"));
            return await Page(query.OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }), request, ct);
        });
        api.MapGet("/assignees", async (AppDbContext db, CurrentActor actor, HttpRequest request, CancellationToken ct) =>
        {
            if (!actor.HasPermission("projects.read") && !actor.HasPermission("tickets.read")) Forbidden();
            var staffIds = db.UserRoles.Where(x => db.Roles.Any(r => r.Id == x.RoleId && (r.Name == "Employee" || r.Name == "SuperAdmin"))).Select(x => x.UserId);
            var query = db.Users.AsNoTracking().Where(x => x.IsActive && staffIds.Contains(x.Id));
            return await Page(query.OrderBy(x => x.DisplayName).Select(x => new { x.Id, name = x.DisplayName, x.Email }), request, ct);
        });
        api.MapGet("/notifications", async (AppDbContext db, CurrentActor actor, HttpRequest request, CancellationToken ct) =>
        {
            ReadAccess(actor, "tickets.read");
            var query = db.UserNotifications.AsNoTracking().Where(x => x.UserId == actor.UserId);
            if (actor.IsCustomer) query = query.Where(x => db.Tickets.Any(t => t.Id == x.TicketId && t.OrganizationId == actor.OrganizationId && !t.IsArchived));
            return await Page(query.OrderByDescending(x => x.CreatedAt).Select(x => new { x.Id, x.TicketId, x.Message, x.CreatedAt, x.ReadAt }), request, ct);
        });
        api.MapPost("/notifications/{id:guid}/read", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        {
            ReadAccess(actor, "tickets.read");
            var notification = await db.UserNotifications.SingleOrDefaultAsync(x => x.Id == id && x.UserId == actor.UserId, ct) ?? throw Missing();
            if (actor.IsCustomer && !await db.Tickets.AnyAsync(t => t.Id == notification.TicketId && t.OrganizationId == actor.OrganizationId && !t.IsArchived, ct)) throw Missing();
            notification.ReadAt ??= DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { notification.Id, notification.ReadAt });
        });
    }
    private static ApiException Missing() => new(404, "not_found", "Dit onderdeel is niet gevonden.");
    private static void Forbidden() => throw new ApiException(403, "forbidden", "Je hebt geen toegang tot deze functie.");
    private static void ReadAccess(CurrentActor actor, string permission) { if (!actor.IsCustomer) actor.DemandPermission(permission); else if (actor.OrganizationId == null) Forbidden(); }
    private static string Required(string? value, string name, int max = 200)
    {
        var text = Text(value, max);
        if (text.Length == 0) throw new ApiException(400, "validation", $"Vul {name} in.");
        return text;
    }
    private static string Text(string? value, int max = 4000)
    {
        var text = value?.Trim() ?? "";
        if (text.Length > max || text.Contains('\0')) throw new ApiException(400, "validation", $"Gebruik maximaal {max} tekens.");
        return text;
    }
    private static string Choice(string? value, string[] options, string fallback)
    {
        var result = string.IsNullOrWhiteSpace(value) ? fallback : value;
        if (!options.Contains(result)) throw new ApiException(400, "validation", "Kies een geldige status of categorie.");
        return result;
    }
    private static string Email(string? input, bool required = false)
    {
        var email = Text(input, 254);
        if (email.Length == 0 && !required) return "";
        if (!MailAddress.TryCreate(email, out var address) || address.Address != email || !email.Contains('.')) throw new ApiException(400, "validation", "Vul een geldig e-mailadres in.");
        return email;
    }
    private static string? Url(string? input)
    {
        var value = Text(input, 2000);
        if (value.Length == 0) return null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http") || !string.IsNullOrEmpty(uri.UserInfo)) throw new ApiException(400, "validation", "Gebruik een geldig HTTP- of HTTPS-adres zonder inloggegevens.");
        return value;
    }
    private static string[] Tags(string[]? tags)
    {
        if (tags?.Length > 30) throw new ApiException(400, "validation", "Gebruik maximaal 30 labels.");
        return (tags ?? []).Select(x => Required(x, "een label", 50)).Distinct().ToArray();
    }
    private static readonly string[] Priorities = ["low", "normal", "high", "urgent"];
    private static string? Query(HttpRequest request, string key) => request.Query[key].FirstOrDefault() is { Length: > 0 } value ? Text(value, 500) : null;
    private static Guid? QueryId(HttpRequest request, string key)
    {
        var value = Query(request, key);
        if (value == null) return null;
        return Guid.TryParse(value, out var id) ? id : throw new ApiException(400, "validation", "Ongeldige referentie.");
    }
    private static bool Flag(HttpRequest request, string key) => bool.TryParse(Query(request, key), out var value) && value;
    private static async Task<IResult> Page<T>(IQueryable<T> query, HttpRequest request, CancellationToken ct)
    {
        var page = int.TryParse(Query(request, "page"), out var p) ? Math.Max(1, p) : 1;
        var pageSize = int.TryParse(Query(request, "pageSize"), out var size) ? Math.Clamp(size, 1, 100) : 25;
        page = Math.Min(page, 1000000);
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Results.Ok(new { items, total, page, pageSize });
    }
    private static async Task<Organization> OrganizationAccess(AppDbContext db, CurrentActor actor, Guid id, string permission, CancellationToken ct)
    {
        ReadAccess(actor, permission);
        actor.DemandOrganization(id);
        return await db.Organizations.SingleOrDefaultAsync(x => x.Id == id && !x.IsArchived, ct) ?? throw Missing();
    }
    private static async Task CheckProject(AppDbContext db, CurrentActor actor, Guid organizationId, Guid? projectId, CancellationToken ct)
    {
        if (projectId == null) return;
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == projectId && x.OrganizationId == organizationId && !x.IsArchived, ct) ?? throw Missing();
        if (actor.IsCustomer && !project.SharedWithCustomer) throw Missing();
    }
    private static async Task CheckAssignee(AppDbContext db, string? userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;
        if (userId.Length > 450 || userId.Any(char.IsControl)) throw new ApiException(400, "validation", "Kies een geldige medewerker.");
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct) ?? throw new ApiException(400, "validation", "Kies een actieve medewerker.");
        var staff = await db.UserRoles.AnyAsync(x => x.UserId == user.Id && db.Roles.Any(r => r.Id == x.RoleId && (r.Name == "Employee" || r.Name == "SuperAdmin")), ct);
        if (!staff) throw new ApiException(400, "validation", "Kies een medewerker als verantwoordelijke.");
    }
}
