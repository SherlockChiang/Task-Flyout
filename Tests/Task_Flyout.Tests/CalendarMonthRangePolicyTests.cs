using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class CalendarMonthRangePolicyTests
{
    [Theory]
    [InlineData(2026, 2, DayOfWeek.Sunday, "2026-02-01", "2026-03-01")]
    [InlineData(2026, 2, DayOfWeek.Monday, "2026-01-26", "2026-03-02")]
    [InlineData(2026, 8, DayOfWeek.Sunday, "2026-07-26", "2026-09-06")]
    [InlineData(2026, 12, DayOfWeek.Monday, "2026-11-30", "2027-01-04")]
    public void GetRange_ReturnsExactRenderedGrid(
        int year,
        int month,
        DayOfWeek firstDayOfWeek,
        string expectedStart,
        string expectedEndExclusive)
    {
        var range = CalendarMonthRangePolicy.GetRange(new DateTime(year, month, 1), firstDayOfWeek);

        Assert.Equal(DateTime.Parse(expectedStart), range.Start);
        Assert.Equal(DateTime.Parse(expectedEndExclusive), range.EndExclusive);
        Assert.Equal(0, (range.EndExclusive - range.Start).Days % 7);
    }

    [Fact]
    public void ContainsDateKey_ExcludesPrefetchedDatesAfterGrid()
    {
        var range = CalendarMonthRangePolicy.GetRange(new DateTime(2026, 8, 1), DayOfWeek.Sunday);

        Assert.True(range.ContainsDateKey("2026-08-15"));
        Assert.True(range.ContainsDateKey("2026-09-05"));
        Assert.False(range.ContainsDateKey("2026-09-06"));
        Assert.False(range.ContainsDateKey("2026-11-01"));
    }

    [Fact]
    public void ContainsMonthDateKey_ExcludesGridSpilloverDates()
    {
        var range = CalendarMonthRangePolicy.GetRange(new DateTime(2026, 8, 1), DayOfWeek.Sunday);

        Assert.False(range.ContainsMonthDateKey("2026-07-31"));
        Assert.True(range.ContainsMonthDateKey("2026-08-01"));
        Assert.True(range.ContainsMonthDateKey("2026-08-31"));
        Assert.False(range.ContainsMonthDateKey("2026-09-01"));
    }

    [Fact]
    public void GetWeekRange_ReturnsSevenDaysFromConfiguredWeekStart()
    {
        var range = CalendarMonthRangePolicy.GetWeekRange(new DateTime(2026, 8, 12), DayOfWeek.Monday);

        Assert.Equal(new DateTime(2026, 8, 10), range.Start);
        Assert.Equal(new DateTime(2026, 8, 17), range.EndExclusive);
        Assert.Equal(7, (range.EndExclusive - range.Start).Days);
    }

    [Fact]
    public void GetYearRange_ReturnsCalendarYear()
    {
        var range = CalendarMonthRangePolicy.GetYearRange(2026);

        Assert.Equal(new DateTime(2026, 1, 1), range.Start);
        Assert.Equal(new DateTime(2027, 1, 1), range.EndExclusive);
        Assert.True(range.ContainsDateKey("2026-12-31"));
        Assert.False(range.ContainsDateKey("2027-01-01"));
    }
}
