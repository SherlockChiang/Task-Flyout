using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class ProviderAuthCleanupTests
{
    [Theory]
    [InlineData("Google")]
    [InlineData("Microsoft")]
    [InlineData("google")]
    public void Supports_calendar_providers_with_local_auth(string provider)
    {
        Assert.True(ProviderAuthCleanup.SupportsProvider(provider));
    }

    [Fact]
    public void Does_not_claim_mail_only_provider_cleanup()
    {
        Assert.False(ProviderAuthCleanup.SupportsProvider("IMAP"));
    }

    [Fact]
    public void Exposes_expected_google_legacy_token_path()
    {
        Assert.EndsWith(Path.Combine("TaskFlyout", "GoogleToken"), ProviderAuthCleanup.GoogleLegacyTokenPath);
    }

    [Fact]
    public void Exposes_expected_microsoft_auth_record_path()
    {
        Assert.EndsWith(Path.Combine("TaskFlyout", "ms_auth_record.bin"), ProviderAuthCleanup.MicrosoftAuthRecordPath);
    }
}
