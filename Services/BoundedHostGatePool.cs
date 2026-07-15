using System;
using System.Threading;

namespace Task_Flyout.Services
{
    internal sealed class BoundedHostGatePool
    {
        private readonly SemaphoreSlim[] _gates;

        public BoundedHostGatePool(int gateCount, int concurrencyPerGate)
        {
            if (gateCount < 1) throw new ArgumentOutOfRangeException(nameof(gateCount));
            if (concurrencyPerGate < 1) throw new ArgumentOutOfRangeException(nameof(concurrencyPerGate));

            _gates = new SemaphoreSlim[gateCount];
            for (int index = 0; index < _gates.Length; index++)
                _gates[index] = new SemaphoreSlim(concurrencyPerGate, concurrencyPerGate);
        }

        public int GateCount => _gates.Length;

        public SemaphoreSlim GetGate(string host)
        {
            int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(host ?? "");
            int index = (int)((uint)hash % (uint)_gates.Length);
            return _gates[index];
        }
    }
}
