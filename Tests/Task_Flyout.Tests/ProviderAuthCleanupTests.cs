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
    public void Deletes_existing_google_legacy_token_store_recursively()
    {
        string? deletedPath = null;
        bool recursive = false;

        ProviderAuthCleanup.DeleteGoogleLegacyTokenStore(
            "legacy-token-path",
            _ => true,
            (path, deleteRecursively) =>
            {
                deletedPath = path;
                recursive = deleteRecursively;
            });

        Assert.Equal("legacy-token-path", deletedPath);
        Assert.True(recursive);
    }

    [Fact]
    public void Propagates_google_legacy_token_deletion_failure()
    {
        var expected = new IOException("Token directory is locked.");

        var actual = Assert.Throws<IOException>(() =>
            ProviderAuthCleanup.DeleteGoogleLegacyTokenStore(
                "legacy-token-path",
                _ => true,
                (_, _) => throw expected));

        Assert.Same(expected, actual);
    }

    [Fact]
    public void Ignores_missing_google_legacy_token_store()
    {
        bool deleteCalled = false;

        ProviderAuthCleanup.DeleteGoogleLegacyTokenStore(
            "legacy-token-path",
            _ => false,
            (_, _) => deleteCalled = true);

        Assert.False(deleteCalled);
    }

    [Fact]
    public void Exposes_expected_microsoft_auth_record_path()
    {
        Assert.EndsWith(Path.Combine("TaskFlyout", "ms_auth_record.bin"), ProviderAuthCleanup.MicrosoftAuthRecordPath);
    }

    [Fact]
    public void Exposes_azure_identity_microsoft_cache_locations()
    {
        Assert.EndsWith(".IdentityService", ProviderAuthCleanup.MicrosoftTokenCacheDirectory);
        Assert.Equal(
            new[] { "TaskFlyout_MSAL_Cache.nocae", "TaskFlyout_MSAL_Cache.cae" },
            ProviderAuthCleanup.MicrosoftTokenCacheNames);
    }
}
