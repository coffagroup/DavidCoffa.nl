using System.Net;
using Coffa.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Coffa.Api.Infrastructure;

/// <summary>Durable outbox for public ticket updates; never changes a saved reply's outcome.</summary>
public sealed class TicketNotificationDispatcher(AppDbContext db, IEmailService email, IConfiguration configuration, ILogger<TicketNotificationDispatcher> logger)
{
    public async Task<bool> DispatchOneAsync(CancellationToken ct = default)
    {
        if (!configuration.GetValue<bool>("Tickets:EmailNotificationsEnabled")) return false;
        var now = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var notification = await db.UserNotifications.FromSqlInterpolated($"SELECT * FROM \"UserNotifications\" WHERE \"EmailRecipient\" IS NOT NULL AND ((\"EmailState\" IN ('pending','failed') AND (\"EmailRetryAt\" IS NULL OR \"EmailRetryAt\" <= {now})) OR (\"EmailState\" = 'processing' AND \"LeaseUntil\" < {now})) ORDER BY \"CreatedAt\" LIMIT 1 FOR UPDATE SKIP LOCKED").SingleOrDefaultAsync(ct);
        if (notification == null) { await transaction.CommitAsync(ct); return false; }
        notification.EmailState = "processing"; notification.EmailAttempts++; notification.LeaseUntil = now.AddMinutes(5);
        await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        try
        {
            // A revoked or reassigned account must not receive a queued tenant notification.
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == notification.UserId && x.IsActive && x.Email == notification.EmailRecipient, ct);
            var ticket = notification.TicketId == null ? null : await db.Tickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == notification.TicketId && !x.IsArchived, ct);
            if (user == null || ticket == null || !await db.Organizations.AnyAsync(x => x.Id == ticket.OrganizationId && !x.IsArchived, ct))
            { notification.EmailState = "cancelled"; notification.LeaseUntil = null; await db.SaveChangesAsync(ct); return true; }
            var customer = await db.UserRoles.AnyAsync(ur => ur.UserId == user.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == "Customer"), ct);
            if (customer && user.OrganizationId != ticket.OrganizationId)
            { notification.EmailState = "cancelled"; notification.LeaseUntil = null; await db.SaveChangesAsync(ct); return true; }
            if (!customer)
            {
                var authorized = await db.UserRoles.AnyAsync(ur => ur.UserId == user.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == "SuperAdmin"), ct)
                    || await db.UserClaims.AnyAsync(x => x.UserId == user.Id && x.ClaimType == "permission" && x.ClaimValue == "tickets.read", ct);
                if (!authorized) { notification.EmailState = "cancelled"; notification.LeaseUntil = null; await db.SaveChangesAsync(ct); return true; }
            }
            var delivery = await email.SendAsync(notification.EmailRecipient!, "Update over je supportticket", $"<p>{WebUtility.HtmlEncode(notification.Message)}</p><p>Log in op Coffa OS om het ticket te bekijken en te reageren.</p>", ct);
            notification.EmailState = delivery.Sent ? "sent" : delivery.DevelopmentOutbox ? "development_outbox" : "failed";
            notification.EmailSentAt = delivery.Sent ? DateTimeOffset.UtcNow : null; notification.EmailLastError = delivery.Sent || delivery.DevelopmentOutbox ? null : "not_sent"; notification.LeaseUntil = null;
            if (notification.EmailState == "failed") notification.EmailRetryAt = DateTimeOffset.UtcNow.AddMinutes(15);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            notification.EmailState = "failed"; notification.EmailLastError = "delivery_failed"; notification.LeaseUntil = null;
            notification.EmailRetryAt = DateTimeOffset.UtcNow.AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(notification.EmailAttempts, 6))));
            logger.LogWarning("Ticket notification email failed {NotificationId}; retry is scheduled", notification.Id);
        }
        await db.SaveChangesAsync(ct);
        return true;
    }
}

public sealed class TicketNotificationWorker(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<TicketNotificationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (configuration.GetValue<bool>("Tickets:EmailNotificationsEnabled"))
                {
                    for (var i = 0; i < 20; i++)
                    {
                        using var scope = scopes.CreateScope();
                        if (!await scope.ServiceProvider.GetRequiredService<TicketNotificationDispatcher>().DispatchOneAsync(stoppingToken)) break;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch { logger.LogWarning("Ticket notification processing is temporarily unavailable"); }
            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
