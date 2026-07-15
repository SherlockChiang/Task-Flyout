using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class ProviderAuthorizationLifecycleTests
{
    [Theory]
    [InlineData("Google", true)]
    [InlineData("Gmail", true)]
    [InlineData("Microsoft", true)]
    [InlineData("Outlook", true)]
    [InlineData("IMAP", false)]
    public void Identifies_shared_provider_authorization(string provider, bool expected)
    {
        Assert.Equal(expected, ProviderAuthorizationLifecycle.HasSharedAuthorization(provider));
    }

    [Theory]
    [InlineData("Gmail", "Google")]
    [InlineData("google", "Google")]
    [InlineData("Outlook", "Microsoft")]
    [InlineData("MICROSOFT", "Microsoft")]
    [InlineData("IMAP", "IMAP")]
    public void Normalizes_feature_names_to_provider_names(string featureName, string expected)
    {
        Assert.Equal(expected, ProviderAuthorizationLifecycle.NormalizeProviderName(featureName));
    }

    [Fact]
    public async Task Complete_disconnect_clears_authorization_before_feature_data()
    {
        var calls = new List<string>();

        await ProviderAuthorizationLifecycle.DisconnectCompletelyAsync(
            () => Add("auth"),
            () => Add("agenda"),
            () => Add("mail"),
            () => Add("browser"));

        Assert.Equal(["auth", "agenda", "mail", "browser"], calls);
        Task Add(string value) { calls.Add(value); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Authorization_failure_preserves_all_feature_data()
    {
        var featureRemovalCalls = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ProviderAuthorizationLifecycle.DisconnectCompletelyAsync(
                () => Task.FromException(new InvalidOperationException("locked token cache")),
                RemoveFeature,
                RemoveFeature,
                RemoveFeature));

        Assert.Equal(0, featureRemovalCalls);
        Task RemoveFeature() { featureRemovalCalls++; return Task.CompletedTask; }
    }
}
