using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Task_Flyout.Services;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.Devices.Geolocation;

namespace Task_Flyout.Views
{
    public sealed partial class WeatherPage : Page
    {
        private bool _isInitializing = true;
        private WeatherService _weatherService = null!;
        private ResourceLoader _loader;
        private WeatherInfo? _currentWeatherInfo;

        private readonly string[] _commonFonts = new[]
        {
            "Segoe UI Emoji", "Segoe UI Symbol", "Segoe Fluent Icons", "Segoe MDL2 Assets",
            "Noto Color Emoji", "Apple Color Emoji", "Twemoji Mozilla", "Font Awesome 5 Free",
            "Webdings", "Wingdings"
        };

        public WeatherPage()
        {
            this.InitializeComponent();
            this.Language = Windows.Globalization.ApplicationLanguages.Languages[0];
            _loader = new ResourceLoader();
            this.Loaded += WeatherPage_Loaded;
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
            catch { return fallbackText; }
        }

        private async void WeatherPage_Loaded(object sender, RoutedEventArgs e)
        {
            if ((App.Current as App)?.WeatherService is not WeatherService weatherService) return;
            _weatherService = weatherService;

            WeatherToggle.IsOn = _weatherService.IsEnabled;
            CitySearchBox.Text = _weatherService.City;

            // Weather bar toggle
            bool weatherBarEnabled = Windows.Storage.ApplicationData.Current.LocalSettings.Values["WeatherBarEnabled"] as bool? ?? false;
            WeatherBarToggle.IsOn = weatherBarEnabled;
            WeatherBarDesc.Text = GetSafeString("WeatherPage_WeatherBarDesc", "Show a floating weather bar on the taskbar.");

            // Source ComboBox
            string src = _weatherService.WeatherSource;
            var srcItem = SourceComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag?.ToString() == src);
            if (srcItem != null) SourceComboBox.SelectedItem = srcItem;
            else SourceComboBox.SelectedIndex = 0;
            UpdateSourceHint();

            RefreshIconSourceComboBox();

            DailyForecastTitle.Text = GetSafeString("WeatherPage_DailyForecastTitle", "7-day forecast");

            // Flyout Fields header
            FlyoutFieldsHeader.Text = GetSafeString("WeatherPage_FlyoutFields", "Flyout fields");
            FlyoutFieldsDesc.Text = GetSafeString("WeatherPage_FlyoutFieldsDesc", "Choose weather details shown in the system flyout.");

            BuildFieldToggles();

            // Weather bar fields
            BarFieldsHeader.Text = GetSafeString("WeatherPage_BarFieldsHeader", "Taskbar weather bar content");
            BarFieldsDesc.Text = GetSafeString("WeatherPage_BarFieldsDesc", "Choose fields shown on the taskbar weather bar.");
            BuildBarFieldToggles();

            // Alerts
            AlertsHeader.Text = GetSafeString("WeatherPage_AlertsHeader", "Extreme weather alerts");
            AlertsDesc.Text = GetSafeString("WeatherPage_AlertsDesc", "Show alerts on the weather bar when severe conditions are detected.");
            AlertsToggle.Header = GetSafeString("WeatherPage_AlertsToggleHeader", "Enable alerts");
            AlertsToggle.IsOn = _weatherService.BarAlertsEnabled;
            AlertHoursLabel.Text = GetSafeString("WeatherPage_AlertHoursLabel", "Alert lead time (hours):");
            AlertHoursBox.Value = _weatherService.BarAlertHours;
            BuildAlertTypeToggles();

            _isInitializing = false;

            if (_weatherService.IsEnabled && !string.IsNullOrEmpty(_weatherService.City))
            {
                await LoadWeatherDataAsync();
            }
        }

        #region Current Weather Card

        private void UpdateCurrentWeatherCard(WeatherInfo? info)
        {
            if (info == null)
            {
                CurrentWeatherCard.Visibility = Visibility.Collapsed;
                return;
            }

            CurrentIcon.Text = info.Icon;
            CurrentIcon.FontFamily = new FontFamily(info.IconFont);
            ApplyIconLayers(info.IconLayerUris, CurrentIcon,
                CurrentIconImage0, CurrentIconImage1, CurrentIconImage2, CurrentIconImage3);
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

        #region Daily Forecast

        private void UpdateDailyForecast(WeatherInfo info)
        {
            _currentWeatherInfo = info;

            if (info?.DailyForecast == null || info.DailyForecast.Count == 0)
            {
                DailyForecastPanel.Visibility = Visibility.Collapsed;
                DailyForecastListView.ItemsSource = null;
                return;
            }

            DailyForecastListView.ItemsSource = info.DailyForecast.Take(7).ToList();
            DailyForecastPanel.Visibility = Visibility.Visible;
        }

        #endregion

        #region Field Toggles

        private void BuildFieldToggles()
        {
            var enabled = _weatherService.GetEnabledFields();
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
                    Text = GetSafeString(field.ResourceKey, field.EnLabel),
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
                    Text = GetSafeString(field.ResourceKey, field.EnLabel),
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
                    Text = GetSafeString(entry.ResourceKey, entry.EnLabel),
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
            string? appLang = Windows.Storage.ApplicationData.Current.LocalSettings.Values["AppLang"] as string;
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
                    "Open-Meteo: Free, no API key, supports air quality and pollen data, true hourly forecast.");
            else
                SourceHintText.Text = GetSafeString("WeatherPage_SourceHintWttr",
                    "wttr.in: Free, no API key, basic weather data only, interpolated 3-hour forecast.");
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

        private static void ApplyIconLayers(string[]? layers, TextBlock emojiBlock, params Image[] images)
        {
            bool useBitmap = layers != null && layers.Length > 0;
            emojiBlock.Visibility = useBitmap ? Visibility.Collapsed : Visibility.Visible;
            for (int i = 0; i < images.Length; i++)
            {
                if (useBitmap && i < layers!.Length && !string.IsNullOrEmpty(layers[i]))
                {
                    try
                    {
                        images[i].Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(layers[i]))
                        {
                            DecodePixelWidth = 96
                        };
                        images[i].Visibility = Visibility.Visible;
                    }
                    catch
                    {
                        images[i].Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    images[i].Visibility = Visibility.Collapsed;
                }
            }
        }

        #region Icon Source (unified: emoji / custom font / imported pack)

        private void RefreshIconSourceComboBox()
        {
            if (IconFontComboBox == null) return;
            string lang = GetCurrentLang();

            // Migrate the old "Fluent Icons" classical preset which never rendered (no matching glyphs).
            if (_weatherService != null && _weatherService.IconFontFamily == "Segoe Fluent Icons, Segoe MDL2 Assets")
                _weatherService.IconFontFamily = "Segoe UI Symbol";

            IconFontComboBox.SelectionChanged -= IconFontComboBox_SelectionChanged;
            IconFontComboBox.Items.Clear();

            // Built-in font presets. Labels mirror the old resw strings so zh/en stay consistent.
            IconFontComboBox.Items.Add(new ComboBoxItem
            {
                Content = GetSafeString("WeatherPage_IconFontEmoji", "Windows 11 style (emoji)"),
                Tag = "Segoe UI Emoji"
            });
            IconFontComboBox.Items.Add(new ComboBoxItem
            {
                Content = GetSafeString("WeatherPage_IconFontSymbol", "Classic style (Segoe UI Symbol)"),
                Tag = "Segoe UI Symbol"
            });
            IconFontComboBox.Items.Add(new ComboBoxItem
            {
                Content = GetSafeString("WeatherPage_IconFontCustom", "Custom font..."),
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

                IconPackStatusText.Text = GetSafeString("WeatherPage_IconPackImporting", "Importing...");

                var id = await IconPackService.Instance.ImportFromZipAsync(file, System.IO.Path.GetFileNameWithoutExtension(file.Name));
                if (id == null)
                {
                    IconPackStatusText.Text = GetSafeString("WeatherPage_IconPackFailed", "Failed: drawable_filter.xml was not found in the ZIP.");
                    return;
                }

                IconPackService.Instance.ActivePackId = id;
                RefreshIconSourceComboBox();
                IconPackStatusText.Text = string.Format(GetSafeString("WeatherPage_IconPackImported", "Imported: {0}"), id);
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

        private async void UseCurrentLocationButton_Click(object sender, RoutedEventArgs e)
        {
            UseCurrentLocationButton.IsEnabled = false;
            LocationStatusText.Visibility = Visibility.Collapsed;
            try
            {
                var access = await Geolocator.RequestAccessAsync();
                if (access != GeolocationAccessStatus.Allowed)
                {
                    ShowLocationStatus(_loader.GetStringOrDefault("WeatherLocationDenied")
                        ?? "Location is off. Turn it on in Windows Settings › Privacy & security › Location.");
                    return;
                }

                var geolocator = new Geolocator { DesiredAccuracyInMeters = 2000 };
                var position = await geolocator.GetGeopositionAsync();
                var point = position.Coordinate.Point.Position;

                string label = _loader.GetStringOrDefault("WeatherCurrentLocation") ?? "Current location";
                _weatherService.SetCoordinates(point.Latitude, point.Longitude, label);
                CitySearchBox.Text = label;
                await LoadWeatherDataAsync(forceRefresh: true);
            }
            catch (Exception ex)
            {
                ShowLocationStatus(_loader.GetStringOrDefault("WeatherLocationFailed")
                    ?? "Could not get your current location. Please try again.");
                System.Diagnostics.Debug.WriteLine($"Use current location failed: {ex.Message}");
            }
            finally
            {
                UseCurrentLocationButton.IsEnabled = true;
            }
        }

        private void ShowLocationStatus(string message)
        {
            LocationStatusText.Text = message;
            LocationStatusText.Visibility = Visibility.Visible;
        }

        #endregion

        #region Hourly Selection

        private void HourlyListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HourlyListView.SelectedItem is HourlyWeather hw)
            {
                DetailIcon.Text = hw.Icon;
                DetailIcon.FontFamily = new FontFamily(hw.IconFont);
                ApplyIconLayers(hw.IconLayerUris, DetailIcon,
                    DetailIconImage0, DetailIconImage1, DetailIconImage2, DetailIconImage3);
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
            var items = new List<StackPanel>();

            void AddField(string glyph, string label, string? value)
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

            AddField("\uE9CA", GetSafeString("WeatherPage_FeelsLike", "Feels like"), hw.FeelsLike);
            AddField("\uE945", GetSafeString("WeatherPage_Humidity", "Humidity"), hw.Humidity);
            AddField("\uEBE7", GetSafeString("WeatherPage_Wind", "Wind"), $"{hw.WindSpeed} {hw.WindDirection}");
            AddField("\uE790", GetSafeString("WeatherPage_PrecipProb", "Precipitation probability"), hw.PrecipProbability);
            AddField("\uE790", GetSafeString("WeatherPage_Precipitation", "Precipitation"), hw.Precipitation);
            AddField("\uE706", GetSafeString("WeatherPage_UVIndex", "UV index"), hw.UVIndex);
            AddField("\uE7B3", GetSafeString("WeatherPage_Visibility", "Visibility"), hw.Visibility);
            AddField("\uEC49", GetSafeString("WeatherPage_Pressure", "Pressure"), hw.Pressure);
            AddField("\uE9CA", GetSafeString("WeatherPage_AQI", "Air quality"), FormatAqi(hw.AirQuality));
            AddField("\uE9CA", "PM2.5", hw.PM25);
            AddField("\uE9CA", "PM10", hw.PM10);
            AddField("\uE710", GetSafeString("WeatherPage_GrassPollen", "Grass pollen"), hw.PollenGrass);
            AddField("\uE710", GetSafeString("WeatherPage_BirchPollen", "Birch pollen"), hw.PollenBirch);
            AddField("\uE710", GetSafeString("WeatherPage_RagweedPollen", "Ragweed pollen"), hw.PollenRagweed);

            DetailFieldsRepeater.ItemsSource = items;
        }

        private static string? FormatAqi(string? aqi)
        {
            if (string.IsNullOrEmpty(aqi)) return null;
            if (!double.TryParse(aqi, out var val)) return aqi;
            var loader = new ResourceLoader();
            string key = val <= 50 ? "WeatherPage_AQIGood" : val <= 100 ? "WeatherPage_AQIModerate" : val <= 150 ? "WeatherPage_AQIUnhealthySensitive" : val <= 200 ? "WeatherPage_AQIUnhealthy" : "WeatherPage_AQIHazardous";
            string defaultVal = val <= 50 ? "Good" : val <= 100 ? "Moderate" : val <= 150 ? "Unhealthy for sensitive groups" : val <= 200 ? "Unhealthy" : "Hazardous";
            var level = loader.GetStringOrDefault(key, defaultVal);
            
            return $"{aqi} ({level})";
        }

        #endregion

        #region Load Data

        private async Task LoadWeatherDataAsync(bool forceRefresh = false)
        {
            if (_weatherService == null || string.IsNullOrWhiteSpace(_weatherService.City)) return;

            LoadingRing.IsActive = true;
            ForecastPanel.Visibility = Visibility.Collapsed;
            DailyForecastPanel.Visibility = Visibility.Collapsed;

            var info = await _weatherService.GetWeatherAsync(forceRefresh);
            _ = App.MyFlyoutWindow?.RefreshWeatherAsync(forceRefresh);
            App.RefreshWeatherBar();

            // Update current weather card
            if (info == null)
            {
                UpdateCurrentWeatherCard(null);
                return;
            }

            UpdateCurrentWeatherCard(info);
            UpdateDailyForecast(info);

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
