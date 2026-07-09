using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class RecurrencePolicyTests
{
    [Theory]
    [InlineData("Daily", "DAILY")]
    [InlineData("Weekly", "WEEKLY")]
    [InlineData("Monthly", "MONTHLY")]
    [InlineData("Yearly", "YEARLY")]
    [InlineData("None", "DAILY")]
    [InlineData(null, "DAILY")]
    public void ToGoogleFrequency_maps_create_event_values(string? recurrence, string expected)
    {
        Assert.Equal(expected, RecurrencePolicy.ToGoogleFrequency(recurrence));
    }

    [Theory]
    [InlineData("RRULE:FREQ=DAILY", "Daily")]
    [InlineData("RRULE:FREQ=WEEKLY;INTERVAL=1", "Weekly")]
    [InlineData("RRULE:FREQ=MONTHLY", "Monthly")]
    [InlineData("RRULE:FREQ=YEARLY", "Yearly")]
    [InlineData("RRULE:FREQ=HOURLY", "None")]
    public void ToDisplayKindFromGoogleRRules_maps_supported_frequency(string rule, string expected)
    {
        Assert.Equal(expected, RecurrencePolicy.ToDisplayKindFromGoogleRRules(new[] { rule }));
    }

    [Fact]
    public void ToDisplayKindFromGoogleRRules_ignores_non_rrule_entries()
    {
        Assert.Equal("None", RecurrencePolicy.ToDisplayKindFromGoogleRRules(new[] { "EXDATE:20260709" }));
    }

    [Theory]
    [InlineData("Daily", "Daily")]
    [InlineData("Weekly", "Weekly")]
    [InlineData("AbsoluteMonthly", "Monthly")]
    [InlineData("RelativeMonthly", "Monthly")]
    [InlineData("AbsoluteYearly", "Yearly")]
    [InlineData("RelativeYearly", "Yearly")]
    [InlineData("", "None")]
    [InlineData(null, "None")]
    public void ToDisplayKindFromMicrosoftPattern_maps_supported_pattern_types(string? patternType, string expected)
    {
        Assert.Equal(expected, RecurrencePolicy.ToDisplayKindFromMicrosoftPattern(patternType));
    }
}
