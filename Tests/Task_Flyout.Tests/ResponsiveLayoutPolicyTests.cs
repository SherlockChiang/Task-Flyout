using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class ResponsiveLayoutPolicyTests
{
    [Theory]
    [InlineData(0, ResponsiveLayoutMode.Narrow)]
    [InlineData(719, ResponsiveLayoutMode.Narrow)]
    [InlineData(720, ResponsiveLayoutMode.Medium)]
    [InlineData(1099, ResponsiveLayoutMode.Medium)]
    [InlineData(1100, ResponsiveLayoutMode.Wide)]
    [InlineData(1920, ResponsiveLayoutMode.Wide)]
    public void Selects_layout_mode_at_defined_breakpoints(double width, ResponsiveLayoutMode expected)
    {
        Assert.Equal(expected, ResponsiveLayoutPolicy.GetMode(width));
    }

    [Theory]
    [InlineData(700, 354)]
    [InlineData(600, 250)]
    [InlineData(500, 190)]
    public void Selects_compact_flyout_calendar_height(double availableHeight, double expected)
        => Assert.Equal(expected, ResponsiveLayoutPolicy.GetFlyoutCalendarHeight(availableHeight));

    [Theory]
    [InlineData(419, 40)]
    [InlineData(420, 56)]
    public void Selects_calendar_cell_minimum_for_short_viewports(double availableHeight, double expected)
        => Assert.Equal(expected, ResponsiveLayoutPolicy.GetCalendarCellMinimumHeight(availableHeight));
}
