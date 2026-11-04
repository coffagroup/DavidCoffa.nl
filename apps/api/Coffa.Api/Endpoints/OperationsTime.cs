using System.Data;
using Coffa.Api.Data;
using Coffa.Api.Domain;
using Coffa.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Coffa.Api.Endpoints;

public static partial class OperationsEndpoints
{
    public sealed record TimeInput(Guid OrganizationId, Guid? ProjectId, Guid? TicketId, string Description, DateOnly WorkDate, int DurationMinutes, bool IsBillable, decimal HourlyRate = 0);
    public sealed record TimerInput(Guid OrganizationId, Guid? ProjectId, Guid? TicketId, string Description, bool IsBillable, decimal HourlyRate = 0);
    private static object TimeDto(TimeEntry entry, bool finance) => finance
        ? new { entry.Id, entry.OrganizationId, entry.ProjectId, entry.TicketId, entry.InvoiceId, entry.UserId, entry.Description, entry.WorkDate, entry.DurationMinutes, entry.IsBillable, entry.HourlyRate, value = decimal.Round(entry.DurationMinutes / 60m * entry.HourlyRate, 2), entry.CreatedAt }
        : new { entry.Id, entry.OrganizationId, entry.ProjectId, entry.TicketId, entry.InvoiceId, entry.UserId, entry.Description, entry.WorkDate, entry.DurationMinutes, entry.IsBillable, entry.CreatedAt };
    private static async Task CheckTimeRelations(AppDbContext db, CurrentActor actor, Guid orgId, Guid? projectId, Guid? ticketId, CancellationToken ct)
    {
        await OrganizationAccess(db, actor, orgId, "time.write", ct); await CheckProject(db, actor, orgId, projectId, ct);
        if (ticketId != null && !await db.Tickets.AnyAsync(x => x.Id == ticketId && x.OrganizationId == orgId && !x.IsArchived && (projectId == null || x.ProjectId == projectId), ct)) throw Missing();
    }
    private static void CheckRate(decimal rate, CurrentActor actor)
    {
        if (rate is < 0 or > 100000m) throw new ApiException(400, "validation", "Gebruik een geldig uurtarief.");
        if (rate != 0 && !actor.HasPermission("finance.write")) Forbidden();
    }
    private static void MapTime(RouteGroupBuilder api)
    {
        api.MapGet("/time-entries", async (AppDbContext db, CurrentActor actor, HttpRequest request, CancellationToken ct) =>
        {
            actor.DemandPermission("time.read"); var query = db.TimeEntries.AsNoTracking().AsQueryable();
            if (QueryId(request, "organizationId") is { } orgId) query = query.Where(x => x.OrganizationId == orgId);
            if (QueryId(request, "projectId") is { } projectId) query = query.Where(x => x.ProjectId == projectId);
            if (QueryId(request, "ticketId") is { } ticketId) query = query.Where(x => x.TicketId == ticketId);
            if (Query(request, "from") is { } from && DateOnly.TryParse(from, out var fromDate)) query = query.Where(x => x.WorkDate >= fromDate);
            if (Query(request, "to") is { } to && DateOnly.TryParse(to, out var toDate)) query = query.Where(x => x.WorkDate <= toDate);
            if (Query(request, "q") is { } q) query = query.Where(x => EF.Functions.ILike(x.Description, "%" + q + "%"));
            if (Flag(request, "unbilled")) query = query.Where(x => x.InvoiceId == null && x.IsBillable);
            if (Flag(request, "mine")) query = query.Where(x => x.UserId == actor.UserId);
            var (page, pageSize) = Validation.Page(int.TryParse(Query(request, "page"), out var p) ? p : null, int.TryParse(Query(request, "pageSize"), out var s) ? s : null); page = Math.Min(page, 1000000);
            var total = await query.CountAsync(ct); var items = (await query.OrderByDescending(x => x.WorkDate).ThenByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct)).Select(x => TimeDto(x, actor.HasPermission("finance.read")));
            var totalMinutes = await query.SumAsync(x => (long?)x.DurationMinutes, ct) ?? 0;
            var billableMinutes = await query.Where(x => x.IsBillable).SumAsync(x => (long?)x.DurationMinutes, ct) ?? 0;
            object summary = new { totalHours = totalMinutes / 60m, billableHours = billableMinutes / 60m };
            if (actor.HasPermission("finance.read"))
            {
                var billableValue = await query.Where(x => x.IsBillable).SumAsync(x => (decimal?)(x.DurationMinutes / 60m * x.HourlyRate), ct) ?? 0;
                summary = new { totalHours = totalMinutes / 60m, billableHours = billableMinutes / 60m, billableValue = decimal.Round(billableValue, 2), effectiveHourlyRate = totalMinutes > 0 ? decimal.Round(billableValue / (totalMinutes / 60m), 2) : 0 };
            }
            return Results.Ok(new { items, total, page, pageSize, summary });
        });
        api.MapPost("/time-entries", async (TimeInput input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("time.write"); await CheckTimeRelations(db, actor, input.OrganizationId, input.ProjectId, input.TicketId, ct); CheckRate(input.HourlyRate, actor);
            var entry = new TimeEntry { UserId = actor.UserId }; ApplyTime(entry, input, actor); db.TimeEntries.Add(entry); await db.SaveChangesAsync(ct);
            await audit.RecordAsync("time.created", "time_entry", entry.Id.ToString(), organizationId: entry.OrganizationId, cancellationToken: ct); return Results.Created($"/api/time-entries/{entry.Id}", TimeDto(entry, actor.HasPermission("finance.read")));
        });
        api.MapGet("/time-entries/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        { actor.DemandPermission("time.read"); return Results.Ok(TimeDto(await db.TimeEntries.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing(), actor.HasPermission("finance.read"))); });
        api.MapPut("/time-entries/{id:guid}", async (Guid id, TimeInput input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("time.write"); await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext('coffa:time:invoice'))", ct);
            var entry = await db.TimeEntries.FromSqlInterpolated($"SELECT * FROM \"TimeEntries\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct) ?? throw Missing();
            if (entry.InvoiceId != null) throw new ApiException(409, "time_invoiced", "Gefactureerde uren kunnen niet meer worden gewijzigd.");
            if (input.OrganizationId != entry.OrganizationId) throw new ApiException(400, "validation", "Een urenregistratie kan niet naar een andere organisatie worden verplaatst.");
            await CheckTimeRelations(db, actor, input.OrganizationId, input.ProjectId, input.TicketId, ct);
            CheckRate(input.HourlyRate, actor);
            ApplyTime(entry, input, actor); await db.SaveChangesAsync(ct); await audit.RecordAsync("time.updated", "time_entry", id.ToString(), organizationId: entry.OrganizationId, cancellationToken: ct); await transaction.CommitAsync(ct); return Results.Ok(TimeDto(entry, actor.HasPermission("finance.read")));
        });
        api.MapDelete("/time-entries/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("time.write"); await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext('coffa:time:invoice'))", ct);
            var entry = await db.TimeEntries.FromSqlInterpolated($"SELECT * FROM \"TimeEntries\" WHERE \"Id\" = {id} FOR UPDATE").SingleOrDefaultAsync(ct) ?? throw Missing();
            if (entry.InvoiceId != null) throw new ApiException(409, "time_invoiced", "Gefactureerde uren kunnen niet worden verwijderd.");
            db.TimeEntries.Remove(entry); await db.SaveChangesAsync(ct); await audit.RecordAsync("time.deleted", "time_entry", id.ToString(), organizationId: entry.OrganizationId, cancellationToken: ct); await transaction.CommitAsync(ct); return Results.NoContent();
        });
        api.MapGet("/time-entries/timer", async (AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        {
            actor.DemandPermission("time.read"); var timer = await db.ActiveTimers.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == actor.UserId, ct);
            return Results.Ok(timer == null ? null : new { timer.Id, timer.OrganizationId, timer.ProjectId, timer.TicketId, timer.Description, timer.IsBillable, timer.StartedAt, hourlyRate = actor.HasPermission("finance.read") ? (decimal?)timer.HourlyRate : null });
        });
        api.MapPost("/time-entries/timer/start", async (TimerInput input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("time.write"); await CheckTimeRelations(db, actor, input.OrganizationId, input.ProjectId, input.TicketId, ct); CheckRate(input.HourlyRate, actor);
            if (await db.ActiveTimers.AnyAsync(x => x.UserId == actor.UserId, ct)) throw new ApiException(409, "timer_running", "Stop eerst je lopende timer.");
            var timer = new ActiveTimer { UserId = actor.UserId, OrganizationId = input.OrganizationId, ProjectId = input.ProjectId, TicketId = input.TicketId, Description = Required(input.Description, "een omschrijving", 4000), IsBillable = input.IsBillable, HourlyRate = input.HourlyRate };
            db.ActiveTimers.Add(timer); try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { throw new ApiException(409, "timer_running", "Er loopt al een timer voor je account."); }
            await audit.RecordAsync("timer.started", "timer", timer.Id.ToString(), organizationId: timer.OrganizationId, cancellationToken: ct); return Results.Ok(new { timer.Id, timer.StartedAt });
        });
        api.MapPost("/time-entries/timer/stop", async (AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("time.write"); await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            var userId = actor.UserId; var timer = await db.ActiveTimers.FromSqlInterpolated($"SELECT * FROM \"ActiveTimers\" WHERE \"UserId\" = {userId} FOR UPDATE").SingleOrDefaultAsync(ct) ?? throw new ApiException(409, "no_timer", "Je hebt geen lopende timer.");
            var elapsed = Math.Max(1, (int)Math.Ceiling((DateTimeOffset.UtcNow - timer.StartedAt).TotalMinutes));
            var entry = new TimeEntry { OrganizationId = timer.OrganizationId, ProjectId = timer.ProjectId, TicketId = timer.TicketId, UserId = actor.UserId, Description = timer.Description, WorkDate = DateOnly.FromDateTime(timer.StartedAt.UtcDateTime), DurationMinutes = elapsed, IsBillable = timer.IsBillable, HourlyRate = timer.HourlyRate };
            db.TimeEntries.Add(entry); db.ActiveTimers.Remove(timer); await db.SaveChangesAsync(ct); await audit.RecordAsync("timer.stopped", "time_entry", entry.Id.ToString(), organizationId: entry.OrganizationId, cancellationToken: ct); await transaction.CommitAsync(ct);
            return Results.Ok(TimeDto(entry, actor.HasPermission("finance.read")));
        });
    }
    private static void ApplyTime(TimeEntry entry, TimeInput input, CurrentActor actor)
    {
        if (input.DurationMinutes is < 1 or > 525600 || input.WorkDate == default || input.WorkDate > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))) throw new ApiException(400, "validation", "Controleer de datum en het aantal minuten.");
        entry.OrganizationId = input.OrganizationId; entry.ProjectId = input.ProjectId; entry.TicketId = input.TicketId; entry.Description = Required(input.Description, "een omschrijving", 4000); entry.WorkDate = input.WorkDate; entry.DurationMinutes = input.DurationMinutes; entry.IsBillable = input.IsBillable;
        if (actor.HasPermission("finance.write")) entry.HourlyRate = input.HourlyRate;
    }
}
