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

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORY_PRIORITY_INFORMATION
        {
            public uint MemoryPriority;
        }

        private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
        private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
        private const uint PROCESS_POWER_THROTTLING_IGNORE_TIMER_RESOLUTION = 0x4;
        private const int ProcessPowerThrottling = 4; // PROCESS_INFORMATION_CLASS
        private const int ThreadPowerThrottling = 3; // THREAD_INFORMATION_CLASS

        // Task Manager shows the green "Efficiency mode" leaf only when EcoQoS
        // (EXECUTION_SPEED throttling) is paired with a low base priority. IDLE +
        // EcoQoS is the same combination Edge/PowerToys use to enter the mode. The
        // trade-off versus the old background mode: under heavy CPU contention the
        // background mail/notification poller may be preempted more aggressively —
        // which is the intended semantics of efficiency mode.
        private const uint IDLE_PRIORITY_CLASS = 0x00000040;
        private const uint NORMAL_PRIORITY_CLASS = 0x00000020;
        private const int THREAD_SET_INFORMATION = 0x0020;

        // Idle + EcoQoS alone don't trim resident memory the way the old background
        // mode did. Lowering memory priority and trimming the working set restores
        // those savings: pages get paged out / compressed and fault back in lazily.
        private const int ProcessMemoryPriority = 0; // PROCESS_INFORMATION_CLASS
        private const uint MEMORY_PRIORITY_LOW = 2;
        private const uint MEMORY_PRIORITY_NORMAL = 5;

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
        private static extern bool SetProcessInformation(
            IntPtr hProcess,
            int processInformationClass,
            ref MEMORY_PRIORITY_INFORMATION processInformation,
            uint processInformationSize);

        // Passing (SIZE_T)-1 for both bounds asks the memory manager to trim as many
        // pages out of the working set as possible.
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSizeEx(
            IntPtr hProcess,
            IntPtr dwMinimumWorkingSetSize,
            IntPtr dwMaximumWorkingSetSize,
            uint flags);

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

                // Drop the base priority to Idle while throttled so Task Manager
                // recognizes the EcoQoS state and shows the efficiency-mode leaf;
                // restore Normal when a window is on screen.
                if (!SetPriorityClass(handle, enabled ? IDLE_PRIORITY_CLASS : NORMAL_PRIORITY_CLASS))
                    LogLastWin32Error("SetPriorityClass(idle/normal)", enabled);

                ApplyThreadPowerThrottling(state, enabled);
                ApplyMemoryCompression(handle, enabled);
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

        /// <summary>
        /// Restore the memory savings the old background mode gave us: lower the
        /// process memory priority while throttled and trim the working set so resident
        /// pages are paged out / compressed. On restore, bump priority back to normal
        /// (trimmed pages fault back in lazily, so there's nothing to "untrim").
        /// </summary>
        private static void ApplyMemoryCompression(IntPtr handle, bool enabled)
        {
            try
            {
                var mem = new MEMORY_PRIORITY_INFORMATION
                {
                    MemoryPriority = enabled ? MEMORY_PRIORITY_LOW : MEMORY_PRIORITY_NORMAL,
                };

                if (!SetProcessInformation(handle, ProcessMemoryPriority, ref mem,
                        (uint)Marshal.SizeOf<MEMORY_PRIORITY_INFORMATION>()))
                {
                    LogLastWin32Error("SetProcessInformation(ProcessMemoryPriority)", enabled);
                }

                if (enabled &&
                    !SetProcessWorkingSetSizeEx(handle, (IntPtr)(-1), (IntPtr)(-1), 0))
                {
                    LogLastWin32Error("SetProcessWorkingSetSizeEx(trim)", enabled);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ApplyMemoryCompression({enabled}) failed: {ex.Message}");
            }
        }

        private static void LogLastWin32Error(string operation, bool enabled)
        {
            var error = Marshal.GetLastWin32Error();
            Debug.WriteLine($"{operation} for efficiency mode={enabled} failed: Win32 error {error}");
        }
    }
}
