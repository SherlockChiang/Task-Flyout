using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class StatusMessageFormatterTests
{
    [Fact]
    public void Format_returns_message_when_last_success_not_requested()
    {
        var lastSuccess = new DateTimeOffset(2026, 7, 9, 12, 30, 0, TimeSpan.Zero);

        Assert.Equal(
            "Load failed",
            StatusMessageFormatter.Format(
                "Load failed",
                lastSuccess,
                includeLastSuccess: false,
                "Last success: {0:g}"));
    }

    [Fact]
    public void Format_appends_last_success_when_requested()
    {
        var lastSuccess = new DateTimeOffset(2026, 7, 9, 12, 30, 0, TimeSpan.Zero);
        var result = StatusMessageFormatter.Format(
            "Load failed",
            lastSuccess,
            includeLastSuccess: true,
            "Last success: {0:g}");

        Assert.Contains("Load failed", result);
        Assert.Contains("Last success:", result);
    }

    [Fact]
    public void Format_handles_null_message()
    {
        Assert.Equal(
            "",
            StatusMessageFormatter.Format(
                null!,
                null,
                includeLastSuccess: true,
                "Last success: {0:g}"));
    }

    [Fact]
    public void Format_uses_the_supplied_localized_suffix()
    {
        var lastSuccess = new DateTimeOffset(new DateTime(2026, 7, 9, 12, 30, 0, DateTimeKind.Local));

        var result = StatusMessageFormatter.Format(
            "载入失败",
            lastSuccess,
            includeLastSuccess: true,
            "最近成功：{0:yyyy-MM-dd HH:mm}");

        Assert.Equal("载入失败 · 最近成功：2026-07-09 12:30", result);
    }
}
