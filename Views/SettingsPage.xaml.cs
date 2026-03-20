using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace Task_Flyout.Views
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            this.InitializeComponent();
            this.Loaded += SettingsPage_Loaded;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            // 读取已保存的主题
            var theme = ApplicationData.Current.LocalSettings.Values["AppTheme"] as string;
            ThemeComboBox.SelectedIndex = theme switch { "Light" => 1, "Dark" => 2, _ => 0 };

            // 读取已保存的语言
            var lang = ApplicationData.Current.LocalSettings.Values["AppLang"] as string;
            LanguageComboBox.SelectedIndex = lang switch { "zh-Hans" => 1, "en-US" => 2, _ => 0 };
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedTheme = ElementTheme.Default;
            string themeStr = "Default";

            if (ThemeComboBox.SelectedIndex == 1) { selectedTheme = ElementTheme.Light; themeStr = "Light"; }
            else if (ThemeComboBox.SelectedIndex == 2) { selectedTheme = ElementTheme.Dark; themeStr = "Dark"; }

            // 1. 保存设置
            ApplicationData.Current.LocalSettings.Values["AppTheme"] = themeStr;

            // 2. 动态更新当前主窗口主题
            if (this.XamlRoot?.Content is FrameworkElement rootElement)
                rootElement.RequestedTheme = selectedTheme;

            // 3. 👉 核心修复：把 FlyoutWindowInstance 改成你 App.xaml.cs 里的真实名字 MyFlyoutWindow
            if (App.MyFlyoutWindow?.Content is FrameworkElement flyoutRoot)
                flyoutRoot.RequestedTheme = selectedTheme;
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LanguageComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                string langCode = item.Tag.ToString();
                ApplicationData.Current.LocalSettings.Values["AppLang"] = langCode;
                // WinUI 3 官方切换语言 API (必须重启应用生效)
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = langCode;
            }
            else
            {
                ApplicationData.Current.LocalSettings.Values["AppLang"] = "";
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "";
            }
        }
    }
}