using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class MailNotificationIdentityPolicyTests
{
    [Theory]
    [InlineData(MailAccountKind.Google)]
    [InlineData(MailAccountKind.Outlook)]
    public void Stable_provider_tag_is_shared_across_folder_copies(MailAccountKind providerKind)
    {
        var inbox = Item("account", "INBOX", "message");
        var archive = Item("account", "Archive", "message");

        Assert.Equal(
            MailNotificationIdentityPolicy.BuildTag(providerKind, inbox),
            MailNotificationIdentityPolicy.BuildTag(providerKind, archive));
    }

    [Fact]
    public void Tag_is_account_scoped_and_within_windows_identifier_limit()
    {
        string first = MailNotificationIdentityPolicy.BuildTag(
            MailAccountKind.Google,
            Item("account-a", "INBOX", "message"));
        string second = MailNotificationIdentityPolicy.BuildTag(
            MailAccountKind.Google,
            Item("account-b", "INBOX", "message"));

        Assert.NotEqual(first, second);
        Assert.Equal(16, first.Length);
        Assert.Matches("^[0-9A-F]{16}$", first);
        Assert.Equal("mail", MailNotificationIdentityPolicy.Group);
    }

    [Fact]
    public void Imap_tag_includes_folder_and_uidvalidity()
    {
        string original = MailNotificationIdentityPolicy.BuildTag(
            MailAccountKind.Imap,
            Item("account", "INBOX", "42", 7));
        string otherFolder = MailNotificationIdentityPolicy.BuildTag(
            MailAccountKind.Imap,
            Item("account", "Archive", "42", 7));
        string otherGeneration = MailNotificationIdentityPolicy.BuildTag(
            MailAccountKind.Imap,
            Item("account", "INBOX", "42", 8));

        Assert.NotEqual(original, otherFolder);
        Assert.NotEqual(original, otherGeneration);
    }

    [Fact]
    public void Tag_is_provider_scoped()
    {
        var item = Item("account", "INBOX", "message", 7);

        string outlook = MailNotificationIdentityPolicy.BuildTag(MailAccountKind.Outlook, item);
        string google = MailNotificationIdentityPolicy.BuildTag(MailAccountKind.Google, item);
        string imap = MailNotificationIdentityPolicy.BuildTag(MailAccountKind.Imap, item);

        Assert.Equal(3, new[] { outlook, google, imap }.Distinct().Count());
    }

    private static MailItem Item(string accountId, string folderId, string id, uint? uidValidity = null)
        => new()
        {
            AccountId = accountId,
            FolderId = folderId,
            Id = id,
            ImapUidValidity = uidValidity
        };
}
