using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class AccountIdentityPolicyTests
{
    [Theory]
    [InlineData("Google", "legacy-google")]
    [InlineData("Gmail", "legacy-google")]
    [InlineData("Microsoft", "legacy-microsoft")]
    [InlineData("Outlook", "legacy-microsoft")]
    [InlineData("iCloud", "legacy-icloud")]
    public void LegacyIds_AreStableAcrossProviderAliases(string provider, string expected)
        => Assert.Equal(expected, AccountIdentityPolicy.CreateLegacyAccountId(provider));

    [Fact]
    public void ProviderKeys_IsolateAccountsFromTheSameProvider()
    {
        var first = AccountIdentityPolicy.CreateProviderKey("Google", "account-a");
        var second = AccountIdentityPolicy.CreateProviderKey("Google", "account-b");

        Assert.NotEqual(first, second);
        Assert.False(AccountIdentityPolicy.Matches("Google", "account-a", "Google", "account-b"));
    }

    [Fact]
    public void ProviderKeys_NormalizeAliasesAndMissingLegacyIds()
    {
        Assert.True(AccountIdentityPolicy.Matches("Gmail", "", "Google", "legacy-google"));
        Assert.True(AccountIdentityPolicy.Matches("Outlook", null, "Microsoft", "legacy-microsoft"));
    }

    [Fact]
    public void NewAccountIds_AreOpaqueAndUnique()
    {
        var first = AccountIdentityPolicy.CreateAccountId();
        var second = AccountIdentityPolicy.CreateAccountId();

        Assert.Equal(32, first.Length);
        Assert.NotEqual(first, second);
        Assert.True(Guid.TryParseExact(first, "N", out _));
    }
}
