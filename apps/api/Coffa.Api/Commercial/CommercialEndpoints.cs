using System.Globalization;
using System.Text;
using System.Text.Json;
using Coffa.Api.Data;
using Coffa.Api.Domain;
using Coffa.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Coffa.Api.Commercial;

public sealed record FromTimeRequest(Guid OrganizationId, List<Guid> TimeEntryIds, string? Title, DateOnly? InvoiceDate, DateOnly? DueDate);
public sealed record BusinessExpenseRequest(DateOnly Date, string Supplier, string Description, decimal NetAmount, decimal TaxRate);

public static class CommercialEndpoints
{
    public static void AddCommercial(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<CommercialService>();
        builder.Services.AddScoped<CommercialPdf>();
        builder.Services.AddHostedService<RecurringInvoiceWorker>();
    }
    private static void Read(CurrentActor actor)
    {
        if (!actor.IsCustomer) actor.DemandPermission("finance.read");
        else if (!actor.OrganizationId.HasValue) throw new ApiException(403, "organization_required", "Je account is niet aan een klantorganisatie gekoppeld.");
    }
    private static void Write(CurrentActor actor) => actor.DemandPermission("finance.write");
    private static IQueryable<Quote> Quotes(AppDbContext db, CurrentActor actor)
    {
        Read(actor); return actor.IsCustomer ? db.Quotes.Where(x => x.OrganizationId == actor.OrganizationId && x.SharedWithCustomer && x.Status != "draft") : db.Quotes;
    }
    private static IQueryable<Invoice> Invoices(AppDbContext db, CurrentActor actor)
    {
        Read(actor); return actor.IsCustomer ? db.Invoices.Where(x => x.OrganizationId == actor.OrganizationId && x.SharedWithCustomer && x.Status != "draft") : db.Invoices;
    }
    private static async Task<Quote> QuoteById(AppDbContext db, CurrentActor actor, Guid id, CancellationToken ct) => await Quotes(db, actor).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "not_found", "Offerte niet gevonden.");
    private static async Task<Invoice> InvoiceById(AppDbContext db, CurrentActor actor, Guid id, CancellationToken ct) => await Invoices(db, actor).Include(x => x.Payments).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "not_found", "Factuur niet gevonden.");
    private static object QuoteDto(Quote quote) => new { quote.Id, quote.OrganizationId, quote.ProjectId, quote.Number, quote.Title, status = CommercialCalculations.QuoteStatus(quote, CommercialService.Today), quote.ValidUntil, quote.Notes, quote.Terms, lines = CommercialCalculations.Lines(quote.LinesJson), quote.NetAmount, quote.TaxAmount, quote.TotalAmount, quote.SharedWithCustomer, quote.CreatedAt, quote.IssuedAt, quote.SentAt, quote.AcceptedAt, quote.ConvertedInvoiceId, quote.ConvertedProjectId };
    private static object InvoiceDto(Invoice invoice, decimal credit = 0) => new { invoice.Id, invoice.OrganizationId, invoice.ProjectId, invoice.QuoteId, invoice.SubscriptionId, invoice.CreditForInvoiceId, invoice.Number, invoice.Title, status = CommercialCalculations.InvoiceStatus(invoice, CommercialService.Today, credit), documentStatus = invoice.Status, invoice.Kind, invoice.InvoiceDate, invoice.DueDate, invoice.Notes, invoice.Terms, lines = CommercialCalculations.Lines(invoice.LinesJson), invoice.NetAmount, invoice.TaxAmount, invoice.TotalAmount, paidAmount = invoice.Payments.Sum(x => x.Amount), creditedAmount = credit, outstandingAmount = invoice.Kind == "credit" ? 0 : CommercialCalculations.Outstanding(invoice, credit), creditBalance = invoice.Kind == "credit" ? 0 : Math.Max(0, invoice.Payments.Sum(x => x.Amount) + credit - invoice.TotalAmount), invoice.SharedWithCustomer, invoice.CreatedAt, invoice.IssuedAt, invoice.SentAt, invoice.OriginalDocumentId, payments = invoice.Payments.Select(x => new { x.Id, x.Amount, x.Date, x.Reference }) };
    private static async Task<Dictionary<Guid, decimal>> Credits(AppDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var keys = ids.ToArray(); return await db.Invoices.Where(x => x.CreditForInvoiceId.HasValue && keys.Contains(x.CreditForInvoiceId.Value) && x.Status != "draft").GroupBy(x => x.CreditForInvoiceId!.Value).Select(g => new { Id = g.Key, Amount = -g.Sum(x => x.TotalAmount) }).ToDictionaryAsync(x => x.Id, x => x.Amount, ct);
    }
    public static void MapCommercial(this WebApplication app)
    {
        var api = app.MapGroup("/api").RequireAuthorization().WithTags("Commercieel");
        api.MapGet("/quotes", async (AppDbContext db, CurrentActor actor, string? search, string? status, Guid? organizationId, Guid? projectId, int? page, int? pageSize, CancellationToken ct) =>
        {
            var query = Quotes(db, actor).AsNoTracking(); if (organizationId.HasValue) query = query.Where(x => x.OrganizationId == organizationId);
            if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId);
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => EF.Functions.ILike(x.Title, "%" + search + "%") || (x.Number != null && EF.Functions.ILike(x.Number, "%" + search + "%")));
            var values = await query.OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
            if (!string.IsNullOrWhiteSpace(status)) values = values.Where(x => CommercialCalculations.QuoteStatus(x, CommercialService.Today) == status).ToList();
            var p = Math.Max(1, page ?? 1); var size = Math.Clamp(pageSize ?? 20, 1, 100);
            return Results.Ok(new { items = values.Skip((p - 1) * size).Take(size).Select(QuoteDto), total = values.Count, page = p, pageSize = size });
        });
        api.MapGet("/quotes/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) => Results.Ok(QuoteDto(await QuoteById(db, actor, id, ct))));
        api.MapPost("/quotes", async (QuoteRequest input, CommercialService service, CurrentActor actor, CancellationToken ct) => { Write(actor); return Results.Ok(QuoteDto(await service.SaveQuote(input, null, ct))); });
        api.MapPut("/quotes/{id:guid}", async (Guid id, QuoteRequest input, CommercialService service, CurrentActor actor, CancellationToken ct) => { Write(actor); return Results.Ok(QuoteDto(await service.SaveQuote(input, id, ct))); });
        api.MapPost("/quotes/{id:guid}/issue", async (Guid id, AppDbContext db, CommercialService service, CurrentActor actor, CommercialPdf pdf, IObjectStorage storage, AuditService audit, CancellationToken ct) =>
        {
            Write(actor); var quote = await service.IssueQuote(id, ct); await SaveQuotePdf(db, quote, service, pdf, storage, actor, audit, ct); return Results.Ok(QuoteDto(quote));
        });
        api.MapPost("/quotes/{id:guid}/accept", async (Guid id, AppDbContext db, CurrentActor actor, CommercialService service, AuditService audit, CancellationToken ct) =>
        {
            if (!actor.IsCustomer) Write(actor);
            await using var tx = await db.Database.BeginTransactionAsync(ct); await service.Lock($"coffa:quote:{id}", ct);
            var quote = await QuoteById(db, actor, id, ct);
            if (quote.Status == "accepted") return Results.Ok(QuoteDto(quote));
            if (quote.Status is not ("issued" or "sent") || quote.ValidUntil < CommercialService.Today) throw new ApiException(409, "quote_unavailable", "Deze offerte kan niet worden geaccepteerd.");
            quote.Status = "accepted"; quote.AcceptedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            await audit.RecordAsync("quote.accepted", "quote", id.ToString(), organizationId: quote.OrganizationId, cancellationToken: ct); return Results.Ok(QuoteDto(quote));
        });
        api.MapPost("/quotes/{id:guid}/reject", async (Guid id, AppDbContext db, CurrentActor actor, CommercialService service, AuditService audit, CancellationToken ct) =>
        {
            if (!actor.IsCustomer) Write(actor); await using var tx = await db.Database.BeginTransactionAsync(ct); await service.Lock($"coffa:quote:{id}", ct); var quote = await QuoteById(db, actor, id, ct);
            if (quote.Status is not ("issued" or "sent" or "rejected")) throw new ApiException(409, "quote_unavailable", "Deze offerte kan niet worden afgewezen.");
            quote.Status = "rejected"; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); await audit.RecordAsync("quote.rejected", "quote", id.ToString(), organizationId: quote.OrganizationId, cancellationToken: ct); return Results.Ok(QuoteDto(quote));
        });
        api.MapPost("/quotes/{id:guid}/convert-to-invoice", async (Guid id, CommercialService service, CurrentActor actor, CancellationToken ct) => { Write(actor); return Results.Ok(InvoiceDto(await service.ConvertQuoteToInvoice(id, ct))); });
        api.MapPost("/quotes/{id:guid}/convert-to-project", async (Guid id, CommercialService service, CurrentActor actor, CancellationToken ct) => { Write(actor); return Results.Ok(await service.ConvertQuoteToProject(id, ct)); });
        api.MapPost("/quotes/{id:guid}/share", async (Guid id, ShareRequest input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            Write(actor); var quote = await QuoteById(db, actor, id, ct); quote.SharedWithCustomer = input.SharedWithCustomer;
            var pdf = await db.DocumentRecords.Where(x => x.QuoteId == id && x.Category == "Offerte PDF" && !x.IsArchived).ToListAsync(ct);
            foreach (var document in pdf) document.SharedWithCustomer = input.SharedWithCustomer;
            await db.SaveChangesAsync(ct); await audit.RecordAsync("quote.sharing_changed", "quote", id.ToString(), organizationId: quote.OrganizationId, cancellationToken: ct); return Results.Ok(QuoteDto(quote));
        });
        api.MapGet("/quotes/{id:guid}/pdf", async (Guid id, AppDbContext db, CurrentActor actor, CommercialPdf pdf, CommercialService service, IObjectStorage storage, CancellationToken ct) =>
        {
            var quote = await QuoteById(db, actor, id, ct);
            var saved = quote.Status == "draft" ? null : await db.DocumentRecords.AsNoTracking().SingleOrDefaultAsync(x => x.QuoteId == quote.Id && x.Category == "Offerte PDF" && !x.IsArchived, ct);
            if (saved is not null) return Results.File(await storage.OpenAsync(saved.StorageKey, ct), "application/pdf", saved.OriginalName);
            var bytes = await pdf.Quote(quote, await service.Settings(ct), await service.Organization(quote.OrganizationId, ct), ct); return Results.File(bytes, "application/pdf", $"offerte-{quote.Number ?? quote.Id.ToString()}.pdf");
        });
        api.MapPost("/quotes/{id:guid}/send", async (Guid id, CommercialService service, AppDbContext db, CurrentActor actor, CommercialPdf pdf, IObjectStorage storage, IEmailService mail, AuditService audit, CancellationToken ct) =>
        {
            Write(actor); var quote = await service.IssueQuote(id, ct); var organization = await service.Organization(quote.OrganizationId, ct);
            var document = await SaveQuotePdf(db, quote, service, pdf, storage, actor, audit, ct);
            var file = await ReadStoredPdf(document, storage, ct);
            var delivery = await mail.SendAsync(new EmailMessage(organization.Email, $"Offerte {quote.Number} · {quote.Title}", $"<p>Beste klant,</p><p>Bijgevoegd vind je offerte {System.Net.WebUtility.HtmlEncode(quote.Number)}. Neem contact op als je vragen hebt.</p><p>David Coffa</p>", [new EmailAttachment($"offerte-{quote.Number}.pdf", file, "application/pdf")]), ct);
            if (delivery.Sent) { quote.SentAt = DateTimeOffset.UtcNow; if (quote.Status == "issued") quote.Status = "sent"; await db.SaveChangesAsync(ct); }
            await audit.RecordAsync(delivery.Sent ? "quote.email_sent" : "quote.email_development_outbox", "quote", id.ToString(), organizationId: quote.OrganizationId, cancellationToken: ct); return Results.Ok(delivery);
        });
        MapExpenses(api);
        MapInvoices(api);
        MapSubscriptions(api);
        MapReports(api);
    }
    private static object ExpenseDto(BusinessExpense expense) => new { expense.Id, expense.Date, expense.Supplier, expense.Description, expense.NetAmount, expense.TaxRate, expense.TaxAmount, expense.TotalAmount, expense.CreatedAt, expense.UpdatedAt };
    private static void ApplyExpense(BusinessExpense expense, BusinessExpenseRequest input)
    {
        CommercialService.Text(input.Supplier, 200, "de leverancier", true);
        CommercialService.Text(input.Description, 1000, "de omschrijving", true);
        if (input.Date.Year < 1900 || input.Date > CommercialService.Today || input.NetAmount < 0.01m || input.NetAmount > 1000000000m ||
            input.TaxRate is < 0 or > 100 || input.NetAmount != CommercialCalculations.Round(input.NetAmount) || input.TaxRate != CommercialCalculations.Round(input.TaxRate))
            throw new ApiException(400, "invalid_expense", "Controleer de datum, het bedrag exclusief btw en het btw-percentage.");
        expense.Date = input.Date; expense.Supplier = input.Supplier.Trim(); expense.Description = input.Description.Trim();
        expense.NetAmount = input.NetAmount; expense.TaxRate = input.TaxRate;
        expense.TaxAmount = CommercialCalculations.Round(input.NetAmount * input.TaxRate / 100m);
        expense.TotalAmount = expense.NetAmount + expense.TaxAmount; expense.UpdatedAt = DateTimeOffset.UtcNow;
    }
    private static void MapExpenses(RouteGroupBuilder api)
    {
        api.MapGet("/expenses", async (AppDbContext db, CurrentActor actor, string? search, string? q, DateOnly? from, DateOnly? to, int? page, int? pageSize, CancellationToken ct) =>
        {
            actor.DemandPermission("finance.read");
            var query = db.Expenses.AsNoTracking().Where(x => !x.IsArchived);
            if (from.HasValue || to.HasValue) { var period = Period(from, to); query = query.Where(x => x.Date >= period.From && x.Date <= period.To); }
            var term = string.IsNullOrWhiteSpace(search) ? q : search;
            if (!string.IsNullOrWhiteSpace(term)) query = query.Where(x => EF.Functions.ILike(x.Supplier, "%" + term + "%") || EF.Functions.ILike(x.Description, "%" + term + "%"));
            var total = await query.CountAsync(ct); var p = Math.Max(1, page ?? 1); var size = Math.Clamp(pageSize ?? 20, 1, 100);
            var items = await query.OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt).Skip((p - 1) * size).Take(size).ToListAsync(ct);
            return Results.Ok(new { items = items.Select(ExpenseDto), total, page = p, pageSize = size });
        });
        api.MapGet("/expenses/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        {
            actor.DemandPermission("finance.read");
            return Results.Ok(ExpenseDto(await db.Expenses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && !x.IsArchived, ct) ?? throw new ApiException(404, "not_found", "Uitgave niet gevonden.")));
        });
        api.MapPost("/expenses", async (BusinessExpenseRequest input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("finance.write"); var expense = new BusinessExpense { CreatedByUserId = actor.UserId }; ApplyExpense(expense, input);
            db.Expenses.Add(expense); await db.SaveChangesAsync(ct);
            await audit.RecordAsync("expense.created", "expense", expense.Id.ToString(), details: expense.Description, cancellationToken: ct);
            return Results.Created($"/api/expenses/{expense.Id}", ExpenseDto(expense));
        });
        api.MapPut("/expenses/{id:guid}", async (Guid id, BusinessExpenseRequest input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("finance.write"); var expense = await db.Expenses.SingleOrDefaultAsync(x => x.Id == id && !x.IsArchived, ct) ?? throw new ApiException(404, "not_found", "Uitgave niet gevonden.");
            ApplyExpense(expense, input); await db.SaveChangesAsync(ct);
            await audit.RecordAsync("expense.updated", "expense", expense.Id.ToString(), details: expense.Description, cancellationToken: ct);
            return Results.Ok(ExpenseDto(expense));
        });
        api.MapDelete("/expenses/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("finance.write"); var expense = await db.Expenses.SingleOrDefaultAsync(x => x.Id == id && !x.IsArchived, ct) ?? throw new ApiException(404, "not_found", "Uitgave niet gevonden.");
            expense.IsArchived = true; expense.UpdatedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct);
            await audit.RecordAsync("expense.archived", "expense", expense.Id.ToString(), details: expense.Description, cancellationToken: ct);
            return Results.NoContent();
        });
    }
    private static void MapInvoices(RouteGroupBuilder api)
    {
        api.MapGet("/invoices", async (AppDbContext db, CurrentActor actor, string? search, string? status, Guid? organizationId, Guid? projectId, int? page, int? pageSize, CancellationToken ct) =>
        {
            var query = Invoices(db, actor).AsNoTracking().Include(x => x.Payments).AsQueryable();
            if (organizationId.HasValue) query = query.Where(x => x.OrganizationId == organizationId); if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId);
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => EF.Functions.ILike(x.Title, "%" + search + "%") || (x.Number != null && EF.Functions.ILike(x.Number, "%" + search + "%")));
            var values = await query.OrderByDescending(x => x.InvoiceDate).ThenByDescending(x => x.CreatedAt).ToListAsync(ct); var credits = await Credits(db, values.Select(x => x.Id), ct);
            if (!string.IsNullOrWhiteSpace(status)) values = values.Where(x => CommercialCalculations.InvoiceStatus(x, CommercialService.Today, credits.GetValueOrDefault(x.Id)) == status).ToList();
            var p = Math.Max(1, page ?? 1); var size = Math.Clamp(pageSize ?? 20, 1, 100);
            return Results.Ok(new { items = values.Skip((p - 1) * size).Take(size).Select(x => InvoiceDto(x, credits.GetValueOrDefault(x.Id))), total = values.Count, page = p, pageSize = size });
        });
        api.MapGet("/invoices/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) => { var invoice = await InvoiceById(db, actor, id, ct); var credits = await Credits(db, [id], ct); return Results.Ok(InvoiceDto(invoice, credits.GetValueOrDefault(id))); });
        api.MapPost("/invoices", async (InvoiceRequest input, CommercialService service, CurrentActor actor, CancellationToken ct) => { Write(actor); return Results.Ok(InvoiceDto(await service.SaveInvoice(input, null, ct))); });
        api.MapPut("/invoices/{id:guid}", async (Guid id, InvoiceRequest input, CommercialService service, CurrentActor actor, CancellationToken ct) => { Write(actor); return Results.Ok(InvoiceDto(await service.SaveInvoice(input, id, ct))); });
        api.MapPost("/invoices/{id:guid}/issue", async (Guid id, AppDbContext db, CommercialService service, CurrentActor actor, CommercialPdf pdf, IObjectStorage storage, AuditService audit, CancellationToken ct) =>
        {
            Write(actor); var invoice = await service.IssueInvoice(id, ct); await SaveInvoicePdf(db, invoice, service, pdf, storage, actor, audit, ct); return Results.Ok(InvoiceDto(invoice));
        });
        api.MapPost("/invoices/{id:guid}/payments", async (Guid id, PaymentRequest input, CommercialService service, AppDbContext db, CurrentActor actor, CancellationToken ct) => { Write(actor); await service.AddPayment(id, input, ct); var invoice = await InvoiceById(db, actor, id, ct); var credits = await Credits(db, [id], ct); return Results.Ok(InvoiceDto(invoice, credits.GetValueOrDefault(id))); });
        api.MapPost("/invoices/{id:guid}/credit", async (Guid id, CreditRequest input, CommercialService service, CurrentActor actor, CancellationToken ct) => { Write(actor); return Results.Ok(InvoiceDto(await service.Credit(id, input.Reason, ct))); });
        api.MapPost("/invoices/{id:guid}/share", async (Guid id, ShareRequest input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            Write(actor); var invoice = await InvoiceById(db, actor, id, ct); invoice.SharedWithCustomer = input.SharedWithCustomer;
            var documents = await db.DocumentRecords.Where(x => x.InvoiceId == id || x.Id == invoice.OriginalDocumentId).ToListAsync(ct);
            foreach (var document in documents) document.SharedWithCustomer = input.SharedWithCustomer;
            await db.SaveChangesAsync(ct); await audit.RecordAsync("invoice.sharing_changed", "invoice", id.ToString(), organizationId: invoice.OrganizationId, cancellationToken: ct); return Results.Ok(InvoiceDto(invoice));
        });
        api.MapGet("/invoices/{id:guid}/pdf", async (Guid id, AppDbContext db, CurrentActor actor, CommercialPdf pdf, CommercialService service, IObjectStorage storage, CancellationToken ct) =>
        {
            var invoice = await InvoiceById(db, actor, id, ct);
            if (invoice.OriginalDocumentId.HasValue)
            {
                var document = await db.DocumentRecords.SingleOrDefaultAsync(x => x.Id == invoice.OriginalDocumentId && x.OrganizationId == invoice.OrganizationId, ct) ?? throw new ApiException(404, "not_found", "Origineel document niet gevonden.");
                return Results.File(await storage.OpenAsync(document.StorageKey, ct), "application/pdf", document.OriginalName);
            }
            var generated = invoice.Status == "draft" ? null : await db.DocumentRecords.AsNoTracking().SingleOrDefaultAsync(x => x.InvoiceId == invoice.Id && x.Category == "Factuur PDF" && !x.IsArchived, ct);
            if (generated is not null) return Results.File(await storage.OpenAsync(generated.StorageKey, ct), "application/pdf", generated.OriginalName);
            var file = await pdf.Invoice(invoice, await service.Settings(ct), await service.Organization(invoice.OrganizationId, ct), ct); return Results.File(file, "application/pdf", $"factuur-{invoice.Number ?? invoice.Id.ToString()}.pdf");
        });
        api.MapGet("/invoices/{id:guid}/reminder-preview", async (Guid id, AppDbContext db, CurrentActor actor, CommercialService service, CancellationToken ct) =>
        {
            Write(actor); var invoice = await InvoiceById(db, actor, id, ct); var credits = await Credits(db, [id], ct); var balance = CommercialCalculations.Outstanding(invoice, credits.GetValueOrDefault(id));
            if (invoice.Status == "draft" || invoice.Kind == "credit" || balance <= 0) throw new ApiException(409, "no_outstanding_balance", "Deze factuur heeft geen openstaand bedrag.");
            var organization = await service.Organization(invoice.OrganizationId, ct); return Results.Ok(new { to = organization.Email, subject = $"Betalingsherinnering {invoice.Number}", body = $"Beste klant,\n\nVoor factuur {invoice.Number} staat nog {balance.ToString("C", CultureInfo.GetCultureInfo("nl-NL"))} open. De vervaldatum was {invoice.DueDate:dd-MM-yyyy}. Wil je de betaling controleren?\n\nMet vriendelijke groet,\nDavid Coffa" });
        });
        api.MapPost("/invoices/{id:guid}/send-reminder", async (Guid id, AppDbContext db, CurrentActor actor, CommercialService service, IEmailService mail, AuditService audit, CancellationToken ct) =>
        {
            Write(actor); var invoice = await InvoiceById(db, actor, id, ct); var credits = await Credits(db, [id], ct); var balance = CommercialCalculations.Outstanding(invoice, credits.GetValueOrDefault(id));
            if (invoice.Status == "draft" || invoice.Kind == "credit" || balance <= 0) throw new ApiException(409, "no_outstanding_balance", "Deze factuur heeft geen openstaand bedrag.");
            var organization = await service.Organization(invoice.OrganizationId, ct);
            var delivery = await mail.SendAsync(organization.Email, $"Betalingsherinnering {invoice.Number}", $"<p>Beste klant,</p><p>Voor factuur {System.Net.WebUtility.HtmlEncode(invoice.Number)} staat nog {balance.ToString("C", CultureInfo.GetCultureInfo("nl-NL"))} open. De vervaldatum was {invoice.DueDate:dd-MM-yyyy}. Wil je de betaling controleren?</p><p>Met vriendelijke groet,<br>David Coffa</p>", ct);
            await audit.RecordAsync(delivery.Sent ? "invoice.reminder_sent" : "invoice.reminder_development_outbox", "invoice", id.ToString(), organizationId: invoice.OrganizationId, cancellationToken: ct); return Results.Ok(delivery);
        });
        api.MapPost("/invoices/{id:guid}/send", async (Guid id, AppDbContext db, CurrentActor actor, CommercialService service, CommercialPdf pdf, IEmailService mail, IObjectStorage storage, AuditService audit, CancellationToken ct) =>
        {
            Write(actor); var invoice = await service.IssueInvoice(id, ct); var organization = await service.Organization(invoice.OrganizationId, ct); byte[] file;
            if (invoice.OriginalDocumentId.HasValue) { var document = await db.DocumentRecords.SingleAsync(x => x.Id == invoice.OriginalDocumentId, ct); await using var stream = await storage.OpenAsync(document.StorageKey, ct); using var buffer = new MemoryStream(); await stream.CopyToAsync(buffer, ct); file = buffer.ToArray(); }
            else { var document = await SaveInvoicePdf(db, invoice, service, pdf, storage, actor, audit, ct); file = await ReadStoredPdf(document, storage, ct); }
            var delivery = await mail.SendAsync(new EmailMessage(organization.Email, $"Factuur {invoice.Number} · {invoice.Title}", $"<p>Beste klant,</p><p>Bijgevoegd vind je factuur {System.Net.WebUtility.HtmlEncode(invoice.Number)}. De vervaldatum is {invoice.DueDate:dd-MM-yyyy}.</p><p>David Coffa</p>", [new EmailAttachment($"factuur-{invoice.Number}.pdf", file, "application/pdf")]), ct);
            if (delivery.Sent) { invoice.SentAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync(ct); }
            await audit.RecordAsync(delivery.Sent ? "invoice.email_sent" : "invoice.email_development_outbox", "invoice", id.ToString(), organizationId: invoice.OrganizationId, cancellationToken: ct); return Results.Ok(delivery);
        });
        api.MapPost("/invoices/import", ImportInvoice);
        api.MapPost("/time-entries/invoice", async (TimeInvoiceRequest input, CommercialService service, CurrentActor actor, CancellationToken ct) => { Write(actor); return Results.Ok(InvoiceDto(await service.InvoiceTime(input, ct))); });
        api.MapPost("/invoices/from-time", async (FromTimeRequest input, CommercialService service, CurrentActor actor, CancellationToken ct) => { Write(actor); return Results.Ok(InvoiceDto(await service.InvoiceTime(new TimeInvoiceRequest(input.OrganizationId, input.TimeEntryIds, input.Title, input.InvoiceDate, input.DueDate), ct))); });
    }
    private static async Task<DocumentRecord> SaveQuotePdf(AppDbContext db, Quote quote, CommercialService service, CommercialPdf pdf, IObjectStorage storage, CurrentActor actor, AuditService audit, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct); await service.Lock($"coffa:quote-pdf:{quote.Id}", ct);
        var existing = await db.DocumentRecords.SingleOrDefaultAsync(x => x.QuoteId == quote.Id && x.Category == "Offerte PDF" && !x.IsArchived, ct);
        if (existing is not null)
        {
            if (existing.SharedWithCustomer != quote.SharedWithCustomer) { existing.SharedWithCustomer = quote.SharedWithCustomer; await db.SaveChangesAsync(ct); }
            await transaction.CommitAsync(ct);
            return existing;
        }
        var content = await pdf.Quote(quote, await service.Settings(ct), await service.Organization(quote.OrganizationId, ct), ct);
        var document = new DocumentRecord { OrganizationId = quote.OrganizationId, ProjectId = quote.ProjectId, QuoteId = quote.Id, OriginalName = $"offerte-{quote.Number}.pdf", ContentType = "application/pdf", SizeBytes = content.LongLength, Category = "Offerte PDF", SharedWithCustomer = quote.SharedWithCustomer, CreatedByUserId = actor.UserId };
        document.StorageKey = $"{quote.OrganizationId:N}/{document.Id:N}.pdf";
        await using var stream = new MemoryStream(content);
        await storage.PutAsync(document.StorageKey, stream, document.ContentType, ct);
        try { db.DocumentRecords.Add(document); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch { await storage.DeleteAsync(document.StorageKey, CancellationToken.None); throw; }
        await audit.RecordAsync("quote.pdf_generated", "quote", quote.Id.ToString(), organizationId: quote.OrganizationId, cancellationToken: ct);
        return document;
    }
    private static async Task<DocumentRecord> SaveInvoicePdf(AppDbContext db, Invoice invoice, CommercialService service, CommercialPdf pdf, IObjectStorage storage, CurrentActor actor, AuditService audit, CancellationToken ct)
    {
        if (invoice.OriginalDocumentId.HasValue)
            return await db.DocumentRecords.SingleAsync(x => x.Id == invoice.OriginalDocumentId && x.OrganizationId == invoice.OrganizationId, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct); await service.Lock($"coffa:invoice-pdf:{invoice.Id}", ct);
        var existing = await db.DocumentRecords.SingleOrDefaultAsync(x => x.InvoiceId == invoice.Id && x.Category == "Factuur PDF" && !x.IsArchived, ct);
        if (existing is not null)
        {
            if (existing.SharedWithCustomer != invoice.SharedWithCustomer) { existing.SharedWithCustomer = invoice.SharedWithCustomer; await db.SaveChangesAsync(ct); }
            await transaction.CommitAsync(ct);
            return existing;
        }
        var content = await pdf.Invoice(invoice, await service.Settings(ct), await service.Organization(invoice.OrganizationId, ct), ct);
        var document = new DocumentRecord { OrganizationId = invoice.OrganizationId, ProjectId = invoice.ProjectId, InvoiceId = invoice.Id, OriginalName = $"factuur-{invoice.Number}.pdf", ContentType = "application/pdf", SizeBytes = content.LongLength, Category = "Factuur PDF", SharedWithCustomer = invoice.SharedWithCustomer, CreatedByUserId = actor.UserId };
        document.StorageKey = $"{invoice.OrganizationId:N}/{document.Id:N}.pdf";
        await using var stream = new MemoryStream(content);
        await storage.PutAsync(document.StorageKey, stream, document.ContentType, ct);
        try { db.DocumentRecords.Add(document); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); }
        catch { await storage.DeleteAsync(document.StorageKey, CancellationToken.None); throw; }
        await audit.RecordAsync("invoice.pdf_generated", "invoice", invoice.Id.ToString(), organizationId: invoice.OrganizationId, cancellationToken: ct);
        return document;
    }
    private static async Task<byte[]> ReadStoredPdf(DocumentRecord document, IObjectStorage storage, CancellationToken ct)
    {
        await using var stream = await storage.OpenAsync(document.StorageKey, ct);
        using var buffer = new MemoryStream(); await stream.CopyToAsync(buffer, ct); return buffer.ToArray();
    }
    private static async Task<IResult> ImportInvoice(HttpRequest request, AppDbContext db, CurrentActor actor, CommercialService service, IObjectStorage storage, AuditService audit, CancellationToken ct)
    {
        Write(actor);
        if (!request.HasFormContentType || request.ContentLength > 12 * 1024 * 1024) throw new ApiException(400, "invalid_upload", "Upload een PDF van maximaal 10 MB.");
        var form = await request.ReadFormAsync(ct); var file = form.Files.GetFile("file");
        if (file is null || file.Length is < 5 or > 10 * 1024 * 1024 || !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) throw new ApiException(400, "invalid_pdf", "Upload een PDF van maximaal 10 MB.");
        var json = form["metadata"].FirstOrDefault();
        HistoricalInvoiceRequest? input;
        try { input = string.IsNullOrWhiteSpace(json) ? new HistoricalInvoiceRequest(Guid.Parse(form["organizationId"].ToString()), Guid.TryParse(form["projectId"], out var project) ? project : null, form["number"].ToString(), DateOnly.Parse(form["invoiceDate"].ToString(), CultureInfo.InvariantCulture), DateOnly.Parse(form["dueDate"].ToString(), CultureInfo.InvariantCulture), decimal.Parse(form["netAmount"].ToString(), CultureInfo.InvariantCulture), decimal.Parse(form["taxAmount"].ToString(), CultureInfo.InvariantCulture), decimal.Parse(form["totalAmount"].ToString(), CultureInfo.InvariantCulture), decimal.TryParse(form["paidAmount"], CultureInfo.InvariantCulture, out var paid) ? paid : 0, DateOnly.TryParse(form["paymentDate"], CultureInfo.InvariantCulture, out var paidDate) ? paidDate : null) : JsonSerializer.Deserialize<HistoricalInvoiceRequest>(json, CommercialCalculations.Json); }
        catch { throw new ApiException(400, "invalid_import", "Controleer de factuurgegevens."); }
        if (input is null) throw new ApiException(400, "invalid_import", "Vul de factuurgegevens in.");
        CommercialService.Text(input.Number, 64, "het oorspronkelijke factuurnummer", true); CommercialService.Dates(input.InvoiceDate, input.DueDate);
        if (input.NetAmount < 0 || input.TaxAmount < 0 || input.TotalAmount <= 0 || input.PaidAmount < 0 || input.PaidAmount > input.TotalAmount || CommercialCalculations.Round(input.NetAmount + input.TaxAmount) != input.TotalAmount || new[] { input.NetAmount, input.TaxAmount, input.TotalAmount, input.PaidAmount }.Any(x => x != CommercialCalculations.Round(x))) throw new ApiException(400, "invalid_import_amounts", "Subtotaal plus btw moet gelijk zijn aan het totaal. Controleer ook de ontvangen betalingen.");
        if (input.PaidAmount > 0 && (!input.PaymentDate.HasValue || input.PaymentDate.Value > CommercialService.Today || input.PaymentDate.Value.Year < 1900)) throw new ApiException(400, "payment_date_required", "Vul bij een ontvangen bedrag ook de werkelijke betaaldatum in.");
        var organization = await service.Organization(input.OrganizationId, ct); await service.CheckProject(input.ProjectId, input.OrganizationId, ct);
        var number = input.Number.Trim().ToUpperInvariant();
        await using var tx = await db.Database.BeginTransactionAsync(ct); await service.Lock("coffa:commercial:issue", ct);
        if (await db.Invoices.AnyAsync(x => x.Number == number, ct)) throw new ApiException(409, "duplicate_invoice", "Dit factuurnummer is al geregistreerd.");
        await using var source = file.OpenReadStream(); var signature = new byte[5]; if (await source.ReadAsync(signature, ct) != 5 || Encoding.ASCII.GetString(signature) != "%PDF-") throw new ApiException(400, "invalid_pdf", "Dit bestand is geen PDF."); source.Position = 0;
        var invoice = new Invoice { OrganizationId = input.OrganizationId, ProjectId = input.ProjectId, Number = number, Title = $"Historische factuur {number}", Kind = "historical", Status = "issued", InvoiceDate = input.InvoiceDate, DueDate = input.DueDate, NetAmount = input.NetAmount, TaxAmount = input.TaxAmount, TotalAmount = input.TotalAmount, IssuedAt = DateTimeOffset.UtcNow, CustomerSnapshot = JsonSerializer.Serialize(new { organization.Name, organization.Address, organization.Email, organization.ChamberOfCommerce, organization.VatNumber }, CommercialCalculations.Json), IssuerSnapshot = JsonSerializer.Serialize(await service.Settings(ct), CommercialCalculations.Json) };
        var document = new DocumentRecord { OrganizationId = input.OrganizationId, ProjectId = input.ProjectId, InvoiceId = invoice.Id, OriginalName = Path.GetFileName(file.FileName), ContentType = "application/pdf", StorageKey = $"invoices/{input.OrganizationId}/{Guid.NewGuid():N}.pdf", Category = "invoice", CreatedByUserId = actor.UserId, SizeBytes = file.Length };
        invoice.OriginalDocumentId = document.Id;
        if (input.PaidAmount > 0) invoice.Payments.Add(new InvoicePayment { InvoiceId = invoice.Id, Amount = input.PaidAmount, Date = input.PaymentDate!.Value, Reference = "Bij historische import geregistreerd", IdempotencyKey = Guid.NewGuid() });
        var uploaded = false;
        try
        {
            await storage.PutAsync(document.StorageKey, source, "application/pdf", ct); uploaded = true;
            db.DocumentRecords.Add(document); db.Invoices.Add(invoice); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        catch { if (uploaded) { try { await storage.DeleteAsync(document.StorageKey, CancellationToken.None); } catch { } } throw; }
        await audit.RecordAsync("invoice.historical_imported", "invoice", invoice.Id.ToString(), organizationId: invoice.OrganizationId, cancellationToken: ct);
        return Results.Ok(InvoiceDto(invoice));
    }
    private static void MapSubscriptions(RouteGroupBuilder api)
    {
        api.MapGet("/subscriptions", async (AppDbContext db, CurrentActor actor, Guid? organizationId, string? status, string? search, int? page, int? pageSize, CancellationToken ct) =>
        {
            actor.DemandPermission("finance.read"); var query = db.Subscriptions.AsNoTracking().AsQueryable();
            if (organizationId.HasValue) query = query.Where(x => x.OrganizationId == organizationId); if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status); if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => EF.Functions.ILike(x.Name, "%" + search + "%"));
            var p = Math.Max(1, page ?? 1); var size = Math.Clamp(pageSize ?? 20, 1, 100); var total = await query.CountAsync(ct);
            return Results.Ok(new { items = await query.OrderBy(x => x.NextInvoiceDate).Skip((p - 1) * size).Take(size).ToListAsync(ct), total, page = p, pageSize = size });
        });
        api.MapGet("/subscriptions/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) => { actor.DemandPermission("finance.read"); return Results.Ok(await db.Subscriptions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "not_found", "Abonnement niet gevonden.")); });
        api.MapPost("/subscriptions", async (SubscriptionRequest input, CommercialService service, CurrentActor actor, CancellationToken ct) => { Write(actor); return Results.Ok(await service.SaveSubscription(input, null, ct)); });
        api.MapPut("/subscriptions/{id:guid}", async (Guid id, SubscriptionRequest input, CommercialService service, CurrentActor actor, CancellationToken ct) => { Write(actor); return Results.Ok(await service.SaveSubscription(input, id, ct)); });
        api.MapPost("/subscriptions/generate", async (CommercialService service, CurrentActor actor, CancellationToken ct) => { Write(actor); return Results.Ok(new { generated = await service.GenerateRecurring(CommercialService.Today, ct) }); });
        api.MapGet("/subscriptions/summary", async (AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        {
            actor.DemandPermission("finance.read"); var values = await db.Subscriptions.AsNoTracking().ToListAsync(ct); var active = values.Where(x => x.Status == "active" && x.StartDate <= CommercialService.Today && (!x.EndDate.HasValue || x.EndDate >= CommercialService.Today)).ToArray(); var mrr = CommercialCalculations.Round(active.Sum(CommercialCalculations.MonthlyAmount));
            return Results.Ok(new { mrr, arr = mrr * 12, activeContracts = active.Length, endedContracts = values.Count(x => x.Status == "ended" || x.EndDate < CommercialService.Today), upcoming = active.Where(x => x.AutomaticInvoicing).OrderBy(x => x.NextInvoiceDate).Take(10) });
        });
    }
    private static (DateOnly From, DateOnly To) Period(DateOnly? from, DateOnly? to)
    {
        var month = FinancialMonth.At(DateTimeOffset.UtcNow); var first = from ?? month.Start; var last = to ?? month.End.AddDays(-1);
        if (last < first || first.Year < 1900 || last.Year > 2200 || last.DayNumber - first.DayNumber > 3660) throw new ApiException(400, "invalid_period", "Kies een geldige periode van maximaal tien jaar."); return (first, last);
    }
    private static void MapReports(RouteGroupBuilder api)
    {
        api.MapGet("/organizations/{id:guid}/commercial-summary", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) =>
        {
            actor.DemandPermission("finance.read");
            if (!await db.Organizations.AnyAsync(x => x.Id == id && !x.IsArchived, ct)) throw new ApiException(404, "not_found", "Klant niet gevonden.");
            var invoices = await db.Invoices.AsNoTracking().Include(x => x.Payments).Where(x => x.OrganizationId == id && x.Status != "draft").ToListAsync(ct);
            var credits = await Credits(db, invoices.Select(x => x.Id), ct);
            return Results.Ok(new { invoicedRevenue = invoices.Sum(x => x.NetAmount), receivedPayments = invoices.Sum(x => x.Payments.Sum(p => p.Amount)), outstandingAmount = invoices.Where(x => x.Kind != "credit").Sum(x => CommercialCalculations.Outstanding(x, credits.GetValueOrDefault(x.Id))) });
        });
        api.MapGet("/finance", async (AppDbContext db, CurrentActor actor, DateOnly? from, DateOnly? to, bool? financialMonth, CancellationToken ct) =>
        {
            actor.DemandPermission("finance.read"); var period = Period(from, to); return Results.Ok(await FinancialReport(db, period.From, period.To, ct, financialMonth ?? (!from.HasValue && !to.HasValue)));
        });
        api.MapGet("/finance/export", async (AppDbContext db, CurrentActor actor, DateOnly? from, DateOnly? to, bool? financialMonth, CancellationToken ct) =>
        {
            actor.DemandPermission("finance.read"); var period = Period(from, to); var invoices = await db.Invoices.AsNoTracking().Include(x => x.Payments).Where(x => x.Status != "draft" && x.InvoiceDate >= period.From && x.InvoiceDate < period.To.AddDays(1)).OrderBy(x => x.InvoiceDate).ToListAsync(ct); var credits = await Credits(db, invoices.Select(x => x.Id), ct);
            static string Cell(string value) { var sanitized = value.Length > 0 && "=+-@\t\r".Contains(value[0]) ? "'" + value : value; return "\"" + sanitized.Replace("\"", "\"\"") + "\""; }
            var output = new StringBuilder("Factuurnummer;Soort;Datum;KlantId;ProjectId;Excl btw;Btw;Totaal;Ontvangen;Openstaand\r\n");
            foreach (var invoice in invoices) output.AppendLine(string.Join(';', Cell(invoice.Number ?? ""), Cell(invoice.Kind), invoice.InvoiceDate.ToString("yyyy-MM-dd"), invoice.OrganizationId, invoice.ProjectId?.ToString() ?? "", invoice.NetAmount.ToString("0.00", CultureInfo.InvariantCulture), invoice.TaxAmount.ToString("0.00", CultureInfo.InvariantCulture), invoice.TotalAmount.ToString("0.00", CultureInfo.InvariantCulture), invoice.Payments.Sum(x => x.Amount).ToString("0.00", CultureInfo.InvariantCulture), CommercialCalculations.Outstanding(invoice, credits.GetValueOrDefault(invoice.Id)).ToString("0.00", CultureInfo.InvariantCulture)));
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(output.ToString())).ToArray(), "text/csv; charset=utf-8", $"coffa-financieel-{period.From:yyyyMMdd}-{period.To:yyyyMMdd}.csv");
        });
        api.MapGet("/dashboard", Dashboard);
    }
    public static async Task<object> FinancialReport(AppDbContext db, DateOnly from, DateOnly to, CancellationToken ct, bool financialMonth = false)
    {
        var endExclusive = to.AddDays(1);
        var invoices = await db.Invoices.AsNoTracking().Include(x => x.Payments).Where(x => x.Status != "draft").ToListAsync(ct); var credits = await Credits(db, invoices.Select(x => x.Id), ct);
        var selected = invoices.Where(x => x.InvoiceDate >= from && x.InvoiceDate < endExclusive).ToArray();
        var expenses = await db.Expenses.AsNoTracking().Where(x => !x.IsArchived && x.Date >= from && x.Date < endExclusive).OrderBy(x => x.Date).ToListAsync(ct);
        var duration = to.DayNumber - from.DayNumber + 1; var previousTo = from.AddDays(-1); var previousFrom = financialMonth ? from.AddMonths(-1) : previousTo.AddDays(1 - duration);
        var payments = invoices.SelectMany(x => x.Payments).ToArray();
        var allSubs = await db.Subscriptions.AsNoTracking().Where(x => x.Status == "active" && x.StartDate <= to && (!x.EndDate.HasValue || x.EndDate >= to)).ToListAsync(ct);
        var mrr = CommercialCalculations.Round(allSubs.Sum(CommercialCalculations.MonthlyAmount));
        var vat = selected.SelectMany(x => CommercialCalculations.Lines(x.LinesJson)).GroupBy(x => x.TaxRate).Select(g => new { taxRate = g.Key, netAmount = g.Sum(x => x.NetAmount), taxAmount = g.Sum(x => x.TaxAmount) }).ToArray();
        var historical = selected.Where(x => x.Kind == "historical").ToArray();
        var revenue = selected.Sum(x => x.NetAmount); var received = payments.Where(x => x.Date >= from && x.Date < endExclusive).Sum(x => x.Amount);
        var outstanding = invoices.Where(x => x.Kind != "credit").Sum(x => CommercialCalculations.Outstanding(x, credits.GetValueOrDefault(x.Id)));
        var overdue = invoices.Where(x => x.Kind != "credit" && x.DueDate < CommercialService.Today).Sum(x => CommercialCalculations.Outstanding(x, credits.GetValueOrDefault(x.Id)));
        var series = new List<object>(); var cursor = financialMonth ? FinancialMonth.ForDate(from).Start : new DateOnly(from.Year, from.Month, 1);
        while (cursor <= to) { var next = cursor.AddMonths(1); series.Add(new { period = financialMonth ? $"{cursor:dd-MM-yyyy} – {next.AddDays(-1):dd-MM-yyyy}" : cursor.ToString("yyyy-MM"), invoiced = selected.Where(x => x.InvoiceDate >= cursor && x.InvoiceDate < next).Sum(x => x.NetAmount), received = payments.Where(x => x.Date >= from && x.Date < endExclusive && x.Date >= cursor && x.Date < next).Sum(x => x.Amount), costs = expenses.Where(x => x.Date >= cursor && x.Date < next).Sum(x => x.NetAmount) }); cursor = next; }
        var organizationNames = await db.Organizations.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var projectNames = await db.Projects.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        return new { from, to, invoicedRevenue = revenue, receivedPayments = received, expenses = expenses.Sum(x => x.NetAmount), expenseTax = expenses.Sum(x => x.TaxAmount), outstandingAmount = outstanding, overdueAmount = overdue, taxAmount = selected.Sum(x => x.TaxAmount), mrr, arr = mrr * 12, previous = new { from = previousFrom, to = previousTo, invoicedRevenue = invoices.Where(x => x.InvoiceDate >= previousFrom && x.InvoiceDate < from).Sum(x => x.NetAmount), receivedPayments = payments.Where(x => x.Date >= previousFrom && x.Date < from).Sum(x => x.Amount) }, revenueSeries = series, vatOverview = vat, importedVat = new { netAmount = historical.Sum(x => x.NetAmount), taxAmount = historical.Sum(x => x.TaxAmount), note = "Historische import bevat totaal-btw; er is geen tariefverdeling afgeleid." }, revenueByCustomer = selected.GroupBy(x => x.OrganizationId).Select(g => new { organizationId = g.Key, organizationName = organizationNames.GetValueOrDefault(g.Key, "Onbekende klant"), revenue = g.Sum(x => x.NetAmount) }), revenueByProject = selected.GroupBy(x => x.ProjectId).Select(g => new { projectId = g.Key, projectName = g.Key.HasValue ? projectNames.GetValueOrDefault(g.Key.Value, "Onbekend project") : null, revenue = g.Sum(x => x.NetAmount) }) };
    }
    private static async Task<IResult> Dashboard(AppDbContext db, CurrentActor actor, CancellationToken ct)
    {
        var month = FinancialMonth.At(DateTimeOffset.UtcNow); var monthStart = month.Start; var monthEnd = month.End.AddDays(-1);
        var activityStart = month.StartInstant; var activityEnd = month.EndInstant;
        if (actor.IsCustomer)
        {
            if (!actor.OrganizationId.HasValue) throw new ApiException(403, "organization_required", "Dit account heeft geen klantorganisatie.");
            var organizationId = actor.OrganizationId.Value;
            var tasksQuery = db.WorkTasks.AsNoTracking().Where(x => x.OrganizationId == organizationId && x.SharedWithCustomer && !x.IsArchived && x.Status != "done" && (x.Status == "in_progress" || x.Status == "review" || (x.DueDate.HasValue && x.DueDate <= monthEnd)) && (!x.ProjectId.HasValue || db.Projects.Any(p => p.Id == x.ProjectId && p.OrganizationId == organizationId && p.SharedWithCustomer && !p.IsArchived)));
            var tasks = await tasksQuery.OrderBy(x => x.DueDate).Take(10).Select(x => new { x.Id, x.Title, x.Status, x.DueDate, x.ProjectId }).ToListAsync(ct);
            var customerActivities = await db.UserNotifications.AsNoTracking().Where(x => x.UserId == actor.UserId && x.CreatedAt >= activityStart && x.CreatedAt < activityEnd).OrderByDescending(x => x.CreatedAt).Take(8).Select(x => new { x.Id, action = x.Message, entityType = "ticket", entityId = x.TicketId, x.CreatedAt }).ToListAsync(ct);
            return Results.Ok(new { period = new { from = month.Start, to = monthEnd }, kpis = new { activeProjects = await db.Projects.CountAsync(x => x.OrganizationId == organizationId && x.SharedWithCustomer && !x.IsArchived && x.Status != "completed" && x.Status != "archived", ct), openTickets = await db.Tickets.CountAsync(x => x.OrganizationId == organizationId && !x.IsArchived && x.Status != "closed" && x.Status != "resolved", ct), openTasks = await tasksQuery.CountAsync(ct) }, activities = customerActivities, tasks });
        }
        var kpis = new Dictionary<string, object>();
        if (actor.HasPermission("finance.read"))
        {
            kpis["monthlyRevenue"] = await db.Invoices.AsNoTracking().Where(x => x.Status != "draft" && x.InvoiceDate >= monthStart && x.InvoiceDate < month.End).SumAsync(x => x.NetAmount, ct);
            kpis["monthlyCosts"] = await db.Expenses.AsNoTracking().Where(x => !x.IsArchived && x.Date >= monthStart && x.Date < month.End).SumAsync(x => x.NetAmount, ct);
        }
        if (actor.HasPermission("projects.read")) kpis["activeProjects"] = await db.Projects.CountAsync(x => !x.IsArchived && x.Status != "completed" && x.Status != "archived", ct);
        if (actor.HasPermission("tickets.read")) kpis["openTickets"] = await db.Tickets.CountAsync(x => !x.IsArchived && x.Status != "closed" && x.Status != "resolved", ct);
        var myTasksQuery = db.WorkTasks.AsNoTracking().Where(x => !x.IsArchived && x.AssignedToUserId == actor.UserId && (x.Status == "in_progress" || x.Status == "review" || (x.DueDate.HasValue && x.DueDate <= monthEnd)) && x.Status != "done");
        var myTasks = actor.HasPermission("projects.read") ? await myTasksQuery.OrderBy(x => x.DueDate).Take(10).Select(x => new { x.Id, x.Title, x.Status, x.DueDate, x.ProjectId }).ToListAsync(ct) : null;
        if (actor.HasPermission("projects.read")) kpis["openTasks"] = await myTasksQuery.CountAsync(ct);
        var activityQuery = db.AuditEntries.AsNoTracking().AsQueryable();
        if (!actor.IsAdmin)
        {
            var crm = actor.HasPermission("crm.read"); var projects = actor.HasPermission("projects.read"); var tickets = actor.HasPermission("tickets.read"); var documents = actor.HasPermission("documents.read"); var finance = actor.HasPermission("finance.read");
            activityQuery = activityQuery.Where(x => (crm && (x.EntityType == "organization" || x.EntityType == "lead")) || (projects && (x.EntityType == "project" || x.EntityType == "task")) || (tickets && x.EntityType == "ticket") || (documents && x.EntityType == "document") || (finance && (x.EntityType == "invoice" || x.EntityType == "quote" || x.EntityType == "expense")));
        }
        var activities = await activityQuery.Where(x => x.CreatedAt >= activityStart && x.CreatedAt < activityEnd).OrderByDescending(x => x.CreatedAt).Take(8).Select(x => new { x.Id, x.Action, x.EntityType, x.EntityId, x.CreatedAt }).ToListAsync(ct);
        return Results.Ok(new { period = new { from = month.Start, to = monthEnd }, kpis, activities, tasks = myTasks });
    }
}
