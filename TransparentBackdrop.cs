using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Task_Flyout
{
    /// <summary>
    /// Paints a fully transparent backdrop behind the XAML content. Without a
    /// backdrop, WinUI 3 fills every pixel the XAML tree leaves unpainted with
    /// opaque black; a transparent color brush makes those pixels truly
    /// transparent so whatever is behind the window — for the weather bar,
    /// the taskbar's own material — shows through.
    /// Since Windows App SDK 2.x the backdrop pipeline runs on the system
    /// compositor, so the brush must be a Windows.UI.Composition one.
    /// </summary>
    internal sealed partial class TransparentBackdrop : SystemBackdrop
    {
        private static readonly object CompositorLock = new();
        private static Windows.UI.Composition.Compositor? _compositor;
        private static object? _dispatcherQueueController;

        [StructLayout(LayoutKind.Sequential)]
        private struct DispatcherQueueOptions
        {
            public int dwSize;
            public int threadType;
            public int apartmentType;
        }

        [DllImport("CoreMessaging.dll")]
        private static extern int CreateDispatcherQueueController(
            DispatcherQueueOptions options,
            [MarshalAs(UnmanagedType.IUnknown)] out object dispatcherQueueController);

        private static Windows.UI.Composition.Compositor Compositor
        {
            get
            {
                if (_compositor == null)
                {
                    lock (CompositorLock)
                    {
                        if (_compositor == null)
                        {
                            // The system Compositor requires a Windows.System.DispatcherQueue
                            // on its thread; the WinUI 3 UI thread may only have the lifted one.
                            if (Windows.System.DispatcherQueue.GetForCurrentThread() == null)
                            {
                                var options = new DispatcherQueueOptions
                                {
                                    dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
                                    threadType = 2,   // DQTYPE_THREAD_CURRENT
                                    apartmentType = 2 // DQTAT_COM_STA
                                };
                                CreateDispatcherQueueController(options, out _dispatcherQueueController);
                            }

                            _compositor = new Windows.UI.Composition.Compositor();
                        }
                    }
                }

                return _compositor;
            }
        }

        protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
        {
            // The base initializes the default SystemBackdropConfiguration. For the weather
            // bar — whose window is reparented as a WS_CHILD of Shell_TrayWnd — the framework
            // can reject that target with E_INVALIDARG; guard it so the transparent brush is
            // still applied regardless.
            try { base.OnTargetConnected(connectedTarget, xamlRoot); } catch { }
            connectedTarget.SystemBackdrop = Compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        }

        protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
        {
            // A transparent backdrop ignores the default configuration entirely — it always
            // paints the same fully transparent brush regardless of theme, activation, or
            // high-contrast state. The base re-evaluates that configuration and throws
            // E_INVALIDARG ("参数错误") for this reparented taskbar-child window when the
            // system theme switches (e.g. the sunset schedule). There is nothing to
            // reconfigure, so swallow it instead of crashing the process.
            try { base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot); } catch { }
        }

        protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
        {
            var backdrop = disconnectedTarget.SystemBackdrop;
            disconnectedTarget.SystemBackdrop = null;
            backdrop?.Dispose();
            try { base.OnTargetDisconnected(disconnectedTarget); } catch { }
        }
    }
}
