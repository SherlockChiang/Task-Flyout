using System;
using System.Threading;
using System.Threading.Tasks;

namespace Task_Flyout.Services;

internal sealed class StandaloneTaskbarCoordinator : IAsyncDisposable
{
    public static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan MountReadyPollInterval =
        TimeSpan.FromMilliseconds(400);
    public static readonly TimeSpan MountLeaseTimeout =
        TimeSpan.FromSeconds(12);
    public const int MountReadyMaxAttempts = 5;

    private readonly IStandaloneTaskbarBrokerClient _client;
    private readonly TimeSpan _shutdownTimeout;
    private readonly TimeSpan _mountReadyPollInterval;
    private readonly TimeSpan _mountLeaseTimeout;
    private readonly int _mountReadyMaxAttempts;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly object _notificationLock = new();
    private CancellationTokenSource? _generationCancellation;
    private TaskCompletionSource<bool>? _disposeCompletion;
    private Task _latestTask = Task.CompletedTask;
    private StandaloneTaskbarRuntimeStatus _status = new(
        StandaloneTaskbarRuntimeState.Disabled);
    private long _generation;
    private bool _desiredEnabled;
    private bool _stopRequired;
    private bool _diagnosticOnlyRejected;
    private StandaloneTaskbarControllerIdentity? _controllerIdentity;
    private ulong _lastMountGeneration;
    private WeatherCompanionMountState _lastMountState;
    private long _mountLeaseVersion;
    private bool _disposed;

    public StandaloneTaskbarCoordinator(
        IStandaloneTaskbarBrokerClient? client = null)
        : this(
            client ?? new StandaloneTaskbarBrokerClient(),
            ShutdownTimeout,
            cleanupRequired: false)
    {
    }

    internal StandaloneTaskbarCoordinator(bool cleanupRequired)
        : this(
            new StandaloneTaskbarBrokerClient(),
            ShutdownTimeout,
            cleanupRequired)
    {
    }

    internal StandaloneTaskbarCoordinator(
        IStandaloneTaskbarBrokerClient client,
        TimeSpan shutdownTimeout,
        bool cleanupRequired = false,
        TimeSpan? mountReadyPollInterval = null,
        int mountReadyMaxAttempts = MountReadyMaxAttempts,
        TimeSpan? mountLeaseTimeout = null)
    {
        if (shutdownTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(shutdownTimeout));
        TimeSpan pollInterval =
            mountReadyPollInterval ?? MountReadyPollInterval;
        if (pollInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(mountReadyPollInterval));
        if (mountReadyMaxAttempts <= 0)
            throw new ArgumentOutOfRangeException(nameof(mountReadyMaxAttempts));
        TimeSpan leaseTimeout = mountLeaseTimeout ?? MountLeaseTimeout;
        if (leaseTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(mountLeaseTimeout));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _shutdownTimeout = shutdownTimeout;
        _mountReadyPollInterval = pollInterval;
        _mountReadyMaxAttempts = mountReadyMaxAttempts;
        _mountLeaseTimeout = leaseTimeout;
        _stopRequired = cleanupRequired;
    }

    public event Action<StandaloneTaskbarRuntimeStatus>? StatusChanged;

    public StandaloneTaskbarRuntimeStatus Status
    {
        get
        {
            lock (_stateLock) return _status;
        }
    }

    public bool IsRequested
    {
        get
        {
            lock (_stateLock) return _desiredEnabled;
        }
    }

    public bool RequiresStop
    {
        get
        {
            lock (_stateLock) return _stopRequired;
        }
    }

    public bool DiagnosticOnlyRejected
    {
        get
        {
            lock (_stateLock) return _diagnosticOnlyRejected;
        }
    }

    internal bool ReportMountReadiness(
        WeatherCompanionMountReadinessReport report)
    {
        if (report.ClientProcessId == 0 ||
            report.ControllerNonce == 0 ||
            report.MountGeneration == 0 ||
            report.MountState is not WeatherCompanionMountState.Ready and
                not WeatherCompanionMountState.Lost)
        {
            return false;
        }

        long generation;
        long leaseVersion;
        StandaloneTaskbarRuntimeStatus? nextStatus = null;
        lock (_stateLock)
        {
            if (_disposed || !_desiredEnabled ||
                _controllerIdentity is not StandaloneTaskbarControllerIdentity identity ||
                identity.ProcessId != report.ClientProcessId ||
                identity.ControlNonce != report.ControllerNonce ||
                _status.State is not StandaloneTaskbarRuntimeState.ControllerActiveUnverified and
                    not StandaloneTaskbarRuntimeState.MountReady)
            {
                return false;
            }

            if (report.MountGeneration < _lastMountGeneration ||
                (report.MountGeneration == _lastMountGeneration &&
                 _lastMountState != WeatherCompanionMountState.None &&
                 _lastMountState != report.MountState))
            {
                return false;
            }

            if (report.MountGeneration > _lastMountGeneration)
            {
                _lastMountGeneration = report.MountGeneration;
                _lastMountState = report.MountState;
            }
            else if (_lastMountState == WeatherCompanionMountState.None)
            {
                _lastMountState = report.MountState;
            }

            generation = _generation;
            leaseVersion = ++_mountLeaseVersion;
            StandaloneTaskbarRuntimeState desiredState =
                report.MountState == WeatherCompanionMountState.Ready
                    ? StandaloneTaskbarRuntimeState.MountReady
                    : StandaloneTaskbarRuntimeState.ControllerActiveUnverified;
            if (_status.State != desiredState)
                nextStatus = new StandaloneTaskbarRuntimeStatus(desiredState);
        }

        if (nextStatus.HasValue)
        {
            PublishMountLeaseIfCurrent(
                generation,
                leaseVersion,
                nextStatus.Value);
        }
        if (report.MountState == WeatherCompanionMountState.Ready)
            _ = ExpireMountLeaseAsync(generation, leaseVersion);
        return true;
    }

    public Task SetEnabledAsync(bool enabled)
    {
        ScheduledOperation operation;
        lock (_stateLock)
        {
            // Repeating a persisted desired state must not create a broker
            // retry loop. RefreshAsync is the explicit recovery entry point.
            if (_disposed || _desiredEnabled == enabled)
                return _latestTask;
            operation = CreateOperationLocked(enabled, isRecovery: false);
        }

        return Launch(operation, notifyStatus: true);
    }

    public Task RefreshAsync()
    {
        ScheduledOperation operation;
        lock (_stateLock)
        {
            if (_disposed)
                return _latestTask;
            // Explorer recreation and explicit recovery always get a new
            // generation, even though the desired state itself is unchanged.
            operation = CreateOperationLocked(
                _desiredEnabled,
                isRecovery: true);
        }

        return Launch(operation, notifyStatus: true);
    }

    public ValueTask DisposeAsync()
    {
        ScheduledOperation operation;
        TaskCompletionSource<bool> disposeCompletion;
        lock (_stateLock)
        {
            if (_disposeCompletion != null)
                return new ValueTask(_disposeCompletion.Task);

            _disposed = true;
            operation = CreateOperationLocked(
                enabled: false,
                isRecovery: false);
            disposeCompletion = operation.Completion;
            _disposeCompletion = disposeCompletion;
        }

        CancelNoThrow(operation.PreviousCancellation);
        PublishIfCurrent(
            operation.Generation,
            operation.InitialStatus,
            notify: false);
        _ = DisposeCoreAsync(operation);
        return new ValueTask(disposeCompletion.Task);
    }

    private ScheduledOperation CreateOperationLocked(
        bool enabled,
        bool isRecovery)
    {
        _desiredEnabled = enabled;
        if (enabled && !isRecovery)
            _diagnosticOnlyRejected = false;
        long generation = ++_generation;
        _controllerIdentity = null;
        _lastMountGeneration = 0;
        _lastMountState = WeatherCompanionMountState.None;
        _mountLeaseVersion++;
        CancellationTokenSource? previous = _generationCancellation;
        var generationCancellation = new CancellationTokenSource();
        _generationCancellation = generationCancellation;
        var completion = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _latestTask = completion.Task;

        StandaloneTaskbarRuntimeState initialState = enabled
            ? isRecovery
                ? StandaloneTaskbarRuntimeState.Recovery
                : StandaloneTaskbarRuntimeState.Starting
            : _stopRequired
                ? StandaloneTaskbarRuntimeState.Stopping
                : StandaloneTaskbarRuntimeState.Disabled;
        return new ScheduledOperation(
            generation,
            enabled,
            previous,
            generationCancellation,
            completion,
            new StandaloneTaskbarRuntimeStatus(initialState));
    }

    private Task Launch(
        ScheduledOperation operation,
        bool notifyStatus)
    {
        CancelNoThrow(operation.PreviousCancellation);
        PublishIfCurrent(
            operation.Generation,
            operation.InitialStatus,
            notifyStatus);
        _ = CompleteOperationAsync(operation, notifyStatus);
        return operation.Completion.Task;
    }

    private async Task CompleteOperationAsync(
        ScheduledOperation operation,
        bool notifyStatus)
    {
        try
        {
            await ReconcileAsync(operation, notifyStatus)
                .ConfigureAwait(false);
        }
        finally
        {
            operation.Completion.TrySetResult(true);
        }
    }

    private async Task ReconcileAsync(
        ScheduledOperation operation,
        bool notifyStatus)
    {
        bool gateEntered = false;
        CancellationToken cancellationToken =
            operation.GenerationCancellation.Token;
        try
        {
            await _operationGate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            gateEntered = true;

            if (!IsCurrent(operation.Generation, operation.Enabled))
                return;

            if (operation.Enabled)
            {
                await StartCoreAsync(
                    operation.Generation,
                    cancellationToken,
                    notifyStatus).ConfigureAwait(false);
            }
            else
            {
                await StopCoreAsync(
                    operation.Generation,
                    cancellationToken,
                    notifyStatus).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            PublishIfCurrent(
                operation.Generation,
                new StandaloneTaskbarRuntimeStatus(
                    StandaloneTaskbarRuntimeState.Cancelled),
                notifyStatus);
        }
        catch
        {
            PublishIfCurrent(
                operation.Generation,
                new StandaloneTaskbarRuntimeStatus(
                    StandaloneTaskbarRuntimeState.Recovery),
                notifyStatus);
        }
        finally
        {
            if (gateEntered)
                _operationGate.Release();
            ReleaseGenerationCancellation(operation.GenerationCancellation);
        }
    }

    private async Task StartCoreAsync(
        long generation,
        CancellationToken cancellationToken,
        bool notifyStatus)
    {
        StandaloneTaskbarBrokerResult probe =
            await _client.ProbeAsync(cancellationToken).ConfigureAwait(false);
        if (!IsCurrent(generation, enabled: true))
            return;

        if (probe.Kind != StandaloneTaskbarBrokerResultKind.ProbeSupported)
        {
            PublishIfCurrent(
                generation,
                new StandaloneTaskbarRuntimeStatus(
                    StandaloneTaskbarLifecyclePolicy.MapProbe(probe.Kind)),
                notifyStatus);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!MarkStopRequiredIfCurrent(generation))
            return;

        StandaloneTaskbarBrokerResult start =
            await _client.StartAsync(cancellationToken).ConfigureAwait(false);
        if (!IsCurrent(generation, enabled: true))
            return;

        StandaloneTaskbarRuntimeState startState =
            StandaloneTaskbarLifecyclePolicy.MapStart(start.Kind);
        if (startState == StandaloneTaskbarRuntimeState.DiagnosticOnly)
        {
            // The diagnostic Host is diagnostic-only and has no managed mount
            // lifecycle. Stop it before publishing a terminal state so the
            // app never observes a diagnostic controller as active while its
            // detour is still installed or its cleanup lease is unresolved.
            await StopDiagnosticOnlyControllerAsync(
                generation,
                notifyStatus).ConfigureAwait(false);
            return;
        }
        PublishIfCurrent(
            generation,
            new StandaloneTaskbarRuntimeStatus(startState),
            notifyStatus);
        if (startState !=
                StandaloneTaskbarRuntimeState.ControllerActiveUnverified ||
            !IsCurrent(generation, enabled: true))
        {
            return;
        }

        SetControllerIdentityIfCurrent(
            generation,
            start.ControllerIdentity);

        await VerifyMountReadyAsync(
            generation,
            cancellationToken,
            notifyStatus).ConfigureAwait(false);
    }

    private async Task StopDiagnosticOnlyControllerAsync(
        long generation,
        bool notifyStatus)
    {
        StandaloneTaskbarBrokerResult stop;
        try
        {
            // A diagnostic-only Host must never remain active because the app
            // does not have a mount lifecycle for it. Cleanup is deliberately
            // independent of the start cancellation token, but still bounded
            // if an alternate client ignores its own process timeout.
            using var cleanupCancellation =
                new CancellationTokenSource(_shutdownTimeout);
            stop = await _client.StopAsync(cleanupCancellation.Token)
                .WaitAsync(_shutdownTimeout).ConfigureAwait(false);
        }
        catch
        {
            stop = new(StandaloneTaskbarBrokerResultKind.Ambiguous);
        }

        Action<StandaloneTaskbarRuntimeStatus>? handler;
        StandaloneTaskbarRuntimeStatus status =
            stop.Kind == StandaloneTaskbarBrokerResultKind.ControllerInactive
                ? new(StandaloneTaskbarRuntimeState.DiagnosticOnly)
                : new(StandaloneTaskbarRuntimeState.Recovery);
        lock (_stateLock)
        {
            if (generation != _generation)
                return;
            _desiredEnabled = false;
            _diagnosticOnlyRejected = true;
            _controllerIdentity = null;
            _lastMountGeneration = 0;
            _lastMountState = WeatherCompanionMountState.None;
            _mountLeaseVersion++;
            _stopRequired =
                stop.Kind != StandaloneTaskbarBrokerResultKind.ControllerInactive;
            _status = status;
            handler = notifyStatus ? StatusChanged : null;
        }
        NotifySubscribers(generation, status, handler);
    }

    private async Task VerifyMountReadyAsync(
        long generation,
        CancellationToken cancellationToken,
        bool notifyStatus)
    {
        for (int attempt = 0; attempt < _mountReadyMaxAttempts; attempt++)
        {
            if (IsMountReadyCurrent(generation))
                return;

            if (attempt != 0 && _mountReadyPollInterval > TimeSpan.Zero)
            {
                await Task.Delay(
                    _mountReadyPollInterval,
                    cancellationToken).ConfigureAwait(false);
            }

            if (!IsCurrent(generation, enabled: true))
                return;

            StandaloneTaskbarBrokerResult status;
            try
            {
                status = await _client.GetStatusAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Status is an additive proof channel. An older or failed
                // implementation cannot invalidate the acknowledged start,
                // but it also cannot hide the safe fallback.
                return;
            }

            if (!IsCurrent(generation, enabled: true))
                return;

            if (status.Kind ==
                StandaloneTaskbarBrokerResultKind.ControllerMountReady)
            {
                if (TryBeginInitialMountLease(generation, out long leaseVersion))
                {
                    PublishMountLeaseIfCurrent(
                        generation,
                        leaseVersion,
                        new StandaloneTaskbarRuntimeStatus(
                            StandaloneTaskbarRuntimeState.MountReady));
                    _ = ExpireMountLeaseAsync(generation, leaseVersion);
                }
                return;
            }

            StandaloneTaskbarRuntimeState mapped =
                StandaloneTaskbarLifecyclePolicy.MapStatus(status.Kind);
            if (status.Kind ==
                StandaloneTaskbarBrokerResultKind.ControllerActiveUnverified)
            {
                continue;
            }
            if (mapped == StandaloneTaskbarRuntimeState.DiagnosticOnly)
            {
                // A diagnostic-only acknowledgement can arrive after start
                // through the additive status channel. It has the same
                // lifecycle contract as a diagnostic start result: the host
                // must be stopped before the terminal state is published.
                await StopDiagnosticOnlyControllerAsync(
                    generation,
                    notifyStatus).ConfigureAwait(false);
                return;
            }
            if (mapped ==
                StandaloneTaskbarRuntimeState.ControllerActiveUnverified)
            {
                return;
            }

            ClearControllerIdentityIfCurrent(generation);
            PublishIfCurrent(
                generation,
                new StandaloneTaskbarRuntimeStatus(mapped),
                notifyStatus);
            return;
        }
    }

    private async Task StopCoreAsync(
        long generation,
        CancellationToken cancellationToken,
        bool notifyStatus)
    {
        bool shouldStop;
        lock (_stateLock)
        {
            if (generation != _generation || _desiredEnabled)
                return;
            shouldStop = _stopRequired;
        }

        if (!shouldStop)
        {
            PublishIfCurrent(
                generation,
                new StandaloneTaskbarRuntimeStatus(
                    StandaloneTaskbarRuntimeState.Disabled),
                notifyStatus);
            return;
        }

        StandaloneTaskbarBrokerResult stop =
            await _client.StopAsync(cancellationToken).ConfigureAwait(false);
        if (!IsCurrent(generation, enabled: false))
            return;

        if (stop.Kind == StandaloneTaskbarBrokerResultKind.ControllerInactive)
        {
            ClearStopRequiredIfCurrent(generation);
            PublishIfCurrent(
                generation,
                new StandaloneTaskbarRuntimeStatus(
                    StandaloneTaskbarRuntimeState.Disabled),
                notifyStatus);
            return;
        }

        PublishIfCurrent(
            generation,
            new StandaloneTaskbarRuntimeStatus(
                StandaloneTaskbarLifecyclePolicy.MapStop(stop.Kind)),
            notifyStatus);
    }

    private async Task DisposeCoreAsync(ScheduledOperation operation)
    {
        Task reconciliation = ReconcileAsync(operation, notifyStatus: false);
        try
        {
            await reconciliation.WaitAsync(_shutdownTimeout)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            CancelNoThrow(operation.GenerationCancellation);
            MarkDisposeTimedOut(operation.Generation);
            _ = ObserveCompletionAsync(reconciliation);
        }
        finally
        {
            operation.Completion.TrySetResult(true);
        }
    }

    private bool IsCurrent(long generation, bool enabled)
    {
        lock (_stateLock)
        {
            return generation == _generation &&
                _desiredEnabled == enabled;
        }
    }

    private bool MarkStopRequiredIfCurrent(long generation)
    {
        lock (_stateLock)
        {
            if (generation != _generation || !_desiredEnabled)
                return false;
            _stopRequired = true;
            return true;
        }
    }

    private void ClearStopRequiredIfCurrent(long generation)
    {
        lock (_stateLock)
        {
            if (generation == _generation && !_desiredEnabled)
                _stopRequired = false;
        }
    }

    private void SetControllerIdentityIfCurrent(
        long generation,
        StandaloneTaskbarControllerIdentity? identity)
    {
        lock (_stateLock)
        {
            if (generation != _generation || !_desiredEnabled)
                return;
            _controllerIdentity = identity is { IsValid: true }
                ? identity
                : null;
            _lastMountGeneration = 0;
            _lastMountState = WeatherCompanionMountState.None;
            _mountLeaseVersion++;
        }
    }

    private void ClearControllerIdentityIfCurrent(long generation)
    {
        lock (_stateLock)
        {
            if (generation != _generation)
                return;
            _controllerIdentity = null;
            _lastMountGeneration = 0;
            _lastMountState = WeatherCompanionMountState.None;
            _mountLeaseVersion++;
        }
    }

    private bool IsMountReadyCurrent(long generation)
    {
        lock (_stateLock)
        {
            return generation == _generation &&
                _desiredEnabled &&
                _status.State == StandaloneTaskbarRuntimeState.MountReady;
        }
    }

    private bool TryBeginInitialMountLease(
        long generation,
        out long leaseVersion)
    {
        lock (_stateLock)
        {
            if (generation != _generation || !_desiredEnabled ||
                _controllerIdentity is not { IsValid: true } ||
                _lastMountState == WeatherCompanionMountState.Lost)
            {
                leaseVersion = 0;
                return false;
            }

            leaseVersion = ++_mountLeaseVersion;
            return true;
        }
    }

    private async Task ExpireMountLeaseAsync(
        long generation,
        long leaseVersion)
    {
        await Task.Delay(_mountLeaseTimeout).ConfigureAwait(false);

        long expiryVersion;
        lock (_stateLock)
        {
            if (generation != _generation || !_desiredEnabled ||
                leaseVersion != _mountLeaseVersion ||
                _status.State != StandaloneTaskbarRuntimeState.MountReady)
            {
                return;
            }
            expiryVersion = ++_mountLeaseVersion;
        }

        PublishMountLeaseIfCurrent(
            generation,
            expiryVersion,
            new StandaloneTaskbarRuntimeStatus(
                StandaloneTaskbarRuntimeState.ControllerActiveUnverified));
    }

    private void PublishMountLeaseIfCurrent(
        long generation,
        long leaseVersion,
        StandaloneTaskbarRuntimeStatus status)
    {
        Action<StandaloneTaskbarRuntimeStatus>? handler;
        lock (_stateLock)
        {
            if (generation != _generation || !_desiredEnabled ||
                leaseVersion != _mountLeaseVersion ||
                _controllerIdentity is not { IsValid: true })
            {
                return;
            }
            if (_status == status)
                return;
            _status = status;
            handler = StatusChanged;
        }

        NotifySubscribers(generation, status, handler);
    }

    private void PublishIfCurrent(
        long generation,
        StandaloneTaskbarRuntimeStatus status,
        bool notify)
    {
        Action<StandaloneTaskbarRuntimeStatus>? handler;
        lock (_stateLock)
        {
            if (generation != _generation)
                return;
            _status = status;
            handler = notify ? StatusChanged : null;
        }

        NotifySubscribers(generation, status, handler);
    }

    private void NotifySubscribers(
        long generation,
        StandaloneTaskbarRuntimeStatus status,
        Action<StandaloneTaskbarRuntimeStatus>? handler)
    {
        if (handler == null) return;
        lock (_notificationLock)
        {
            foreach (Action<StandaloneTaskbarRuntimeStatus> subscriber in
                     handler.GetInvocationList())
            {
                lock (_stateLock)
                {
                    if (generation != _generation)
                        return;
                }

                try { subscriber(status); }
                catch { }
            }
        }
    }

    private void MarkDisposeTimedOut(long generation)
    {
        lock (_stateLock)
        {
            if (generation != _generation)
                return;
            _generation++;
            _status = new StandaloneTaskbarRuntimeStatus(
                StandaloneTaskbarRuntimeState.Cancelled);
        }
    }

    private void ReleaseGenerationCancellation(
        CancellationTokenSource generationCancellation)
    {
        lock (_stateLock)
        {
            if (ReferenceEquals(_generationCancellation, generationCancellation))
                _generationCancellation = null;
        }
        generationCancellation.Dispose();
    }

    private static void CancelNoThrow(CancellationTokenSource? cancellation)
    {
        if (cancellation == null)
            return;
        try { cancellation.Cancel(); }
        catch (Exception ex) when (ex is ObjectDisposedException or AggregateException) { }
    }

    private static async Task ObserveCompletionAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch { }
    }

    private sealed record ScheduledOperation(
        long Generation,
        bool Enabled,
        CancellationTokenSource? PreviousCancellation,
        CancellationTokenSource GenerationCancellation,
        TaskCompletionSource<bool> Completion,
        StandaloneTaskbarRuntimeStatus InitialStatus);
}
