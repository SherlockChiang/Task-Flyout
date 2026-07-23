using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    public enum TaskMutationPhase
    {
        Queued,
        Pending,
        Succeeded,
        Failed
    }

    internal sealed record TaskMutationState(string Key, TaskMutationPhase Phase);

    internal sealed class TaskMutationCoordinator
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, Func<Task>> _retryOperations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, QueueEntry> _queues = new(StringComparer.Ordinal);
        public event EventHandler? StateChanged;

        public int PendingCount
        {
            get
            {
                lock (_lock)
                {
                    int count = 0;
                    foreach (var entry in _queues.Values) count += entry.Users;
                    return count;
                }
            }
        }

        public int FailedCount
        {
            get { lock (_lock) return _retryOperations.Count; }
        }

        private sealed class QueueEntry
        {
            public SemaphoreSlim Gate { get; } = new(1, 1);
            public int Users { get; set; }
        }

        public async Task<TaskMutationState> ExecuteAsync(
            string key,
            Func<Task> operation,
            Action<TaskMutationState>? stateChanged = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            ArgumentNullException.ThrowIfNull(operation);

            return await EnqueueAsync(key, operation, stateChanged, forceQueued: false);
        }

        public async Task<TaskMutationState?> RetryAsync(string key, Action<TaskMutationState>? stateChanged = null)
        {
            Func<Task>? operation;
            lock (_lock)
                if (!_retryOperations.TryGetValue(key, out operation)) return null;
            return await EnqueueAsync(key, operation, stateChanged, forceQueued: true);
        }

        private async Task<TaskMutationState> EnqueueAsync(
            string key,
            Func<Task> operation,
            Action<TaskMutationState>? stateChanged,
            bool forceQueued)
        {
            QueueEntry entry;
            bool wasQueued;
            lock (_lock)
            {
                if (!_queues.TryGetValue(key, out entry!))
                {
                    entry = new QueueEntry();
                    _queues[key] = entry;
                }
                wasQueued = entry.Users > 0;
                entry.Users++;
            }

            if (forceQueued || wasQueued)
                ReportState(new TaskMutationState(key, TaskMutationPhase.Queued), stateChanged);
            await entry.Gate.WaitAsync();
            var pending = new TaskMutationState(key, TaskMutationPhase.Pending);
            ReportState(pending, stateChanged);
            try
            {
                await operation();
                lock (_lock) _retryOperations.Remove(key);
                var succeeded = new TaskMutationState(key, TaskMutationPhase.Succeeded);
                ReportState(succeeded, stateChanged);
                return succeeded;
            }
            catch
            {
                lock (_lock) _retryOperations[key] = operation;
                var failed = new TaskMutationState(key, TaskMutationPhase.Failed);
                ReportState(failed, stateChanged);
                return failed;
            }
            finally
            {
                entry.Gate.Release();
                lock (_lock)
                {
                    entry.Users--;
                    if (entry.Users == 0)
                    {
                        _queues.Remove(key);
                        entry.Gate.Dispose();
                    }
                }
            }
        }

        private void ReportState(TaskMutationState state, Action<TaskMutationState>? callback)
        {
            callback?.Invoke(state);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
