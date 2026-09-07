using System;
using System.Threading;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    internal readonly record struct SearchWorkRequest(long Generation, CancellationToken Token);

    // Request ownership is UI-thread-only; the semaphore bounds even readers that
    // cannot interrupt an individual synchronous database operation.
    internal sealed class LatestSearchWork
    {
        private readonly SemaphoreSlim _worker = new(1, 1);
        private CancellationTokenSource? _cancellation;
        private long _generation;

        public SearchWorkRequest Begin()
        {
            Cancel();
            _cancellation = new CancellationTokenSource();
            return new(_generation, _cancellation.Token);
        }

        public bool IsCurrent(SearchWorkRequest request)
            => _cancellation != null && request.Generation == _generation && !request.Token.IsCancellationRequested;

        public void Cancel()
        {
            _generation++;
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;
        }

        public async Task<T> RunAsync<T>(SearchWorkRequest request, Func<CancellationToken, T> work)
        {
            await _worker.WaitAsync(request.Token).ConfigureAwait(false);
            try
            {
                return await Task.Run(() =>
                {
                    request.Token.ThrowIfCancellationRequested();
                    T result = work(request.Token);
                    request.Token.ThrowIfCancellationRequested();
                    return result;
                }, request.Token).ConfigureAwait(false);
            }
            finally { _worker.Release(); }
        }
    }
}
