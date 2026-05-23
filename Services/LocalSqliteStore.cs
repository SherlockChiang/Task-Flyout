using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    public static class LocalSqliteStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TaskFlyout.LocalCache.v1");
        private static readonly object InitLock = new();
        private static bool _initialized;
        private static string? _connectionString;

        public static string? ReadProtectedText(string scope, string key)
        {
            EnsureInitialized();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM protected_store WHERE scope = $scope AND key = $key LIMIT 1;";
            command.Parameters.AddWithValue("$scope", scope);
            command.Parameters.AddWithValue("$key", key);

            var value = command.ExecuteScalar();
            if (value is not byte[] bytes || bytes.Length == 0) return null;

            try
            {
                var decrypted = ProtectedData.Unprotect(bytes, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                return null;
            }
        }

        public static void WriteProtectedText(string scope, string key, string text)
        {
            EnsureInitialized();
            var bytes = Encoding.UTF8.GetBytes(text ?? "");
            var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
INSERT INTO protected_store(scope, key, value, updated_ticks)
VALUES ($scope, $key, $value, $updatedTicks)
ON CONFLICT(scope, key) DO UPDATE SET value = excluded.value, updated_ticks = excluded.updated_ticks;
""";
            command.Parameters.AddWithValue("$scope", scope);
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.Add("$value", SqliteType.Blob).Value = encrypted;
            command.Parameters.AddWithValue("$updatedTicks", DateTimeOffset.UtcNow.UtcTicks);
            command.ExecuteNonQuery();
        }

        public static async Task WriteProtectedTextAsync(string scope, string key, string text)
        {
            EnsureInitialized();
            var bytes = Encoding.UTF8.GetBytes(text ?? "");
            var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);

            await using var connection = CreateConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
INSERT INTO protected_store(scope, key, value, updated_ticks)
VALUES ($scope, $key, $value, $updatedTicks)
ON CONFLICT(scope, key) DO UPDATE SET value = excluded.value, updated_ticks = excluded.updated_ticks;
""";
            command.Parameters.AddWithValue("$scope", scope);
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.Add("$value", SqliteType.Blob).Value = encrypted;
            command.Parameters.AddWithValue("$updatedTicks", DateTimeOffset.UtcNow.UtcTicks);
            await command.ExecuteNonQueryAsync();
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            lock (InitLock)
            {
                if (_initialized) return;
                Directory.CreateDirectory(GetAppDataPath());
                using var connection = OpenConnection();
                ExecuteNonQuery(connection, "PRAGMA journal_mode=WAL;");
                ExecuteNonQuery(connection, "PRAGMA synchronous=NORMAL;");
                ExecuteNonQuery(connection, """
CREATE TABLE IF NOT EXISTS protected_store (
    scope TEXT NOT NULL,
    key TEXT NOT NULL,
    value BLOB NOT NULL,
    updated_ticks INTEGER NOT NULL,
    PRIMARY KEY(scope, key)
);
""");
                _initialized = true;
            }
        }

        private static SqliteConnection OpenConnection()
        {
            var connection = CreateConnection();
            connection.Open();
            return connection;
        }

        private static SqliteConnection CreateConnection()
        {
            _connectionString ??= new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(GetAppDataPath(), "taskflyout_store.db")
            }.ToString();
            return new SqliteConnection(_connectionString);
        }

        private static void ExecuteNonQuery(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static string GetAppDataPath()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskFlyout");
    }
}
