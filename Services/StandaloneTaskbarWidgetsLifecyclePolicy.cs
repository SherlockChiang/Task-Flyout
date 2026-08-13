namespace Task_Flyout.Services;

internal static class StandaloneTaskbarWidgetsLifecyclePolicy
{
    public static bool IsStandaloneDesired(
        bool launchSuppressed,
        WeatherBarMode requestedMode,
        bool weatherBarEnabled,
        bool weatherProviderEnabled,
        bool diagnosticOnlyRejected = false)
        => !launchSuppressed &&
           !diagnosticOnlyRejected &&
           requestedMode == WeatherBarMode.StandaloneTaskbar &&
           weatherBarEnabled &&
           weatherProviderEnabled;

    public static bool CanRestore(
        bool launchSuppressed,
        bool standaloneDesired,
        bool controllerCleanupPending)
        => !launchSuppressed &&
           !standaloneDesired &&
           !controllerCleanupPending;

    public static bool ShouldKeepVerification(
        StandaloneTaskbarWidgetsResult result,
        bool nativeEntryPointPresent)
    {
        if (result.NotificationPending)
            return true;
        // Keep a low-frequency ownership monitor after activation. It detects a
        // user or policy changing TaskbarDa while the standalone host is active
        // and lets App stop the host before the Windows entry can coexist.
        if (result.Succeeded &&
            result.IsEffectivelySuppressed &&
            result.OwnsSetting)
            return true;
        if (result.IsEffectivelySuppressed && nativeEntryPointPresent)
            return true;
        if (result.Kind == StandaloneTaskbarWidgetsResultKind.LockUnavailable)
            return true;
        return result.RequiresRecovery &&
               result.Kind is
                   StandaloneTaskbarWidgetsResultKind.SettingsFailure or
                   StandaloneTaskbarWidgetsResultKind.RegistryFailure;
    }
}
