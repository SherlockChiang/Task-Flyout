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
}
