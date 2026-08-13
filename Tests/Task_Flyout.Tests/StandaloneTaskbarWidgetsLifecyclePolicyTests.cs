using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class StandaloneTaskbarWidgetsLifecyclePolicyTests
{
    [Theory]
    [InlineData(false, 2, true, true, true)]
    [InlineData(true, 2, true, true, false)]
    [InlineData(false, 0, true, true, false)]
    [InlineData(false, 2, false, true, false)]
    [InlineData(false, 2, true, false, false)]
    public void Standalone_suppression_is_limited_to_an_eligible_request(
        bool launchSuppressed,
        int modeValue,
        bool weatherBarEnabled,
        bool providerEnabled,
        bool expected)
        => Assert.Equal(
            expected,
            StandaloneTaskbarWidgetsLifecyclePolicy.IsStandaloneDesired(
                launchSuppressed,
                (WeatherBarMode)modeValue,
                weatherBarEnabled,
                providerEnabled));

    [Fact]
    public void Diagnostic_only_rejection_blocks_repeated_standalone_request()
        => Assert.False(
            StandaloneTaskbarWidgetsLifecyclePolicy.IsStandaloneDesired(
                launchSuppressed: false,
                requestedMode: WeatherBarMode.StandaloneTaskbar,
                weatherBarEnabled: true,
                weatherProviderEnabled: true,
                diagnosticOnlyRejected: true));

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void Restore_waits_for_controller_cleanup_and_skips_suppressed_launches(
        bool launchSuppressed,
        bool standaloneDesired,
        bool cleanupPending,
        bool expected)
        => Assert.Equal(
            expected,
            StandaloneTaskbarWidgetsLifecyclePolicy.CanRestore(
                launchSuppressed,
                standaloneDesired,
                cleanupPending));

    [Fact]
    public void Verification_covers_active_ownership_and_bounded_recovery()
    {
        Assert.True(StandaloneTaskbarWidgetsLifecyclePolicy.ShouldKeepVerification(
            Result(
                StandaloneTaskbarWidgetsResultKind.RecoveredSuppression,
                succeeded: true,
                suppressed: true,
                owns: true),
            nativeEntryPointPresent: false));
        Assert.False(StandaloneTaskbarWidgetsLifecyclePolicy.ShouldKeepVerification(
            Result(
                StandaloneTaskbarWidgetsResultKind.AlreadySuppressedUnowned,
                succeeded: true,
                suppressed: true,
                owns: false),
            nativeEntryPointPresent: false));
        Assert.True(StandaloneTaskbarWidgetsLifecyclePolicy.ShouldKeepVerification(
            Result(
                StandaloneTaskbarWidgetsResultKind.Suppressed,
                succeeded: true,
                suppressed: true),
            nativeEntryPointPresent: true));
        Assert.True(StandaloneTaskbarWidgetsLifecyclePolicy.ShouldKeepVerification(
            Result(
                StandaloneTaskbarWidgetsResultKind.NoOwnership,
                succeeded: true,
                notificationPending: true),
            nativeEntryPointPresent: false));
        Assert.True(StandaloneTaskbarWidgetsLifecyclePolicy.ShouldKeepVerification(
            Result(
                StandaloneTaskbarWidgetsResultKind.LockUnavailable,
                succeeded: false),
            nativeEntryPointPresent: false));
        Assert.True(StandaloneTaskbarWidgetsLifecyclePolicy.ShouldKeepVerification(
            Result(
                StandaloneTaskbarWidgetsResultKind.RegistryFailure,
                succeeded: false,
                owns: true),
            nativeEntryPointPresent: false));
        Assert.False(StandaloneTaskbarWidgetsLifecyclePolicy.ShouldKeepVerification(
            Result(
                StandaloneTaskbarWidgetsResultKind.InvalidSnapshot,
                succeeded: false,
                owns: true),
            nativeEntryPointPresent: false));
        Assert.False(StandaloneTaskbarWidgetsLifecyclePolicy.ShouldKeepVerification(
            Result(
                StandaloneTaskbarWidgetsResultKind.ExternalChangePreserved,
                succeeded: false,
                owns: true),
            nativeEntryPointPresent: false));
    }

    private static StandaloneTaskbarWidgetsResult Result(
        StandaloneTaskbarWidgetsResultKind kind,
        bool succeeded,
        bool suppressed = false,
        bool owns = false,
        bool notificationPending = false)
        => new(
            kind,
            succeeded,
            RegistryChanged: false,
            IsEffectivelySuppressed: suppressed,
            OwnsSetting: owns,
            NotificationPending: notificationPending);
}
