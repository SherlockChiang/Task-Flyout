using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class UserSafeErrorMessageTests
{
    [Fact]
    public void Format_redacts_sensitive_values()
    {
        var result = UserSafeErrorMessage.Format("Fetch failed: https://user:pass@example.com/?access_token=secret");

        Assert.Contains("[redacted]", result);
        Assert.DoesNotContain("user:pass", result);
        Assert.DoesNotContain("secret", result);
    }

    [Fact]
    public void Format_collapses_whitespace()
    {
        var result = UserSafeErrorMessage.Format("Line 1\r\n\tLine 2");

        Assert.Equal("Line 1 Line 2", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Format_uses_fallback_for_empty_messages(string? message)
    {
        var result = UserSafeErrorMessage.Format(message, fallback: "Try again.");

        Assert.Equal("Try again.", result);
    }

    [Fact]
    public void Format_truncates_long_messages()
    {
        var result = UserSafeErrorMessage.Format(new string('a', 20), maxLength: 8);

        Assert.Equal("aaaaaaaa...", result);
    }

    [Fact]
    public void FromException_formats_exception_message()
    {
        var result = UserSafeErrorMessage.FromException(new InvalidOperationException("Bad token=abc"));

        Assert.Contains("token=[redacted]", result);
        Assert.DoesNotContain("abc", result);
    }
}
