using System;
using System.Diagnostics;
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
        private const uint PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION = 0x4;
        private const int ProcessPowerThrottling = 4; // PROCESS_INFORMATION_CLASS
        private const int ThreadPowerThrottling = 3; // THREAD_INFORMATION_CLASS

        // Background mode lowers I/O and memory priority but keeps a normal CPU
        // scheduling class — unlike IDLE_PRIORITY_CLASS it won't starve background mail
        // polling / notification checks when the machine is busy. Paired BEGIN/END.
        private const uint PROCESS_MODE_BACKGROUND_BEGIN = 0x00100000;
        private const uint PROCESS_MODE_BACKGROUND_END = 0x00200000;
        private const int THREAD_SET_INFORMATION = 0x0020;

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessInformation(
            IntPtr hProcess,
            int processInformationClass,
            ref PROCESS_POWER_THROTTLING_STATE processInformation,
            uint processInformationSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenThread(int dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetThreadInformation(
            IntPtr hThread,
            int threadInformationClass,
            ref PROCESS_POWER_THROTTLING_STATE threadInformation,
            uint threadInformationSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetPriorityClass(IntPtr hProcess, uint dwPriorityClass);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private static bool? _current;

        /// <summary>Idempotently enable/disable EcoQoS for this process.</summary>
        public static void SetEfficiencyMode(bool enabled)
        {
            if (_current == enabled) return;

            try
            {
                var state = new PROCESS_POWER_THROTTLING_STATE
                {
                    Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
                    ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED | PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION,
                    StateMask = enabled
                        ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED | PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION
                        : 0u,
                };

                var handle = GetCurrentProcess();
                if (!SetProcessInformation(handle, ProcessPowerThrottling, ref state,
                        (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>()))
                {
                    LogLastWin32Error("SetProcessInformation(ProcessPowerThrottling)", enabled);
                }

                // Task Manager shows the leaf when EcoQoS + background mode are active.
                // BACKGROUND_BEGIN/END only adjusts I/O & memory priority (reversible),
                // so it won't choke the background poller like IDLE_PRIORITY_CLASS would.
                if (!SetPriorityClass(handle, enabled ? PROCESS_MODE_BACKGROUND_BEGIN : PROCESS_MODE_BACKGROUND_END))
                    LogLastWin32Error("SetPriorityClass(background mode)", enabled);

                ApplyThreadPowerThrottling(state, enabled);
                _current = enabled;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetEfficiencyMode({enabled}) failed: {ex.Message}");
            }
        }

        private static void ApplyThreadPowerThrottling(PROCESS_POWER_THROTTLING_STATE state, bool enabled)
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                foreach (ProcessThread thread in process.Threads)
                {
                    var threadHandle = OpenThread(THREAD_SET_INFORMATION, false, (uint)thread.Id);
                    if (threadHandle == IntPtr.Zero)
                    {
                        LogLastWin32Error($"OpenThread({thread.Id})", enabled);
                        continue;
                    }

                    try
                    {
                        if (!SetThreadInformation(threadHandle, ThreadPowerThrottling, ref state,
                                (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>()))
                        {
                            LogLastWin32Error($"SetThreadInformation({thread.Id})", enabled);
                        }
                    }
                    finally
                    {
                        CloseHandle(threadHandle);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ApplyThreadPowerThrottling({enabled}) failed: {ex.Message}");
            }
        }

        private static void LogLastWin32Error(string operation, bool enabled)
        {
            var error = Marshal.GetLastWin32Error();
            Debug.WriteLine($"{operation} for efficiency mode={enabled} failed: Win32 error {error}");
        }
    }
}
