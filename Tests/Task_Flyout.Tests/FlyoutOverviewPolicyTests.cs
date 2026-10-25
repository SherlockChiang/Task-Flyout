using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class FlyoutOverviewPolicyTests
{
    [Fact]
    public void Overview_fills_today_with_future_items_without_completed_tasks_or_duplicates()
    {
        var today = new DateTime(2026, 10, 25);
        var items = new[]
        {
            new FlyoutOverviewEntry("future", "2026-10-26", null, false),
            new FlyoutOverviewEntry("late", "2026-10-25", today.AddHours(15), false),
            new FlyoutOverviewEntry("early", "2026-10-25", today.AddHours(9), false),
            new FlyoutOverviewEntry("done", "2026-10-25", null, true),
            new FlyoutOverviewEntry("past", "2026-10-24", null, false),
            new FlyoutOverviewEntry("bad", "not-a-date", null, false)
        };
        Assert.Equal(new[] { "early", "late", "future" },
            FlyoutOverviewPolicy.Select(items.Concat(new[] { items[0] }), today).Select(item => item.Key));
    }

    [Fact]
    public void Overview_caps_at_five_and_keeps_different_accounts()
    {
        var items = Enumerable.Range(0, 8).Select(index =>
            new FlyoutOverviewEntry("account-" + index, "2026-10-25", null, false));
        Assert.Equal(Enumerable.Range(0, 5).Select(index => "account-" + index),
            FlyoutOverviewPolicy.Select(items, new DateTime(2026, 10, 25)).Select(item => item.Key));
    }

    [Theory]
    [InlineData(380, 112)]
    [InlineData(480, 112)]
    [InlineData(720, 112)]
    [InlineData(1000, 112)]
    public void Header_budget_keeps_footer_and_content_visible(double height, double footer)
    {
        double header = FlyoutOverviewPolicy.HeaderMaximum(height, footer);
        Assert.InRange(header, 0, height * .6);
        Assert.True(height - footer - header >= 100);
    }
}
