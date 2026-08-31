using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class MailNotificationNavigationPolicyTests
{
    [Theory]
    [InlineData("INBOX", "INBOX", "Inbox")]
    [InlineData("Inbox", "INBOX", "收件箱")]
    [InlineData("收件匣", "Inbox", "INBOX")]
    public void FolderMatchesAcceptsProviderAndLocalizedInboxAliases(
        string requestedId,
        string folderId,
        string displayName)
    {
        var folder = new MailFolder
        {
            Id = folderId,
            DisplayName = displayName
        };

        Assert.True(MailNotificationNavigationPolicy.FolderMatches(folder, requestedId));
    }

    [Fact]
    public void FolderMatchesRejectsArbitraryDisplayNameFallback()
    {
        var folder = new MailFolder { Id = "provider-id-v2", DisplayName = "Archive" };

        Assert.False(MailNotificationNavigationPolicy.FolderMatches(folder, "Archive"));
        Assert.False(MailNotificationNavigationPolicy.FolderMatches(folder, "Sent"));
    }

    [Theory]
    [InlineData(MailAccountKind.Google)]
    [InlineData(MailAccountKind.Outlook)]
    public void CrossFolderMessageIdentityIsAllowedForStableProviderKinds(MailAccountKind accountKind)
    {
        Assert.True(MailNotificationNavigationPolicy.CanUseCrossFolderIdentity(accountKind));
    }

    [Fact]
    public void CrossFolderMessageIdentityIsRejectedForImapEvenWithoutUidMetadata()
    {
        Assert.False(MailNotificationNavigationPolicy.CanUseCrossFolderIdentity(MailAccountKind.Imap));
    }

    [Fact]
    public void MessageMatchAlwaysRequiresTheOriginalFolder()
    {
        var item = new MailItem { AccountId = "account", FolderId = "INBOX", Id = "42" };

        Assert.False(MailNotificationNavigationPolicy.MessageMatches(
            item, "account", "Archive", "42"));
        Assert.True(MailNotificationNavigationPolicy.MessageMatches(
            item, "account", "INBOX", "42"));
    }

    [Fact]
    public void PollMergeCreatesColdStartUnreadNotificationSlice()
    {
        var newest = CreateItem("new", DateTimeOffset.Parse("2026-08-31T10:00:00Z"));
        var older = CreateItem("old", DateTimeOffset.Parse("2026-08-31T09:00:00Z"));

        var result = MailNotificationNavigationPolicy.MergePolledMessages(
            existing: null,
            polled: new[] { older, newest },
            unreadOnly: true,
            maxItems: 5);

        Assert.Equal(new[] { "new", "old" }, result.Select(item => item.Id));
    }

    [Fact]
    public void PollMergeReplacesExistingMetadataAndKeepsWindowBounded()
    {
        var stale = CreateItem("same", DateTimeOffset.Parse("2026-08-31T08:00:00Z"));
        stale.Subject = "stale";
        var refreshed = CreateItem("same", DateTimeOffset.Parse("2026-08-31T10:00:00Z"));
        refreshed.Subject = "fresh";
        var second = CreateItem("second", DateTimeOffset.Parse("2026-08-31T09:00:00Z"));

        var result = MailNotificationNavigationPolicy.MergePolledMessages(
            existing: new[] { stale },
            polled: new[] { refreshed, second },
            unreadOnly: true,
            maxItems: 1);

        Assert.Single(result);
        Assert.Equal("fresh", result[0].Subject);
    }

    [Fact]
    public void PollMergeOmitsReadItemsFromUnreadSlice()
    {
        var read = CreateItem("read", DateTimeOffset.Parse("2026-08-31T10:00:00Z"));
        read.IsRead = true;

        var result = MailNotificationNavigationPolicy.MergePolledMessages(
            existing: null,
            polled: new[] { read },
            unreadOnly: true,
            maxItems: 5);

        Assert.Empty(result);
    }

    private static MailItem CreateItem(string id, DateTimeOffset received)
        => new()
        {
            AccountId = "account",
            FolderId = "INBOX",
            Id = id,
            RawReceivedTime = received
        };
}
