using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;

namespace Task_Flyout.Services
{
    public class HourlyWeather
    {
        public int Hour { get; set; }
        public DateTime RawTime { get; set; }
        public string Time { get; set; }
        public string Temperature { get; set; }
        public string Icon { get; set; }
        public string IconFont { get; set; }
        public string Description { get; set; }
        public string FeelsLike { get; set; }
        public string Humidity { get; set; }
        public string WindSpeed { get; set; }
        public string WindDirection { get; set; }
        public string Precipitation { get; set; }
        public string PrecipProbability { get; set; }
        public string UVIndex { get; set; }
        public string Visibility { get; set; }
        public string Pressure { get; set; }
        public string AirQuality { get; set; }
        public string PM25 { get; set; }
        public string PM10 { get; set; }
        public string PollenGrass { get; set; }
        public string PollenBirch { get; set; }
        public string PollenRagweed { get; set; }

        // Raw numeric values used for alert detection
        public int WeatherCode { get; set; }
        public double TempValue { get; set; }
        public double WindSpeedValue { get; set; }
        public double PrecipProbValue { get; set; }
        public double PrecipValue { get; set; }

        // Icon pack stacked layers (index 0 = base drawable, 1..3 = overlay layers for rain intensity).
        // Empty array means the active pack is the built-in emoji or no drawable matched.
        public string[] IconLayerUris { get; set; } = Array.Empty<string>();

        // x:Bind helpers — let the hourly DataTemplate pick up layer paths without a converter.
        public string IconLayer0Uri => IconLayerUris.Length > 0 ? IconLayerUris[0] : "";
        public string IconLayer1Uri => IconLayerUris.Length > 1 ? IconLayerUris[1] : "";
        public string IconLayer2Uri => IconLayerUris.Length > 2 ? IconLayerUris[2] : "";
        public string IconLayer3Uri => IconLayerUris.Length > 3 ? IconLayerUris[3] : "";
        public bool HasIconBitmap => IconLayerUris.Length > 0;
    }

    public enum WeatherAlertType
    {
        HeavyRain,
        Rain,
        FreezingRain,
        HeavySnow,
        Snow,
        Thunderstorm,
        Fog,
        HighWind,
        ExtremeHeat,
        ExtremeCold
    }

    public class WeatherAlert
    {
        public WeatherAlertType Type { get; set; }
        public int HoursAhead { get; set; }
        public string Icon { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public class WeatherInfo
    {
        public string Temperature { get; set; }
        public string Description { get; set; }
        public string Icon { get; set; }
        public string IconFont { get; set; }
        public string IconBitmapUri { get; set; } // layer 0 convenience (same as IconLayerUris[0])
        public string[] IconLayerUris { get; set; } = Array.Empty<string>();
        public int RawWeatherCode { get; set; }
        public bool IsDayTime { get; set; }
        public string City { get; set; }
        public string Sunrise { get; set; }
        public string Sunset { get; set; }
        public string MoonPhase { get; set; }
        public string FeelsLike { get; set; }
        public string Humidity { get; set; }
        public string WindSpeed { get; set; }
        public string UVIndex { get; set; }
        public string Visibility { get; set; }
        public string Pressure { get; set; }
        public string AirQuality { get; set; }
        public string Pollen { get; set; }
        public List<HourlyWeather> HourlyForecast { get; set; } = new();
    }

    public class WeatherService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private WeatherInfo _cachedWeather;
        private DateTime _lastFetchTime = DateTime.MinValue;
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(30);
        private Dictionary<string, (double Lat, double Lon)> _lastSearchCoords = new();

        #region Settings Properties

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

        public double CityLat
        {
            get => ApplicationData.Current.LocalSettings.Values["WeatherCityLat"] as double? ?? 0;
            set => ApplicationData.Current.LocalSettings.Values["WeatherCityLat"] = value;
        }

        public double CityLon
        {
            get => ApplicationData.Current.LocalSettings.Values["WeatherCityLon"] as double? ?? 0;
            set => ApplicationData.Current.LocalSettings.Values["WeatherCityLon"] = value;
        }

        public string IconFontFamily
        {
            get => ApplicationData.Current.LocalSettings.Values["WeatherIconFont"] as string ?? "Segoe UI Emoji";
            set => ApplicationData.Current.LocalSettings.Values["WeatherIconFont"] = value;
        }

        public string WeatherSource
        {
            get => ApplicationData.Current.LocalSettings.Values["WeatherSource"] as string ?? "OpenMeteo";
            set => ApplicationData.Current.LocalSettings.Values["WeatherSource"] = value;
        }

        public string EnabledFlyoutFields
        {
            get => ApplicationData.Current.LocalSettings.Values["WeatherFlyoutFields"] as string ?? "temperature,description";
            set => ApplicationData.Current.LocalSettings.Values["WeatherFlyoutFields"] = value;
        }

        public string EnabledBarFields
        {
            get => ApplicationData.Current.LocalSettings.Values["WeatherBarFields"] as string ?? "icon,temperature,description";
            set => ApplicationData.Current.LocalSettings.Values["WeatherBarFields"] = value;
        }

        public bool BarAlertsEnabled
        {
            get => ApplicationData.Current.LocalSettings.Values["WeatherBarAlertsEnabled"] as bool? ?? true;
            set => ApplicationData.Current.LocalSettings.Values["WeatherBarAlertsEnabled"] = value;
        }

        public int BarAlertHours
        {
            get => ApplicationData.Current.LocalSettings.Values["WeatherBarAlertHours"] as int? ?? 3;
            set => ApplicationData.Current.LocalSettings.Values["WeatherBarAlertHours"] = value;
        }

        public string EnabledAlertTypes
        {
            get => ApplicationData.Current.LocalSettings.Values["WeatherAlertTypes"] as string
                   ?? "HeavyRain,Rain,FreezingRain,HeavySnow,Snow,Thunderstorm,Fog,HighWind,ExtremeHeat,ExtremeCold";
            set => ApplicationData.Current.LocalSettings.Values["WeatherAlertTypes"] = value;
        }

        #endregion

        public HashSet<string> GetEnabledBarFields()
        {
            return new HashSet<string>(
                (EnabledBarFields ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        public void SetEnabledBarFields(HashSet<string> fields)
        {
            EnabledBarFields = string.Join(",", fields);
        }

        public HashSet<WeatherAlertType> GetEnabledAlertTypes()
        {
            var set = new HashSet<WeatherAlertType>();
            foreach (var s in (EnabledAlertTypes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Enum.TryParse<WeatherAlertType>(s, out var t)) set.Add(t);
            }
            return set;
        }

        public void SetEnabledAlertTypes(HashSet<WeatherAlertType> types)
        {
            EnabledAlertTypes = string.Join(",", types);
        }

        /// <summary>
        /// Scan upcoming hours for extreme weather, return the most urgent alert (or null).
        /// </summary>
        public WeatherAlert DetectUpcomingAlert(WeatherInfo info)
        {
            if (info == null || info.HourlyForecast == null || info.HourlyForecast.Count == 0)
                return null;

            int lookAhead = Math.Max(1, BarAlertHours);
            var enabled = GetEnabledAlertTypes();
            if (enabled.Count == 0) return null;

            string lang = GetCurrentLanguage();
            var now = DateTime.Now;
            var upcoming = info.HourlyForecast
                .Where(h => (h.RawTime - now).TotalHours >= 0 && (h.RawTime - now).TotalHours <= lookAhead)
                .OrderBy(h => h.RawTime)
                .ToList();

            if (upcoming.Count == 0) return null;

            // Priority order: most severe first
            var priority = new[]
            {
                WeatherAlertType.Thunderstorm,
                WeatherAlertType.FreezingRain,
                WeatherAlertType.HeavyRain,
                WeatherAlertType.HeavySnow,
                WeatherAlertType.Snow,
                WeatherAlertType.Rain,
                WeatherAlertType.HighWind,
                WeatherAlertType.ExtremeHeat,
                WeatherAlertType.ExtremeCold,
                WeatherAlertType.Fog,
            };

            foreach (var type in priority)
            {
                if (!enabled.Contains(type)) continue;
                var hit = upcoming.FirstOrDefault(h => MatchAlert(h, type));
                if (hit != null)
                {
                    int hoursAhead = Math.Max(0, (int)Math.Round((hit.RawTime - now).TotalHours));
                    return new WeatherAlert
                    {
                        Type = type,
                        HoursAhead = hoursAhead,
                        Icon = GetAlertIcon(type),
                        Message = BuildAlertMessage(type, hoursAhead, lang)
                    };
                }
            }
            return null;
        }

        private static bool MatchAlert(HourlyWeather h, WeatherAlertType type)
        {
            int code = h.WeatherCode;
            switch (type)
            {
                case WeatherAlertType.Thunderstorm:
                    return code == 95 || code == 96 || code == 99;
                case WeatherAlertType.FreezingRain:
                    return code == 56 || code == 57 || code == 66 || code == 67;
                case WeatherAlertType.HeavyRain:
                    return (code == 65 || code == 82) && h.PrecipProbValue >= 50;
                case WeatherAlertType.Rain:
                    return (code == 61 || code == 63 || code == 80 || code == 81) && h.PrecipProbValue >= 50;
                case WeatherAlertType.HeavySnow:
                    return code == 75 || code == 86;
                case WeatherAlertType.Snow:
                    return code == 71 || code == 73 || code == 77 || code == 85;
                case WeatherAlertType.Fog:
                    return code == 45 || code == 48;
                case WeatherAlertType.HighWind:
                    return h.WindSpeedValue >= 40; // km/h
                case WeatherAlertType.ExtremeHeat:
                    return h.TempValue >= 35;
                case WeatherAlertType.ExtremeCold:
                    return h.TempValue <= -10;
            }
            return false;
        }

        private static string GetAlertIcon(WeatherAlertType type) => type switch
        {
            WeatherAlertType.Thunderstorm => "\u26C8\uFE0F",
            WeatherAlertType.FreezingRain => "\U0001F327\uFE0F",
            WeatherAlertType.HeavyRain => "\U0001F327\uFE0F",
            WeatherAlertType.Rain => "\U0001F326\uFE0F",
            WeatherAlertType.HeavySnow => "\u2744\uFE0F",
            WeatherAlertType.Snow => "\U0001F328\uFE0F",
            WeatherAlertType.Fog => "\U0001F32B\uFE0F",
            WeatherAlertType.HighWind => "\U0001F4A8",
            WeatherAlertType.ExtremeHeat => "\U0001F525",
            WeatherAlertType.ExtremeCold => "\U0001F976",
            _ => "\u26A0\uFE0F"
        };

        private static string BuildAlertMessage(WeatherAlertType type, int hoursAhead, string lang)
        {
            bool zh = lang == "zh";
            string when = hoursAhead <= 0
                ? (zh ? "\u5373\u5c06" : "now")
                : (zh ? $"{hoursAhead}h\u540e" : $"in {hoursAhead}h");
            string label = type switch
            {
                WeatherAlertType.Thunderstorm => zh ? "\u96f7\u9635\u96e8" : "Thunderstorm",
                WeatherAlertType.FreezingRain => zh ? "\u51bb\u96e8" : "Freezing rain",
                WeatherAlertType.HeavyRain => zh ? "\u5927\u96e8" : "Heavy rain",
                WeatherAlertType.Rain => zh ? "\u964d\u96e8" : "Rain",
                WeatherAlertType.HeavySnow => zh ? "\u5927\u96ea" : "Heavy snow",
                WeatherAlertType.Snow => zh ? "\u964d\u96ea" : "Snow",
                WeatherAlertType.Fog => zh ? "\u8d77\u96fe" : "Fog",
                WeatherAlertType.HighWind => zh ? "\u5927\u98ce" : "Strong wind",
                WeatherAlertType.ExtremeHeat => zh ? "\u9ad8\u6e29" : "Extreme heat",
                WeatherAlertType.ExtremeCold => zh ? "\u4e25\u5bd2" : "Extreme cold",
                _ => ""
            };
            return zh ? $"{when}{label}" : $"{label} {when}";
        }

        public static readonly (string Key, string ZhLabel, string EnLabel)[] AllBarFields = new[]
        {
            ("icon",         "\u56fe\u6807",     "Icon"),
            ("temperature",  "\u6e29\u5ea6",     "Temperature"),
            ("description",  "\u63cf\u8ff0",     "Description"),
            ("feelslike",    "\u4f53\u611f",     "Feels like"),
            ("humidity",     "\u6e7f\u5ea6",     "Humidity"),
            ("wind",         "\u98ce\u901f",     "Wind"),
        };

        public static readonly (WeatherAlertType Type, string ZhLabel, string EnLabel)[] AllAlertTypes = new[]
        {
            (WeatherAlertType.Thunderstorm, "\u96f7\u9635\u96e8",   "Thunderstorm"),
            (WeatherAlertType.FreezingRain, "\u51bb\u96e8",         "Freezing rain"),
            (WeatherAlertType.HeavyRain,    "\u5927\u96e8",         "Heavy rain"),
            (WeatherAlertType.Rain,         "\u964d\u96e8",         "Rain"),
            (WeatherAlertType.HeavySnow,    "\u5927\u96ea",         "Heavy snow"),
            (WeatherAlertType.Snow,         "\u964d\u96ea",         "Snow"),
            (WeatherAlertType.Fog,          "\u8d77\u96fe",         "Fog"),
            (WeatherAlertType.HighWind,     "\u5927\u98ce",         "Strong wind"),
            (WeatherAlertType.ExtremeHeat,  "\u9ad8\u6e29",         "Extreme heat"),
            (WeatherAlertType.ExtremeCold,  "\u4e25\u5bd2",         "Extreme cold"),
        };

        public HashSet<string> GetEnabledFields()
        {
            return new HashSet<string>(
                (EnabledFlyoutFields ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        public void SetEnabledFields(HashSet<string> fields)
        {
            EnabledFlyoutFields = string.Join(",", fields);
        }

        public void SelectCity(string cityName)
        {
            City = cityName;
            if (_lastSearchCoords.TryGetValue(cityName, out var coords))
            {
                CityLat = coords.Lat;
                CityLon = coords.Lon;
            }
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
                    _lastSearchCoords.Clear();
                    foreach (var item in results.EnumerateArray())
                    {
                        string name = item.GetProperty("name").GetString();
                        string admin1 = item.TryGetProperty("admin1", out var a1) ? a1.GetString() : "";
                        string country = item.TryGetProperty("country", out var c) ? c.GetString() : "";
                        double lat = item.GetProperty("latitude").GetDouble();
                        double lon = item.GetProperty("longitude").GetDouble();

                        string fullName = $"{name}, {admin1}, {country}".Replace(", ,", ",").TrimEnd(',', ' ');
                        _lastSearchCoords[fullName] = (lat, lon);
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
                WeatherInfo info;
                if (WeatherSource == "OpenMeteo" && CityLat != 0 && CityLon != 0)
                    info = await GetWeatherFromOpenMeteoAsync();
                else
                    info = await GetWeatherFromWttrInAsync();

                if (info != null)
                {
                    _cachedWeather = info;
                    _lastFetchTime = DateTime.Now;
                }
                return _cachedWeather;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Weather fetch failed: {ex.Message}");
                return _cachedWeather;
            }
        }

        #region Open-Meteo Provider

        private async Task<WeatherInfo> GetWeatherFromOpenMeteoAsync()
        {
            string forecastUrl = $"https://api.open-meteo.com/v1/forecast?" +
                $"latitude={CityLat}&longitude={CityLon}" +
                $"&hourly=temperature_2m,relative_humidity_2m,apparent_temperature," +
                $"precipitation_probability,precipitation,weather_code," +
                $"surface_pressure,visibility,wind_speed_10m,wind_direction_10m,uv_index" +
                $"&daily=sunrise,sunset" +
                $"&timezone=auto&forecast_days=2";

            string aqUrl = $"https://air-quality-api.open-meteo.com/v1/air-quality?" +
                $"latitude={CityLat}&longitude={CityLon}" +
                $"&hourly=us_aqi,pm2_5,pm10,grass_pollen,birch_pollen,ragweed_pollen" +
                $"&forecast_days=2";

            _httpClient.DefaultRequestHeaders.UserAgent.Clear();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("TaskFlyout/1.0");

            var forecastTask = _httpClient.GetStringAsync(forecastUrl);
            Task<string> aqTask;
            try { aqTask = _httpClient.GetStringAsync(aqUrl); }
            catch { aqTask = Task.FromResult<string>(null); }

            string forecastJson = await forecastTask;
            string aqJson = null;
            try { aqJson = await aqTask; } catch { }

            using var forecastDoc = JsonDocument.Parse(forecastJson);
            var root = forecastDoc.RootElement;
            var hourly = root.GetProperty("hourly");
            var daily = root.GetProperty("daily");

            var times = hourly.GetProperty("time");
            var temps = hourly.GetProperty("temperature_2m");
            var humidity = hourly.GetProperty("relative_humidity_2m");
            var feelsLike = hourly.GetProperty("apparent_temperature");
            var precipProb = hourly.GetProperty("precipitation_probability");
            var precip = hourly.GetProperty("precipitation");
            var weatherCodes = hourly.GetProperty("weather_code");
            var pressure = hourly.GetProperty("surface_pressure");
            var visibility = hourly.GetProperty("visibility");
            var windSpeed = hourly.GetProperty("wind_speed_10m");
            var windDir = hourly.GetProperty("wind_direction_10m");
            var uvIndex = hourly.GetProperty("uv_index");

            // Parse air quality data
            JsonElement aqTimes = default, aqAqi = default, aqPm25 = default, aqPm10 = default;
            JsonElement aqGrassPollen = default, aqBirchPollen = default, aqRagweedPollen = default;
            bool hasAq = false;

            if (aqJson != null)
            {
                try
                {
                    using var aqDoc = JsonDocument.Parse(aqJson);
                    var aqRoot = aqDoc.RootElement;
                    if (aqRoot.TryGetProperty("hourly", out var aqHourly))
                    {
                        // Clone to separate document since aqDoc will be disposed
                        var aqClone = JsonDocument.Parse(aqHourly.GetRawText());
                        var aqH = aqClone.RootElement;
                        aqTimes = aqH.GetProperty("time");
                        aqAqi = aqH.GetProperty("us_aqi");
                        aqPm25 = aqH.GetProperty("pm2_5");
                        aqPm10 = aqH.GetProperty("pm10");
                        aqGrassPollen = aqH.GetProperty("grass_pollen");
                        aqBirchPollen = aqH.GetProperty("birch_pollen");
                        aqRagweedPollen = aqH.GetProperty("ragweed_pollen");
                        hasAq = true;
                    }
                }
                catch { }
            }

            string sunrise = daily.GetProperty("sunrise")[0].GetString();
            string sunset = daily.GetProperty("sunset")[0].GetString();
            string sunriseTime = sunrise.Contains("T") ? sunrise.Split('T')[1] : sunrise;
            string sunsetTime = sunset.Contains("T") ? sunset.Split('T')[1] : sunset;

            string lang = GetCurrentLanguage();
            var info = new WeatherInfo
            {
                City = City,
                IconFont = IconFontFamily,
                Sunrise = sunriseTime,
                Sunset = sunsetTime,
                MoonPhase = GetMoonPhaseEmoji(DateTime.Today),
                HourlyForecast = new()
            };

            int totalHours = times.GetArrayLength();
            int nowHour = DateTime.Now.Hour;

            // Build hourly data: 24 hours from current hour
            for (int i = 0; i < Math.Min(totalHours, 48); i++)
            {
                string timeStr = times[i].GetString();
                if (!DateTime.TryParse(timeStr, out var dt)) continue;

                // Only include hours from now through next 23 hours
                var diffHours = (dt - DateTime.Now).TotalHours;
                if (diffHours < -1 || diffHours > 24) continue;

                int code = weatherCodes[i].GetInt32();
                double tempVal = temps[i].GetDouble();
                double flVal = feelsLike[i].GetDouble();
                int humVal = humidity[i].GetInt32();
                double wsVal = windSpeed[i].GetDouble();
                int wdVal = windDir[i].GetInt32();
                double ppVal = precipProb[i].GetDouble();
                double pVal = precip[i].GetDouble();
                double uvVal = uvIndex[i].GetDouble();
                double visVal = visibility[i].GetDouble();
                double pressVal = pressure[i].GetDouble();

                string aqiStr = "", pm25Str = "", pm10Str = "";
                string grassStr = "", birchStr = "", ragweedStr = "";

                if (hasAq && i < aqAqi.GetArrayLength())
                {
                    try
                    {
                        aqiStr = aqAqi[i].ValueKind != JsonValueKind.Null ? aqAqi[i].GetDouble().ToString("F0") : "";
                        pm25Str = aqPm25[i].ValueKind != JsonValueKind.Null ? aqPm25[i].GetDouble().ToString("F1") : "";
                        pm10Str = aqPm10[i].ValueKind != JsonValueKind.Null ? aqPm10[i].GetDouble().ToString("F1") : "";
                        grassStr = aqGrassPollen[i].ValueKind != JsonValueKind.Null ? aqGrassPollen[i].GetDouble().ToString("F0") : "";
                        birchStr = aqBirchPollen[i].ValueKind != JsonValueKind.Null ? aqBirchPollen[i].GetDouble().ToString("F0") : "";
                        ragweedStr = aqRagweedPollen[i].ValueKind != JsonValueKind.Null ? aqRagweedPollen[i].GetDouble().ToString("F0") : "";
                    }
                    catch { }
                }

                bool omHourIsDay = IsDaylight(dt, sunriseTime, sunsetTime);
                var hw = new HourlyWeather
                {
                    Hour = dt.Hour,
                    RawTime = dt,
                    WeatherCode = code,
                    TempValue = tempVal,
                    WindSpeedValue = wsVal,
                    PrecipProbValue = ppVal,
                    PrecipValue = pVal,
                    Time = dt.ToString("HH:mm"),
                    Temperature = $"{tempVal:F0}°C",
                    Icon = OpenMeteoCodeToIcon(code, IconFontFamily),
                    IconFont = IconFontFamily,
                    IconLayerUris = IconPackService.Instance.TryResolveBitmapLayers(code, omHourIsDay, isOpenMeteo: true),
                    Description = OpenMeteoCodeToDescription(code, lang),
                    FeelsLike = $"{flVal:F0}°C",
                    Humidity = $"{humVal}%",
                    WindSpeed = $"{wsVal:F0} km/h",
                    WindDirection = WindDegreeToDirection(wdVal, lang),
                    PrecipProbability = $"{ppVal:F0}%",
                    Precipitation = $"{pVal:F1} mm",
                    UVIndex = $"{uvVal:F1}",
                    Visibility = visVal >= 1000 ? $"{visVal / 1000:F1} km" : $"{visVal:F0} m",
                    Pressure = $"{pressVal:F0} hPa",
                    AirQuality = aqiStr,
                    PM25 = pm25Str,
                    PM10 = pm10Str,
                    PollenGrass = grassStr,
                    PollenBirch = birchStr,
                    PollenRagweed = ragweedStr
                };
                info.HourlyForecast.Add(hw);
            }

            // Set current conditions from nearest hour
            var currentHw = info.HourlyForecast.FirstOrDefault();
            if (currentHw != null)
            {
                bool omIsDay = IsDaylight(DateTime.Now, info.Sunrise, info.Sunset);
                info.Temperature = currentHw.Temperature;
                info.Description = currentHw.Description;
                info.Icon = currentHw.Icon;
                info.RawWeatherCode = currentHw.WeatherCode;
                info.IsDayTime = omIsDay;
                info.IconLayerUris = currentHw.IconLayerUris;
                info.IconBitmapUri = currentHw.IconLayerUris.Length > 0 ? currentHw.IconLayerUris[0] : null;
                info.FeelsLike = currentHw.FeelsLike;
                info.Humidity = currentHw.Humidity;
                info.WindSpeed = currentHw.WindSpeed;
                info.UVIndex = currentHw.UVIndex;
                info.Visibility = currentHw.Visibility;
                info.Pressure = currentHw.Pressure;
                info.AirQuality = currentHw.AirQuality;

                var maxPollen = new[] {
                    double.TryParse(currentHw.PollenGrass, out var g) ? g : 0,
                    double.TryParse(currentHw.PollenBirch, out var b) ? b : 0,
                    double.TryParse(currentHw.PollenRagweed, out var r) ? r : 0
                }.Max();
                info.Pollen = maxPollen > 0 ? maxPollen.ToString("F0") : "";
            }

            return info;
        }

        private static string GetCurrentLanguage()
        {
            string appLang = ApplicationData.Current.LocalSettings.Values["AppLang"] as string;
            if (string.IsNullOrEmpty(appLang))
                appLang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            return appLang.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? "en" : "zh";
        }

        private static string OpenMeteoCodeToIcon(int code, string fontFamily)
        {
            bool isSymbol = fontFamily != null && fontFamily.Contains("Segoe UI Symbol");

            if (isSymbol)
            {
                return code switch
                {
                    0 => "\u2600",
                    1 or 2 => "\u26C5",
                    3 => "\u2601",
                    45 or 48 => "\u2601",
                    51 or 53 or 55 or 56 or 57 => "\u2602",
                    61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 => "\u2614",
                    71 or 73 or 75 or 77 or 85 or 86 => "\u2744",
                    95 or 96 or 99 => "\u26A1",
                    _ => "\u2600"
                };
            }
            return code switch
            {
                0 => "\u2600\uFE0F",
                1 => "\U0001F324\uFE0F",
                2 => "\u26C5",
                3 => "\u2601\uFE0F",
                45 or 48 => "\U0001F32B\uFE0F",
                51 or 53 or 55 => "\U0001F326\uFE0F",
                56 or 57 => "\U0001F327\uFE0F",
                61 or 63 or 80 or 81 => "\U0001F327\uFE0F",
                65 or 82 => "\U0001F327\uFE0F",
                66 or 67 => "\U0001F327\uFE0F",
                71 or 73 or 75 or 77 => "\u2744\uFE0F",
                85 or 86 => "\U0001F328\uFE0F",
                95 => "\u26C8\uFE0F",
                96 or 99 => "\u26C8\uFE0F",
                _ => "\U0001F324\uFE0F"
            };
        }

        private static string OpenMeteoCodeToDescription(int code, string lang)
        {
            if (lang == "zh")
            {
                return code switch
                {
                    0 => "\u6674",
                    1 => "\u5927\u90E8\u6674\u6717",
                    2 => "\u591A\u4E91",
                    3 => "\u9634",
                    45 => "\u96FE",
                    48 => "\u51BB\u96FE",
                    51 => "\u5C0F\u6BDB\u6BDB\u96E8",
                    53 => "\u6BDB\u6BDB\u96E8",
                    55 => "\u5927\u6BDB\u6BDB\u96E8",
                    56 or 57 => "\u51BB\u6BDB\u6BDB\u96E8",
                    61 => "\u5C0F\u96E8",
                    63 => "\u4E2D\u96E8",
                    65 => "\u5927\u96E8",
                    66 or 67 => "\u51BB\u96E8",
                    71 => "\u5C0F\u96EA",
                    73 => "\u4E2D\u96EA",
                    75 => "\u5927\u96EA",
                    77 => "\u96EA\u7C92",
                    80 => "\u5C0F\u9635\u96E8",
                    81 => "\u4E2D\u9635\u96E8",
                    82 => "\u5927\u9635\u96E8",
                    85 => "\u5C0F\u9635\u96EA",
                    86 => "\u5927\u9635\u96EA",
                    95 => "\u96F7\u9635\u96E8",
                    96 or 99 => "\u96F7\u9635\u96E8\u4F34\u51B0\u96F9",
                    _ => "\u672A\u77E5"
                };
            }
            return code switch
            {
                0 => "Clear sky",
                1 => "Mainly clear",
                2 => "Partly cloudy",
                3 => "Overcast",
                45 => "Fog",
                48 => "Rime fog",
                51 => "Light drizzle",
                53 => "Moderate drizzle",
                55 => "Dense drizzle",
                56 or 57 => "Freezing drizzle",
                61 => "Slight rain",
                63 => "Moderate rain",
                65 => "Heavy rain",
                66 or 67 => "Freezing rain",
                71 => "Slight snow",
                73 => "Moderate snow",
                75 => "Heavy snow",
                77 => "Snow grains",
                80 => "Slight rain showers",
                81 => "Moderate rain showers",
                82 => "Violent rain showers",
                85 => "Slight snow showers",
                86 => "Heavy snow showers",
                95 => "Thunderstorm",
                96 or 99 => "Thunderstorm with hail",
                _ => "Unknown"
            };
        }

        private static string WindDegreeToDirection(int deg, string lang)
        {
            string[] dirsZh = { "\u5317", "\u4E1C\u5317", "\u4E1C", "\u4E1C\u5357", "\u5357", "\u897F\u5357", "\u897F", "\u897F\u5317" };
            string[] dirsEn = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            int idx = (int)Math.Round(deg / 45.0) % 8;
            return lang == "zh" ? dirsZh[idx] : dirsEn[idx];
        }

        private static string GetMoonPhaseEmoji(DateTime date)
        {
            double year = date.Year;
            double month = date.Month;
            double day = date.Day;
            if (month <= 2) { year--; month += 12; }
            double jd = Math.Floor(365.25 * (year + 4716)) + Math.Floor(30.6001 * (month + 1)) + day - 1524.5;
            double phase = ((jd - 2451549.5) / 29.53 % 1 + 1) % 1 * 29.53;

            if (phase < 1.84) return "\U0001F311";
            if (phase < 5.53) return "\U0001F312";
            if (phase < 9.22) return "\U0001F313";
            if (phase < 12.91) return "\U0001F314";
            if (phase < 16.61) return "\U0001F315";
            if (phase < 20.30) return "\U0001F316";
            if (phase < 23.99) return "\U0001F317";
            if (phase < 27.68) return "\U0001F318";
            return "\U0001F311";
        }

        #endregion

        #region wttr.in Provider (Legacy)

        private async Task<WeatherInfo> GetWeatherFromWttrInAsync()
        {
            string lang = GetCurrentLanguage();
            string wttrLang = lang == "en" ? "en" : "zh";
            string searchCity = City.Split(',')[0].Trim();
            string url = $"https://wttr.in/{Uri.EscapeDataString(searchCity)}?format=j1&lang={wttrLang}";

            _httpClient.DefaultRequestHeaders.UserAgent.Clear();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("curl/7.68.0");

            var response = await _httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            var current = root.GetProperty("current_condition")[0];
            string tempC = current.GetProperty("temp_C").GetString();
            string weatherCode = current.GetProperty("weatherCode").GetString();
            string humidityVal = current.TryGetProperty("humidity", out var h) ? h.GetString() : "";
            string windVal = current.TryGetProperty("windspeedKmph", out var w) ? w.GetString() : "";
            string flVal = current.TryGetProperty("FeelsLikeC", out var fl) ? fl.GetString() : "";
            string visVal = current.TryGetProperty("visibility", out var vis) ? vis.GetString() : "";
            string pressVal = current.TryGetProperty("pressure", out var pr) ? pr.GetString() : "";
            string uvVal = current.TryGetProperty("uvIndex", out var uv) ? uv.GetString() : "";

            string desc = "";
            if (wttrLang == "zh" && current.TryGetProperty("lang_zh", out var langZh) && langZh.GetArrayLength() > 0)
                desc = langZh[0].GetProperty("value").GetString();
            else if (current.TryGetProperty("weatherDesc", out var wDesc) && wDesc.GetArrayLength() > 0)
                desc = wDesc[0].GetProperty("value").GetString();

            int.TryParse(weatherCode, out int wttrRawCode);
            bool wttrIsDay = DateTime.Now.Hour >= 6 && DateTime.Now.Hour < 18;
            var wttrLayers = IconPackService.Instance.TryResolveBitmapLayers(wttrRawCode, wttrIsDay, isOpenMeteo: false);
            var info = new WeatherInfo
            {
                Temperature = $"{tempC}\u00B0C",
                Description = desc,
                Icon = WttrCodeToIcon(weatherCode, IconFontFamily),
                IconFont = IconFontFamily,
                IconLayerUris = wttrLayers,
                IconBitmapUri = wttrLayers.Length > 0 ? wttrLayers[0] : null,
                RawWeatherCode = wttrRawCode,
                IsDayTime = wttrIsDay,
                City = City,
                FeelsLike = string.IsNullOrEmpty(flVal) ? "" : $"{flVal}\u00B0C",
                Humidity = string.IsNullOrEmpty(humidityVal) ? "" : $"{humidityVal}%",
                WindSpeed = string.IsNullOrEmpty(windVal) ? "" : $"{windVal} km/h",
                UVIndex = uvVal,
                Visibility = string.IsNullOrEmpty(visVal) ? "" : $"{visVal} km",
                Pressure = string.IsNullOrEmpty(pressVal) ? "" : $"{pressVal} hPa"
            };

            // wttr.in provides 3-hourly data; interpolate to hourly
            if (root.TryGetProperty("weather", out var weatherArray) && weatherArray.GetArrayLength() > 0)
            {
                var allHourlyRaw = new List<(int hour, string tempC, string code, string hDesc,
                    string hum, string ws, string flC, string pp, string uvi, string visKm, string press)>();

                for (int d = 0; d < Math.Min(weatherArray.GetArrayLength(), 2); d++)
                {
                    var day = weatherArray[d];
                    if (!day.TryGetProperty("hourly", out var hourlyArray)) continue;
                    foreach (var hour in hourlyArray.EnumerateArray())
                    {
                        string timeRaw = hour.GetProperty("time").GetString();
                        int hourVal = int.Parse(timeRaw) / 100;

                        string hd = "";
                        if (wttrLang == "zh" && hour.TryGetProperty("lang_zh", out var hLang) && hLang.GetArrayLength() > 0)
                            hd = hLang[0].GetProperty("value").GetString();
                        else if (hour.TryGetProperty("weatherDesc", out var hdesc) && hdesc.GetArrayLength() > 0)
                            hd = hdesc[0].GetProperty("value").GetString();

                        allHourlyRaw.Add((
                            hourVal + d * 24,
                            hour.GetProperty("tempC").GetString(),
                            hour.GetProperty("weatherCode").GetString(),
                            hd,
                            hour.TryGetProperty("humidity", out var hh) ? hh.GetString() : "",
                            hour.TryGetProperty("windspeedKmph", out var ww) ? ww.GetString() : "",
                            hour.TryGetProperty("FeelsLikeC", out var ff) ? ff.GetString() : "",
                            hour.TryGetProperty("chanceofrain", out var cr) ? cr.GetString() : "",
                            hour.TryGetProperty("uvIndex", out var ui) ? ui.GetString() : "",
                            hour.TryGetProperty("visibility", out var vv) ? vv.GetString() : "",
                            hour.TryGetProperty("pressure", out var pp) ? pp.GetString() : ""
                        ));
                    }
                }

                // Interpolate 3-hourly to hourly for 24 hours from now
                int nowHour = DateTime.Now.Hour;
                for (int offset = 0; offset < 24; offset++)
                {
                    int targetHour = nowHour + offset;
                    // Find surrounding 3-hourly points
                    var before = allHourlyRaw.LastOrDefault(x => x.hour <= targetHour);
                    var after = allHourlyRaw.FirstOrDefault(x => x.hour > targetHour);

                    var source = before.code != null ? before : (after.code != null ? after : before);
                    if (source.code == null) continue;

                    int displayHour = targetHour % 24;
                    int.TryParse(source.code, out int rawCode);
                    double.TryParse(source.tempC, out double rawTemp);
                    double.TryParse(source.ws, out double rawWs);
                    double.TryParse(source.pp, out double rawPp);
                    bool hIsDay = displayHour >= 6 && displayHour < 18;
                    info.HourlyForecast.Add(new HourlyWeather
                    {
                        Hour = displayHour,
                        RawTime = DateTime.Today.AddHours(targetHour),
                        WeatherCode = rawCode,
                        TempValue = rawTemp,
                        WindSpeedValue = rawWs,
                        PrecipProbValue = rawPp,
                        Time = $"{displayHour:D2}:00",
                        Temperature = $"{source.tempC}\u00B0C",
                        Icon = WttrCodeToIcon(source.code, IconFontFamily),
                        IconFont = IconFontFamily,
                        IconLayerUris = IconPackService.Instance.TryResolveBitmapLayers(rawCode, hIsDay, isOpenMeteo: false),
                        Description = source.hDesc,
                        FeelsLike = string.IsNullOrEmpty(source.flC) ? "" : $"{source.flC}\u00B0C",
                        Humidity = string.IsNullOrEmpty(source.hum) ? "" : $"{source.hum}%",
                        WindSpeed = string.IsNullOrEmpty(source.ws) ? "" : $"{source.ws} km/h",
                        PrecipProbability = string.IsNullOrEmpty(source.pp) ? "" : $"{source.pp}%",
                        UVIndex = source.uvi ?? "",
                        Visibility = string.IsNullOrEmpty(source.visKm) ? "" : $"{source.visKm} km",
                        Pressure = string.IsNullOrEmpty(source.press) ? "" : $"{source.press} hPa"
                    });
                }
            }

            return info;
        }

        private static bool IsDaylight(DateTime now, string sunrise, string sunset)
        {
            if (!string.IsNullOrEmpty(sunrise) && !string.IsNullOrEmpty(sunset)
                && TimeSpan.TryParse(sunrise, out var sr) && TimeSpan.TryParse(sunset, out var ss))
            {
                var t = now.TimeOfDay;
                return t >= sr && t < ss;
            }
            return now.Hour >= 6 && now.Hour < 18;
        }

        private static string WttrCodeToIcon(string code, string fontFamily)
        {
            bool isSymbol = fontFamily != null && fontFamily.Contains("Segoe UI Symbol");

            if (isSymbol)
            {
                return code switch
                {
                    "113" => "\u2600",
                    "116" => "\u26C5",
                    "119" or "122" => "\u2601",
                    "143" or "248" or "260" => "\u2601",
                    "176" or "263" or "266" or "293" or "296" => "\u2602",
                    "299" or "302" or "305" or "308" or "356" or "359" => "\u2614",
                    "179" or "227" or "323" or "326" or "329" or "332" or "335" or "338" or "368" or "371" => "\u2744",
                    "200" or "386" or "389" or "392" or "395" => "\u26A1",
                    "182" or "185" or "281" or "284" or "311" or "314" or "317" or "350" or "362" or "365" or "374" or "377" => "\u2744",
                    _ => "\u2600"
                };
            }
            return code switch
            {
                "113" => "\u2600\uFE0F",
                "116" => "\u26C5",
                "119" or "122" => "\u2601\uFE0F",
                "143" or "248" or "260" => "\U0001F32B\uFE0F",
                "176" or "263" or "266" or "293" or "296" => "\U0001F326\uFE0F",
                "299" or "302" or "305" or "308" or "356" or "359" => "\U0001F327\uFE0F",
                "179" or "227" or "323" or "326" or "329" or "332" or "335" or "338" or "368" or "371" => "\u2744\uFE0F",
                "200" or "386" or "389" or "392" or "395" => "\u26C8\uFE0F",
                "182" or "185" or "281" or "284" or "311" or "314" or "317" or "350" or "362" or "365" or "374" or "377" => "\U0001F328\uFE0F",
                _ => "\U0001F324\uFE0F"
            };
        }

        #endregion

        public static readonly (string Key, string ZhLabel, string EnLabel, string Glyph)[] AllFlyoutFields = new[]
        {
            ("temperature",    "\u6E29\u5EA6",         "Temperature",    "\uE9CA"),
            ("feelslike",      "\u4F53\u611F\u6E29\u5EA6",     "Feels like",     "\uE9CA"),
            ("description",    "\u5929\u6C14\u63CF\u8FF0",     "Description",    "\uE286"),
            ("humidity",       "\u6E7F\u5EA6",         "Humidity",       "\uE945"),
            ("wind",           "\u98CE\u901F",         "Wind",           "\uEBE7"),
            ("precipitation",  "\u964D\u6C34\u6982\u7387",     "Precipitation",  "\uE790"),
            ("uv",             "UV \u6307\u6570",      "UV Index",       "\uE706"),
            ("visibility",     "\u80FD\u89C1\u5EA6",       "Visibility",     "\uE7B3"),
            ("pressure",       "\u6C14\u538B",         "Pressure",       "\uEC49"),
            ("airquality",     "\u7A7A\u6C14\u8D28\u91CF",     "Air Quality",    "\uE9CA"),
            ("pollen",         "\u82B1\u7C89\u8FC7\u654F",     "Pollen",         "\uE710"),
            ("sun",            "\u65E5\u51FA\u65E5\u843D",     "Sunrise/Sunset", "\uE706"),
            ("moon",           "\u6708\u76F8",         "Moon Phase",     "\uE708"),
        };
    }
}
