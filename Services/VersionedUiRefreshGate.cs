using System.Threading;

namespace Task_Flyout.Services
{
    internal sealed class VersionedUiRefreshGate
    {
        private long _pendingVersion;
        private long _appliedVersion;
        private int _dispatchQueued;

        public bool TryQueue(long version)
        {
            if (version <= Volatile.Read(ref _appliedVersion))
                return false;
            UpdatePendingVersion(version);
            return Interlocked.CompareExchange(ref _dispatchQueued, 1, 0) == 0;
        }

        public bool TryBeginApply(out long version)
        {
            Volatile.Write(ref _dispatchQueued, 0);
            version = Volatile.Read(ref _pendingVersion);

            while (true)
            {
                long applied = Volatile.Read(ref _appliedVersion);
                if (version <= applied)
                    return false;
                if (Interlocked.CompareExchange(ref _appliedVersion, version, applied) == applied)
                    return true;
            }
        }

        public void CancelQueuedDispatch() => Volatile.Write(ref _dispatchQueued, 0);

        private void UpdatePendingVersion(long version)
        {
            while (true)
            {
                long pending = Volatile.Read(ref _pendingVersion);
                if (version <= pending)
                    return;
                if (Interlocked.CompareExchange(ref _pendingVersion, version, pending) == pending)
                    return;
            }
        }
    }
}
