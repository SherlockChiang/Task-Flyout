using System;
using System.Collections.Generic;
using System.Linq;

namespace Task_Flyout.Services
{
    public sealed class WeatherLocationSettings
    {
        public string City { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    public sealed class SavedWeatherLocation
    {
        public string Id { get; set; } = "";
        public string Alias { get; set; } = "";
        public string DisplayLabel { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public bool IsCurrentLocation { get; set; }

        public string Label => string.IsNullOrWhiteSpace(Alias) ? DisplayLabel : Alias;
    }

    public sealed class WeatherFavoritesStore
    {
        public List<SavedWeatherLocation> Locations { get; set; } = new();
        public string ActiveId { get; set; } = "";
    }

    internal static class WeatherLocationPolicy
    {
        public const int MaxLocations = 5;

        public static WeatherLocationSettings Normalize(string? city, double latitude, double longitude)
            => new()
            {
                City = city?.Trim() ?? "",
                Latitude = double.IsFinite(latitude) && latitude is >= -90 and <= 90 ? latitude : 0,
                Longitude = double.IsFinite(longitude) && longitude is >= -180 and <= 180 ? longitude : 0
            };

        public static bool HasPersistableData(WeatherLocationSettings location)
            => !string.IsNullOrWhiteSpace(location.City) || location.Latitude != 0 || location.Longitude != 0;

        public static WeatherFavoritesStore Normalize(WeatherFavoritesStore? store)
        {
            var normalized = new WeatherFavoritesStore();
            var source = store?.Locations ?? new List<SavedWeatherLocation>();
            var ordered = source
                .OrderByDescending(location => location.Id == store?.ActiveId)
                .ThenBy(location => source.IndexOf(location));
            foreach (var location in ordered)
            {
                if (normalized.Locations.Count == MaxLocations) break;
                if (string.IsNullOrWhiteSpace(location.Id) || normalized.Locations.Any(x => x.Id == location.Id)) continue;
                var coordinates = Normalize(location.DisplayLabel, location.Latitude, location.Longitude);
                if (string.IsNullOrWhiteSpace(coordinates.City) || !HasValidCoordinates(location.Latitude, location.Longitude)) continue;
                if (normalized.Locations.Any(existing => SameCoordinates(existing.Latitude, existing.Longitude, coordinates.Latitude, coordinates.Longitude))) continue;
                normalized.Locations.Add(new SavedWeatherLocation
                {
                    Id = location.Id.Trim(),
                    Alias = location.Alias?.Trim() ?? "",
                    DisplayLabel = coordinates.City,
                    Latitude = coordinates.Latitude,
                    Longitude = coordinates.Longitude,
                    IsCurrentLocation = location.IsCurrentLocation
                });
            }
            normalized.ActiveId = normalized.Locations.Any(x => x.Id == store?.ActiveId)
                ? store!.ActiveId
                : normalized.Locations.FirstOrDefault()?.Id ?? "";
            return normalized;
        }

        public static bool TryAdd(WeatherFavoritesStore store, string displayLabel, double latitude, double longitude, bool current, out SavedWeatherLocation? location)
        {
            var normalized = Normalize(displayLabel, latitude, longitude);
            if (string.IsNullOrWhiteSpace(normalized.City) || !HasValidCoordinates(latitude, longitude))
            {
                location = null;
                return false;
            }

            if (current)
            {
                location = store.Locations.FirstOrDefault(x => x.IsCurrentLocation);
                if (location != null)
                {
                    location.DisplayLabel = normalized.City;
                    location.Latitude = normalized.Latitude;
                    location.Longitude = normalized.Longitude;
                    store.ActiveId = location.Id;
                    return true;
                }
            }
            location = store.Locations.FirstOrDefault(existing =>
                SameCoordinates(existing.Latitude, existing.Longitude, normalized.Latitude, normalized.Longitude));
            if (location != null)
            {
                location.DisplayLabel = normalized.City;
                store.ActiveId = location.Id;
                return true;
            }
            if (store.Locations.Count >= MaxLocations)
            {
                location = null;
                return false;
            }
            location = new SavedWeatherLocation
            {
                Id = Guid.NewGuid().ToString("N"),
                DisplayLabel = normalized.City,
                Latitude = normalized.Latitude,
                Longitude = normalized.Longitude,
                IsCurrentLocation = current
            };
            store.Locations.Add(location);
            store.ActiveId = location.Id;
            return true;
        }

        private static bool HasValidCoordinates(double latitude, double longitude)
            => double.IsFinite(latitude) && latitude is >= -90 and <= 90
                && double.IsFinite(longitude) && longitude is >= -180 and <= 180;

        private static bool SameCoordinates(double leftLatitude, double leftLongitude, double rightLatitude, double rightLongitude)
            => Math.Abs(leftLatitude - rightLatitude) < 0.000001
                && Math.Abs(leftLongitude - rightLongitude) < 0.000001;

        public static bool Remove(WeatherFavoritesStore store, string id)
        {
            int index = store.Locations.FindIndex(x => x.Id == id);
            if (index < 0) return false;
            store.Locations.RemoveAt(index);
            if (store.ActiveId == id)
                store.ActiveId = store.Locations.Count == 0 ? "" : store.Locations[Math.Min(index, store.Locations.Count - 1)].Id;
            return true;
        }
    }
}
