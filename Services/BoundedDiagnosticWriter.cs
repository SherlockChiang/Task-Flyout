using System;
using System.Threading;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    // Producers never wait for disk. The capacity includes the write in progress.
    internal sealed class BoundedDiagnosticWriter<T>(Action<long, T> append, int capacity = 256)
    {
        private readonly object _gate = new();
        private readonly int _capacity = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
        private Task _tail = Task.CompletedTask;
        private int _pending;
        private long _sequence;
        private long _dropped;
        private long _failures;

        public long Dropped => Interlocked.Read(ref _dropped);
        public long Failures => Interlocked.Read(ref _failures);

        public bool TryEnqueue(T value)
        {
            lock (_gate)
            {
                long sequence = ++_sequence;
                if (_pending >= _capacity)
                {
                    Interlocked.Increment(ref _dropped);
                    return false;
                }

                _pending++;
                _tail = _tail.ContinueWith(_ =>
                {
                    try { append(sequence, value); }
                    catch { Interlocked.Increment(ref _failures); }
                    finally { lock (_gate) _pending--; }
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
                return true;
            }
        }

        public Task DrainAsync()
        {
            lock (_gate) return _tail;
        }
    }
}
