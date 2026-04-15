using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Task_Flyout.Services;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Task_Flyout.Views
{
    public sealed partial class WeatherPage : Page
    {
        private bool _isInitializing = true;
        private WeatherService _weatherService;
        private ResourceLoader _loader;

        private readonly string[] _commonFonts = new[]
        {
            "Segoe UI Emoji", "Segoe UI Symbol", "Segoe Fluent Icons", "Segoe MDL2 Assets",
            "Noto Color Emoji", "Apple Color Emoji", "Twemoji Mozilla", "Font Awesome 5 Free",
            "Webdings", "Wingdings"
        };

        public WeatherPage()
        {
            this.InitializeComponent();
            _loader = new ResourceLoader();
            this.Loaded += WeatherPage_Loaded;
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
            catch { return fallbackText; }
        }

        private async void WeatherPage_Loaded(object sender, RoutedEventArgs e)
        {
            _weatherService = (App.Current as App)?.WeatherService;
            if (_weatherService == null) return;

            WeatherToggle.IsOn = _weatherService.IsEnabled;
            CitySearchBox.Text = _weatherService.City;

            // Weather bar toggle
            bool weatherBarEnabled = Windows.Storage.ApplicationData.Current.LocalSettings.Values["WeatherBarEnabled"] as bool? ?? false;
            WeatherBarToggle.IsOn = weatherBarEnabled;
            string lang = GetCurrentLang();
            WeatherBarDesc.Text = lang == "en"
                ? "Show a floating weather pill on the taskbar (replaces Win11 widget weather)"
                : "\u5728\u4EFB\u52A1\u680F\u4E0A\u663E\u793A\u6D6E\u52A8\u5929\u6C14\u6761\uFF08\u66FF\u4EE3 Win11 \u5C0F\u7EC4\u4EF6\u5929\u6C14\uFF09";

            // Source ComboBox
            string src = _weatherService.WeatherSource;
            var srcItem = SourceComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag?.ToString() == src);
            if (srcItem != null) SourceComboBox.SelectedItem = srcItem;
            else SourceComboBox.SelectedIndex = 0;
            UpdateSourceHint();

            RefreshIconSourceComboBox();

            // Flyout Fields header
            FlyoutFieldsHeader.Text = GetSafeString("WeatherPage_FlyoutFields", "\u6D6E\u7A97\u663E\u793A\u5B57\u6BB5");
            FlyoutFieldsDesc.Text = GetSafeString("WeatherPage_FlyoutFieldsDesc", "\u52FE\u9009\u5E0C\u671B\u5728\u7CFB\u7EDF\u6D6E\u7A97\u4E2D\u663E\u793A\u7684\u5929\u6C14\u4FE1\u606F");

            BuildFieldToggles();

            // Weather bar fields
            BarFieldsHeader.Text = lang == "en" ? "Taskbar Bar Content" : "\u4efb\u52a1\u680f\u5929\u6c14\u6761\u5185\u5bb9";
            BarFieldsDesc.Text = lang == "en"
                ? "Pick which fields appear in the taskbar weather bar"
                : "\u9009\u62e9\u4efb\u52a1\u680f\u5929\u6c14\u6761\u4e0a\u663e\u793a\u7684\u5b57\u6bb5";
            BuildBarFieldToggles();

            // Alerts
            AlertsHeader.Text = lang == "en" ? "Extreme Weather Alerts" : "\u6781\u7aef\u5929\u6c14\u9884\u8b66";
            AlertsDesc.Text = lang == "en"
                ? "Show alerts on the weather bar when extreme conditions are forecast"
                : "\u68c0\u6d4b\u5230\u6781\u7aef\u5929\u6c14\u65f6\u5728\u5929\u6c14\u6761\u4e0a\u663e\u793a\u9884\u8b66";
            AlertsToggle.Header = lang == "en" ? "Enable alerts" : "\u542f\u7528\u9884\u8b66";
            AlertsToggle.IsOn = _weatherService.BarAlertsEnabled;
            AlertHoursLabel.Text = lang == "en" ? "Look ahead (hours):" : "\u9884\u8b66\u63d0\u524d\u91cf\uff08\u5c0f\u65f6\uff09\uff1a";
            AlertHoursBox.Value = _weatherService.BarAlertHours;
            BuildAlertTypeToggles();

            _isInitializing = false;

            if (_weatherService.IsEnabled && !string.IsNullOrEmpty(_weatherService.City))
            {
                await LoadWeatherDataAsync();
            }
        }

        #region Current Weather Card

        private void UpdateCurrentWeatherCard(WeatherInfo info)
        {
            if (info == null)
            {
                CurrentWeatherCard.Visibility = Visibility.Collapsed;
                return;
            }

            CurrentIcon.Text = info.Icon;
            CurrentIcon.FontFamily = new FontFamily(info.IconFont);
            CurrentTemp.Text = info.Temperature;
            CurrentDesc.Text = info.Description ?? "";
            CurrentCity.Text = info.City ?? _weatherService?.City ?? "";

            // Build detail chips
            CurrentDetailsPanel.Children.Clear();
            string lang = GetCurrentLang();

            void AddChip(string glyph, string text)
            {
                if (string.IsNullOrEmpty(text)) return;
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                panel.Children.Add(new FontIcon
                {
                    Glyph = glyph,
                    FontSize = 12,
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    VerticalAlignment = VerticalAlignment.Center
                });
                panel.Children.Add(new TextBlock
                {
                    Text = text,
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    VerticalAlignment = VerticalAlignment.Center
                });
                CurrentDetailsPanel.Children.Add(panel);
            }

            AddChip("\uE9CA", info.FeelsLike);
            AddChip("\uE945", info.Humidity);
            AddChip("\uEBE7", info.WindSpeed);
            if (!string.IsNullOrEmpty(info.UVIndex))
                AddChip("\uE706", $"UV {info.UVIndex}");

            CurrentWeatherCard.Visibility = Visibility.Visible;
        }

        #endregion

        #region Field Toggles

        private void BuildFieldToggles()
        {
            var enabled = _weatherService.GetEnabledFields();
            string lang = GetCurrentLang();
            var items = new List<StackPanel>();

            foreach (var field in WeatherService.AllFlyoutFields)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var icon = new FontIcon
                {
                    Glyph = field.Glyph,
                    FontSize = 14,
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                };
                var toggle = new ToggleSwitch
                {
                    IsOn = enabled.Contains(field.Key),
                    OnContent = "",
                    OffContent = "",
                    MinWidth = 0,
                    Tag = field.Key
                };
                toggle.Toggled += FieldToggle_Toggled;

                var label = new TextBlock
                {
                    Text = lang == "en" ? field.EnLabel : field.ZhLabel,
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center
                };

                panel.Children.Add(icon);
                panel.Children.Add(label);
                panel.Children.Add(toggle);
                items.Add(panel);
            }

            FieldToggleRepeater.ItemsSource = items;
        }

        private void FieldToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _weatherService == null) return;
            if (sender is ToggleSwitch ts && ts.Tag is string key)
            {
                var fields = _weatherService.GetEnabledFields();
                if (ts.IsOn) fields.Add(key);
                else fields.Remove(key);
                _weatherService.SetEnabledFields(fields);

                _ = App.MyFlyoutWindow?.RefreshWeatherAsync(forceRefresh: false);
                App.RefreshWeatherBar();
            }
        }

        private void BuildBarFieldToggles()
        {
            var enabled = _weatherService.GetEnabledBarFields();
            string lang = GetCurrentLang();
            var items = new List<StackPanel>();

            foreach (var field in WeatherService.AllBarFields)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var toggle = new ToggleSwitch
                {
                    IsOn = enabled.Contains(field.Key),
                    OnContent = "",
                    OffContent = "",
                    MinWidth = 0,
                    Tag = field.Key
                };
                toggle.Toggled += BarFieldToggle_Toggled;

                var label = new TextBlock
                {
                    Text = lang == "en" ? field.EnLabel : field.ZhLabel,
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center
                };

                panel.Children.Add(label);
                panel.Children.Add(toggle);
                items.Add(panel);
            }

            BarFieldToggleRepeater.ItemsSource = items;
        }

        private void BarFieldToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _weatherService == null) return;
            if (sender is ToggleSwitch ts && ts.Tag is string key)
            {
                var fields = _weatherService.GetEnabledBarFields();
                if (ts.IsOn) fields.Add(key);
                else fields.Remove(key);
                _weatherService.SetEnabledBarFields(fields);
                App.RefreshWeatherBar();
            }
        }

        private void BuildAlertTypeToggles()
        {
            var enabled = _weatherService.GetEnabledAlertTypes();
            string lang = GetCurrentLang();
            var items = new List<StackPanel>();

            foreach (var entry in WeatherService.AllAlertTypes)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                var toggle = new ToggleSwitch
                {
                    IsOn = enabled.Contains(entry.Type),
                    OnContent = "",
                    OffContent = "",
                    MinWidth = 0,
                    Tag = entry.Type
                };
                toggle.Toggled += AlertTypeToggle_Toggled;

                var label = new TextBlock
                {
                    Text = lang == "en" ? entry.EnLabel : entry.ZhLabel,
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center
                };

                panel.Children.Add(label);
                panel.Children.Add(toggle);
                items.Add(panel);
            }

            AlertTypeRepeater.ItemsSource = items;
        }

        private void AlertTypeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _weatherService == null) return;
            if (sender is ToggleSwitch ts && ts.Tag is WeatherAlertType type)
            {
                var types = _weatherService.GetEnabledAlertTypes();
                if (ts.IsOn) types.Add(type);
                else types.Remove(type);
                _weatherService.SetEnabledAlertTypes(types);
                App.RefreshWeatherBar();
            }
        }

        private void AlertsToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _weatherService == null) return;
            _weatherService.BarAlertsEnabled = AlertsToggle.IsOn;
            App.RefreshWeatherBar();
        }

        private void AlertHoursBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (_isInitializing || _weatherService == null) return;
            if (!double.IsNaN(args.NewValue))
            {
                _weatherService.BarAlertHours = (int)args.NewValue;
                App.RefreshWeatherBar();
            }
        }

        private static string GetCurrentLang()
        {
            string appLang = Windows.Storage.ApplicationData.Current.LocalSettings.Values["AppLang"] as string;
            if (string.IsNullOrEmpty(appLang))
                appLang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return appLang.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh";
        }

        #endregion

        #region Source Selection

        private async void SourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _weatherService == null) return;
            if (SourceComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                _weatherService.WeatherSource = tag;
                UpdateSourceHint();
                await LoadWeatherDataAsync(forceRefresh: true);
            }
        }

        private void UpdateSourceHint()
        {
            string src = _weatherService?.WeatherSource ?? "OpenMeteo";
            if (src == "OpenMeteo")
                SourceHintText.Text = GetSafeString("WeatherPage_SourceHintOM",
                    "Open-Meteo: \u514D\u8D39\u3001\u65E0\u9700 API Key\u3001\u652F\u6301\u7A7A\u6C14\u8D28\u91CF\u4E0E\u82B1\u7C89\u6570\u636E\u3001\u771F\u6B63\u903C\u65F6\u9884\u62A5");
            else
                SourceHintText.Text = GetSafeString("WeatherPage_SourceHintWttr",
                    "wttr.in: \u514D\u8D39\u3001\u65E0\u9700 API Key\u3001\u4EC5\u63D0\u4F9B\u57FA\u7840\u5929\u6C14\u6570\u636E\u3001\u6BCF 3 \u5C0F\u65F6\u63D2\u503C\u9884\u62A5");
        }

        #endregion

        #region Icon Font

        private async void IconFontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _weatherService == null) return;
            if (IconFontComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string tag) return;

            if (tag.StartsWith("pack:", StringComparison.Ordinal))
            {
                // Imported third-party icon pack
                CustomFontBox.Visibility = Visibility.Collapsed;
                IconPackService.Instance.ActivePackId = tag.Substring("pack:".Length);
                BtnDeleteIconPack.IsEnabled = true;
                await LoadWeatherDataAsync(forceRefresh: true);
                return;
            }

            // Font-based source — ensure pack path is disabled
            IconPackService.Instance.ActivePackId = IconPackService.BuiltInEmojiId;
            BtnDeleteIconPack.IsEnabled = false;

            if (tag == "__custom__")
            {
                CustomFontBox.Visibility = Visibility.Visible;
                if (!string.IsNullOrWhiteSpace(CustomFontBox.Text))
                {
                    _weatherService.IconFontFamily = CustomFontBox.Text.Trim();
                    await LoadWeatherDataAsync(forceRefresh: true);
                }
            }
            else
            {
                CustomFontBox.Visibility = Visibility.Collapsed;
                _weatherService.IconFontFamily = tag;
                await LoadWeatherDataAsync(forceRefresh: true);
            }
        }

        private void CustomFontBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                string query = sender.Text.ToLower();
                var suggestions = _commonFonts.Where(f => f.ToLower().Contains(query)).ToList();
                sender.ItemsSource = suggestions.Count > 0 ? suggestions : null;
            }
        }

        private async void CustomFontBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is string fontName)
            {
                sender.Text = fontName;
                _weatherService.IconFontFamily = fontName;
                await LoadWeatherDataAsync(forceRefresh: true);
            }
        }

        private async void CustomFontBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            string fontName = args.ChosenSuggestion?.ToString() ?? sender.Text.Trim();
            if (!string.IsNullOrEmpty(fontName))
            {
                _weatherService.IconFontFamily = fontName;
                await LoadWeatherDataAsync(forceRefresh: true);
            }
        }

        #endregion

        #region Icon Source (unified: emoji / custom font / imported pack)

        private void RefreshIconSourceComboBox()
        {
            if (IconFontComboBox == null) return;
            string lang = GetCurrentLang();

            IconFontComboBox.SelectionChanged -= IconFontComboBox_SelectionChanged;
            IconFontComboBox.Items.Clear();

            // Built-in font presets. Labels mirror the old resw strings so zh/en stay consistent.
            IconFontComboBox.Items.Add(new ComboBoxItem
            {
                Content = lang == "en" ? "Windows 11 Style (emoji)" : "Windows 11 \u98CE\u683C\uFF08emoji\uFF09",
                Tag = "Segoe UI Emoji"
            });
            IconFontComboBox.Items.Add(new ComboBoxItem
            {
                Content = lang == "en" ? "Classical (Segoe Fluent Icons)" : "\u7ECF\u5178\u98CE\u683C\uFF08Segoe Fluent Icons\uFF09",
                Tag = "Segoe Fluent Icons, Segoe MDL2 Assets"
            });
            IconFontComboBox.Items.Add(new ComboBoxItem
            {
                Content = lang == "en" ? "Custom font..." : "\u81EA\u5B9A\u4E49\u5B57\u4F53\u2026",
                Tag = "__custom__"
            });

            // Imported icon packs
            var packs = IconPackService.Instance.ListInstalledPacks();
            foreach (var pack in packs)
            {
                IconFontComboBox.Items.Add(new ComboBoxItem
                {
                    Content = $"{pack.DisplayName} ({pack.IconCount})",
                    Tag = "pack:" + pack.Id
                });
            }

            // Resolve current selection: pack wins over font preset when active.
            ComboBoxItem? target = null;
            if (!IconPackService.Instance.IsBuiltInActive)
            {
                string packTag = "pack:" + IconPackService.Instance.ActivePackId;
                target = IconFontComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (i.Tag as string) == packTag);
            }
            if (target == null)
            {
                string fontStr = _weatherService?.IconFontFamily ?? "Segoe UI Emoji";
                target = IconFontComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (i.Tag as string) == fontStr);
                if (target == null)
                {
                    target = IconFontComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (i.Tag as string) == "__custom__");
                    CustomFontBox.Text = fontStr;
                    CustomFontBox.Visibility = Visibility.Visible;
                }
                else
                {
                    CustomFontBox.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                CustomFontBox.Visibility = Visibility.Collapsed;
            }
            IconFontComboBox.SelectedItem = target ?? IconFontComboBox.Items.FirstOrDefault();

            IconFontComboBox.SelectionChanged += IconFontComboBox_SelectionChanged;
            BtnDeleteIconPack.IsEnabled = !IconPackService.Instance.IsBuiltInActive;
            if (IconPackStatusText != null) IconPackStatusText.Text = "";
        }

        private async void BtnImportIconPack_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.Downloads;
                picker.FileTypeFilter.Add(".zip");

                var window = App.MyMainWindow;
                if (window != null)
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
                }

                var file = await picker.PickSingleFileAsync();
                if (file == null) return;

                string lang = GetCurrentLang();
                IconPackStatusText.Text = lang == "en" ? "Importing..." : "\u6B63\u5728\u5BFC\u5165\u2026";

                var id = await IconPackService.Instance.ImportFromZipAsync(file, System.IO.Path.GetFileNameWithoutExtension(file.Name));
                if (id == null)
                {
                    IconPackStatusText.Text = lang == "en"
                        ? "Failed: drawable_filter.xml not found in zip"
                        : "\u5931\u8D25\uFF1AZIP \u4E2D\u672A\u627E\u5230 drawable_filter.xml";
                    return;
                }

                IconPackService.Instance.ActivePackId = id;
                RefreshIconSourceComboBox();
                IconPackStatusText.Text = lang == "en" ? $"Imported: {id}" : $"\u5BFC\u5165\u5B8C\u6210\uFF1A{id}";
                if (_weatherService != null) await LoadWeatherDataAsync(forceRefresh: true);
            }
            catch (Exception ex)
            {
                IconPackStatusText.Text = "Error: " + ex.Message;
            }
        }

        private async void BtnDeleteIconPack_Click(object sender, RoutedEventArgs e)
        {
            var active = IconPackService.Instance.ActivePackId;
            if (active == IconPackService.BuiltInEmojiId) return;
            IconPackService.Instance.DeletePack(active);
            RefreshIconSourceComboBox();
            if (_weatherService != null) await LoadWeatherDataAsync(forceRefresh: true);
        }

        #endregion

        #region Toggle & City

        private async void WeatherToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _weatherService == null) return;
            _weatherService.IsEnabled = WeatherToggle.IsOn;

            if (WeatherToggle.IsOn && !string.IsNullOrEmpty(_weatherService.City))
                await LoadWeatherDataAsync();
            else
            {
                ForecastPanel.Visibility = Visibility.Collapsed;
                CurrentWeatherCard.Visibility = Visibility.Collapsed;
            }

            _ = App.MyFlyoutWindow?.RefreshWeatherAsync(forceRefresh: true);
            App.RefreshWeatherBar();

            // Auto-disable weather bar when weather is turned off
            if (!WeatherToggle.IsOn && WeatherBarToggle.IsOn)
            {
                WeatherBarToggle.IsOn = false;
            }
        }

        private void WeatherBarToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            App.ToggleWeatherBar(WeatherBarToggle.IsOn);
        }

        private async void CitySearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                string query = sender.Text.Trim();
                if (query.Length >= 2)
                {
                    var suggestions = await _weatherService.SearchCityAsync(query);
                    sender.ItemsSource = suggestions;
                }
                else
                {
                    sender.ItemsSource = null;
                }
            }
        }

        private async void CitySearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            if (args.SelectedItem is string selectedCity)
            {
                sender.Text = selectedCity;
                _weatherService.SelectCity(selectedCity);
                await LoadWeatherDataAsync(forceRefresh: true);
            }
        }

        private async void CitySearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            string city = args.ChosenSuggestion?.ToString() ?? args.QueryText;
            if (!string.IsNullOrWhiteSpace(city))
            {
                _weatherService.SelectCity(city);
                await LoadWeatherDataAsync(forceRefresh: true);
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadWeatherDataAsync(forceRefresh: true);
        }

        #endregion

        #region Hourly Selection

        private void HourlyListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HourlyListView.SelectedItem is HourlyWeather hw)
            {
                DetailIcon.Text = hw.Icon;
                DetailIcon.FontFamily = new FontFamily(hw.IconFont);
                DetailTemp.Text = hw.Temperature;
                DetailTime.Text = hw.Time;
                DetailDesc.Text = hw.Description ?? "";

                BuildDetailFields(hw);
                HourDetailPanel.Visibility = Visibility.Visible;
            }
            else
            {
                HourDetailPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void BuildDetailFields(HourlyWeather hw)
        {
            string lang = GetCurrentLang();
            var items = new List<StackPanel>();

            void AddField(string glyph, string label, string value)
            {
                if (string.IsNullOrEmpty(value)) return;
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new Thickness(4, 6, 4, 6) };
                panel.Children.Add(new FontIcon
                {
                    Glyph = glyph,
                    FontSize = 14,
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    VerticalAlignment = VerticalAlignment.Center
                });
                panel.Children.Add(new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = label, FontSize = 11, Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"] },
                        new TextBlock { Text = value, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }
                    }
                });
                items.Add(panel);
            }

            AddField("\uE9CA", lang == "en" ? "Feels like" : "\u4F53\u611F", hw.FeelsLike);
            AddField("\uE945", lang == "en" ? "Humidity" : "\u6E7F\u5EA6", hw.Humidity);
            AddField("\uEBE7", lang == "en" ? "Wind" : "\u98CE\u901F", $"{hw.WindSpeed} {hw.WindDirection}");
            AddField("\uE790", lang == "en" ? "Precip. prob." : "\u964D\u6C34\u6982\u7387", hw.PrecipProbability);
            AddField("\uE790", lang == "en" ? "Precipitation" : "\u964D\u6C34\u91CF", hw.Precipitation);
            AddField("\uE706", lang == "en" ? "UV Index" : "UV \u6307\u6570", hw.UVIndex);
            AddField("\uE7B3", lang == "en" ? "Visibility" : "\u80FD\u89C1\u5EA6", hw.Visibility);
            AddField("\uEC49", lang == "en" ? "Pressure" : "\u6C14\u538B", hw.Pressure);
            AddField("\uE9CA", lang == "en" ? "AQI" : "\u7A7A\u6C14\u8D28\u91CF", FormatAqi(hw.AirQuality, lang));
            AddField("\uE9CA", "PM2.5", hw.PM25);
            AddField("\uE9CA", "PM10", hw.PM10);
            AddField("\uE710", lang == "en" ? "Grass pollen" : "\u8349\u7C7B\u82B1\u7C89", hw.PollenGrass);
            AddField("\uE710", lang == "en" ? "Birch pollen" : "\u6866\u6811\u82B1\u7C89", hw.PollenBirch);
            AddField("\uE710", lang == "en" ? "Ragweed pollen" : "\u8C5A\u8349\u82B1\u7C89", hw.PollenRagweed);

            DetailFieldsRepeater.ItemsSource = items;
        }

        private static string FormatAqi(string aqi, string lang)
        {
            if (string.IsNullOrEmpty(aqi)) return null;
            if (!double.TryParse(aqi, out var val)) return aqi;
            string level;
            if (lang == "en")
                level = val <= 50 ? "Good" : val <= 100 ? "Moderate" : val <= 150 ? "Unhealthy (Sensitive)" : val <= 200 ? "Unhealthy" : "Hazardous";
            else
                level = val <= 50 ? "\u4F18" : val <= 100 ? "\u826F" : val <= 150 ? "\u8F7B\u5EA6\u6C61\u67D3" : val <= 200 ? "\u4E2D\u5EA6\u6C61\u67D3" : "\u91CD\u5EA6\u6C61\u67D3";
            return $"{aqi} ({level})";
        }

        #endregion

        #region Load Data

        private async Task LoadWeatherDataAsync(bool forceRefresh = false)
        {
            if (_weatherService == null || string.IsNullOrWhiteSpace(_weatherService.City)) return;

            LoadingRing.IsActive = true;
            ForecastPanel.Visibility = Visibility.Collapsed;

            var info = await _weatherService.GetWeatherAsync(forceRefresh);
            _ = App.MyFlyoutWindow?.RefreshWeatherAsync(forceRefresh);
            App.RefreshWeatherBar();

            // Update current weather card
            UpdateCurrentWeatherCard(info);

            if (info != null && info.HourlyForecast.Count > 0)
            {
                HourlyListView.ItemsSource = info.HourlyForecast;
                ForecastPanel.Visibility = Visibility.Visible;
                HourDetailPanel.Visibility = Visibility.Collapsed;

                // Select current hour
                int nowHour = DateTime.Now.Hour;
                int idx = 0;
                for (int i = 0; i < info.HourlyForecast.Count; i++)
                {
                    if (info.HourlyForecast[i].Hour == nowHour) { idx = i; break; }
                }
                HourlyListView.SelectedIndex = idx;

                // Scroll to selected
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (idx < info.HourlyForecast.Count)
                        HourlyListView.ScrollIntoView(info.HourlyForecast[idx], ScrollIntoViewAlignment.Leading);
                });
            }

            LoadingRing.IsActive = false;
        }

        #endregion
    }
}
