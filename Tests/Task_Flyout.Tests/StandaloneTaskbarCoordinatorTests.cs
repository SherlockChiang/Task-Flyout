using System.Collections.Concurrent;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class StandaloneTaskbarCoordinatorTests
{
    private const uint ControllerProcessId = 4242;
    private const uint ControllerNonce = 0xA17E52C3u;

    [Fact]
    public async Task Enable_probes_then_starts_without_claiming_mount_ready()
    {
        var client = new FakeClient();
        await using var coordinator = new StandaloneTaskbarCoordinator(client);
        var states = new List<StandaloneTaskbarRuntimeState>();
        coordinator.StatusChanged += status => states.Add(status.State);

        await coordinator.SetEnabledAsync(true);

        Assert.Equal(new[] { "probe", "start", "status" }, client.Calls);
        Assert.Equal(
            StandaloneTaskbarRuntimeState.ControllerActiveUnverified,
            coordinator.Status.State);
        Assert.True(coordinator.RequiresStop);
        Assert.True(coordinator.Status.KeepTaskFlyoutFallback);
        Assert.Equal(
            new[]
            {
                StandaloneTaskbarRuntimeState.Starting,
                StandaloneTaskbarRuntimeState.ControllerActiveUnverified
            },
            states);
    }

    [Fact]
    public async Task Live_mount_proof_closes_the_safe_fallback()
    {
        var client = new FakeClient
        {
            Status = _ => Result(
                StandaloneTaskbarBrokerResultKind.ControllerMountReady)
        };
        await using var coordinator = new StandaloneTaskbarCoordinator(client);
        var states = new List<StandaloneTaskbarRuntimeState>();
        coordinator.StatusChanged += status => states.Add(status.State);

        await coordinator.SetEnabledAsync(true);

        Assert.Equal(new[] { "probe", "start", "status" }, client.Calls);
        Assert.Equal(
            StandaloneTaskbarRuntimeState.MountReady,
            coordinator.Status.State);
        Assert.False(coordinator.Status.KeepTaskFlyoutFallback);
        Assert.Equal(
            new[]
            {
                StandaloneTaskbarRuntimeState.Starting,
                StandaloneTaskbarRuntimeState.ControllerActiveUnverified,
                StandaloneTaskbarRuntimeState.MountReady
            },
            states);
    }

    [Fact]
    public async Task Diagnostic_only_start_is_stopped_and_never_mounts()
    {
        var client = new FakeClient
        {
            Start = _ => Task.FromResult(new StandaloneTaskbarBrokerResult(
                StandaloneTaskbarBrokerResultKind.ControllerDiagnosticOnly)),
            Status = _ => Result(
                StandaloneTaskbarBrokerResultKind.ControllerDiagnosticOnly)
        };
        await using var coordinator = new StandaloneTaskbarCoordinator(client);
        var states = new List<StandaloneTaskbarRuntimeState>();
        coordinator.StatusChanged += status =>
        {
            // A diagnostic result is terminal only after the synchronous
            // cleanup attempt has resolved the controller lease.
            if (status.State == StandaloneTaskbarRuntimeState.DiagnosticOnly)
            {
                Assert.False(coordinator.IsRequested);
                Assert.False(coordinator.RequiresStop);
            }
            states.Add(status.State);
        };

        await coordinator.SetEnabledAsync(true);

        Assert.Equal(new[] { "probe", "start", "stop" }, client.Calls);
        Assert.Equal(
            new[]
            {
                StandaloneTaskbarRuntimeState.Starting,
                StandaloneTaskbarRuntimeState.DiagnosticOnly
            },
            states);
        Assert.Equal(
            StandaloneTaskbarRuntimeState.DiagnosticOnly,
            coordinator.Status.State);
        Assert.False(coordinator.IsRequested);
        Assert.False(coordinator.Status.State ==
            StandaloneTaskbarRuntimeState.MountReady);
        Assert.True(coordinator.Status.KeepTaskFlyoutFallback);
        Assert.True(coordinator.DiagnosticOnlyRejected);
    }

    [Fact]
    public async Task Diagnostic_only_state_is_published_only_after_cleanup()
    {
        var stopEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStop = new TaskCompletionSource<StandaloneTaskbarBrokerResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeClient
        {
            Start = _ => Result(
                StandaloneTaskbarBrokerResultKind.ControllerDiagnosticOnly),
            Stop = _ =>
            {
                stopEntered.TrySetResult(true);
                return releaseStop.Task;
            }
        };
        await using var coordinator = new StandaloneTaskbarCoordinator(client);
        var states = new ConcurrentQueue<StandaloneTaskbarRuntimeState>();
        coordinator.StatusChanged += status => states.Enqueue(status.State);

        Task enable = coordinator.SetEnabledAsync(true);
        await stopEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(
            StandaloneTaskbarRuntimeState.Starting,
            coordinator.Status.State);
        Assert.DoesNotContain(
            StandaloneTaskbarRuntimeState.DiagnosticOnly,
            states);

        releaseStop.SetResult(new(
            StandaloneTaskbarBrokerResultKind.ControllerInactive));
        await enable.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(
            new[]
            {
                StandaloneTaskbarRuntimeState.Starting,
                StandaloneTaskbarRuntimeState.DiagnosticOnly
            },
            states);
        Assert.False(coordinator.RequiresStop);
    }

    [Fact]
    public async Task Delayed_diagnostic_status_is_stopped_before_fallback_state_is_published()
    {
        var client = new FakeClient
        {
            Status = _ => Result(
                StandaloneTaskbarBrokerResultKind.ControllerDiagnosticOnly)
        };
        await using var coordinator = new StandaloneTaskbarCoordinator(client);
        var states = new List<StandaloneTaskbarRuntimeState>();
        coordinator.StatusChanged += status =>
        {
            if (status.State == StandaloneTaskbarRuntimeState.DiagnosticOnly)
            {
                Assert.False(coordinator.IsRequested);
                Assert.False(coordinator.RequiresStop);
            }
            states.Add(status.State);
        };

        await coordinator.SetEnabledAsync(true);

        Assert.Equal(new[] { "probe", "start", "status", "stop" }, client.Calls);
        Assert.Equal(
            new[]
            {
                StandaloneTaskbarRuntimeState.Starting,
                StandaloneTaskbarRuntimeState.ControllerActiveUnverified,
                StandaloneTaskbarRuntimeState.DiagnosticOnly
            },
            states);
        Assert.Equal(
            StandaloneTaskbarRuntimeState.DiagnosticOnly,
            coordinator.Status.State);
        Assert.True(coordinator.Status.KeepTaskFlyoutFallback);
        Assert.True(coordinator.DiagnosticOnlyRejected);
    }

    [Fact]
    public async Task Failed_diagnostic_cleanup_keeps_recovery_obligation()
    {
        var client = new FakeClient
        {
            Start = _ => Result(
                StandaloneTaskbarBrokerResultKind.ControllerDiagnosticOnly),
            Stop = _ => Result(StandaloneTaskbarBrokerResultKind.Ambiguous)
        };
        await using var coordinator = new StandaloneTaskbarCoordinator(client);

        await coordinator.SetEnabledAsync(true);

        Assert.Equal(new[] { "probe", "start", "stop" }, client.Calls);
        Assert.Equal(
            StandaloneTaskbarRuntimeState.Recovery,
            coordinator.Status.State);
        Assert.True(coordinator.RequiresStop);
        Assert.False(coordinator.IsRequested);
        Assert.True(coordinator.DiagnosticOnlyRejected);
    }

    [Fact]
    public async Task Diagnostic_cleanup_is_bounded_when_client_ignores_cancellation()
    {
        var never = new TaskCompletionSource<StandaloneTaskbarBrokerResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeClient
        {
            Start = _ => Result(
                StandaloneTaskbarBrokerResultKind.ControllerDiagnosticOnly),
            Stop = _ => never.Task
        };
        await using var coordinator = new StandaloneTaskbarCoordinator(
            client,
            TimeSpan.FromMilliseconds(50));

        await coordinator.SetEnabledAsync(true)
            .WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(
            StandaloneTaskbarRuntimeState.Recovery,
            coordinator.Status.State);
        Assert.True(coordinator.RequiresStop);
        Assert.True(coordinator.DiagnosticOnlyRejected);

        never.TrySetResult(new(
            StandaloneTaskbarBrokerResultKind.ControllerInactive));
        await WaitUntilAsync(
            () => client.ActiveCalls == 0,
            TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Mount_proof_without_a_controller_identity_keeps_fallback()
    {
        var client = new FakeClient
        {
            Start = _ => Result(
                StandaloneTaskbarBrokerResultKind.ControllerActiveUnverified),
            Status = _ => Result(
                StandaloneTaskbarBrokerResultKind.ControllerMountReady)
        };
        await using var coordinator = new StandaloneTaskbarCoordinator(client);

        await coordinator.SetEnabledAsync(true);

        Assert.Equal(
            StandaloneTaskbarRuntimeState.ControllerActiveUnverified,
            coordinator.Status.State);
        Assert.True(coordinator.Status.KeepTaskFlyoutFallback);
    }

    [Fact]
    public async Task Authenticated_reports_move_pending_ready_lost_and_ready()
    {
        await using var coordinator = new StandaloneTaskbarCoordinator(
            new FakeClient(),
            TimeSpan.FromSeconds(1),
            mountReadyPollInterval: TimeSpan.Zero,
            mountReadyMaxAttempts: 1);
        await coordinator.SetEnabledAsync(true);

        Assert.True(coordinator.ReportMountReadiness(Report(1, ready: true)));
        Assert.Equal(StandaloneTaskbarRuntimeState.MountReady, coordinator.Status.State);

        Assert.True(coordinator.ReportMountReadiness(Report(2, ready: false)));
        Assert.Equal(
            StandaloneTaskbarRuntimeState.ControllerActiveUnverified,
            coordinator.Status.State);

        Assert.True(coordinator.ReportMountReadiness(Report(3, ready: true)));
        Assert.Equal(StandaloneTaskbarRuntimeState.MountReady, coordinator.Status.State);
    }

    [Fact]
    public async Task Forged_or_stale_mount_reports_cannot_renew_the_lease()
    {
        await using var coordinator = new StandaloneTaskbarCoordinator(
            new FakeClient(),
            TimeSpan.FromSeconds(1),
            mountReadyPollInterval: TimeSpan.Zero,
            mountReadyMaxAttempts: 1);
        await coordinator.SetEnabledAsync(true);

        Assert.False(coordinator.ReportMountReadiness(
            Report(1, ready: true) with { ClientProcessId = 9999 }));
        Assert.False(coordinator.ReportMountReadiness(
            Report(1, ready: true) with { ControllerNonce = 1 }));
        Assert.True(coordinator.ReportMountReadiness(Report(2, ready: true)));
        Assert.False(coordinator.ReportMountReadiness(Report(1, ready: true)));
        Assert.False(coordinator.ReportMountReadiness(Report(2, ready: false)));
        Assert.Equal(StandaloneTaskbarRuntimeState.MountReady, coordinator.Status.State);
    }

    [Fact]
    public async Task Authenticated_lost_beats_a_late_broker_ready_result()
    {
        var statusEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseStatus = new TaskCompletionSource<StandaloneTaskbarBrokerResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeClient
        {
            Status = _ =>
            {
                statusEntered.TrySetResult(true);
                return releaseStatus.Task;
            }
        };
        await using var coordinator = new StandaloneTaskbarCoordinator(client);

        Task enable = coordinator.SetEnabledAsync(true);
        await statusEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(coordinator.ReportMountReadiness(Report(1, ready: false)));
        releaseStatus.TrySetResult(new StandaloneTaskbarBrokerResult(
            StandaloneTaskbarBrokerResultKind.ControllerMountReady));
        await enable.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(
            StandaloneTaskbarRuntimeState.ControllerActiveUnverified,
            coordinator.Status.State);
        Assert.True(coordinator.Status.KeepTaskFlyoutFallback);
    }

    [Fact]
    public async Task Missing_heartbeat_expires_ready_and_restores_fallback()
    {
        await using var coordinator = new StandaloneTaskbarCoordinator(
            new FakeClient(),
            TimeSpan.FromSeconds(1),
            mountReadyPollInterval: TimeSpan.Zero,
            mountReadyMaxAttempts: 1,
            mountLeaseTimeout: TimeSpan.FromMilliseconds(40));
        await coordinator.SetEnabledAsync(true);
        Assert.True(coordinator.ReportMountReadiness(Report(1, ready: true)));

        await WaitUntilAsync(
            () => coordinator.Status.State ==
                StandaloneTaskbarRuntimeState.ControllerActiveUnverified,
            TimeSpan.FromSeconds(1));

        Assert.True(coordinator.Status.KeepTaskFlyoutFallback);
    }

    [Fact]
    public async Task Disable_revokes_identity_before_old_reports_arrive()
    {
        await using var coordinator = new StandaloneTaskbarCoordinator(
            new FakeClient(),
            TimeSpan.FromSeconds(1),
            mountReadyPollInterval: TimeSpan.Zero,
            mountReadyMaxAttempts: 1);
        await coordinator.SetEnabledAsync(true);
        Assert.True(coordinator.ReportMountReadiness(Report(1, ready: true)));

        await coordinator.SetEnabledAsync(false);

        Assert.False(coordinator.ReportMountReadiness(Report(2, ready: true)));
        Assert.Equal(StandaloneTaskbarRuntimeState.Disabled, coordinator.Status.State);
    }

    [Fact]
    public async Task Pending_mount_is_polled_with_a_strict_attempt_bound()
    {
        int statusCalls = 0;
        var client = new FakeClient
        {
            Status = _ => Result(
                ++statusCalls == 3
                    ? StandaloneTaskbarBrokerResultKind.ControllerMountReady
                    : StandaloneTaskbarBrokerResultKind.ControllerActiveUnverified)
        };
        await using var coordinator = new StandaloneTaskbarCoordinator(
            client,
            TimeSpan.FromSeconds(1),
            mountReadyPollInterval: TimeSpan.Zero,
            mountReadyMaxAttempts: 3);

        await coordinator.SetEnabledAsync(true);

        Assert.Equal(
            new[] { "probe", "start", "status", "status", "status" },
            client.Calls);
        Assert.Equal(3, statusCalls);
        Assert.Equal(
            StandaloneTaskbarRuntimeState.MountReady,
            coordinator.Status.State);
    }

    [Fact]
    public async Task Pending_mount_exhaustion_never_closes_fallback()
    {
        var client = new FakeClient
        {
            Status = _ => Result(
                StandaloneTaskbarBrokerResultKind.ControllerActiveUnverified)
        };
        await using var coordinator = new StandaloneTaskbarCoordinator(
            client,
            TimeSpan.FromSeconds(1),
            mountReadyPollInterval: TimeSpan.Zero,
            mountReadyMaxAttempts: 2);

        await coordinator.SetEnabledAsync(true);

        Assert.Equal(
            new[] { "probe", "start", "status", "status" },
            client.Calls);
        Assert.Equal(
            StandaloneTaskbarRuntimeState.ControllerActiveUnverified,
            coordinator.Status.State);
        Assert.True(coordinator.Status.KeepTaskFlyoutFallback);
    }

    [Fact]
    public async Task Status_failure_is_not_retried_or_promoted_to_ready()
    {
        var client = new FakeClient
        {
            Status = _ => Result(StandaloneTaskbarBrokerResultKind.TimedOut)
        };
        await using var coordinator = new StandaloneTaskbarCoordinator(
            client,
            TimeSpan.FromSeconds(1),
            mountReadyPollInterval: TimeSpan.Zero,
            mountReadyMaxAttempts: 5);

        await coordinator.SetEnabledAsync(true);

        Assert.Equal(new[] { "probe", "start", "status" }, client.Calls);
        Assert.Equal(
            StandaloneTaskbarRuntimeState.ControllerActiveUnverified,
            coordinator.Status.State);
        Assert.True(coordinator.Status.KeepTaskFlyoutFallback);
    }

    [Fact]
    public async Task Unsupported_probe_skips_start_and_keeps_fallback()
    {
        var client = new FakeClient
        {
            Probe = _ => Result(StandaloneTaskbarBrokerResultKind.Unsupported)
        };
        await using var coordinator = new StandaloneTaskbarCoordinator(client);

        await coordinator.SetEnabledAsync(true);

        Assert.Equal(new[] { "probe" }, client.Calls);
        Assert.Equal(
            StandaloneTaskbarRuntimeState.Unsupported,
            coordinator.Status.State);
        Assert.True(coordinator.IsRequested);
        Assert.True(coordinator.Status.KeepTaskFlyoutFallback);
    }

    [Fact]
    public async Task Adopted_cleanup_lease_runs_an_idempotent_stop_before_any_start()
    {
        var client = new FakeClient();
        await using var coordinator = new StandaloneTaskbarCoordinator(
            client,
            TimeSpan.FromSeconds(1),
            cleanupRequired: true);

        Assert.True(coordinator.RequiresStop);
        await coordinator.RefreshAsync();

        Assert.Equal(new[] { "stop" }, client.Calls);
        Assert.False(coordinator.RequiresStop);
        Assert.Equal(StandaloneTaskbarRuntimeState.Disabled, coordinator.Status.State);
    }

    [Fact]
    public async Task Rapid_disable_cancels_stale_start_and_serializes_cleanup()
    {
        var startEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeClient
        {
            Start = async cancellationToken =>
            {
                startEntered.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new(StandaloneTaskbarBrokerResultKind.ControllerActiveUnverified);
            }
        };
        await using var coordinator = new StandaloneTaskbarCoordinator(client);
        var states = new ConcurrentQueue<StandaloneTaskbarRuntimeState>();
        coordinator.StatusChanged += status => states.Enqueue(status.State);

        Task enable = coordinator.SetEnabledAsync(true);
        await startEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task disable = coordinator.SetEnabledAsync(false);
        await Task.WhenAll(enable, disable).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(new[] { "probe", "start", "stop" }, client.Calls);
        Assert.Equal(1, client.MaxConcurrentCalls);
        Assert.Equal(StandaloneTaskbarRuntimeState.Disabled, coordinator.Status.State);
        Assert.False(coordinator.IsRequested);
        Assert.DoesNotContain(
            StandaloneTaskbarRuntimeState.ControllerActiveUnverified,
            states);
    }

    [Fact]
    public async Task Disable_during_probe_cancels_without_starting_or_stopping_controller()
    {
        var probeEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeClient
        {
            Probe = async cancellationToken =>
            {
                probeEntered.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new(StandaloneTaskbarBrokerResultKind.ProbeSupported);
            }
        };
        await using var coordinator = new StandaloneTaskbarCoordinator(client);

        Task enable = coordinator.SetEnabledAsync(true);
        await probeEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task disable = coordinator.SetEnabledAsync(false);
        await Task.WhenAll(enable, disable).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(new[] { "probe" }, client.Calls);
        Assert.False(coordinator.RequiresStop);
        Assert.Equal(StandaloneTaskbarRuntimeState.Disabled, coordinator.Status.State);
    }

    [Fact]
    public async Task Explicit_refresh_reprobes_only_while_requested()
    {
        var client = new FakeClient();
        await using var coordinator = new StandaloneTaskbarCoordinator(client);
        var states = new List<StandaloneTaskbarRuntimeState>();
        coordinator.StatusChanged += status => states.Add(status.State);

        await coordinator.RefreshAsync();
        Assert.Empty(client.Calls);

        await coordinator.SetEnabledAsync(true);
        await coordinator.RefreshAsync();

        Assert.Equal(
            new[] { "probe", "start", "status", "probe", "start", "status" },
            client.Calls);
        Assert.Contains(StandaloneTaskbarRuntimeState.Recovery, states);
        Assert.Equal(
            StandaloneTaskbarRuntimeState.ControllerActiveUnverified,
            coordinator.Status.State);
    }

    [Fact]
    public async Task Ambiguous_start_requires_an_idempotent_stop_on_disable()
    {
        var client = new FakeClient
        {
            Start = _ => Result(StandaloneTaskbarBrokerResultKind.Ambiguous)
        };
        await using var coordinator = new StandaloneTaskbarCoordinator(client);

        await coordinator.SetEnabledAsync(true);
        Assert.Equal(StandaloneTaskbarRuntimeState.Ambiguous, coordinator.Status.State);

        await coordinator.SetEnabledAsync(false);

        Assert.Equal(new[] { "probe", "start", "stop" }, client.Calls);
        Assert.Equal(StandaloneTaskbarRuntimeState.Disabled, coordinator.Status.State);
    }

    [Fact]
    public async Task Throwing_start_maps_to_recovery_and_preserves_cleanup_obligation()
    {
        var client = new FakeClient
        {
            Start = _ => throw new InvalidOperationException()
        };
        await using var coordinator = new StandaloneTaskbarCoordinator(client);

        await coordinator.SetEnabledAsync(true);
        Assert.Equal(StandaloneTaskbarRuntimeState.Recovery, coordinator.Status.State);

        await coordinator.SetEnabledAsync(false);

        Assert.Equal(new[] { "probe", "start", "stop" }, client.Calls);
        Assert.Equal(StandaloneTaskbarRuntimeState.Disabled, coordinator.Status.State);
    }

    [Fact]
    public async Task Rejected_stop_remains_visible_and_explicit_refresh_retries_it()
    {
        int stopCount = 0;
        var client = new FakeClient
        {
            Stop = _ => Result(
                ++stopCount == 1
                    ? StandaloneTaskbarBrokerResultKind.Rejected
                    : StandaloneTaskbarBrokerResultKind.ControllerInactive)
        };
        await using var coordinator = new StandaloneTaskbarCoordinator(client);

        await coordinator.SetEnabledAsync(true);
        await coordinator.SetEnabledAsync(false);
        Assert.Equal(StandaloneTaskbarRuntimeState.Rejected, coordinator.Status.State);

        await coordinator.RefreshAsync();

        Assert.Equal(2, stopCount);
        Assert.False(coordinator.RequiresStop);
        Assert.Equal(StandaloneTaskbarRuntimeState.Disabled, coordinator.Status.State);
    }

    [Fact]
    public async Task Dispose_cancels_pending_start_and_attempts_bounded_stop()
    {
        var startEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeClient
        {
            Start = async cancellationToken =>
            {
                startEntered.TrySetResult(true);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new(StandaloneTaskbarBrokerResultKind.ControllerActiveUnverified);
            }
        };
        var coordinator = new StandaloneTaskbarCoordinator(client);

        Task enable = coordinator.SetEnabledAsync(true);
        await startEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        await enable.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(new[] { "probe", "start", "stop" }, client.Calls);
        Assert.Equal(StandaloneTaskbarRuntimeState.Disabled, coordinator.Status.State);
        await coordinator.SetEnabledAsync(true);
        Assert.Equal(new[] { "probe", "start", "stop" }, client.Calls);
    }

    [Fact]
    public async Task Dispose_is_idempotent_and_uses_one_final_stop()
    {
        var client = new FakeClient();
        var coordinator = new StandaloneTaskbarCoordinator(client);
        await coordinator.SetEnabledAsync(true);

        Task first = coordinator.DisposeAsync().AsTask();
        Task second = coordinator.DisposeAsync().AsTask();
        await Task.WhenAll(first, second);

        Assert.Same(first, second);
        Assert.Equal(new[] { "probe", "start", "status", "stop" }, client.Calls);
    }

    [Fact]
    public async Task Faulting_status_subscriber_cannot_abort_controller_cleanup()
    {
        var client = new FakeClient();
        await using var coordinator = new StandaloneTaskbarCoordinator(client);
        coordinator.StatusChanged += _ => throw new InvalidOperationException();

        await coordinator.SetEnabledAsync(true);
        await coordinator.SetEnabledAsync(false);

        Assert.Equal(new[] { "probe", "start", "status", "stop" }, client.Calls);
        Assert.Equal(StandaloneTaskbarRuntimeState.Disabled, coordinator.Status.State);
    }

    [Fact]
    public async Task Repeating_the_same_desired_state_reuses_the_latest_operation()
    {
        var client = new FakeClient();
        await using var coordinator = new StandaloneTaskbarCoordinator(client);

        Task first = coordinator.SetEnabledAsync(true);
        Task second = coordinator.SetEnabledAsync(true);
        await Task.WhenAll(first, second);

        Assert.Same(first, second);
        Assert.Equal(new[] { "probe", "start", "status" }, client.Calls);
    }

    [Fact]
    public async Task Dispose_has_a_hard_bound_when_stop_ignores_cancellation()
    {
        var never = new TaskCompletionSource<StandaloneTaskbarBrokerResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeClient
        {
            Stop = _ => never.Task
        };
        var coordinator = new StandaloneTaskbarCoordinator(
            client,
            TimeSpan.FromMilliseconds(50));

        await coordinator.SetEnabledAsync(true);
        await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(new[] { "probe", "start", "status", "stop" }, client.Calls);
        Assert.Equal(StandaloneTaskbarRuntimeState.Cancelled, coordinator.Status.State);
        never.TrySetResult(new(StandaloneTaskbarBrokerResultKind.ControllerInactive));
        await WaitUntilAsync(
            () => client.ActiveCalls == 0,
            TimeSpan.FromSeconds(1));
        Assert.Equal(StandaloneTaskbarRuntimeState.Cancelled, coordinator.Status.State);
    }

    [Fact]
    public async Task Reentrant_status_change_cannot_deliver_an_old_state_after_the_new_one()
    {
        var client = new FakeClient();
        await using var coordinator = new StandaloneTaskbarCoordinator(client);
        Task? disable = null;
        var observedBySecondSubscriber = new List<StandaloneTaskbarRuntimeState>();
        coordinator.StatusChanged += status =>
        {
            if (status.State == StandaloneTaskbarRuntimeState.ControllerActiveUnverified)
                disable = coordinator.SetEnabledAsync(false);
        };
        coordinator.StatusChanged += status => observedBySecondSubscriber.Add(status.State);

        await coordinator.SetEnabledAsync(true);
        if (disable != null)
            await disable;

        int stopping = observedBySecondSubscriber.IndexOf(
            StandaloneTaskbarRuntimeState.Stopping);
        int active = observedBySecondSubscriber.IndexOf(
            StandaloneTaskbarRuntimeState.ControllerActiveUnverified);
        Assert.True(stopping >= 0);
        Assert.True(active < 0 || active < stopping);
        Assert.Equal(StandaloneTaskbarRuntimeState.Disabled, coordinator.Status.State);
    }

    private static Task<StandaloneTaskbarBrokerResult> Result(
        StandaloneTaskbarBrokerResultKind kind)
        => Task.FromResult(new StandaloneTaskbarBrokerResult(kind));

    private static Task<StandaloneTaskbarBrokerResult> ControllerResult()
        => Task.FromResult(new StandaloneTaskbarBrokerResult(
            StandaloneTaskbarBrokerResultKind.ControllerActiveUnverified,
            new StandaloneTaskbarControllerIdentity(
                ControllerProcessId,
                ControllerNonce)));

    private static WeatherCompanionMountReadinessReport Report(
        ulong generation,
        bool ready)
        => new(
            ControllerProcessId,
            ControllerNonce,
            generation,
            ready
                ? WeatherCompanionMountState.Ready
                : WeatherCompanionMountState.Lost);

    private static async Task WaitUntilAsync(
        Func<bool> predicate,
        TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!predicate() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(predicate());
    }

    private sealed class FakeClient : IStandaloneTaskbarBrokerClient
    {
        public Func<CancellationToken, Task<StandaloneTaskbarBrokerResult>> Probe { get; init; } =
            _ => Result(StandaloneTaskbarBrokerResultKind.ProbeSupported);

        public Func<CancellationToken, Task<StandaloneTaskbarBrokerResult>> Start { get; init; } =
            _ => ControllerResult();

        public Func<CancellationToken, Task<StandaloneTaskbarBrokerResult>> Stop { get; init; } =
            _ => Result(StandaloneTaskbarBrokerResultKind.ControllerInactive);

        public Func<CancellationToken, Task<StandaloneTaskbarBrokerResult>> Status { get; init; } =
            _ => Result(StandaloneTaskbarBrokerResultKind.Failed);

        public ConcurrentQueue<string> CallQueue { get; } = new();

        public string[] Calls => CallQueue.ToArray();

        public int MaxConcurrentCalls => Volatile.Read(ref _maxConcurrentCalls);

        public int ActiveCalls => Volatile.Read(ref _activeCalls);

        private int _activeCalls;
        private int _maxConcurrentCalls;

        public Task<StandaloneTaskbarBrokerResult> ProbeAsync(
            CancellationToken cancellationToken = default)
            => InvokeAsync("probe", Probe, cancellationToken);

        public Task<StandaloneTaskbarBrokerResult> StartAsync(
            CancellationToken cancellationToken = default)
            => InvokeAsync("start", Start, cancellationToken);

        public Task<StandaloneTaskbarBrokerResult> StopAsync(
            CancellationToken cancellationToken = default)
            => InvokeAsync("stop", Stop, cancellationToken);

        public Task<StandaloneTaskbarBrokerResult> GetStatusAsync(
            CancellationToken cancellationToken = default)
            => InvokeAsync("status", Status, cancellationToken);

        private async Task<StandaloneTaskbarBrokerResult> InvokeAsync(
            string name,
            Func<CancellationToken, Task<StandaloneTaskbarBrokerResult>> operation,
            CancellationToken cancellationToken)
        {
            CallQueue.Enqueue(name);
            int active = Interlocked.Increment(ref _activeCalls);
            int maximum = Volatile.Read(ref _maxConcurrentCalls);
            while (active > maximum)
            {
                int observed = Interlocked.CompareExchange(
                    ref _maxConcurrentCalls,
                    active,
                    maximum);
                if (observed == maximum) break;
                maximum = observed;
            }

            try
            {
                return await operation(cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _activeCalls);
            }
        }
    }
}
