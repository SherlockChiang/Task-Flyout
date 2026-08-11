using System;
using System.Threading;
using System.Threading.Tasks;

namespace Task_Flyout.Services;

internal sealed class StandaloneTaskbarCoordinator : IAsyncDisposable
{
    public static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(15);

    private readonly IStandaloneTaskbarBrokerClient _client;
    private readonly TimeSpan _shutdownTimeout;
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
    private bool _disposed;

    public StandaloneTaskbarCoordinator(
        IStandaloneTaskbarBrokerClient? client = null)
        : this(
            client ?? new StandaloneTaskbarBrokerClient(),
            ShutdownTimeout)
    {
    }

    internal StandaloneTaskbarCoordinator(
        IStandaloneTaskbarBrokerClient client,
        TimeSpan shutdownTimeout)
    {
        if (shutdownTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(shutdownTimeout));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _shutdownTimeout = shutdownTimeout;
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
        long generation = ++_generation;
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

        PublishIfCurrent(
            generation,
            new StandaloneTaskbarRuntimeStatus(
                StandaloneTaskbarLifecyclePolicy.MapStart(start.Kind)),
            notifyStatus);
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
