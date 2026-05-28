using System;
using System.Runtime.InteropServices;

namespace Task_Flyout.Services
{
    /// <summary>
    /// Toggles Win11 "Efficiency Mode" (EcoQoS) for the current process. When enabled the
    /// process is hinted to run on efficiency cores at reduced frequency and at idle
    /// priority — the same state Task Manager shows with the green leaf. Used to throttle
    /// Task Flyout while it sits collapsed in the tray with no window on screen.
    /// </summary>
    internal static class EfficiencyModeService
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_POWER_THROTTLING_STATE
        {
            public uint Version;
            public uint ControlMask;
            public uint StateMask;
        }

        private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
        private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
        private const int ProcessPowerThrottling = 4; // PROCESS_INFORMATION_CLASS

        private const uint IDLE_PRIORITY_CLASS = 0x00000040;
        private const uint NORMAL_PRIORITY_CLASS = 0x00000020;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(
            IntPtr hProcess,
            int processInformationClass,
            ref PROCESS_POWER_THROTTLING_STATE processInformation,
            uint processInformationSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetPriorityClass(IntPtr hProcess, uint dwPriorityClass);

        private static bool? _current;

        /// <summary>Idempotently enable/disable EcoQoS for this process.</summary>
        public static void SetEfficiencyMode(bool enabled)
        {
            if (_current == enabled) return;
            _current = enabled;

            try
            {
                var state = new PROCESS_POWER_THROTTLING_STATE
                {
                    Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                    ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                    StateMask = enabled ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0u,
                };

                var handle = GetCurrentProcess();
                SetProcessInformation(handle, ProcessPowerThrottling, ref state,
                    (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());

                // Task Manager only shows the leaf when priority is also lowered.
                SetPriorityClass(handle, enabled ? IDLE_PRIORITY_CLASS : NORMAL_PRIORITY_CLASS);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetEfficiencyMode({enabled}) failed: {ex.Message}");
            }
        }
    }
}
