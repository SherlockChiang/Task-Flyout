using System;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    internal sealed class DeferredStartupWork
    {
        private readonly object _lock = new();
        private Task? _task;

        public Task RunAsync(Func<Task> work)
        {
            ArgumentNullException.ThrowIfNull(work);
            lock (_lock)
                return _task ??= Task.Run(work);
        }
    }
}
