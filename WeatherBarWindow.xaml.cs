using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Task_Flyout.Services;
using Windows.Graphics;

namespace Task_Flyout
{
    public sealed partial class WeatherBarWindow : Window
    {
        private AppWindow _appWindow;
        private DispatcherTimer _refreshTimer;
        private bool _initialActivationDone;

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

        private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll")]
        private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int SW_SHOWNOACTIVATE = 4;
        private const uint WM_MOUSEACTIVATE = 0x0021;
        private const int MA_NOACTIVATE = 3;

        // prevent GC collection of the delegate
        private SUBCLASSPROC _subclassProc;

        #endregion

        public WeatherBarWindow()
        {
            InitializeComponent();

            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);

            ConfigureBarStyle(hWnd);
            InstallSubclass(hWnd);
            PositionOnTaskbar();

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
            _refreshTimer.Tick += async (s, e) => await RefreshWeatherAsync();
            _refreshTimer.Start();

            RootGrid.Loaded += async (s, e) => await RefreshWeatherAsync();
        }

        private void InstallSubclass(IntPtr hWnd)
        {
            // Subclass the Win32 window to intercept WM_MOUSEACTIVATE
            // and return MA_NOACTIVATE so clicking the bar never activates it.
            _subclassProc = SubclassProc;
            SetWindowSubclass(hWnd, _subclassProc, IntPtr.Zero, IntPtr.Zero);
        }

        private IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == WM_MOUSEACTIVATE)
            {
                return (IntPtr)MA_NOACTIVATE;
            }
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        private void ConfigureBarStyle(IntPtr hWnd)
        {
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsAlwaysOnTop = true;
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
            }

            _appWindow.IsShownInSwitchers = false;

            // Remove window chrome
            int style = GetWindowLong(hWnd, GWL_STYLE);
            style &= ~WS_THICKFRAME;
            style &= ~WS_CAPTION;
            SetWindowLong(hWnd, GWL_STYLE, style);

            // Tool window (no taskbar entry) + no-activate
            int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            exStyle |= WS_EX_TOOLWINDOW;
            exStyle |= WS_EX_NOACTIVATE;
            SetWindowLong(hWnd, GWL_EXSTYLE, exStyle);
        }

        public void PositionOnTaskbar()
        {
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            double scaleFactor = GetDpiForWindow(hWnd) / 96.0;

            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var display = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
            var workArea = display.WorkArea;
            var outerBounds = display.OuterBounds;

            // Taskbar height in physical pixels
            int taskbarPhysicalHeight = outerBounds.Height - workArea.Height;
            if (taskbarPhysicalHeight < 20)
                taskbarPhysicalHeight = (int)(48 * scaleFactor);

            // Pill fills the taskbar height with small vertical insets
            int verticalInset = (int)(4 * scaleFactor);
            int pillHeight = taskbarPhysicalHeight - verticalInset * 2;
            if (pillHeight < (int)(28 * scaleFactor))
                pillHeight = (int)(28 * scaleFactor);

            int pillWidth = (int)(180 * scaleFactor);

            _appWindow.Resize(new SizeInt32(pillWidth, pillHeight));

            // Position: left side of taskbar, vertically centered
            int horizontalMargin = (int)(8 * scaleFactor);
            int x = workArea.X + horizontalMargin;
            int y = workArea.Y + workArea.Height + (taskbarPhysicalHeight - pillHeight) / 2;

            _appWindow.Move(new PointInt32(x, y));
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
                    WeatherIcon.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(info.IconFont);
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
            PositionOnTaskbar();

            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

            if (!_initialActivationDone)
            {
                // WinUI 3 requires Activate() once to render XAML content
                Activate();
                _initialActivationDone = true;
            }

            // Show without stealing focus
            ShowWindow(hWnd, SW_SHOWNOACTIVATE);
            SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        public void HideBar()
        {
            _appWindow.Hide();
        }
    }
}
