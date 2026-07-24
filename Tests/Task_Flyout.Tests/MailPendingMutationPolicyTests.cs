using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class MailPendingMutationPolicyTests
{
    [Fact]
    public void Upsert_deduplicates_identity_and_refreshes_provider_generation()
    {
        var now = DateTimeOffset.UtcNow;
        var queue = new List<PendingMailMutation>();
        var mutation = Create("account", "folder", "message", createdTicks: 0);

        MailPendingMutationPolicy.Upsert(queue, mutation, now, 500);
        mutation.ImapUidValidity = 42;
        MailPendingMutationPolicy.Upsert(queue, mutation, now.AddMinutes(1), 500);

        var pending = Assert.Single(queue);
        Assert.Equal(42u, pending.ImapUidValidity);
        Assert.Equal(2, pending.FailureCount);
        Assert.Equal(now.UtcTicks, pending.CreatedUtcTicks);
    }

    [Fact]
    public void Upsert_is_last_write_wins_per_message_and_kind()
    {
        var now = DateTimeOffset.UtcNow;
        var queue = new List<PendingMailMutation>();
        var read = Create("account", "folder", "message", 0);
        read.Kind = MailMutationKind.SetReadState;
        read.Value = true;
        MailPendingMutationPolicy.Upsert(queue, read, now, 500);
        read.Value = false;
        MailPendingMutationPolicy.Upsert(queue, read, now.AddSeconds(1), 500);
        var flag = Create("account", "folder", "message", 0);
        flag.Kind = MailMutationKind.SetFlagged;
        flag.Value = true;
        MailPendingMutationPolicy.Upsert(queue, flag, now.AddSeconds(2), 500);

        Assert.Equal(2, queue.Count);
        Assert.False(queue.Single(item => item.Kind == MailMutationKind.SetReadState).Value);
        Assert.True(queue.Single(item => item.Kind == MailMutationKind.SetFlagged).Value);
    }

    [Fact]
    public void Legacy_entry_migrates_to_mark_read_true()
    {
        var queue = new List<PendingMailMutation> { Create("account", "folder", "message", DateTimeOffset.UtcNow.UtcTicks) };

        Assert.True(MailPendingMutationPolicy.MigrateLegacyAndDeduplicate(queue));
        var mutation = Assert.Single(queue);
        Assert.Equal(MailMutationKind.SetReadState, mutation.Kind);
        Assert.True(mutation.Value);
    }

    [Fact]
    public void New_intent_can_replace_queued_opposite_value()
    {
        var queued = Create("account", "folder", "message", DateTimeOffset.UtcNow.UtcTicks);
        queued.Kind = MailMutationKind.SetReadState;
        queued.Value = true;
        var queue = new List<PendingMailMutation> { queued };

        Assert.True(MailPendingMutationPolicy.RemoveKind(queue, new PendingMailMutation
        {
            AccountId = "account", FolderId = "folder", MessageId = "message", Kind = MailMutationKind.SetReadState, Value = false
        }));
        Assert.Empty(queue);
    }

    [Fact]
    public void Queue_limit_retains_newest_mutations()
    {
        var now = DateTimeOffset.UtcNow;
        var queue = Enumerable.Range(0, 3)
            .Select(index => Create("account", "folder", $"message-{index}", now.AddMinutes(index).UtcTicks))
            .ToList();

        MailPendingMutationPolicy.Upsert(queue, Create("account", "folder", "new", 0), now.AddMinutes(4), maximumCount: 3);

        Assert.Equal(3, queue.Count);
        Assert.DoesNotContain(queue, mutation => mutation.MessageId == "message-0");
        Assert.Contains(queue, mutation => mutation.MessageId == "new");
    }

    [Fact]
    public void Select_due_returns_earliest_bounded_clones_for_matching_account()
    {
        var now = DateTimeOffset.UtcNow;
        var queue = new List<PendingMailMutation>
        {
            Create("account", "folder", "later", now.UtcTicks, now.AddMinutes(-1).UtcTicks),
            Create("other", "folder", "other", now.UtcTicks, now.AddMinutes(-3).UtcTicks),
            Create("account", "folder", "earlier", now.UtcTicks, now.AddMinutes(-2).UtcTicks),
            Create("account", "folder", "future", now.UtcTicks, now.AddMinutes(1).UtcTicks)
        };

        var due = MailPendingMutationPolicy.SelectDue(queue, "account", MailAccountKind.Google, now, maximumCount: 1);

        var selected = Assert.Single(due);
        Assert.Equal("earlier", selected.MessageId);
        Assert.NotSame(queue[2], selected);
    }

    [Fact]
    public void Cleanup_removes_expired_and_account_mutations_only()
    {
        var now = DateTimeOffset.UtcNow;
        var queue = new List<PendingMailMutation>
        {
            Create("account", "folder", "expired", now.AddDays(-8).UtcTicks),
            Create("account", "folder", "current", now.UtcTicks),
            Create("other", "folder", "other", now.UtcTicks)
        };

        Assert.Equal(1, MailPendingMutationPolicy.RemoveExpired(queue, now));
        Assert.Equal(1, MailPendingMutationPolicy.RemoveAccount(queue, "account"));
        Assert.Equal("other", Assert.Single(queue).AccountId);
    }

    private static PendingMailMutation Create(string accountId, string folderId, string messageId, long createdTicks, long? nextAttemptTicks = null)
        => new()
        {
            AccountId = accountId,
            FolderId = folderId,
            MessageId = messageId,
            ProviderKind = MailAccountKind.Google,
            CreatedUtcTicks = createdTicks,
            NextAttemptUtcTicks = nextAttemptTicks ?? createdTicks
        };
}
