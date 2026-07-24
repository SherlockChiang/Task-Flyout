using System;

namespace Task_Flyout.Services
{
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
        public int? MinutesUntilEnd { get; set; }
        public string Icon { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public double? PrecipProbability { get; set; }
        public double? WindSpeed { get; set; }
        public double? Temperature { get; set; }
    }
}
