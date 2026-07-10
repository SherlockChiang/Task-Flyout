using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class RssSensitiveDataProtectorTests
{
    [Fact]
    public void Protect_hides_plaintext_and_round_trips_for_current_user()
    {
        const string value = "https://feeds.example/private?token=secret-value";

        var protectedValue = RssSensitiveDataProtector.Protect(value);

        Assert.True(RssSensitiveDataProtector.IsProtected(protectedValue));
        Assert.DoesNotContain(value, protectedValue, StringComparison.Ordinal);
        Assert.Equal(value, RssSensitiveDataProtector.Unprotect(protectedValue));
    }

    [Fact]
    public void Protect_is_idempotent_for_existing_protected_value()
    {
        var protectedValue = RssSensitiveDataProtector.Protect("private article title");

        Assert.Equal(protectedValue, RssSensitiveDataProtector.Protect(protectedValue));
    }

    [Theory]
    [InlineData("dpapi:v1:not-base64")]
    [InlineData("dpapi:v1:")]
    public void Unprotect_returns_empty_for_invalid_ciphertext(string invalidValue)
    {
        Assert.Equal("", RssSensitiveDataProtector.Unprotect(invalidValue));
    }

    [Fact]
    public void Unprotect_leaves_legacy_plaintext_unchanged()
    {
        Assert.Equal("legacy title", RssSensitiveDataProtector.Unprotect("legacy title"));
    }
}
