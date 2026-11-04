using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Coffa.Api.Data;
using Coffa.Api.Domain;
using Coffa.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Coffa.Api.Endpoints;

public static partial class OperationsEndpoints
{
    public sealed record LeadInput(string? CompanyName, string ContactName, string? Email, string? Phone, string? Website, string? Address, string? Stage, decimal? ExpectedValue, DateOnly? ExpectedCloseDate, string? Notes, string[]? Tags);
    public sealed record LeadActivityInput(string? Kind, string Body, DateOnly? DueDate, bool IsCompleted = false);
    public sealed record PublicContactInput(Guid Id, string Name, string Email, string? Subject, string Message, DateTimeOffset? CreatedAt, string? Source);
    private static object LeadDto(SalesLead lead, CurrentActor actor) => actor.HasPermission("finance.read") ? lead
        : new { lead.Id, lead.CompanyName, lead.ContactName, lead.Email, lead.Phone, lead.Website, lead.Address, lead.Stage, lead.ExpectedCloseDate, lead.Notes, lead.Tags, lead.ConvertedOrganizationId, lead.IsArchived, lead.CreatedAt };
    private static void ApplyLead(SalesLead lead, LeadInput input, CurrentActor actor)
    {
        lead.CompanyName = Text(input.CompanyName, 200); lead.ContactName = Required(input.ContactName, "een contactnaam"); lead.Email = Email(input.Email); lead.Phone = Text(input.Phone, 80); lead.Website = Url(input.Website) ?? ""; lead.Address = Text(input.Address, 1000);
        lead.Stage = Choice(input.Stage, ["new", "contacted", "meeting", "prepare_quote", "quote_sent", "negotiation", "won", "lost"], "new");
        if (input.ExpectedValue is < 0 or > 1000000000m) throw new ApiException(400, "validation", "Gebruik een geldige verwachte opdrachtwaarde.");
        if (input.ExpectedValue != null && !actor.HasPermission("finance.write")) Forbidden();
        if (actor.HasPermission("finance.write")) lead.ExpectedValue = input.ExpectedValue;
        lead.ExpectedCloseDate = input.ExpectedCloseDate; lead.Notes = Text(input.Notes, 20000); lead.Tags = Tags(input.Tags);
    }
    private static void MapLeads(RouteGroupBuilder api)
    {
        api.MapGet("/leads", async (AppDbContext db, CurrentActor actor, HttpRequest request, CancellationToken ct) =>
        {
            actor.DemandPermission("crm.read"); var query = db.SalesLeads.AsNoTracking().Where(x => x.IsArchived == Flag(request, "archived"));
            if (Query(request, "stage") is { } stage) query = query.Where(x => x.Stage == stage);
            if (Query(request, "tag") is { } tag) query = query.Where(x => x.Tags.Contains(tag));
            if (Query(request, "q") is { } q) query = query.Where(x => EF.Functions.ILike(x.CompanyName, "%" + q + "%") || EF.Functions.ILike(x.ContactName, "%" + q + "%") || EF.Functions.ILike(x.Email, "%" + q + "%"));
            var (page, pageSize) = Validation.Page(int.TryParse(Query(request, "page"), out var p) ? p : null, int.TryParse(Query(request, "pageSize"), out var s) ? s : null); page = Math.Min(page, 1000000);
            var total = await query.CountAsync(ct); var items = (await query.OrderByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct)).Select(x => LeadDto(x, actor));
            return Results.Ok(new { items, total, page, pageSize });
        });
        api.MapGet("/leads/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        { actor.DemandPermission("crm.read"); return Results.Ok(LeadDto(await db.SalesLeads.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && !x.IsArchived, ct) ?? throw Missing(), actor)); });
        api.MapPost("/leads", async (LeadInput input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("crm.write"); var lead = new SalesLead(); ApplyLead(lead, input, actor); db.SalesLeads.Add(lead); await db.SaveChangesAsync(ct);
            await audit.RecordAsync("lead.created", "lead", lead.Id.ToString(), cancellationToken: ct); return Results.Created($"/api/leads/{lead.Id}", LeadDto(lead, actor));
        });
        api.MapPut("/leads/{id:guid}", async (Guid id, LeadInput input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("crm.write"); var lead = await db.SalesLeads.SingleOrDefaultAsync(x => x.Id == id && !x.IsArchived, ct) ?? throw Missing(); ApplyLead(lead, input, actor); await db.SaveChangesAsync(ct);
            await audit.RecordAsync("lead.updated", "lead", id.ToString(), cancellationToken: ct); return Results.Ok(LeadDto(lead, actor));
        });
        api.MapDelete("/leads/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        { actor.DemandPermission("crm.write"); var lead = await db.SalesLeads.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing(); lead.IsArchived = true; await db.SaveChangesAsync(ct); return Results.NoContent(); });
        api.MapGet("/leads/{id:guid}/activities", async (Guid id, AppDbContext db, CurrentActor actor, HttpRequest request, CancellationToken ct) =>
        {
            actor.DemandPermission("crm.read"); if (!await db.SalesLeads.AnyAsync(x => x.Id == id && !x.IsArchived, ct)) throw Missing();
            return await Page(db.LeadActivities.AsNoTracking().Where(x => x.LeadId == id).OrderByDescending(x => x.CreatedAt), request, ct);
        });
        api.MapPost("/leads/{id:guid}/activities", async (Guid id, LeadActivityInput input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("crm.write"); if (!await db.SalesLeads.AnyAsync(x => x.Id == id && !x.IsArchived, ct)) throw Missing();
            var activity = new LeadActivity { LeadId = id, UserId = actor.UserId, Kind = Choice(input.Kind, ["note", "call", "meeting", "email", "task"], "note"), Body = Required(input.Body, "een omschrijving", 10000), DueDate = input.DueDate, IsCompleted = input.IsCompleted };
            db.LeadActivities.Add(activity); await db.SaveChangesAsync(ct); await audit.RecordAsync("lead.activity", "lead", id.ToString(), cancellationToken: ct); return Results.Created($"/api/leads/{id}/activities", activity);
        });
        api.MapPut("/leads/{id:guid}/activities/{activityId:guid}", async (Guid id, Guid activityId, LeadActivityInput input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("crm.write");
            if (!await db.SalesLeads.AnyAsync(x => x.Id == id && !x.IsArchived, ct)) throw Missing();
            var activity = await db.LeadActivities.SingleOrDefaultAsync(x => x.Id == activityId && x.LeadId == id, ct) ?? throw Missing();
            activity.Kind = Choice(input.Kind, ["note", "call", "meeting", "email", "task"], "note"); activity.Body = Required(input.Body, "een omschrijving", 10000); activity.DueDate = input.DueDate; activity.IsCompleted = input.IsCompleted;
            await db.SaveChangesAsync(ct); await audit.RecordAsync("lead.activity_updated", "lead", id.ToString(), cancellationToken: ct); return Results.Ok(activity);
        });
        api.MapPost("/leads/{id:guid}/convert", async (Guid id, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("crm.write"); await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            var lead = await db.SalesLeads.FromSqlInterpolated($"SELECT * FROM \"SalesLeads\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct) ?? throw Missing();
            if (lead.IsArchived) throw Missing();
            if (lead.ConvertedOrganizationId is { } existing) { await transaction.CommitAsync(ct); return Results.Ok(new { organizationId = existing }); }
            var org = new Organization { Name = lead.CompanyName.Length > 0 ? lead.CompanyName : lead.ContactName, Email = lead.Email, Phone = lead.Phone, Website = lead.Website, Address = lead.Address, Tags = lead.Tags, InternalNotes = lead.Notes, Type = "customer", Status = "active" };
            db.Organizations.Add(org);
            db.ContactPeople.Add(new ContactPerson { OrganizationId = org.Id, Name = lead.ContactName, Email = lead.Email, Phone = lead.Phone, IsPrimary = true });
            var activities = await db.LeadActivities.Where(x => x.LeadId == id).OrderBy(x => x.CreatedAt).ToListAsync(ct);
            foreach (var activity in activities)
                db.OrganizationNotes.Add(new OrganizationNote { OrganizationId = org.Id, Title = $"Lead: {activity.Kind}", Body = activity.Body, CreatedByUserId = activity.UserId, CreatedAt = activity.CreatedAt });
            lead.ConvertedOrganizationId = org.Id; lead.Stage = "won";
            await db.SaveChangesAsync(ct); await audit.RecordAsync("lead.converted", "lead", id.ToString(), organizationId: org.Id, cancellationToken: ct); await transaction.CommitAsync(ct);
            return Results.Ok(new { organizationId = org.Id });
        });
    }
    private static void MapPublicContact(WebApplication app)
    {
        app.MapPost("/api/public/contact", async (HttpContext context, AppDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            var expected = config["Contact:ApiKey"];
            if (string.IsNullOrWhiteSpace(expected) || expected.Length < 32) throw new ApiException(503, "contact_unconfigured", "De contactkoppeling is nog niet geconfigureerd.");
            var supplied = context.Request.Headers["X-Coffa-Contact-Key"].ToString();
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied))) throw new ApiException(401, "invalid_service_key", "Ongeldige serviceverbinding.");
            if (context.Request.ContentLength > 24576) throw new ApiException(413, "request_too_large", "Dit bericht is te groot.");
            if (!context.Request.HasJsonContentType()) throw new ApiException(415, "json_required", "Verstuur de aanvraag als JSON.");
            using var body = new MemoryStream();
            var buffer = new byte[4096];
            int read;
            while ((read = await context.Request.Body.ReadAsync(buffer, ct)) > 0)
            {
                if (body.Length + read > 24576) throw new ApiException(413, "request_too_large", "Dit bericht is te groot.");
                await body.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            PublicContactInput input;
            try { input = JsonSerializer.Deserialize<PublicContactInput>(body.ToArray(), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new JsonException(); }
            catch (JsonException) { throw new ApiException(400, "invalid_json", "De contactaanvraag kon niet worden gelezen."); }
            if (input.Id == Guid.Empty || input.Source != "website") throw new ApiException(400, "validation", "Ongeldige contactaanvraag.");
            var name = Required(input.Name, "een naam", 120); var email = Email(input.Email, true); var subject = Text(input.Subject, 160); var message = Required(input.Message, "een vraag", 4000);
            if (message.Length < 10 || email.Length > 160 || name.Any(char.IsControl) || subject.Any(char.IsControl)) throw new ApiException(400, "validation", "Controleer de contactgegevens.");
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { name, email, subject, message }))));
            var receipt = await db.PublicContactReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.Id, ct);
            if (receipt != null)
            {
                if (receipt.ContentHash != fingerprint) throw new ApiException(409, "submission_conflict", "Deze aanvraag is al met andere inhoud ontvangen.");
                return Results.Ok(new { ok = true, leadId = receipt.LeadId });
            }
            var lead = new SalesLead { Id = input.Id, ContactName = name, Email = email, Notes = string.IsNullOrEmpty(subject) ? message : $"Onderwerp: {subject}\n\n{message}", Stage = "new" };
            db.SalesLeads.Add(lead); db.PublicContactReceipts.Add(new PublicContactReceipt { Id = input.Id, LeadId = lead.Id, ContentHash = fingerprint });
            db.AuditEntries.Add(new AuditEntry { Action = "lead.received", EntityType = "lead", EntityId = lead.Id.ToString(), Details = "website" });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear(); receipt = await db.PublicContactReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.Id, ct);
                if (receipt == null) throw;
                if (receipt.ContentHash != fingerprint) throw new ApiException(409, "submission_conflict", "Deze aanvraag is al met andere inhoud ontvangen.");
                return Results.Ok(new { ok = true, leadId = receipt.LeadId });
            }
            return Results.Ok(new { ok = true, leadId = lead.Id });
        }).AllowAnonymous().WithMetadata(new PublicServerEndpoint()).RequireRateLimiting("public-contact").WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(24576));
    }
}
