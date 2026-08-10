using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class WeatherBarModePolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("future-mode")]
    [InlineData("TaskFlyout")]
    public void Missing_or_unknown_mode_preserves_existing_task_flyout_bar(string? value)
        => Assert.Equal(WeatherBarMode.TaskFlyout, WeatherBarModePolicy.Parse(value));

    [Theory]
    [InlineData("WindowsWidgets")]
    [InlineData("windowswidgets")]
    public void Windows_widgets_mode_parse_is_case_insensitive(string value)
        => Assert.Equal(WeatherBarMode.WindowsWidgets, WeatherBarModePolicy.Parse(value));

    [Theory]
    [InlineData("StandaloneTaskbar")]
    [InlineData("standalonetaskbar")]
    public void Standalone_mode_parse_is_case_insensitive(string value)
        => Assert.Equal(WeatherBarMode.StandaloneTaskbar, WeatherBarModePolicy.Parse(value));

    [Theory]
    [InlineData("TaskFlyout")]
    [InlineData("WindowsWidgets")]
    [InlineData("StandaloneTaskbar")]
    public void Modes_serialize_to_stable_setting_values(string expected)
        => Assert.Equal(expected, WeatherBarModePolicy.Serialize(WeatherBarModePolicy.Parse(expected)));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Disabled_setting_never_owns_a_taskbar_surface(bool useWindowsWidgets)
    {
        var mode = useWindowsWidgets ? WeatherBarMode.WindowsWidgets : WeatherBarMode.TaskFlyout;
        var result = WeatherBarModePolicy.Resolve(
            weatherBarEnabled: false,
            taskFlyoutWeatherEnabled: true,
            mode,
            windowsWidgetsAvailable: true);

        Assert.Equal(WeatherBarPresentation.Disabled, result.Presentation);
        Assert.Equal(WeatherBarFallbackReason.WeatherBarDisabled, result.FallbackReason);
    }

    [Fact]
    public void Existing_mode_runs_task_flyout_bar_when_weather_is_enabled()
    {
        var result = Resolve(WeatherBarMode.TaskFlyout);

        Assert.True(result.ShouldRunTaskFlyoutBar);
        Assert.False(result.ShouldUseWindowsWidgets);
        Assert.False(result.IsFallback);
        Assert.Equal(WeatherBarFallbackReason.None, result.FallbackReason);
    }

    [Fact]
    public void Existing_mode_stops_when_task_flyout_weather_is_disabled()
    {
        var result = Resolve(WeatherBarMode.TaskFlyout, taskFlyoutWeatherEnabled: false);

        Assert.Equal(WeatherBarPresentation.Disabled, result.Presentation);
        Assert.Equal(WeatherBarFallbackReason.TaskFlyoutWeatherDisabled, result.FallbackReason);
    }

    [Fact]
    public void Available_windows_widgets_owns_surface_without_task_flyout_weather()
    {
        var result = Resolve(
            WeatherBarMode.WindowsWidgets,
            taskFlyoutWeatherEnabled: false,
            windowsWidgetsAvailable: true);

        Assert.True(result.ShouldUseWindowsWidgets);
        Assert.False(result.ShouldRunTaskFlyoutBar);
        Assert.False(result.IsFallback);
        Assert.Equal(WeatherBarFallbackReason.None, result.FallbackReason);
    }

    [Fact]
    public void Unavailable_windows_widgets_falls_back_to_existing_bar()
    {
        var result = Resolve(
            WeatherBarMode.WindowsWidgets,
            taskFlyoutWeatherEnabled: true,
            windowsWidgetsAvailable: false);

        Assert.True(result.ShouldRunTaskFlyoutBar);
        Assert.True(result.IsFallback);
        Assert.Equal(WeatherBarFallbackReason.WindowsWidgetsUnavailable, result.FallbackReason);
    }

    [Fact]
    public void Native_unavailable_without_fallback_weather_disables_surface()
    {
        var result = Resolve(
            WeatherBarMode.WindowsWidgets,
            taskFlyoutWeatherEnabled: false,
            windowsWidgetsAvailable: false);

        Assert.Equal(WeatherBarPresentation.Disabled, result.Presentation);
        Assert.False(result.IsFallback);
        Assert.Equal(
            WeatherBarFallbackReason.WindowsWidgetsUnavailableAndTaskFlyoutWeatherDisabled,
            result.FallbackReason);
    }

    [Fact]
    public void Active_standalone_host_owns_surface_when_task_flyout_weather_is_enabled()
    {
        var result = Resolve(
            WeatherBarMode.StandaloneTaskbar,
            standaloneTaskbarActive: true);

        Assert.True(result.ShouldUseStandaloneTaskbar);
        Assert.False(result.ShouldRunTaskFlyoutBar);
        Assert.False(result.ShouldUseWindowsWidgets);
        Assert.False(result.IsFallback);
        Assert.Equal(WeatherBarFallbackReason.None, result.FallbackReason);
    }

    [Fact]
    public void Inactive_standalone_host_keeps_existing_bar_visible()
    {
        var result = Resolve(
            WeatherBarMode.StandaloneTaskbar,
            standaloneTaskbarActive: false);

        Assert.True(result.ShouldRunTaskFlyoutBar);
        Assert.True(result.IsFallback);
        Assert.Equal(
            WeatherBarFallbackReason.StandaloneTaskbarUnavailable,
            result.FallbackReason);
    }

    [Fact]
    public void Standalone_mode_requires_task_flyout_weather_provider()
    {
        var result = Resolve(
            WeatherBarMode.StandaloneTaskbar,
            taskFlyoutWeatherEnabled: false,
            standaloneTaskbarActive: true);

        Assert.Equal(WeatherBarPresentation.Disabled, result.Presentation);
        Assert.False(result.ShouldUseStandaloneTaskbar);
        Assert.False(result.IsFallback);
        Assert.Equal(
            WeatherBarFallbackReason.StandaloneTaskbarUnavailableAndTaskFlyoutWeatherDisabled,
            result.FallbackReason);
    }

    private static WeatherBarModeResolution Resolve(
        WeatherBarMode mode,
        bool taskFlyoutWeatherEnabled = true,
        bool windowsWidgetsAvailable = true,
        bool standaloneTaskbarActive = false)
        => WeatherBarModePolicy.Resolve(
            weatherBarEnabled: true,
            taskFlyoutWeatherEnabled,
            mode,
            windowsWidgetsAvailable,
            standaloneTaskbarActive);
}
