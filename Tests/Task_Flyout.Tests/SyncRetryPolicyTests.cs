using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class SyncRetryPolicyTests
{
    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(599)]
    public void Retries_transient_http_statuses(int statusCode)
    {
        Assert.True(SyncRetryPolicy.ShouldRetryHttpStatus(statusCode, 0));
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(600)]
    public void Does_not_retry_permanent_http_statuses(int statusCode)
    {
        Assert.False(SyncRetryPolicy.ShouldRetryHttpStatus(statusCode, 0));
    }

    [Fact]
    public void Stops_after_maximum_retry_count()
    {
        Assert.False(SyncRetryPolicy.ShouldRetryHttpStatus(429, SyncRetryPolicy.MaximumRetryCount));
    }

    [Fact]
    public void Uses_bounded_exponential_delays()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(500), SyncRetryPolicy.GetDelay(0));
        Assert.Equal(TimeSpan.FromSeconds(1), SyncRetryPolicy.GetDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(2), SyncRetryPolicy.GetDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(2), SyncRetryPolicy.GetDelay(20));
    }
}
