using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private DispatcherTimer? _themeRefreshTimer;
        private bool _initialActivationDone;
        private IntPtr _taskbarHwnd = IntPtr.Zero;
        private bool _isParented;
        private bool _userHidden;
        private double _preferredLogicalWidth = 180;
        private IntPtr _fluentFlyoutHwnd = IntPtr.Zero;
        private int _refreshing;
        private int _mediaSessionInitializing;
        private GlobalSystemMediaTransportControlsSessionManager? _mediaSessionManager;
        private GlobalSystemMediaTransportControlsSession? _mediaSession;
        private int _lastBarX = int.MinValue;
        private int _lastBarY = int.MinValue;
        private int _lastBarWidth = int.MinValue;
        private int _lastBarHeight = int.MinValue;
        private IntPtr _lastInsertAfter = IntPtr.Zero;
        private DateTime _fastReparentPollingUntilUtc = DateTime.MinValue;
        private static readonly TimeSpan NormalReparentInterval = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan FastReparentInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan FastReparentDuration = TimeSpan.FromSeconds(20);
        private const double MinLogicalWidth = 80;
        private const double MaxLogicalWidth = 420;
        private bool _subclassInstalled;
        private string _lastWeatherLayerKey = "";
        private readonly StringBuilder _classNameBuffer = new(256);

        #region P/Invoke

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

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

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hdc, int x, int y);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll")]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll")]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass);

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
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref uint pvAttribute, int cbAttribute);

        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOZORDER = 0x0004;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;
        private const uint DWMWCP_DONOTROUND = 1;
        private const int SW_SHOWNOACTIVATE = 4;
        private const uint WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;
        private const uint WM_DISPLAYCHANGE = 0x007E;
        private const uint WM_SETTINGCHANGE = 0x001A;
        private const uint WM_DPICHANGED = 0x02E0;
        private const uint WM_PARENTNOTIFY = 0x0210;
        private const uint CLR_INVALID = 0xFFFFFFFF;

        private SUBCLASSPROC? _subclassProc;

        #endregion

        // System child-window class names inside Shell_TrayWnd (not widgets, skip these)
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
            _refreshTimer.Tick += async (_, _) =>
            {
                try { await RefreshWeatherAsync(); }
                catch (Exception ex) { Debug.WriteLine($"Weather refresh tick failed: {ex.Message}"); }
            };
            _refreshTimer.Start();

            _reparentTimer = new DispatcherTimer { Interval = NormalReparentInterval };
            _reparentTimer.Tick += ReparentTimer_Tick;
            _reparentTimer.Start();

            _ = InitializeMediaSessionManagerAsync();

            RootGrid.Loaded += async (s, e) =>
            {
                if (_contentPanel == null && RootGrid.FindName("ContentPanel") is FrameworkElement contentPanel)
                {
                    _contentPanel = contentPanel;
                    _contentPanel.SizeChanged += ContentPanel_SizeChanged;
                }

                await RefreshWeatherAsync();
            };

            Closed += (_, _) =>
            {
                _refreshTimer.Stop();
                _reparentTimer.Stop();
                _themeRefreshTimer?.Stop();
                UnsubscribeMediaSession();
                if (_mediaSessionManager != null)
                {
                    _mediaSessionManager.SessionsChanged -= MediaSessionsChanged;
                    _mediaSessionManager.CurrentSessionChanged -= MediaCurrentSessionChanged;
                    _mediaSessionManager = null;
                }
                DetachContentPanelEvents();
                UninstallSubclassIfAlive();
                SystemBackdrop = null;
            };
        }

        private FrameworkElement? _contentPanel;

        private void ContentPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RecomputeBarWidth();
        }

        private void DetachContentPanelEvents()
        {
            if (_contentPanel != null)
            {
                _contentPanel.SizeChanged -= ContentPanel_SizeChanged;
                _contentPanel = null;
            }
        }

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
                SuppressDwmBorder(hWnd);
                ApplyWindowsTheme();
            }
        }

        private void InstallSubclass(IntPtr hWnd)
        {
            _subclassProc = SubclassProc;
            _subclassInstalled = SetWindowSubclass(hWnd, _subclassProc, IntPtr.Zero, IntPtr.Zero);
        }

        private void UninstallSubclassIfAlive()
        {
            try
            {
                IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (_subclassInstalled && _subclassProc != null && hWnd != IntPtr.Zero && IsWindow(hWnd))
                    RemoveWindowSubclass(hWnd, _subclassProc, IntPtr.Zero);
            }
            catch { }
            finally
            {
                _subclassInstalled = false;
            }
        }

        private IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == WM_MOUSEACTIVATE)
                return (IntPtr)MA_NOACTIVATE;

            if (uMsg == WM_DISPLAYCHANGE || uMsg == WM_DPICHANGED)
            {
                // Taskbar geometry genuinely changed — re-theme, reposition, and poll faster.
                DispatcherQueue.TryEnqueue(() =>
                {
                    ApplyWindowsTheme();
                    PositionOnTaskbar();
                    UseFastReparentPolling();
                });
            }
            else if (uMsg == WM_SETTINGCHANGE)
            {
                // Light/dark theme switches (e.g. the sunset schedule) arrive as a burst of
                // WM_SETTINGCHANGE. The taskbar hasn't moved, so only refresh the bar colours,
                // debounced — the previous full reparent re-scan + 20s fast-poll on every one
                // of these made the colour switch stutter.
                DispatcherQueue.TryEnqueue(ScheduleThemeRefresh);
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

            SuppressDwmBorder(hWnd);
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
            int minPillWidth = (int)(MinLogicalWidth * scaleFactor);
            int maxPillWidth = (int)(MaxLogicalWidth * scaleFactor);
            pillWidth = Math.Clamp(pillWidth, minPillWidth, maxPillWidth);

            IntPtr previousFluentFlyout = _fluentFlyoutHwnd;
            int widgetsOffset = GetTaskbarWidgetsOffset(hWnd, tbRect);
            if (previousFluentFlyout != _fluentFlyoutHwnd)
                UseFastReparentPolling();

            int gap = (int)(6 * scaleFactor);
            int x = widgetsOffset + (widgetsOffset > 0 ? gap : 0);
            int y = (taskbarHeight - pillHeight) / 2;

                // If FluentFlyout is detected, place ourselves behind it (lower z-order) to avoid overlap
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

                bool sampledColorChanged = RefreshTaskbarSampleColor(tbRect, x, y, pillWidth, pillHeight);
                if (sampledColorChanged)
                    ApplyGlassBrushes();
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

        /// <summary>
        /// Detects taskbar widgets. FluentFlyout TaskbarWidget is a transparent top-level window
        /// spanning the full screen; its visible area is clipped via a window region — we must
        /// use GetWindowRgn to obtain the real bounding rectangle.
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
                // FluentFlyout shows media controls in both Playing and Paused states
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
                int taskbarMidX = (taskbarScreenRect.Left + taskbarScreenRect.Right) / 2;

                GetWindowThreadProcessId(ownHwnd, out uint ownPid);

                int maxRightScreen = taskbarLeft;
                _fluentFlyoutHwnd = IntPtr.Zero;

                // Enumerate direct children of Shell_TrayWnd (FluentFlyout TaskbarWindow is
                // attached here via SetParent; native Win11 Widgets are DesktopWindowContentBridge children)
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

                        _classNameBuffer.Clear();
                        GetClassName(child, _classNameBuffer, _classNameBuffer.Capacity);
                        string cls = _classNameBuffer.ToString();

                        if (SystemTaskbarClasses.Contains(cls)) continue;

                        bool isBridge = cls == "Windows.UI.Composition.DesktopWindowContentBridge";
                        bool isFluentFlyout = cls.StartsWith("HwndWrapper[FluentFlyout", StringComparison.Ordinal);

                        if (!isBridge && !isFluentFlyout) continue;

                        // FluentFlyout: only dodge when media is actively playing
                        if (isFluentFlyout && !_isMediaActive) continue;

                        if (!GetWindowRect(child, out RECT rc)) continue;

                        // FluentFlyout TaskbarWindow spans the full taskbar width; we need
                        // the window region to determine the actual visible widget area
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

                        // Remember FluentFlyout's HWND for z-order arrangement
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
        /// Gets the bounding rectangle of a window's region in screen coordinates.
        /// Region coordinates are relative to the window's top-left corner and must be
        /// offset by the GetWindowRect origin.
        /// </summary>
        private static bool TryGetWindowVisibleRect(IntPtr hWnd, RECT windowRect, out RECT visibleRect)
        {
            visibleRect = windowRect;
            IntPtr hrgn = CreateRectRgn(0, 0, 0, 0);
            try
            {
                int result = GetWindowRgn(hWnd, hrgn);
                if (result == 0) return false; // No region — the window is rectangular (i.e. windowRect)

                if (GetRgnBox(hrgn, out RECT rgnBox) == 0) return false;

                // Region coordinates are window-relative; convert to screen coordinates
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



        private bool _isLightTheme;
        private bool _barAlertActive;
        private int _themeRefreshGeneration;
        private bool _themeApplied;
        private Color? _sampledTaskbarColor;
        private DateTime _lastTaskbarColorSampleUtc = DateTime.MinValue;
        private bool _glassBrushCacheValid;
        private bool _glassBrushCacheLightTheme;
        private Color _glassBrushCacheBaseColor;
        private SolidColorBrush? _glassOuterBrush;
        private SolidColorBrush? _glassTransparentBrush;
        private SolidColorBrush? _glassIconBackdropBrush;
        private Brush? _glassRestBrush;
        private Brush? _glassHoverBrush;
        private Brush? _glassRestHighlightBrush;
        private Brush? _glassHoverHighlightBrush;

        // Collapse a burst of theme/colour change notifications into a single ApplyWindowsTheme
        // call so a sunset light/dark switch doesn't re-theme the bar several times in a row.
        private void ScheduleThemeRefresh()
        {
            if (_themeRefreshTimer == null)
            {
                _themeRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                _themeRefreshTimer.Tick += (_, _) =>
                {
                    _themeRefreshTimer!.Stop();
                    ApplyWindowsTheme(force: false);
                };
            }

            Interlocked.Increment(ref _themeRefreshGeneration);
            _themeRefreshTimer.Stop();
            _themeRefreshTimer.Start();
            _ = ApplyThemeAfterSystemSettlesAsync(_themeRefreshGeneration);
        }

        public void RefreshAfterSystemThemeChanged()
        {
            try
            {
                if (!IsAlive()) return;
                ScheduleThemeRefresh();
            }
            catch { }
        }

        private async Task ApplyThemeAfterSystemSettlesAsync(int generation)
        {
            try
            {
                await Task.Delay(800);
                QueueThemeRefreshIfCurrent(generation);

                await Task.Delay(1700);
                QueueThemeRefreshIfCurrent(generation);
            }
            catch { }
        }

        private void QueueThemeRefreshIfCurrent(int generation)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (generation != _themeRefreshGeneration) return;
                ApplyWindowsTheme(force: false);
            });
        }

        private static bool ResolveWeatherBarLightTheme(bool matchTaskbar)
        {
            var configuredTheme = App.GetConfiguredTheme();
            if (!matchTaskbar)
            {
                if (configuredTheme == ElementTheme.Light) return true;
                if (configuredTheme == ElementTheme.Dark) return false;
            }

            if (TryReadWindowsLightTheme("SystemUsesLightTheme", out bool systemLight))
                return systemLight;

            if (TryReadWindowsLightTheme("AppsUseLightTheme", out bool appsLight))
                return appsLight;

            return configuredTheme != ElementTheme.Dark;
        }

        private static bool TryReadWindowsLightTheme(string valueName, out bool isLightTheme)
        {
            isLightTheme = true;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var val = key?.GetValue(valueName);
                if (val is int intValue)
                {
                    isLightTheme = intValue == 1;
                    return true;
                }
            }
            catch { }

            return false;
        }

        public void ApplyWindowsTheme()
        {
            ApplyWindowsTheme(force: true);
        }

        private void ApplyWindowsTheme(bool force)
        {
            try
            {
                // After an Explorer restart this bar (a WS_CHILD of Shell_TrayWnd) has its
                // HWND destroyed together with the taskbar, but the managed Window object
                // lingers. Touching its XAML/composition (RequestedTheme, brushes) now
                // triggers a *native* access violation that bypasses managed try/catch and
                // crashes the process. Bail out while the window handle is dead — the
                // `root == null` check below is not enough because Content stays non-null.
                IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (hWnd == IntPtr.Zero || !IsWindow(hWnd)) return;

                var root = this.Content as FrameworkElement;
                if (root == null) return;

                bool resolvedLightTheme = ResolveWeatherBarLightTheme(matchTaskbar: true);
                bool themeChanged = !_themeApplied || resolvedLightTheme != _isLightTheme;
                if (!force && !themeChanged) return;

                if (themeChanged)
                {
                    _isLightTheme = resolvedLightTheme;
                    _themeApplied = true;
                    InvalidateGlassBrushCache();
                }

                // Push the resolved theme into the XAML tree so ThemeResource brushes
                // (TextFillColorPrimaryBrush etc.) and inherited Foregrounds match the
                // bar's actual background — otherwise text follows the system theme and
                // ends up white-on-light or black-on-dark when the two disagree.
                root.RequestedTheme = _isLightTheme ? ElementTheme.Light : ElementTheme.Dark;
                SystemBackdrop = null;

                ApplyGlassBrushes(root);
                ApplyWeatherBarTextBrush(root, includeDescription: !_barAlertActive);
            }
            catch { }
        }

        private void ApplyGlassBrushes()
        {
            if (this.Content is FrameworkElement root)
                ApplyGlassBrushes(root);
        }

        private void ApplyGlassBrushes(FrameworkElement root)
        {
            EnsureGlassBrushCache();

            var rootGrid = root.FindName("RootGrid") as Microsoft.UI.Xaml.Controls.Grid;
            var mainBorder = root.FindName("MainBorder") as Microsoft.UI.Xaml.Controls.Border;
            var topBorder = root.FindName("TopBorder") as Microsoft.UI.Xaml.Controls.Border;

            if (rootGrid != null)
                rootGrid.Background = _glassOuterBrush;

            if (mainBorder != null)
            {
                mainBorder.Background = _glassRestBrush;
                mainBorder.BorderBrush = _glassTransparentBrush;
            }

            if (topBorder != null)
            {
                topBorder.BorderBrush = _glassTransparentBrush;
                topBorder.Background = _glassRestHighlightBrush;
            }

            // Color-emoji glyphs carry their own hues, and many imported icon packs
            // ship pure-white PNGs. Give the icon a subtle counter-surface when needed.
            if (root.FindName("WeatherIconBackdrop") is Microsoft.UI.Xaml.Controls.Border iconBackdrop)
            {
                iconBackdrop.Background = _glassIconBackdropBrush;
            }
        }

        private SolidColorBrush CreateWeatherBarTextBrush()
        {
            return new SolidColorBrush(_isLightTheme ? Colors.Black : Colors.White);
        }

        private void ApplyWeatherBarTextBrush(FrameworkElement root, bool includeDescription)
        {
            var brush = CreateWeatherBarTextBrush();
            string[] names = includeDescription
                ? new[] { "WeatherIcon", "TxtTemp", "TxtDesc", "TxtLocation", "TxtFeels", "TxtHumidity", "TxtWind" }
                : new[] { "WeatherIcon", "TxtTemp", "TxtLocation", "TxtFeels", "TxtHumidity", "TxtWind" };

            foreach (string name in names)
            {
                if (root.FindName(name) is Microsoft.UI.Xaml.Controls.TextBlock textBlock)
                    textBlock.Foreground = brush;
            }
        }

        private static void SuppressDwmBorder(IntPtr hWnd)
        {
            try
            {
                uint border = DWMWA_COLOR_NONE;
                DwmSetWindowAttribute(hWnd, DWMWA_BORDER_COLOR, ref border, sizeof(uint));

                uint corners = DWMWCP_DONOTROUND;
                DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(uint));
            }
            catch { }
        }

        private Color GetWeatherBarOuterFillColor()
        {
            return _sampledTaskbarColor ?? (_isLightTheme
                ? Color.FromArgb(255, 235, 238, 242)
                : Color.FromArgb(255, 38, 40, 44));
        }

        private Color GetGlassIconBackdropColor()
        {
            return _isLightTheme
                ? Color.FromArgb(44, 0, 0, 0)
                : Colors.Transparent;
        }

        private void EnsureGlassBrushCache()
        {
            Color baseColor = GetWeatherBarOuterFillColor();
            if (_glassBrushCacheValid &&
                _glassBrushCacheLightTheme == _isLightTheme &&
                SameColor(_glassBrushCacheBaseColor, baseColor))
            {
                return;
            }

            _glassBrushCacheLightTheme = _isLightTheme;
            _glassBrushCacheBaseColor = baseColor;
            _glassOuterBrush = new SolidColorBrush(baseColor);
            _glassTransparentBrush = new SolidColorBrush(Colors.Transparent);
            _glassIconBackdropBrush = new SolidColorBrush(GetGlassIconBackdropColor());
            _glassRestBrush = CreateGlassMaterialBrush(isHovering: false);
            _glassHoverBrush = CreateGlassMaterialBrush(isHovering: true);
            _glassRestHighlightBrush = CreateGlassHighlightBrush(isHovering: false);
            _glassHoverHighlightBrush = CreateGlassHighlightBrush(isHovering: true);
            _glassBrushCacheValid = true;
        }

        private void InvalidateGlassBrushCache()
        {
            _glassBrushCacheValid = false;
            _glassOuterBrush = null;
            _glassTransparentBrush = null;
            _glassIconBackdropBrush = null;
            _glassRestBrush = null;
            _glassHoverBrush = null;
            _glassRestHighlightBrush = null;
            _glassHoverHighlightBrush = null;
        }

        private static bool SameColor(Color left, Color right)
        {
            return left.A == right.A && left.R == right.R && left.G == right.G && left.B == right.B;
        }

        private Brush CreateGlassMaterialBrush(bool isHovering)
        {
            Color baseColor = GetWeatherBarOuterFillColor();
            var brush = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1)
            };

            if (_isLightTheme)
            {
                brush.GradientStops.Add(new GradientStop { Color = Mix(baseColor, Colors.White, isHovering ? 0.9 : 0.84), Offset = 0 });
                brush.GradientStops.Add(new GradientStop { Color = Mix(Mix(baseColor, Color.FromArgb(255, 116, 218, 255), 0.07), Colors.White, isHovering ? 0.86 : 0.8), Offset = 0.34 });
                brush.GradientStops.Add(new GradientStop { Color = Mix(Mix(baseColor, Color.FromArgb(255, 210, 142, 255), 0.05), Colors.White, isHovering ? 0.84 : 0.78), Offset = 0.68 });
                brush.GradientStops.Add(new GradientStop { Color = Mix(Mix(baseColor, Color.FromArgb(255, 255, 202, 150), 0.04), Colors.White, isHovering ? 0.82 : 0.76), Offset = 1 });
            }
            else
            {
                Color darkBase = Mix(baseColor, Colors.Black, isHovering ? 0.48 : 0.58);
                brush.GradientStops.Add(new GradientStop { Color = Mix(darkBase, Colors.White, isHovering ? 0.12 : 0.08), Offset = 0 });
                brush.GradientStops.Add(new GradientStop { Color = Mix(darkBase, Color.FromArgb(255, 42, 178, 218), isHovering ? 0.12 : 0.08), Offset = 0.32 });
                brush.GradientStops.Add(new GradientStop { Color = Mix(darkBase, Color.FromArgb(255, 154, 88, 218), isHovering ? 0.1 : 0.07), Offset = 0.7 });
                brush.GradientStops.Add(new GradientStop { Color = Mix(darkBase, Color.FromArgb(255, 206, 132, 88), isHovering ? 0.08 : 0.05), Offset = 1 });
            }

            return brush;
        }

        private bool RefreshTaskbarSampleColor(RECT taskbarRect, int barX, int barY, int barWidth, int barHeight)
        {
            if ((DateTime.UtcNow - _lastTaskbarColorSampleUtc) < TimeSpan.FromMilliseconds(250)) return false;

            _lastTaskbarColorSampleUtc = DateTime.UtcNow;
            if (!TrySampleTaskbarColor(taskbarRect, barX, barY, barWidth, barHeight, out Color sampled))
                return false;

            if (_sampledTaskbarColor.HasValue && ColorDistance(_sampledTaskbarColor.Value, sampled) < 10)
                return false;

            _sampledTaskbarColor = sampled;
            InvalidateGlassBrushCache();
            return true;
        }

        private static bool TrySampleTaskbarColor(RECT taskbarRect, int barX, int barY, int barWidth, int barHeight, out Color color)
        {
            color = Colors.Transparent;

            int taskbarWidth = taskbarRect.Right - taskbarRect.Left;
            int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
            if (taskbarWidth <= 8 || taskbarHeight <= 8) return false;

            int[] yCandidates =
            {
                Math.Clamp(barY + barHeight / 2, 3, taskbarHeight - 4),
                Math.Clamp(barY + Math.Max(3, barHeight / 4), 3, taskbarHeight - 4),
                Math.Clamp(barY + Math.Max(3, barHeight * 3 / 4), 3, taskbarHeight - 4)
            };

            int[] xCandidates =
            {
                barX + barWidth + 12,
                barX + barWidth + 28,
                barX + barWidth + 48,
                barX - 12,
                barX - 28,
                taskbarWidth / 2
            };

            IntPtr dc = GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero) return false;

            try
            {
                var samples = new List<Color>(12);
                var seen = new HashSet<long>();
                foreach (int candidateX in xCandidates)
                {
                    if (candidateX >= barX - 2 && candidateX <= barX + barWidth + 2)
                        continue;

                    int relX = Math.Clamp(candidateX, 3, taskbarWidth - 4);
                    if (relX >= barX - 2 && relX <= barX + barWidth + 2)
                        continue;

                    foreach (int relY in yCandidates)
                    {
                        long key = ((long)relX << 32) | (uint)relY;
                        if (!seen.Add(key))
                            continue;

                        if (TrySamplePixel(dc, taskbarRect.Left + relX, taskbarRect.Top + relY, out Color sample))
                            samples.Add(sample);
                    }
                }

                if (samples.Count == 0) return false;

                color = MedianColor(samples);
                return true;
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, dc);
            }
        }

        private static bool TrySamplePixel(IntPtr dc, int x, int y, out Color color)
        {
            color = Colors.Transparent;
            uint pixel = GetPixel(dc, x, y);
            if (pixel == CLR_INVALID) return false;

            color = Color.FromArgb(
                255,
                (byte)(pixel & 0xFF),
                (byte)((pixel >> 8) & 0xFF),
                (byte)((pixel >> 16) & 0xFF));
            return true;
        }

        private static Color MedianColor(List<Color> samples)
        {
            return Color.FromArgb(
                255,
                Median(samples.Select(sample => sample.R)),
                Median(samples.Select(sample => sample.G)),
                Median(samples.Select(sample => sample.B)));
        }

        private static byte Median(IEnumerable<byte> values)
        {
            var sorted = values.OrderBy(value => value).ToArray();
            return sorted[sorted.Length / 2];
        }

        private static double ColorDistance(Color a, Color b)
        {
            int dr = a.R - b.R;
            int dg = a.G - b.G;
            int db = a.B - b.B;
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }

        private static Color Mix(Color a, Color b, double amount)
        {
            amount = Math.Clamp(amount, 0, 1);
            double keep = 1 - amount;
            return Color.FromArgb(
                255,
                (byte)Math.Clamp((int)Math.Round(a.R * keep + b.R * amount), 0, 255),
                (byte)Math.Clamp((int)Math.Round(a.G * keep + b.G * amount), 0, 255),
                (byte)Math.Clamp((int)Math.Round(a.B * keep + b.B * amount), 0, 255));
        }

        private Brush CreateGlassHighlightBrush(bool isHovering)
        {
            byte topAlpha = _isLightTheme
                ? (isHovering ? (byte)42 : (byte)28)
                : (isHovering ? (byte)16 : (byte)10);
            byte bottomAlpha = _isLightTheme
                ? (isHovering ? (byte)6 : (byte)3)
                : (isHovering ? (byte)4 : (byte)2);
            Color sheen = _isLightTheme ? Colors.White : Color.FromArgb(255, 255, 255, 255);

            var brush = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(0, 1)
            };
            brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(topAlpha, sheen.R, sheen.G, sheen.B), Offset = 0 });
            brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(bottomAlpha, sheen.R, sheen.G, sheen.B), Offset = 0.48 });
            brush.GradientStops.Add(new GradientStop { Color = Colors.Transparent, Offset = 1 });
            return brush;
        }

        private void MainBorder_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            var border = sender as Microsoft.UI.Xaml.Controls.Border;
            if (border == null) return;

            var topBorder = (this.Content as FrameworkElement)?.FindName("TopBorder") as Microsoft.UI.Xaml.Controls.Border;
            EnsureGlassBrushCache();

            border.Background = _glassHoverBrush;
            border.BorderBrush = _glassTransparentBrush;

            if (topBorder != null)
            {
                topBorder.BorderBrush = _glassTransparentBrush;
                topBorder.Background = _glassHoverHighlightBrush;
            }
        }

        private void MainBorder_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            // Restore resting-state brushes directly instead of re-reading the registry
            // and recalculating the full theme via ApplyWindowsTheme().
            var border = sender as Microsoft.UI.Xaml.Controls.Border;
            if (border == null) return;

            var topBorder = (this.Content as FrameworkElement)?.FindName("TopBorder") as Microsoft.UI.Xaml.Controls.Border;
            EnsureGlassBrushCache();

            border.Background = _glassRestBrush;
            border.BorderBrush = _glassTransparentBrush;

            if (topBorder != null)
            {
                topBorder.BorderBrush = _glassTransparentBrush;
                topBorder.Background = _glassRestHighlightBrush;
            }
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
                    _barAlertActive = false;
                    if (this.Content is FrameworkElement root)
                        ApplyWeatherBarTextBrush(root, includeDescription: true);
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
                    _barAlertActive = false;
                    if (this.Content is FrameworkElement root)
                        ApplyWeatherBarTextBrush(root, includeDescription: true);
                    WeatherIcon.Text = "--";
                    TxtTemp.Text = "--";
                    TxtDesc.Text = "";
                    TxtLocation.Text = "";
                    TxtLocation.Visibility = Visibility.Collapsed;
                    return;
                }

                _barAlertActive = alert != null;
                if (this.Content is FrameworkElement contentRoot)
                    ApplyWeatherBarTextBrush(contentRoot, includeDescription: alert == null);

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
                    ClearWeatherIconLayerImages(layerImages, clearSources: true);
                }
                else if (useBitmap)
                {
                    WeatherIcon.Visibility = Visibility.Collapsed;
                    ApplyWeatherIconLayerImages(displayLayers!, layerImages);
                }
                else
                {
                    WeatherIcon.Visibility = Visibility.Visible;
                    ClearWeatherIconLayerImages(layerImages, clearSources: true);
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
                    if (alert != null)
                    {
                        TxtDesc.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 170, 80));
                    }
                    else
                    {
                        TxtDesc.Foreground = CreateWeatherBarTextBrush();
                    }
                }

                // Hide secondary fields when an alert is active to avoid text crowding
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

        private void ApplyWeatherIconLayerImages(string[] displayLayers, Microsoft.UI.Xaml.Controls.Image[] layerImages)
        {
            string layerKey = string.Join('\u001f', displayLayers.Take(layerImages.Length));
            bool sourcesChanged = !string.Equals(_lastWeatherLayerKey, layerKey, StringComparison.Ordinal);
            _lastWeatherLayerKey = layerKey;

            for (int i = 0; i < layerImages.Length; i++)
            {
                var image = layerImages[i];
                if (i >= displayLayers.Length || string.IsNullOrWhiteSpace(displayLayers[i]))
                {
                    image.Visibility = Visibility.Collapsed;
                    if (sourcesChanged)
                        image.Source = null;
                    continue;
                }

                if (sourcesChanged || image.Source == null)
                {
                    try
                    {
                        image.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(displayLayers[i]));
                    }
                    catch
                    {
                        image.Source = null;
                        image.Visibility = Visibility.Collapsed;
                        continue;
                    }
                }

                image.Visibility = Visibility.Visible;
            }
        }

        private void ClearWeatherIconLayerImages(Microsoft.UI.Xaml.Controls.Image[] layerImages, bool clearSources)
        {
            if (clearSources)
                _lastWeatherLayerKey = "";

            foreach (var image in layerImages)
            {
                image.Visibility = Visibility.Collapsed;
                if (clearSources)
                    image.Source = null;
            }
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

                logical = Math.Clamp(logical, MinLogicalWidth, MaxLogicalWidth);

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

        /// <summary>True while the underlying native window still exists. Returns false once
        /// Explorer has destroyed the taskbar (and with it this WS_CHILD window).</summary>
        public bool IsAlive()
        {
            try
            {
                var h = WinRT.Interop.WindowNative.GetWindowHandle(this);
                return h != IntPtr.Zero && IsWindow(h);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Tear down a bar whose native window Explorer already destroyed so its timers stop
        /// keeping the object alive, allowing the App to replace it with a fresh window.
        /// </summary>
        public void DetachForRecovery()
        {
            try { _refreshTimer?.Stop(); } catch { }
            try { _reparentTimer?.Stop(); } catch { }
            try { _themeRefreshTimer?.Stop(); } catch { }
            try { UnsubscribeMediaSession(); } catch { }
            try { DetachContentPanelEvents(); } catch { }
            try { UninstallSubclassIfAlive(); } catch { }
            try
            {
                if (_mediaSessionManager != null)
                {
                    _mediaSessionManager.SessionsChanged -= MediaSessionsChanged;
                    _mediaSessionManager.CurrentSessionChanged -= MediaCurrentSessionChanged;
                    _mediaSessionManager = null;
                }
            }
            catch { }
            try
            {
                if (IsAlive())
                    Close();
            }
            catch { }
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
                ResetCachedWindowPlacement();
                SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
            }
            catch { }
        }
    }
}
