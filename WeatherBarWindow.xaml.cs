using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Task_Flyout.Services;
using Windows.Graphics;
using Windows.UI;

namespace Task_Flyout
{
    public sealed partial class WeatherBarWindow : Window
    {
        private AppWindow _appWindow;
        private DispatcherTimer _refreshTimer;
        private DispatcherTimer _reparentTimer;
        private bool _initialActivationDone;
        private IntPtr _taskbarHwnd = IntPtr.Zero;
        private bool _isParented;

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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string lpszWindow);

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
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

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
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_LAYERED = 0x00080000;
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

        private SUBCLASSPROC _subclassProc;

        #endregion

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

            // 定期检查父窗口状态 + 重新定位（应对任务栏重启、widget 启停）
            _reparentTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _reparentTimer.Tick += ReparentTimer_Tick;
            _reparentTimer.Start();

            RootGrid.Loaded += async (s, e) => await RefreshWeatherAsync();
        }

        private void ReparentTimer_Tick(object sender, object e)
        {
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            IntPtr currentTaskbar = FindWindow("Shell_TrayWnd", null);

            // 任务栏句柄变了（explorer 重启），需要重新挂载
            if (currentTaskbar != _taskbarHwnd || GetParent(hWnd) != currentTaskbar)
            {
                _taskbarHwnd = currentTaskbar;
                _isParented = false;
                AttachToTaskbar();
            }

            PositionOnTaskbar();

            if (!IsWindowVisible(hWnd))
                ShowWindow(hWnd, SW_SHOWNOACTIVATE);
        }

        private void AttachToTaskbar()
        {
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (_taskbarHwnd == IntPtr.Zero)
                _taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (_taskbarHwnd == IntPtr.Zero) return;

            // 改为子窗口样式
            int style = GetWindowLong(hWnd, GWL_STYLE);
            style &= ~WS_POPUP;
            style |= WS_CHILD | WS_CLIPSIBLINGS;
            SetWindowLong(hWnd, GWL_STYLE, style);

            SetParent(hWnd, _taskbarHwnd);
            _isParented = true;
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
                DispatcherQueue.TryEnqueue(() => PositionOnTaskbar());
            }

            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        private void ConfigureBarStyle(IntPtr hWnd)
        {
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsAlwaysOnTop = false; // 作为子窗口不需要 topmost
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
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

            if (_taskbarHwnd == IntPtr.Zero || !_isParented)
                return;

            if (!GetWindowRect(_taskbarHwnd, out RECT tbRect))
                return;
            if (!GetClientRect(_taskbarHwnd, out RECT tbClient))
                return;

            double scaleFactor = GetDpiForWindow(_taskbarHwnd) / 96.0;

            int taskbarHeight = tbClient.Bottom - tbClient.Top;

            // 胶囊尺寸
            int verticalInset = (int)(4 * scaleFactor);
            int pillHeight = taskbarHeight - verticalInset * 2;
            if (pillHeight < (int)(28 * scaleFactor))
                pillHeight = (int)(28 * scaleFactor);
            int pillWidth = (int)(180 * scaleFactor);

            // 计算避让偏移（Win11 Widgets + 第三方 widget）
            int widgetsOffset = GetTaskbarWidgetsOffset(hWnd, tbRect);

            // 子窗口坐标 = 相对 Shell_TrayWnd 客户区
            int horizontalMargin = (int)(8 * scaleFactor);
            int x = horizontalMargin + widgetsOffset;
            int y = (taskbarHeight - pillHeight) / 2;

            SetWindowPos(hWnd, IntPtr.Zero, x, y, pillWidth, pillHeight,
                SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        /// <summary>
        /// 检测任务栏左侧已存在的 widget（Win11 Widgets、FluentFlyout 等），
        /// 返回需要避让的横向偏移（相对 Shell_TrayWnd 客户区，物理像素）。
        /// </summary>
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

                int maxRightScreen = 0;

                // 1. Win11 Widgets（作为 Shell_TrayWnd 的子窗口）
                bool widgetsEnabled = false;
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(
                        @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
                    var val = key?.GetValue("TaskbarDa");
                    widgetsEnabled = val is int v && v == 1;
                }
                catch { }

                if (widgetsEnabled && _taskbarHwnd != IntPtr.Zero)
                {
                    IntPtr bridge = IntPtr.Zero;
                    while (true)
                    {
                        bridge = FindWindowEx(_taskbarHwnd, bridge,
                            "Windows.UI.Composition.DesktopWindowContentBridge", null);
                        if (bridge == IntPtr.Zero) break;
                        if (bridge == ownHwnd) continue;

                        if (GetWindowRect(bridge, out RECT rc) && IsWindowVisible(bridge))
                        {
                            int w = rc.Right - rc.Left;
                            if (rc.Left <= taskbarLeft + 10 && w > 0 && w < 500)
                                maxRightScreen = Math.Max(maxRightScreen, rc.Right);
                        }
                    }
                }

                // 2. 第三方 widget（独立顶层窗口，如 FluentFlyout）
                EnumWindows((hWnd, lParam) =>
                {
                    if (hWnd == ownHwnd || hWnd == _taskbarHwnd) return true;
                    if (!IsWindowVisible(hWnd)) return true;

                    GetWindowThreadProcessId(hWnd, out uint pid);
                    if (pid == ownPid) return true;

                    if (!GetWindowRect(hWnd, out RECT rc)) return true;

                    int w = rc.Right - rc.Left;
                    int h = rc.Bottom - rc.Top;
                    if (w <= 0 || h <= 0) return true;

                    // 窗口必须在任务栏附近：任务栏内部 或 紧贴任务栏上方 200px 内
                    bool inTaskbarBand = rc.Bottom > taskbarTop - 200 && rc.Top < taskbarBottom + 10;
                    if (!inTaskbarBand) return true;

                    // 只关心左半边
                    if (rc.Left >= taskbarMidX) return true;

                    // 必须是 widget 尺寸
                    if (w > 500 || h > taskbarHeight * 3) return true;

                    // 进一步过滤：已知 widget 的进程名
                    bool isKnownWidget = false;
                    try
                    {
                        var proc = Process.GetProcessById((int)pid);
                        var name = proc.ProcessName.ToLowerInvariant();
                        if (name.Contains("fluentflyout") ||
                            name.Contains("translucenttb") ||
                            name.Contains("taskbarx") ||
                            name.Contains("widget"))
                        {
                            isKnownWidget = true;
                        }
                    }
                    catch { }

                    // 或者：尺寸非常像 widget（宽 < 400，高接近任务栏）
                    bool looksLikeWidget = w < 400 && h < taskbarHeight * 2;

                    if (isKnownWidget || looksLikeWidget)
                        maxRightScreen = Math.Max(maxRightScreen, rc.Right);

                    return true;
                }, IntPtr.Zero);

                return maxRightScreen > taskbarLeft ? maxRightScreen - taskbarLeft : 0;
            }
            catch
            {
                return 0;
            }
        }

        private void ApplyWindowsTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var val = key?.GetValue("SystemUsesLightTheme");
                bool lightTheme = val is int v && v == 1;

                if (lightTheme)
                {
                    TopBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0));
                }
                else
                {
                    TopBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
                }
            }
            catch { }
        }

        private void MainBorder_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            var targetColor = Color.FromArgb(30, 255, 255, 255);
            MainBorder.Background = new SolidColorBrush(targetColor);
        }

        private void MainBorder_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            MainBorder.Background = new SolidColorBrush(Colors.Transparent);
        }

        public async Task RefreshWeatherAsync()
        {
            var weatherService = (App.Current as App)?.WeatherService;
            if (weatherService == null || !weatherService.IsEnabled)
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    WeatherIcon.Text = "--";
                    TxtTemp.Text = "--";
                    TxtDesc.Text = "";
                });
                return;
            }

            var info = await weatherService.GetWeatherAsync(false);

            DispatcherQueue.TryEnqueue(() =>
            {
                if (info != null)
                {
                    WeatherIcon.Text = info.Icon;
                    WeatherIcon.FontFamily = new FontFamily(info.IconFont);
                    TxtTemp.Text = info.Temperature;
                    TxtDesc.Text = info.Description;
                }
                else
                {
                    WeatherIcon.Text = "--";
                    TxtTemp.Text = "--";
                    TxtDesc.Text = "";
                }
            });
        }

        private void WeatherButton_Click(object sender, RoutedEventArgs e)
        {
            App.OpenMainWindowInternal(win => win.NavigateToWeather());
        }

        public void ShowBar()
        {
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

            if (!_initialActivationDone)
            {
                Activate();
                _initialActivationDone = true;
            }

            // 首次显示时挂载到任务栏
            if (!_isParented)
                AttachToTaskbar();

            PositionOnTaskbar();
            ShowWindow(hWnd, SW_SHOWNOACTIVATE);
        }

        public void HideBar()
        {
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            ShowWindow(hWnd, 0); // SW_HIDE
        }
    }
}