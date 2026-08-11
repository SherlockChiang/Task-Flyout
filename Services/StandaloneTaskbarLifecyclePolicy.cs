using System;

namespace Task_Flyout.Services;

internal enum StandaloneTaskbarRuntimeState
{
    Disabled,
    Starting,
    ControllerActiveUnverified,
    Stopping,
    Inactive,
    BinaryMissing,
    Unsupported,
    TemporarilyUnavailable,
    Rejected,
    Ambiguous,
    Recovery,
    InvalidResponse,
    Cancelled
}

internal readonly record struct StandaloneTaskbarRuntimeStatus(
    StandaloneTaskbarRuntimeState State)
{
    // There is no mount-ready acknowledgement in the current native ABI. Keep
    // the existing app-owned bar for every requested standalone state until a
    // later protocol version can prove the XAML button is visible.
    public bool KeepTaskFlyoutFallback => State != StandaloneTaskbarRuntimeState.Disabled;

    public string DiagnosticKey => State switch
    {
        StandaloneTaskbarRuntimeState.Disabled => "disabled",
        StandaloneTaskbarRuntimeState.Starting => "starting",
        StandaloneTaskbarRuntimeState.ControllerActiveUnverified => "controller-active-unverified",
        StandaloneTaskbarRuntimeState.Stopping => "stopping",
        StandaloneTaskbarRuntimeState.Inactive => "inactive",
        StandaloneTaskbarRuntimeState.BinaryMissing => "binary-missing",
        StandaloneTaskbarRuntimeState.Unsupported => "unsupported",
        StandaloneTaskbarRuntimeState.TemporarilyUnavailable => "temporarily-unavailable",
        StandaloneTaskbarRuntimeState.Rejected => "rejected",
        StandaloneTaskbarRuntimeState.Ambiguous => "ambiguous",
        StandaloneTaskbarRuntimeState.Recovery => "recovery",
        StandaloneTaskbarRuntimeState.InvalidResponse => "invalid-response",
        StandaloneTaskbarRuntimeState.Cancelled => "cancelled",
        _ => "recovery"
    };
}

internal static class StandaloneTaskbarLifecyclePolicy
{
    public static StandaloneTaskbarRuntimeState MapProbe(
        StandaloneTaskbarBrokerResultKind result)
        => result switch
        {
            StandaloneTaskbarBrokerResultKind.ProbeSupported =>
                StandaloneTaskbarRuntimeState.Starting,
            StandaloneTaskbarBrokerResultKind.BinaryMissing =>
                StandaloneTaskbarRuntimeState.BinaryMissing,
            StandaloneTaskbarBrokerResultKind.Unsupported =>
                StandaloneTaskbarRuntimeState.Unsupported,
            StandaloneTaskbarBrokerResultKind.TemporarilyUnavailable or
            StandaloneTaskbarBrokerResultKind.TimedOut =>
                StandaloneTaskbarRuntimeState.TemporarilyUnavailable,
            StandaloneTaskbarBrokerResultKind.Cancelled =>
                StandaloneTaskbarRuntimeState.Cancelled,
            StandaloneTaskbarBrokerResultKind.InvalidResponse =>
                StandaloneTaskbarRuntimeState.InvalidResponse,
            _ => StandaloneTaskbarRuntimeState.Recovery
        };

    public static StandaloneTaskbarRuntimeState MapStart(
        StandaloneTaskbarBrokerResultKind result)
        => result switch
        {
            StandaloneTaskbarBrokerResultKind.ControllerActiveUnverified =>
                StandaloneTaskbarRuntimeState.ControllerActiveUnverified,
            StandaloneTaskbarBrokerResultKind.BinaryMissing =>
                StandaloneTaskbarRuntimeState.BinaryMissing,
            StandaloneTaskbarBrokerResultKind.Unsupported =>
                StandaloneTaskbarRuntimeState.Unsupported,
            StandaloneTaskbarBrokerResultKind.TemporarilyUnavailable or
            StandaloneTaskbarBrokerResultKind.TimedOut =>
                StandaloneTaskbarRuntimeState.TemporarilyUnavailable,
            StandaloneTaskbarBrokerResultKind.Rejected =>
                StandaloneTaskbarRuntimeState.Rejected,
            StandaloneTaskbarBrokerResultKind.Ambiguous =>
                StandaloneTaskbarRuntimeState.Ambiguous,
            StandaloneTaskbarBrokerResultKind.Cancelled =>
                StandaloneTaskbarRuntimeState.Cancelled,
            StandaloneTaskbarBrokerResultKind.InvalidResponse =>
                StandaloneTaskbarRuntimeState.InvalidResponse,
            _ => StandaloneTaskbarRuntimeState.Recovery
        };

    public static StandaloneTaskbarRuntimeState MapStop(
        StandaloneTaskbarBrokerResultKind result)
        => result switch
        {
            StandaloneTaskbarBrokerResultKind.ControllerInactive =>
                StandaloneTaskbarRuntimeState.Inactive,
            StandaloneTaskbarBrokerResultKind.BinaryMissing =>
                StandaloneTaskbarRuntimeState.BinaryMissing,
            StandaloneTaskbarBrokerResultKind.Unsupported =>
                StandaloneTaskbarRuntimeState.Unsupported,
            StandaloneTaskbarBrokerResultKind.TemporarilyUnavailable or
            StandaloneTaskbarBrokerResultKind.TimedOut =>
                StandaloneTaskbarRuntimeState.TemporarilyUnavailable,
            StandaloneTaskbarBrokerResultKind.Rejected =>
                StandaloneTaskbarRuntimeState.Rejected,
            StandaloneTaskbarBrokerResultKind.Ambiguous =>
                StandaloneTaskbarRuntimeState.Ambiguous,
            StandaloneTaskbarBrokerResultKind.Cancelled =>
                StandaloneTaskbarRuntimeState.Cancelled,
            StandaloneTaskbarBrokerResultKind.InvalidResponse =>
                StandaloneTaskbarRuntimeState.InvalidResponse,
            _ => StandaloneTaskbarRuntimeState.Recovery
        };
}
