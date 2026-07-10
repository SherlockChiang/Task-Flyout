using Microsoft.Data.Sqlite;

namespace Task_Flyout.Tests;

public class SqliteRuntimeTests
{
    [Fact]
    public void Bundled_sqlite_is_fixed_and_provider_can_read_and_write()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT sqlite_version();";
        var version = Version.Parse((string)versionCommand.ExecuteScalar()!);
        Assert.True(version >= new Version(3, 50, 2), $"SQLite {version} is affected by GHSA-2m69-gcr7-jv3q.");

        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE sample (value TEXT NOT NULL); INSERT INTO sample VALUES ($value); SELECT value FROM sample;";
        command.Parameters.AddWithValue("$value", "ok");
        Assert.Equal("ok", command.ExecuteScalar());
    }
}
