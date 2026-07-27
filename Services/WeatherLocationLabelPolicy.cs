using System;
using System.Collections.Generic;
using System.Linq;

namespace Task_Flyout.Services
{
    internal static class WeatherLocationLabelPolicy
    {
        public static string FormatProvinceCity(string? province, string? city)
        {
            province = province?.Trim();
            city = city?.Trim();
            if (string.IsNullOrWhiteSpace(province)) return city ?? string.Empty;
            if (string.IsNullOrWhiteSpace(city)) return province;
            if (string.Equals(province, city, StringComparison.OrdinalIgnoreCase)
                || province.Contains(city, StringComparison.OrdinalIgnoreCase)
                || city.Contains(province, StringComparison.OrdinalIgnoreCase))
                return city.Length <= province.Length ? city : province;
            return $"{province} · {city}";
        }

        public static string FormatDetailed(
            string? province,
            string? city,
            string? district,
            string? township,
            string? street)
        {
            var specific = DistinctParts(district, township, street);
            if (specific.Count >= 2) return string.Join(" · ", specific.TakeLast(2));
            if (specific.Count == 1)
            {
                var cityParts = DistinctParts(city, specific[0]);
                return string.Join(" · ", cityParts.TakeLast(2));
            }
            return FormatProvinceCity(province, city);
        }

        public static string FormatForWeatherBar(string? location, int maximumLength = 18)
        {
            if (string.IsNullOrWhiteSpace(location)) return string.Empty;
            string text = location.Trim();
            var parts = text.Split('·', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string mostSpecific = parts.Length > 0 ? parts[^1] : text;
            if (mostSpecific.Length <= maximumLength) return mostSpecific;
            return mostSpecific[..Math.Max(1, maximumLength - 1)] + "…";
        }

        private static List<string> DistinctParts(params string?[] values)
        {
            var result = new List<string>();
            foreach (string value in values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()))
            {
                if (result.Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)
                    || existing.Contains(value, StringComparison.OrdinalIgnoreCase)
                    || value.Contains(existing, StringComparison.OrdinalIgnoreCase)))
                    continue;
                result.Add(value);
            }
            return result;
        }
    }
}
