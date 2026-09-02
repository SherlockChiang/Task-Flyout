using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Task_Flyout.Services
{
    internal static class FlyoutForegroundProbe
    {
        private const string HostWindowTitle = "DesktopFlyoutHostWindow";
        private const uint GetAncestorRoot = 2;
        private const uint GetAncestorRootOwner = 3;

        public static bool TryIsCurrentFlyoutForeground(out bool isFlyoutForeground)
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                isFlyoutForeground = false;
                return false;
            }

            isFlyoutForeground = IsCurrentProcessFlyoutHost(foreground)
                || IsCurrentProcessFlyoutHost(GetAncestor(foreground, GetAncestorRoot))
                || IsCurrentProcessFlyoutHost(GetAncestor(foreground, GetAncestorRootOwner));
            return true;
        }

        private static bool IsCurrentProcessFlyoutHost(IntPtr window)
        {
            if (window == IntPtr.Zero) return false;

            _ = GetWindowThreadProcessId(window, out uint processId);
            if (processId != (uint)Environment.ProcessId) return false;

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
    }
}
