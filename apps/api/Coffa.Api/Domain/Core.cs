using Microsoft.AspNetCore.Identity;
namespace Coffa.Api.Domain;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
    public Guid? OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public bool IsActive { get; set; } = true;
    public int SessionVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Website { get; set; } = "";
    public string Address { get; set; } = "";
    public string ChamberOfCommerce { get; set; } = "";
    public string VatNumber { get; set; } = "";
    public string Type { get; set; } = "customer";
    public string Status { get; set; } = "active";
    public string[] Tags { get; set; } = [];
    public string InternalNotes { get; set; } = "";
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }
    public int Version { get; set; }
    public bool SetupOnly { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string UserAgent { get; set; } = "";
}
public class UserInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public Guid? OrganizationId { get; set; }
    public string Role { get; set; } = "Customer";
    public string[] Permissions { get; set; } = [];
    public string TokenHash { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddDays(7);
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
}
public class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? UserId { get; set; }
    public Guid? OrganizationId { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string? EntityId { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class BusinessSettings
{
    public int Id { get; set; } = 1;
    public string CompanyName { get; set; } = "David Coffa";
    public string Email { get; set; } = "info@davidcoffa.nl";
    public string Address { get; set; } = "";
    public string ChamberOfCommerce { get; set; } = "";
    public string VatNumber { get; set; } = "";
    public string Iban { get; set; } = "";
    public string PaymentInstructions { get; set; } = "";
    public string InvoicePrefix { get; set; } = "DC";
    public string QuotePrefix { get; set; } = "OFF";
    public int PaymentTermDays { get; set; } = 14;
    public bool AutomaticEmailEnabled { get; set; }
}
