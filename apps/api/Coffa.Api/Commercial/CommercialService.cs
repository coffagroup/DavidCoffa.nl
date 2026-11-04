using System.Data;
using System.Text.Json;
using Coffa.Api.Data;
using Coffa.Api.Domain;
using Coffa.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Coffa.Api.Commercial;

public sealed record QuoteRequest(Guid OrganizationId, Guid? ProjectId, string Title, DateOnly ValidUntil, string? Notes, string? Terms, bool SharedWithCustomer, List<CommercialLineInput> Lines);
public sealed record InvoiceRequest(Guid OrganizationId, Guid? ProjectId, string Title, DateOnly InvoiceDate, DateOnly DueDate, string? Notes, string? Terms, bool SharedWithCustomer, List<CommercialLineInput> Lines);
public sealed record SubscriptionRequest(Guid OrganizationId, string Name, string? Description, decimal Amount, decimal TaxRate, string Frequency, DateOnly StartDate, DateOnly? EndDate, DateOnly NextInvoiceDate, string Status, bool AutomaticInvoicing);
public sealed record PaymentRequest(decimal Amount, DateOnly Date, string? Reference, Guid IdempotencyKey);
public sealed record CreditRequest(string Reason);
public sealed record ShareRequest(bool SharedWithCustomer);
public sealed record TimeInvoiceRequest(Guid OrganizationId, List<Guid> EntryIds, string? Title, DateOnly? InvoiceDate = null, DateOnly? DueDate = null);
public sealed record HistoricalInvoiceRequest(Guid OrganizationId, Guid? ProjectId, string Number, DateOnly InvoiceDate, DateOnly DueDate, decimal NetAmount, decimal TaxAmount, decimal TotalAmount, decimal PaidAmount = 0, DateOnly? PaymentDate = null);

public sealed class CommercialService(AppDbContext db, AuditService audit)
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
    public async Task<BusinessSettings> Settings(CancellationToken ct = default) => await db.BusinessSettings.SingleOrDefaultAsync(x => x.Id == 1, ct) ?? new BusinessSettings();
    public async Task<Organization> Organization(Guid id, CancellationToken ct) => await db.Organizations.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "organization_not_found", "Klantorganisatie niet gevonden.");
    public async Task CheckProject(Guid? id, Guid organizationId, CancellationToken ct)
    {
        if (id.HasValue && !await db.Projects.AnyAsync(x => x.Id == id && x.OrganizationId == organizationId && !x.IsArchived, ct)) throw new ApiException(400, "invalid_project", "Dit project hoort niet bij de geselecteerde klant.");
    }
    public static void Text(string? value, int max, string field, bool required = false)
    {
        if ((required && string.IsNullOrWhiteSpace(value)) || value?.Length > max || value?.Contains('\0') == true) throw new ApiException(400, "invalid_input", $"Controleer {field} (maximaal {max} tekens).");
    }
    public static void Dates(DateOnly start, DateOnly end)
    {
        if (start.Year < 1900 || end.Year > 2200 || end < start) throw new ApiException(400, "invalid_dates", "Controleer de datum en vervaldatum.");
    }
    public async Task<Quote> SaveQuote(QuoteRequest input, Guid? id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock("coffa:commercial:issue", ct);
        Text(input.Title, 200, "de titel", true); Text(input.Notes, 10000, "de opmerkingen"); Text(input.Terms, 20000, "de voorwaarden");
        if (input.ValidUntil.Year < 1900 || input.ValidUntil.Year > 2200) throw new ApiException(400, "invalid_dates", "Controleer de geldigheidsdatum.");
        await Organization(input.OrganizationId, ct); await CheckProject(input.ProjectId, input.OrganizationId, ct);
        var totals = CommercialCalculations.Calculate(input.Lines);
        var quote = id.HasValue ? await db.Quotes.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "not_found", "Offerte niet gevonden.") : new Quote();
        if (quote.Status != "draft") throw new ApiException(409, "immutable_quote", "Een uitgegeven offerte kan niet meer worden gewijzigd. Maak een nieuw concept.");
        quote.OrganizationId = input.OrganizationId; quote.ProjectId = input.ProjectId; quote.Title = input.Title.Trim(); quote.ValidUntil = input.ValidUntil;
        quote.Notes = input.Notes?.Trim() ?? ""; quote.Terms = input.Terms?.Trim() ?? ""; quote.SharedWithCustomer = input.SharedWithCustomer;
        quote.LinesJson = CommercialCalculations.Serialize(totals); quote.NetAmount = totals.NetAmount; quote.TaxAmount = totals.TaxAmount; quote.TotalAmount = totals.TotalAmount;
        if (!id.HasValue) db.Quotes.Add(quote);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); await audit.RecordAsync(id.HasValue ? "quote.updated" : "quote.created", "quote", quote.Id.ToString(), organizationId: quote.OrganizationId, cancellationToken: ct);
        return quote;
    }
    public async Task<Invoice> SaveInvoice(InvoiceRequest input, Guid? id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock("coffa:commercial:issue", ct);
        Text(input.Title, 200, "de titel", true); Text(input.Notes, 10000, "de opmerkingen"); Text(input.Terms, 20000, "de voorwaarden"); Dates(input.InvoiceDate, input.DueDate);
        await Organization(input.OrganizationId, ct); await CheckProject(input.ProjectId, input.OrganizationId, ct);
        var totals = CommercialCalculations.Calculate(input.Lines);
        var invoice = id.HasValue ? await db.Invoices.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "not_found", "Factuur niet gevonden.") : new Invoice();
        if (invoice.Status != "draft") throw new ApiException(409, "immutable_invoice", "Een definitieve factuur kan niet worden gewijzigd. Gebruik een creditfactuur voor correcties.");
        invoice.OrganizationId = input.OrganizationId; invoice.ProjectId = input.ProjectId; invoice.Title = input.Title.Trim(); invoice.InvoiceDate = input.InvoiceDate; invoice.DueDate = input.DueDate;
        invoice.Notes = input.Notes?.Trim() ?? ""; invoice.Terms = input.Terms?.Trim() ?? ""; invoice.SharedWithCustomer = input.SharedWithCustomer;
        invoice.LinesJson = CommercialCalculations.Serialize(totals); invoice.NetAmount = totals.NetAmount; invoice.TaxAmount = totals.TaxAmount; invoice.TotalAmount = totals.TotalAmount;
        if (!id.HasValue) db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); await audit.RecordAsync(id.HasValue ? "invoice.updated" : "invoice.created", "invoice", invoice.Id.ToString(), organizationId: invoice.OrganizationId, cancellationToken: ct);
        return invoice;
    }
    public async Task Lock(string name, CancellationToken ct)
    {
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({name}))", ct);
    }
    public async Task<string> AllocateNumber(string kind, int year, string prefix, CancellationToken ct)
    {
        var key = $"{kind}:{year}:{prefix}";
        var counter = db.CommercialCounters.Local.FirstOrDefault(x => x.Key == key) ?? await db.CommercialCounters.SingleOrDefaultAsync(x => x.Key == key, ct);
        if (counter is null) { counter = new CommercialCounter { Key = key }; db.CommercialCounters.Add(counter); }
        string number;
        do { number = $"{prefix.ToUpperInvariant()}-{year}-{counter.NextValue++:D4}"; }
        while (kind == "invoice" ? await db.Invoices.AnyAsync(x => x.Number == number, ct) : await db.Quotes.AnyAsync(x => x.Number == number, ct));
        return number;
    }
    private async Task Snapshot(Invoice invoice, BusinessSettings settings, CancellationToken ct)
    {
        var organization = await Organization(invoice.OrganizationId, ct);
        invoice.IssuerSnapshot = JsonSerializer.Serialize(settings, CommercialCalculations.Json);
        invoice.CustomerSnapshot = JsonSerializer.Serialize(new { organization.Name, organization.Address, organization.Email, organization.ChamberOfCommerce, organization.VatNumber }, CommercialCalculations.Json);
    }
    public async Task<Invoice> IssueInvoice(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock("coffa:commercial:issue", ct);
        var invoice = await db.Invoices.Include(x => x.Payments).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "not_found", "Factuur niet gevonden.");
        if (invoice.Status != "draft") return invoice;
        var settings = await Settings(ct); ValidateIssuer(settings);
        invoice.Number = await AllocateNumber("invoice", invoice.InvoiceDate.Year, settings.InvoicePrefix, ct);
        await Snapshot(invoice, settings, ct); invoice.Status = "issued"; invoice.IssuedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        await audit.RecordAsync("invoice.issued", "invoice", invoice.Id.ToString(), organizationId: invoice.OrganizationId, cancellationToken: ct);
        return invoice;
    }
    public static void ValidateIssuer(BusinessSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.CompanyName) || string.IsNullOrWhiteSpace(settings.Address) || string.IsNullOrWhiteSpace(settings.ChamberOfCommerce) || string.IsNullOrWhiteSpace(settings.VatNumber))
            throw new ApiException(409, "issuer_not_configured", "Vul eerst bedrijfsnaam, adres, KvK- en btw-nummer in bij Instellingen.");
    }
    public async Task<Quote> IssueQuote(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock("coffa:commercial:issue", ct);
        var quote = await db.Quotes.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "not_found", "Offerte niet gevonden.");
        if (quote.Status != "draft") return quote;
        if (quote.ValidUntil < Today) throw new ApiException(409, "expired_quote", "Deze geldigheidsdatum is verstreken.");
        var settings = await Settings(ct); ValidateIssuer(settings); var organization = await Organization(quote.OrganizationId, ct);
        quote.Number = await AllocateNumber("quote", Today.Year, settings.QuotePrefix, ct);
        quote.IssuerSnapshot = JsonSerializer.Serialize(settings, CommercialCalculations.Json);
        quote.CustomerSnapshot = JsonSerializer.Serialize(new { organization.Name, organization.Address, organization.Email, organization.ChamberOfCommerce, organization.VatNumber }, CommercialCalculations.Json);
        quote.Status = "issued"; quote.IssuedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        await audit.RecordAsync("quote.issued", "quote", quote.Id.ToString(), organizationId: quote.OrganizationId, cancellationToken: ct);
        return quote;
    }
    public async Task AddPayment(Guid id, PaymentRequest input, CancellationToken ct)
    {
        if (input.Amount <= 0 || input.Amount != CommercialCalculations.Round(input.Amount) || input.IdempotencyKey == Guid.Empty || input.Date > Today || input.Date.Year < 1900) throw new ApiException(400, "invalid_payment", "Controleer bedrag, betaaldatum en referentie.");
        Text(input.Reference, 200, "de betalingsreferentie");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock($"coffa:invoice:{id}", ct);
        var invoice = await db.Invoices.Include(x => x.Payments).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "not_found", "Factuur niet gevonden.");
        if (invoice.Status == "draft" || invoice.Kind == "credit") throw new ApiException(409, "invalid_payment_target", "Registreer een betaling op een definitieve gewone factuur.");
        var existing = await db.InvoicePayments.SingleOrDefaultAsync(x => x.IdempotencyKey == input.IdempotencyKey, ct);
        if (existing is not null)
        {
            if (existing.InvoiceId != id || existing.Amount != input.Amount || existing.Date != input.Date || existing.Reference != (input.Reference?.Trim() ?? "")) throw new ApiException(409, "payment_conflict", "Deze betalingsreferentie is al voor een andere registratie gebruikt.");
            return;
        }
        var credit = -await db.Invoices.Where(x => x.CreditForInvoiceId == id && x.Status != "draft").SumAsync(x => x.TotalAmount, ct);
        if (input.Amount > CommercialCalculations.Outstanding(invoice, credit)) throw new ApiException(400, "payment_exceeds_balance", "De betaling is hoger dan het openstaande bedrag.");
        db.InvoicePayments.Add(new InvoicePayment { InvoiceId = id, Amount = input.Amount, Date = input.Date, Reference = input.Reference?.Trim() ?? "", IdempotencyKey = input.IdempotencyKey });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        await audit.RecordAsync("invoice.payment_registered", "invoice", id.ToString(), organizationId: invoice.OrganizationId, cancellationToken: ct);
    }
    public async Task<Invoice> Credit(Guid id, string reason, CancellationToken ct)
    {
        Text(reason, 2000, "de reden", true);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock("coffa:commercial:issue", ct); await Lock($"coffa:invoice:{id}", ct);
        var original = await db.Invoices.Include(x => x.Payments).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "not_found", "Factuur niet gevonden.");
        var existing = await db.Invoices.SingleOrDefaultAsync(x => x.CreditForInvoiceId == id, ct); if (existing is not null) return existing;
        if (original.Status == "draft" || original.Kind == "credit") throw new ApiException(409, "invalid_credit_target", "Alleen een definitieve factuur kan worden gecrediteerd.");
        var settings = await Settings(ct); ValidateIssuer(settings);
        var lines = CommercialCalculations.Lines(original.LinesJson).Select(x => x with { Quantity = -x.Quantity, NetAmount = -x.NetAmount, TaxAmount = -x.TaxAmount, TotalAmount = -x.TotalAmount }).ToArray();
        var invoice = new Invoice { OrganizationId = original.OrganizationId, ProjectId = original.ProjectId, CreditForInvoiceId = id, Number = await AllocateNumber("invoice", Today.Year, settings.InvoicePrefix, ct), Title = $"Credit op {original.Number}", Kind = "credit", Status = "issued", InvoiceDate = Today, DueDate = Today, Notes = reason.Trim(), LinesJson = JsonSerializer.Serialize(lines, CommercialCalculations.Json), NetAmount = -original.NetAmount, TaxAmount = -original.TaxAmount, TotalAmount = -original.TotalAmount, IssuerSnapshot = original.IssuerSnapshot, CustomerSnapshot = original.CustomerSnapshot, IssuedAt = DateTimeOffset.UtcNow };
        db.Invoices.Add(invoice); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        await audit.RecordAsync("invoice.credited", "invoice", invoice.Id.ToString(), organizationId: invoice.OrganizationId, cancellationToken: ct); return invoice;
    }
    public async Task<Invoice> ConvertQuoteToInvoice(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock($"coffa:quote:{id}", ct);
        var quote = await db.Quotes.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "not_found", "Offerte niet gevonden.");
        if (quote.ConvertedInvoiceId.HasValue) return await db.Invoices.SingleAsync(x => x.Id == quote.ConvertedInvoiceId, ct);
        if (quote.Status != "accepted") throw new ApiException(409, "quote_not_accepted", "Accepteer de offerte voordat je deze omzet.");
        var settings = await Settings(ct);
        var invoice = new Invoice { OrganizationId = quote.OrganizationId, ProjectId = quote.ConvertedProjectId ?? quote.ProjectId, QuoteId = quote.Id, Title = quote.Title, InvoiceDate = Today, DueDate = Today.AddDays(settings.PaymentTermDays), Notes = quote.Notes, Terms = quote.Terms, LinesJson = quote.LinesJson, NetAmount = quote.NetAmount, TaxAmount = quote.TaxAmount, TotalAmount = quote.TotalAmount };
        db.Invoices.Add(invoice); quote.ConvertedInvoiceId = invoice.Id; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        await audit.RecordAsync("quote.converted_to_invoice", "quote", id.ToString(), organizationId: quote.OrganizationId, cancellationToken: ct); return invoice;
    }
    public async Task<Project> ConvertQuoteToProject(Guid id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock($"coffa:quote:{id}", ct);
        var quote = await db.Quotes.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "not_found", "Offerte niet gevonden.");
        if (quote.ConvertedProjectId.HasValue) return await db.Projects.SingleAsync(x => x.Id == quote.ConvertedProjectId, ct);
        if (quote.Status != "accepted") throw new ApiException(409, "quote_not_accepted", "Accepteer de offerte voordat je deze omzet.");
        var project = new Project { OrganizationId = quote.OrganizationId, Name = quote.Title, Description = quote.Notes, Budget = quote.NetAmount, Status = "draft" };
        db.Projects.Add(project); quote.ConvertedProjectId = project.Id; quote.ProjectId = project.Id; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        await audit.RecordAsync("quote.converted_to_project", "quote", id.ToString(), organizationId: quote.OrganizationId, cancellationToken: ct); return project;
    }
    public async Task<Invoice> InvoiceTime(TimeInvoiceRequest input, CancellationToken ct)
    {
        if (input.EntryIds is null || input.EntryIds.Count is < 1 or > 200 || input.EntryIds.Distinct().Count() != input.EntryIds.Count) throw new ApiException(400, "invalid_entries", "Selecteer 1 tot 200 verschillende urenregistraties.");
        await Organization(input.OrganizationId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock("coffa:time:invoice", ct);
        var entries = await db.TimeEntries.Where(x => input.EntryIds.Contains(x.Id) && x.OrganizationId == input.OrganizationId && x.IsBillable && x.InvoiceId == null).ToListAsync(ct);
        if (entries.Count != input.EntryIds.Count) throw new ApiException(409, "entries_already_invoiced", "Een selectie is al gefactureerd of hoort niet bij deze klant.");
        var totals = CommercialCalculations.Calculate(entries.Select(x => new CommercialLineInput($"{x.WorkDate:dd-MM-yyyy} · {(x.Description.Length > 980 ? x.Description[..977] + "..." : x.Description)}", decimal.Round(x.DurationMinutes / 60m, 4), x.HourlyRate)).ToList());
        var settings = await Settings(ct);
        var invoiceDate = input.InvoiceDate ?? Today; var dueDate = input.DueDate ?? invoiceDate.AddDays(settings.PaymentTermDays); Dates(invoiceDate, dueDate);
        var projectIds = entries.Select(x => x.ProjectId).Distinct().ToArray();
        var invoice = new Invoice { OrganizationId = input.OrganizationId, ProjectId = projectIds.Length == 1 ? projectIds[0] : null, Title = string.IsNullOrWhiteSpace(input.Title) ? "Gewerkte uren" : input.Title.Trim(), InvoiceDate = invoiceDate, DueDate = dueDate, LinesJson = CommercialCalculations.Serialize(totals), NetAmount = totals.NetAmount, TaxAmount = totals.TaxAmount, TotalAmount = totals.TotalAmount };
        Text(invoice.Title, 200, "de titel", true); db.Invoices.Add(invoice); foreach (var entry in entries) entry.InvoiceId = invoice.Id;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); await audit.RecordAsync("time.invoiced", "invoice", invoice.Id.ToString(), organizationId: input.OrganizationId, cancellationToken: ct); return invoice;
    }
    public async Task<ServiceSubscription> SaveSubscription(SubscriptionRequest input, Guid? id, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock("coffa:subscriptions:generate", ct);
        await Organization(input.OrganizationId, ct); Text(input.Name, 200, "de naam", true); Text(input.Description, 5000, "de omschrijving");
        CommercialCalculations.FrequencyMonths(input.Frequency);
        if (input.Amount <= 0 || input.Amount > 10000000 || input.Amount != CommercialCalculations.Round(input.Amount) || input.TaxRate is < 0 or > 100 || input.StartDate.Year < 1900 || input.StartDate.Year > 2200 || input.NextInvoiceDate.Year > 2200 || input.EndDate?.Year > 2200 || input.NextInvoiceDate < input.StartDate || input.EndDate < input.StartDate || input.Status is not ("active" or "paused" or "ended")) throw new ApiException(400, "invalid_subscription", "Controleer bedrag, status en datums van het abonnement.");
        var subscription = id.HasValue ? await db.Subscriptions.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "not_found", "Abonnement niet gevonden.") : new ServiceSubscription();
        // The contractual billing day is retained when editing a schedule.
        if (!id.HasValue || subscription.StartDate != input.StartDate) subscription.BillingDay = input.StartDate.Day;
        subscription.OrganizationId = input.OrganizationId; subscription.Name = input.Name.Trim(); subscription.Description = input.Description?.Trim() ?? ""; subscription.Amount = input.Amount; subscription.TaxRate = input.TaxRate; subscription.Frequency = input.Frequency; subscription.StartDate = input.StartDate; subscription.EndDate = input.EndDate; subscription.NextInvoiceDate = input.NextInvoiceDate; subscription.Status = input.Status; subscription.AutomaticInvoicing = input.AutomaticInvoicing;
        if (!id.HasValue) db.Subscriptions.Add(subscription); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); await audit.RecordAsync(id.HasValue ? "subscription.updated" : "subscription.created", "subscription", subscription.Id.ToString(), organizationId: subscription.OrganizationId, cancellationToken: ct); return subscription;
    }
    public async Task<int> SendRecurringEmails(IEmailService mail, CommercialPdf pdf, CancellationToken ct)
    {
        var settings = await Settings(ct); if (!settings.AutomaticEmailEnabled) return 0;
        var ids = await db.Invoices.AsNoTracking().Where(x => x.SubscriptionId != null && x.Status == "issued" && x.SentAt == null && x.InvoiceDate <= Today).OrderBy(x => x.InvoiceDate).Select(x => x.Id).Take(50).ToListAsync(ct);
        var sent = 0;
        foreach (var id in ids)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock($"coffa:invoice:email:{id}", ct);
            var invoice = await db.Invoices.SingleAsync(x => x.Id == id, ct); await db.Entry(invoice).ReloadAsync(ct);
            if (invoice.SentAt.HasValue) continue;
            var organization = await Organization(invoice.OrganizationId, ct); var content = await pdf.Invoice(invoice, settings, organization, ct);
            var delivery = await mail.SendAsync(new EmailMessage(organization.Email, $"Factuur {invoice.Number} · {invoice.Title}", $"<p>Beste klant,</p><p>Bijgevoegd vind je de factuur voor je abonnement. De vervaldatum is {invoice.DueDate:dd-MM-yyyy}.</p><p>David Coffa</p>", [new EmailAttachment($"factuur-{invoice.Number}.pdf", content, "application/pdf")]), ct);
            if (delivery.Sent) { invoice.SentAt = DateTimeOffset.UtcNow; sent++; }
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        if (sent > 0) await audit.RecordAsync("subscriptions.emails_sent", "subscription", details: $"{sent} facturen", cancellationToken: ct);
        return sent;
    }
    public async Task<int> GenerateRecurring(DateOnly through, CancellationToken ct)
    {
        var generated = 0;
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock("coffa:subscriptions:generate", ct);
        var settings = await Settings(ct);
        var subscriptions = await db.Subscriptions.Where(x => x.Status == "active" && x.AutomaticInvoicing && x.NextInvoiceDate <= through).OrderBy(x => x.NextInvoiceDate).Take(1000).ToListAsync(ct);
        foreach (var subscription in subscriptions)
        {
            var iterations = 0;
            while (subscription.NextInvoiceDate <= through && (!subscription.EndDate.HasValue || subscription.NextInvoiceDate <= subscription.EndDate))
            {
                if (++iterations > 120) throw new ApiException(409, "subscription_backlog", "Dit abonnement heeft meer dan 120 vervallen perioden. Corrigeer eerst de startdatum.");
                var period = subscription.NextInvoiceDate;
                if (!await db.Invoices.AnyAsync(x => x.SubscriptionId == subscription.Id && x.SubscriptionPeriod == period, ct))
                {
                    var totals = CommercialCalculations.Calculate([new CommercialLineInput(subscription.Name, 1, subscription.Amount, 0, subscription.TaxRate)]);
                    var invoice = new Invoice { OrganizationId = subscription.OrganizationId, SubscriptionId = subscription.Id, SubscriptionPeriod = period, Title = $"{subscription.Name} · {period:MM-yyyy}", Notes = subscription.Description, InvoiceDate = period, DueDate = period.AddDays(settings.PaymentTermDays), LinesJson = CommercialCalculations.Serialize(totals), NetAmount = totals.NetAmount, TaxAmount = totals.TaxAmount, TotalAmount = totals.TotalAmount };
                    ValidateIssuer(settings);
                    await Lock("coffa:commercial:issue", ct);
                    invoice.Number = await AllocateNumber("invoice", period.Year, settings.InvoicePrefix, ct);
                    await Snapshot(invoice, settings, ct); invoice.Status = "issued"; invoice.IssuedAt = DateTimeOffset.UtcNow;
                    db.Invoices.Add(invoice); generated++;
                }
                subscription.NextInvoiceDate = CommercialCalculations.NextBillingDate(period, subscription.Frequency, subscription.BillingDay);
            }
            if (subscription.EndDate.HasValue && subscription.NextInvoiceDate > subscription.EndDate) subscription.Status = "ended";
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        if (generated > 0) await audit.RecordAsync("subscriptions.invoices_generated", "subscription", details: $"{generated} facturen", cancellationToken: ct);
        return generated;
    }
}

public sealed class RecurringInvoiceWorker(IServiceScopeFactory scopes, ILogger<RecurringInvoiceWorker> logger, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope(); var service = scope.ServiceProvider.GetRequiredService<CommercialService>();
                await service.GenerateRecurring(CommercialService.Today, stoppingToken);
                if (configuration.GetValue<bool>("Email:EnableSending")) await service.SendRecurringEmails(scope.ServiceProvider.GetRequiredService<IEmailService>(), scope.ServiceProvider.GetRequiredService<CommercialPdf>(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogError("Recurring invoice generation failed; retrying on the next interval."); }
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; } catch (OperationCanceledException) { break; }
        }
    }
}
