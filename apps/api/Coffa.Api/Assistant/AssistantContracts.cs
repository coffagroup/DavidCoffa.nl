using System.Text.Json;

namespace Coffa.Api.Assistant;

public interface IAssistantProvider
{
    bool Configured { get; }
    Task<string> TranscribeAsync(IFormFile file, CancellationToken ct);
    Task<AssistantModelPlan> PlanAsync(string text, DateOnly today, int paymentTermDays, CancellationToken ct);
}
public sealed record AssistantModelPlan(string Message, List<AssistantModelAction> Actions, List<string> Questions);
public sealed record AssistantModelAction(string Kind, string Summary, string? OrganizationName, string? ProjectName, List<AssistantField> Fields, List<AssistantLine> Lines, List<string> Warnings);
public sealed record AssistantField(string Name, JsonElement Value);
public sealed record AssistantLine(string Description, decimal? Quantity, decimal? UnitPrice, decimal? DiscountPercent, decimal? TaxRate);
public sealed record AssistantAction(string Kind, string Summary, string? OrganizationName, string? ProjectName, Dictionary<string, object?> Initial, List<string> Warnings);
public sealed record AssistantPlan(string Message, List<AssistantAction> Actions, List<string> Questions);
