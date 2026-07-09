using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class SyncRangePolicyTests
{
    [Fact]
    public void NormalizeHalfOpenDateRange_strips_times()
    {
        var range = SyncRangePolicy.NormalizeHalfOpenDateRange(
            new DateTime(2026, 7, 9, 13, 30, 0),
            new DateTime(2026, 7, 12, 22, 15, 0));

        Assert.Equal(new DateTime(2026, 7, 9), range.StartDate);
        Assert.Equal(new DateTime(2026, 7, 12), range.EndDate);
    }

    [Fact]
    public void NormalizeHalfOpenDateRange_clamps_reversed_end_to_start()
    {
        var range = SyncRangePolicy.NormalizeHalfOpenDateRange(
            new DateTime(2026, 7, 12),
            new DateTime(2026, 7, 9));

        Assert.Equal(new DateTime(2026, 7, 12), range.StartDate);
        Assert.Equal(new DateTime(2026, 7, 12), range.EndDate);
    }

    [Theory]
    [InlineData("2026-07-09", true)]
    [InlineData("2026-07-10", true)]
    [InlineData("2026-07-11", true)]
    [InlineData("2026-07-12", false)]
    [InlineData("2026-07-08", false)]
    public void IsInHalfOpenDateRange_includes_start_and_excludes_end(string dateText, bool expected)
    {
        var date = DateTime.Parse(dateText);

        var result = SyncRangePolicy.IsInHalfOpenDateRange(
            date,
            new DateTime(2026, 7, 9, 13, 30, 0),
            new DateTime(2026, 7, 12, 22, 15, 0));

        Assert.Equal(expected, result);
    }
}
