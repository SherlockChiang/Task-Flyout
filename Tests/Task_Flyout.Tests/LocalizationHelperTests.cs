using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class LocalizationHelperTests
{
    [Theory]
    [InlineData("zh-CN", "zh")]
    [InlineData("zh-Hans", "zh")]
    [InlineData("en-US", "en")]
    [InlineData("fr-FR", "en")]
    [InlineData("ja-JP", "en")]
    [InlineData(null, "en")]
    public void Unsupported_weather_languages_fall_back_to_english(string? language, string expected)
    {
        Assert.Equal(expected, LocalizationHelper.GetSupportedLanguageCode(language));
    }

    [Theory]
    [InlineData(DayOfWeek.Sunday, DayOfWeek.Sunday, 0)]
    [InlineData(DayOfWeek.Monday, DayOfWeek.Sunday, 1)]
    [InlineData(DayOfWeek.Sunday, DayOfWeek.Monday, 6)]
    [InlineData(DayOfWeek.Wednesday, DayOfWeek.Monday, 2)]
    public void Calculates_day_offset_from_culture_week_start(
        DayOfWeek day,
        DayOfWeek firstDayOfWeek,
        int expected)
    {
        Assert.Equal(expected, LocalizationHelper.GetDayOffset(day, firstDayOfWeek));
    }

    [Fact]
    public void Calculates_monday_first_visible_calendar_start()
    {
        var firstOfMonth = new DateTime(2026, 8, 1); // Saturday

        var start = LocalizationHelper.GetWeekStart(firstOfMonth, DayOfWeek.Monday);

        Assert.Equal(new DateTime(2026, 7, 27), start);
    }
}
