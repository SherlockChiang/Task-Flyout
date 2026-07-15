using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class DiagnosticEventFormatterTests
{
    [Fact]
    public void Formats_exception_without_message_stack_or_sensitive_values()
    {
        var exception = new InvalidOperationException(
            "Request failed for user@example.com at https://example.test/private?api_key=secret",
            new IOException("C:\\Users\\person\\private.txt"));

        var result = DiagnosticEventFormatter.FormatException(
            "rss.refresh",
            exception,
            DateTimeOffset.Parse("2026-07-15T10:00:00Z"),
            "11111111-2222-3333-4444-555555555555");

        Assert.Contains("operation=rss.refresh", result);
        Assert.Contains("exception_0_type=System.InvalidOperationException", result);
        Assert.Contains("exception_1_type=System.IO.IOException", result);
        Assert.Contains("correlation_id=11111111222233334444555555555555", result);
        Assert.DoesNotContain("user@example.com", result);
        Assert.DoesNotContain("api_key", result);
        Assert.DoesNotContain("private.txt", result);
        Assert.DoesNotContain(" at ", result);
    }

    [Fact]
    public void Normalizes_untrusted_operation_name()
    {
        var result = DiagnosticEventFormatter.FormatException(
            "rss\nemail=user@example.com",
            new Exception("secret"),
            correlationId: "not-a-guid");

        Assert.Contains("operation=rssemailuserexample.com", result);
        Assert.DoesNotContain("user@example.com", result);
        Assert.Matches("correlation_id=[0-9a-f]{32}", result);
    }

    [Fact]
    public void Bounds_inner_exception_depth()
    {
        Exception exception = new Exception("level 5");
        for (int index = 4; index >= 1; index--)
            exception = new Exception($"level {index}", exception);

        var result = DiagnosticEventFormatter.FormatException("crash", exception);

        Assert.Contains("exception_depth=4", result);
        Assert.DoesNotContain("exception_4_type", result);
    }
}
