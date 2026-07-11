using Task_Flyout.Services;

namespace Task_Flyout.Tests;

[Collection("LocalSqliteStore")]
public class LocalSqliteStoreTests
{
    [Fact]
    public async Task Protected_store_round_trips_and_deletes_data_in_isolated_database()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "taskflyout-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        LocalSqliteStore.SetDataPathForTests(testRoot);

        try
        {
            LocalSqliteStore.WriteProtectedText("test-scope", "first", "secret value");
            LocalSqliteStore.WriteProtectedText("test-scope", "second", "another value");

            Assert.Equal("secret value", LocalSqliteStore.ReadProtectedText("test-scope", "first"));
            Assert.True(File.Exists(Path.Combine(testRoot, "taskflyout_store.db")));

            await LocalSqliteStore.DeleteProtectedTextAsync("test-scope", "first");
            Assert.Null(LocalSqliteStore.ReadProtectedText("test-scope", "first"));

            await LocalSqliteStore.DeleteProtectedScopeAsync("test-scope");
            Assert.Null(LocalSqliteStore.ReadProtectedText("test-scope", "second"));
        }
        finally
        {
            LocalSqliteStore.SetDataPathForTests(null);
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Verification_code_token_is_single_use()
    {
        var testRoot = CreateIsolatedStore();
        var token = Guid.NewGuid().ToString("N");
        LocalSqliteStore.WriteProtectedText(
            "notification-verification-code",
            token,
            $"{DateTimeOffset.UtcNow.UtcTicks}|123456");

        try
        {
            Assert.Equal("123456", await VerificationCodeStore.TakeAsync(token));
            Assert.Null(await VerificationCodeStore.TakeAsync(token));
        }
        finally
        {
            DeleteIsolatedStore(testRoot);
        }
    }

    [Fact]
    public async Task Verification_code_token_rejects_and_deletes_expired_value()
    {
        var testRoot = CreateIsolatedStore();
        var token = Guid.NewGuid().ToString("N");
        var expiredAt = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(11));
        LocalSqliteStore.WriteProtectedText(
            "notification-verification-code",
            token,
            $"{expiredAt.UtcTicks}|654321");

        try
        {
            Assert.Null(await VerificationCodeStore.TakeAsync(token));
            Assert.Null(LocalSqliteStore.ReadProtectedText("notification-verification-code", token));
        }
        finally
        {
            DeleteIsolatedStore(testRoot);
        }
    }

    [Fact]
    public async Task Protected_store_waits_for_short_write_lock_contention()
    {
        var testRoot = CreateIsolatedStore();
        try
        {
            LocalSqliteStore.WriteProtectedText("test-scope", "initial", "value");
            var databasePath = Path.Combine(testRoot, "taskflyout_store.db");
            using var blocker = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={databasePath}");
            blocker.Open();
            using var transaction = blocker.BeginTransaction();
            using (var command = blocker.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "UPDATE protected_store SET updated_ticks = updated_ticks + 1 WHERE scope = 'test-scope';";
                command.ExecuteNonQuery();
            }

            var pendingWrite = Task.Run(() => LocalSqliteStore.WriteProtectedText("mail", "cache", "snapshot"));
            await Task.Delay(150);
            Assert.False(pendingWrite.IsCompleted);
            transaction.Commit();
            await pendingWrite.WaitAsync(TimeSpan.FromSeconds(3));

            Assert.Equal("snapshot", LocalSqliteStore.ReadProtectedText("mail", "cache"));
        }
        finally
        {
            DeleteIsolatedStore(testRoot);
        }
    }

    [Fact]
    public async Task Sensitive_delete_defers_checkpoint_while_reader_is_active_and_retries_on_access()
    {
        var testRoot = CreateIsolatedStore();
        try
        {
            LocalSqliteStore.WriteProtectedText("mail", "cache", new string('x', 20_000));
            var databasePath = Path.Combine(testRoot, "taskflyout_store.db");
            using (var readerConnection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={databasePath}"))
            {
                readerConnection.Open();
                using var transaction = readerConnection.BeginTransaction(deferred: true);
                using var command = readerConnection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "SELECT value FROM protected_store WHERE scope = 'mail' AND key = 'cache';";
                using var reader = command.ExecuteReader();
                Assert.True(reader.Read());

                await LocalSqliteStore.DeleteProtectedTextAsync("mail", "cache").WaitAsync(TimeSpan.FromSeconds(1));
                Assert.Null(LocalSqliteStore.ReadProtectedText("mail", "cache"));
            }

            Assert.Null(LocalSqliteStore.ReadProtectedText("mail", "cache"));
            var walPath = databasePath + "-wal";
            Assert.True(!File.Exists(walPath) || new FileInfo(walPath).Length == 0);
        }
        finally
        {
            DeleteIsolatedStore(testRoot);
        }
    }

    [Fact]
    public async Task Explicit_checkpoint_flush_does_not_initialize_unused_store()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "taskflyout-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        LocalSqliteStore.SetDataPathForTests(testRoot);
        try
        {
            await LocalSqliteStore.FlushPendingCheckpointAsync();
            Assert.False(File.Exists(Path.Combine(testRoot, "taskflyout_store.db")));
        }
        finally
        {
            DeleteIsolatedStore(testRoot);
        }
    }

    private static string CreateIsolatedStore()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "taskflyout-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        LocalSqliteStore.SetDataPathForTests(testRoot);
        return testRoot;
    }

    private static void DeleteIsolatedStore(string testRoot)
    {
        LocalSqliteStore.SetDataPathForTests(null);
        Directory.Delete(testRoot, recursive: true);
    }
}
