using Task_Flyout.Services;

namespace Task_Flyout.Tests;

[Collection("LocalSqliteStore")]
public class NotificationActionStoreTests
{
    [Fact]
    public async Task Protected_capability_binds_actions_and_complete_is_single_use()
    {
        var root = Path.Combine(Path.GetTempPath(), "taskflyout-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        LocalSqliteStore.SetDataPathForTests(root);
        try
        {
            var target = new NotificationActionTarget(NotificationActionTarget.CurrentSchemaVersion, "Google", "task", "2026-07-15", NotificationActionMask.Open | NotificationActionMask.Complete, DateTimeOffset.UtcNow.AddMinutes(10), AccountId: "account-a");
            var token = NotificationActionStore.Store(target);

            Assert.True(NotificationActivationParser.IsOpaqueToken(token));
            Assert.Null(await NotificationActionStore.ReadAsync(token, NotificationActionMask.None, consume: true));
            Assert.Null(await NotificationActionStore.ReadAsync(token, NotificationActionMask.Open | NotificationActionMask.Complete, consume: true));
            Assert.Null(await NotificationActionStore.ReadAsync(token, NotificationActionMask.MarkRead, consume: true));
            Assert.Null(await NotificationActionStore.ReadAsync(token, NotificationActionMask.Snooze, consume: false));
            var stored = await NotificationActionStore.ReadAsync(token, NotificationActionMask.Complete, consume: true);
            Assert.NotNull(stored);
            Assert.Equal("account-a", stored.AccountId);
            Assert.Null(await NotificationActionStore.ReadAsync(token, NotificationActionMask.Complete, consume: true));
        }
        finally
        {
            LocalSqliteStore.SetDataPathForTests(null);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Failed_attempt_keeps_capability_and_parallel_consumption_is_single_use()
    {
        var root = Path.Combine(Path.GetTempPath(), "taskflyout-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        LocalSqliteStore.SetDataPathForTests(root);
        try
        {
            var target = new NotificationActionTarget(3, "Imap", "42", "", NotificationActionMask.MarkRead,
                DateTimeOffset.UtcNow.AddHours(1), AccountId: "a", ContainerId: "inbox", AccountAddress: "a@example.test", ImapUidValidity: 5);
            var token = NotificationActionStore.Store(target);
            Assert.Equal(target, await NotificationActionStore.ReadAsync(token, NotificationActionMask.MarkRead, false));
            Assert.Equal(target, await NotificationActionStore.ReadAsync(token, NotificationActionMask.MarkRead, false));
            var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
                NotificationActionStore.ReadAsync(token, NotificationActionMask.MarkRead, true)));
            Assert.Single(results, result => result != null);
            Assert.Null(await NotificationActionStore.ReadAsync(token, NotificationActionMask.MarkRead, false));
        }
        finally { LocalSqliteStore.SetDataPathForTests(null); Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Snooze_survives_reload_extends_expiry_and_does_not_reschedule_duplicate_clicks()
    {
        var root = Path.Combine(Path.GetTempPath(), "taskflyout-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        LocalSqliteStore.SetDataPathForTests(root);
        try
        {
            var target = new NotificationActionTarget(3, "Microsoft", "task", "2026-10-25",
                NotificationActionMask.Open | NotificationActionMask.Snooze | NotificationActionMask.Complete,
                DateTimeOffset.UtcNow.AddMinutes(1), AccountId: "a", ContainerId: "list", IsTask: true, SnoozeMinutes: 60);
            var token = NotificationActionStore.Store(target);
            var due = await NotificationActionStore.ScheduleSnoozeAsync(token);
            Assert.NotNull(due);
            Assert.InRange(due.Value, DateTimeOffset.UtcNow.AddMinutes(59), DateTimeOffset.UtcNow.AddMinutes(61));
            LocalSqliteStore.SetDataPathForTests(root);
            Assert.Equal(due, await NotificationActionStore.ScheduleSnoozeAsync(token));
            var stored = await NotificationActionStore.ReadAsync(token, NotificationActionMask.Snooze, false);
            Assert.NotNull(stored);
            Assert.Equal(due, stored.SnoozedUntil);
            Assert.True(stored.ExpiresAt > due);
            Assert.Equal("list", stored.ContainerId);
            Assert.True(stored.IsTask);
            Assert.Equal(60, stored.SnoozeMinutes);
        }
        finally { LocalSqliteStore.SetDataPathForTests(null); Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(2, 60)]
    [InlineData(3, -60)]
    public async Task Old_schema_and_expired_capabilities_cannot_act_or_snooze(int schema, int expiresInSeconds)
    {
        var root = Path.Combine(Path.GetTempPath(), "taskflyout-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        LocalSqliteStore.SetDataPathForTests(root);
        try
        {
            var token = NotificationActionStore.Store(new NotificationActionTarget(schema, "Google", "task", "2026-10-25",
                NotificationActionMask.Open | NotificationActionMask.Snooze, DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds)));
            Assert.Null(await NotificationActionStore.ReadAsync(token, NotificationActionMask.Open, false));
            Assert.Null(await NotificationActionStore.ScheduleSnoozeAsync(token));
        }
        finally { LocalSqliteStore.SetDataPathForTests(null); Directory.Delete(root, true); }
    }
}
