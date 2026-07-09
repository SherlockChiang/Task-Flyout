using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class StatusMessageFormatterTests
{
    [Fact]
    public void Format_returns_message_when_last_success_not_requested()
    {
        var lastSuccess = new DateTimeOffset(2026, 7, 9, 12, 30, 0, TimeSpan.Zero);

        Assert.Equal("Load failed", StatusMessageFormatter.Format("Load failed", lastSuccess, includeLastSuccess: false));
    }

    [Fact]
    public void Format_appends_last_success_when_requested()
    {
        var lastSuccess = new DateTimeOffset(2026, 7, 9, 12, 30, 0, TimeSpan.Zero);
        var result = StatusMessageFormatter.Format("Load failed", lastSuccess, includeLastSuccess: true);

        Assert.Contains("Load failed", result);
        Assert.Contains("Last success:", result);
    }

    [Fact]
    public void Format_handles_null_message()
    {
        Assert.Equal("", StatusMessageFormatter.Format(null!, null, includeLastSuccess: true));
    }
}
