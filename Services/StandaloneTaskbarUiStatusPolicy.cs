using System;

namespace Task_Flyout.Services;

internal enum StandaloneTaskbarUiStatusKind
{
    Off,
    WeatherBarDisabled,
    WeatherProviderDisabled,
    Preparing,
    ActiveUnverified,
    BinaryMissing,
    Unsupported,
    TemporarilyUnavailable,
    WidgetsConflict,
    ControlSuppressed,
    Stopping,
    Failed,
    Fallback
}

internal readonly record struct StandaloneTaskbarDiagnostics(
    StandaloneTaskbarRuntimeStatus RuntimeStatus,
    bool ControllerRequested,
    bool ControllerCleanupPending,
    bool CompanionPipeActive,
    bool WidgetsSuppressionReady,
    bool WidgetsSnapshotCaptured,
    bool WidgetsSnapshotValid,
    bool WidgetsSnapshotApplied,
    bool WidgetsNotificationPending,
    bool NativeWidgetsEntryPresent);

internal static class StandaloneTaskbarUiStatusPolicy
{
    public static StandaloneTaskbarUiStatusKind Resolve(
        bool standaloneModeRequested,
        bool weatherBarEnabled,
        bool weatherProviderEnabled,
        StandaloneTaskbarRuntimeState runtimeState,
        bool companionPipeActive,
        string? runtimeDetail)
    {
        if (!standaloneModeRequested)
            return StandaloneTaskbarUiStatusKind.Off;
        if (!weatherBarEnabled)
            return StandaloneTaskbarUiStatusKind.WeatherBarDisabled;
        if (!weatherProviderEnabled)
            return StandaloneTaskbarUiStatusKind.WeatherProviderDisabled;

        string detail = runtimeDetail ?? string.Empty;
        if (Contains(detail, "taskbar-control-suppressed"))
            return StandaloneTaskbarUiStatusKind.ControlSuppressed;
        if (Contains(detail, "windows-widgets-external-change-preserved") ||
            Contains(detail, "windows-widgets-invalid-snapshot") ||
            Contains(detail, "windows-widgets-conflicting-owner"))
        {
            return StandaloneTaskbarUiStatusKind.WidgetsConflict;
        }
        if (Contains(detail, "waiting-for-windows-widgets-removal") ||
            Contains(detail, "windows-widgets-lock-unavailable") ||
            Contains(detail, "windows-widgets-settings-failure") ||
            Contains(detail, "windows-widgets-registry-failure"))
        {
            return StandaloneTaskbarUiStatusKind.Preparing;
        }

        return runtimeState switch
        {
            StandaloneTaskbarRuntimeState.ControllerActiveUnverified =>
                StandaloneTaskbarUiStatusKind.ActiveUnverified,
            StandaloneTaskbarRuntimeState.BinaryMissing =>
                StandaloneTaskbarUiStatusKind.BinaryMissing,
            StandaloneTaskbarRuntimeState.Unsupported =>
                StandaloneTaskbarUiStatusKind.Unsupported,
            StandaloneTaskbarRuntimeState.TemporarilyUnavailable =>
                StandaloneTaskbarUiStatusKind.TemporarilyUnavailable,
            StandaloneTaskbarRuntimeState.Stopping =>
                StandaloneTaskbarUiStatusKind.Stopping,
            StandaloneTaskbarRuntimeState.Starting or
            StandaloneTaskbarRuntimeState.Recovery =>
                StandaloneTaskbarUiStatusKind.Preparing,
            StandaloneTaskbarRuntimeState.Rejected or
            StandaloneTaskbarRuntimeState.Ambiguous or
            StandaloneTaskbarRuntimeState.InvalidResponse or
            StandaloneTaskbarRuntimeState.Cancelled =>
                StandaloneTaskbarUiStatusKind.Failed,
            _ when companionPipeActive =>
                StandaloneTaskbarUiStatusKind.Preparing,
            _ => StandaloneTaskbarUiStatusKind.Fallback
        };
    }

    private static bool Contains(string value, string token)
        => value.Contains(token, StringComparison.Ordinal);
}
