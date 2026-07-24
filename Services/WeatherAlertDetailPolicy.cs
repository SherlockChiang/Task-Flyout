using System;
using System.Collections.Generic;

namespace Task_Flyout.Services
{
    internal static class WeatherAlertDetailPolicy
    {
        public static string GetThreshold(WeatherAlertType type) => type switch
        {
            WeatherAlertType.HeavyRain => "precipitation probability >= 50%",
            WeatherAlertType.Rain => "precipitation probability >= 50%",
            WeatherAlertType.HighWind => "wind >= 40 km/h",
            WeatherAlertType.ExtremeHeat => "temperature >= 35 C",
            WeatherAlertType.ExtremeCold => "temperature <= -10 C",
            _ => "forecast condition code"
        };

        public static IReadOnlyList<string> FormatValues(WeatherAlert alert, string precipitationLabel, string windLabel, string temperatureLabel, string thresholdLabel, string? thresholdValue = null)
        {
            var values = new List<string>();
            if (alert.PrecipProbability.HasValue) values.Add($"{precipitationLabel}: {alert.PrecipProbability.Value:F0}%");
            if (alert.WindSpeed.HasValue) values.Add($"{windLabel}: {alert.WindSpeed.Value:F0} km/h");
            if (alert.Temperature.HasValue) values.Add($"{temperatureLabel}: {alert.Temperature.Value:F0} C");
            values.Add($"{thresholdLabel}: {thresholdValue ?? GetThreshold(alert.Type)}");
            return values;
        }
    }
}
