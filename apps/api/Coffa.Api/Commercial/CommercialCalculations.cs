using System.Text.Json;
using Coffa.Api.Domain;
using Coffa.Api.Infrastructure;

namespace Coffa.Api.Commercial;

public sealed record CommercialLineInput(string Description, decimal Quantity, decimal UnitPrice, decimal DiscountPercent = 0, decimal TaxRate = 21);
public sealed record CommercialLine(string Description, decimal Quantity, decimal UnitPrice, decimal DiscountPercent, decimal TaxRate, decimal NetAmount, decimal TaxAmount, decimal TotalAmount);
public sealed record CommercialTotals(IReadOnlyList<CommercialLine> Lines, decimal NetAmount, decimal TaxAmount, decimal TotalAmount);

public static class CommercialCalculations
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static decimal Round(decimal amount) => decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
    public static CommercialTotals Calculate(IReadOnlyList<CommercialLineInput>? lines)
    {
        if (lines is null || lines.Count is < 1 or > 200) throw new ApiException(400, "invalid_lines", "Voeg 1 tot 200 regels toe.");
        var result = new List<CommercialLine>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.Description) || line.Description.Length > 1000 || line.Quantity <= 0 || line.Quantity > 100000 || line.UnitPrice < 0 || line.UnitPrice > 10000000 || line.DiscountPercent < 0 || line.DiscountPercent > 100 || line.TaxRate < 0 || line.TaxRate > 100)
                throw new ApiException(400, "invalid_line", "Controleer omschrijving, aantal, prijs, korting en btw per regel.");
            var unitPrice = Round(line.UnitPrice);
            var net = Round(line.Quantity * unitPrice * (1m - line.DiscountPercent / 100m));
            var tax = Round(net * line.TaxRate / 100m);
            result.Add(new CommercialLine(line.Description.Trim(), line.Quantity, unitPrice, line.DiscountPercent, line.TaxRate, net, tax, net + tax));
        }
        var total = new CommercialTotals(result, result.Sum(x => x.NetAmount), result.Sum(x => x.TaxAmount), result.Sum(x => x.TotalAmount));
        if (total.TotalAmount > 9999999999999999.99m) throw new ApiException(400, "amount_too_large", "Het totaalbedrag is te hoog.");
        return total;
    }
    public static IReadOnlyList<CommercialLine> Lines(string json) => JsonSerializer.Deserialize<List<CommercialLine>>(json, Json) ?? [];
    public static string Serialize(CommercialTotals totals) => JsonSerializer.Serialize(totals.Lines, Json);
    public static int FrequencyMonths(string frequency) => frequency switch { "monthly" => 1, "quarterly" => 3, "yearly" => 12, _ => throw new ApiException(400, "invalid_frequency", "Kies maandelijks, per kwartaal of jaarlijks.") };
    public static DateOnly NextBillingDate(DateOnly date, string frequency, int day)
    {
        var next = new DateOnly(date.Year, date.Month, 1).AddMonths(FrequencyMonths(frequency));
        return new DateOnly(next.Year, next.Month, Math.Min(Math.Max(1, day), DateTime.DaysInMonth(next.Year, next.Month)));
    }
    public static decimal MonthlyAmount(ServiceSubscription subscription) => subscription.Amount / FrequencyMonths(subscription.Frequency);
    public static decimal Outstanding(Invoice invoice, decimal creditAmount = 0) => Math.Max(0, Round(invoice.TotalAmount - invoice.Payments.Sum(x => x.Amount) - creditAmount));
    public static string InvoiceStatus(Invoice invoice, DateOnly today, decimal creditAmount = 0)
    {
        if (invoice.Status == "draft") return "draft";
        if (invoice.Kind == "credit") return "credit";
        var outstanding = Outstanding(invoice, creditAmount);
        return outstanding == 0 ? "paid" : invoice.DueDate < today ? "overdue" : invoice.Payments.Count > 0 ? "partial" : "open";
    }
    public static string QuoteStatus(Quote quote, DateOnly today) => quote.Status is "issued" or "sent" && quote.ValidUntil < today ? "expired" : quote.Status;
}
