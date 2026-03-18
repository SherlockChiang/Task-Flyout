using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Task_Flyout.Views
{
    public sealed partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            this.InitializeComponent();

            // 确保页面加载完成后再去读取全局主题，防止 XamlRoot 为 null
            this.Loaded += SettingsPage_Loaded;
        }

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (this.XamlRoot?.Content is FrameworkElement rootElement)
            {
                ThemeComboBox.SelectedIndex = rootElement.RequestedTheme switch
                {
                    ElementTheme.Default => 0,
                    ElementTheme.Light => 1,
                    ElementTheme.Dark => 2,
                    _ => 0
                };
            }
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (this.XamlRoot?.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = ThemeComboBox.SelectedIndex switch
                {
                    1 => ElementTheme.Light,
                    2 => ElementTheme.Dark,
                    _ => ElementTheme.Default // 跟随系统
                };
            }
        }
    }
}