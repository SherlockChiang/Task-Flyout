using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Task_Flyout.Services
{
    internal static class FlyoutForegroundProbe
    {
        private const string HostWindowClassNamePrefix = "DesktopFlyoutHostClass";
        private const string HostWindowTitle = "DesktopFlyoutHostWindow";
        private const uint GetAncestorRoot = 2;
        private const uint GetAncestorRootOwner = 3;

        private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

        public static IntPtr CaptureForegroundAnchor()
            => GetForegroundAnchor(GetForegroundWindow());

        public static bool TryGetCurrentForegroundState(
            IntPtr openingForegroundAnchor,
            out bool isFlyoutForeground,
            out bool isOpeningForegroundStillActive)
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                isFlyoutForeground = false;
                isOpeningForegroundStillActive = false;
                return false;
            }

            isFlyoutForeground = IsCurrentProcessFlyoutHost(foreground)
                || IsCurrentProcessFlyoutHost(GetAncestor(foreground, GetAncestorRoot))
                || IsCurrentProcessFlyoutHost(GetAncestor(foreground, GetAncestorRootOwner));
            IntPtr currentAnchor = GetForegroundAnchor(foreground);
            isOpeningForegroundStillActive = openingForegroundAnchor == IntPtr.Zero ||
                currentAnchor == IntPtr.Zero ||
                currentAnchor == openingForegroundAnchor;
            return true;
        }

        public static bool TryActivateCurrentProcessFlyoutHost()
        {
            IntPtr host = FindCurrentProcessFlyoutHost();
            if (host == IntPtr.Zero) return false;

            _ = BringWindowToTop(host);
            _ = SetActiveWindow(host);
            _ = SetForegroundWindow(host);
            return true;
        }

        internal static bool IsFlyoutHostClassName(string className)
            => string.Equals(className, HostWindowClassNamePrefix, StringComparison.Ordinal) ||
               className.StartsWith($"{HostWindowClassNamePrefix}.", StringComparison.Ordinal);

        private static IntPtr FindCurrentProcessFlyoutHost()
        {
            IntPtr result = IntPtr.Zero;
            _ = EnumWindows((window, _) =>
            {
                if (!IsWindowVisible(window) || !IsCurrentProcessFlyoutHost(window)) return true;
                result = window;
                return false;
            }, IntPtr.Zero);
            return result;
        }

        private static IntPtr GetForegroundAnchor(IntPtr window)
        {
            if (window == IntPtr.Zero) return IntPtr.Zero;

            IntPtr rootOwner = GetAncestor(window, GetAncestorRootOwner);
            if (rootOwner != IntPtr.Zero) return rootOwner;

            IntPtr root = GetAncestor(window, GetAncestorRoot);
            return root != IntPtr.Zero ? root : window;
        }

        private static bool IsCurrentProcessFlyoutHost(IntPtr window)
        {
            if (window == IntPtr.Zero) return false;

            _ = GetWindowThreadProcessId(window, out uint processId);
            if (processId != (uint)Environment.ProcessId) return false;

            var className = new StringBuilder(64);
            if (GetClassName(window, className, className.Capacity) > 0 &&
                IsFlyoutHostClassName(className.ToString()))
            {
                return true;
            }

            var title = new StringBuilder(64);
            return GetWindowText(window, title, title.Capacity) > 0
                && string.Equals(title.ToString(), HostWindowTitle, StringComparison.Ordinal);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, uint flags);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximumCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BringWindowToTop(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr SetActiveWindow(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr window);
    }
}
