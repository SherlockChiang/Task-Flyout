using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class DiagnosticsRedactorTests
{
    [Fact]
    public void Redacts_bearer_tokens()
    {
        var result = DiagnosticsRedactor.Redact("Authorization: Bearer abc.def.ghi");

        Assert.Contains("Bearer [redacted]", result);
        Assert.DoesNotContain("abc.def.ghi", result);
    }

    [Theory]
    [InlineData("https://example.com/callback?code=secret-code&state=ok")]
    [InlineData("https://example.com/callback?access_token=secret-token#frag")]
    [InlineData("https://example.com/callback?client_secret=secret&x=1")]
    public void Redacts_sensitive_query_values(string input)
    {
        var result = DiagnosticsRedactor.Redact(input);

        Assert.Contains("[redacted]", result);
        Assert.DoesNotContain("secret-code", result, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-token", result, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("client_secret=secret", result, System.StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("password=hunter2")]
    [InlineData("refresh_token:abcd")]
    [InlineData("client_secret=topsecret")]
    public void Redacts_sensitive_key_value_pairs(string input)
    {
        var result = DiagnosticsRedactor.Redact(input);

        Assert.Contains("[redacted]", result);
        Assert.DoesNotContain("hunter2", result);
        Assert.DoesNotContain("abcd", result);
        Assert.DoesNotContain("topsecret", result);
    }
}
