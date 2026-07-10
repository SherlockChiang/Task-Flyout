using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class MailCacheKeyPolicyTests
{
    [Fact]
    public void Cache_key_does_not_duplicate_windows_by_page_size()
    {
        Assert.Equal("account|folder|False", MailCacheKeyPolicy.Build("account", "folder", unreadOnly: false));
    }

    [Theory]
    [InlineData("account|folder|False|25", "account|folder|False")]
    [InlineData("account|folder|with|pipes|True|100", "account|folder|with|pipes|True")]
    public void Legacy_page_size_key_is_canonicalized(string legacyKey, string expected)
    {
        Assert.True(MailCacheKeyPolicy.TryCanonicalizeLegacy(legacyKey, out var canonicalKey));
        Assert.Equal(expected, canonicalKey);
    }

    [Theory]
    [InlineData("account|folder|False")]
    [InlineData("account|folder|other|25")]
    public void Canonical_or_unknown_key_is_not_rewritten(string key)
    {
        Assert.False(MailCacheKeyPolicy.TryCanonicalizeLegacy(key, out var canonicalKey));
        Assert.Equal(key, canonicalKey);
    }
}
