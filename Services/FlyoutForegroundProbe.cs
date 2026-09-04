using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Task_Flyout.Services
{
    internal static class FlyoutForegroundProbe
    {
        private const string HostWindowClassName = "DesktopFlyoutHostClass";
        private const string HostWindowTitle = "DesktopFlyoutHostWindow";
        private const uint GetAncestorRoot = 2;
        private const uint GetAncestorRootOwner = 3;

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
                string.Equals(className.ToString(), HostWindowClassName, StringComparison.Ordinal))
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
    }
}
