using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class MailProviderAccountLinkPolicyTests
{
    [Theory]
    [InlineData(MailAccountKind.Google, null, "legacy-google")]
    [InlineData(MailAccountKind.Outlook, "", "legacy-microsoft")]
    [InlineData(MailAccountKind.Imap, null, "")]
    [InlineData(MailAccountKind.Google, " account-a ", "account-a")]
    public void MissingLinksMigrateOnlyOAuthBackedProviders(
        MailAccountKind kind,
        string? providerAccountId,
        string expected)
        => Assert.Equal(
            expected,
            MailProviderAccountLinkPolicy.ResolveProviderAccountId(kind, providerAccountId));

    [Fact]
    public void GoogleMailboxMatchesOnlyItsLinkedProviderAccount()
    {
        Assert.True(MailProviderAccountLinkPolicy.MatchesProviderAccount(
            MailAccountKind.Google,
            "google-a",
            "Gmail",
            "google-a"));
        Assert.False(MailProviderAccountLinkPolicy.MatchesProviderAccount(
            MailAccountKind.Google,
            "google-a",
            "Google",
            "google-b"));
    }

    [Fact]
    public void LegacyMailboxLinkMatchesMissingLegacyAgendaIdentity()
        => Assert.True(MailProviderAccountLinkPolicy.MatchesProviderAccount(
            MailAccountKind.Google,
            null,
            "Google",
            null));

    [Theory]
    [InlineData(MailAccountKind.Google, "Gmail", " person@example.com ", "person@example.com")]
    [InlineData(MailAccountKind.Google, " Gmail ", null, "Gmail")]
    [InlineData(MailAccountKind.Outlook, " Person ", "person@example.com", "Person")]
    [InlineData(MailAccountKind.Outlook, "", " person@example.com ", "person@example.com")]
    [InlineData(MailAccountKind.Imap, "Personal", "person@example.com", "")]
    public void AgendaDisplayNameUsesStableMailboxIdentity(
        MailAccountKind kind,
        string? displayName,
        string? address,
        string expected)
        => Assert.Equal(
            expected,
            MailProviderAccountLinkPolicy.GetPreferredAgendaDisplayName(kind, displayName, address));

    [Theory]
    [InlineData(null, "Google", true)]
    [InlineData("", "Google", true)]
    [InlineData("google", "Google", true)]
    [InlineData("person@example.com", "Google", false)]
    public void DisplayNameHydrationPreservesEstablishedAccountLabels(
        string? currentDisplayName,
        string providerName,
        bool expected)
        => Assert.Equal(
            expected,
            MailProviderAccountLinkPolicy.ShouldHydrateAgendaDisplayName(
                currentDisplayName,
                providerName));
}
