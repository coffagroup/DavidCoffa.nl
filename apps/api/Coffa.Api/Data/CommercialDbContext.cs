using Coffa.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Coffa.Api.Data;

public partial class AppDbContext
{
    public DbSet<CommercialCounter> CommercialCounters => Set<CommercialCounter>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoicePayment> InvoicePayments => Set<InvoicePayment>();
    public DbSet<BusinessExpense> Expenses => Set<BusinessExpense>();
    public DbSet<ServiceSubscription> Subscriptions => Set<ServiceSubscription>();

    partial void ConfigureCommercial(ModelBuilder builder)
    {
        builder.Entity<CommercialCounter>().HasKey(x => x.Key);
        builder.Entity<CommercialCounter>().Property(x => x.Key).HasMaxLength(100);
        builder.Entity<Quote>().HasKey(x => x.Id);
        builder.Entity<Invoice>().HasKey(x => x.Id);
        builder.Entity<ServiceSubscription>().HasKey(x => x.Id);
        builder.Entity<InvoicePayment>().HasKey(x => x.Id);
        builder.Entity<BusinessExpense>().HasKey(x => x.Id);
        builder.Entity<Quote>().HasIndex(x => x.Number).IsUnique();
        builder.Entity<Invoice>().HasIndex(x => x.Number).IsUnique();
        builder.Entity<Invoice>().HasIndex(x => new { x.SubscriptionId, x.SubscriptionPeriod }).IsUnique();
        builder.Entity<Invoice>().HasIndex(x => x.CreditForInvoiceId).IsUnique();
        builder.Entity<InvoicePayment>().HasIndex(x => x.IdempotencyKey).IsUnique();
        builder.Entity<Quote>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Invoice>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ServiceSubscription>().HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Invoice>().HasMany(x => x.Payments).WithOne().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Quote>().HasIndex(x => new { x.OrganizationId, x.Status });
        builder.Entity<Invoice>().HasIndex(x => new { x.OrganizationId, x.InvoiceDate });
        builder.Entity<ServiceSubscription>().HasIndex(x => new { x.Status, x.NextInvoiceDate });
        builder.Entity<BusinessExpense>().HasIndex(x => new { x.IsArchived, x.Date });
        foreach (var entity in new[] { typeof(Quote), typeof(Invoice), typeof(InvoicePayment), typeof(BusinessExpense), typeof(ServiceSubscription) })
        {
            foreach (var property in entity.GetProperties().Where(x => x.PropertyType == typeof(decimal)))
                builder.Entity(entity).Property(property.Name).HasPrecision(18, 2);
        }
        builder.Entity<ServiceSubscription>().Property(x => x.TaxRate).HasPrecision(5, 2);
        builder.Entity<BusinessExpense>().Property(x => x.TaxRate).HasPrecision(5, 2);
        builder.Entity<Quote>().Property(x => x.LinesJson).HasColumnType("jsonb");
        builder.Entity<Invoice>().Property(x => x.LinesJson).HasColumnType("jsonb");
    }
}
