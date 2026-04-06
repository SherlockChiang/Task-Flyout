using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Linq;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel;
using Task_Flyout.Services;
using Task_Flyout.Models;

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

        private string GetSafeString(string key, string fallbackText)
        {
            if (_loader == null) return fallbackText;
            try
            {
                string safeKey = key.Replace(".", "/");
                string result = _loader.GetString(safeKey);
                return string.IsNullOrEmpty(result) ? fallbackText : result;
            }
            catch
            {
                return fallbackText;
            }
        }

        private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            var settings = ApplicationData.Current.LocalSettings;

            var theme = settings.Values["AppTheme"] as string;
            ThemeComboBox.SelectedIndex = theme switch { "Light" => 1, "Dark" => 2, _ => 0 };

            var lang = settings.Values["AppLang"] as string;
            LanguageComboBox.SelectedIndex = lang switch { "zh-Hans" => 1, "en-US" => 2, _ => 0 };

            BackgroundToggle.IsOn = settings.Values["RunInBackground"] as bool? ?? true;
            NotifyToggle.IsOn = settings.Values["NotifyEnabled"] as bool? ?? true;

            // 👉 这里已经支持多语言了！只需在英文 resw 中添加键名 TextMinutes，值为 Minutes 即可。
            string minuteStr = GetSafeString("TextMinutes", "分钟");

            NotifyTimeComboBox.Items.Clear();
            foreach (int m in new[] { 5, 10, 15, 30, 60 })
                NotifyTimeComboBox.Items.Add(new ComboBoxItem { Content = $"{m} {minuteStr}", Tag = m.ToString() });

            SyncIntervalComboBox.Items.Clear();
            foreach (int m in new[] { 5, 15, 30, 60 })
                SyncIntervalComboBox.Items.Add(new ComboBoxItem { Content = $"{m} {minuteStr}", Tag = m.ToString() });

            int notifyMin = settings.Values["NotifyMinutes"] as int? ?? 15;
            SelectComboByTag(NotifyTimeComboBox, notifyMin.ToString());

            int syncMin = settings.Values["SyncIntervalMinutes"] as int? ?? 15;
            SelectComboByTag(SyncIntervalComboBox, syncMin.ToString());

            try
            {
                StartupTask startupTask = await StartupTask.GetAsync("TaskFlyoutStartupId");
                StartupToggle.IsOn = startupTask.State == StartupTaskState.Enabled;
            }
            catch { }

            var weatherService = (App.Current as App)?.WeatherService;
            if (weatherService != null)
            {
                WeatherToggle.IsOn = weatherService.IsEnabled;
                WeatherCityTextBox.Text = weatherService.City;
            }

            BuildColorPaletteUI();

            _isInitializing = false;
        }

        private void SelectComboByTag(ComboBox combo, string tag)
        {
            var item = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag?.ToString() == tag);
            if (item != null) combo.SelectedItem = item;
            else combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
        }

        private void BuildColorPaletteUI()
        {
            ColorPalettePanel.Children.Clear();
            var mgr = (App.Current as App)?.SyncManager?.AccountManager;
            if (mgr == null || mgr.Accounts.Count == 0)
            {
                ColorPalettePanel.Children.Add(new TextBlock
                {
                    Text = GetSafeString("TextNoAccount", "尚未连接任何账户"),
                    FontSize = 13,
                    Opacity = 0.5
                });
                return;
            }

            foreach (var account in mgr.Accounts)
            {
                var headerPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Margin = new Thickness(0, 8, 0, 4)
                };
                var accentColor = (Windows.UI.Color)Application.Current.Resources["SystemAccentColor"];
                var accountIcon = new FontIcon
                {
                    Glyph = "\uE77B",
                    FontSize = 15,
                    Foreground = new SolidColorBrush(accentColor),
                    VerticalAlignment = VerticalAlignment.Center
                };
                headerPanel.Children.Add(accountIcon);
                var accountHeader = new TextBlock
                {
                    Text = account.ProviderName,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center
                };
                headerPanel.Children.Add(accountHeader);
                ColorPalettePanel.Children.Add(headerPanel);

                foreach (var cal in account.Calendars)
                {
                    var row = CreateColorRow(cal.Name, cal.ColorHex, selectedColor =>
                    {
                        cal.ColorHex = selectedColor;
                        mgr.Save();
                        BroadcastChange();
                    });
                    ColorPalettePanel.Children.Add(row);
                }

                var taskRow = CreateColorRow(
                    GetSafeString("MainWindow_ToggleTasks/Text", "待办任务"),
                    account.TaskColorHex,
                    selectedColor =>
                    {
                        account.TaskColorHex = selectedColor;
                        mgr.Save();
                        BroadcastChange();
                    });
                ColorPalettePanel.Children.Add(taskRow);
            }
        }

        private Grid CreateColorRow(string label, string currentHex, Action<string> onColorSelected)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var dot = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(ColorHelper.ParseHex(currentHex)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            dot.Tapped += (s, e) =>
            {
                ShowColorPickerFlyout(dot, currentHex, color =>
                {
                    dot.Background = new SolidColorBrush(ColorHelper.ParseHex(color));
                    currentHex = color;
                    onColorSelected(color);
                });
            };
            Grid.SetColumn(dot, 0);
            grid.Children.Add(dot);

            var text = new TextBlock
            {
                Text = label,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            return grid;
        }

        // 👉 核心：融合了“预设莫奈色”与“自定义RGB拾色器”的高级面板
        private void ShowColorPickerFlyout(FrameworkElement anchor, string currentColor, Action<string> onColorSelected)
        {
            var flyout = new Flyout();
            var panel = new StackPanel { Spacing = 12, Padding = new Thickness(8), MaxWidth = 340 };

            // 1. 预设颜色标题
            var presetHeader = new TextBlock
            {
                Text = GetSafeString("TextPresetColors", "预设颜色"),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 14
            };
            panel.Children.Add(presetHeader);

            // 2. 预设莫奈色网格
            var presetGrid = new GridView
            {
                SelectionMode = ListViewSelectionMode.None,
                IsItemClickEnabled = true,
                Margin = new Thickness(-4, 0, -4, 0)
            };
            presetGrid.ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                "<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
                "<ItemsWrapGrid MaximumRowsOrColumns='6' Orientation='Horizontal'/>" +
                "</ItemsPanelTemplate>");

            // 精选莫奈色系 Hex 代码
            string[] monetColors = {
                "#D5A5A1", "#B38B8D", "#C3D1C6", "#9CB4A3", "#758A7A", "#E6D4B8",
                "#D2B88F", "#B49665", "#BBD0D9", "#92A6B9", "#6A7B92", "#B19FB6"
            };

            // 提前声明 ColorPicker，以便点击预设时能联动修改它
            var colorPicker = new ColorPicker
            {
                Color = ColorHelper.ParseHex(currentColor),
                IsAlphaEnabled = false,
                IsColorChannelTextInputVisible = true,
                IsHexInputVisible = true,
                Margin = new Thickness(0, -8, 0, 0)
            };

            foreach (var hex in monetColors)
            {
                var swatch = new Border
                {
                    Width = 32,
                    Height = 32,
                    CornerRadius = new CornerRadius(16),
                    Background = new SolidColorBrush(ColorHelper.ParseHex(hex)),
                    Margin = new Thickness(4),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.LightGray)
                };

                // 点击预设色块时，让下方的高级调色盘跟着变
                swatch.Tapped += (s, args) =>
                {
                    colorPicker.Color = ColorHelper.ParseHex(hex);
                };
                presetGrid.Items.Add(swatch);
            }
            panel.Children.Add(presetGrid);

            // 3. 自定义颜色标题
            var customHeader = new TextBlock
            {
                Text = GetSafeString("TextCustomColor", "自定义颜色 (RGB/HEX)"),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 14,
                Margin = new Thickness(0, 8, 0, 0)
            };
            panel.Children.Add(customHeader);

            // 4. 高级调色盘
            panel.Children.Add(colorPicker);

            // 5. 底部按钮
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };

            var cancelBtn = new Button { Content = GetSafeString("CalendarDialog/CloseButtonText", "取消") };
            cancelBtn.Click += (s, e) => flyout.Hide();

            var applyBtn = new Button
            {
                Content = GetSafeString("TextConfirm", "确定"),
                Style = (Style)Application.Current.Resources["AccentButtonStyle"]
            };
            applyBtn.Click += (s, e) =>
            {
                var c = colorPicker.Color;
                string newHex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                onColorSelected(newHex);
                flyout.Hide();
            };

            btnPanel.Children.Add(cancelBtn);
            btnPanel.Children.Add(applyBtn);
            panel.Children.Add(btnPanel);

            flyout.Content = panel;
            flyout.ShowAt(anchor);
        }

        private void BroadcastChange()
        {
            BuildColorPaletteUI();

            if (App.MyMainWindow != null)
            {
                App.MyMainWindow.RefreshAccountList();
            }

            App.MyFlyoutWindow?.ReloadFilters();
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
                Title = GetSafeString("RestartRequired_Title", "需要重启"),
                Content = GetSafeString("RestartRequired_Content", "更改语言需要重启应用才能生效。"),
                PrimaryButtonText = GetSafeString("RestartRequired_Primary", "立即重启"),
                CloseButtonText = GetSafeString("RestartRequired_Close", "稍后"),
                XamlRoot = this.XamlRoot
            };

            var result = await restartDialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                Microsoft.Windows.AppLifecycle.AppInstance.Restart("");
            }
        }

        private void NotifyToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ApplicationData.Current.LocalSettings.Values["NotifyEnabled"] = NotifyToggle.IsOn;

            if (App.Current is App app && app.NotificationService != null)
            {
                if (NotifyToggle.IsOn)
                    app.NotificationService.StartPeriodicCheck();
                else
                    app.NotificationService.StopTimer();
            }
        }

        private void NotifyTimeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (NotifyTimeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag && int.TryParse(tag, out int minutes))
            {
                ApplicationData.Current.LocalSettings.Values["NotifyMinutes"] = minutes;
                if (App.Current is App app)
                    app.NotificationService?.SetReminderMinutes(minutes);
            }
        }

        private void SyncIntervalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (SyncIntervalComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag && int.TryParse(tag, out int minutes))
            {
                ApplicationData.Current.LocalSettings.Values["SyncIntervalMinutes"] = minutes;
                App.MyFlyoutWindow?.UpdateSyncInterval(minutes);
            }
        }

        private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            try
            {
                StartupTask startupTask = await StartupTask.GetAsync("TaskFlyoutStartupId");

                if (StartupToggle.IsOn)
                {
                    StartupTaskState state = await startupTask.RequestEnableAsync();

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

        private void WeatherToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            var weatherService = (App.Current as App)?.WeatherService;
            if (weatherService != null)
            {
                weatherService.IsEnabled = WeatherToggle.IsOn;
                _ = App.MyFlyoutWindow?.RefreshWeatherAsync(forceRefresh: true);
            }
        }

        private void WeatherCityTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            var weatherService = (App.Current as App)?.WeatherService;
            if (weatherService != null)
            {
                weatherService.City = WeatherCityTextBox.Text.Trim();
                _ = App.MyFlyoutWindow?.RefreshWeatherAsync(forceRefresh: true);
            }
        }
    }
}