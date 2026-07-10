using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    internal sealed class SharedRequestCoordinator<TResult>
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, Entry> _requests = new(StringComparer.Ordinal);

        public async Task<TResult> RunAsync(
            string key,
            Func<CancellationToken, Task<TResult>> requestFactory,
            CancellationToken callerCancellationToken)
        {
            Entry request;
            lock (_lock)
            {
                if (!_requests.TryGetValue(key, out request!))
                {
                    var requestCancellation = new CancellationTokenSource();
                    Task<TResult> requestTask;
                    try
                    {
                        requestTask = requestFactory(requestCancellation.Token);
                    }
                    catch
                    {
                        requestCancellation.Dispose();
                        throw;
                    }
                    request = new Entry(requestTask, requestCancellation);
                    _requests[key] = request;
                    _ = request.Task.ContinueWith(
                        _ => RemoveCompletedRequest(key, request),
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
                request.WaiterCount++;
            }

            try
            {
                return await request.Task.WaitAsync(callerCancellationToken);
            }
            finally
            {
                ReleaseWaiter(key, request);
            }
        }

        private void ReleaseWaiter(string key, Entry request)
        {
            bool cancelRequest = false;
            lock (_lock)
            {
                request.WaiterCount--;
                if (request.WaiterCount == 0 && !request.Task.IsCompleted &&
                    _requests.TryGetValue(key, out var current) && ReferenceEquals(current, request))
                {
                    _requests.Remove(key);
                    cancelRequest = true;
                }
            }

            if (cancelRequest)
            {
                try { request.Cancellation.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }

        private void RemoveCompletedRequest(string key, Entry completedRequest)
        {
            lock (_lock)
            {
                if (_requests.TryGetValue(key, out var current) && ReferenceEquals(current, completedRequest))
                    _requests.Remove(key);
            }
            completedRequest.Cancellation.Dispose();
        }

        private sealed class Entry(Task<TResult> task, CancellationTokenSource cancellation)
        {
            public Task<TResult> Task { get; } = task;
            public CancellationTokenSource Cancellation { get; } = cancellation;
            public int WaiterCount { get; set; }
        }
    }
}
