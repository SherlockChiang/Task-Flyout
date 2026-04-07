using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace Task_Flyout.Services
{
    public class HourlyWeather
    {
        public string Time { get; set; }
        public string Temperature { get; set; }
        public string Icon { get; set; }
        public string IconFont { get; set; }
    }

    public class WeatherInfo
    {
        public string Temperature { get; set; }
        public string Description { get; set; }
        public string Icon { get; set; }
        public string IconFont { get; set; }
        public string City { get; set; }
        public List<HourlyWeather> HourlyForecast { get; set; } = new();
    }

    public class WeatherService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private WeatherInfo _cachedWeather;
        private DateTime _lastFetchTime = DateTime.MinValue;
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(30);

        public bool IsEnabled
        {
            get => ApplicationData.Current.LocalSettings.Values["WeatherEnabled"] as bool? ?? false;
            set => ApplicationData.Current.LocalSettings.Values["WeatherEnabled"] = value;
        }

        public string City
        {
            get => ApplicationData.Current.LocalSettings.Values["WeatherCity"] as string ?? "";
            set => ApplicationData.Current.LocalSettings.Values["WeatherCity"] = value;
        }

        public string IconFontFamily
        {
            get => ApplicationData.Current.LocalSettings.Values["WeatherIconFont"] as string ?? "Segoe UI Emoji";
            set => ApplicationData.Current.LocalSettings.Values["WeatherIconFont"] = value;
        }
        public async Task<List<string>> SearchCityAsync(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<string>();
            try
            {
                string url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query)}&count=5&language=zh";
                _httpClient.DefaultRequestHeaders.UserAgent.Clear();
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("TaskFlyout/1.0");

                var response = await _httpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("results", out var results))
                {
                    var list = new List<string>();
                    foreach (var item in results.EnumerateArray())
                    {
                        string name = item.GetProperty("name").GetString();
                        string admin1 = item.TryGetProperty("admin1", out var a1) ? a1.GetString() : "";
                        string country = item.TryGetProperty("country", out var c) ? c.GetString() : "";
                        
                        string fullName = $"{name}, {admin1}, {country}".Replace(", ,", ",").TrimEnd(',', ' ');
                        list.Add(fullName);
                    }
                    return list;
                }
            }
            catch { }
            return new List<string>();
        }

        public async Task<WeatherInfo> GetWeatherAsync(bool forceRefresh = false)
        {
            if (!IsEnabled || string.IsNullOrWhiteSpace(City))
                return null;

            if (!forceRefresh && _cachedWeather != null && (DateTime.Now - _lastFetchTime) < _cacheExpiry)
                return _cachedWeather;

            try
            {
                // 👉 1. 动态获取当前设置的语言
                string appLang = ApplicationData.Current.LocalSettings.Values["AppLang"] as string;
                if (string.IsNullOrEmpty(appLang))
                    appLang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

                string wttrLang = appLang.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh";

                string searchCity = City.Split(',')[0].Trim();
                // 👉 2. 将语言参数动态拼接入 URL
                string url = $"https://wttr.in/{Uri.EscapeDataString(searchCity)}?format=j1&lang={wttrLang}";

                _httpClient.DefaultRequestHeaders.UserAgent.Clear();
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("curl/7.68.0");

                var response = await _httpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                var current = root.GetProperty("current_condition")[0];
                string tempC = current.GetProperty("temp_C").GetString();
                string weatherCode = current.GetProperty("weatherCode").GetString();

                // 👉 3. 根据语言动态提取对应的天气描述字段
                string desc = "";
                if (wttrLang == "zh" && current.TryGetProperty("lang_zh", out var langZh) && langZh.GetArrayLength() > 0)
                    desc = langZh[0].GetProperty("value").GetString();
                else if (current.TryGetProperty("weatherDesc", out var wDesc) && wDesc.GetArrayLength() > 0)
                    desc = wDesc[0].GetProperty("value").GetString();

                var weatherInfo = new WeatherInfo
                {
                    Temperature = $"{tempC}°C",
                    Description = desc,
                    Icon = WeatherCodeToIcon(weatherCode, IconFontFamily),
                    IconFont = IconFontFamily,
                    City = City
                };

                if (root.TryGetProperty("weather", out var weatherArray) && weatherArray.GetArrayLength() > 0)
                {
                    var today = weatherArray[0];
                    if (today.TryGetProperty("hourly", out var hourlyArray))
                    {
                        foreach (var hour in hourlyArray.EnumerateArray())
                        {
                            string timeRaw = hour.GetProperty("time").GetString();
                            string timeFormatted = timeRaw == "0" ? "00:00" : timeRaw.PadLeft(4, '0').Insert(2, ":");
                            string hourTemp = hour.GetProperty("tempC").GetString() + "°C";
                            string hourCode = hour.GetProperty("weatherCode").GetString();

                            weatherInfo.HourlyForecast.Add(new HourlyWeather
                            {
                                Time = timeFormatted,
                                Temperature = hourTemp,
                                Icon = WeatherCodeToIcon(hourCode, IconFontFamily),
                                IconFont = IconFontFamily
                            });
                        }
                    }
                }

                _cachedWeather = weatherInfo;
                _lastFetchTime = DateTime.Now;

                return _cachedWeather;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取天气失败: {ex.Message}");
                return _cachedWeather;
            }
        }

        private static string WeatherCodeToIcon(string code, string fontFamily)
        {
            bool isSymbol = fontFamily != null && fontFamily.Contains("Segoe UI Symbol");

            if (isSymbol)
            {
                // Basic Unicode weather symbols — render reliably in Segoe UI Symbol
                return code switch
                {
                    "113" => "\u2600",  // ☀ 晴天
                    "116" => "\u26C5",  // ⛅ 多云
                    "119" or "122" => "\u2601",  // ☁ 阴天
                    "143" or "248" or "260" => "\u2601",  // ☁ 雾
                    "176" or "263" or "266" or "293" or "296" => "\u2602",  // ☂ 阵雨
                    "299" or "302" or "305" or "308" or "356" or "359" => "\u2614",  // ☔ 大雨
                    "179" or "227" or "323" or "326" or "329" or "332" or "335" or "338" or "368" or "371" => "\u2744",  // ❄ 雪
                    "200" or "386" or "389" or "392" or "395" => "\u26A1",  // ⚡ 雷阵雨
                    "182" or "185" or "281" or "284" or "311" or "314" or "317" or "350" or "362" or "365" or "374" or "377" => "\u2744",  // ❄ 雨夹雪
                    _ => "\u2600"
                };
            }
            else
            {
                // Color emoji — works with Segoe UI Emoji / Noto Color Emoji / custom font
                return code switch
                {
                    "113" => "☀️",
                    "116" => "⛅",
                    "119" or "122" => "☁️",
                    "143" or "248" or "260" => "🌫️",
                    "176" or "263" or "266" or "293" or "296" => "🌦️",
                    "299" or "302" or "305" or "308" or "356" or "359" => "🌧️",
                    "179" or "227" or "323" or "326" or "329" or "332" or "335" or "338" or "368" or "371" => "❄️",
                    "200" or "386" or "389" or "392" or "395" => "⛈️",
                    "182" or "185" or "281" or "284" or "311" or "314" or "317" or "350" or "362" or "365" or "374" or "377" => "🌨️",
                    _ => "🌤️"
                };
            }
        }
    }
}