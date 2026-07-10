using Task_Flyout.Services;

namespace Task_Flyout.Tests;

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
}
