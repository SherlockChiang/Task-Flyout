using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Task_Flyout.Services;
using Windows.Media.Control;
using Windows.UI;


namespace Task_Flyout
{
    public sealed partial class WeatherBarWindow : Window
    {
        private AppWindow _appWindow;
        private DispatcherTimer _refreshTimer = null!;
        private DispatcherTimer _reparentTimer = null!;
        private bool _initialActivationDone;
        private IntPtr _taskbarHwnd = IntPtr.Zero;
        private bool _isParented;
        private bool _userHidden;
        private double _preferredLogicalWidth = 180;
        private IntPtr _fluentFlyoutHwnd = IntPtr.Zero;
        private IntPtr _currentRgn = IntPtr.Zero;
        private int _refreshing;
        private int _mediaSessionInitializing;
        private GlobalSystemMediaTransportControlsSessionManager? _mediaSessionManager;
        private GlobalSystemMediaTransportControlsSession? _mediaSession;
        private int _lastBarX = int.MinValue;
        private int _lastBarY = int.MinValue;
        private int _lastBarWidth = int.MinValue;
        private int _lastBarHeight = int.MinValue;
        private int _lastRegionWidth = int.MinValue;
        private int _lastRegionHeight = int.MinValue;
        private int _lastRegionRadius = int.MinValue;
        private IntPtr _lastInsertAfter = IntPtr.Zero;
        private DateTime _fastReparentPollingUntilUtc = DateTime.MinValue;
        private static readonly TimeSpan NormalReparentInterval = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan FastReparentInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan FastReparentDuration = TimeSpan.FromSeconds(20);
        #region P/Invoke

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [StructLayout(LayoutKind.Sequential)]
        private struct DWM_BLURBEHIND
        {
            public uint dwFlags;
            public bool fEnable;
            public IntPtr hRgnBlur;
            public bool fTransitionOnMaximized;
        }
        private const uint DWM_BB_ENABLE = 0x00000001;
        private const uint DWM_BB_BLURREGION = 0x00000002;

        [DllImport("dwmapi.dll", PreserveSig = false)]
        private static extern void DwmEnableBlurBehindWindow(IntPtr hWnd, ref DWM_BLURBEHIND blurBehind);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string? lpszClass, string? lpszWindow);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        private delegate bool EnumWindowProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowRgnBox(IntPtr hWnd, out RECT lprc);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll")]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int WS_CHILD = 0x40000000;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_CLIPSIBLINGS = 0x04000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_LAYERED = 0x00080000;

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
        private const uint LWA_COLORKEY = 0x01;
        private const uint LWA_ALPHA = 0x02;

        // 用一个极不可能出现的颜色作为透明 key（Win32 COLORREF 是 BGR）
        private const uint TRANSPARENCY_KEY = 0x00010201; // R=1 G=2 B=1

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

        [DllImport("user32.dll")]
        private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOZORDER = 0x0004;
        private const int SW_SHOWNOACTIVATE = 4;
        private const uint WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;
        private const uint WM_DISPLAYCHANGE = 0x007E;
        private const uint WM_SETTINGCHANGE = 0x001A;
        private const uint WM_DPICHANGED = 0x02E0;
        private const uint WM_PARENTNOTIFY = 0x0210;

        private SUBCLASSPROC? _subclassProc;

        #endregion

        // 已知第三方 widget 进程名（小写，不含 .exe）
        private static readonly HashSet<string> KnownWidgetProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "fluentflyout",
            "fluentflyoutwpf",
            "translucenttb",
            "taskbarx",
            "roundedtb",
        };

        // Shell_TrayWnd 内部系统子窗口类名（不是 widget，需要跳过）
        private static readonly HashSet<string> SystemTaskbarClasses = new(StringComparer.Ordinal)
        {
            "TrayNotifyWnd",
            "ReBarWindow32",
            "MSTaskSwWClass",
            "MSTaskListWClass",
            "Start",
            "TrayButton",
            "TrayDummySearchControl",
            "TrayShowDesktopButtonWClass",
            "TrayInputIndicatorWClass",
            "TrayClockWClass",
            "SysPager",
            "ToolbarWindow32",
            "WorkerW",
        };

        public WeatherBarWindow()
        {
            InitializeComponent();

            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);

            ConfigureBarStyle(hWnd);
            InstallSubclass(hWnd);
            ApplyWindowsTheme();

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
            _refreshTimer.Tick += async (s, e) => await RefreshWeatherAsync();
            _refreshTimer.Start();

            _reparentTimer = new DispatcherTimer { Interval = NormalReparentInterval };
            _reparentTimer.Tick += ReparentTimer_Tick;
            _reparentTimer.Start();

            _ = InitializeMediaSessionManagerAsync();

            RootGrid.Loaded += async (s, e) =>
            {
                _contentPanel = RootGrid.FindName("ContentPanel") as FrameworkElement;
                if (_contentPanel != null)
                    _contentPanel.SizeChanged += (s2, e2) => RecomputeBarWidth();
                await RefreshWeatherAsync();
            };

            Closed += (_, _) =>
            {
                _refreshTimer.Stop();
                _reparentTimer.Stop();
                UnsubscribeMediaSession();
                if (_mediaSessionManager != null)
                {
                    _mediaSessionManager.SessionsChanged -= MediaSessionsChanged;
                    _mediaSessionManager.CurrentSessionChanged -= MediaCurrentSessionChanged;
                    _mediaSessionManager = null;
                }
                SystemBackdrop = null;
            };
        }

        private FrameworkElement? _contentPanel;

        private void ReparentTimer_Tick(object? sender, object e)
        {
            try
            {
                if (_userHidden)
                {
                    _reparentTimer.Stop();
                    return;
                }

                IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return;

                IntPtr currentTaskbar = FindWindow("Shell_TrayWnd", null);
                if (currentTaskbar == IntPtr.Zero || !IsWindow(currentTaskbar))
                {
                    _taskbarHwnd = IntPtr.Zero;
                    _isParented = false;
                    return;
                }

                bool needsAttach = currentTaskbar != _taskbarHwnd || GetParent(hWnd) != currentTaskbar;
                if (needsAttach)
                {
                    _taskbarHwnd = currentTaskbar;
                    _isParented = false;
                    AttachToTaskbar();
                }

                if (_mediaSessionManager == null)
                    _ = InitializeMediaSessionManagerAsync();
                PositionOnTaskbar();

                if ((needsAttach || !IsWindowVisible(hWnd)) && IsWindow(hWnd))
                    ShowWindow(hWnd, SW_SHOWNOACTIVATE);

                // Keep polling after parenting: taskbar widgets and FluentFlyout media
                // controls can appear or disappear without changing our parent HWND.
            }
            catch
            {
                _taskbarHwnd = IntPtr.Zero;
                _isParented = false;
            }
            finally
            {
                UpdateReparentPollingInterval();
            }
        }

        private void UseFastReparentPolling()
        {
            if (_userHidden) return;

            _fastReparentPollingUntilUtc = DateTime.UtcNow.Add(FastReparentDuration);
            UpdateReparentPollingInterval();
            if (!_reparentTimer.IsEnabled)
                _reparentTimer.Start();
        }

        private void UpdateReparentPollingInterval()
        {
            var desired = DateTime.UtcNow < _fastReparentPollingUntilUtc
                ? FastReparentInterval
                : NormalReparentInterval;

            if (_reparentTimer.Interval != desired)
                _reparentTimer.Interval = desired;
        }

        private void AttachToTaskbar()
        {
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return;

            if (_taskbarHwnd == IntPtr.Zero)
                _taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (_taskbarHwnd == IntPtr.Zero || !IsWindow(_taskbarHwnd)) return;

            int style = GetWindowLong(hWnd, GWL_STYLE);
            style &= ~WS_POPUP;
            style |= WS_CHILD | WS_CLIPSIBLINGS;
            SetWindowLong(hWnd, GWL_STYLE, style);

            _isParented = SetParent(hWnd, _taskbarHwnd) != IntPtr.Zero || GetParent(hWnd) == _taskbarHwnd;
            if (_isParented)
            {
                ResetCachedWindowPlacement();
                ApplyWindowsTheme();
            }
        }

        private void InstallSubclass(IntPtr hWnd)
        {
            _subclassProc = SubclassProc;
            SetWindowSubclass(hWnd, _subclassProc, IntPtr.Zero, IntPtr.Zero);
        }

        private IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == WM_MOUSEACTIVATE)
                return (IntPtr)MA_NOACTIVATE;

            if (uMsg == WM_DISPLAYCHANGE || uMsg == WM_DPICHANGED || uMsg == WM_SETTINGCHANGE)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    ApplyWindowsTheme();
                    PositionOnTaskbar();
                    UseFastReparentPolling();
                });
            }

            if (uMsg == WM_PARENTNOTIFY)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    IntPtr parent = GetParent(hWnd);
                    if (parent == IntPtr.Zero || parent != _taskbarHwnd)
                    {
                        _isParented = false;
                        UseFastReparentPolling();
                    }
                });
            }

            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        private void ConfigureBarStyle(IntPtr hWnd)
        {
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsAlwaysOnTop = false;
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
            }

            _appWindow.IsShownInSwitchers = false;

            int style = GetWindowLong(hWnd, GWL_STYLE);
            style &= ~WS_THICKFRAME;
            style &= ~WS_CAPTION;
            SetWindowLong(hWnd, GWL_STYLE, style);

            int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            exStyle |= WS_EX_TOOLWINDOW;
            exStyle |= WS_EX_NOACTIVATE;
            SetWindowLong(hWnd, GWL_EXSTYLE, exStyle);
        }

        public void PositionOnTaskbar()
        {
            try
            {
                if (_userHidden) return;

                IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

                if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return;
                if (_taskbarHwnd == IntPtr.Zero || !IsWindow(_taskbarHwnd) || !_isParented)
                {
                    _isParented = false;
                    return;
                }
                if (!GetWindowRect(_taskbarHwnd, out RECT tbRect)) return;
                if (!GetClientRect(_taskbarHwnd, out RECT tbClient)) return;

            double scaleFactor = GetDpiForWindow(_taskbarHwnd) / 96.0;
            int taskbarHeight = tbClient.Bottom - tbClient.Top;

            int verticalInset = (int)(4 * scaleFactor);
            int pillHeight = taskbarHeight - verticalInset * 2;
            if (pillHeight < (int)(28 * scaleFactor))
                pillHeight = (int)(28 * scaleFactor);
            int pillWidth = (int)(_preferredLogicalWidth * scaleFactor);
            if (pillWidth < (int)(80 * scaleFactor)) pillWidth = (int)(80 * scaleFactor);

            IntPtr previousFluentFlyout = _fluentFlyoutHwnd;
            int widgetsOffset = GetTaskbarWidgetsOffset(hWnd, tbRect);
            if (previousFluentFlyout != _fluentFlyoutHwnd)
                UseFastReparentPolling();

            int gap = (int)(6 * scaleFactor);
            int x = widgetsOffset + (widgetsOffset > 0 ? gap : 0);
            int y = (taskbarHeight - pillHeight) / 2;

                // 如果检测到 FluentFlyout，把自己排在它后面（z-order 更低），避免遮挡
                IntPtr insertAfter = _fluentFlyoutHwnd != IntPtr.Zero ? _fluentFlyoutHwnd : IntPtr.Zero;
                uint flags = SWP_NOACTIVATE | SWP_SHOWWINDOW;
                if (_fluentFlyoutHwnd == IntPtr.Zero)
                    flags |= SWP_NOZORDER;

                if (insertAfter != IntPtr.Zero && !IsWindow(insertAfter))
                {
                    insertAfter = IntPtr.Zero;
                    flags |= SWP_NOZORDER;
                }

                bool placementChanged =
                    x != _lastBarX ||
                    y != _lastBarY ||
                    pillWidth != _lastBarWidth ||
                    pillHeight != _lastBarHeight ||
                    insertAfter != _lastInsertAfter ||
                    !IsWindowVisible(hWnd);

                if (placementChanged)
                {
                    SetWindowPos(hWnd, insertAfter, x, y, pillWidth, pillHeight, flags);
                    _lastBarX = x;
                    _lastBarY = y;
                    _lastBarWidth = pillWidth;
                    _lastBarHeight = pillHeight;
                    _lastInsertAfter = insertAfter;
                }

                // 圆角裁剪窗口外形（Win32 region）
                int cornerRadius = (int)(8 * scaleFactor);
                if (pillWidth != _lastRegionWidth ||
                    pillHeight != _lastRegionHeight ||
                    cornerRadius != _lastRegionRadius)
                {
                    IntPtr rgn = CreateRoundRectRgn(0, 0, pillWidth + 1, pillHeight + 1, cornerRadius, cornerRadius);
                    if (rgn != IntPtr.Zero)
                    {
                        SetWindowRgn(hWnd, rgn, true);
                        // SetWindowRgn takes ownership — do NOT DeleteObject(rgn).
                        _currentRgn = rgn;
                        _lastRegionWidth = pillWidth;
                        _lastRegionHeight = pillHeight;
                        _lastRegionRadius = cornerRadius;
                    }
                }
            }
            catch
            {
                _taskbarHwnd = IntPtr.Zero;
                _isParented = false;
            }
        }

        private void ResetCachedWindowPlacement()
        {
            _lastBarX = int.MinValue;
            _lastBarY = int.MinValue;
            _lastBarWidth = int.MinValue;
            _lastBarHeight = int.MinValue;
            _lastRegionWidth = int.MinValue;
            _lastRegionHeight = int.MinValue;
            _lastRegionRadius = int.MinValue;
            _lastInsertAfter = IntPtr.Zero;
        }

        [DllImport("gdi32.dll")]
        private static extern int GetRgnBox(IntPtr hrgn, out RECT lprc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern int GetWindowRgn(IntPtr hWnd, IntPtr hRgn);

        // 把这两个常量也加到 P/Invoke 区域
        private const int SIMPLEREGION = 2;
        private const int COMPLEXREGION = 3;

        /// <summary>
        /// 检测任务栏上的 widget。FluentFlyout TaskbarWidget 是横跨屏幕的透明顶层窗口，
        /// 实际可见区域通过 window region 裁剪 —— 必须用 GetWindowRgn 才能拿到真实矩形。
        /// </summary>
        private bool _isMediaActive;

        private async Task InitializeMediaSessionManagerAsync()
        {
            if (Interlocked.CompareExchange(ref _mediaSessionInitializing, 1, 0) != 0)
                return;

            try
            {
                var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                DispatcherQueue.TryEnqueue(() =>
                {
                    _mediaSessionManager = manager;
                    _mediaSessionManager.SessionsChanged += MediaSessionsChanged;
                    _mediaSessionManager.CurrentSessionChanged += MediaCurrentSessionChanged;
                    UpdateCurrentMediaSession();
                });
            }
            catch
            {
                DispatcherQueue.TryEnqueue(() => _isMediaActive = false);
                Interlocked.Exchange(ref _mediaSessionInitializing, 0);
            }
        }

        private void MediaSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
            => DispatcherQueue.TryEnqueue(UpdateCurrentMediaSession);

        private void MediaCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
            => DispatcherQueue.TryEnqueue(UpdateCurrentMediaSession);

        private void MediaPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
            => DispatcherQueue.TryEnqueue(() =>
            {
                UpdateMediaPlaybackState(sender);
                PositionOnTaskbar();
                UseFastReparentPolling();
            });

        private void UpdateCurrentMediaSession()
        {
            var current = _mediaSessionManager?.GetCurrentSession();
            if (!ReferenceEquals(current, _mediaSession))
            {
                UnsubscribeMediaSession();
                _mediaSession = current;
                if (_mediaSession != null)
                    _mediaSession.PlaybackInfoChanged += MediaPlaybackInfoChanged;
            }

            UpdateMediaPlaybackState(_mediaSession);
            PositionOnTaskbar();
            UseFastReparentPolling();
        }

        private void UpdateMediaPlaybackState(GlobalSystemMediaTransportControlsSession? session)
        {
            try
            {
                var status = session?.GetPlaybackInfo()?.PlaybackStatus;
                // Playing/Paused 状态下 FluentFlyout 都会显示媒体控件
                _isMediaActive = status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                              || status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused;
            }
            catch
            {
                _isMediaActive = false;
            }
        }

        private void UnsubscribeMediaSession()
        {
            if (_mediaSession != null)
            {
                _mediaSession.PlaybackInfoChanged -= MediaPlaybackInfoChanged;
                _mediaSession = null;
            }
        }

        private int GetTaskbarWidgetsOffset(IntPtr ownHwnd, RECT taskbarScreenRect)
        {
            try
            {
                int taskbarLeft = taskbarScreenRect.Left;
                int taskbarTop = taskbarScreenRect.Top;
                int taskbarBottom = taskbarScreenRect.Bottom;
                int taskbarHeight = taskbarBottom - taskbarTop;
                int taskbarMidX = (taskbarScreenRect.Left + taskbarScreenRect.Right) / 2;

                GetWindowThreadProcessId(ownHwnd, out uint ownPid);

                int maxRightScreen = taskbarLeft;
                _fluentFlyoutHwnd = IntPtr.Zero;

                // 枚举 Shell_TrayWnd 的直接子窗口（FluentFlyout TaskbarWindow 通过 SetParent 挂载在这里，
                // Win11 原生 Widgets 也是 DesktopWindowContentBridge 子窗口）
                if (_taskbarHwnd != IntPtr.Zero)
                {
                    IntPtr child = IntPtr.Zero;
                    while (true)
                    {
                        child = FindWindowEx(_taskbarHwnd, child, null, null);
                        if (child == IntPtr.Zero) break;
                        if (child == ownHwnd) continue;
                        if (!IsWindowVisible(child)) continue;

                        GetWindowThreadProcessId(child, out uint pid);
                        if (pid == ownPid) continue;

                        var clsBuf = new StringBuilder(256);
                        GetClassName(child, clsBuf, clsBuf.Capacity);
                        string cls = clsBuf.ToString();

                        if (SystemTaskbarClasses.Contains(cls)) continue;

                        bool isBridge = cls == "Windows.UI.Composition.DesktopWindowContentBridge";
                        bool isFluentFlyout = cls.StartsWith("HwndWrapper[FluentFlyout", StringComparison.Ordinal);

                        if (!isBridge && !isFluentFlyout) continue;

                        // FluentFlyout: 只在有媒体正在播放时才避让
                        if (isFluentFlyout && !_isMediaActive) continue;

                        if (!GetWindowRect(child, out RECT rc)) continue;

                        // FluentFlyout TaskbarWindow 覆盖整个任务栏宽度，必须用 window region
                        // 拿到真实的 widget 可见区域
                        bool hasRegion = TryGetWindowVisibleRect(child, rc, out RECT rgnRc);
                        RECT visibleRc = hasRegion ? rgnRc : rc;

                        int vw = visibleRc.Right - visibleRc.Left;
                        int vh = visibleRc.Bottom - visibleRc.Top;
                        int taskbarWidth = taskbarScreenRect.Right - taskbarScreenRect.Left;
                        int maxWidgetWidth = Math.Min(900, Math.Max(360, (int)(taskbarWidth * 0.60)));
                        if (vw <= 0 || vh <= 0 || vw > maxWidgetWidth) continue;
                        if (visibleRc.Bottom <= taskbarTop || visibleRc.Top >= taskbarBottom) continue;
                        if (visibleRc.Left >= taskbarMidX) continue;

                        Debug.WriteLine($"[WeatherBar] Widget detected: cls={cls} " +
                                        $"fullRc={rc.Left},{rc.Top},{rc.Right},{rc.Bottom} " +
                                        $"visibleRc={visibleRc.Left},{visibleRc.Top},{visibleRc.Right},{visibleRc.Bottom} " +
                                        $"hasRegion={hasRegion} mediaPlaying={_isMediaActive}");

                        // 记住 FluentFlyout 的 HWND 用于 z-order 排列
                        if (isFluentFlyout)
                            _fluentFlyoutHwnd = child;

                        int right = Math.Min(visibleRc.Right, taskbarMidX);
                        if (right > maxRightScreen) maxRightScreen = right;
                    }
                }

                return maxRightScreen > taskbarLeft ? maxRightScreen - taskbarLeft : 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 获取窗口 region 的边界矩形（屏幕坐标）。
        /// region 坐标是相对窗口左上角的，需要加上 GetWindowRect 的 offset。
        /// </summary>
        private static bool TryGetWindowVisibleRect(IntPtr hWnd, RECT windowRect, out RECT visibleRect)
        {
            visibleRect = windowRect;
            IntPtr hrgn = CreateRectRgn(0, 0, 0, 0);
            try
            {
                int result = GetWindowRgn(hWnd, hrgn);
                if (result == 0) return false; // 没有 region，窗口是矩形（即 windowRect）

                if (GetRgnBox(hrgn, out RECT rgnBox) == 0) return false;

                // region 坐标是客户区/窗口相对坐标，转换为屏幕坐标
                visibleRect = new RECT
                {
                    Left = windowRect.Left + rgnBox.Left,
                    Top = windowRect.Top + rgnBox.Top,
                    Right = windowRect.Left + rgnBox.Right,
                    Bottom = windowRect.Top + rgnBox.Bottom
                };
                return true;
            }
            finally
            {
                DeleteObject(hrgn);
            }
        }

        private void TryAccumulateWidgetRight(
            IntPtr hWnd, IntPtr ownHwnd, uint ownPid,
            int taskbarLeft, int taskbarTop, int taskbarBottom, int taskbarMidX, int taskbarHeight,
            ref int maxRightScreen)
        {
            if (hWnd == ownHwnd) return;
            if (!IsWindowVisible(hWnd)) return;

            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == ownPid) return;

            var clsBuf = new StringBuilder(256);
            GetClassName(hWnd, clsBuf, clsBuf.Capacity);
            string cls = clsBuf.ToString();

            // 跳过 Shell 自身的系统子窗口
            if (SystemTaskbarClasses.Contains(cls)) return;

            if (!GetWindowRect(hWnd, out RECT rc)) return;
            int w = rc.Right - rc.Left;
            int h = rc.Bottom - rc.Top;
            if (w <= 0 || h <= 0) return;

            // 尺寸护栏：widget 通常 < 600px 宽，高度不超过任务栏 2 倍（动画/阴影时会超出）
            if (w > 600) return;
            if (h > taskbarHeight * 2 + 20) return;

            // 必须与任务栏垂直相交
            if (rc.Bottom <= taskbarTop || rc.Top >= taskbarBottom) return;

            // 必须与任务栏左半边有横向重叠（不要求整体落在左半边 —— FluentFlyout 居中时会横跨中线）
            if (rc.Left >= taskbarMidX) return;

            // 判断是否是 widget：
            //   1) 类名是 WinUI bridge (Win11 Widgets)
            //   2) 类名是 WPF HwndWrapper（FluentFlyout 等）
            //   3) 进程名在已知列表
            bool isBridge = cls == "Windows.UI.Composition.DesktopWindowContentBridge";
            bool isWpf = cls.StartsWith("HwndWrapper[", StringComparison.Ordinal);
            bool isKnownProc = TryGetProcessName(pid, out string procName) &&
                               KnownWidgetProcesses.Contains(procName);

            if (!(isBridge || isWpf || isKnownProc)) return;

            // 避让终点不能超过任务栏中线，避免被横跨中线的 widget 推到屏幕中央
            int effectiveRight = Math.Min(rc.Right, taskbarMidX);
            if (effectiveRight > maxRightScreen)
                maxRightScreen = effectiveRight;
        }

        private static bool TryGetProcessName(uint pid, out string name)
        {
            name = string.Empty;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                name = p.ProcessName;
                return !string.IsNullOrEmpty(name);
            }
            catch
            {
                return false;
            }
        }

        private bool _isLightTheme;

        public void ApplyWindowsTheme()
        {
            try
            {
                var configuredTheme = App.GetConfiguredTheme();
                if (configuredTheme == ElementTheme.Light)
                {
                    _isLightTheme = true;
                }
                else if (configuredTheme == ElementTheme.Dark)
                {
                    _isLightTheme = false;
                }
                else
                {
                    using var key = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                    var val = key?.GetValue("SystemUsesLightTheme");
                    _isLightTheme = val is int v && v == 1;
                }

                var root = this.Content as FrameworkElement;
                if (root == null) return;
                var rootGrid = root.FindName("RootGrid") as Microsoft.UI.Xaml.Controls.Grid;
                var mainBorder = root.FindName("MainBorder") as Microsoft.UI.Xaml.Controls.Border;
                var topBorder = root.FindName("TopBorder") as Microsoft.UI.Xaml.Controls.Border;
                var weatherService = (App.Current as App)?.WeatherService;
                bool transparent = weatherService?.WeatherBarTransparentBackground == true;

                IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                SystemBackdrop = null;
                if (transparent)
                {
                    // Ask DWM for a blur-behind on this HWND. Combined with transparent
                    // XAML backgrounds the bar should show wallpaper/taskbar blurred.
                    // This is a long shot on a child window of Shell_TrayWnd — if DWM
                    // refuses, we'll just see solid Transparent (i.e. black swap chain).
                    int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
                    exStyle &= ~WS_EX_LAYERED;
                    SetWindowLong(hWnd, GWL_EXSTYLE, exStyle);

                    SetDwmBlurBehind(hWnd, true);

                    if (rootGrid != null)
                        rootGrid.Background = new SolidColorBrush(Colors.Transparent);
                    if (mainBorder != null)
                        mainBorder.Background = new SolidColorBrush(_isLightTheme
                            ? Color.FromArgb(120, 255, 255, 255)
                            : Color.FromArgb(140, 0, 0, 0));
                    if (topBorder != null)
                        topBorder.BorderBrush = new SolidColorBrush(_isLightTheme
                            ? Color.FromArgb(40, 0, 0, 0)
                            : Color.FromArgb(60, 255, 255, 255));
                    return;
                }

                SetDwmBlurBehind(hWnd, false);

                int opaqueExStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
                opaqueExStyle &= ~WS_EX_LAYERED;
                SetWindowLong(hWnd, GWL_EXSTYLE, opaqueExStyle);
                SystemBackdrop = null;

                if (_isLightTheme)
                {
                    if (rootGrid != null)
                        rootGrid.Background = new SolidColorBrush(Color.FromArgb(255, 243, 243, 243));
                    if (mainBorder != null)
                        mainBorder.Background = new SolidColorBrush(Color.FromArgb(255, 243, 243, 243));
                    if (topBorder != null)
                        topBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));
                }
                else
                {
                    if (rootGrid != null)
                        rootGrid.Background = new SolidColorBrush(Color.FromArgb(255, 44, 44, 44));
                    if (mainBorder != null)
                        mainBorder.Background = new SolidColorBrush(Color.FromArgb(255, 44, 44, 44));
                    if (topBorder != null)
                        topBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
                }
            }
            catch { }
        }

        private void MainBorder_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            var border = sender as Microsoft.UI.Xaml.Controls.Border;
            if (border == null) return;

            var topBorder = (this.Content as FrameworkElement)?.FindName("TopBorder") as Microsoft.UI.Xaml.Controls.Border;
            var weatherService = (App.Current as App)?.WeatherService;
            bool transparent = weatherService?.WeatherBarTransparentBackground == true;

            if (_isLightTheme)
            {
                border.Background = transparent
                    ? CreateWeatherBarAcrylicBrush(true, true)
                    : new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
                if (topBorder != null)
                    topBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(237, 255, 255, 255));
            }
            else
            {
                border.Background = transparent
                    ? CreateWeatherBarAcrylicBrush(false, true)
                    : new SolidColorBrush(Color.FromArgb(255, 60, 60, 60));
                if (topBorder != null)
                    topBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255));
            }
        }

        private void MainBorder_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            ApplyWindowsTheme(); // restore resting background + border
        }

        private static void SetDwmBlurBehind(IntPtr hWnd, bool enable)
        {
            try
            {
                var bb = new DWM_BLURBEHIND
                {
                    dwFlags = DWM_BB_ENABLE,
                    fEnable = enable,
                    hRgnBlur = IntPtr.Zero,
                    fTransitionOnMaximized = false
                };
                DwmEnableBlurBehindWindow(hWnd, ref bb);
            }
            catch { }
        }

        private static Brush CreateWeatherBarAcrylicBrush(bool isLightTheme, bool isHover)
        {
            var tint = isLightTheme
                ? Color.FromArgb(255, 246, 246, 246)
                : Color.FromArgb(255, isHover ? (byte)78 : (byte)66, isHover ? (byte)78 : (byte)66, isHover ? (byte)78 : (byte)66);
            var fallback = isLightTheme
                ? Color.FromArgb(255, isHover ? (byte)248 : (byte)238, isHover ? (byte)248 : (byte)238, isHover ? (byte)248 : (byte)238)
                : Color.FromArgb(255, isHover ? (byte)72 : (byte)58, isHover ? (byte)72 : (byte)58, isHover ? (byte)72 : (byte)58);

            return new AcrylicBrush
            {
                AlwaysUseFallback = true,
                TintColor = tint,
                TintOpacity = isLightTheme ? 0.86 : 0.92,
                TintLuminosityOpacity = isLightTheme ? 0.90 : 0.82,
                FallbackColor = fallback
            };
        }

        private static string FormatBarLocation(string city)
        {
            if (string.IsNullOrWhiteSpace(city)) return "";

            string text = city.Trim();
            int comma = text.IndexOf(',');
            if (comma > 0)
                text = text[..comma].Trim();

            return text.Length > 18 ? text[..18] + "..." : text;
        }

        public async Task RefreshWeatherAsync()
        {
            if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0) return;
            try
            {
                await RefreshWeatherCoreAsync();
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        }

        private async Task RefreshWeatherCoreAsync()
        {
            var weatherService = (App.Current as App)?.WeatherService;
            if (weatherService == null || !weatherService.IsEnabled)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    WeatherIcon.Text = "--";
                    TxtTemp.Text = "--";
                    TxtDesc.Text = "";
                    TxtLocation.Text = "";
                    TxtLocation.Visibility = Visibility.Collapsed;
                });
                return;
            }

            var info = await weatherService.GetWeatherAsync(false);
            var alert = (info != null && weatherService.BarAlertsEnabled)
                ? weatherService.DetectUpcomingAlert(info)
                : null;

            DispatcherQueue.TryEnqueue(() =>
            {
                if (info == null)
                {
                    WeatherIcon.Text = "--";
                    TxtTemp.Text = "--";
                    TxtDesc.Text = "";
                    TxtLocation.Text = "";
                    TxtLocation.Visibility = Visibility.Collapsed;
                    return;
                }

                var enabledFields = weatherService.GetEnabledBarFields();

                // Icon: pack-bitmap > emoji. When an alert is active, try the pack's alert
                // drawable first so imported icon packs are honored; otherwise fall back to the
                // emoji alert glyph.
                bool showIcon = enabledFields.Contains("icon");
                bool packActive = info.IconLayerUris != null && info.IconLayerUris.Length > 0;
                string[]? displayLayers = null;
                if (showIcon)
                {
                    if (alert != null && packActive)
                    {
                        var alertLayers = IconPackService.Instance.TryResolveAlertLayers(alert.Type, info.IsDayTime);
                        if (alertLayers != null && alertLayers.Length > 0) displayLayers = alertLayers;
                    }
                    else if (alert == null && packActive)
                    {
                        displayLayers = info.IconLayerUris;
                    }
                }
                bool useBitmap = displayLayers != null && displayLayers.Length > 0;
                var layerImages = new[] { WeatherIconImage, WeatherIconImage1, WeatherIconImage2, WeatherIconImage3 };

                if (!showIcon)
                {
                    WeatherIcon.Visibility = Visibility.Collapsed;
                    foreach (var img in layerImages) img.Visibility = Visibility.Collapsed;
                }
                else if (useBitmap)
                {
                    WeatherIcon.Visibility = Visibility.Collapsed;
                    for (int i = 0; i < layerImages.Length; i++)
                    {
                        if (i < displayLayers!.Length && !string.IsNullOrEmpty(displayLayers[i]))
                        {
                            try
                            {
                                layerImages[i].Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(displayLayers[i]));
                                layerImages[i].Visibility = Visibility.Visible;
                            }
                            catch { layerImages[i].Visibility = Visibility.Collapsed; }
                        }
                        else layerImages[i].Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    WeatherIcon.Visibility = Visibility.Visible;
                    foreach (var img in layerImages) img.Visibility = Visibility.Collapsed;
                    WeatherIcon.Text = alert != null ? alert.Icon : info.Icon;
                    WeatherIcon.FontFamily = new FontFamily(info.IconFont);
                }

                // Temperature
                bool showTemp = enabledFields.Contains("temperature");
                TxtTemp.Visibility = showTemp ? Visibility.Visible : Visibility.Collapsed;
                if (showTemp) TxtTemp.Text = info.Temperature ?? "";

                // Description (alert message replaces description text when active)
                bool showDesc = enabledFields.Contains("description") || alert != null;
                TxtDesc.Visibility = showDesc ? Visibility.Visible : Visibility.Collapsed;
                if (showDesc)
                {
                    TxtDesc.Text = alert != null ? alert.Message : (info.Description ?? "");
                    TxtDesc.Foreground = new SolidColorBrush(alert != null
                        ? Color.FromArgb(255, 255, 170, 80)
                        : Color.FromArgb(255, 180, 180, 180));
                }

                // 告警激活时隐藏副字段，避免文字挤占
                bool hasAlert = alert != null;

                // Location
                bool showLocation = !hasAlert && enabledFields.Contains("location") && !string.IsNullOrWhiteSpace(info.City);
                TxtLocation.Visibility = showLocation ? Visibility.Visible : Visibility.Collapsed;
                if (showLocation) TxtLocation.Text = FormatBarLocation(info.City);

                // Feels like
                bool showFeels = !hasAlert && enabledFields.Contains("feelslike") && !string.IsNullOrEmpty(info.FeelsLike);
                TxtFeels.Visibility = showFeels ? Visibility.Visible : Visibility.Collapsed;
                if (showFeels) TxtFeels.Text = info.FeelsLike;

                // Humidity
                bool showHum = !hasAlert && enabledFields.Contains("humidity") && !string.IsNullOrEmpty(info.Humidity);
                TxtHumidity.Visibility = showHum ? Visibility.Visible : Visibility.Collapsed;
                if (showHum) TxtHumidity.Text = info.Humidity;

                // Wind
                bool showWind = !hasAlert && enabledFields.Contains("wind") && !string.IsNullOrEmpty(info.WindSpeed);
                TxtWind.Visibility = showWind ? Visibility.Visible : Visibility.Collapsed;
                if (showWind) TxtWind.Text = info.WindSpeed;

                RecomputeBarWidth();
            });
        }

        /// <summary>
        /// Measure the content panel and resize the bar accordingly.
        /// </summary>
        private void RecomputeBarWidth()
        {
            try
            {
                var panel = _contentPanel;
                if (panel == null) return;

                // Prefer ActualWidth (post-layout) over Measure+DesiredSize so we reflect the
                // real laid-out size including all item spacing.
                double contentLogical = panel.ActualWidth;
                if (contentLogical <= 0)
                {
                    panel.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
                    contentLogical = panel.DesiredSize.Width;
                }
                if (contentLogical <= 0) return;

                // ContentPanel Margin="10,0,10,0" = 20 DIPs. Add small slack for rounding.
                double logical = contentLogical + 24;

                if (logical < 80) logical = 80;

                if (Math.Abs(logical - _preferredLogicalWidth) > 1)
                {
                    _preferredLogicalWidth = logical;
                    PositionOnTaskbar();
                }
            }
            catch { }
        }

        private void ContentPanel_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            App.OpenMainWindowInternal(win => win.NavigateToWeather());
        }

        public void ShowBar()
        {
            try
            {
                _userHidden = false;
                IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return;

                if (!_initialActivationDone)
                {
                    Activate();
                    _initialActivationDone = true;
                }

                ApplyWindowsTheme();
                UseFastReparentPolling();

                if (!_isParented)
                    AttachToTaskbar();

                PositionOnTaskbar();
                if (IsWindow(hWnd))
                    ShowWindow(hWnd, SW_SHOWNOACTIVATE);
            }
            catch
            {
                _taskbarHwnd = IntPtr.Zero;
                _isParented = false;
            }
        }

        public void HideBar()
        {
            try
            {
                _userHidden = true;
                IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return;

                ShowWindow(hWnd, 0); // SW_HIDE
                _reparentTimer.Stop();
                // Clear region — system releases the previous one.
                SetWindowRgn(hWnd, IntPtr.Zero, true);
                _currentRgn = IntPtr.Zero;
                ResetCachedWindowPlacement();
                SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
            }
            catch { }
        }
    }
}
