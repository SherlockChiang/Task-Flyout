using System;
using System.Collections.Generic;

namespace Task_Flyout.Services
{
    /// <summary>
    /// Small adapter around LocalSettings.Values. Keeping it dictionary-based makes the
    /// migration deterministic and unit-testable without starting a packaged application.
    /// </summary>
    internal static class WeatherBarModeSettings
    {
        public const string SettingKey = "WeatherBarMode";

        public static WeatherBarMode Read(IDictionary<string, object> values)
        {
            ArgumentNullException.ThrowIfNull(values);
            values.TryGetValue(SettingKey, out object? persistedValue);
            return WeatherBarModePolicy.Parse(persistedValue as string);
        }

        public static WeatherBarMode ReadAndMigrate(IDictionary<string, object> values)
        {
            WeatherBarMode mode = Read(values);
            string canonicalValue = WeatherBarModePolicy.Serialize(mode);
            if (!values.TryGetValue(SettingKey, out object? persistedValue) ||
                !string.Equals(persistedValue as string, canonicalValue, StringComparison.Ordinal))
            {
                values[SettingKey] = canonicalValue;
            }

            return mode;
        }

        public static void Write(IDictionary<string, object> values, WeatherBarMode mode)
        {
            ArgumentNullException.ThrowIfNull(values);
            values[SettingKey] = WeatherBarModePolicy.Serialize(mode);
        }
    }
}
