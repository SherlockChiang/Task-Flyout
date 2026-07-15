using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class WeatherLocationPolicyTests
{
    [Fact]
    public void Normalizes_legacy_location_values()
    {
        var location = WeatherLocationPolicy.Normalize("  London  ", 51.5072, -0.1276);

        Assert.Equal("London", location.City);
        Assert.Equal(51.5072, location.Latitude);
        Assert.Equal(-0.1276, location.Longitude);
        Assert.True(WeatherLocationPolicy.HasPersistableData(location));
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    public void Rejects_invalid_latitude(double latitude, double expected)
    {
        Assert.Equal(expected, WeatherLocationPolicy.Normalize("", latitude, 0).Latitude);
    }

    [Fact]
    public void Empty_location_is_not_persistable()
    {
        Assert.False(WeatherLocationPolicy.HasPersistableData(WeatherLocationPolicy.Normalize("  ", 0, 0)));
    }
}
