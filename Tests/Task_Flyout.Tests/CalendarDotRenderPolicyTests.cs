using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class CalendarDotRenderPolicyTests
{
    [Fact]
    public void Identical_key_skips_semantic_render()
    {
        var key = new CalendarDotRenderKey(2026, 7, 12, 0, 4, 2);

        Assert.False(CalendarDotRenderPolicy.RequiresSemanticRender(key, key));
    }

    [Theory]
    [InlineData(2026, 8, 12, 0, 4, 2)]
    [InlineData(2026, 7, 13, 0, 4, 2)]
    [InlineData(2026, 7, 12, 1, 4, 2)]
    [InlineData(2026, 7, 12, 0, 5, 2)]
    [InlineData(2026, 7, 12, 0, 4, 3)]
    public void Every_key_dimension_invalidates_semantic_render(
        int year, int month, long cacheVersion, int displayMode, long generation, long filterVersion)
    {
        var previous = new CalendarDotRenderKey(2026, 7, 12, 0, 4, 2);
        var current = new CalendarDotRenderKey(year, month, cacheVersion, displayMode, generation, filterVersion);

        Assert.True(CalendarDotRenderPolicy.RequiresSemanticRender(previous, current));
    }

    [Fact]
    public void Displayed_month_uses_visible_realized_area_and_ignores_recycled_offscreen_days()
    {
        var days = new[]
        {
            new RealizedDayVisibility(new DateTime(2026, 6, 30), -60, -20),
            new RealizedDayVisibility(new DateTime(2026, 7, 1), 0, 40),
            new RealizedDayVisibility(new DateTime(2026, 7, 15), 40, 80),
            new RealizedDayVisibility(new DateTime(2026, 7, 31), 80, 120),
            new RealizedDayVisibility(new DateTime(2026, 8, 1), 120, 160),
            new RealizedDayVisibility(new DateTime(2026, 8, 2), 200, 240)
        };

        Assert.Equal(new DateTime(2026, 7, 1), CalendarDotRenderPolicy.DetectDisplayedMonth(days, 130));
    }

    [Fact]
    public void Displayed_month_returns_null_without_visible_containers()
    {
        var days = new[] { new RealizedDayVisibility(new DateTime(2026, 7, 1), 200, 240) };

        Assert.Null(CalendarDotRenderPolicy.DetectDisplayedMonth(days, 100));
    }
}
