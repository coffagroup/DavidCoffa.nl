using System.Globalization;
using System.Text.Json;
using Coffa.Api.Data;
using Coffa.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Coffa.Api.Assistant;

// AI proposes values. This layer discards unknown fields and resolves references
// locally; the normal business endpoints remain the only writers and validators.
public sealed class AssistantPlanner(AppDbContext db)
{
    private static readonly Dictionary<string, string[]> Fields = new()
    {
        ["create_organization"] = ["name", "email", "phone", "website", "address", "chamberOfCommerce", "vatNumber", "type", "status", "tags", "internalNotes"],
        ["create_contact"] = ["name", "email", "phone", "jobTitle", "isPrimary"],
        ["create_lead"] = ["companyName", "contactName", "email", "phone", "website", "address", "stage", "expectedValue", "expectedCloseDate", "notes", "tags"],
        ["create_note"] = ["title", "body"],
        ["create_project"] = ["name", "description", "status", "priority", "startDate", "deadline", "budget", "estimatedHours", "repositoryUrl", "deploymentUrl"],
        ["create_task"] = ["title", "description", "status", "priority", "dueDate"],
        ["create_time_entry"] = ["description", "workDate", "durationMinutes", "isBillable", "hourlyRate"],
        ["create_ticket"] = ["title", "description", "category", "priority", "status"],
        ["create_quote"] = ["title", "notes", "terms", "validUntil"],
        ["create_invoice"] = ["title", "notes", "terms", "invoiceDate", "dueDate"],
        ["create_subscription"] = ["name", "description", "amount", "taxRate", "frequency", "startDate", "endDate", "nextInvoiceDate", "status"]
    };
    private static readonly HashSet<string> Numbers = ["expectedValue", "budget", "estimatedHours", "durationMinutes", "hourlyRate", "amount", "taxRate"];
    private static readonly HashSet<string> Dates = ["expectedCloseDate", "startDate", "deadline", "dueDate", "workDate", "validUntil", "invoiceDate", "endDate", "nextInvoiceDate"];
    private static readonly HashSet<string> Booleans = ["isPrimary", "isBillable"];
    private static readonly Dictionary<string, string[]> Choices = new()
    {
        ["priority"] = ["low", "normal", "high", "urgent"], ["frequency"] = ["monthly", "quarterly", "yearly"],
        ["stage"] = ["new", "contacted", "meeting", "prepare_quote", "quote_sent", "negotiation", "won", "lost"],
        ["category"] = ["general", "website", "hosting", "bug", "change", "billing", "other"]
    };
    private static string Short(string? value, int max = 1000)
    {
        var text = (value ?? "").Trim();
        return text.Length <= max ? text : throw InvalidOutput();
    }
    private static ApiException InvalidOutput() => new(502, "assistant_invalid_output", "De assistent gaf een onbruikbaar voorstel. Er is niets opgeslagen. Probeer een duidelijkere opdracht.");
    public async Task<AssistantPlan> PrepareAsync(AssistantModelPlan plan, DateOnly today, int paymentTermDays, CancellationToken ct)
    {
        if (plan.Actions is null || plan.Questions is null || plan.Actions.Count > 12 || plan.Questions.Count > 20) throw InvalidOutput();
        var actions = new List<AssistantAction>();
        var questions = plan.Questions.Select(x => Short(x)).Where(x => x.Length > 0).Distinct().ToList();
        foreach (var action in plan.Actions)
        {
            if (action is null || action.Kind is null || !Fields.TryGetValue(action.Kind, out var permitted) || action.Fields is null || action.Lines is null || action.Warnings is null || action.Fields.Count > 40 || action.Lines.Count > 200 || action.Warnings.Count > 20) throw InvalidOutput();
            var initial = new Dictionary<string, object?>();
            var warnings = action.Warnings.Select(x => Short(x)).Where(x => x.Length > 0).ToList();
            foreach (var field in action.Fields)
            {
                if (field is null || field.Name is null || !permitted.Contains(field.Name) || field.Value.ValueKind == JsonValueKind.Null) continue;
                var value = Clean(field.Name, field.Value);
                if (value is not null) initial[field.Name] = value;
                else warnings.Add($"Een ongeldige waarde voor {field.Name} is weggelaten. Controleer dit veld.");
            }
            var orgName = NullName(action.OrganizationName); var projectName = NullName(action.ProjectName);
            if (action.Kind is not ("create_organization" or "create_lead"))
            {
                if (orgName is not null)
                {
                    var normalized = orgName.ToLowerInvariant();
                    var matches = await db.Organizations.AsNoTracking().Where(x => !x.IsArchived && x.Name.ToLower() == normalized).Select(x => x.Id).Take(2).ToListAsync(ct);
                    if (matches.Count == 1) initial["organizationId"] = matches[0];
                    else warnings.Add(matches.Count > 1 ? $"Er zijn meerdere klanten met de naam '{orgName}'. Kies de juiste organisatie." : $"Klant '{orgName}' is niet eenduidig gevonden. Maak deze eerst aan of kies de juiste organisatie.");
                }
                else warnings.Add("Kies de klantorganisatie bij dit voorstel.");
                if (projectName is not null && initial.TryGetValue("organizationId", out var organization) && organization is Guid orgId)
                {
                    var normalized = projectName.ToLowerInvariant();
                    var matches = await db.Projects.AsNoTracking().Where(x => !x.IsArchived && x.OrganizationId == orgId && x.Name.ToLower() == normalized).Select(x => x.Id).Take(2).ToListAsync(ct);
                    if (matches.Count == 1) initial["projectId"] = matches[0];
                    else warnings.Add($"Project '{projectName}' is niet eenduidig gevonden bij deze klant. Kies het project zelf.");
                }
            }
            if (action.Kind is "create_invoice" or "create_quote")
            {
                initial["sharedWithCustomer"] = false;
                if (action.Kind == "create_invoice")
                {
                    initial.TryAdd("invoiceDate", today.ToString("yyyy-MM-dd"));
                    var start = DateOnly.Parse((string)initial["invoiceDate"]!, CultureInfo.InvariantCulture);
                    initial.TryAdd("dueDate", start.AddDays(paymentTermDays).ToString("yyyy-MM-dd"));
                }
                else initial.TryAdd("validUntil", today.AddDays(30).ToString("yyyy-MM-dd"));
                var lines = new List<Dictionary<string, object?>>();
                foreach (var line in action.Lines)
                {
                    if (line is null) throw InvalidOutput();
                    var row = new Dictionary<string, object?> { ["description"] = Short(line.Description, 1000) };
                    row["quantity"] = Bounded(line.Quantity ?? 1, 0.0001m, 100000m);
                    if (line.UnitPrice.HasValue) row["unitPrice"] = Bounded(line.UnitPrice.Value, 0, 10000000m);
                    else warnings.Add("Vul de ontbrekende prijs exclusief btw in voordat je het concept opslaat.");
                    row["discountPercent"] = Bounded(line.DiscountPercent ?? 0, 0, 100);
                    row["taxRate"] = Bounded(line.TaxRate ?? 21, 0, 100);
                    if (!line.TaxRate.HasValue) warnings.Add("Er is 21% btw voorgesteld omdat geen tarief is herkend. Controleer het btw-tarief.");
                    lines.Add(row);
                }
                if (lines.Count > 0) initial["lines"] = lines;
                else warnings.Add("Vul de factuur- of offerteregels en bedragen in.");
                warnings.Add("Dit wordt een concept. Definitief uitgeven, delen en verzenden doe je daarna afzonderlijk.");
            }
            if (action.Kind is "create_project" or "create_task") initial["sharedWithCustomer"] = false;
            if (action.Kind == "create_subscription")
            {
                initial["automaticInvoicing"] = false;
                initial.TryAdd("startDate", today.ToString("yyyy-MM-dd"));
                initial.TryAdd("nextInvoiceDate", initial["startDate"]);
                warnings.Add("Automatische facturatie staat uit. Schakel dit na controle bewust in als je dat wilt.");
            }
            if (action.Kind == "create_time_entry") initial.TryAdd("workDate", today.ToString("yyyy-MM-dd"));
            actions.Add(new AssistantAction(action.Kind, Short(action.Summary), orgName, projectName, initial, warnings.Distinct().Take(30).ToList()));
        }
        return new AssistantPlan(Short(plan.Message), actions, questions);
    }
    private static string? NullName(string? value) => string.IsNullOrWhiteSpace(value) ? null : Short(value, 200);
    private static decimal Bounded(decimal value, decimal min, decimal max) => value >= min && value <= max ? value : throw InvalidOutput();
    private static object? Clean(string key, JsonElement value)
    {
        if (Numbers.Contains(key)) return value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) && number >= 0 && number <= 1000000000m && (key != "durationMinutes" || decimal.Truncate(number) == number) ? number : null;
        if (Booleans.Contains(key)) return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
        if (key == "tags") return value.ValueKind == JsonValueKind.Array && value.GetArrayLength() <= 30 && value.EnumerateArray().All(x => x.ValueKind == JsonValueKind.String && x.GetString()!.Length <= 80) ? value.EnumerateArray().Select(x => x.GetString()!).ToArray() : null;
        if (value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString()!.Trim(); if (text.Length > 20000) return null;
        if (Dates.Contains(key)) return DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date.Year >= 1900 && date.Year <= 2200 ? text : null;
        if (Choices.TryGetValue(key, out var choices) && !choices.Contains(text)) return null;
        return text;
    }
}
