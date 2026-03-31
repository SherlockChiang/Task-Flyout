using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel; // 👉 新增：引入应用模型命名空间

namespace Task_Flyout.Views
{
    public sealed partial class SettingsPage : Page
    {
        private ResourceLoader _loader;
        private bool _isInitializing = true;

        public SettingsPage()
        {
            this.InitializeComponent();
            _loader = new ResourceLoader();
            this.Loaded += SettingsPage_Loaded;
        }

        private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            var theme = ApplicationData.Current.LocalSettings.Values["AppTheme"] as string;
            ThemeComboBox.SelectedIndex = theme switch { "Light" => 1, "Dark" => 2, _ => 0 };

            var lang = ApplicationData.Current.LocalSettings.Values["AppLang"] as string;
            LanguageComboBox.SelectedIndex = lang switch { "zh-Hans" => 1, "en-US" => 2, _ => 0 };

            BackgroundToggle.IsOn = ApplicationData.Current.LocalSettings.Values["RunInBackground"] as bool? ?? true;

            // 👉 修改：使用现代化 API 读取自启状态
            try
            {
                StartupTask startupTask = await StartupTask.GetAsync("TaskFlyoutStartupId");
                StartupToggle.IsOn = startupTask.State == StartupTaskState.Enabled;
            }
            catch
            {
                // 如果在未打包(Unpackaged)模式下调试，这个 API 会报错，这里吞掉异常防止闪退
            }

            _isInitializing = false;
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            var selectedTheme = ElementTheme.Default;
            string themeStr = "Default";

            if (ThemeComboBox.SelectedIndex == 1) { selectedTheme = ElementTheme.Light; themeStr = "Light"; }
            else if (ThemeComboBox.SelectedIndex == 2) { selectedTheme = ElementTheme.Dark; themeStr = "Dark"; }

            ApplicationData.Current.LocalSettings.Values["AppTheme"] = themeStr;

            if (this.XamlRoot?.Content is FrameworkElement rootElement)
                rootElement.RequestedTheme = selectedTheme;

            if (App.MyFlyoutWindow?.Content is FrameworkElement flyoutRoot)
                flyoutRoot.RequestedTheme = selectedTheme;
        }

        private async void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!this.IsLoaded || _isInitializing) return;

            if (LanguageComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                string langCode = item.Tag.ToString();
                ApplicationData.Current.LocalSettings.Values["AppLang"] = langCode;
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = langCode;
            }
            else
            {
                ApplicationData.Current.LocalSettings.Values["AppLang"] = "";
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "";
            }

            ContentDialog restartDialog = new ContentDialog
            {
                Title = _loader.GetString("RestartRequired_Title"),
                Content = _loader.GetString("RestartRequired_Content"),
                PrimaryButtonText = _loader.GetString("RestartRequired_Primary"),
                CloseButtonText = _loader.GetString("RestartRequired_Close"),
                XamlRoot = this.XamlRoot
            };

            var result = await restartDialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                Microsoft.Windows.AppLifecycle.AppInstance.Restart("");
            }
        }

        // 👉 修改：使用现代化 API 设置开机自启
        private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            try
            {
                StartupTask startupTask = await StartupTask.GetAsync("TaskFlyoutStartupId");

                if (StartupToggle.IsOn)
                {
                    // 向系统请求自启权限
                    StartupTaskState state = await startupTask.RequestEnableAsync();

                    // 如果用户在系统设置里禁用了该应用的自启，或者请求被拒绝，则把开关强行拨回去
                    if (state != StartupTaskState.Enabled)
                    {
                        _isInitializing = true;
                        StartupToggle.IsOn = false;
                        _isInitializing = false;
                    }
                }
                else
                {
                    startupTask.Disable();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"设置开机自启失败: {ex.Message}");
            }
        }

        private void BackgroundToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ApplicationData.Current.LocalSettings.Values["RunInBackground"] = BackgroundToggle.IsOn;
        }
    }
}