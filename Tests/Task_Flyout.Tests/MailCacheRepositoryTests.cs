using System.Text.Json;
using System.Text;
using Microsoft.Data.Sqlite;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

[Collection("LocalSqliteStore")]
public class MailCacheRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "TaskFlyoutMailCacheTests", Guid.NewGuid().ToString("N"));

    public MailCacheRepositoryTests()
    {
        Directory.CreateDirectory(_root);
        LocalSqliteStore.SetDataPathForTests(_root);
    }

    [Fact]
    public async Task Protected_cache_round_trips_pending_mutations_across_repository_instances()
    {
        var mutation = new PendingMailMutation
        {
            AccountId = "account",
            FolderId = "folder",
            MessageId = "message",
            ProviderKind = MailAccountKind.Imap,
            ImapUidValidity = 42,
            FailureCount = 2,
            CreatedUtcTicks = DateTimeOffset.UtcNow.UtcTicks,
            NextAttemptUtcTicks = DateTimeOffset.UtcNow.AddMinutes(2).UtcTicks
        };
        string json = JsonSerializer.Serialize(new[] { mutation });

        await new MailCacheRepository().SaveAsync(json);
        string? loaded = new MailCacheRepository().Load();

        var restored = Assert.Single(JsonSerializer.Deserialize<List<PendingMailMutation>>(loaded!)!);
        Assert.Equal(mutation.AccountId, restored.AccountId);
        Assert.Equal(mutation.ImapUidValidity, restored.ImapUidValidity);
        Assert.Equal(mutation.NextAttemptUtcTicks, restored.NextAttemptUtcTicks);
        Assert.DoesNotContain("account", Encoding.UTF8.GetString(ReadStoredBlob()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_overwrites_previous_snapshot_and_delete_removes_it()
    {
        var repository = new MailCacheRepository();
        await repository.SaveAsync("first snapshot");
        await repository.SaveAsync("second snapshot");

        Assert.Equal("second snapshot", repository.Load());

        await repository.DeleteAsync();
        Assert.Null(repository.Load());
    }

    [Fact]
    public async Task Queue_cleanup_and_limit_survive_restart_after_resave()
    {
        var now = DateTimeOffset.UtcNow;
        var queue = Enumerable.Range(0, 501)
            .Select(index => new PendingMailMutation
            {
                AccountId = index == 500 ? "removed-account" : "account",
                FolderId = "folder",
                MessageId = $"message-{index}",
                ProviderKind = MailAccountKind.Google,
                CreatedUtcTicks = now.AddMinutes(index).UtcTicks,
                NextAttemptUtcTicks = now.UtcTicks
            })
            .ToList();
        queue.Add(new PendingMailMutation
        {
            AccountId = "expired-account",
            FolderId = "folder",
            MessageId = "expired",
            ProviderKind = MailAccountKind.Google,
            CreatedUtcTicks = now.AddDays(-8).UtcTicks
        });
        var repository = new MailCacheRepository();
        await repository.SaveAsync(JsonSerializer.Serialize(queue));

        var restored = JsonSerializer.Deserialize<List<PendingMailMutation>>(new MailCacheRepository().Load()!)!;
        Assert.Equal(1, MailPendingMutationPolicy.RemoveExpired(restored, now));
        MailPendingMutationPolicy.Upsert(restored, new PendingMailMutation
        {
            AccountId = "account",
            FolderId = "folder",
            MessageId = "newest",
            ProviderKind = MailAccountKind.Google
        }, now.AddDays(1), maximumCount: 500);
        MailPendingMutationPolicy.RemoveAccount(restored, "removed-account");
        await repository.SaveAsync(JsonSerializer.Serialize(restored));

        var afterRestart = JsonSerializer.Deserialize<List<PendingMailMutation>>(new MailCacheRepository().Load()!)!;
        Assert.True(afterRestart.Count <= 500);
        Assert.DoesNotContain(afterRestart, mutation => mutation.MessageId == "expired");
        Assert.DoesNotContain(afterRestart, mutation => mutation.AccountId == "removed-account");
        Assert.Contains(afterRestart, mutation => mutation.MessageId == "newest");
    }

    public void Dispose()
    {
        LocalSqliteStore.SetDataPathForTests(null);
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private byte[] ReadStoredBlob()
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(_root, "taskflyout_store.db")}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM protected_store WHERE scope = 'mail' AND key = 'cache';";
        return command.ExecuteScalar() as byte[] ?? Array.Empty<byte>();
    }
}

[CollectionDefinition("LocalSqliteStore", DisableParallelization = true)]
public sealed class LocalSqliteStoreCollection
{
}
