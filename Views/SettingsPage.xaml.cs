using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Microsoft.Windows.ApplicationModel.Resources;
using System;

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

        private void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            var theme = ApplicationData.Current.LocalSettings.Values["AppTheme"] as string;
            ThemeComboBox.SelectedIndex = theme switch { "Light" => 1, "Dark" => 2, _ => 0 };

            var lang = ApplicationData.Current.LocalSettings.Values["AppLang"] as string;
            LanguageComboBox.SelectedIndex = lang switch { "zh-Hans" => 1, "en-US" => 2, _ => 0 };
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
            if (!this.IsLoaded) return; 

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
                CloseButtonText = _loader.GetString("RestartRequired_Close"),
                XamlRoot = this.XamlRoot
            };

            await restartDialog.ShowAsync();
        }
    }
}