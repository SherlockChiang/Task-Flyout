using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;
using Task_Flyout.Services;
using Windows.Graphics;

namespace Task_Flyout
{
    public sealed partial class WeatherBarWindow : Window
    {
        private AppWindow _appWindow;
        private DispatcherTimer _refreshTimer;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

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

        public WeatherBarWindow()
        {
            InitializeComponent();

            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            _appWindow = AppWindow.GetFromWindowId(windowId);

            ConfigureBarStyle();
            PositionOnTaskbar();

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
            _refreshTimer.Tick += async (s, e) => await RefreshWeatherAsync();
            _refreshTimer.Start();

            RootGrid.Loaded += async (s, e) => await RefreshWeatherAsync();
        }

        private void ConfigureBarStyle()
        {
            // Frameless, non-resizable, always on top
            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsAlwaysOnTop = true;
                presenter.SetBorderAndTitleBar(false, false);
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
            }

            _appWindow.IsShownInSwitchers = false;

            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

            // Remove window chrome
            int style = GetWindowLong(hWnd, GWL_STYLE);
            style &= ~WS_THICKFRAME;
            style &= ~WS_CAPTION;
            SetWindowLong(hWnd, GWL_STYLE, style);

            // Tool window (no taskbar entry) + no-activate (don't steal focus)
            int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            exStyle |= WS_EX_TOOLWINDOW;
            exStyle |= WS_EX_NOACTIVATE;
            SetWindowLong(hWnd, GWL_EXSTYLE, exStyle);

            // Ensure topmost
            SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        public void PositionOnTaskbar()
        {
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            double scaleFactor = GetDpiForWindow(hWnd) / 96.0;

            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var display = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
            var workArea = display.WorkArea;
            var outerBounds = display.OuterBounds;

            // Taskbar height = screen height minus work area height
            int taskbarHeight = outerBounds.Height - workArea.Height;
            if (taskbarHeight < 20) taskbarHeight = (int)(48 * scaleFactor); // fallback

            // Pill size in physical pixels
            int pillWidth = (int)(160 * scaleFactor);
            int pillHeight = (int)(34 * scaleFactor);

            _appWindow.Resize(new SizeInt32(pillWidth, pillHeight));

            // Position: left side of taskbar area, vertically centered in taskbar
            int margin = (int)(8 * scaleFactor);
            int x = workArea.X + margin;
            int y = workArea.Y + workArea.Height + (taskbarHeight - pillHeight) / 2;

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
            // Click opens the main flyout window
            App.MyFlyoutWindow?.ToggleFlyout();
        }

        public void ShowBar()
        {
            Activate();
            _appWindow.Show();

            // Re-apply topmost after show
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            SetWindowPos(hWnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        public void HideBar()
        {
            _appWindow.Hide();
        }
    }
}
