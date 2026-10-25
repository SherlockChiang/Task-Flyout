using System.Text.Json;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class TaskMailActionsTests
{
    private static readonly TaskMailSource Source = new("mail-a", "a@example.com", MailAccountKind.Outlook, "inbox", "message", null, "Subject", false);
    private static readonly CreatedTaskReference TaskRef = new("task", "list");
    private static readonly MailArchiveReceipt Receipt = new(new MailItem { AccountId = "mail-a", FolderId = "archive", Id = "moved" }, "inbox");

    [Fact]
    public async Task Completion_matches_account_list_and_task_and_runs_once()
    {
        int calls = 0;
        var actions = new TaskMailActions(() => null, _ => { }, _ => { calls++; return Task.FromResult(Receipt); }, (_, _) => Task.CompletedTask);
        await actions.AddAsync("Microsoft|a", TaskRef, "Task", Source);
        await actions.CompleteAsync("Microsoft|b", "list", "task");
        await actions.CompleteAsync("Microsoft|a", "other", "task");
        await actions.CompleteAsync("Microsoft|a", "list", "other");
        Assert.Equal(0, calls);
        await actions.CompleteAsync("microsoft|A", "list", "task");
        await actions.CompleteAsync("Microsoft|a", "list", "task");
        Assert.Equal(1, calls);
        Assert.Equal(TaskMailPhase.Archived, Assert.Single(actions.Snapshot()).Phase);
    }

    [Fact]
    public async Task Concurrent_completion_and_retry_do_not_duplicate_the_archive()
    {
        int calls = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actions = new TaskMailActions(() => null, _ => { }, async _ => { calls++; entered.SetResult(); await release.Task; return Receipt; }, (_, _) => Task.CompletedTask);
        await actions.AddAsync("a", TaskRef, "Task", Source);
        var first = actions.CompleteAsync("a", "list", "task");
        await entered.Task;
        var second = actions.CompleteAsync("a", "list", "task");
        var retry = actions.RetryAsync(TaskMailActions.Key("a", "list", "task"));
        release.SetResult();
        await Task.WhenAll(first, second, retry);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Definite_failure_does_not_fail_completion_and_needs_explicit_retry()
    {
        int calls = 0;
        var actions = new TaskMailActions(() => null, _ => { }, _ => ++calls == 1
            ? Task.FromException<MailArchiveReceipt>(new InvalidOperationException("offline")) : Task.FromResult(Receipt), (_, _) => Task.CompletedTask);
        await actions.AddAsync("a", TaskRef, "Task", Source);
        await actions.CompleteAsync("a", "list", "task");
        await actions.CompleteAsync("a", "list", "task");
        Assert.Equal(1, calls);
        Assert.Equal(TaskMailPhase.Failed, Assert.Single(actions.Snapshot()).Phase);
        await actions.RetryAsync(TaskMailActions.Key("a", "list", "task"));
        Assert.Equal(2, calls);
        Assert.Equal(TaskMailPhase.Archived, Assert.Single(actions.Snapshot()).Phase);
    }

    [Fact]
    public async Task Unknown_outcome_never_replays_even_on_explicit_retry()
    {
        int calls = 0;
        var actions = new TaskMailActions(() => null, _ => { }, _ => { calls++; throw new TaskMailOutcomeUnknownException(new IOException()); }, (_, _) => Task.CompletedTask);
        await actions.AddAsync("a", TaskRef, "Task", Source);
        await actions.CompleteAsync("a", "list", "task");
        await actions.RetryAsync(TaskMailActions.Key("a", "list", "task"));
        await actions.CompleteAsync("a", "list", "task");
        Assert.Equal(1, calls);
        Assert.Equal(TaskMailPhase.Unknown, Assert.Single(actions.Snapshot()).Phase);
    }

    [Theory]
    [InlineData((int)TaskMailPhase.Archiving)]
    [InlineData((int)TaskMailPhase.Undoing)]
    public async Task Restart_does_not_repeat_an_interrupted_operation(int phase)
    {
        var link = new TaskMailLink(TaskMailActions.Key("a", "list", "task"), "a", "list", "task", "Task", Source, (TaskMailPhase)phase, Receipt);
        int calls = 0;
        var actions = new TaskMailActions(() => JsonSerializer.Serialize(new[] { link }), _ => { }, _ => { calls++; return Task.FromResult(Receipt); }, (_, _) => { calls++; return Task.CompletedTask; });
        await actions.CompleteAsync("a", "list", "task");
        await actions.RetryAsync(link.Key);
        await actions.UndoAsync(link.Key);
        Assert.Equal(0, calls);
        Assert.Equal(TaskMailPhase.Unknown, Assert.Single(actions.Snapshot()).Phase);
    }

    [Fact]
    public async Task Undo_uses_persisted_moved_identity_and_never_rearchives()
    {
        string? json = null;
        var actions = new TaskMailActions(() => json, value => json = value, _ => Task.FromResult(Receipt), (_, _) => Task.CompletedTask);
        await actions.AddAsync("a", TaskRef, "Task", Source);
        await actions.CompleteAsync("a", "list", "task");
        int undo = 0;
        var restored = new TaskMailActions(() => json, value => json = value, _ => throw new Exception("Must not archive again"), (source, receipt) =>
        {
            Assert.Equal(Source, source);
            Assert.Equal("moved", receipt.Item.Id);
            Assert.Equal("inbox", receipt.ReturnFolderId);
            undo++;
            return Task.CompletedTask;
        });
        await restored.UndoAsync(TaskMailActions.Key("a", "list", "task"));
        await restored.UndoAsync(TaskMailActions.Key("a", "list", "task"));
        await restored.CompleteAsync("a", "list", "task");
        Assert.Equal(1, undo);
        Assert.Equal(TaskMailPhase.Undone, Assert.Single(restored.Snapshot()).Phase);
    }

    [Fact]
    public async Task Failure_to_save_inflight_state_prevents_remote_write()
    {
        bool fail = false;
        int calls = 0;
        var actions = new TaskMailActions(() => null, _ => { if (fail) throw new IOException(); }, _ => { calls++; return Task.FromResult(Receipt); }, (_, _) => Task.CompletedTask);
        await actions.AddAsync("a", TaskRef, "Task", Source);
        fail = true;
        await actions.CompleteAsync("a", "list", "task");
        Assert.Equal(0, calls);
        Assert.True(actions.StorageFailed);
        Assert.Equal(TaskMailPhase.Failed, Assert.Single(actions.Snapshot()).Phase);
        fail = false;
        await actions.RetryAsync(TaskMailActions.Key("a", "list", "task"));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Failure_to_save_receipt_retains_memory_result_and_disk_inflight_marker()
    {
        string? json = null;
        bool fail = false;
        var actions = new TaskMailActions(() => json, value => { if (fail) throw new IOException(); json = value; },
            _ => { fail = true; return Task.FromResult(Receipt); }, (_, _) => Task.CompletedTask);
        await actions.AddAsync("a", TaskRef, "Task", Source);
        await actions.CompleteAsync("a", "list", "task");
        Assert.True(actions.StorageFailed);
        Assert.Equal(TaskMailPhase.Archived, Assert.Single(actions.Snapshot()).Phase);
        var restored = new TaskMailActions(() => json, _ => { }, _ => throw new Exception(), (_, _) => throw new Exception());
        Assert.Equal(TaskMailPhase.Unknown, Assert.Single(restored.Snapshot()).Phase);
        await restored.CompleteAsync("a", "list", "task");
    }

    [Fact]
    public async Task Corrupt_storage_is_not_overwritten()
    {
        int writes = 0;
        var actions = new TaskMailActions(() => "broken json", _ => writes++, _ => throw new Exception(), (_, _) => throw new Exception());
        Assert.Empty(actions.Snapshot());
        Assert.True(actions.StorageFailed);
        await Assert.ThrowsAsync<JsonException>(() => actions.AddAsync("a", TaskRef, "Task", Source));
        await actions.CompleteAsync("a", "list", "task");
        Assert.Equal(0, writes);
    }

    [Fact]
    public async Task Undo_failure_is_not_retried_and_task_remains_independent()
    {
        int calls = 0;
        var actions = new TaskMailActions(() => null, _ => { }, _ => Task.FromResult(Receipt), (_, _) => { calls++; throw new IOException(); });
        await actions.AddAsync("a", TaskRef, "Task", Source);
        await actions.CompleteAsync("a", "list", "task");
        await actions.UndoAsync(TaskMailActions.Key("a", "list", "task"));
        await actions.UndoAsync(TaskMailActions.Key("a", "list", "task"));
        Assert.Equal(1, calls);
        Assert.Equal(TaskMailPhase.Unknown, Assert.Single(actions.Snapshot()).Phase);
    }

    [Fact]
    public async Task Reopening_failed_task_disables_archive_retry_until_next_completion()
    {
        int calls = 0;
        var actions = new TaskMailActions(() => null, _ => { }, _ => { calls++; throw new InvalidOperationException(); }, (_, _) => Task.CompletedTask);
        await actions.AddAsync("a", TaskRef, "Task", Source);
        await actions.CompleteAsync("a", "list", "task");
        await actions.ReopenAsync("a", "list", "task");
        await actions.RetryAsync(TaskMailActions.Key("a", "list", "task"));
        Assert.Equal(1, calls);
        Assert.Equal(TaskMailPhase.Ready, Assert.Single(actions.Snapshot()).Phase);
    }

    [Fact]
    public async Task Removing_an_account_or_dismissing_link_prevents_further_actions()
    {
        var actions = new TaskMailActions(() => null, _ => { }, _ => throw new Exception(), (_, _) => throw new Exception());
        await actions.AddAsync("a", TaskRef, "Task", Source);
        await actions.AddAsync("b", TaskRef, "Other", Source with { AccountId = "mail-b" });
        await actions.ForgetAccountAsync(mailAccountId: "mail-a");
        Assert.Equal("b", Assert.Single(actions.Snapshot()).ProviderKey);
        await actions.ForgetAsync(TaskMailActions.Key("b", "list", "task"));
        await actions.CompleteAsync("a", "list", "task");
        Assert.Empty(actions.Snapshot());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Gmail_label_deltas_and_imap_uid_validity_survive_restart(bool google)
    {
        var item = new MailItem { AccountId = "mail-a", Id = "42", FolderId = "archive", ImapUidValidity = 500 };
        var gmail = new GmailLabelMutationResult(item, GmailOnlineActionKind.Archive, "INBOX", null,
            Array.Empty<string>(), new[] { "INBOX" }, new[] { "INBOX", "UNREAD" }, new[] { "UNREAD" }, true, true);
        var imap = new ImapMoveResult(item, new("INBOX", 100, 12), new("archive", 500, 42));
        var receipt = google ? new MailArchiveReceipt(item, "INBOX", Gmail: gmail) : new MailArchiveReceipt(item, "INBOX", Imap: imap);
        string? json = null;
        var actions = new TaskMailActions(() => json, value => json = value, _ => Task.FromResult(receipt), (_, _) => Task.CompletedTask);
        await actions.AddAsync("a", TaskRef, "Task", Source);
        await actions.CompleteAsync("a", "list", "task");
        bool undone = false;
        var restarted = new TaskMailActions(() => json, value => json = value, _ => throw new Exception(), (_, restored) =>
        {
            if (google) Assert.Equal(new[] { "INBOX" }, restored.Gmail!.RemovedLabelIds);
            else
            {
                Assert.Equal(100u, restored.Imap!.Source.UidValidity);
                Assert.Equal(500u, restored.Imap.Destination.UidValidity);
                Assert.Equal(42u, restored.Imap.Destination.Uid);
            }
            undone = true;
            return Task.CompletedTask;
        });
        await restarted.UndoAsync(TaskMailActions.Key("a", "list", "task"));
        Assert.True(undone);
    }
}
