namespace Coffa.Api.Commercial;

/// <summary>Amsterdam financial month: start inclusive, end exclusive.</summary>
public readonly record struct FinancialMonth(DateOnly Start, DateOnly End)
{
    public static TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
    public static FinancialMonth At(DateTimeOffset instant) => ForDate(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Zone).DateTime));
    public static FinancialMonth ForDate(DateOnly date)
    {
        var start = new DateOnly(date.Year, date.Month, 20);
        if (date < start) start = start.AddMonths(-1);
        return new(start, start.AddMonths(1));
    }
    public DateTimeOffset StartInstant => Midnight(Start);
    public DateTimeOffset EndInstant => Midnight(End);
    private static DateTimeOffset Midnight(DateOnly date) => new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), Zone), TimeSpan.Zero);
}
