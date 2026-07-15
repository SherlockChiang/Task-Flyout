using System;

namespace Task_Flyout.Services
{
    public sealed class WeatherLocationSettings
    {
        public string City { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    internal static class WeatherLocationPolicy
    {
        public static WeatherLocationSettings Normalize(string? city, double latitude, double longitude)
            => new()
            {
                City = city?.Trim() ?? "",
                Latitude = double.IsFinite(latitude) && latitude is >= -90 and <= 90 ? latitude : 0,
                Longitude = double.IsFinite(longitude) && longitude is >= -180 and <= 180 ? longitude : 0
            };

        public static bool HasPersistableData(WeatherLocationSettings location)
            => !string.IsNullOrWhiteSpace(location.City) || location.Latitude != 0 || location.Longitude != 0;
    }
}
