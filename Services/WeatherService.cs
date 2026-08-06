using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Devices.Geolocation;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Task_Flyout.Services
{
    public class HourlyWeather
    {
        public int Hour { get; set; }
        public DateTime RawTime { get; set; }
        public string Time { get; set; } = "";
        public string Temperature { get; set; } = "";
        public string Icon { get; set; } = "";
        public string IconFont { get; set; } = "";
        public string Description { get; set; } = "";
        public string FeelsLike { get; set; } = "";
        public string Humidity { get; set; } = "";
        public string WindSpeed { get; set; } = "";
        public string WindDirection { get; set; } = "";
        public string Precipitation { get; set; } = "";
        public string PrecipProbability { get; set; } = "";
        public string UVIndex { get; set; } = "";
        public string Visibility { get; set; } = "";
        public string Pressure { get; set; } = "";
        public string AirQuality { get; set; } = "";
        public string PM25 { get; set; } = "";
        public string PM10 { get; set; } = "";
        public string PollenGrass { get; set; } = "";
        public string PollenBirch { get; set; } = "";
        public string PollenRagweed { get; set; } = "";

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

    public class DailyWeather
    {
        public DateTime Date { get; set; }
        public string DayLabel { get; set; } = "";
        public string DateLabel { get; set; } = "";
        public string TemperatureRange { get; set; } = "";
        public string HighTemperature { get; set; } = "";
        public string LowTemperature { get; set; } = "";
        public string Icon { get; set; } = "";
        public string IconFont { get; set; } = "";
        public string Description { get; set; } = "";
        public string PrecipProbability { get; set; } = "";
        public string Precipitation { get; set; } = "";
        public string WindSpeed { get; set; } = "";
        public string UVIndex { get; set; } = "";
        public int WeatherCode { get; set; }
        public string[] IconLayerUris { get; set; } = Array.Empty<string>();
        public string IconLayer0Uri => IconLayerUris.Length > 0 ? IconLayerUris[0] : "";
        public string IconLayer1Uri => IconLayerUris.Length > 1 ? IconLayerUris[1] : "";
        public string IconLayer2Uri => IconLayerUris.Length > 2 ? IconLayerUris[2] : "";
        public string IconLayer3Uri => IconLayerUris.Length > 3 ? IconLayerUris[3] : "";
    }

    public class WeatherInfo
    {
        public string Temperature { get; set; } = "";
        public string Description { get; set; } = "";
        public string Icon { get; set; } = "";
        public string IconFont { get; set; } = "";
        public string? IconBitmapUri { get; set; } // layer 0 convenience (same as IconLayerUris[0])
        public string[] IconLayerUris { get; set; } = Array.Empty<string>();
        public int RawWeatherCode { get; set; }
        public bool IsDayTime { get; set; }
        public string City { get; set; } = "";
        public string Sunrise { get; set; } = "";
        public string Sunset { get; set; } = "";
        public string MoonPhase { get; set; } = "";
        public string FeelsLike { get; set; } = "";
        public string Humidity { get; set; } = "";
        public string WindSpeed { get; set; } = "";
        public string UVIndex { get; set; } = "";
        public string Visibility { get; set; } = "";
        public string Pressure { get; set; } = "";
        public string AirQuality { get; set; } = "";
        public string Pollen { get; set; } = "";
        public List<HourlyWeather> HourlyForecast { get; set; } = new();
        public List<DailyWeather> DailyForecast { get; set; } = new();
    }

    // On-disk envelope for the last successful fetch so a cold start can show the
    // previous result immediately (when still within the cache window) instead of
    // always waiting on the network.
    public class WeatherCacheEnvelope
    {
        public string LocationId { get; set; } = "";
        public string Key { get; set; } = "";
        public long FetchedTicks { get; set; }
        public WeatherInfo? Info { get; set; }
        public string? FailureKey { get; set; }
        public long FailureUtcTicks { get; set; }
        public int FailureCount { get; set; }
    }

    public sealed class WeatherCacheStore
    {
        public List<WeatherCacheEnvelope> Entries { get; set; } = new();
    }

    public sealed record CitySuggestion(string DisplayName, double Latitude, double Longitude)
    {
        public override string ToString() => DisplayName;
    }

    public class WeatherService
    {
        private static readonly ResourceLoader _loader = new();
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private WeatherInfo? _cachedWeather;
        private string? _cachedWeatherKey;
        private DateTime _lastFetchTime = DateTime.MinValue;
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(30);
        // The coordinator isolates caller cancellation while sharing equivalent requests.
        private readonly object _weatherLock = new();
        private readonly object _contextLock = new();
        private readonly SharedRequestCoordinator<WeatherInfo?> _weatherRequests = new();
        private CancellationTokenSource _weatherContextCancellation = new();
        private string? _lastFailureWeatherKey;
        private DateTimeOffset _lastFailureUtc = DateTimeOffset.MinValue;
        private int _consecutiveFailures;
        private bool _persistentWeatherLoaded;
        private static string PersistentWeatherPath => AppDataPathHelper.ResolveLocal("WeatherCache.json");
        private const string ProtectedWeatherScope = "weather";
        private const string ProtectedLocationKey = "locations_v2";
        private const string LegacyProtectedLocationKey = "location_v1";
        private const string ProtectedCacheKey = "cache_v2";
        private const string LegacyProtectedCacheKey = "cache_v1";
        private readonly object _locationLock = new();
        private WeatherLocationSettings _location = new();
        private WeatherFavoritesStore _favorites = new();
        private WeatherCacheStore _persistentCacheStore = new();
        private bool _locationLoaded;
        private long _weatherDataGeneration;
        private readonly SemaphoreSlim _weatherPersistenceGate = new(1, 1);
        private sealed record WeatherRequestContext(
            long Generation,
            string LocationId,
            bool IsEnabled,
            string Source,
            string City,
            string ProviderCity,
            double Latitude,
            double Longitude,
            string IconFont,
            CancellationToken ServiceCancellationToken)
        {
            public string Key => $"{Source}|{ProviderCity}|{Latitude.ToString(CultureInfo.InvariantCulture)}|{Longitude.ToString(CultureInfo.InvariantCulture)}";
            public string CoordinatorKey => $"{Generation}|{Key}";
        }

        public event EventHandler? LocationsChanged;

        public IReadOnlyList<SavedWeatherLocation> SavedLocations
        {
            get { EnsureLocationLoaded(); lock (_locationLock) return _favorites.Locations.Select(CloneLocation).ToList(); }
        }

        public string ActiveLocationId
        {
            get { EnsureLocationLoaded(); lock (_locationLock) return _favorites.ActiveId; }
        }

        public string ActiveLocationDisplayLabel
        {
            get
            {
                EnsureLocationLoaded();
                lock (_locationLock)
                    return _favorites.Locations.FirstOrDefault(x => x.Id == _favorites.ActiveId)?.DisplayLabel ?? _location.City;
            }
        }

        private static SavedWeatherLocation CloneLocation(SavedWeatherLocation location) => new()
        {
            Id = location.Id,
            Alias = location.Alias,
            DisplayLabel = location.DisplayLabel,
            Latitude = location.Latitude,
            Longitude = location.Longitude,
            IsCurrentLocation = location.IsCurrentLocation
        };

        private static async Task<string> GetStringWithAgentAsync(string url, string userAgent, CancellationToken ct = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(userAgent);
            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }

        #region Settings Properties

        public bool IsEnabled
        {
            get => ApplicationData.Current.LocalSettings.Values["WeatherEnabled"] as bool? ?? false;
            set
            {
                lock (_contextLock)
                {
                    if (IsEnabled == value) return;
                    ApplicationData.Current.LocalSettings.Values["WeatherEnabled"] = value;
                    InvalidateWeatherContext();
                }
            }
        }

        public string City
        {
            get { EnsureLocationLoaded(); lock (_locationLock) return _location.City; }
        }

        public double CityLat
        {
            get { EnsureLocationLoaded(); lock (_locationLock) return _location.Latitude; }
        }

        public double CityLon
        {
            get { EnsureLocationLoaded(); lock (_locationLock) return _location.Longitude; }
        }

        public string IconFontFamily
        {
            get => ApplicationData.Current.LocalSettings.Values["WeatherIconFont"] as string ?? "Segoe UI Emoji";
            set
            {
                lock (_contextLock)
                {
                    if (string.Equals(IconFontFamily, value, StringComparison.Ordinal)) return;
                    ApplicationData.Current.LocalSettings.Values["WeatherIconFont"] = value;
                    InvalidateWeatherContext();
                }
            }
        }

        public string WeatherSource
        {
            get => ApplicationData.Current.LocalSettings.Values["WeatherSource"] as string ?? "OpenMeteo";
            set
            {
                lock (_contextLock)
                {
                    if (string.Equals(WeatherSource, value, StringComparison.Ordinal)) return;
                    ApplicationData.Current.LocalSettings.Values["WeatherSource"] = value;
                    InvalidateWeatherContext();
                }
            }
        }

        public string EnabledFlyoutFields
        {
            get => ApplicationData.Current.LocalSettings.Values["WeatherFlyoutFields"] as string ?? "temperature,description";
            set => ApplicationData.Current.LocalSettings.Values["WeatherFlyoutFields"] = value;
        }

        public string EnabledBarFields
        {
            get => ApplicationData.Current.LocalSettings.Values["WeatherBarFields"] as string ?? "icon,temperature,description,location";
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
        public WeatherAlert? DetectUpcomingAlert(WeatherInfo? info)
        {
            if (info == null || info.HourlyForecast == null || info.HourlyForecast.Count == 0)
                return null;

            int lookAhead = Math.Max(1, BarAlertHours);
            var enabled = GetEnabledAlertTypes();
            if (enabled.Count == 0) return null;

            var now = DateTime.Now;
            var forecast = info.HourlyForecast
                .OrderBy(h => h.RawTime)
                .ToList();
            var currentHour = forecast.LastOrDefault(h => h.RawTime <= now) ?? forecast.FirstOrDefault();
            if (currentHour == null) return null;

            var upcoming = forecast
                .Where(h => h.RawTime >= currentHour.RawTime && (h.RawTime - now).TotalHours <= lookAhead)
                .OrderBy(h => h.RawTime)
                .ToList();

            if (upcoming.Count == 0) return null;

            if (IsRainCondition(currentHour) && TryGetEnabledRainType(currentHour, enabled, out var currentRainType))
            {
                var stopHour = forecast.FirstOrDefault(h => h.RawTime > currentHour.RawTime && !IsRainCondition(h));
                return new WeatherAlert
                {
                    Type = currentRainType,
                    HoursAhead = 0,
                    MinutesUntilEnd = GetRainDurationMinutes(stopHour, now, forecast.LastOrDefault()?.RawTime),
                    Icon = GetAlertIcon(currentRainType),
                    Message = BuildRainEndingMessage(stopHour, now, forecast.LastOrDefault()?.RawTime),
                    StartTime = currentHour.RawTime,
                    EndTime = stopHour?.RawTime,
                    PrecipProbability = currentHour.PrecipProbValue,
                    WindSpeed = currentHour.WindSpeedValue,
                    Temperature = currentHour.TempValue
                };
            }

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
                    var end = forecast.FirstOrDefault(h => h.RawTime > hit.RawTime && !MatchAlert(h, type));
                    return new WeatherAlert
                    {
                        Type = type,
                        HoursAhead = hoursAhead,
                        Icon = GetAlertIcon(type),
                        Message = BuildAlertMessage(type, hoursAhead),
                        StartTime = hit.RawTime,
                        EndTime = end?.RawTime,
                        PrecipProbability = hit.PrecipProbValue,
                        WindSpeed = hit.WindSpeedValue,
                        Temperature = hit.TempValue
                    };
                }
            }
            return null;
        }

        private static bool TryGetEnabledRainType(HourlyWeather h, HashSet<WeatherAlertType> enabled, out WeatherAlertType type)
        {
            type = ClassifyRainType(h);
            if (enabled.Contains(type)) return true;

            if (enabled.Contains(WeatherAlertType.Rain))
            {
                type = WeatherAlertType.Rain;
                return true;
            }

            return false;
        }

        private static WeatherAlertType ClassifyRainType(HourlyWeather h)
        {
            int code = h.WeatherCode;
            if (code is 95 or 96 or 99) return WeatherAlertType.Thunderstorm;
            if (code is 56 or 57 or 66 or 67) return WeatherAlertType.FreezingRain;
            if (code is 65 or 82) return WeatherAlertType.HeavyRain;
            return WeatherAlertType.Rain;
        }

        private static bool IsRainCondition(HourlyWeather h)
        {
            int code = h.WeatherCode;
            if (code is 51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 or 95 or 96 or 99)
                return true;

            return h.PrecipValue > 0.05 && h.PrecipProbValue >= 40;
        }

        private static string BuildRainEndingMessage(HourlyWeather? stopHour, DateTime now, DateTime? lastForecastTime)
        {
            if (stopHour != null)
            {
                var minutes = Math.Max(0, (int)Math.Round((stopHour.RawTime - now).TotalMinutes));
                if (minutes <= 5)
                    return _loader.GetStringOrDefault("TextRainStopSoon") ?? "Rain now, should stop soon";

                var when = FormatRainTimeSpan(minutes);
                return string.Format(_loader.GetStringOrDefault("TextRainStopInAbout") ?? "Rain now, should stop in about {0}", when);
            }

            var hours = lastForecastTime.HasValue
                ? Math.Max(1, (int)Math.Ceiling((lastForecastTime.Value - now).TotalHours))
                : 24;
            return string.Format(_loader.GetStringOrDefault("TextRainContinues") ?? "Rain continues for the next {0}h", hours);
        }

        private static int? GetRainDurationMinutes(HourlyWeather? stopHour, DateTime now, DateTime? lastForecastTime)
        {
            DateTime? end = stopHour?.RawTime ?? lastForecastTime;
            return end.HasValue ? Math.Max(1, (int)Math.Round((end.Value - now).TotalMinutes)) : null;
        }

        private static string FormatRainTimeSpan(int minutes)
        {
            if (minutes < 60) return string.Format(_loader.GetStringOrDefault("TextRainMinute") ?? "{0} min", minutes);

            var hours = Math.Max(1, (int)Math.Round(minutes / 60.0));
            return string.Format(
                LocalizationHelper.AppCulture,
                _loader.GetStringOrDefault("TextRainHour") ?? "{0}h",
                hours);
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

        private static string BuildAlertMessage(WeatherAlertType type, int hoursAhead)
        {
            string labelKey = type switch
            {
                WeatherAlertType.Thunderstorm => "WeatherAlert_Thunderstorm",
                WeatherAlertType.FreezingRain => "WeatherAlert_FreezingRain",
                WeatherAlertType.HeavyRain => "WeatherAlert_HeavyRain",
                WeatherAlertType.Rain => "WeatherAlert_Rain",
                WeatherAlertType.HeavySnow => "WeatherAlert_HeavySnow",
                WeatherAlertType.Snow => "WeatherAlert_Snow",
                WeatherAlertType.Fog => "WeatherAlert_Fog",
                WeatherAlertType.HighWind => "WeatherAlert_HighWind",
                WeatherAlertType.ExtremeHeat => "WeatherAlert_ExtremeHeat",
                WeatherAlertType.ExtremeCold => "WeatherAlert_ExtremeCold",
                _ => "WeatherCondition_Unknown"
            };
            string label = _loader.GetStringOrDefault(labelKey) ?? type.ToString();
            if (hoursAhead <= 0)
            {
                return string.Format(
                    _loader.GetStringOrDefault("WeatherAlert_NowFormat") ?? "{0} now",
                    label);
            }

            return string.Format(
                _loader.GetStringOrDefault("WeatherAlert_InHoursFormat") ?? "{0} in {1}h",
                label,
                hoursAhead);
        }

        public static readonly (string Key, string ResourceKey, string EnLabel)[] AllBarFields = new[]
        {
            ("icon",         "WeatherField_Icon",        "Icon"),
            ("temperature",  "WeatherField_Temperature", "Temperature"),
            ("description",  "WeatherField_Description", "Description"),
            ("location",     "WeatherField_Location",    "Location"),
            ("feelslike",    "WeatherField_FeelsLike",   "Feels like"),
            ("humidity",     "WeatherField_Humidity",    "Humidity"),
            ("wind",         "WeatherField_Wind",        "Wind"),
        };

        public static readonly (WeatherAlertType Type, string ResourceKey, string EnLabel)[] AllAlertTypes = new[]
        {
            (WeatherAlertType.Thunderstorm, "WeatherAlert_Thunderstorm", "Thunderstorm"),
            (WeatherAlertType.FreezingRain, "WeatherAlert_FreezingRain", "Freezing rain"),
            (WeatherAlertType.HeavyRain,    "WeatherAlert_HeavyRain",    "Heavy rain"),
            (WeatherAlertType.Rain,         "WeatherAlert_Rain",         "Rain"),
            (WeatherAlertType.HeavySnow,    "WeatherAlert_HeavySnow",    "Heavy snow"),
            (WeatherAlertType.Snow,         "WeatherAlert_Snow",         "Snow"),
            (WeatherAlertType.Fog,          "WeatherAlert_Fog",          "Fog"),
            (WeatherAlertType.HighWind,     "WeatherAlert_HighWind",     "Strong wind"),
            (WeatherAlertType.ExtremeHeat,  "WeatherAlert_ExtremeHeat",  "Extreme heat"),
            (WeatherAlertType.ExtremeCold,  "WeatherAlert_ExtremeCold",  "Extreme cold"),
        };

        public string FormatBarAlert(WeatherAlert alert)
        {
            var descriptor = AllAlertTypes.First(entry => entry.Type == alert.Type);
            string label = _loader.GetStringOrDefault(descriptor.ResourceKey) ?? descriptor.EnLabel;
            return WeatherAlertTextPolicy.FormatCompact(label, alert.HoursAhead, alert.MinutesUntilEnd);
        }

        public HashSet<string> GetEnabledFields()
        {
            return new HashSet<string>(
                (EnabledFlyoutFields ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        public void SetEnabledFields(HashSet<string> fields)
        {
            EnabledFlyoutFields = string.Join(",", fields);
        }

        private void EnsureLocationLoaded()
        {
            lock (_locationLock)
            {
                if (_locationLoaded) return;

                WeatherFavoritesStore? favorites = null;
                try
                {
                    string? protectedJson = LocalSqliteStore.ReadProtectedText(ProtectedWeatherScope, ProtectedLocationKey);
                    if (!string.IsNullOrWhiteSpace(protectedJson))
                        favorites = JsonSerializer.Deserialize(protectedJson, AppJsonContext.Default.WeatherFavoritesStore);
                }
                catch
                {
                }

                if (favorites == null)
                {
                    WeatherLocationSettings? legacy = null;
                    try
                    {
                        string? legacyJson = LocalSqliteStore.ReadProtectedText(ProtectedWeatherScope, LegacyProtectedLocationKey);
                        if (!string.IsNullOrWhiteSpace(legacyJson))
                            legacy = JsonSerializer.Deserialize(legacyJson, AppJsonContext.Default.WeatherLocationSettings);
                    }
                    catch { }
                    if (legacy == null)
                    {
                        var values = ApplicationData.Current.LocalSettings.Values;
                        legacy = WeatherLocationPolicy.Normalize(values["WeatherCity"] as string,
                            values["WeatherCityLat"] as double? ?? 0, values["WeatherCityLon"] as double? ?? 0);
                    }
                    favorites = new WeatherFavoritesStore();
                    if (WeatherLocationPolicy.HasPersistableData(legacy))
                        WeatherLocationPolicy.TryAdd(favorites, legacy.City, legacy.Latitude, legacy.Longitude, false, out _);
                    PersistFavorites(favorites);
                }
                _favorites = WeatherLocationPolicy.Normalize(favorites);
                var active = _favorites.Locations.FirstOrDefault(x => x.Id == _favorites.ActiveId);
                _location = active == null ? new WeatherLocationSettings() : WeatherLocationPolicy.Normalize(active.Label, active.Latitude, active.Longitude);
                RemoveLegacyLocationSettings();
                _locationLoaded = true;
            }
        }

        private static void RemoveLegacyLocationSettings()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            values.Remove("WeatherCity");
            values.Remove("WeatherCityLat");
            values.Remove("WeatherCityLon");
        }

        public bool SelectCity(CitySuggestion suggestion)
            => AddLocation(suggestion.Latitude, suggestion.Longitude, suggestion.DisplayName, false);

        /// <summary>Set the weather location directly from coordinates (e.g. device GPS),
        /// with a display label. Open-Meteo uses the coordinates directly.</summary>
        public void SetCoordinates(double latitude, double longitude, string displayName)
            => AddLocation(latitude, longitude, displayName, true);

        public bool AddLocation(double latitude, double longitude, string displayName, bool currentLocation)
        {
            EnsureLocationLoaded();
            lock (_locationLock)
            {
                if (!WeatherLocationPolicy.TryAdd(_favorites, displayName, latitude, longitude, currentLocation, out var added)) return false;
                _location = WeatherLocationPolicy.Normalize(added!.Label, added.Latitude, added.Longitude);
                PersistFavorites(_favorites);
            }
            InvalidateWeatherContext();
            LocationsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public bool SelectLocation(string id)
        {
            EnsureLocationLoaded();
            lock (_locationLock)
            {
                var selected = _favorites.Locations.FirstOrDefault(x => x.Id == id);
                if (selected == null || _favorites.ActiveId == id) return selected != null;
                _favorites.ActiveId = id;
                _location = WeatherLocationPolicy.Normalize(selected.Label, selected.Latitude, selected.Longitude);
                PersistFavorites(_favorites);
                TryHydrateLocationCache(id);
            }
            InvalidateWeatherContext();
            LocationsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public WeatherInfo? GetActiveCachedWeather()
        {
            EnsureLocationLoaded();
            lock (_weatherLock) return _cachedWeatherKey == BuildCurrentWeatherKey() ? _cachedWeather : null;
        }

        private string BuildCurrentWeatherKey()
        {
            string providerCity = _favorites.Locations.FirstOrDefault(x => x.Id == _favorites.ActiveId)?.DisplayLabel ?? _location.City;
            return $"{WeatherSource}|{providerCity}|{_location.Latitude.ToString(CultureInfo.InvariantCulture)}|{_location.Longitude.ToString(CultureInfo.InvariantCulture)}";
        }

        private void TryHydrateLocationCache(string locationId)
        {
            try
            {
                if (_persistentCacheStore.Entries.Count == 0)
                {
                    string? json = LocalSqliteStore.ReadProtectedText(ProtectedWeatherScope, ProtectedCacheKey);
                    if (!string.IsNullOrWhiteSpace(json))
                        _persistentCacheStore = JsonSerializer.Deserialize(json, AppJsonContext.Default.WeatherCacheStore) ?? new();
                }
                var envelope = _persistentCacheStore.Entries.FirstOrDefault(x => x.LocationId == locationId);
                lock (_weatherLock)
                {
                    _cachedWeather = envelope?.Info;
                    _cachedWeatherKey = envelope?.Key;
                    _lastFetchTime = envelope?.FetchedTicks > 0 ? new DateTime(envelope.FetchedTicks, DateTimeKind.Local) : DateTime.MinValue;
                    _lastFailureWeatherKey = envelope?.FailureKey;
                    _lastFailureUtc = envelope?.FailureUtcTicks > 0 ? new DateTimeOffset(envelope.FailureUtcTicks, TimeSpan.Zero) : DateTimeOffset.MinValue;
                    _consecutiveFailures = envelope?.FailureCount ?? 0;
                    _persistentWeatherLoaded = envelope != null;
                }
            }
            catch { }
        }

        public bool RenameLocation(string id, string alias)
        {
            EnsureLocationLoaded();
            lock (_locationLock)
            {
                var location = _favorites.Locations.FirstOrDefault(x => x.Id == id);
                if (location == null) return false;
                location.Alias = alias?.Trim() ?? "";
                if (_favorites.ActiveId == id)
                    _location = WeatherLocationPolicy.Normalize(location.Label, location.Latitude, location.Longitude);
                PersistFavorites(_favorites);
            }
            LocationsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public bool RemoveLocation(string id)
        {
            EnsureLocationLoaded();
            lock (_locationLock)
            {
                if (!WeatherLocationPolicy.Remove(_favorites, id)) return false;
                _persistentCacheStore.Entries.RemoveAll(x => x.LocationId == id);
                var active = _favorites.Locations.FirstOrDefault(x => x.Id == _favorites.ActiveId);
                _location = active == null ? new WeatherLocationSettings() : WeatherLocationPolicy.Normalize(active.Label, active.Latitude, active.Longitude);
                PersistFavorites(_favorites);
                string cacheJson = JsonSerializer.Serialize(_persistentCacheStore, AppJsonContext.Default.WeatherCacheStore);
                LocalSqliteStore.WriteProtectedText(ProtectedWeatherScope, ProtectedCacheKey, cacheJson);
            }
            InvalidateWeatherContext();
            LocationsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        private static void PersistFavorites(WeatherFavoritesStore favorites)
        {
            string json = JsonSerializer.Serialize(favorites, AppJsonContext.Default.WeatherFavoritesStore);
            LocalSqliteStore.WriteProtectedText(ProtectedWeatherScope, ProtectedLocationKey, json);
        }

        private void SetCoordinates(double latitude, double longitude, string displayName, long? expectedGeneration)
        {
            EnsureLocationLoaded();
            var normalized = WeatherLocationPolicy.Normalize(displayName, latitude, longitude);
            string json;
            long generation;
            lock (_contextLock)
            {
                if (expectedGeneration.HasValue
                    && expectedGeneration.Value != Volatile.Read(ref _weatherDataGeneration)) return;

                lock (_locationLock)
                {
                    var current = _favorites.Locations.FirstOrDefault(x => x.IsCurrentLocation);
                    if (current != null && _location.City == normalized.City &&
                        _location.Latitude == normalized.Latitude &&
                        _location.Longitude == normalized.Longitude)
                        return;
                    if (current == null)
                    {
                        if (!WeatherLocationPolicy.TryAdd(_favorites, displayName, latitude, longitude, true, out current)) return;
                    }
                    else
                    {
                        current.DisplayLabel = normalized.City;
                        current.Latitude = normalized.Latitude;
                        current.Longitude = normalized.Longitude;
                        _favorites.ActiveId = current.Id;
                    }
                    _location = WeatherLocationPolicy.Normalize(current!.Label, current.Latitude, current.Longitude);
                    json = JsonSerializer.Serialize(_favorites, AppJsonContext.Default.WeatherFavoritesStore);
                }
                InvalidateWeatherContext();
                generation = Volatile.Read(ref _weatherDataGeneration);
            }

            _weatherPersistenceGate.Wait();
            try
            {
                if (generation != Volatile.Read(ref _weatherDataGeneration)) return;
                LocalSqliteStore.WriteProtectedText(ProtectedWeatherScope, ProtectedLocationKey, json);
            }
            finally
            {
                _weatherPersistenceGate.Release();
            }
        }

        /// <summary>Outcome of a reverse-geocode, including which provider answered (for diagnostics).</summary>
        public sealed class ReverseGeocodeResult
        {
            public string? Name { get; init; }
            /// <summary>osm-road | osm-area | osm-empty | osm-error | bigdatacloud | none.</summary>
            public string Source { get; init; } = "none";
        }

        /// <summary>Resolve coordinates to a localized street-level label,
        /// falling back to the keyless city-level geocoder. Returns null on failure.</summary>
        public async Task<string?> ReverseGeocodeAsync(
            double latitude,
            double longitude,
            CancellationToken cancellationToken = default)
            => (await ReverseGeocodeDetailedAsync(latitude, longitude, cancellationToken)).Name;

        /// <summary>Like <see cref="ReverseGeocodeAsync"/> but also reports which provider answered,
        /// so callers can tell a Nominatim-unreachable city fallback from a coarse fix.</summary>
        public async Task<ReverseGeocodeResult> ReverseGeocodeDetailedAsync(
            double latitude,
            double longitude,
            CancellationToken cancellationToken = default)
        {
            var area = await ReverseGeocodeStreetAsync(latitude, longitude, cancellationToken);
            if (!string.IsNullOrWhiteSpace(area.Name)) return area;

            // Nominatim gave nothing usable (often unreachable from mainland China) — try city level.
            var city = await ReverseGeocodeCityAsync(latitude, longitude, cancellationToken);
            if (!string.IsNullOrWhiteSpace(city))
                return new ReverseGeocodeResult { Name = city, Source = "bigdatacloud" };

            return new ReverseGeocodeResult { Name = null, Source = area.Source };
        }

        /// <summary>Street-level reverse geocode via OpenStreetMap Nominatim.</summary>
        private async Task<ReverseGeocodeResult> ReverseGeocodeStreetAsync(
            double latitude,
            double longitude,
            CancellationToken cancellationToken)
        {
            try
            {
                string lang = GetCurrentLanguage() == "zh" ? "zh" : "en";
                string url = "https://nominatim.openstreetmap.org/reverse" +
                             $"?lat={latitude.ToString(CultureInfo.InvariantCulture)}" +
                             $"&lon={longitude.ToString(CultureInfo.InvariantCulture)}" +
                             "&format=jsonv2&zoom=18&addressdetails=1" +
                             $"&accept-language={lang}";

                // Nominatim's usage policy requires an identifying User-Agent.
                var json = await GetStringWithAgentAsync(url, "TaskFlyout/1.0 (weather location)", cancellationToken);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("address", out var addr) ||
                    addr.ValueKind != JsonValueKind.Object)
                    return new ReverseGeocodeResult { Source = "osm-empty" };

                string? Pick(string prop) =>
                    addr.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                        ? v.GetString()?.Trim() : null;

                string? province = Pick("state") ?? Pick("province") ?? Pick("region");
                string? city = Pick("city") ?? Pick("municipality") ?? Pick("town")
                               ?? Pick("county");
                string? district = Pick("city_district") ?? Pick("district") ?? Pick("county");
                string? township = Pick("town") ?? Pick("village") ?? Pick("suburb")
                                   ?? Pick("neighbourhood") ?? Pick("quarter");
                string? street = Pick("road") ?? Pick("pedestrian") ?? Pick("residential")
                                 ?? Pick("footway") ?? Pick("path");
                string label = WeatherLocationLabelPolicy.FormatDetailed(province, city, district, township, street);
                return string.IsNullOrWhiteSpace(label)
                    ? new ReverseGeocodeResult { Source = "osm-empty" }
                    : new ReverseGeocodeResult { Name = label, Source = string.IsNullOrWhiteSpace(street) ? "osm-area" : "osm-road" };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Nominatim reverse geocode failed: {ex.Message}");
                return new ReverseGeocodeResult { Source = "osm-error" };
            }
        }

        /// <summary>City-level reverse geocode via the keyless BigDataCloud client (fallback).</summary>
        private async Task<string?> ReverseGeocodeCityAsync(
            double latitude,
            double longitude,
            CancellationToken cancellationToken)
        {
            try
            {
                string lang = GetCurrentLanguage() == "zh" ? "zh" : "en";
                string url = "https://api.bigdatacloud.net/data/reverse-geocode-client" +
                             $"?latitude={latitude.ToString(CultureInfo.InvariantCulture)}" +
                             $"&longitude={longitude.ToString(CultureInfo.InvariantCulture)}" +
                             $"&localityLanguage={lang}";

                var json = await GetStringWithAgentAsync(url, "TaskFlyout/1.0", cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string? Pick(string prop) =>
                    root.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
                        ? v.GetString() : null;

                string? province = Pick("principalSubdivision");
                string? city = Pick("city");
                if (string.IsNullOrWhiteSpace(city)) city = Pick("locality");
                string label = WeatherLocationLabelPolicy.FormatProvinceCity(province, city);
                return string.IsNullOrWhiteSpace(label) ? null : label;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                return null;
            }
        }

        public bool AutoFollowLocation
        {
            get => ApplicationData.Current.LocalSettings.Values["WeatherAutoFollowLocation"] as bool? ?? false;
            set
            {
                lock (_contextLock)
                {
                    if (AutoFollowLocation == value) return;
                    ApplicationData.Current.LocalSettings.Values["WeatherAutoFollowLocation"] = value;
                    InvalidateWeatherContext();
                }
            }
        }

        private Geolocator? _trackingGeolocator;

        public bool IsLocationTrackingActive => _trackingGeolocator != null;

        /// <summary>Raised (on a background thread) after the followed location changed and
        /// the weather coordinates were updated — subscribers should refresh on the UI thread.</summary>
        public event EventHandler? LocationUpdated;

        /// <summary>Begin following the device location. Returns false if access was denied.</summary>
        public async Task<bool> StartLocationTrackingAsync()
        {
            try
            {
                if (_trackingGeolocator != null) return true;

                var access = await Geolocator.RequestAccessAsync();
                if (access != GeolocationAccessStatus.Allowed) return false;

                // High accuracy so the followed position resolves to a street; the 3km
                // movement threshold still keeps us from refetching weather constantly.
                _trackingGeolocator = new Geolocator
                {
                    DesiredAccuracy = PositionAccuracy.High,
                    MovementThreshold = 3000
                };
                _trackingGeolocator.PositionChanged += OnTrackedPositionChanged;
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StartLocationTracking failed: {ex.Message}");
                return false;
            }
        }

        public void StopLocationTracking()
        {
            if (_trackingGeolocator != null)
            {
                _trackingGeolocator.PositionChanged -= OnTrackedPositionChanged;
                _trackingGeolocator = null;
            }
        }

        private async void OnTrackedPositionChanged(Geolocator sender, PositionChangedEventArgs args)
        {
            if (!ReferenceEquals(sender, _trackingGeolocator) || !AutoFollowLocation) return;
            long generation = Volatile.Read(ref _weatherDataGeneration);
            try
            {
                var p = args.Position.Coordinate.Point.Position;
                CancellationToken serviceToken;
                lock (_contextLock) serviceToken = _weatherContextCancellation.Token;
                string? place = await ReverseGeocodeAsync(p.Latitude, p.Longitude, serviceToken);
                if (generation != Volatile.Read(ref _weatherDataGeneration) ||
                    !ReferenceEquals(sender, _trackingGeolocator) || !AutoFollowLocation)
                    return;
                string label = !string.IsNullOrWhiteSpace(place)
                    ? place!
                    : (_loader.GetStringOrDefault("WeatherCurrentLocation") ?? "Current location");

                SetCoordinates(p.Latitude, p.Longitude, label, generation);
                if (generation + 1 != Volatile.Read(ref _weatherDataGeneration) || !AutoFollowLocation)
                    return;
                // Coordinates are part of the cache key, so the next fetch is a miss and refetches.
                LocationUpdated?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Tracked position change failed: {ex.Message}");
            }
        }

        public async Task<List<CitySuggestion>> SearchCityAsync(string query, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<CitySuggestion>();
            try
            {
                string url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(query)}&count=5&language=zh";

                var response = await GetStringWithAgentAsync(url, "TaskFlyout/1.0", cancellationToken);
                using var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("results", out var results))
                {
                    var list = new List<CitySuggestion>();
                    foreach (var item in results.EnumerateArray())
                    {
                        string name = item.GetProperty("name").GetString() ?? "";
                        string admin1 = item.TryGetProperty("admin1", out var a1) ? a1.GetString() ?? "" : "";
                        string country = item.TryGetProperty("country", out var c) ? c.GetString() ?? "" : "";
                        double lat = item.GetProperty("latitude").GetDouble();
                        double lon = item.GetProperty("longitude").GetDouble();

                        string fullName = $"{name}, {admin1}, {country}".Replace(", ,", ",").TrimEnd(',', ' ');
                        list.Add(new CitySuggestion(fullName, lat, lon));
                    }
                    return list;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { }
            return new List<CitySuggestion>();
        }

        public async Task<WeatherInfo?> GetWeatherAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            var context = CaptureWeatherRequestContext();
            if (!context.IsEnabled || string.IsNullOrWhiteSpace(context.City))
                return null;

            await TryLoadPersistentWeatherAsync(context, cancellationToken);
            lock (_weatherLock)
            {
                if (!forceRefresh && _cachedWeather != null && _cachedWeatherKey == context.Key
                    && (DateTime.Now - _lastFetchTime) < _cacheExpiry)
                    return _cachedWeather;

                if (!forceRefresh && WeatherRetryPolicy.IsBackedOff(
                    context.Key, _lastFailureWeatherKey, _lastFailureUtc.UtcTicks,
                    _consecutiveFailures, DateTimeOffset.UtcNow))
                    return _cachedWeatherKey == context.Key ? _cachedWeather : null;
            }

            return await _weatherRequests.RunAsync(
                context.CoordinatorKey,
                requestCancellation => FetchAndCacheWeatherAsync(context, requestCancellation),
                cancellationToken);
        }

        private WeatherRequestContext CaptureWeatherRequestContext()
        {
            EnsureLocationLoaded();
            lock (_contextLock)
            lock (_locationLock)
            {
                return new WeatherRequestContext(
                    Volatile.Read(ref _weatherDataGeneration),
                    _favorites.ActiveId,
                    IsEnabled,
                    WeatherSource,
                    _favorites.Locations.FirstOrDefault(x => x.Id == _favorites.ActiveId)?.Label ?? _location.City,
                    _favorites.Locations.FirstOrDefault(x => x.Id == _favorites.ActiveId)?.DisplayLabel ?? _location.City,
                    _location.Latitude,
                    _location.Longitude,
                    IconFontFamily,
                    _weatherContextCancellation.Token);
            }
        }

        private void InvalidateWeatherContext()
        {
            var oldCancellation = _weatherContextCancellation;
            _weatherContextCancellation = new CancellationTokenSource();
            Interlocked.Increment(ref _weatherDataGeneration);
            lock (_weatherLock) _persistentWeatherLoaded = false;
            oldCancellation.Cancel();
        }

        private async Task<WeatherInfo?> FetchAndCacheWeatherAsync(
            WeatherRequestContext context,
            CancellationToken requestCancellationToken)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                requestCancellationToken, context.ServiceCancellationToken);
            var cancellationToken = linkedCancellation.Token;
            try
            {
                WeatherInfo info;
                if (context.Source == "OpenMeteo" && context.Latitude != 0 && context.Longitude != 0)
                    info = await GetWeatherFromOpenMeteoAsync(context, cancellationToken);
                else
                    info = await GetWeatherFromWttrInAsync(context, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                string? persistJson = null;
                lock (_weatherLock)
                {
                    if (context.Generation == Volatile.Read(ref _weatherDataGeneration))
                    {
                        _cachedWeather = info;
                        _cachedWeatherKey = context.Key;
                        _lastFetchTime = DateTime.Now;
                        _lastFailureWeatherKey = null;
                        _lastFailureUtc = DateTimeOffset.MinValue;
                        _consecutiveFailures = 0;
                        _persistentWeatherLoaded = true;
                        persistJson = SerializePersistentWeatherLocked(context.LocationId);
                    }
                }
                if (persistJson != null)
                    await WritePersistentWeatherAsync(persistJson, context.Generation, CancellationToken.None);

                return context.Generation == Volatile.Read(ref _weatherDataGeneration) ? info : null;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                string? persistJson = null;
                lock (_weatherLock)
                {
                    if (context.Generation == Volatile.Read(ref _weatherDataGeneration))
                    {
                        if (_lastFailureWeatherKey == context.Key)
                            _consecutiveFailures = Math.Min(_consecutiveFailures + 1, 4);
                        else
                        {
                            _lastFailureWeatherKey = context.Key;
                            _consecutiveFailures = 1;
                        }
                        _lastFailureUtc = DateTimeOffset.UtcNow;
                        persistJson = SerializePersistentWeatherLocked(context.LocationId);
                    }
                }
                if (persistJson != null)
                    await WritePersistentWeatherAsync(persistJson, context.Generation, CancellationToken.None);
                System.Diagnostics.Debug.WriteLine($"Weather fetch failed: {ex.Message}");
            }

            lock (_weatherLock)
            {
                return _cachedWeatherKey == context.Key ? _cachedWeather : null;
            }
        }

        // Lazily hydrate the in-memory cache from disk on first use. Adopts the persisted
        // entry whenever it matches the current location (regardless of age) so there is
        // always something to show offline; the freshness check elsewhere drives refresh.
        private async Task TryLoadPersistentWeatherAsync(
            WeatherRequestContext context,
            CancellationToken cancellationToken)
        {
            lock (_weatherLock)
            {
                if (_persistentWeatherLoaded) return;
            }

            try
            {
                await _weatherPersistenceGate.WaitAsync(cancellationToken);
                string json;
                try
                {
                    json = await Task.Run(() =>
                    {
                        string? protectedJson = LocalSqliteStore.ReadProtectedText(ProtectedWeatherScope, ProtectedCacheKey);
                        if (!string.IsNullOrWhiteSpace(protectedJson))
                        {
                            TryDeleteLegacyWeatherCache();
                            return protectedJson;
                        }

                        string legacyJson = LocalSqliteStore.ReadProtectedText(ProtectedWeatherScope, LegacyProtectedCacheKey) ?? "";
                        var path = PersistentWeatherPath;
                        if (string.IsNullOrWhiteSpace(legacyJson) && File.Exists(path)) legacyJson = File.ReadAllText(path);
                        if (!string.IsNullOrWhiteSpace(legacyJson))
                        {
                            var old = JsonSerializer.Deserialize(legacyJson, AppJsonContext.Default.WeatherCacheEnvelope);
                            if (old != null)
                            {
                                old.LocationId = context.LocationId;
                                var migrated = new WeatherCacheStore { Entries = new List<WeatherCacheEnvelope> { old } };
                                legacyJson = JsonSerializer.Serialize(migrated, AppJsonContext.Default.WeatherCacheStore);
                                LocalSqliteStore.WriteProtectedText(ProtectedWeatherScope, ProtectedCacheKey, legacyJson);
                            }
                            TryDeleteLegacyWeatherCache();
                        }
                        return legacyJson;
                    }, cancellationToken);
                }
                finally
                {
                    _weatherPersistenceGate.Release();
                }
                if (string.IsNullOrWhiteSpace(json))
                {
                    lock (_weatherLock)
                    {
                        if (context.Generation == Volatile.Read(ref _weatherDataGeneration))
                            _persistentWeatherLoaded = true;
                    }
                    return;
                }

                var store = JsonSerializer.Deserialize(json, AppJsonContext.Default.WeatherCacheStore);
                if (store != null) _persistentCacheStore = store;
                var envelope = store?.Entries.FirstOrDefault(x => x.LocationId == context.LocationId)
                    ?? store?.Entries.FirstOrDefault(x => x.Key == context.Key);
                if (envelope == null) return;

                // Always adopt the saved entry as a baseline so the bar shows the last-known
                // weather even when offline. Staleness is handled downstream: GetWeatherAsync's
                // freshness check uses _lastFetchTime and still triggers a network refresh when
                // the entry is old; the fresh result replaces it once a connection is available.
                lock (_weatherLock)
                {
                    if (context.Generation != Volatile.Read(ref _weatherDataGeneration)) return;
                    _persistentWeatherLoaded = true;
                    _lastFailureWeatherKey = envelope.FailureKey;
                    _lastFailureUtc = envelope.FailureUtcTicks > 0
                        ? new DateTimeOffset(envelope.FailureUtcTicks, TimeSpan.Zero)
                        : DateTimeOffset.MinValue;
                    _consecutiveFailures = Math.Max(0, envelope.FailureCount);
                    if (envelope.Info != null && envelope.Key == context.Key && _cachedWeatherKey != context.Key)
                    {
                        _cachedWeather = envelope.Info;
                        _cachedWeatherKey = envelope.Key;
                        _lastFetchTime = new DateTime(envelope.FetchedTicks, DateTimeKind.Local);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                lock (_weatherLock) _persistentWeatherLoaded = false;
                throw;
            }
            catch (Exception ex)
            {
                lock (_weatherLock)
                {
                    if (context.Generation == Volatile.Read(ref _weatherDataGeneration))
                        _persistentWeatherLoaded = true;
                }
                System.Diagnostics.Debug.WriteLine($"Weather cache load failed: {ex.Message}");
            }
        }

        private string? SerializePersistentWeatherLocked(string locationId)
        {
            try
            {
                var envelope = _persistentCacheStore.Entries.FirstOrDefault(x => x.LocationId == locationId);
                if (envelope == null)
                {
                    envelope = new WeatherCacheEnvelope { LocationId = locationId };
                    _persistentCacheStore.Entries.Add(envelope);
                }
                envelope.Key = _cachedWeatherKey ?? "";
                envelope.FetchedTicks = _lastFetchTime.Ticks;
                envelope.Info = _cachedWeather;
                envelope.FailureKey = _lastFailureWeatherKey;
                envelope.FailureUtcTicks = _lastFailureUtc == DateTimeOffset.MinValue ? 0 : _lastFailureUtc.UtcTicks;
                envelope.FailureCount = _consecutiveFailures;
                var validIds = _favorites.Locations.Select(x => x.Id).ToHashSet();
                _persistentCacheStore.Entries = _persistentCacheStore.Entries
                    .Where(x => validIds.Contains(x.LocationId))
                    .OrderByDescending(x => x.LocationId == locationId)
                    .Take(WeatherLocationPolicy.MaxLocations)
                    .ToList();
                return JsonSerializer.Serialize(
                    _persistentCacheStore,
                    AppJsonContext.Default.WeatherCacheStore);
            }
            catch
            {
                return null;
            }
        }

        private async Task WritePersistentWeatherAsync(
            string json,
            long generation,
            CancellationToken cancellationToken)
        {
            await _weatherPersistenceGate.WaitAsync(cancellationToken);
            try
            {
                if (generation != Volatile.Read(ref _weatherDataGeneration)) return;
                await LocalSqliteStore.WriteProtectedTextAsync(ProtectedWeatherScope, ProtectedCacheKey, json);
                TryDeleteLegacyWeatherCache();
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Weather cache write failed: {ex.Message}"); }
            finally { _weatherPersistenceGate.Release(); }
        }

        private static void TryDeleteLegacyWeatherCache()
        {
            try
            {
                if (File.Exists(PersistentWeatherPath))
                    File.Delete(PersistentWeatherPath);
            }
            catch
            {
            }
        }

        public async Task ClearWeatherAndLocationDataAsync()
        {
            StopLocationTracking();
            lock (_contextLock)
            {
                ApplicationData.Current.LocalSettings.Values["WeatherAutoFollowLocation"] = false;
                ApplicationData.Current.LocalSettings.Values["WeatherEnabled"] = false;
                InvalidateWeatherContext();
            }

            lock (_locationLock)
            {
                _location = new WeatherLocationSettings();
                _favorites = new WeatherFavoritesStore();
                _locationLoaded = true;
                RemoveLegacyLocationSettings();
            }

            lock (_weatherLock)
            {
                _cachedWeather = null;
                _cachedWeatherKey = null;
                _lastFetchTime = DateTime.MinValue;
                _lastFailureWeatherKey = null;
                _lastFailureUtc = DateTimeOffset.MinValue;
                _consecutiveFailures = 0;
                _persistentWeatherLoaded = true;
                _persistentCacheStore = new WeatherCacheStore();
            }

            await _weatherPersistenceGate.WaitAsync();
            try
            {
                TryDeleteLegacyWeatherCache();
                await LocalSqliteStore.DeleteProtectedScopeAsync(ProtectedWeatherScope);
            }
            finally
            {
                _weatherPersistenceGate.Release();
            }
        }

        #region Open-Meteo Provider

        private async Task<WeatherInfo> GetWeatherFromOpenMeteoAsync(
            WeatherRequestContext context,
            CancellationToken cancellationToken)
        {
            string forecastUrl = $"https://api.open-meteo.com/v1/forecast?" +
                $"latitude={context.Latitude.ToString(CultureInfo.InvariantCulture)}&longitude={context.Longitude.ToString(CultureInfo.InvariantCulture)}" +
                $"&hourly=temperature_2m,relative_humidity_2m,apparent_temperature," +
                $"precipitation_probability,precipitation,weather_code," +
                $"surface_pressure,visibility,wind_speed_10m,wind_direction_10m,uv_index" +
                $"&daily=weather_code,temperature_2m_max,temperature_2m_min," +
                $"precipitation_sum,precipitation_probability_max,wind_speed_10m_max,uv_index_max,sunrise,sunset" +
                $"&timezone=auto&forecast_days=7";

            string aqUrl = $"https://air-quality-api.open-meteo.com/v1/air-quality?" +
                $"latitude={context.Latitude.ToString(CultureInfo.InvariantCulture)}&longitude={context.Longitude.ToString(CultureInfo.InvariantCulture)}" +
                $"&hourly=us_aqi,pm2_5,pm10,grass_pollen,birch_pollen,ragweed_pollen" +
                $"&forecast_days=2";

            var forecastTask = GetStringWithAgentAsync(forecastUrl, "TaskFlyout/1.0", cancellationToken);
            Task<string> aqTask;
            try { aqTask = GetStringWithAgentAsync(aqUrl, "TaskFlyout/1.0", cancellationToken); }
            catch { aqTask = Task.FromResult(""); }

            string forecastJson = await forecastTask;
            string? aqJson = null;
            try { aqJson = await aqTask; }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { }

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
                        var aqH = aqHourly.Clone();
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

            string sunrise = daily.GetProperty("sunrise")[0].GetString() ?? "";
            string sunset = daily.GetProperty("sunset")[0].GetString() ?? "";
            string sunriseTime = sunrise.Contains("T") ? sunrise.Split('T')[1] : sunrise;
            string sunsetTime = sunset.Contains("T") ? sunset.Split('T')[1] : sunset;

            var info = new WeatherInfo
            {
                City = context.City,
                IconFont = context.IconFont,
                Sunrise = sunriseTime,
                Sunset = sunsetTime,
                MoonPhase = GetMoonPhaseEmoji(DateTime.Today),
                HourlyForecast = new(),
                DailyForecast = new()
            };

            BuildOpenMeteoDailyForecast(info, daily, context.IconFont);

            int totalHours = times.GetArrayLength();
            int nowHour = DateTime.Now.Hour;

            // Build hourly data: 24 hours from current hour
            for (int i = 0; i < Math.Min(totalHours, 48); i++)
            {
                string timeStr = times[i].GetString() ?? "";
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
                    Icon = OpenMeteoCodeToIcon(code, context.IconFont),
                    IconFont = context.IconFont,
                    IconLayerUris = IconPackService.Instance.TryResolveBitmapLayers(code, omHourIsDay, isOpenMeteo: true),
                    Description = OpenMeteoCodeToDescription(code),
                    FeelsLike = $"{flVal:F0}°C",
                    Humidity = $"{humVal}%",
                    WindSpeed = $"{wsVal:F0} km/h",
                    WindDirection = WindDegreeToDirection(wdVal),
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

        private void BuildOpenMeteoDailyForecast(WeatherInfo info, JsonElement daily, string iconFont)
        {
            try
            {
                var dates = daily.GetProperty("time");
                var codes = daily.GetProperty("weather_code");
                var maxTemps = daily.GetProperty("temperature_2m_max");
                var minTemps = daily.GetProperty("temperature_2m_min");
                var precipSum = daily.GetProperty("precipitation_sum");
                var precipProb = daily.GetProperty("precipitation_probability_max");
                var windMax = daily.GetProperty("wind_speed_10m_max");
                var uvMax = daily.GetProperty("uv_index_max");

                int count = Math.Min(dates.GetArrayLength(), 7);
                for (int i = 0; i < count; i++)
                {
                    string dateText = dates[i].GetString() ?? "";
                    if (!DateTime.TryParse(dateText, out var date)) continue;

                    int code = codes[i].GetInt32();
                    double high = maxTemps[i].GetDouble();
                    double low = minTemps[i].GetDouble();
                    double rain = precipSum[i].ValueKind != JsonValueKind.Null ? precipSum[i].GetDouble() : 0;
                    double chance = precipProb[i].ValueKind != JsonValueKind.Null ? precipProb[i].GetDouble() : 0;
                    double wind = windMax[i].ValueKind != JsonValueKind.Null ? windMax[i].GetDouble() : 0;
                    double uv = uvMax[i].ValueKind != JsonValueKind.Null ? uvMax[i].GetDouble() : 0;

                    info.DailyForecast.Add(new DailyWeather
                    {
                        Date = date,
                        DayLabel = GetDailyDayLabel(date),
                        DateLabel = date.ToString(_loader.GetStringOrDefault("TextWeatherDateFormat") ?? "MMM d", LocalizationHelper.AppCulture),
                        WeatherCode = code,
                        HighTemperature = $"{high:F0}°",
                        LowTemperature = $"{low:F0}°",
                        TemperatureRange = $"{low:F0}° / {high:F0}°C",
                        Icon = OpenMeteoCodeToIcon(code, iconFont),
                        IconFont = iconFont,
                        IconLayerUris = IconPackService.Instance.TryResolveBitmapLayers(code, true, isOpenMeteo: true),
                        Description = OpenMeteoCodeToDescription(code),
                        PrecipProbability = $"{chance:F0}%",
                        Precipitation = $"{rain:F1} mm",
                        WindSpeed = $"{wind:F0} km/h",
                        UVIndex = $"{uv:F1}"
                    });
                }
            }
            catch { }
        }

        private static string GetDailyDayLabel(DateTime date)
        {
            int days = (date.Date - DateTime.Today).Days;
            if (days == 0) return _loader.GetStringOrDefault("TextToday") ?? "Today";
            if (days == 1) return _loader.GetStringOrDefault("TextTomorrow") ?? "Tomorrow";
            return date.ToString("ddd", LocalizationHelper.AppCulture);
        }

        private static string GetCurrentLanguage()
            => LocalizationHelper.SupportedLanguageCode;

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

        private static string OpenMeteoCodeToDescription(int code)
        {
            string key = code switch
            {
                0 => "WeatherCondition_ClearSky",
                1 => "WeatherCondition_MainlyClear",
                2 => "WeatherCondition_PartlyCloudy",
                3 => "WeatherCondition_Overcast",
                45 => "WeatherCondition_Fog",
                48 => "WeatherCondition_RimeFog",
                51 => "WeatherCondition_LightDrizzle",
                53 => "WeatherCondition_ModerateDrizzle",
                55 => "WeatherCondition_DenseDrizzle",
                56 or 57 => "WeatherCondition_FreezingDrizzle",
                61 => "WeatherCondition_SlightRain",
                63 => "WeatherCondition_ModerateRain",
                65 => "WeatherCondition_HeavyRain",
                66 or 67 => "WeatherCondition_FreezingRain",
                71 => "WeatherCondition_SlightSnow",
                73 => "WeatherCondition_ModerateSnow",
                75 => "WeatherCondition_HeavySnow",
                77 => "WeatherCondition_SnowGrains",
                80 => "WeatherCondition_SlightRainShowers",
                81 => "WeatherCondition_ModerateRainShowers",
                82 => "WeatherCondition_ViolentRainShowers",
                85 => "WeatherCondition_SlightSnowShowers",
                86 => "WeatherCondition_HeavySnowShowers",
                95 => "WeatherCondition_Thunderstorm",
                96 or 99 => "WeatherCondition_ThunderstormWithHail",
                _ => "WeatherCondition_Unknown"
            };
            return _loader.GetStringOrDefault(key)
                ?? _loader.GetStringOrDefault("WeatherCondition_Unknown")
                ?? "Unknown";
        }

        private static string WindDegreeToDirection(int deg)
        {
            string[] keys =
            {
                "WeatherWind_N", "WeatherWind_NE", "WeatherWind_E", "WeatherWind_SE",
                "WeatherWind_S", "WeatherWind_SW", "WeatherWind_W", "WeatherWind_NW"
            };
            int idx = ((int)Math.Round(deg / 45.0) % keys.Length + keys.Length) % keys.Length;
            return _loader.GetStringOrDefault(keys[idx]) ?? keys[idx]["WeatherWind_".Length..];
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

        private async Task<WeatherInfo> GetWeatherFromWttrInAsync(
            WeatherRequestContext context,
            CancellationToken cancellationToken)
        {
            string lang = GetCurrentLanguage();
            string wttrLang = lang == "en" ? "en" : "zh";
            string searchCity = context.ProviderCity.Split(',')[0].Trim();
            string url = $"https://wttr.in/{Uri.EscapeDataString(searchCity)}?format=j1&lang={wttrLang}";

            var response = await GetStringWithAgentAsync(url, "curl/7.68.0", cancellationToken);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            var current = root.GetProperty("current_condition")[0];
            string tempC = current.GetProperty("temp_C").GetString() ?? "";
            string weatherCode = current.GetProperty("weatherCode").GetString() ?? "";
            string humidityVal = current.TryGetProperty("humidity", out var h) ? h.GetString() ?? "" : "";
            string windVal = current.TryGetProperty("windspeedKmph", out var w) ? w.GetString() ?? "" : "";
            string flVal = current.TryGetProperty("FeelsLikeC", out var fl) ? fl.GetString() ?? "" : "";
            string visVal = current.TryGetProperty("visibility", out var vis) ? vis.GetString() ?? "" : "";
            string pressVal = current.TryGetProperty("pressure", out var pr) ? pr.GetString() ?? "" : "";
            string uvVal = current.TryGetProperty("uvIndex", out var uv) ? uv.GetString() ?? "" : "";

            string desc = "";
            if (wttrLang == "zh" && current.TryGetProperty("lang_zh", out var langZh) && langZh.GetArrayLength() > 0)
                desc = langZh[0].GetProperty("value").GetString() ?? "";
            else if (current.TryGetProperty("weatherDesc", out var wDesc) && wDesc.GetArrayLength() > 0)
                desc = wDesc[0].GetProperty("value").GetString() ?? "";

            int.TryParse(weatherCode, out int wttrRawCode);
            bool wttrIsDay = DateTime.Now.Hour >= 6 && DateTime.Now.Hour < 18;
            var wttrLayers = IconPackService.Instance.TryResolveBitmapLayers(wttrRawCode, wttrIsDay, isOpenMeteo: false);
            var info = new WeatherInfo
            {
                Temperature = $"{tempC}\u00B0C",
                Description = desc,
                Icon = WttrCodeToIcon(weatherCode, context.IconFont),
                IconFont = context.IconFont,
                IconLayerUris = wttrLayers,
                IconBitmapUri = wttrLayers.Length > 0 ? wttrLayers[0] : null,
                RawWeatherCode = wttrRawCode,
                IsDayTime = wttrIsDay,
                City = context.City,
                FeelsLike = string.IsNullOrEmpty(flVal) ? "" : $"{flVal}\u00B0C",
                Humidity = string.IsNullOrEmpty(humidityVal) ? "" : $"{humidityVal}%",
                WindSpeed = string.IsNullOrEmpty(windVal) ? "" : $"{windVal} km/h",
                UVIndex = uvVal,
                Visibility = string.IsNullOrEmpty(visVal) ? "" : $"{visVal} km",
                Pressure = string.IsNullOrEmpty(pressVal) ? "" : $"{pressVal} hPa"
            };

            // wttr.in provides a short daily forecast and 3-hourly data; interpolate hourly.
            if (root.TryGetProperty("weather", out var weatherArray) && weatherArray.GetArrayLength() > 0)
            {
                for (int d = 0; d < weatherArray.GetArrayLength(); d++)
                {
                    var day = weatherArray[d];
                    string dateRaw = day.TryGetProperty("date", out var dateEl) ? dateEl.GetString() ?? "" : "";
                    if (!DateTime.TryParse(dateRaw, out var date)) date = DateTime.Today.AddDays(d);

                    string maxC = day.TryGetProperty("maxtempC", out var maxEl) ? maxEl.GetString() ?? "" : "";
                    string minC = day.TryGetProperty("mintempC", out var minEl) ? minEl.GetString() ?? "" : "";
                    string dailyUv = day.TryGetProperty("uvIndex", out var duvEl) ? duvEl.GetString() ?? "" : "";
                    string sunHour = day.TryGetProperty("sunHour", out var sunEl) ? sunEl.GetString() ?? "" : "";

                    string dayCode = "";
                    string dayDesc = "";
                    int maxRainChance = 0;
                    string totalPrecip = "";
                    string maxWind = "";

                    if (day.TryGetProperty("hourly", out var dailyHourly) && dailyHourly.GetArrayLength() > 0)
                    {
                        var noon = dailyHourly.EnumerateArray()
                            .FirstOrDefault(h => h.TryGetProperty("time", out var t) && t.GetString() == "1200");
                        if (noon.ValueKind == JsonValueKind.Undefined)
                            noon = dailyHourly[dailyHourly.GetArrayLength() / 2];

                        dayCode = noon.TryGetProperty("weatherCode", out var dc) ? dc.GetString() ?? "" : "";
                        if (wttrLang == "zh" && noon.TryGetProperty("lang_zh", out var dz) && dz.GetArrayLength() > 0)
                            dayDesc = dz[0].GetProperty("value").GetString() ?? "";
                        else if (noon.TryGetProperty("weatherDesc", out var dd) && dd.GetArrayLength() > 0)
                            dayDesc = dd[0].GetProperty("value").GetString() ?? "";

                        foreach (var hour in dailyHourly.EnumerateArray())
                        {
                            if (hour.TryGetProperty("chanceofrain", out var rainChance)
                                && int.TryParse(rainChance.GetString(), out var chance)
                                && chance > maxRainChance)
                                maxRainChance = chance;

                            if (string.IsNullOrEmpty(totalPrecip) && hour.TryGetProperty("precipMM", out var precipEl))
                                totalPrecip = precipEl.GetString() ?? "";

                            if (hour.TryGetProperty("windspeedKmph", out var windEl)
                                && int.TryParse(windEl.GetString(), out var wind)
                                && (string.IsNullOrEmpty(maxWind) || int.Parse(maxWind) < wind))
                                maxWind = wind.ToString();
                        }
                    }

                    int.TryParse(dayCode, out int rawDailyCode);
                    info.DailyForecast.Add(new DailyWeather
                    {
                        Date = date,
                        DayLabel = GetDailyDayLabel(date),
                        DateLabel = date.ToString(_loader.GetStringOrDefault("TextWeatherDateFormat") ?? "MMM d", LocalizationHelper.AppCulture),
                        WeatherCode = rawDailyCode,
                        HighTemperature = string.IsNullOrEmpty(maxC) ? "" : $"{maxC}°",
                        LowTemperature = string.IsNullOrEmpty(minC) ? "" : $"{minC}°",
                        TemperatureRange = string.IsNullOrEmpty(minC) || string.IsNullOrEmpty(maxC) ? "" : $"{minC}° / {maxC}°C",
                        Icon = WttrCodeToIcon(dayCode, context.IconFont),
                        IconFont = context.IconFont,
                        IconLayerUris = IconPackService.Instance.TryResolveBitmapLayers(rawDailyCode, true, isOpenMeteo: false),
                        Description = dayDesc,
                        PrecipProbability = $"{maxRainChance}%",
                        Precipitation = string.IsNullOrEmpty(totalPrecip) ? "" : $"{totalPrecip} mm",
                        WindSpeed = string.IsNullOrEmpty(maxWind) ? "" : $"{maxWind} km/h",
                        UVIndex = string.IsNullOrEmpty(dailyUv) ? sunHour : dailyUv
                    });
                }

                var allHourlyRaw = new List<(int hour, string tempC, string code, string hDesc,
                    string hum, string ws, string flC, string pp, string uvi, string visKm, string press)>();

                for (int d = 0; d < Math.Min(weatherArray.GetArrayLength(), 2); d++)
                {
                    var day = weatherArray[d];
                    if (!day.TryGetProperty("hourly", out var hourlyArray)) continue;
                    foreach (var hour in hourlyArray.EnumerateArray())
                    {
                        string timeRaw = hour.GetProperty("time").GetString() ?? "0";
                        int hourVal = int.TryParse(timeRaw, out var parsedHour) ? parsedHour / 100 : 0;

                        string hd = "";
                        if (wttrLang == "zh" && hour.TryGetProperty("lang_zh", out var hLang) && hLang.GetArrayLength() > 0)
                            hd = hLang[0].GetProperty("value").GetString() ?? "";
                        else if (hour.TryGetProperty("weatherDesc", out var hdesc) && hdesc.GetArrayLength() > 0)
                            hd = hdesc[0].GetProperty("value").GetString() ?? "";

                        allHourlyRaw.Add((
                            hourVal + d * 24,
                            hour.GetProperty("tempC").GetString() ?? "",
                            hour.GetProperty("weatherCode").GetString() ?? "",
                            hd,
                            hour.TryGetProperty("humidity", out var hh) ? hh.GetString() ?? "" : "",
                            hour.TryGetProperty("windspeedKmph", out var ww) ? ww.GetString() ?? "" : "",
                            hour.TryGetProperty("FeelsLikeC", out var ff) ? ff.GetString() ?? "" : "",
                            hour.TryGetProperty("chanceofrain", out var cr) ? cr.GetString() ?? "" : "",
                            hour.TryGetProperty("uvIndex", out var ui) ? ui.GetString() ?? "" : "",
                            hour.TryGetProperty("visibility", out var vv) ? vv.GetString() ?? "" : "",
                            hour.TryGetProperty("pressure", out var pp) ? pp.GetString() ?? "" : ""
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
                        WeatherCode = WeatherConditionCodePolicy.WttrToOpenMeteo(rawCode),
                        TempValue = rawTemp,
                        WindSpeedValue = rawWs,
                        PrecipProbValue = rawPp,
                        Time = $"{displayHour:D2}:00",
                        Temperature = $"{source.tempC}\u00B0C",
                        Icon = WttrCodeToIcon(source.code, context.IconFont),
                        IconFont = context.IconFont,
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

        public static readonly (string Key, string ResourceKey, string EnLabel, string Glyph)[] AllFlyoutFields = new[]
        {
            ("temperature",    "WeatherField_Temperature",   "Temperature",    "\uE9CA"),
            ("feelslike",      "WeatherField_FeelsLike",     "Feels like",     "\uE9CA"),
            ("description",    "WeatherField_Description",   "Description",    "\uE286"),
            ("humidity",       "WeatherField_Humidity",      "Humidity",       "\uE945"),
            ("wind",           "WeatherField_Wind",          "Wind",           "\uEBE7"),
            ("precipitation",  "WeatherField_Precipitation", "Precipitation",  "\uE790"),
            ("uv",             "WeatherField_UVIndex",       "UV Index",       "\uE706"),
            ("visibility",     "WeatherField_Visibility",    "Visibility",     "\uE7B3"),
            ("pressure",       "WeatherField_Pressure",      "Pressure",       "\uEC49"),
            ("airquality",     "WeatherField_AirQuality",    "Air Quality",    "\uE9CA"),
            ("pollen",         "WeatherField_Pollen",        "Pollen",         "\uE710"),
            ("sun",            "WeatherField_Sun",           "Sunrise/Sunset", "\uE706"),
            ("moon",           "WeatherField_Moon",          "Moon Phase",     "\uE708"),
        };
    }
}
