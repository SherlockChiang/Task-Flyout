using System;
using System.Threading;

namespace Task_Flyout.Services
{
    internal readonly record struct RssDataLease(long Generation, CancellationToken CancellationToken);

    internal sealed class RssDataLifecycle
    {
        private readonly object _lock = new();
        private CancellationTokenSource _cancellation = new();
        private long _generation;
        private int _clearInProgress;

        public bool IsClearInProgress => Volatile.Read(ref _clearInProgress) != 0;

        public RssDataLease Capture()
        {
            lock (_lock)
            {
                if (IsClearInProgress)
                    throw new InvalidOperationException("RSS local data is being cleared.");
                return new RssDataLease(_generation, _cancellation.Token);
            }
        }

        public void BeginClear()
        {
            CancellationTokenSource previous;
            lock (_lock)
            {
                Volatile.Write(ref _clearInProgress, 1);
                _generation++;
                previous = _cancellation;
                _cancellation = new CancellationTokenSource();
            }

            try { previous.Cancel(); }
            catch (AggregateException) { }
            finally { previous.Dispose(); }
        }

        public void EndClear()
            => Volatile.Write(ref _clearInProgress, 0);

        public bool IsCurrent(RssDataLease lease)
        {
            lock (_lock)
                return !IsClearInProgress &&
                       lease.Generation == _generation &&
                       !lease.CancellationToken.IsCancellationRequested;
        }
    }
}
