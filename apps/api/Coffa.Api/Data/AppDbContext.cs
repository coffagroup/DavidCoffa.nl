using Coffa.Api.Domain;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace Coffa.Api.Data;

public partial class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<UserInvitation> UserInvitations => Set<UserInvitation>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<BusinessSettings> BusinessSettings => Set<BusinessSettings>();
    public DbSet<BlogArticle> BlogArticles => Set<BlogArticle>();
    public DbSet<BlogResearch> BlogResearches => Set<BlogResearch>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>().HasOne(x=>x.Organization).WithMany().HasForeignKey(x=>x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ApplicationUser>().Property(x=>x.DisplayName).HasMaxLength(200);
        builder.Entity<Organization>().Property(x=>x.Name).HasMaxLength(200);
        builder.Entity<Organization>().HasIndex(x=>x.Name);
        builder.Entity<BlogArticle>().HasIndex(x=>x.Slug).IsUnique();
        builder.Entity<BlogArticle>().HasIndex(x=>new{x.Status,x.PublishedAt});
        builder.Entity<BlogResearch>().HasIndex(x=>x.CreatedAt);
        builder.Entity<BlogArticle>().Property(x=>x.RelatedKeywordsJson).HasColumnType("jsonb");
        builder.Entity<BlogArticle>().Property(x=>x.SectionsJson).HasColumnType("jsonb");
        builder.Entity<BlogArticle>().Property(x=>x.InternalLinksJson).HasColumnType("jsonb");
        builder.Entity<BlogResearch>().Property(x=>x.OpportunitiesJson).HasColumnType("jsonb");
        builder.Entity<BlogResearch>().Property(x=>x.ExistingContentJson).HasColumnType("jsonb");
        builder.Entity<BlogArticle>().HasOne<BlogResearch>().WithMany().HasForeignKey(x=>x.ResearchId).OnDelete(DeleteBehavior.SetNull);
        builder.Entity<UserSession>().HasOne(x=>x.User).WithMany().HasForeignKey(x=>x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Entity<UserSession>().HasIndex(x=>new{x.UserId,x.RevokedAt});
        builder.Entity<UserInvitation>().HasIndex(x=>x.TokenHash).IsUnique();
        builder.Entity<UserInvitation>().HasIndex(x=>x.Email);
        builder.Entity<AuditEntry>().HasIndex(x=>x.CreatedAt);
        ConfigureCommercial(builder);
        ConfigureOperations(builder);
    }
    partial void ConfigureCommercial(ModelBuilder builder);
    partial void ConfigureOperations(ModelBuilder builder);
}
