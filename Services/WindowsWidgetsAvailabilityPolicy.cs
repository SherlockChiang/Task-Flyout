using System;

namespace Task_Flyout.Services
{
    internal enum WindowsWidgetsAvailabilityReason
    {
        Available,
        UnsupportedWindowsBuild,
        WebExperiencePackMissing,
        TaskbarUnavailable,
        DetectionFailed
    }

    internal readonly record struct WindowsWidgetsAvailabilitySignals(
        bool WindowsBuildSupported,
        bool WebExperiencePackRegistered,
        bool TaskbarPresent,
        bool NativeEntryPointPresent);

    internal readonly record struct WindowsWidgetsAvailability(
        bool IsAvailable,
        WindowsWidgetsAvailabilityReason Reason,
        bool WebExperiencePackRegistered,
        bool TaskbarPresent,
        bool NativeEntryPointPresent,
        string Detail)
    {
        public static WindowsWidgetsAvailability Unavailable(
            WindowsWidgetsAvailabilityReason reason,
            string detail,
            bool webExperiencePackRegistered = false,
            bool taskbarPresent = false,
            bool nativeEntryPointPresent = false)
            => new(
                false,
                reason,
                webExperiencePackRegistered,
                taskbarPresent,
                nativeEntryPointPresent,
                detail);
    }

    internal static class WindowsWidgetsAvailabilityPolicy
    {
        public static WindowsWidgetsAvailability Resolve(
            WindowsWidgetsAvailabilitySignals signals,
            string? detail = null)
        {
            if (!signals.WindowsBuildSupported)
            {
                return WindowsWidgetsAvailability.Unavailable(
                    WindowsWidgetsAvailabilityReason.UnsupportedWindowsBuild,
                    detail ?? "Windows Widgets requires Windows 11 taskbar components.",
                    signals.WebExperiencePackRegistered,
                    signals.TaskbarPresent,
                    signals.NativeEntryPointPresent);
            }

            if (!signals.WebExperiencePackRegistered)
            {
                return WindowsWidgetsAvailability.Unavailable(
                    WindowsWidgetsAvailabilityReason.WebExperiencePackMissing,
                    detail ?? "Windows Web Experience Pack is not registered for this user.",
                    false,
                    signals.TaskbarPresent,
                    signals.NativeEntryPointPresent);
            }

            if (!signals.TaskbarPresent)
            {
                return WindowsWidgetsAvailability.Unavailable(
                    WindowsWidgetsAvailabilityReason.TaskbarUnavailable,
                    detail ?? "The Windows taskbar is not ready.",
                    true,
                    false,
                    signals.NativeEntryPointPresent);
            }

            return new WindowsWidgetsAvailability(
                true,
                WindowsWidgetsAvailabilityReason.Available,
                true,
                true,
                signals.NativeEntryPointPresent,
                detail ?? (signals.NativeEntryPointPresent
                    ? "Windows Widgets entry point is present."
                    : "Windows Widgets is available; the taskbar entry point is waiting for Explorer."));
        }
    }
}
