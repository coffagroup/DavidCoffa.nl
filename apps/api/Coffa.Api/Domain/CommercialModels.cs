namespace Coffa.Api.Domain;

public sealed class CommercialCounter
{
    public string Key { get; set; } = "";
    public long NextValue { get; set; } = 1;
}

public sealed class Quote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid? ProjectId { get; set; }
    public string? Number { get; set; }
    public string Title { get; set; } = "";
    public string Status { get; set; } = "draft";
    public DateOnly ValidUntil { get; set; }
    public string Notes { get; set; } = "";
    public string Terms { get; set; } = "";
    public string LinesJson { get; set; } = "[]";
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public bool SharedWithCustomer { get; set; }
    public string IssuerSnapshot { get; set; } = "";
    public string CustomerSnapshot { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? IssuedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? ViewedAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public Guid? ConvertedInvoiceId { get; set; }
    public Guid? ConvertedProjectId { get; set; }
}

public sealed class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? QuoteId { get; set; }
    public Guid? SubscriptionId { get; set; }
    public DateOnly? SubscriptionPeriod { get; set; }
    public Guid? CreditForInvoiceId { get; set; }
    public string? Number { get; set; }
    public string Title { get; set; } = "";
    public string Status { get; set; } = "draft";
    public string Kind { get; set; } = "invoice";
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string Notes { get; set; } = "";
    public string Terms { get; set; } = "";
    public string LinesJson { get; set; } = "[]";
    public decimal NetAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public bool SharedWithCustomer { get; set; }
    public string IssuerSnapshot { get; set; } = "";
    public string CustomerSnapshot { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? IssuedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public Guid? OriginalDocumentId { get; set; }
    public List<InvoicePayment> Payments { get; set; } = [];
}

public sealed class InvoicePayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public Guid IdempotencyKey { get; set; }
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    public string Reference { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class BusinessExpense
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; }
    public string Supplier { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal NetAmount { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool IsArchived { get; set; }
}

public sealed class ServiceSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal TaxRate { get; set; } = 21;
    public string Frequency { get; set; } = "monthly";
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public DateOnly NextInvoiceDate { get; set; }
    public int BillingDay { get; set; }
    public string Status { get; set; } = "active";
    public bool AutomaticInvoicing { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
