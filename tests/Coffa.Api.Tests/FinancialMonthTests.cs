using Coffa.Api.Commercial;
using Xunit;

namespace Coffa.Api.Tests;

public sealed class FinancialMonthTests
{
    [Theory]
    [InlineData("2026-11-06T12:00:00Z", "2026-10-20", "2026-11-20")]
    [InlineData("2026-11-19T22:59:59Z", "2026-10-20", "2026-11-20")]
    [InlineData("2026-11-19T23:00:00Z", "2026-11-20", "2026-12-20")]
    [InlineData("2027-01-01T00:00:00Z", "2026-12-20", "2027-01-20")]
    [InlineData("2026-03-29T00:59:59Z", "2026-03-20", "2026-04-20")]
    [InlineData("2026-03-29T01:00:00Z", "2026-03-20", "2026-04-20")]
    [InlineData("2026-10-25T00:59:59Z", "2026-10-20", "2026-11-20")]
    [InlineData("2026-10-25T01:00:00Z", "2026-10-20", "2026-11-20")]
    public void UsesAmsterdamDate(string instant, string start, string end)
    {
        var month = FinancialMonth.At(DateTimeOffset.Parse(instant));
        Assert.Equal(DateOnly.Parse(start), month.Start);
        Assert.Equal(DateOnly.Parse(end), month.End);
        Assert.Equal(month, FinancialMonth.At(month.StartInstant));
        Assert.Equal(month, FinancialMonth.At(month.EndInstant.AddTicks(-1)));
        Assert.Equal(month.End, FinancialMonth.At(month.EndInstant).Start);
    }

    [Theory]
    [InlineData("2026-03-20", 743)]
    [InlineData("2026-10-20", 745)]
    public void BoundariesUseActualDaylightSavingOffsets(string start, int hours)
    {
        var month = FinancialMonth.ForDate(DateOnly.Parse(start));
        Assert.Equal(hours, (month.EndInstant - month.StartInstant).TotalHours);
    }
}
