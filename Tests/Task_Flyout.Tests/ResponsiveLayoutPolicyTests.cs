using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class ResponsiveLayoutPolicyTests
{
    [Theory]
    [InlineData(0, ResponsiveLayoutMode.Narrow)]
    [InlineData(679, ResponsiveLayoutMode.Narrow)]
    [InlineData(680, ResponsiveLayoutMode.Medium)]
    [InlineData(1039, ResponsiveLayoutMode.Medium)]
    [InlineData(1040, ResponsiveLayoutMode.Wide)]
    [InlineData(1920, ResponsiveLayoutMode.Wide)]
    public void Selects_layout_mode_at_defined_breakpoints(double width, ResponsiveLayoutMode expected)
    {
        Assert.Equal(expected, ResponsiveLayoutPolicy.GetMode(width));
    }

    [Theory]
    [InlineData(859, ResponsiveLayoutMode.Medium)]
    [InlineData(860, ResponsiveLayoutMode.Wide)]
    public void Tasks_use_available_width_earlier(double width, ResponsiveLayoutMode expected)
        => Assert.Equal(expected, ResponsiveLayoutPolicy.GetTasksMode(width));

    [Theory]
    [InlineData(679, ResponsiveLayoutMode.Narrow)]
    [InlineData(680, ResponsiveLayoutMode.Medium)]
    [InlineData(1119, ResponsiveLayoutMode.Medium)]
    [InlineData(1120, ResponsiveLayoutMode.Wide)]
    public void Three_pane_pages_wait_for_readable_width(double width, ResponsiveLayoutMode expected)
    {
        Assert.Equal(expected, ResponsiveLayoutPolicy.GetCalendarMode(width));
        Assert.Equal(expected, ResponsiveLayoutPolicy.GetMailMode(width));
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

    [Theory]
    [InlineData(0, 0)]
    [InlineData(200, 64)]
    [InlineData(960, 307.2)]
    [InlineData(1280, 320)]
    [InlineData(1920, 320)]
    public void Caps_weather_bar_to_taskbar_share(double taskbarWidth, double expected)
        => Assert.Equal(expected, ResponsiveLayoutPolicy.GetWeatherBarMaximumWidth(taskbarWidth), 3);

    [Theory]
    [InlineData(40, 48, 40)]
    [InlineData(0, 48, 48)]
    [InlineData(60, 48, 48)]
    [InlineData(0, 24, 24)]
    public void Uses_exact_widgets_height_with_bounded_fallback(
        int detectedHeight, int taskbarHeight, int expected)
        => Assert.Equal(expected, ResponsiveLayoutPolicy.GetWeatherBarPhysicalHeight(
            detectedHeight, taskbarHeight));
}
