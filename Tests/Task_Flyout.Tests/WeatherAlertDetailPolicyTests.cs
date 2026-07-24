using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class WeatherAlertDetailPolicyTests
{
    [Theory]
    [InlineData(WeatherAlertType.HeavyRain, "precipitation probability >= 50%")]
    [InlineData(WeatherAlertType.HighWind, "wind >= 40 km/h")]
    [InlineData(WeatherAlertType.ExtremeHeat, "temperature >= 35 C")]
    [InlineData(WeatherAlertType.ExtremeCold, "temperature <= -10 C")]
    public void Threshold_matches_detection_rule(WeatherAlertType type, string expected)
        => Assert.Equal(expected, WeatherAlertDetailPolicy.GetThreshold(type));

    [Fact]
    public void Detail_values_include_observations_and_threshold()
    {
        var alert = new WeatherAlert
        {
            Type = WeatherAlertType.HighWind,
            PrecipProbability = 20,
            WindSpeed = 48,
            Temperature = 12
        };

        var values = WeatherAlertDetailPolicy.FormatValues(alert, "Chance", "Wind", "Temp", "Threshold");

        Assert.Equal(new[]
        {
            "Chance: 20%", "Wind: 48 km/h", "Temp: 12 C", "Threshold: wind >= 40 km/h"
        }, values);
    }
}
