using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace Task_Flyout.Services
{
    public class WeatherInfo
    {
        public string Temperature { get; set; }
        public string Description { get; set; }
        public string Icon { get; set; }
        public string City { get; set; }
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

        public async Task<WeatherInfo> GetWeatherAsync(bool forceRefresh = false)
        {
            if (!IsEnabled || string.IsNullOrWhiteSpace(City))
                return null;

            if (!forceRefresh && _cachedWeather != null && (DateTime.Now - _lastFetchTime) < _cacheExpiry)
                return _cachedWeather;

            try
            {
                // 👉 核心修复 1：强制加上 &lang=zh 确保有中文描述
                string url = $"https://wttr.in/{Uri.EscapeDataString(City)}?format=j1&lang=zh";
                
                _httpClient.DefaultRequestHeaders.UserAgent.Clear();
                // 👉 核心修复 2：伪装成命令行工具，防止 wttr.in 拒绝响应 JSON
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("curl/7.68.0");

                var response = await _httpClient.GetStringAsync(url);
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                var current = root.GetProperty("current_condition")[0];
                string tempC = current.GetProperty("temp_C").GetString();
                string weatherCode = current.GetProperty("weatherCode").GetString();

                string desc = "";
                if (current.TryGetProperty("lang_zh", out var langZh) && langZh.GetArrayLength() > 0)
                    desc = langZh[0].GetProperty("value").GetString();
                else if (current.TryGetProperty("weatherDesc", out var wDesc) && wDesc.GetArrayLength() > 0)
                    desc = wDesc[0].GetProperty("value").GetString();

                _cachedWeather = new WeatherInfo
                {
                    Temperature = $"{tempC}°C",
                    Description = desc,
                    Icon = WeatherCodeToIcon(weatherCode),
                    City = City
                };
                _lastFetchTime = DateTime.Now;

                return _cachedWeather;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"获取天气失败: {ex.Message}");
                return _cachedWeather; // 报错则返回旧缓存
            }
        }

        private static string WeatherCodeToIcon(string code)
        {
            return code switch
            {
                "113" => "\uE9BD",  // Sunny - E9BD
                "116" => "\uE9BE",  // Partly cloudy
                "119" or "122" => "\uE9BF",  // Cloudy/Overcast
                "143" or "248" or "260" => "\uE9CB",  // Fog/Mist
                "176" or "263" or "266" or "293" or "296" => "\uE9C1",  // Light rain
                "299" or "302" or "305" or "308" or "356" or "359" => "\uE9C2",  // Rain/Heavy rain
                "179" or "227" or "323" or "326" or "329" or "332" or "335" or "338" or "368" or "371" => "\uE9C8",  // Snow
                "200" or "386" or "389" or "392" or "395" => "\uE9C6",  // Thunderstorm
                "182" or "185" or "281" or "284" or "311" or "314" or "317" or "350" or "362" or "365" or "374" or "377" => "\uE9C4",  // Sleet/Freezing
                _ => "\uE9BD"
            };
        }
    }
}