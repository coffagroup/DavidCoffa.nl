using System.Data;
using Coffa.Api.Data;
using Coffa.Api.Domain;
using Coffa.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Coffa.Api.Endpoints;

public static partial class OperationsEndpoints
{
    public sealed record TicketInput(Guid OrganizationId, Guid? ProjectId, string Title, string Description, string? Category, string? Priority, string? Status, string? AssignedToUserId);
    public sealed record CommentInput(string Body, bool IsInternal = false);
    private static IQueryable<Ticket> AccessibleTickets(AppDbContext db, CurrentActor actor)
    {
        ReadAccess(actor, "tickets.read");
        var query = db.Tickets.Where(x => !x.IsArchived && db.Organizations.Any(o => o.Id == x.OrganizationId && !o.IsArchived));
        return actor.IsCustomer ? query.Where(x => x.OrganizationId == actor.OrganizationId) : query;
    }
    private static object TicketDto(Ticket ticket, bool customer) => customer
        ? new { ticket.Id, ticket.OrganizationId, ticket.ProjectId, ticket.Title, ticket.Description, ticket.Category, ticket.Priority, ticket.Status, ticket.CreatedAt, ticket.UpdatedAt }
        : (object)ticket;
    private static object CommentDto(TicketComment comment, bool customer, string? authorName) => customer
        ? new { comment.Id, comment.TicketId, comment.Body, comment.CreatedAt, authorName }
        : (object)new { comment.Id, comment.TicketId, comment.OrganizationId, comment.CreatedByUserId, comment.Body, comment.IsInternal, comment.CreatedAt, authorName };
    private static void MapTickets(RouteGroupBuilder api)
    {
        api.MapGet("/tickets", async (AppDbContext db, CurrentActor actor, HttpRequest request, CancellationToken ct) =>
        {
            var query = AccessibleTickets(db, actor).AsNoTracking();
            if (QueryId(request, "organizationId") is { } orgId) { actor.DemandOrganization(orgId); query = query.Where(x => x.OrganizationId == orgId); }
            if (QueryId(request, "projectId") is { } projectId) query = query.Where(x => x.ProjectId == projectId);
            if (Query(request, "status") is { } status) query = query.Where(x => x.Status == status);
            if (Query(request, "priority") is { } priority) query = query.Where(x => x.Priority == priority);
            if (Query(request, "q") is { } q) query = query.Where(x => EF.Functions.ILike(x.Title, "%" + q + "%") || EF.Functions.ILike(x.Description, "%" + q + "%"));
            var (page, pageSize) = Validation.Page(int.TryParse(Query(request, "page"), out var p) ? p : null, int.TryParse(Query(request, "pageSize"), out var s) ? s : null); page = Math.Min(page, 1000000);
            var total = await query.CountAsync(ct); var items = (await query.OrderByDescending(x => x.UpdatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct)).Select(x => TicketDto(x, actor.IsCustomer));
            return Results.Ok(new { items, total, page, pageSize });
        });
        api.MapGet("/tickets/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        {
            var ticket = await AccessibleTickets(db, actor).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
            var commentRows = await db.TicketComments.AsNoTracking().Where(x => x.TicketId == id && (!actor.IsCustomer || !x.IsInternal)).OrderBy(x => x.CreatedAt).ToListAsync(ct);
            var authorIds = commentRows.Select(x => x.CreatedByUserId).Distinct().ToArray();
            var names = await db.Users.Where(x => authorIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
            var comments = commentRows.Select(x => CommentDto(x, actor.IsCustomer, names.GetValueOrDefault(x.CreatedByUserId)));
            var documents = await AuthorizedDocuments(db, actor, "tickets.read").AsNoTracking().Where(x => x.TicketId == id).Select(x => new { x.Id, x.OriginalName, x.ContentType, x.SizeBytes, x.Category, x.CreatedAt }).ToListAsync(ct);
            return Results.Ok(new { ticket = TicketDto(ticket, actor.IsCustomer), comments, documents });
        });
        api.MapPost("/tickets", async (TicketInput input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            if (!actor.IsCustomer) actor.DemandPermission("tickets.write");
            var orgId = actor.IsCustomer && input.OrganizationId == Guid.Empty ? actor.OrganizationId ?? throw Missing() : input.OrganizationId;
            await OrganizationAccess(db, actor, orgId, "tickets.write", ct); await CheckProject(db, actor, orgId, input.ProjectId, ct);
            if (actor.IsCustomer && (input.AssignedToUserId != null || (input.Status != null && input.Status != "new"))) Forbidden();
            await CheckAssignee(db, input.AssignedToUserId, ct);
            var ticket = new Ticket { OrganizationId = orgId, CreatedByUserId = actor.UserId }; ApplyTicket(ticket, input, actor); db.Tickets.Add(ticket);
            await AddTicketNotifications(db, actor, ticket, $"Nieuw ticket: {ticket.Title}", ct);
            await db.SaveChangesAsync(ct); await audit.RecordAsync("ticket.created", "ticket", ticket.Id.ToString(), organizationId: orgId, cancellationToken: ct);
            return Results.Created($"/api/tickets/{ticket.Id}", TicketDto(ticket, actor.IsCustomer));
        });
        api.MapPut("/tickets/{id:guid}", async (Guid id, TicketInput input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("tickets.write"); var ticket = await db.Tickets.SingleOrDefaultAsync(x => x.Id == id && !x.IsArchived, ct) ?? throw Missing();
            if (input.OrganizationId != ticket.OrganizationId) throw new ApiException(400, "validation", "Een ticket kan niet naar een andere organisatie worden verplaatst.");
            await CheckProject(db, actor, ticket.OrganizationId, input.ProjectId, ct); await CheckAssignee(db, input.AssignedToUserId, ct); ApplyTicket(ticket, input, actor);
            await db.SaveChangesAsync(ct); await audit.RecordAsync("ticket.updated", "ticket", id.ToString(), organizationId: ticket.OrganizationId, cancellationToken: ct); return Results.Ok(TicketDto(ticket, false));
        });
        api.MapDelete("/tickets/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("tickets.write"); var ticket = await db.Tickets.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing(); ticket.IsArchived = true; await db.SaveChangesAsync(ct);
            await audit.RecordAsync("ticket.archived", "ticket", id.ToString(), organizationId: ticket.OrganizationId, cancellationToken: ct); return Results.NoContent();
        });
        api.MapGet("/tickets/{id:guid}/comments", async (Guid id, AppDbContext db, CurrentActor actor, HttpRequest request, CancellationToken ct) =>
        {
            if (!await AccessibleTickets(db, actor).AnyAsync(x => x.Id == id, ct)) throw Missing();
            var query = db.TicketComments.AsNoTracking().Where(x => x.TicketId == id && (!actor.IsCustomer || !x.IsInternal));
            if (actor.IsCustomer) return await Page(query.OrderBy(x => x.CreatedAt).Select(x => new { x.Id, x.TicketId, x.Body, x.CreatedAt, authorName = db.Users.Where(u => u.Id == x.CreatedByUserId).Select(u => u.DisplayName).FirstOrDefault() }), request, ct);
            return await Page(query.OrderBy(x => x.CreatedAt).Select(x => new { x.Id, x.TicketId, x.OrganizationId, x.CreatedByUserId, x.Body, x.IsInternal, x.CreatedAt, authorName = db.Users.Where(u => u.Id == x.CreatedByUserId).Select(u => u.DisplayName).FirstOrDefault() }), request, ct);
        });
        api.MapPost("/tickets/{id:guid}/comments", async (Guid id, CommentInput input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            if (!actor.IsCustomer) actor.DemandPermission("tickets.write");
            if (actor.IsCustomer && input.IsInternal) Forbidden();
            if (!await AccessibleTickets(db, actor).AnyAsync(x => x.Id == id, ct)) throw Missing();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            var ticket = await db.Tickets.FromSqlInterpolated($"SELECT * FROM \"Tickets\" WHERE \"Id\" = {id} FOR UPDATE").SingleAsync(ct);
            if (ticket.IsArchived) throw Missing();
            actor.DemandOrganization(ticket.OrganizationId);
            var comment = new TicketComment { TicketId = id, OrganizationId = ticket.OrganizationId, CreatedByUserId = actor.UserId, Body = Required(input.Body, "een reactie", 20000), IsInternal = input.IsInternal };
            db.TicketComments.Add(comment); ticket.UpdatedAt = DateTimeOffset.UtcNow;
            if (!input.IsInternal)
            {
                if (actor.IsCustomer && (ticket.Status == "waiting_customer" || ticket.Status == "resolved" || ticket.Status == "closed")) ticket.Status = "in_progress";
                else if (!actor.IsCustomer && ticket.Status != "closed") ticket.Status = "waiting_customer";
                await AddTicketNotifications(db, actor, ticket, $"Nieuwe reactie op: {ticket.Title}", ct);
            }
            await db.SaveChangesAsync(ct); await audit.RecordAsync(input.IsInternal ? "ticket.internal_note" : "ticket.reply", "ticket", id.ToString(), organizationId: ticket.OrganizationId, cancellationToken: ct); await transaction.CommitAsync(ct);
            var authorName = await db.Users.Where(x => x.Id == actor.UserId).Select(x => x.DisplayName).SingleAsync(ct);
            return Results.Created($"/api/tickets/{id}/comments", CommentDto(comment, actor.IsCustomer, authorName));
        });
    }
    private static void ApplyTicket(Ticket ticket, TicketInput input, CurrentActor actor)
    {
        ticket.Title = Required(input.Title, "een titel"); ticket.Description = Required(input.Description, "een omschrijving", 20000); ticket.Category = Choice(input.Category, ["general", "website", "hosting", "bug", "change", "billing", "other"], "general"); ticket.Priority = Choice(input.Priority, Priorities, "normal"); ticket.ProjectId = input.ProjectId;
        if (!actor.IsCustomer) { ticket.Status = Choice(input.Status, ["new", "in_progress", "waiting_customer", "resolved", "closed"], "new"); ticket.AssignedToUserId = string.IsNullOrWhiteSpace(input.AssignedToUserId) ? null : input.AssignedToUserId; }
        ticket.UpdatedAt = DateTimeOffset.UtcNow;
    }
    private static async Task AddTicketNotifications(AppDbContext db, CurrentActor actor, Ticket ticket, string message, CancellationToken ct)
    {
        IQueryable<ApplicationUser> users;
        if (actor.IsCustomer)
            users = db.Users.Where(u => u.IsActive && (db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == "SuperAdmin")) || db.UserClaims.Any(c => c.UserId == u.Id && c.ClaimType == "permission" && c.ClaimValue == "tickets.read")));
        else users = db.Users.Where(u => u.IsActive && u.OrganizationId == ticket.OrganizationId && db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == "Customer")));
        var recipients = await users.Where(u => u.Id != actor.UserId).Select(u => new { u.Id, u.Email }).ToListAsync(ct);
        foreach (var recipient in recipients) db.UserNotifications.Add(new UserNotification { UserId = recipient.Id, TicketId = ticket.Id, Message = message, EmailRecipient = recipient.Email, EmailState = "pending" });
    }
}
