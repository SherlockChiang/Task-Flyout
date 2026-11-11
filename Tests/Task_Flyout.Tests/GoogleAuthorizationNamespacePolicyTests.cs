using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class GoogleAuthorizationNamespacePolicyTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("legacy-google", true)]
    [InlineData("LEGACY-GOOGLE", true)]
    [InlineData("opaque-account-id", false)]
    public void OnlyLegacyGoogleAccountOwnsLegacyAuthorization(string? accountId, bool expected)
        => Assert.Equal(expected, GoogleAuthorizationNamespacePolicy.OwnsLegacyAuthorization(accountId));
}
