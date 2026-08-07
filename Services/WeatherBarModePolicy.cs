using System;

namespace Task_Flyout.Services
{
    /// <summary>
    /// Selects who owns the taskbar weather surface. The persisted names are deliberately
    /// independent from UI text so localized labels can change without migrating settings.
    /// </summary>
    internal enum WeatherBarMode
    {
        TaskFlyout,
        WindowsWidgets
    }

    internal enum WeatherBarPresentation
    {
        Disabled,
        TaskFlyout,
        WindowsWidgets
    }

    internal enum WeatherBarFallbackReason
    {
        None,
        WeatherBarDisabled,
        TaskFlyoutWeatherDisabled,
        WindowsWidgetsUnavailable,
        WindowsWidgetsUnavailableAndTaskFlyoutWeatherDisabled
    }

    internal readonly record struct WeatherBarModeResolution(
        WeatherBarMode RequestedMode,
        WeatherBarPresentation Presentation,
        WeatherBarFallbackReason FallbackReason)
    {
        public bool ShouldRunTaskFlyoutBar => Presentation == WeatherBarPresentation.TaskFlyout;

        public bool ShouldUseWindowsWidgets => Presentation == WeatherBarPresentation.WindowsWidgets;

        public bool IsFallback => RequestedMode == WeatherBarMode.WindowsWidgets &&
                                  Presentation == WeatherBarPresentation.TaskFlyout;
    }

    internal static class WeatherBarModePolicy
    {
        public const string TaskFlyoutSettingValue = "TaskFlyout";
        public const string WindowsWidgetsSettingValue = "WindowsWidgets";

        /// <summary>
        /// Missing and unrecognised values preserve the pre-mode-selector behaviour.
        /// </summary>
        public static WeatherBarMode Parse(string? persistedValue)
        {
            if (string.Equals(persistedValue?.Trim(), WindowsWidgetsSettingValue, StringComparison.OrdinalIgnoreCase))
                return WeatherBarMode.WindowsWidgets;

            return WeatherBarMode.TaskFlyout;
        }

        public static string Serialize(WeatherBarMode mode)
            => mode == WeatherBarMode.WindowsWidgets
                ? WindowsWidgetsSettingValue
                : TaskFlyoutSettingValue;

        /// <summary>
        /// Resolves the requested mode without performing any shell or registry mutation.
        /// A native-mode request remains persisted when the Windows surface is temporarily
        /// unavailable, allowing it to become effective automatically after shell recovery.
        /// </summary>
        public static WeatherBarModeResolution Resolve(
            bool weatherBarEnabled,
            bool taskFlyoutWeatherEnabled,
            WeatherBarMode requestedMode,
            bool windowsWidgetsAvailable)
        {
            if (!weatherBarEnabled)
            {
                return new WeatherBarModeResolution(
                    requestedMode,
                    WeatherBarPresentation.Disabled,
                    WeatherBarFallbackReason.WeatherBarDisabled);
            }

            if (requestedMode == WeatherBarMode.WindowsWidgets)
            {
                if (windowsWidgetsAvailable)
                {
                    return new WeatherBarModeResolution(
                        requestedMode,
                        WeatherBarPresentation.WindowsWidgets,
                        WeatherBarFallbackReason.None);
                }

                if (taskFlyoutWeatherEnabled)
                {
                    return new WeatherBarModeResolution(
                        requestedMode,
                        WeatherBarPresentation.TaskFlyout,
                        WeatherBarFallbackReason.WindowsWidgetsUnavailable);
                }

                return new WeatherBarModeResolution(
                    requestedMode,
                    WeatherBarPresentation.Disabled,
                    WeatherBarFallbackReason.WindowsWidgetsUnavailableAndTaskFlyoutWeatherDisabled);
            }

            return taskFlyoutWeatherEnabled
                ? new WeatherBarModeResolution(
                    requestedMode,
                    WeatherBarPresentation.TaskFlyout,
                    WeatherBarFallbackReason.None)
                : new WeatherBarModeResolution(
                    requestedMode,
                    WeatherBarPresentation.Disabled,
                    WeatherBarFallbackReason.TaskFlyoutWeatherDisabled);
        }
    }
}
