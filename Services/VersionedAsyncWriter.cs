using System;
using System.Threading;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    internal sealed class VersionedAsyncWriter<T>
    {
        private readonly Func<T, Task> _writeAsync;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private long _lastPersistedVersion;

        public VersionedAsyncWriter(Func<T, Task> writeAsync)
        {
            _writeAsync = writeAsync ?? throw new ArgumentNullException(nameof(writeAsync));
        }

        public async Task WriteAsync(long version, T value)
        {
            await _gate.WaitAsync();
            try
            {
                if (version <= _lastPersistedVersion)
                    return;

                await _writeAsync(value);
                _lastPersistedVersion = version;
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
