using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class StandaloneTaskbarLifecyclePolicyTests
{
    [Fact]
    public void Every_requested_state_keeps_the_safe_fallback_until_mount_ready_exists()
    {
        foreach (StandaloneTaskbarRuntimeState state in Enum.GetValues<StandaloneTaskbarRuntimeState>())
        {
            var status = new StandaloneTaskbarRuntimeStatus(state);

            Assert.Equal(
                state != StandaloneTaskbarRuntimeState.Disabled,
                status.KeepTaskFlyoutFallback);
        }
    }

    [Fact]
    public void Diagnostic_keys_are_fixed_complete_and_unique()
    {
        (StandaloneTaskbarRuntimeState State, string Key)[] expected =
        [
            (StandaloneTaskbarRuntimeState.Disabled, "disabled"),
            (StandaloneTaskbarRuntimeState.Starting, "starting"),
            (StandaloneTaskbarRuntimeState.ControllerActiveUnverified, "controller-active-unverified"),
            (StandaloneTaskbarRuntimeState.Stopping, "stopping"),
            (StandaloneTaskbarRuntimeState.Inactive, "inactive"),
            (StandaloneTaskbarRuntimeState.BinaryMissing, "binary-missing"),
            (StandaloneTaskbarRuntimeState.Unsupported, "unsupported"),
            (StandaloneTaskbarRuntimeState.TemporarilyUnavailable, "temporarily-unavailable"),
            (StandaloneTaskbarRuntimeState.Rejected, "rejected"),
            (StandaloneTaskbarRuntimeState.Ambiguous, "ambiguous"),
            (StandaloneTaskbarRuntimeState.Recovery, "recovery"),
            (StandaloneTaskbarRuntimeState.InvalidResponse, "invalid-response"),
            (StandaloneTaskbarRuntimeState.Cancelled, "cancelled")
        ];

        Assert.Equal(Enum.GetValues<StandaloneTaskbarRuntimeState>().Length, expected.Length);
        Assert.Equal(expected.Length, expected.Select(item => item.Key).Distinct().Count());

        foreach (var item in expected)
        {
            Assert.Equal(
                item.Key,
                new StandaloneTaskbarRuntimeStatus(item.State).DiagnosticKey);
        }
    }

    [Fact]
    public void Probe_result_matrix_is_complete()
    {
        AssertCompleteMatrix(
        [
            (StandaloneTaskbarBrokerResultKind.BinaryMissing, StandaloneTaskbarRuntimeState.BinaryMissing),
            (StandaloneTaskbarBrokerResultKind.ProbeSupported, StandaloneTaskbarRuntimeState.Starting),
            (StandaloneTaskbarBrokerResultKind.Unsupported, StandaloneTaskbarRuntimeState.Unsupported),
            (StandaloneTaskbarBrokerResultKind.TemporarilyUnavailable, StandaloneTaskbarRuntimeState.TemporarilyUnavailable),
            (StandaloneTaskbarBrokerResultKind.ControllerActiveUnverified, StandaloneTaskbarRuntimeState.Recovery),
            (StandaloneTaskbarBrokerResultKind.ControllerInactive, StandaloneTaskbarRuntimeState.Recovery),
            (StandaloneTaskbarBrokerResultKind.Rejected, StandaloneTaskbarRuntimeState.Recovery),
            (StandaloneTaskbarBrokerResultKind.Ambiguous, StandaloneTaskbarRuntimeState.Recovery),
            (StandaloneTaskbarBrokerResultKind.Failed, StandaloneTaskbarRuntimeState.Recovery),
            (StandaloneTaskbarBrokerResultKind.InvalidResponse, StandaloneTaskbarRuntimeState.InvalidResponse),
            (StandaloneTaskbarBrokerResultKind.TimedOut, StandaloneTaskbarRuntimeState.TemporarilyUnavailable),
            (StandaloneTaskbarBrokerResultKind.Cancelled, StandaloneTaskbarRuntimeState.Cancelled)
        ], StandaloneTaskbarLifecyclePolicy.MapProbe);
    }

    [Fact]
    public void Start_result_matrix_never_claims_that_the_button_is_visible()
    {
        AssertCompleteMatrix(
        [
            (StandaloneTaskbarBrokerResultKind.BinaryMissing, StandaloneTaskbarRuntimeState.BinaryMissing),
            (StandaloneTaskbarBrokerResultKind.ProbeSupported, StandaloneTaskbarRuntimeState.Recovery),
            (StandaloneTaskbarBrokerResultKind.Unsupported, StandaloneTaskbarRuntimeState.Unsupported),
            (StandaloneTaskbarBrokerResultKind.TemporarilyUnavailable, StandaloneTaskbarRuntimeState.TemporarilyUnavailable),
            (StandaloneTaskbarBrokerResultKind.ControllerActiveUnverified, StandaloneTaskbarRuntimeState.ControllerActiveUnverified),
            (StandaloneTaskbarBrokerResultKind.ControllerInactive, StandaloneTaskbarRuntimeState.Recovery),
            (StandaloneTaskbarBrokerResultKind.Rejected, StandaloneTaskbarRuntimeState.Rejected),
            (StandaloneTaskbarBrokerResultKind.Ambiguous, StandaloneTaskbarRuntimeState.Ambiguous),
            (StandaloneTaskbarBrokerResultKind.Failed, StandaloneTaskbarRuntimeState.Recovery),
            (StandaloneTaskbarBrokerResultKind.InvalidResponse, StandaloneTaskbarRuntimeState.InvalidResponse),
            (StandaloneTaskbarBrokerResultKind.TimedOut, StandaloneTaskbarRuntimeState.TemporarilyUnavailable),
            (StandaloneTaskbarBrokerResultKind.Cancelled, StandaloneTaskbarRuntimeState.Cancelled)
        ], StandaloneTaskbarLifecyclePolicy.MapStart);

        Assert.True(
            new StandaloneTaskbarRuntimeStatus(
                StandaloneTaskbarLifecyclePolicy.MapStart(
                    StandaloneTaskbarBrokerResultKind.ControllerActiveUnverified))
                .KeepTaskFlyoutFallback);
    }

    [Fact]
    public void Stop_result_matrix_is_complete()
    {
        AssertCompleteMatrix(
        [
            (StandaloneTaskbarBrokerResultKind.BinaryMissing, StandaloneTaskbarRuntimeState.BinaryMissing),
            (StandaloneTaskbarBrokerResultKind.ProbeSupported, StandaloneTaskbarRuntimeState.Recovery),
            (StandaloneTaskbarBrokerResultKind.Unsupported, StandaloneTaskbarRuntimeState.Unsupported),
            (StandaloneTaskbarBrokerResultKind.TemporarilyUnavailable, StandaloneTaskbarRuntimeState.TemporarilyUnavailable),
            (StandaloneTaskbarBrokerResultKind.ControllerActiveUnverified, StandaloneTaskbarRuntimeState.Recovery),
            (StandaloneTaskbarBrokerResultKind.ControllerInactive, StandaloneTaskbarRuntimeState.Inactive),
            (StandaloneTaskbarBrokerResultKind.Rejected, StandaloneTaskbarRuntimeState.Rejected),
            (StandaloneTaskbarBrokerResultKind.Ambiguous, StandaloneTaskbarRuntimeState.Ambiguous),
            (StandaloneTaskbarBrokerResultKind.Failed, StandaloneTaskbarRuntimeState.Recovery),
            (StandaloneTaskbarBrokerResultKind.InvalidResponse, StandaloneTaskbarRuntimeState.InvalidResponse),
            (StandaloneTaskbarBrokerResultKind.TimedOut, StandaloneTaskbarRuntimeState.TemporarilyUnavailable),
            (StandaloneTaskbarBrokerResultKind.Cancelled, StandaloneTaskbarRuntimeState.Cancelled)
        ], StandaloneTaskbarLifecyclePolicy.MapStop);
    }

    private static void AssertCompleteMatrix(
        (StandaloneTaskbarBrokerResultKind Result, StandaloneTaskbarRuntimeState Expected)[] matrix,
        Func<StandaloneTaskbarBrokerResultKind, StandaloneTaskbarRuntimeState> map)
    {
        Assert.Equal(Enum.GetValues<StandaloneTaskbarBrokerResultKind>().Length, matrix.Length);
        Assert.Equal(matrix.Length, matrix.Select(item => item.Result).Distinct().Count());

        foreach (var item in matrix)
            Assert.Equal(item.Expected, map(item.Result));
    }
}
