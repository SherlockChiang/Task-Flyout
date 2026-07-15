using Microsoft.Data.Sqlite;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

[Collection("LocalSqliteStore")]
public class ComposeDraftRepositoryTests
{
    [Fact]
    public async Task Repository_round_trips_protected_draft_without_plaintext_blob()
    {
        var root = Path.Combine(Path.GetTempPath(), "taskflyout-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        LocalSqliteStore.SetDataPathForTests(root);
        try
        {
            var repository = new ComposeDraftRepository();
            var draft = new ComposeDraft(1, "account", "private@example.test", "Private subject", "Private body", DateTimeOffset.UtcNow);
            await repository.SaveAsync(draft);

            Assert.Equal(draft.Subject, repository.Load()?.Subject);
            using var connection = new SqliteConnection($"Data Source={Path.Combine(root, "taskflyout_store.db")}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM protected_store WHERE scope = 'mail_compose' AND key = 'active';";
            var bytes = Assert.IsType<byte[]>(command.ExecuteScalar());
            var stored = Convert.ToBase64String(bytes);
            Assert.DoesNotContain("Private subject", stored, StringComparison.Ordinal);
            Assert.DoesNotContain("Private body", stored, StringComparison.Ordinal);

            await repository.DeleteAsync();
            Assert.Null(repository.Load());
        }
        finally
        {
            LocalSqliteStore.SetDataPathForTests(null);
            Directory.Delete(root, recursive: true);
        }
    }
}
