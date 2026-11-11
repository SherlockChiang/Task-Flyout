using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class MailMutationCachePolicyTests
{
    [Fact]
    public void Gmail_mutation_updates_every_label_copy_once()
    {
        var target = Item("a", "INBOX", "m", isRead: false);
        var inboxCopy = Item("a", "INBOX", "m", isRead: false);
        var duplicateInboxCopy = Item("a", "INBOX", "m", isRead: false);
        var starredCopy = Item("a", "STARRED", "m", isRead: false);
        var otherMessage = Item("a", "INBOX", "other", isRead: false);

        var result = MailMutationCachePolicy.Apply(
            new[] { inboxCopy, duplicateInboxCopy, starredCopy, otherMessage },
            MailAccountKind.Google,
            target,
            MailMutationKind.SetReadState,
            true);

        Assert.True(result.Changed);
        Assert.Equal(new[] { "INBOX", "STARRED" }, result.ReadStateChangedFolderIds);
        Assert.All(new[] { inboxCopy, duplicateInboxCopy, starredCopy }, item => Assert.True(item.IsRead));
        Assert.False(otherMessage.IsRead);
    }

    [Theory]
    [InlineData(MailAccountKind.Google)]
    [InlineData(MailAccountKind.Outlook)]
    public void Stable_provider_identity_never_crosses_accounts(MailAccountKind providerKind)
    {
        var target = Item("a", "source", "m");
        var sameAccount = Item("a", "other-folder", "m");
        var otherAccount = Item("b", "source", "m");

        var result = MailMutationCachePolicy.Apply(
            new[] { sameAccount, otherAccount },
            providerKind,
            target,
            MailMutationKind.SetFlagged,
            true);

        Assert.True(result.Changed);
        Assert.True(sameAccount.IsFlagged);
        Assert.False(otherAccount.IsFlagged);
        Assert.Empty(result.ReadStateChangedFolderIds);
    }

    [Fact]
    public void Imap_identity_requires_folder_uidvalidity_and_uid()
    {
        var target = Item("a", "INBOX", "42", uidValidity: 7);
        var match = Item("a", "INBOX", "42", uidValidity: 7);
        var otherFolder = Item("a", "Archive", "42", uidValidity: 7);
        var otherGeneration = Item("a", "INBOX", "42", uidValidity: 8);
        var legacyCopy = Item("a", "INBOX", "42");

        var result = MailMutationCachePolicy.Apply(
            new[] { match, otherFolder, otherGeneration, legacyCopy },
            MailAccountKind.Imap,
            target,
            MailMutationKind.SetReadState,
            true);

        Assert.True(result.Changed);
        Assert.Equal(new[] { "INBOX" }, result.ReadStateChangedFolderIds);
        Assert.True(match.IsRead);
        Assert.False(otherFolder.IsRead);
        Assert.False(otherGeneration.IsRead);
        Assert.False(legacyCopy.IsRead);
    }

    [Fact]
    public void Direct_legacy_imap_item_can_still_reflect_optimistic_state()
    {
        var target = Item("a", "INBOX", "42");

        var result = MailMutationCachePolicy.Apply(
            new[] { target },
            MailAccountKind.Imap,
            target,
            MailMutationKind.SetReadState,
            true);

        Assert.True(result.Changed);
        Assert.True(target.IsRead);
        Assert.Equal(new[] { "INBOX" }, result.ReadStateChangedFolderIds);
    }

    [Theory]
    [InlineData(3, false, true, 2)]
    [InlineData(3, true, false, 4)]
    [InlineData(0, false, true, 0)]
    public void Unread_count_is_adjusted_safely(int count, bool before, bool after, int expected)
        => Assert.Equal(expected, MailMutationCachePolicy.AdjustUnreadCount(count, before, after));

    private static MailItem Item(
        string accountId,
        string folderId,
        string messageId,
        bool isRead = false,
        uint? uidValidity = null)
        => new()
        {
            AccountId = accountId,
            FolderId = folderId,
            Id = messageId,
            IsRead = isRead,
            ImapUidValidity = uidValidity
        };
}
