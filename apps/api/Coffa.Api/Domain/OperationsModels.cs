namespace Coffa.Api.Domain;

public sealed class ContactPerson
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public bool IsPrimary { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class OrganizationNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string CreatedByUserId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsArchived { get; set; }
}

public sealed class SalesLead
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string CompanyName { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Website { get; set; } = "";
    public string Address { get; set; } = "";
    public string Stage { get; set; } = "new";
    public decimal? ExpectedValue { get; set; }
    public DateOnly? ExpectedCloseDate { get; set; }
    public string Notes { get; set; } = "";
    public string[] Tags { get; set; } = [];
    public Guid? ConvertedOrganizationId { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class PublicContactReceipt
{
    public Guid Id { get; set; }
    public Guid LeadId { get; set; }
    public string ContentHash { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LeadActivity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LeadId { get; set; }
    public string UserId { get; set; } = "";
    public string Kind { get; set; } = "note";
    public string Body { get; set; } = "";
    public DateOnly? DueDate { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "draft";
    public string Priority { get; set; } = "normal";
    public DateOnly? StartDate { get; set; }
    public DateOnly? Deadline { get; set; }
    public decimal? Budget { get; set; }
    public decimal? EstimatedHours { get; set; }
    public bool SharedWithCustomer { get; set; }
    public string? RepositoryUrl { get; set; }
    public string? DeploymentUrl { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class WorkTask
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? ParentTaskId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string? AssignedToUserId { get; set; }
    public string Status { get; set; } = "todo";
    public string Priority { get; set; } = "normal";
    public DateOnly? DueDate { get; set; }
    public bool SharedWithCustomer { get; set; }
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TimeEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? TicketId { get; set; }
    public Guid? InvoiceId { get; set; }
    public string UserId { get; set; } = "";
    public string Description { get; set; } = "";
    public DateOnly WorkDate { get; set; }
    public int DurationMinutes { get; set; }
    public bool IsBillable { get; set; }
    public decimal HourlyRate { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ActiveTimer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public Guid OrganizationId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? TicketId { get; set; }
    public string Description { get; set; } = "";
    public bool IsBillable { get; set; }
    public decimal HourlyRate { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Ticket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid? ProjectId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "general";
    public string Priority { get; set; } = "normal";
    public string Status { get; set; } = "new";
    public string? AssignedToUserId { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class TicketComment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }
    public Guid OrganizationId { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsInternal { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class DocumentRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? QuoteId { get; set; }
    public Guid? InvoiceId { get; set; }
    public Guid? TicketId { get; set; }
    public string OriginalName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public string StorageKey { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Category { get; set; } = "general";
    public bool SharedWithCustomer { get; set; }
    public bool IsArchived { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UserNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public Guid? TicketId { get; set; }
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }
    public string? EmailRecipient { get; set; }
    public string EmailState { get; set; } = "disabled";
    public int EmailAttempts { get; set; }
    public DateTimeOffset? EmailRetryAt { get; set; }
    public DateTimeOffset? EmailSentAt { get; set; }
    public string? EmailLastError { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
}
