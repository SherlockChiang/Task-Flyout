using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Task_Flyout.Services;

namespace Task_Flyout.Views
{
    public sealed partial class WeatherPage : Page
    {
        private bool _isInitializing = true;
        private WeatherService _weatherService;

        private readonly string[] _commonFonts = new[]
        {
            "Segoe UI Emoji", "Segoe UI Symbol", "Segoe Fluent Icons", "Segoe MDL2 Assets",
            "Noto Color Emoji", "Apple Color Emoji", "Twemoji Mozilla", "Font Awesome 5 Free",
            "Webdings", "Wingdings"
        };

        public WeatherPage()
        {
            this.InitializeComponent();
            this.Loaded += WeatherPage_Loaded;
        }

        private async void WeatherPage_Loaded(object sender, RoutedEventArgs e)
        {
            _weatherService = (App.Current as App)?.WeatherService;
            if (_weatherService == null) return;

            WeatherToggle.IsOn = _weatherService.IsEnabled;
            CitySearchBox.Text = _weatherService.City;

            string fontStr = _weatherService.IconFontFamily;
            var item = IconFontComboBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => i.Tag.ToString() == fontStr);

            if (item != null)
            {
                IconFontComboBox.SelectedItem = item;
            }
            else
            {
                var customItem = IconFontComboBox.Items.OfType<ComboBoxItem>()
                    .FirstOrDefault(i => i.Tag.ToString() == "__custom__");
                if (customItem != null) IconFontComboBox.SelectedItem = customItem;
                CustomFontBox.Text = fontStr;
                CustomFontBox.Visibility = Visibility.Visible;
            }

            _isInitializing = false;

            if (_weatherService.IsEnabled && !string.IsNullOrEmpty(_weatherService.City))
            {
                await LoadWeatherDataAsync();
            }
        }

        private async void IconFontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _weatherService == null) return;
            if (IconFontComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                string tag = item.Tag.ToString();
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

        private async void WeatherToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _weatherService == null) return;
            _weatherService.IsEnabled = WeatherToggle.IsOn;

            if (WeatherToggle.IsOn && !string.IsNullOrEmpty(_weatherService.City))
                await LoadWeatherDataAsync();
            else
                ForecastPanel.Visibility = Visibility.Collapsed;

            _ = App.MyFlyoutWindow?.RefreshWeatherAsync(forceRefresh: true);
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
                _weatherService.City = selectedCity;
                await LoadWeatherDataAsync();
            }
        }

        private async void CitySearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (args.ChosenSuggestion != null)
            {
                _weatherService.City = args.ChosenSuggestion.ToString();
            }
            else
            {
                _weatherService.City = args.QueryText;
            }
            await LoadWeatherDataAsync();
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadWeatherDataAsync(forceRefresh: true);
        }

        private void HourlyListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (HourlyListView.SelectedItem is HourlyWeather hw)
            {
                DetailIcon.Text = hw.Icon;
                DetailIcon.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(hw.IconFont);
                DetailTemp.Text = hw.Temperature;
                DetailTime.Text = hw.Time;
                DetailDesc.Text = hw.Description ?? "";
                DetailFeelsLike.Text = hw.FeelsLike ?? "";
                DetailHumidity.Text = hw.Humidity ?? "";
                DetailWind.Text = hw.WindSpeed ?? "";
                HourDetailPanel.Visibility = Visibility.Visible;
            }
            else
            {
                HourDetailPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async Task LoadWeatherDataAsync(bool forceRefresh = false)
        {
            if (_weatherService == null || string.IsNullOrWhiteSpace(_weatherService.City)) return;

            LoadingRing.IsActive = true;
            ForecastPanel.Visibility = Visibility.Collapsed;

            var info = await _weatherService.GetWeatherAsync(forceRefresh);
            _ = App.MyFlyoutWindow?.RefreshWeatherAsync(forceRefresh);

            if (info != null && info.HourlyForecast.Count > 0)
            {
                HourlyListView.ItemsSource = info.HourlyForecast;
                ForecastPanel.Visibility = Visibility.Visible;
                HourDetailPanel.Visibility = Visibility.Collapsed;
                HourlyListView.SelectedIndex = 0;
            }

            LoadingRing.IsActive = false;
        }
    }
}