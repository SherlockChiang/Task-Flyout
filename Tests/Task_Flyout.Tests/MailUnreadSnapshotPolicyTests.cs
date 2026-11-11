using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class MailUnreadSnapshotPolicyTests
{
    [Fact]
    public void Complete_empty_snapshot_removes_stale_unread_items()
    {
        var stale = Item("account", "INBOX", "stale");

        var result = MailUnreadSnapshotPolicy.Reconcile(
            new[] { stale },
            Array.Empty<MailItem>(),
            isComplete: true,
            MailAccountKind.Google,
            "account",
            "INBOX",
            Array.Empty<PendingMailMutation>(),
            maximumItems: 25);

        Assert.Empty(result.Items);
        Assert.Same(stale, Assert.Single(result.RemovedItems));
    }

    [Fact]
    public void Incomplete_snapshot_merges_without_deleting_unknown_older_items()
    {
        var older = Item("account", "INBOX", "older", minute: 1);
        var newest = Item("account", "INBOX", "newest", minute: 2);

        var result = MailUnreadSnapshotPolicy.Reconcile(
            new[] { older },
            new[] { newest },
            isComplete: false,
            MailAccountKind.Google,
            "account",
            "INBOX",
            Array.Empty<PendingMailMutation>(),
            maximumItems: 25);

        Assert.Equal(new[] { "newest", "older" }, result.Items.Select(item => item.Id));
        Assert.Empty(result.RemovedItems);
    }

    [Fact]
    public void Pending_mark_read_prevents_stale_provider_snapshot_from_resurrecting_item()
    {
        var staleProviderItem = Item("account", "INBOX", "message");
        var pending = Mutation("account", "STARRED", "message", value: true);

        var result = MailUnreadSnapshotPolicy.Reconcile(
            Array.Empty<MailItem>(),
            new[] { staleProviderItem },
            isComplete: true,
            MailAccountKind.Google,
            "account",
            "INBOX",
            new[] { pending },
            maximumItems: 25);

        Assert.Empty(result.Items);
        Assert.True(staleProviderItem.IsRead);
    }

    [Fact]
    public void Pending_mark_unread_survives_complete_stale_provider_snapshot()
    {
        var optimistic = Item("account", "INBOX", "message");
        var pending = Mutation("account", "INBOX", "message", value: false);

        var result = MailUnreadSnapshotPolicy.Reconcile(
            new[] { optimistic },
            Array.Empty<MailItem>(),
            isComplete: true,
            MailAccountKind.Google,
            "account",
            "INBOX",
            new[] { pending },
            maximumItems: 25);

        Assert.Same(optimistic, Assert.Single(result.Items));
        Assert.Empty(result.RemovedItems);
    }

    [Fact]
    public void Snapshot_does_not_import_same_identity_from_another_folder()
    {
        var inbox = Item("account", "INBOX", "message");
        var archive = Item("account", "Archive", "message");

        var result = MailUnreadSnapshotPolicy.Reconcile(
            new[] { inbox, archive },
            Array.Empty<MailItem>(),
            isComplete: false,
            MailAccountKind.Google,
            "account",
            "INBOX",
            Array.Empty<PendingMailMutation>(),
            maximumItems: 25);

        Assert.Same(inbox, Assert.Single(result.Items));
    }

    [Fact]
    public void Pending_intent_never_crosses_account_or_imap_generation()
    {
        var target = Item("account", "INBOX", "42", uidValidity: 7);
        var otherAccount = Item("other", "INBOX", "42", uidValidity: 7);
        var otherGeneration = Item("account", "INBOX", "42", uidValidity: 8);
        var pending = Mutation("account", "INBOX", "42", value: true, MailAccountKind.Imap, uidValidity: 7);

        MailUnreadSnapshotPolicy.ApplyPendingMutations(
            new[] { target, otherAccount, otherGeneration },
            MailAccountKind.Imap,
            "account",
            new[] { pending });

        Assert.True(target.IsRead);
        Assert.False(otherAccount.IsRead);
        Assert.False(otherGeneration.IsRead);
    }

    private static MailItem Item(string accountId, string folderId, string id, int minute = 0, uint? uidValidity = null)
        => new()
        {
            AccountId = accountId,
            FolderId = folderId,
            Id = id,
            ImapUidValidity = uidValidity,
            RawReceivedTime = DateTimeOffset.UnixEpoch.AddMinutes(minute)
        };

    private static PendingMailMutation Mutation(
        string accountId,
        string folderId,
        string id,
        bool value,
        MailAccountKind kind = MailAccountKind.Google,
        uint? uidValidity = null)
        => new()
        {
            AccountId = accountId,
            FolderId = folderId,
            MessageId = id,
            ProviderKind = kind,
            ImapUidValidity = uidValidity,
            Kind = MailMutationKind.SetReadState,
            Value = value
        };
}
