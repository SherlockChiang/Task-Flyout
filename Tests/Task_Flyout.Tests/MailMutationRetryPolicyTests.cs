using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class MailMutationRetryPolicyTests
{
    [Theory]
    [InlineData(408, true)]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(503, true)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(404, false)]
    public void Retry_only_transient_http_statuses(int statusCode, bool expected)
    {
        Assert.Equal(expected, MailMutationRetryPolicy.IsTransientStatusCode(statusCode));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(7, 64)]
    [InlineData(20, 64)]
    public void Retry_delay_uses_capped_exponential_backoff(int failureCount, int expectedMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), MailMutationRetryPolicy.GetRetryDelay(failureCount));
    }

    [Fact]
    public void Pending_mutation_expires_after_seven_days()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.False(MailMutationRetryPolicy.IsExpired(now.Subtract(TimeSpan.FromDays(6)).UtcTicks, now));
        Assert.True(MailMutationRetryPolicy.IsExpired(now.Subtract(TimeSpan.FromDays(8)).UtcTicks, now));
        Assert.True(MailMutationRetryPolicy.IsExpired(0, now));
    }
}
