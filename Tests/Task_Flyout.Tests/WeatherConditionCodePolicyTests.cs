using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class WeatherConditionCodePolicyTests
{
    [Theory]
    [InlineData(200, 95)]
    [InlineData(308, 65)]
    [InlineData(311, 66)]
    [InlineData(338, 75)]
    [InlineData(248, 45)]
    [InlineData(113, 0)]
    public void Wttr_codes_map_to_alert_detection_codes(int wttr, int openMeteo)
        => Assert.Equal(openMeteo, WeatherConditionCodePolicy.WttrToOpenMeteo(wttr));
}
