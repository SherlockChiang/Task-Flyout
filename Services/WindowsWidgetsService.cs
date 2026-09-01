using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using Windows.Management.Deployment;

namespace Task_Flyout.Services
{
    internal readonly record struct TaskbarEntrySnapshot(
        bool WasPresent,
        string SerializedValue,
        RegistryValueKind Kind);

    /// <summary>
    /// Detects and toggles the Windows-owned Widgets taskbar entry point.
    ///
    /// This service deliberately does not inject XAML into Explorer. The native
    /// weather/news surface belongs to Explorer and the Web Experience Pack; a
    /// regular WinUI process can only request that the shell entry point be shown.
    /// </summary>
    internal static class WindowsWidgetsService
    {
        public const string WebExperiencePackageFamilyName = "MicrosoftWindows.Client.WebExperience_cw5n1h2txyewy";
        public const string TaskbarAdvancedRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        public const string TaskbarWidgetsValueName = "TaskbarDa";

        internal const string CapturedSettingKey = "WeatherBarNativeTaskbarDaCaptured";
        internal const string CapturedPresentKey = "WeatherBarNativeTaskbarDaPresent";
        internal const string CapturedValueKey = "WeatherBarNativeTaskbarDaValue";
        internal const string CapturedKindKey = "WeatherBarNativeTaskbarDaKind";
        internal const string TaskbarStateMutexName = @"Local\TaskFlyout.WindowsWidgets.TaskbarDa";
        internal const int TaskbarStateMutexTimeoutMilliseconds = 3000;
        private const int WM_SETTINGCHANGE = 0x001A;
        private const int HWND_BROADCAST = 0xffff;
        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private const uint SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST = 0x0000;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint Msg,
            IntPtr wParam,
            string? lParam,
            uint fuFlags,
            uint uTimeout,
            out IntPtr lpdwResult);

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(
            uint wEventId,
            uint uFlags,
            IntPtr dwItem1,
            IntPtr dwItem2);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out WindowRect lpRect);

        public static WindowsWidgetsAvailability Detect()
        {
            bool buildSupported = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);
            bool taskbarPresent = FindWindow("Shell_TrayWnd", null) != IntPtr.Zero ||
                                  FindWindow("Shell_SecondaryTrayWnd", null) != IntPtr.Zero;
            bool packageRegistered = IsWebExperiencePackRegistered();
            bool nativeEntryPointPresent = taskbarPresent && HasNativeWidgetsBridge();

            return WindowsWidgetsAvailabilityPolicy.Resolve(
                new WindowsWidgetsAvailabilitySignals(
                    buildSupported,
                    packageRegistered,
                    taskbarPresent,
                    nativeEntryPointPresent));
        }

        /// <summary>
        /// Makes the shell's native Widgets button visible. The previous value is
        /// captured once and restored only when the current value still matches the
        /// value written by this service.
        /// </summary>
        public static bool TryEnableTaskbarEntry(IDictionary<string, object> localSettings, out string detail)
        {
            ArgumentNullException.ThrowIfNull(localSettings);
            Mutex? stateMutex = null;
            bool lockTaken = false;
            try
            {
                stateMutex = new Mutex(initiallyOwned: false, TaskbarStateMutexName);
                lockTaken = TryEnterTaskbarStateMutex(stateMutex);
                if (!lockTaken)
                {
                    detail = "Another Task Flyout process is updating the Windows Widgets taskbar setting.";
                    return false;
                }

                if (StandaloneTaskbarWidgetsService.HasCapturedTaskbarEntry(
                        localSettings))
                {
                    detail = "Standalone taskbar mode still owns the Windows Widgets taskbar setting.";
                    return false;
                }

                using RegistryKey key = Registry.CurrentUser.CreateSubKey(TaskbarAdvancedRegistryPath, writable: true)
                    ?? throw new InvalidOperationException("Taskbar settings key is unavailable.");

                object? existing = key.GetValue(TaskbarWidgetsValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                RegistryValueKind existingKind = existing != null
                    ? key.GetValueKind(TaskbarWidgetsValueName)
                    : RegistryValueKind.DWord;

                // If Windows already owns an enabled DWORD value, there is nothing
                // for Task Flyout to restore later. Avoid claiming ownership of a
                // setting that this process did not change.
                if (!HasCapturedTaskbarEntry(localSettings) &&
                    IsServiceOwnedTaskbarValue(existing, existingKind))
                {
                    RemoveUncommittedSnapshotMetadata(localSettings);
                    ReleaseTaskbarStateMutex(stateMutex, ref lockTaken);
                    BroadcastTaskbarSettingsChanged();
                    detail = "The native Widgets taskbar entry is already enabled by Windows.";
                    return true;
                }

                if (!HasCapturedTaskbarEntry(localSettings))
                {
                    WriteCapturedTaskbarEntry(localSettings, existing, existingKind);
                }
                else if (!TryReadCapturedTaskbarEntry(localSettings, out _))
                {
                    // Legacy builds wrote the commit marker before the payload. An
                    // interrupted legacy write cannot be restored safely, so leave
                    // the registry untouched and discard only the unusable metadata.
                    if (!TryClearCapturedTaskbarEntry(localSettings))
                    {
                        detail = "The saved TaskbarDa snapshot is incomplete and could not be cleared.";
                        return false;
                    }

                    detail = "The saved TaskbarDa snapshot is incomplete; the native Widgets setting was left unchanged.";
                    return false;
                }

                key.SetValue(TaskbarWidgetsValueName, 1, RegistryValueKind.DWord);
                ReleaseTaskbarStateMutex(stateMutex, ref lockTaken);
                BroadcastTaskbarSettingsChanged();
                detail = "Native Widgets taskbar entry requested; Explorer may need a moment to recreate it.";
                return true;
            }
            catch (Exception ex)
            {
                detail = $"Unable to enable the native Widgets taskbar entry: {ex.Message}";
                return false;
            }
            finally
            {
                if (lockTaken)
                {
                    ReleaseTaskbarStateMutex(stateMutex!, ref lockTaken);
                }

                stateMutex?.Dispose();
            }
        }

        /// <summary>
        /// Restores the user's TaskbarDa value after leaving native mode. If the user
        /// changed the value while native mode was active, it is left untouched.
        /// </summary>
        public static bool TryRestoreTaskbarEntry(IDictionary<string, object> localSettings, out string detail)
        {
            ArgumentNullException.ThrowIfNull(localSettings);
            Mutex? stateMutex = null;
            bool lockTaken = false;
            try
            {
                stateMutex = new Mutex(initiallyOwned: false, TaskbarStateMutexName);
                lockTaken = TryEnterTaskbarStateMutex(stateMutex);
                if (!lockTaken)
                {
                    detail = "Another Task Flyout process is updating the Windows Widgets taskbar setting.";
                    return false;
                }

                if (!HasCapturedTaskbarEntry(localSettings))
                {
                    detail = "No TaskbarDa value was changed by Task Flyout.";
                    return true;
                }

                if (!TryReadCapturedTaskbarEntry(localSettings, out TaskbarEntrySnapshot snapshot))
                {
                    // Never guess a default from a partial or malformed snapshot.
                    // That would turn storage corruption into a user-setting change.
                    if (!TryClearCapturedTaskbarEntry(localSettings))
                    {
                        detail = "The saved TaskbarDa snapshot is incomplete and could not be cleared.";
                        return false;
                    }

                    detail = "The saved TaskbarDa snapshot was incomplete; the current Windows setting was preserved.";
                    return true;
                }

                using RegistryKey key = Registry.CurrentUser.CreateSubKey(TaskbarAdvancedRegistryPath, writable: true)
                    ?? throw new InvalidOperationException("Taskbar settings key is unavailable.");
                object? current = key.GetValue(TaskbarWidgetsValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                RegistryValueKind currentKind = current != null
                    ? key.GetValueKind(TaskbarWidgetsValueName)
                    : RegistryValueKind.Unknown;
                bool restored = false;
                if (IsServiceOwnedTaskbarValue(current, currentKind))
                {
                    if (snapshot.WasPresent)
                    {
                        if (!TryDeserializeRegistryValue(snapshot.SerializedValue, snapshot.Kind, out object? restoredValue))
                        {
                            throw new InvalidOperationException("The saved TaskbarDa value is invalid.");
                        }

                        key.SetValue(TaskbarWidgetsValueName, restoredValue!, snapshot.Kind);
                    }
                    else
                    {
                        key.DeleteValue(TaskbarWidgetsValueName, throwOnMissingValue: false);
                    }

                    restored = true;
                }

                if (!TryClearCapturedTaskbarEntry(localSettings))
                {
                    ReleaseTaskbarStateMutex(stateMutex, ref lockTaken);
                    if (restored) BroadcastTaskbarSettingsChanged();
                    detail = "The Windows Widgets setting was resolved, but its ownership marker could not be cleared; restoration will be retried.";
                    return false;
                }

                ReleaseTaskbarStateMutex(stateMutex, ref lockTaken);
                if (restored) BroadcastTaskbarSettingsChanged();
                detail = restored
                    ? "The previous Windows Widgets taskbar setting was restored."
                    : "TaskbarDa no longer matched Task Flyout's DWORD value; the current Windows setting was preserved.";
                return true;
            }
            catch (Exception ex)
            {
                detail = $"Unable to restore the Windows Widgets taskbar setting: {ex.Message}";
                return false;
            }
            finally
            {
                if (lockTaken)
                {
                    ReleaseTaskbarStateMutex(stateMutex!, ref lockTaken);
                }

                stateMutex?.Dispose();
            }
        }

        /// <summary>
        /// Reports persisted ownership independently of process-local state so a
        /// later process can finish restoration after an update or crash.
        /// </summary>
        internal static bool HasCapturedTaskbarEntry(IDictionary<string, object> localSettings)
        {
            ArgumentNullException.ThrowIfNull(localSettings);
            return GetBoolean(localSettings, CapturedSettingKey);
        }

        internal static bool TryReadCapturedTaskbarEntry(
            IDictionary<string, object> localSettings,
            out TaskbarEntrySnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(localSettings);
            snapshot = default;
            if (!GetBoolean(localSettings, CapturedSettingKey) ||
                !localSettings.TryGetValue(CapturedPresentKey, out object? presentValue) || presentValue is not bool wasPresent ||
                !localSettings.TryGetValue(CapturedValueKey, out object? valueValue) || valueValue is not string serializedValue ||
                !localSettings.TryGetValue(CapturedKindKey, out object? kindValue) || kindValue is not string kindText ||
                !Enum.TryParse(kindText, ignoreCase: false, out RegistryValueKind kind) ||
                !Enum.IsDefined(kind))
            {
                return false;
            }

            if (wasPresent && !TryDeserializeRegistryValue(serializedValue, kind, out _))
            {
                return false;
            }

            snapshot = new TaskbarEntrySnapshot(wasPresent, serializedValue, kind);
            return true;
        }

        internal static bool IsServiceOwnedTaskbarValue(object? value, RegistryValueKind kind)
            => kind == RegistryValueKind.DWord && value is int number && number == 1;

        internal static bool TryClearCapturedTaskbarEntry(IDictionary<string, object> localSettings)
        {
            ArgumentNullException.ThrowIfNull(localSettings);
            try
            {
                // The commit marker is removed first. Once it is gone, leftover
                // payload fields cannot cause another process to alter TaskbarDa.
                localSettings.Remove(CapturedSettingKey);
            }
            catch
            {
                return false;
            }

            RemoveUncommittedSnapshotMetadata(localSettings);
            return true;
        }

        internal static void WriteCapturedTaskbarEntry(
            IDictionary<string, object> localSettings,
            object? existingValue,
            RegistryValueKind existingKind)
        {
            bool wasPresent = existingValue != null;
            string serializedValue = string.Empty;
            if (wasPresent && !TrySerializeRegistryValue(existingValue!, existingKind, out serializedValue))
            {
                throw new InvalidOperationException($"TaskbarDa uses the unsupported registry type {existingKind}.");
            }

            string value = wasPresent ? serializedValue : string.Empty;
            try
            {
                RemoveUncommittedSnapshotMetadata(localSettings);
                localSettings[CapturedPresentKey] = wasPresent;
                localSettings[CapturedValueKey] = value;
                localSettings[CapturedKindKey] = existingKind.ToString();

                // This is the snapshot commit point and must remain the final write.
                localSettings[CapturedSettingKey] = true;
            }
            catch
            {
                _ = TryClearCapturedTaskbarEntry(localSettings);
                throw;
            }
        }

        private static void RemoveUncommittedSnapshotMetadata(IDictionary<string, object> localSettings)
        {
            foreach (string key in new[] { CapturedPresentKey, CapturedValueKey, CapturedKindKey })
            {
                try
                {
                    localSettings.Remove(key);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Removing stale native Widgets snapshot metadata failed: {ex.Message}");
                }
            }
        }

        private static bool IsWebExperiencePackRegistered()
        {
            try
            {
                var manager = new PackageManager();
                return manager.FindPackagesForUser(string.Empty, WebExperiencePackageFamilyName).Any();
            }
            catch
            {
                // PackageQuery is not available in every packaged desktop context.
                // A registry check is only a conservative fallback for a package
                // registered to the current SID; a staged all-user bundle alone is
                // not considered sufficient.
                return IsWebExperiencePackRegisteredForCurrentUserSid();
            }
        }

        private static bool IsWebExperiencePackRegisteredForCurrentUserSid()
        {
            try
            {
                string? sid = WindowsIdentity.GetCurrent().User?.Value;
                if (string.IsNullOrEmpty(sid)) return false;

                string path = $@"SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\{sid}";
                using RegistryKey? userRoot = Registry.LocalMachine.OpenSubKey(path);
                return userRoot?.GetSubKeyNames().Any(name =>
                    name.StartsWith("MicrosoftWindows.Client.WebExperience_", StringComparison.OrdinalIgnoreCase)) == true;
            }
            catch
            {
                return false;
            }
        }

        private static bool HasNativeWidgetsBridge()
        {
            // A generic DesktopWindowContentBridge can host unrelated taskbar XAML,
            // so the class name alone is not evidence of the Widgets entry point.
            // The native Widgets bridge is a visible, bounded surface at the leading
            // edge of a taskbar. Keep this a diagnostic signal rather than a hard
            // prerequisite because a freshly enabled entry may materialize shortly.
            return HasNativeWidgetsBridge(FindWindow("Shell_TrayWnd", null)) ||
                   HasNativeWidgetsBridge(FindWindow("Shell_SecondaryTrayWnd", null));
        }

        private static bool HasNativeWidgetsBridge(IntPtr taskbar)
        {
            if (taskbar == IntPtr.Zero ||
                !IsWindowVisible(taskbar) ||
                !GetWindowRect(taskbar, out WindowRect taskbarRect))
            {
                return false;
            }

            bool found = false;
            EnumChildWindows(taskbar, (hWnd, _) =>
            {
                var buffer = new System.Text.StringBuilder(256);
                if (GetClassName(hWnd, buffer, buffer.Capacity) > 0 &&
                    string.Equals(buffer.ToString(), "Windows.UI.Composition.DesktopWindowContentBridge", StringComparison.Ordinal) &&
                    GetWindowRect(hWnd, out WindowRect bridgeRect) &&
                    IsPlausibleNativeWidgetsBridge(taskbarRect, bridgeRect, IsWindowVisible(hWnd)))
                {
                    found = true;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
            return found;
        }

        internal static bool IsPlausibleNativeWidgetsBridge(
            WindowRect taskbarRect,
            WindowRect bridgeRect,
            bool isVisible)
        {
            int taskbarWidth = taskbarRect.Right - taskbarRect.Left;
            int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
            int bridgeWidth = bridgeRect.Right - bridgeRect.Left;
            int overlapWidth = Math.Min(taskbarRect.Right, bridgeRect.Right) -
                               Math.Max(taskbarRect.Left, bridgeRect.Left);
            int overlapHeight = Math.Min(taskbarRect.Bottom, bridgeRect.Bottom) -
                                Math.Max(taskbarRect.Top, bridgeRect.Top);
            if (!isVisible ||
                taskbarWidth <= 0 ||
                taskbarHeight <= 0 ||
                bridgeWidth <= 0 ||
                overlapWidth <= 0 ||
                overlapHeight <= 0)
            {
                return false;
            }

            int leftTolerance = Math.Max(32, taskbarHeight);
            int minimumPlausibleWidth = Math.Max(32, taskbarHeight);
            int taskbarMidpoint = taskbarRect.Left + (taskbarWidth / 2);
            int maximumPlausibleWidth = Math.Min(900, Math.Max(360, (int)Math.Ceiling(taskbarWidth * 0.60)));
            int bridgeMidpoint = bridgeRect.Left + (bridgeWidth / 2);
            return bridgeRect.Left >= taskbarRect.Left - leftTolerance &&
                   bridgeRect.Left <= taskbarRect.Left + leftTolerance &&
                   bridgeMidpoint < taskbarMidpoint &&
                   bridgeWidth >= minimumPlausibleWidth &&
                   overlapHeight >= Math.Max(1, taskbarHeight / 2) &&
                   bridgeWidth <= maximumPlausibleWidth;
        }

        private static bool GetBoolean(IDictionary<string, object> values, string key)
            => values.TryGetValue(key, out object? value) && value is bool enabled && enabled;

        internal static bool TrySerializeRegistryValue(
            object value,
            RegistryValueKind kind,
            out string serializedValue)
        {
            try
            {
                serializedValue = kind switch
                {
                    RegistryValueKind.DWord when value is int dword =>
                        dword.ToString(CultureInfo.InvariantCulture),
                    RegistryValueKind.QWord when value is long qword =>
                        qword.ToString(CultureInfo.InvariantCulture),
                    RegistryValueKind.String or RegistryValueKind.ExpandString when value is string text => text,
                    RegistryValueKind.MultiString when value is string[] strings => JsonSerializer.Serialize(strings),
                    RegistryValueKind.Binary or RegistryValueKind.None when value is byte[] bytes =>
                        Convert.ToBase64String(bytes),
                    _ => throw new InvalidOperationException()
                };
                return true;
            }
            catch
            {
                serializedValue = string.Empty;
                return false;
            }
        }

        internal static bool TryDeserializeRegistryValue(
            string serializedValue,
            RegistryValueKind kind,
            out object? value)
        {
            try
            {
                value = kind switch
                {
                    RegistryValueKind.DWord when int.TryParse(
                        serializedValue,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int dword) => dword,
                    RegistryValueKind.QWord when long.TryParse(
                        serializedValue,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out long qword) => qword,
                    RegistryValueKind.String or RegistryValueKind.ExpandString => serializedValue,
                    RegistryValueKind.MultiString =>
                        JsonSerializer.Deserialize<string[]>(serializedValue)
                        ?? throw new JsonException("The multi-string value is null."),
                    RegistryValueKind.Binary or RegistryValueKind.None => Convert.FromBase64String(serializedValue),
                    _ => null
                };
                return value != null;
            }
            catch
            {
                value = null;
                return false;
            }
        }

        internal static bool TryEnterTaskbarStateMutex(Mutex stateMutex)
        {
            try
            {
                return stateMutex.WaitOne(TaskbarStateMutexTimeoutMilliseconds);
            }
            catch (AbandonedMutexException)
            {
                // The previous owner exited mid-update. The persisted snapshot is
                // the recovery record, so the new owner should continue.
                return true;
            }
        }

        internal static void ReleaseTaskbarStateMutex(Mutex stateMutex, ref bool lockTaken)
        {
            if (!lockTaken) return;
            try
            {
                stateMutex.ReleaseMutex();
            }
            catch (ApplicationException ex)
            {
                Debug.WriteLine($"Releasing native Widgets settings lock failed: {ex.Message}");
            }
            finally
            {
                lockTaken = false;
            }
        }

        internal static bool BroadcastTaskbarSettingsChanged()
        {
            try
            {
                bool notified = SendMessageTimeout(
                    new IntPtr(HWND_BROADCAST),
                    WM_SETTINGCHANGE,
                    IntPtr.Zero,
                    "TraySettings",
                    SMTO_ABORTIFHUNG,
                    1000,
                    out _) != IntPtr.Zero;
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
                return notified;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Broadcasting taskbar settings failed: {ex.Message}");
                return false;
            }
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [StructLayout(LayoutKind.Sequential)]
        internal readonly struct WindowRect
        {
            public WindowRect(int left, int top, int right, int bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }

            public readonly int Left;
            public readonly int Top;
            public readonly int Right;
            public readonly int Bottom;
        }
    }
}
