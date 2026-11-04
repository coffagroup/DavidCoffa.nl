using Coffa.Api.Data;
using Coffa.Api.Domain;
using Coffa.Api.Infrastructure;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace Coffa.Api.Endpoints;

public static partial class OperationsEndpoints
{
    public sealed record DocumentInput(string? Category, bool SharedWithCustomer);
    private const long MaximumDocumentBytes = 20 * 1024 * 1024;
    private static IQueryable<DocumentRecord> AuthorizedDocuments(AppDbContext db, CurrentActor actor, string permission = "documents.read")
    {
        ReadAccess(actor, permission);
        var query = db.DocumentRecords.Where(x => !x.IsArchived && db.Organizations.Any(o => o.Id == x.OrganizationId && !o.IsArchived));
        if (actor.IsCustomer)
            query = query.Where(x => x.OrganizationId == actor.OrganizationId && x.SharedWithCustomer
                && (x.ProjectId == null || db.Projects.Any(p => p.Id == x.ProjectId && p.OrganizationId == x.OrganizationId && p.SharedWithCustomer && !p.IsArchived))
                && (x.TicketId == null || db.Tickets.Any(t => t.Id == x.TicketId && t.OrganizationId == x.OrganizationId && !t.IsArchived))
                && (x.InvoiceId == null || db.Invoices.Any(i => i.Id == x.InvoiceId && i.OrganizationId == x.OrganizationId && i.SharedWithCustomer && i.Status != "draft"))
                && (x.QuoteId == null || db.Quotes.Any(q => q.Id == x.QuoteId && q.OrganizationId == x.OrganizationId && q.SharedWithCustomer && q.Status != "draft")));
        else
        {
            if (!actor.HasPermission("finance.read")) query = query.Where(x => x.InvoiceId == null);
            if (!actor.HasPermission("finance.read")) query = query.Where(x => x.QuoteId == null);
            if (permission == "tickets.read") query = query.Where(x => x.TicketId != null);
        }
        return query;
    }
    private static object DocumentDto(DocumentRecord document) => new { document.Id, document.OrganizationId, document.ProjectId, document.QuoteId, document.InvoiceId, document.TicketId, document.OriginalName, document.ContentType, document.SizeBytes, document.Category, document.SharedWithCustomer, document.CreatedAt };
    private static void MapDocuments(RouteGroupBuilder api)
    {
        api.MapGet("/documents", async (AppDbContext db, CurrentActor actor, HttpRequest request, CancellationToken ct) =>
        {
            var query = AuthorizedDocuments(db, actor).AsNoTracking();
            if (QueryId(request, "organizationId") is { } orgId) { actor.DemandOrganization(orgId); query = query.Where(x => x.OrganizationId == orgId); }
            if (QueryId(request, "projectId") is { } projectId) query = query.Where(x => x.ProjectId == projectId);
            if (QueryId(request, "ticketId") is { } ticketId) query = query.Where(x => x.TicketId == ticketId);
            if (QueryId(request, "quoteId") is { } quoteId) query = query.Where(x => x.QuoteId == quoteId);
            if (QueryId(request, "invoiceId") is { } invoiceId) query = query.Where(x => x.InvoiceId == invoiceId);
            if (Query(request, "category") is { } category) query = query.Where(x => x.Category == category);
            if (Query(request, "q") is { } q) query = query.Where(x => EF.Functions.ILike(x.OriginalName, "%" + q + "%") || EF.Functions.ILike(x.Category, "%" + q + "%"));
            return await Page(query.OrderByDescending(x => x.CreatedAt).Select(x => new { x.Id, x.OrganizationId, x.ProjectId, x.QuoteId, x.InvoiceId, x.TicketId, x.OriginalName, x.ContentType, x.SizeBytes, x.Category, x.SharedWithCustomer, x.CreatedAt }), request, ct);
        });
        api.MapGet("/documents/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, CancellationToken ct) =>
            Results.Ok(DocumentDto(await AuthorizedDocuments(db, actor).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing())));
        api.MapGet("/documents/{id:guid}/download", async (Guid id, AppDbContext db, CurrentActor actor, IObjectStorage storage, AuditService audit, HttpResponse response, CancellationToken ct) =>
        {
            var permission = !actor.IsCustomer && !actor.HasPermission("documents.read") && actor.HasPermission("tickets.read") ? "tickets.read" : "documents.read";
            var document = await AuthorizedDocuments(db, actor, permission).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
            var stream = await storage.OpenAsync(document.StorageKey, ct);
            await audit.RecordAsync("document.downloaded", "document", id.ToString(), organizationId: document.OrganizationId, cancellationToken: ct);
            response.Headers["X-Content-Type-Options"] = "nosniff"; response.Headers.CacheControl = "private, no-store";
            return Results.File(stream, document.ContentType, fileDownloadName: document.OriginalName);
        });
        api.MapPost("/documents", async (HttpRequest request, AppDbContext db, CurrentActor actor, IObjectStorage storage, AuditService audit, CancellationToken ct) =>
        {
            if (!request.HasFormContentType) throw new ApiException(415, "multipart_required", "Upload het bestand als multipart/form-data.");
            if (request.ContentLength > MaximumDocumentBytes + 131072) throw new ApiException(413, "file_too_large", "Het bestand mag maximaal 20 MiB groot zijn.");
            IFormCollection form;
            try { form = await request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = MaximumDocumentBytes + 131072, ValueLengthLimit = 4000 }, ct); }
            catch (InvalidDataException) { throw new ApiException(400, "invalid_upload", "De upload kon niet worden verwerkt. Gebruik één bestand van maximaal 20 MiB."); }
            var file = form.Files.GetFile("file");
            if (file == null || form.Files.Count != 1 || file.Length is < 1 or > MaximumDocumentBytes) throw new ApiException(400, "invalid_file", "Kies één bestand van maximaal 20 MiB.");
            Guid? Id(string name) => string.IsNullOrWhiteSpace(form[name]) ? null : Guid.TryParse(form[name], out var value) ? value : throw new ApiException(400, "validation", "Ongeldige bestandskoppeling.");
            var orgId = Id("organizationId") ?? (actor.IsCustomer ? actor.OrganizationId : null) ?? throw new ApiException(400, "validation", "Kies een organisatie.");
            var projectId = Id("projectId"); var ticketId = Id("ticketId"); var quoteId = Id("quoteId"); var invoiceId = Id("invoiceId");
            var writePermission = "documents.write";
            if (!actor.IsCustomer && !actor.HasPermission("documents.write"))
            {
                actor.DemandPermission("tickets.write");
                if (ticketId == null || invoiceId != null || quoteId != null) Forbidden();
                writePermission = "tickets.write";
            }
            await OrganizationAccess(db, actor, orgId, writePermission, ct); await CheckProject(db, actor, orgId, projectId, ct);
            if (actor.IsCustomer && (ticketId == null || quoteId != null || invoiceId != null)) Forbidden();
            if (ticketId is { } tid && !await db.Tickets.AnyAsync(x => x.Id == tid && x.OrganizationId == orgId && !x.IsArchived, ct)) throw Missing();
            if (quoteId is { } qid) { actor.DemandPermission("finance.write"); if (!await db.Quotes.AnyAsync(x => x.Id == qid && x.OrganizationId == orgId, ct)) throw Missing(); }
            if (invoiceId is { } iid) { actor.DemandPermission("finance.write"); if (!await db.Invoices.AnyAsync(x => x.Id == iid && x.OrganizationId == orgId, ct)) throw Missing(); }
            await using var content = new MemoryStream(); await file.CopyToAsync(content, ct);
            var (fileName, contentType) = ValidateFile(file.FileName, content.ToArray()); content.Position = 0;
            var document = new DocumentRecord { OrganizationId = orgId, ProjectId = projectId, TicketId = ticketId, QuoteId = quoteId, InvoiceId = invoiceId, OriginalName = fileName, ContentType = contentType, SizeBytes = content.Length, Category = Text(form["category"], 80) is { Length: > 0 } category ? category : "general", SharedWithCustomer = actor.IsCustomer || bool.TryParse(form["sharedWithCustomer"], out var shared) && shared, CreatedByUserId = actor.UserId };
            document.StorageKey = $"{orgId:N}/{document.Id:N}{Path.GetExtension(fileName).ToLowerInvariant()}";
            await storage.PutAsync(document.StorageKey, content, contentType, ct);
            try { db.DocumentRecords.Add(document); await db.SaveChangesAsync(ct); }
            catch { await storage.DeleteAsync(document.StorageKey, CancellationToken.None); throw; }
            await audit.RecordAsync("document.uploaded", "document", document.Id.ToString(), organizationId: orgId, cancellationToken: ct);
            return Results.Created($"/api/documents/{document.Id}", DocumentDto(document));
        });
        api.MapPut("/documents/{id:guid}", async (Guid id, DocumentInput input, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("documents.write"); var document = await AuthorizedDocuments(db, actor).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing();
            document.Category = Text(input.Category, 80) is { Length: > 0 } category ? category : "general"; document.SharedWithCustomer = input.SharedWithCustomer; await db.SaveChangesAsync(ct);
            await audit.RecordAsync("document.updated", "document", id.ToString(), organizationId: document.OrganizationId, cancellationToken: ct); return Results.Ok(DocumentDto(document));
        });
        api.MapDelete("/documents/{id:guid}", async (Guid id, AppDbContext db, CurrentActor actor, AuditService audit, CancellationToken ct) =>
        {
            actor.DemandPermission("documents.write"); var document = await AuthorizedDocuments(db, actor).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw Missing(); document.IsArchived = true; await db.SaveChangesAsync(ct);
            await audit.RecordAsync("document.archived", "document", id.ToString(), organizationId: document.OrganizationId, cancellationToken: ct); return Results.NoContent();
        });
    }
    internal static (string FileName, string ContentType) ValidateFile(string originalName, byte[] content)
    {
        var name = Path.GetFileName(originalName.Replace('\\', '/'));
        if (name.Length == 0 || name.Length > 200 || name.Any(char.IsControl)) throw new ApiException(400, "invalid_filename", "Gebruik een geldige bestandsnaam van maximaal 200 tekens.");
        var extension = Path.GetExtension(name).ToLowerInvariant();
        bool Starts(params byte[] prefix) => content.AsSpan().StartsWith(prefix);
        var mime = extension switch
        {
            ".pdf" when Starts(0x25, 0x50, 0x44, 0x46, 0x2d) => "application/pdf",
            ".png" when Starts(0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a) => "image/png",
            ".jpg" or ".jpeg" when Starts(0xff, 0xd8, 0xff) => "image/jpeg",
            ".webp" when content.Length > 12 && Starts(0x52, 0x49, 0x46, 0x46) && content.AsSpan(8, 4).SequenceEqual("WEBP"u8) => "image/webp",
            ".docx" when ValidOfficeArchive(content, "word/") => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" when ValidOfficeArchive(content, "xl/") => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".txt" or ".csv" when IsText(content) => extension == ".txt" ? "text/plain" : "text/csv",
            _ => throw new ApiException(400, "unsupported_file", "Gebruik PDF, PNG, JPEG, WebP, tekst, CSV, DOCX of XLSX. Het bestand moet bij het gekozen type passen.")
        };
        return (name, mime);
    }
    private static bool IsText(byte[] content)
    {
        if (content.Contains((byte)0)) return false;
        try { _ = new System.Text.UTF8Encoding(false, true).GetString(content); return true; } catch (System.Text.DecoderFallbackException) { return false; }
    }
    private static bool ValidOfficeArchive(byte[] content, string directory)
    {
        try
        {
            using var stream = new MemoryStream(content); using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
            if (archive.Entries.Count > 2000 || archive.Entries.Sum(x => x.Length) > MaximumDocumentBytes * 10 || archive.Entries.Any(x => x.FullName.Contains("..") || x.FullName.EndsWith("vbaProject.bin", StringComparison.OrdinalIgnoreCase))) return false;
            return archive.Entries.Any(x => x.FullName == "[Content_Types].xml") && archive.Entries.Any(x => x.FullName.StartsWith(directory, StringComparison.Ordinal));
        }
        catch (InvalidDataException) { return false; }
    }
}
