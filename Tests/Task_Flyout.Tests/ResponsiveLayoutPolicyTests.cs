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
    [InlineData(double.NaN, false)]
    [InlineData(899, false)]
    [InlineData(900, true)]
    [InlineData(1440, true)]
    public void Settings_use_masonry_only_when_two_columns_remain_readable(
        double width,
        bool expected)
        => Assert.Equal(expected, ResponsiveLayoutPolicy.ShouldUseSettingsMasonry(width));

    [Theory]
    [InlineData(819, 700, true)]
    [InlineData(820, 559, true)]
    [InlineData(820, 560, false)]
    [InlineData(1200, 800, false)]
    public void Calendar_uses_agenda_only_when_the_month_grid_would_be_cramped(
        double width,
        double height,
        bool expected)
        => Assert.Equal(
            expected,
            ResponsiveLayoutPolicy.ShouldShowCalendarAgendaOnly(width, height));

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
    [InlineData(1119, false)]
    [InlineData(1120, true)]
    public void Mail_auto_selects_the_first_message_only_in_the_wide_layout(
        double width,
        bool expected)
        => Assert.Equal(expected, ResponsiveLayoutPolicy.ShouldAutoSelectFirstMail(width));

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
    [InlineData(540, 900, 12)]
    [InlineData(900, 540, 12)]
    [InlineData(900, 700, 20)]
    [InlineData(1200, 800, 28)]
    public void Selects_page_padding_from_logical_viewport(
        double width, double height, double expected)
        => Assert.Equal(expected, ResponsiveLayoutPolicy.GetPagePadding(width, height));

    [Theory]
    [InlineData(320, 500, 280, 436, 4)]
    [InlineData(400, 700, 352, 620, 4)]
    [InlineData(800, 900, 420, 620, 6)]
    public void Constrains_color_picker_to_small_logical_viewports(
        double width,
        double height,
        double expectedWidth,
        double expectedHeight,
        int expectedColumns)
    {
        var metrics = ResponsiveLayoutPolicy.GetColorPickerPopupMetrics(width, height);

        Assert.Equal(expectedWidth, metrics.ContentWidth);
        Assert.Equal(expectedHeight, metrics.MaxContentHeight);
        Assert.Equal(expectedColumns, metrics.PaletteColumns);
    }

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
