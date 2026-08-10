using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class WeatherBarModeSettingsTests
{
    [Fact]
    public void Missing_setting_migrates_to_existing_task_flyout_mode()
    {
        var values = new Dictionary<string, object>();

        var mode = WeatherBarModeSettings.ReadAndMigrate(values);

        Assert.Equal(WeatherBarMode.TaskFlyout, mode);
        Assert.Equal("TaskFlyout", values[WeatherBarModeSettings.SettingKey]);
    }

    [Fact]
    public void Invalid_setting_is_replaced_with_safe_default()
    {
        var values = new Dictionary<string, object>
        {
            [WeatherBarModeSettings.SettingKey] = 42
        };

        var mode = WeatherBarModeSettings.ReadAndMigrate(values);

        Assert.Equal(WeatherBarMode.TaskFlyout, mode);
        Assert.Equal("TaskFlyout", values[WeatherBarModeSettings.SettingKey]);
    }

    [Fact]
    public void Native_setting_is_preserved_and_normalised()
    {
        var values = new Dictionary<string, object>
        {
            [WeatherBarModeSettings.SettingKey] = "windowswidgets"
        };

        var mode = WeatherBarModeSettings.ReadAndMigrate(values);

        Assert.Equal(WeatherBarMode.WindowsWidgets, mode);
        Assert.Equal("WindowsWidgets", values[WeatherBarModeSettings.SettingKey]);
    }

    [Fact]
    public void Write_round_trips_mode()
    {
        var values = new Dictionary<string, object>();

        WeatherBarModeSettings.Write(values, WeatherBarMode.WindowsWidgets);

        Assert.Equal(WeatherBarMode.WindowsWidgets, WeatherBarModeSettings.Read(values));
    }

    [Fact]
    public void Write_round_trips_standalone_mode()
    {
        var values = new Dictionary<string, object>();

        WeatherBarModeSettings.Write(values, WeatherBarMode.StandaloneTaskbar);

        Assert.Equal("StandaloneTaskbar", values[WeatherBarModeSettings.SettingKey]);
        Assert.Equal(WeatherBarMode.StandaloneTaskbar, WeatherBarModeSettings.Read(values));
    }
}
