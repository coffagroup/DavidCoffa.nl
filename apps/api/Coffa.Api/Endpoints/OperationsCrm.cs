using Coffa.Api.Data;
using Coffa.Api.Domain;
using Coffa.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Coffa.Api.Endpoints;

public static partial class OperationsEndpoints
{
    public sealed record OrganizationInput(string Name, string? Email, string? Phone, string? Website, string? Address, string? ChamberOfCommerce, string? VatNumber, string? Type, string? Status, string[]? Tags, string? InternalNotes);
    public sealed record ContactInput(Guid OrganizationId, string Name, string? Email, string? Phone, string? JobTitle, bool IsPrimary = false);
    public sealed record NoteInput(Guid OrganizationId, string Title, string Body);
    private static object OrganizationDto(Organization org, bool customer) => customer
        ? new { org.Id, org.Name, org.Email, org.Phone, org.Website, org.Address, org.ChamberOfCommerce, org.VatNumber, org.CreatedAt }
        : (object)org;
    private static void ApplyOrganization(Organization org, OrganizationInput input)
    {
        org.Name = Required(input.Name, "een bedrijfsnaam"); org.Email = Email(input.Email); org.Phone = Text(input.Phone, 80);
        org.Website = Url(input.Website) ?? ""; org.Address = Text(input.Address, 1000); org.ChamberOfCommerce = Text(input.ChamberOfCommerce, 50); org.VatNumber = Text(input.VatNumber, 50);
        org.Type = Text(input.Type, 50) is { Length: > 0 } type ? type : "customer";
        org.Status = Choice(input.Status, ["active", "inactive", "prospect"], "active"); org.Tags = Tags(input.Tags); org.InternalNotes = Text(input.InternalNotes, 20000);
    }
    private static void MapCrm(RouteGroupBuilder api)
    {
        api.MapGet("/organizations", async (AppDbContext db, CurrentActor actor, HttpRequest request, CancellationToken ct) =>
        {
            ReadAccess(actor, "crm.read");
            var query = db.Organizations.AsNoTracking().Where(x => x.IsArchived == Flag(request, "archived"));
            if (actor.IsCustomer) query = query.Where(x => x.Id == actor.OrganizationId);
            if (Query(request, "q") is { } q) query = query.Where(x => EF.Functions.ILike(x.Name, "%" + q + "%") || EF.Functions.ILike(x.Email, "%" + q + "%"));
            if (Query(request, "status") is { } status) query = query.Where(x => x.Status == status);
            if (Query(request, "type") is { } type) query = query.Where(x => x.Type == type);
            if (Query(request, "tag") is { } tag) query = query.Where(x => x.Tags.Contains(tag));
            if (actor.IsCustomer) return await Page(query.OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.Email, x.Phone, x.Website, x.Address, x.ChamberOfCommerce, x.VatNumber, x.CreatedAt }), request, ct);
            return await Page(query.OrderBy(x => x.Name), request, ct);
        });
        api.MapGet("/organizations/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) => Results.Ok(OrganizationDto(await OrganizationAccess(db, actor, id, "crm.read", ct), actor.IsCustomer)));
        api.MapPost("/organizations", async (OrganizationInput input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("crm.write"); var org = new Organization(); ApplyOrganization(org, input); db.Organizations.Add(org); await db.SaveChangesAsync(ct);
            await audit.RecordAsync("organization.created", "organization", org.Id.ToString(), organizationId: org.Id, cancellationToken: ct);
            return Results.Created($"/api/organizations/{org.Id}", org);
        });
        api.MapPut("/organizations/{id:guid}", async (Guid id, OrganizationInput input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("crm.write"); var org = await db.Organizations.SingleOrDefaultAsync(x => x.Id == id && !x.IsArchived, ct) ?? throw Missing(); ApplyOrganization(org, input); await db.SaveChangesAsync(ct);
            await audit.RecordAsync("organization.updated", "organization", id.ToString(), organizationId: id, cancellationToken: ct); return Results.Ok(org);
        });
        api.MapDelete("/organizations/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("crm.write"); var org = await db.Organizations.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing(); org.IsArchived = true; await db.SaveChangesAsync(ct);
            await audit.RecordAsync("organization.archived", "organization", id.ToString(), organizationId: id, cancellationToken: ct); return Results.NoContent();
        });
        api.MapGet("/organizations/{id:guid}/dossier", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        {
            var org = await OrganizationAccess(db, actor, id, "crm.read", ct);
            var contacts = await db.ContactPeople.AsNoTracking().Where(x => x.OrganizationId == id && !x.IsArchived).OrderByDescending(x => x.IsPrimary).ThenBy(x => x.Name).ToListAsync(ct);
            if (actor.IsCustomer) return Results.Ok(new { organization = OrganizationDto(org, true), contacts });
            var notes = await db.OrganizationNotes.AsNoTracking().Where(x => x.OrganizationId == id && !x.IsArchived).OrderByDescending(x => x.CreatedAt).Take(50).ToListAsync(ct);
            return Results.Ok(new { organization = OrganizationDto(org, false), contacts, notes });
        });
        api.MapGet("/contacts", async (AppDbContext db, CurrentActor actor, HttpRequest request, CancellationToken ct) =>
        {
            ReadAccess(actor, "crm.read"); var query = db.ContactPeople.AsNoTracking().Where(x => !x.IsArchived && db.Organizations.Any(o => o.Id == x.OrganizationId && !o.IsArchived));
            if (actor.IsCustomer) query = query.Where(x => x.OrganizationId == actor.OrganizationId);
            if (QueryId(request, "organizationId") is { } orgId) { actor.DemandOrganization(orgId); query = query.Where(x => x.OrganizationId == orgId); }
            if (Query(request, "q") is { } q) query = query.Where(x => EF.Functions.ILike(x.Name, "%" + q + "%") || EF.Functions.ILike(x.Email, "%" + q + "%"));
            return await Page(query.OrderBy(x => x.Name), request, ct);
        });
        api.MapPost("/contacts", async (ContactInput input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("crm.write"); await OrganizationAccess(db, actor, input.OrganizationId, "crm.write", ct);
            var contact = new ContactPerson(); ApplyContact(contact, input); db.ContactPeople.Add(contact); await db.SaveChangesAsync(ct);
            await audit.RecordAsync("contact.created", "contact", contact.Id.ToString(), organizationId: contact.OrganizationId, cancellationToken: ct); return Results.Created($"/api/contacts/{contact.Id}", contact);
        });
        api.MapGet("/contacts/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        {
            ReadAccess(actor, "crm.read"); var contact = await db.ContactPeople.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && !x.IsArchived, ct) ?? throw Missing();
            await OrganizationAccess(db, actor, contact.OrganizationId, "crm.read", ct); return Results.Ok(contact);
        });
        api.MapPut("/contacts/{id:guid}", async (Guid id, ContactInput input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("crm.write"); var contact = await db.ContactPeople.SingleOrDefaultAsync(x => x.Id == id && !x.IsArchived, ct) ?? throw Missing();
            if (input.OrganizationId != contact.OrganizationId) throw new ApiException(400, "validation", "Een contactpersoon kan niet naar een andere organisatie worden verplaatst.");
            ApplyContact(contact, input); await db.SaveChangesAsync(ct); await audit.RecordAsync("contact.updated", "contact", id.ToString(), organizationId: contact.OrganizationId, cancellationToken: ct); return Results.Ok(contact);
        });
        api.MapDelete("/contacts/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        { actor.DemandPermission("crm.write"); var contact = await db.ContactPeople.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing(); contact.IsArchived = true; await db.SaveChangesAsync(ct); return Results.NoContent(); });
        api.MapGet("/notes", async (AppDbContext db, CurrentActor actor, HttpRequest request, CancellationToken ct) =>
        {
            actor.DemandPermission("crm.read"); var query = db.OrganizationNotes.AsNoTracking().Where(x => !x.IsArchived);
            if (QueryId(request, "organizationId") is { } orgId) query = query.Where(x => x.OrganizationId == orgId);
            if (Query(request, "q") is { } q) query = query.Where(x => EF.Functions.ILike(x.Title, "%" + q + "%") || EF.Functions.ILike(x.Body, "%" + q + "%"));
            return await Page(query.OrderByDescending(x => x.CreatedAt), request, ct);
        });
        api.MapPost("/notes", async (NoteInput input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("crm.write"); await OrganizationAccess(db, actor, input.OrganizationId, "crm.write", ct);
            var note = new OrganizationNote { OrganizationId = input.OrganizationId, Title = Required(input.Title, "een titel"), Body = Required(input.Body, "een notitie", 20000), CreatedByUserId = actor.UserId };
            db.OrganizationNotes.Add(note); await db.SaveChangesAsync(ct); await audit.RecordAsync("note.created", "note", note.Id.ToString(), organizationId: note.OrganizationId, cancellationToken: ct); return Results.Created($"/api/notes/{note.Id}", note);
        });
        api.MapGet("/notes/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        { actor.DemandPermission("crm.read"); return Results.Ok(await db.OrganizationNotes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && !x.IsArchived, ct) ?? throw Missing()); });
        api.MapPut("/notes/{id:guid}", async (Guid id, NoteInput input, AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        {
            actor.DemandPermission("crm.write"); var note = await db.OrganizationNotes.SingleOrDefaultAsync(x => x.Id == id && !x.IsArchived, ct) ?? throw Missing();
            if (input.OrganizationId != note.OrganizationId) throw new ApiException(400, "validation", "Een notitie kan niet naar een andere organisatie worden verplaatst.");
            note.Title = Required(input.Title, "een titel"); note.Body = Required(input.Body, "een notitie", 20000); await db.SaveChangesAsync(ct); return Results.Ok(note);
        });
        api.MapDelete("/notes/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        { actor.DemandPermission("crm.write"); var note = await db.OrganizationNotes.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing(); note.IsArchived = true; await db.SaveChangesAsync(ct); return Results.NoContent(); });
    }
    private static void ApplyContact(ContactPerson contact, ContactInput input)
    {
        contact.OrganizationId = input.OrganizationId; contact.Name = Required(input.Name, "een naam"); contact.Email = Email(input.Email); contact.Phone = Text(input.Phone, 80); contact.JobTitle = Text(input.JobTitle, 150); contact.IsPrimary = input.IsPrimary;
    }
}
