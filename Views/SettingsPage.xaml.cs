using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Automation;
using Windows.Storage;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        private bool _isClearingWebViewCache;
        private bool _isClearingRssData;
        private bool _isClearingWeatherData;
        private bool? _usesMasonryLayout;
        private bool _isColorPickerDialogOpen;

        public SettingsPage()
        {
            this.Language = Windows.Globalization.ApplicationLanguages.Languages[0];
            this.InitializeComponent();
            _loader = new ResourceLoader();
            this.Loaded += SettingsPage_Loaded;
        }

        private string GetSafeString(string key, string fallbackText)
        {
            if (_loader == null) return fallbackText;
            try
            {
                var result = _loader.GetStringOrDefault(key);
                if (string.IsNullOrEmpty(result) && key.Contains('.'))
                    result = _loader.GetStringOrDefault(key.Replace(".", "/"));
                return string.IsNullOrEmpty(result) ? fallbackText : result;
            }
            catch
            {
                return fallbackText;
            }
        }

        private void SettingsScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            double padding = ResponsiveLayoutPolicy.GetPagePadding(e.NewSize.Width, e.NewSize.Height);
            double sectionSpacing = ResponsiveLayoutPolicy.GetPageSectionSpacing(
                e.NewSize.Width,
                e.NewSize.Height);
            SettingsScrollViewer.Padding = new Thickness(padding);
            SettingsContent.Spacing = sectionSpacing;
            SettingsSingleColumn.Spacing = sectionSpacing;
            SettingsMasonryGrid.ColumnSpacing = sectionSpacing;
            SettingsLeftColumn.Spacing = sectionSpacing;
            SettingsRightColumn.Spacing = sectionSpacing;
            ApplySettingsCardLayout(
                ResponsiveLayoutPolicy.ShouldUseSettingsMasonry(e.NewSize.Width));
        }

        private void ApplySettingsCardLayout(bool useMasonry)
        {
            if (_usesMasonryLayout == useMasonry)
                return;

            if (useMasonry)
            {
                SettingsMasonryGrid.Visibility = Visibility.Visible;

                MoveSettingsSection(SettingsLeftColumn, AppearanceSettingsSection);
                MoveSettingsSection(SettingsLeftColumn, MailSyncSettingsSection);
                MoveSettingsSection(SettingsLeftColumn, WeatherDiagnosticsSettingsSection);
                MoveSettingsSection(SettingsLeftColumn, AboutSettingsSection);

                MoveSettingsSection(SettingsRightColumn, ColorPaletteSettingsSection);
                MoveSettingsSection(SettingsRightColumn, ClockSettingsSection);
                MoveSettingsSection(SettingsRightColumn, NotificationSettingsSection);
                MoveSettingsSection(SettingsRightColumn, SystemSettingsSection);

                SettingsSingleColumn.Visibility = Visibility.Collapsed;
            }
            else
            {
                SettingsSingleColumn.Visibility = Visibility.Visible;

                MoveSettingsSection(SettingsSingleColumn, AppearanceSettingsSection);
                MoveSettingsSection(SettingsSingleColumn, ColorPaletteSettingsSection);
                MoveSettingsSection(SettingsSingleColumn, ClockSettingsSection);
                MoveSettingsSection(SettingsSingleColumn, NotificationSettingsSection);
                MoveSettingsSection(SettingsSingleColumn, MailSyncSettingsSection);
                MoveSettingsSection(SettingsSingleColumn, SystemSettingsSection);
                MoveSettingsSection(SettingsSingleColumn, WeatherDiagnosticsSettingsSection);
                MoveSettingsSection(SettingsSingleColumn, AboutSettingsSection);

                SettingsMasonryGrid.Visibility = Visibility.Collapsed;
            }

            _usesMasonryLayout = useMasonry;
        }

        private void MoveSettingsSection(StackPanel target, UIElement section)
        {
            SettingsSingleColumn.Children.Remove(section);
            SettingsLeftColumn.Children.Remove(section);
            SettingsRightColumn.Children.Remove(section);
            target.Children.Add(section);
        }

        private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
        {
            var settings = ApplicationData.Current.LocalSettings;

            var theme = settings.Values["AppTheme"] as string;
            ThemeComboBox.SelectedIndex = theme switch { "Light" => 1, "Dark" => 2, _ => 0 };

            var lang = settings.Values["AppLang"] as string;
            LanguageComboBox.SelectedIndex = lang switch
            {
                "zh" or "zh-CN" or "zh-Hans" => 1,
                "zh-TW" or "zh-Hant" or "zh-CHT" or "zh-HK" or "zh-MO" => 2,
                "en" or "en-US" => 3,
                _ => 0
            };

            BackgroundToggle.IsOn = settings.Values["RunInBackground"] as bool? ?? true;
            EfficiencyModeToggle.IsOn = settings.Values["EfficiencyModeEnabled"] as bool? ?? true;
            FlyoutPrewarmToggle.IsOn = settings.Values["FlyoutPrewarmEnabled"] as bool? ?? false;
            NotifyToggle.IsOn = settings.Values["NotifyEnabled"] as bool? ?? true;
            HideNotificationContentToggle.IsOn = settings.Values["HideNotificationContent"] as bool? ?? true;
            MailPollingToggle.IsOn = settings.Values["MailPollingEnabled"] as bool? ?? true;
            AutoMarkMailAsReadToggle.IsOn = settings.Values["AutoMarkMailAsRead"] as bool? ?? true;
            BlockRemoteImagesToggle.IsOn = settings.Values["BlockRemoteImagesByDefault"] as bool? ?? true;
            AllowRssRemoteResourcesToggle.IsOn = settings.Values["AllowRssRemoteResources"] as bool? ?? false;
            ShowSecondsToggle.IsOn = settings.Values["ShowSeconds"] as bool? ?? false;
            AboutVersionText.Text = GetVersionText();
            UpdateWeatherBarDiagnostics();
            await UpdateWebViewCacheStatusAsync();

            // 👉 这里已经支持多语言了！只需在英文 resw 中添加键名 TextMinutes，值为 Minutes 即可。
            string minuteStr = GetSafeString("TextMinutes", "minutes");

            NotifyTimeComboBox.Items.Clear();
            foreach (int m in new[] { 5, 10, 15, 30, 60 })
                NotifyTimeComboBox.Items.Add(new ComboBoxItem { Content = $"{m} {minuteStr}", Tag = m.ToString() });

            SyncIntervalComboBox.Items.Clear();
            foreach (int m in new[] { 5, 15, 30, 60 })
                SyncIntervalComboBox.Items.Add(new ComboBoxItem { Content = $"{m} {minuteStr}", Tag = m.ToString() });

            MailPollingIntervalComboBox.Items.Clear();
            foreach (int m in new[] { 1, 5, 10, 15, 30, 60 })
                MailPollingIntervalComboBox.Items.Add(new ComboBoxItem { Content = $"{m} {minuteStr}", Tag = m.ToString() });

            int notifyMin = settings.Values["NotifyMinutes"] as int? ?? 15;
            SelectComboByTag(NotifyTimeComboBox, notifyMin.ToString());

            int syncMin = settings.Values["SyncIntervalMinutes"] as int? ?? 15;
            SelectComboByTag(SyncIntervalComboBox, syncMin.ToString());

            int mailPollingMin = settings.Values["MailPollingIntervalMinutes"] as int? ?? 15;
            SelectComboByTag(MailPollingIntervalComboBox, mailPollingMin.ToString());

            try
            {
                StartupTask startupTask = await StartupTask.GetAsync("TaskFlyoutStartupId");
                StartupToggle.IsOn = startupTask.State == StartupTaskState.Enabled;
            }
            catch { }

            BuildColorPaletteUI();

            _isInitializing = false;
            UpdateDependentControlStates();
        }

        private static string GetVersionText()
        {
            var loader = new ResourceLoader();
            var versionPrefix = loader.GetStringOrDefault("TextVersion") ?? "Version";
            try
            {
                var version = Package.Current.Id.Version;
                return $"{versionPrefix} {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
            }
            catch
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                return version == null
                    ? loader.GetStringOrDefault("TextVersionUnknown") ?? "Version unknown"
                    : $"{versionPrefix} {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
            }
        }

        private void UpdateWeatherBarDiagnostics()
        {
            var diagnostics = App.GetWeatherBarDiagnostics();
            var unavailable = _loader.GetStringOrDefault("TextUnavailable") ?? "Unavailable";
            WeatherBarDiagnosticsText.Text = string.Format(
                _loader.GetStringOrDefault("SettingsPage_WeatherBarDiagnosticsFormat")
                    ?? "Taskbar class: {0}\nWidgets bridge source: {1}\nMonitor: {2}\nDPI: {3}\nTaskbar: {4}\nWeather bar: {5}\nFallback: {6}",
                diagnostics.TaskbarClass,
                diagnostics.WidgetsBridgeSource,
                diagnostics.MonitorRect,
                diagnostics.Dpi == 0 ? unavailable : diagnostics.Dpi,
                diagnostics.TaskbarRect,
                diagnostics.BarRect,
                diagnostics.FallbackReason);
        }

        private async void ReattachTaskbarButton_Click(object sender, RoutedEventArgs e)
        {
            App.ReattachWeatherBar();
            await System.Threading.Tasks.Task.Delay(150);
            UpdateWeatherBarDiagnostics();
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
                    Text = GetSafeString("TextNoAccount", "No accounts connected yet"),
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

                if (SyncProviderCapabilityPolicy.ForProvider(account.ProviderName).SupportsTasks)
                {
                    var taskRow = CreateColorRow(
                        GetSafeString("MainWindow_ToggleTasks/Text", "Tasks"),
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
        }

        private Grid CreateColorRow(string label, string currentHex, Action<string> onColorSelected)
        {
            var grid = new Grid
            {
                MinHeight = 40,
                Margin = new Thickness(0, 2, 0, 2),
                ColumnSpacing = 8
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var text = new TextBlock
            {
                Text = label,
                FontSize = 13,
                MaxWidth = 240,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = GetThemeBrush("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray)
            };
            Grid.SetColumn(text, 0);
            grid.Children.Add(text);

            var colorDot = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(ColorHelper.ParseHex(currentHex)),
                BorderBrush = GetThemeBrush("TaskFlyoutSectionBorderBrush", Microsoft.UI.Colors.Gray),
                BorderThickness = new Thickness(1)
            };
            var colorButton = new Button
            {
                Width = 36,
                MinWidth = 36,
                Height = 36,
                Padding = new Thickness(8),
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Content = colorDot
            };
            AutomationProperties.SetName(colorButton, $"{label}, {currentHex}");
            ToolTipService.SetToolTip(colorButton, $"{label}: {currentHex}");
            colorButton.Click += (s, e) =>
            {
                ShowColorPickerDialog(colorButton, currentHex, color =>
                {
                    colorDot.Background = new SolidColorBrush(ColorHelper.ParseHex(color));
                    currentHex = color;
                    AutomationProperties.SetName(colorButton, $"{label}, {color}");
                    ToolTipService.SetToolTip(colorButton, $"{label}: {color}");
                    onColorSelected(color);
                });
            };
            Grid.SetColumn(colorButton, 1);
            grid.Children.Add(colorButton);
            grid.SizeChanged += (_, args) =>
            {
                double availableLabelWidth = args.NewSize.Width - colorButton.Width - grid.ColumnSpacing;
                text.MaxWidth = Math.Clamp(availableLabelWidth, 48, 280);
            };

            return grid;
        }

        private async void ShowColorPickerDialog(
            FrameworkElement anchor,
            string currentColor,
            Action<string> onColorSelected)
        {
            var xamlRoot = anchor.XamlRoot;
            if (_isColorPickerDialogOpen || xamlRoot == null)
                return;

            _isColorPickerDialogOpen = true;
            double availableWidth = xamlRoot.Size.Width;
            double availableHeight = xamlRoot.Size.Height;
            var metrics = ResponsiveLayoutPolicy.GetColorPickerPopupMetrics(
                availableWidth,
                availableHeight);
            var panel = new StackPanel
            {
                MaxWidth = metrics.ContentWidth,
                Padding = new Thickness(12),
                Spacing = 14
            };

            var monetColors = ColorHelper.MonetPalette;
            var parsedCurrentColor = ColorHelper.ParseHex(currentColor);
            var previewSwatch = new Border
            {
                Width = 48,
                Height = 48,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(parsedCurrentColor),
                BorderBrush = GetThemeBrush("TaskFlyoutSectionBorderBrush", Microsoft.UI.Colors.Gray),
                BorderThickness = new Thickness(1)
            };
            var previewHexText = new TextBlock
            {
                Text = currentColor.ToUpperInvariant(),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            var previewCard = new Grid
            {
                ColumnSpacing = 12,
                Padding = new Thickness(10),
                Background = GetThemeBrush("TaskFlyoutSectionBackgroundBrush", Microsoft.UI.Colors.Transparent)
            };
            previewCard.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            previewCard.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            previewCard.Children.Add(previewSwatch);
            var previewText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
            previewText.Children.Add(new TextBlock
            {
                Text = "HEX",
                FontSize = 11,
                Foreground = GetThemeBrush("TextFillColorSecondaryBrush", Microsoft.UI.Colors.Gray)
            });
            previewText.Children.Add(previewHexText);
            Grid.SetColumn(previewText, 1);
            previewCard.Children.Add(previewText);
            panel.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderBrush = GetThemeBrush("TaskFlyoutSectionBorderBrush", Microsoft.UI.Colors.Gray),
                BorderThickness = new Thickness(1),
                Child = previewCard
            });

            panel.Children.Add(new TextBlock
            {
                Text = GetSafeString("TextPresetColors", "Preset Colors"),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 13
            });

            int paletteRows = (int)Math.Ceiling(monetColors.Length / (double)metrics.PaletteColumns);
            var presetGrid = new Grid
            {
                RowSpacing = 6,
                ColumnSpacing = 6,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            for (int column = 0; column < metrics.PaletteColumns; column++)
                presetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int row = 0; row < paletteRows; row++)
                presetGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var colorPicker = new ColorPicker
            {
                Color = parsedCurrentColor,
                IsAlphaEnabled = false,
                IsColorChannelTextInputVisible = true,
                IsHexInputVisible = true,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var swatchIndicators = new Dictionary<string, FontIcon>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < monetColors.Length; index++)
            {
                string hex = monetColors[index];
                var swatchColor = ColorHelper.ParseHex(hex);
                var indicator = new FontIcon
                {
                    Glyph = "\uE73E",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(ColorHelper.ShouldUseWhiteText(hex)
                        ? Microsoft.UI.Colors.White
                        : Microsoft.UI.Colors.Black),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = Visibility.Collapsed
                };
                var swatch = new Button
                {
                    Width = 42,
                    Height = 42,
                    Padding = new Thickness(5),
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                    BorderThickness = new Thickness(0),
                    CornerRadius = new CornerRadius(6),
                    Content = new Grid
                    {
                        Children =
                        {
                            new Border
                            {
                                Width = 32,
                                Height = 32,
                                CornerRadius = new CornerRadius(6),
                                Background = new SolidColorBrush(swatchColor),
                                BorderThickness = new Thickness(1),
                                BorderBrush = GetThemeBrush("TaskFlyoutSectionBorderBrush", Microsoft.UI.Colors.Gray)
                            },
                            indicator
                        }
                    }
                };
                AutomationProperties.SetName(swatch, $"{GetSafeString("TextPresetColors", "Preset Colors")}: {hex}");
                ToolTipService.SetToolTip(swatch, hex);
                swatch.Click += (_, _) => colorPicker.Color = swatchColor;
                swatchIndicators[hex] = indicator;
                Grid.SetColumn(swatch, index % metrics.PaletteColumns);
                Grid.SetRow(swatch, index / metrics.PaletteColumns);
                presetGrid.Children.Add(swatch);
            }
            panel.Children.Add(presetGrid);

            void UpdateColorPreview(Windows.UI.Color color)
            {
                string selectedHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
                previewSwatch.Background = new SolidColorBrush(color);
                previewHexText.Text = selectedHex;
                foreach (var pair in swatchIndicators)
                    pair.Value.Visibility = string.Equals(pair.Key, selectedHex, StringComparison.OrdinalIgnoreCase)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }

            colorPicker.ColorChanged += (_, args) => UpdateColorPreview(args.NewColor);
            UpdateColorPreview(parsedCurrentColor);
            panel.Children.Add(new TextBlock
            {
                Text = GetSafeString("TextCustomColor", "Custom Color (RGB/HEX)"),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                FontSize = 13
            });
            panel.Children.Add(colorPicker);

            var dialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                Title = GetSafeString("TextPickColor", "Pick a color"),
                PrimaryButtonText = GetSafeString("TextConfirm", "Confirm"),
                CloseButtonText = GetSafeString("CalendarDialog.CloseButtonText", "Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                Content = new ScrollViewer
                {
                    MaxHeight = metrics.MaxContentHeight,
                    HorizontalScrollMode = ScrollMode.Disabled,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollMode = ScrollMode.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = panel
                }
            };

            try
            {
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    var color = colorPicker.Color;
                    onColorSelected($"#{color.R:X2}{color.G:X2}{color.B:X2}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Color picker dialog failed: {ex.Message}");
            }
            finally
            {
                _isColorPickerDialogOpen = false;
            }
        }

        private static Brush GetThemeBrush(string resourceKey, Windows.UI.Color fallback)
        {
            if (Application.Current.Resources.TryGetValue(resourceKey, out var resource) &&
                resource is Brush brush)
                return brush;

            return new SolidColorBrush(fallback);
        }

        private void BroadcastChange()
        {
            BuildColorPaletteUI();

            App.MyMainWindow?.RefreshCalendarColors();
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

            App.ApplyConfiguredThemeToOpenWindows();
            App.MyWeatherBar?.ApplyWindowsTheme();
        }

        private async void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!this.IsLoaded || _isInitializing) return;

            if (LanguageComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                string langCode = item.Tag.ToString() ?? "en-US";
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
                Title = GetSafeString("RestartRequired_Title", "Restart Required"),
                Content = GetSafeString("RestartRequired_Content", "Please restart the app to fully apply the language changes."),
                PrimaryButtonText = GetSafeString("RestartRequired_Primary", "Restart Now"),
                CloseButtonText = GetSafeString("RestartRequired_Close", "Later"),
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
            UpdateDependentControlStates();

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

        private void MailPollingToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ApplicationData.Current.LocalSettings.Values["MailPollingEnabled"] = MailPollingToggle.IsOn;
            UpdateDependentControlStates();
            if (App.Current is App app)
                app.MailService.UpdateMailPollingSettings();
        }

        private void UpdateDependentControlStates()
        {
            NotifyTimeComboBox.IsEnabled = NotifyToggle.IsOn;
            MailPollingIntervalComboBox.IsEnabled = MailPollingToggle.IsOn;
        }

        private void MailPollingIntervalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (MailPollingIntervalComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag && int.TryParse(tag, out int minutes))
            {
                ApplicationData.Current.LocalSettings.Values["MailPollingIntervalMinutes"] = minutes;
                if (App.Current is App app)
                    app.MailService.UpdateMailPollingSettings();
            }
        }

        private void AutoMarkMailAsReadToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ApplicationData.Current.LocalSettings.Values["AutoMarkMailAsRead"] = AutoMarkMailAsReadToggle.IsOn;
        }

        private void HideNotificationContentToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ApplicationData.Current.LocalSettings.Values["HideNotificationContent"] = HideNotificationContentToggle.IsOn;
        }

        private void BlockRemoteImagesToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ApplicationData.Current.LocalSettings.Values["BlockRemoteImagesByDefault"] = BlockRemoteImagesToggle.IsOn;
        }

        private void AllowRssRemoteResourcesToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ApplicationData.Current.LocalSettings.Values["AllowRssRemoteResources"] = AllowRssRemoteResourcesToggle.IsOn;
        }

        private async void ClearWebViewCacheButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isClearingWebViewCache) return;

            _isClearingWebViewCache = true;
            ClearWebViewCacheButton.IsEnabled = false;
            WebViewCacheStatusText.Text = GetSafeString("TextCleaning", "Cleaning...");
            try
            {
                var freed = await WebView2RuntimeService.ClearCacheAsync();
                await UpdateWebViewCacheStatusAsync(freed);
            }
            catch
            {
                WebViewCacheStatusText.Text = GetSafeString("TextCleanFailed", "Clean failed");
            }
            finally
            {
                _isClearingWebViewCache = false;
                ClearWebViewCacheButton.IsEnabled = true;
            }
        }

        private async void ClearRssDataButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isClearingRssData) return;

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = GetSafeString("TextClearRssDataTitle", "Clear RSS local data?"),
                Content = GetSafeString("TextClearRssDataContent", "This removes cached RSS subscriptions, articles, and images from this device. Mail and RSS share an embedded browser profile, so its site data, history, and disk cache will also be cleared for both readers. This does not cancel subscriptions at the source or remove system backups."),
                PrimaryButtonText = GetSafeString("TextClear", "Clear"),
                CloseButtonText = GetSafeString("CalendarDialog.CloseButtonText", "Cancel"),
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            _isClearingRssData = true;
            ClearRssDataButton.IsEnabled = false;
            try
            {
                await RssService.ClearLocalDataAsync();
                WebViewCacheStatusText.Text = GetSafeString("TextRssDataCleared", "RSS local data cleared.");
            }
            catch
            {
                WebViewCacheStatusText.Text = GetSafeString("TextCleanFailed", "Clean failed");
            }
            finally
            {
                _isClearingRssData = false;
                ClearRssDataButton.IsEnabled = true;
            }
        }

        private async void ClearWeatherDataButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isClearingWeatherData || App.Current is not App app) return;

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = GetSafeString("TextClearWeatherDataTitle", "Clear weather and location data?"),
                Content = GetSafeString("TextClearWeatherDataContent", "This stops location tracking and removes the saved city, precise coordinates, and cached weather from this device."),
                PrimaryButtonText = GetSafeString("TextClear", "Clear"),
                CloseButtonText = GetSafeString("CalendarDialog.CloseButtonText", "Cancel"),
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

            _isClearingWeatherData = true;
            ClearWeatherDataButton.IsEnabled = false;
            WeatherDataStatusText.Text = GetSafeString("TextCleaning", "Cleaning...");
            try
            {
                await app.WeatherService.ClearWeatherAndLocationDataAsync();
                WeatherDataStatusText.Text = GetSafeString("TextWeatherDataCleared", "Weather and location data cleared.");
                _ = App.MyFlyoutWindow?.RefreshWeatherAsync(forceRefresh: true);
                App.RefreshWeatherBar();
            }
            catch
            {
                WeatherDataStatusText.Text = GetSafeString("TextCleanFailed", "Clean failed");
            }
            finally
            {
                _isClearingWeatherData = false;
                ClearWeatherDataButton.IsEnabled = true;
            }
        }

        // Surface a warning when we get within 80% of the auto-prune ceiling so the
        // user can clear pro-actively instead of paying the latency on next launch.
        private const long WebViewCacheWarnBytes = 240L * 1024 * 1024;

        private async System.Threading.Tasks.Task UpdateWebViewCacheStatusAsync(long? freedBytes = null)
        {
            var bytes = await WebView2RuntimeService.GetCacheSizeBytesAsync();
            var sizeText = FormatBytes(bytes);
            WebViewCacheStatusText.Text = freedBytes.HasValue
                ? $"{GetSafeString("TextCleaned", "Cleaned")} {FormatBytes(freedBytes.Value)} · {sizeText}"
                : sizeText;
            WebViewCacheWarningBar.IsOpen = bytes >= WebViewCacheWarnBytes;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024 * 1024)
                return $"{Math.Round(bytes / 1024d, 1)} KB";
            return $"{Math.Round(bytes / 1024d / 1024d, 1)} MB";
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
                System.Diagnostics.Debug.WriteLine($"Startup task failed: {ex.Message}");
            }
        }

        private void BackgroundToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ApplicationData.Current.LocalSettings.Values["RunInBackground"] = BackgroundToggle.IsOn;
        }

        private void EfficiencyModeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ApplicationData.Current.LocalSettings.Values["EfficiencyModeEnabled"] = EfficiencyModeToggle.IsOn;
            App.UpdateEfficiencyMode();
        }

        private void FlyoutPrewarmToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ApplicationData.Current.LocalSettings.Values["FlyoutPrewarmEnabled"] = FlyoutPrewarmToggle.IsOn;
        }

        private void ShowSecondsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            ApplicationData.Current.LocalSettings.Values["ShowSeconds"] = ShowSecondsToggle.IsOn;
            App.MyFlyoutWindow?.UpdateClockFormat();
        }

    }
}
