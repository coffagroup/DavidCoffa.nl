using Coffa.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coffa.Api.Data;

public partial class AppDbContext
{
    public DbSet<ContactPerson> ContactPeople => Set<ContactPerson>();
    public DbSet<OrganizationNote> OrganizationNotes => Set<OrganizationNote>();
    public DbSet<SalesLead> SalesLeads => Set<SalesLead>();
    public DbSet<PublicContactReceipt> PublicContactReceipts => Set<PublicContactReceipt>();
    public DbSet<LeadActivity> LeadActivities => Set<LeadActivity>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<WorkTask> WorkTasks => Set<WorkTask>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<ActiveTimer> ActiveTimers => Set<ActiveTimer>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketComment> TicketComments => Set<TicketComment>();
    public DbSet<DocumentRecord> DocumentRecords => Set<DocumentRecord>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();

    partial void ConfigureOperations(ModelBuilder builder)
    {
        builder.Entity<ContactPerson>().HasIndex(x => new { x.OrganizationId, x.IsArchived });
        builder.Entity<OrganizationNote>().HasIndex(x => new { x.OrganizationId, x.CreatedAt });
        builder.Entity<SalesLead>().Property(x => x.ExpectedValue).HasPrecision(18, 2);
        builder.Entity<SalesLead>().HasIndex(x => new { x.Stage, x.IsArchived });
        builder.Entity<PublicContactReceipt>().HasOne<SalesLead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<LeadActivity>().HasOne<SalesLead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Project>().Property(x => x.Budget).HasPrecision(18, 2);
        builder.Entity<Project>().Property(x => x.EstimatedHours).HasPrecision(10, 2);
        builder.Entity<Project>().HasIndex(x => new { x.OrganizationId, x.SharedWithCustomer });
        builder.Entity<WorkTask>().HasIndex(x => new { x.OrganizationId, x.ProjectId, x.Status });
        builder.Entity<WorkTask>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<WorkTask>().HasOne<WorkTask>().WithMany().HasForeignKey(x => x.ParentTaskId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TimeEntry>().Property(x => x.HourlyRate).HasPrecision(18, 4);
        builder.Entity<TimeEntry>().HasIndex(x => new { x.OrganizationId, x.WorkDate });
        builder.Entity<TimeEntry>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TimeEntry>().HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ActiveTimer>().HasIndex(x => x.UserId).IsUnique();
        builder.Entity<ActiveTimer>().Property(x => x.HourlyRate).HasPrecision(18, 4);
        builder.Entity<Ticket>().HasIndex(x => new { x.OrganizationId, x.Status });
        builder.Entity<Ticket>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TicketComment>().HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<DocumentRecord>().HasIndex(x => new { x.OrganizationId, x.IsArchived, x.SharedWithCustomer });
        builder.Entity<DocumentRecord>().HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<DocumentRecord>().HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<UserNotification>().HasIndex(x => new { x.UserId, x.ReadAt });
        builder.Entity<UserNotification>().HasIndex(x => new { x.EmailState, x.EmailRetryAt });
        // Tenant foreign keys prevent orphan records; access is additionally checked on every endpoint.
        builder.Entity<ContactPerson>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<OrganizationNote>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Project>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<WorkTask>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<TimeEntry>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Ticket>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<DocumentRecord>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}
