using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Windows.Storage;

namespace Task_Flyout.Services
{
    public sealed class MemoryDiagnosticsService
    {
        private const string SettingKey = "MemoryDiagnosticsEnabled";
        private const string EnvKey = "TASKFLYOUT_MEMORY_DIAGNOSTICS";
        private readonly TimeSpan _interval = TimeSpan.FromSeconds(60);
        private DispatcherTimer? _timer;
        private string? _logPath;

        public bool IsEnabled
        {
            get
            {
                if (ApplicationData.Current.LocalSettings.Values[SettingKey] is bool enabled)
                    return enabled;

                var env = Environment.GetEnvironmentVariable(EnvKey);
                return string.Equals(env, "1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase);
            }
            set => ApplicationData.Current.LocalSettings.Values[SettingKey] = value;
        }

        public void StartIfEnabled()
        {
            if (!IsEnabled || _timer != null) return;

            EnsureLogFile();
            WriteSnapshot();

            _timer = new DispatcherTimer { Interval = _interval };
            _timer.Tick += (_, _) => WriteSnapshot();
            _timer.Start();
        }

        public void Stop()
        {
            _timer?.Stop();
            _timer = null;
        }

        private void EnsureLogFile()
        {
            var logDir = AppDataPathHelper.EnsureDirectory(AppDataPathHelper.ResolveRoaming("Logs"));
            _logPath = AppDataPathHelper.ResolveRoaming("Logs", "memory-diagnostics.csv");
            if (File.Exists(_logPath)) return;

            File.WriteAllText(
                _logPath,
                "timestamp,working_set_mb,private_memory_mb,managed_heap_mb,gc0,gc1,gc2,handle_count,threads,current_page,has_webview2,main_window_created,flyout_created,weather_bar_created" + Environment.NewLine,
                Encoding.UTF8);
        }

        private void WriteSnapshot()
        {
            try
            {
                EnsureLogFile();
                if (string.IsNullOrWhiteSpace(_logPath)) return;

                using var process = Process.GetCurrentProcess();
                process.Refresh();

                var mainWindow = App.MyMainWindow;
                string currentPage = mainWindow?.GetDiagnosticsCurrentPageName() ?? "";
                bool hasWebView2 = mainWindow?.HasDiagnosticsWebView2() ?? false;

                var line = string.Join(
                    ",",
                    Escape(DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture)),
                    ToMb(process.WorkingSet64),
                    ToMb(process.PrivateMemorySize64),
                    ToMb(GC.GetTotalMemory(forceFullCollection: false)),
                    GC.CollectionCount(0).ToString(CultureInfo.InvariantCulture),
                    GC.CollectionCount(1).ToString(CultureInfo.InvariantCulture),
                    GC.CollectionCount(2).ToString(CultureInfo.InvariantCulture),
                    process.HandleCount.ToString(CultureInfo.InvariantCulture),
                    process.Threads.Count.ToString(CultureInfo.InvariantCulture),
                    Escape(currentPage),
                    Bool(hasWebView2),
                    Bool(mainWindow != null),
                    Bool(App.MyFlyoutWindow != null),
                    Bool(App.MyWeatherBar != null));

                File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Diagnostics must never affect normal app behavior.
            }
        }

        private static string ToMb(long bytes)
            => Math.Round(bytes / 1024d / 1024d, 1).ToString(CultureInfo.InvariantCulture);

        private static string Bool(bool value) => value ? "1" : "0";

        private static string Escape(string value)
            => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    }
}
