using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Windows.Management.Deployment;

namespace Task_Flyout.Services
{
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

        private const string CapturedSettingKey = "WeatherBarNativeTaskbarDaCaptured";
        private const string CapturedPresentKey = "WeatherBarNativeTaskbarDaPresent";
        private const string CapturedValueKey = "WeatherBarNativeTaskbarDaValue";
        private const string CapturedKindKey = "WeatherBarNativeTaskbarDaKind";
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
            try
            {
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(TaskbarAdvancedRegistryPath, writable: true)
                    ?? throw new InvalidOperationException("Taskbar settings key is unavailable.");

                if (!GetBoolean(localSettings, CapturedSettingKey))
                {
                    object? existing = key.GetValue(TaskbarWidgetsValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    localSettings[CapturedSettingKey] = true;
                    localSettings[CapturedPresentKey] = existing != null;
                    localSettings[CapturedValueKey] = existing?.ToString() ?? string.Empty;
                    localSettings[CapturedKindKey] = (existing != null
                        ? key.GetValueKind(TaskbarWidgetsValueName)
                        : RegistryValueKind.DWord).ToString();
                }

                key.SetValue(TaskbarWidgetsValueName, 1, RegistryValueKind.DWord);
                BroadcastTaskbarSettingsChanged();
                detail = "Native Widgets taskbar entry requested; Explorer may need a moment to recreate it.";
                return true;
            }
            catch (Exception ex)
            {
                detail = $"Unable to enable the native Widgets taskbar entry: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Restores the user's TaskbarDa value after leaving native mode. If the user
        /// changed the value while native mode was active, it is left untouched.
        /// </summary>
        public static bool TryRestoreTaskbarEntry(IDictionary<string, object> localSettings, out string detail)
        {
            ArgumentNullException.ThrowIfNull(localSettings);
            try
            {
                if (!GetBoolean(localSettings, CapturedSettingKey))
                {
                    detail = "No TaskbarDa value was changed by Task Flyout.";
                    return true;
                }

                using RegistryKey key = Registry.CurrentUser.CreateSubKey(TaskbarAdvancedRegistryPath, writable: true)
                    ?? throw new InvalidOperationException("Taskbar settings key is unavailable.");
                object? current = key.GetValue(TaskbarWidgetsValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (IsEnabledValue(current))
                {
                    bool wasPresent = GetBoolean(localSettings, CapturedPresentKey);
                    if (wasPresent)
                    {
                        string value = localSettings[CapturedValueKey] as string ?? "0";
                        string kindText = localSettings[CapturedKindKey] as string ?? nameof(RegistryValueKind.DWord);
                        RegistryValueKind kind = Enum.TryParse(kindText, out RegistryValueKind parsed)
                            ? parsed
                            : RegistryValueKind.DWord;
                        key.SetValue(TaskbarWidgetsValueName, ParseRegistryValue(value, kind), kind);
                    }
                    else
                    {
                        key.DeleteValue(TaskbarWidgetsValueName, throwOnMissingValue: false);
                    }

                    BroadcastTaskbarSettingsChanged();
                }

                localSettings.Remove(CapturedSettingKey);
                localSettings.Remove(CapturedPresentKey);
                localSettings.Remove(CapturedValueKey);
                localSettings.Remove(CapturedKindKey);
                detail = "The previous Windows Widgets taskbar setting was restored.";
                return true;
            }
            catch (Exception ex)
            {
                detail = $"Unable to restore the Windows Widgets taskbar setting: {ex.Message}";
                return false;
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
            // The bridge is created only after Explorer has materialized the native
            // XAML entry point. We intentionally keep this a diagnostic signal rather
            // than a hard prerequisite: a freshly enabled button may appear shortly.
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero) return false;

            bool found = false;
            EnumChildWindows(taskbar, (hWnd, _) =>
            {
                var buffer = new System.Text.StringBuilder(256);
                if (GetClassName(hWnd, buffer, buffer.Capacity) > 0 &&
                    string.Equals(buffer.ToString(), "Windows.UI.Composition.DesktopWindowContentBridge", StringComparison.Ordinal))
                {
                    found = true;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
            return found;
        }

        private static bool IsEnabledValue(object? value)
            => value switch
            {
                int number => number != 0,
                uint number => number != 0,
                long number => number != 0,
                string text => text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase),
                _ => false
            };

        private static bool GetBoolean(IDictionary<string, object> values, string key)
            => values.TryGetValue(key, out object? value) && value is bool enabled && enabled;

        private static object ParseRegistryValue(string value, RegistryValueKind kind)
        {
            return kind switch
            {
                RegistryValueKind.String or RegistryValueKind.ExpandString => value,
                RegistryValueKind.QWord when long.TryParse(value, out long qword) => qword,
                _ when int.TryParse(value, out int dword) => dword,
                _ => 0
            };
        }

        private static void BroadcastTaskbarSettingsChanged()
        {
            try
            {
                _ = SendMessageTimeout(
                    new IntPtr(HWND_BROADCAST),
                    WM_SETTINGCHANGE,
                    IntPtr.Zero,
                    "TraySettings",
                    SMTO_ABORTIFHUNG,
                    1000,
                    out _);
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Broadcasting taskbar settings failed: {ex.Message}");
            }
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
    }
}
