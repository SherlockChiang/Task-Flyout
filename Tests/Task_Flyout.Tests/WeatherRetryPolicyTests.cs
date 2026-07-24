using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class WeatherRetryPolicyTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 5)]
    [InlineData(4, 15)]
    [InlineData(20, 15)]
    public void GetDelay_UsesCappedSchedule(int failures, int expectedMinutes)
        => Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), WeatherRetryPolicy.GetDelay(failures));

    [Fact]
    public void IsBackedOff_RequiresMatchingKey()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.False(WeatherRetryPolicy.IsBackedOff("new", "old", now.UtcTicks, 4, now));
    }

    [Fact]
    public void IsBackedOff_IgnoresInvalidPersistedTimestamp()
        => Assert.False(WeatherRetryPolicy.IsBackedOff(
            "key", "key", long.MaxValue, 4, DateTimeOffset.UtcNow));

    [Theory]
    [InlineData(1, 59, true)]
    [InlineData(1, 60, false)]
    [InlineData(2, 119, true)]
    [InlineData(2, 120, false)]
    [InlineData(3, 299, true)]
    [InlineData(3, 300, false)]
    [InlineData(4, 899, true)]
    [InlineData(4, 900, false)]
    public void IsBackedOff_ExpiresAtScheduledBoundary(int failures, int elapsedSeconds, bool expected)
    {
        var failedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(expected, WeatherRetryPolicy.IsBackedOff(
            "key", "key", failedAt.UtcTicks, failures, failedAt.AddSeconds(elapsedSeconds)));
    }
}
