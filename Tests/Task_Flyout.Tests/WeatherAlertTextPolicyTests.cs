using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class WeatherAlertTextPolicyTests
{
    [Theory]
    [InlineData("雷阵雨", 0, "雷阵雨")]
    [InlineData("Thunderstorm", 0, "Thunderstorm")]
    [InlineData("大雪", 2, "大雪 · 2h")]
    [InlineData("Extreme cold", 12, "Extreme cold · 12h")]
    public void Compact_text_keeps_type_and_only_adds_future_hours(string label, int hoursAhead, string expected)
        => Assert.Equal(expected, WeatherAlertTextPolicy.FormatCompact(label, hoursAhead));

    [Fact]
    public void Compact_text_normalizes_whitespace_and_negative_hours()
        => Assert.Equal("Fog", WeatherAlertTextPolicy.FormatCompact("  Fog  ", -1));

    [Theory]
    [InlineData(3, "降雨 · ≤5m")]
    [InlineData(35, "降雨 · 35m")]
    [InlineData(115, "降雨 · 2h")]
    public void Current_rain_keeps_a_compact_end_time(int minutesUntilEnd, string expected)
        => Assert.Equal(expected, WeatherAlertTextPolicy.FormatCompact("降雨", 0, minutesUntilEnd));
}
