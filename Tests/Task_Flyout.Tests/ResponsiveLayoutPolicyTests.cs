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

    [Theory]
    [InlineData(960, 307.2)]
    [InlineData(1280, 409.6)]
    [InlineData(1920, 420)]
    public void Caps_weather_bar_to_taskbar_share(double taskbarWidth, double expected)
        => Assert.Equal(expected, ResponsiveLayoutPolicy.GetWeatherBarMaximumWidth(taskbarWidth), 3);

    [Theory]
    [InlineData(1399, true)]
    [InlineData(1400, false)]
    public void Selects_compact_weather_bar(double taskbarWidth, bool expected)
        => Assert.Equal(expected, ResponsiveLayoutPolicy.UseCompactWeatherBar(taskbarWidth));

    [Theory]
    [InlineData(152, 240, 80, 420, 152)]
    [InlineData(0, 240, 80, 420, 240)]
    [InlineData(0, 500, 80, 420, 420)]
    public void Uses_exact_widgets_width_with_bounded_fallback(
        int detectedWidth, int fallbackWidth, int minimumWidth, int maximumWidth, int expected)
        => Assert.Equal(expected, ResponsiveLayoutPolicy.GetWeatherBarPhysicalWidth(
            detectedWidth, fallbackWidth, minimumWidth, maximumWidth));
}
