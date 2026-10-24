using Microsoft.Data.Sqlite;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    public static class LocalSqliteStore
    {
        private const int BusyTimeoutMilliseconds = 5000;
        private const string StoreFileName = "taskflyout_store.db";
        private const string MigrationMarkerFileName = "taskflyout_store.migration.v1";
        private const string MigrationMarkerContent = "1\n";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TaskFlyout.LocalCache.v1");
        private static readonly object InitLock = new();
        private static volatile bool _initialized;
        private static string? _connectionString;
        private static int _checkpointPending;
#if TASKFLYOUT_TESTS
        private static string? _testDataPath;
        private static string? _testRoamingDataPath;
#endif

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
            await Task.Run(() => WriteProtectedText(scope, key, text));
        }

        public static async Task DeleteProtectedTextAsync(string scope, string key)
        {
            EnsureInitialized();
            await using var connection = await OpenConnectionAsync();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM protected_store WHERE scope = $scope AND key = $key;";
                command.Parameters.AddWithValue("$scope", scope);
                command.Parameters.AddWithValue("$key", key);
                await command.ExecuteNonQueryAsync();
            }
            await TruncateWalAsync(connection);
        }

        public static async Task DeleteProtectedScopeAsync(string scope)
        {
            EnsureInitialized();
            await using var connection = await OpenConnectionAsync();
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM protected_store WHERE scope = $scope;";
                command.Parameters.AddWithValue("$scope", scope);
                await command.ExecuteNonQueryAsync();
            }
            await TruncateWalAsync(connection);
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            lock (InitLock)
            {
                if (_initialized) return;
                var localPath = GetAppDataPath();
                Directory.CreateDirectory(localPath);
                MigrateRoamingStoreIfNeeded(localPath);
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
                WriteMigrationMarker(localPath);
                _initialized = true;
                TryTruncateWal(connection);
                CleanupRoamingStoreIfMigrated(localPath);
            }
        }

        private static SqliteConnection OpenConnection()
        {
            var connection = CreateConnection();
            connection.Open();
            ExecuteNonQuery(connection, "PRAGMA secure_delete=ON;");
            ExecuteNonQuery(connection, $"PRAGMA busy_timeout={BusyTimeoutMilliseconds};");
            if (Volatile.Read(ref _checkpointPending) != 0)
                TryTruncateWal(connection);
            return connection;
        }

        private static async Task<SqliteConnection> OpenConnectionAsync()
        {
            var connection = CreateConnection();
            try
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = $"PRAGMA secure_delete=ON; PRAGMA busy_timeout={BusyTimeoutMilliseconds};";
                await command.ExecuteNonQueryAsync();
                if (Volatile.Read(ref _checkpointPending) != 0)
                    TryTruncateWal(connection);
                return connection;
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
        }

        private static Task TruncateWalAsync(SqliteConnection connection)
        {
            TryTruncateWal(connection);
            return Task.CompletedTask;
        }

        public static Task FlushPendingCheckpointAsync()
            => Task.Run(() =>
            {
                if (!_initialized) return;
                using var connection = OpenConnection();
                TryTruncateWal(connection);
            });

        private static void TryTruncateWal(SqliteConnection connection)
        {
            bool completed = false;
            try
            {
                ExecuteNonQuery(connection, "PRAGMA busy_timeout=0;");
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                using var reader = command.ExecuteReader();
                completed = reader.Read() && reader.GetInt32(0) == 0;
            }
            catch (SqliteException)
            {
            }
            finally
            {
                try { ExecuteNonQuery(connection, $"PRAGMA busy_timeout={BusyTimeoutMilliseconds};"); }
                catch (SqliteException) { }
            }

            Interlocked.Exchange(ref _checkpointPending, completed ? 0 : 1);
        }

        private static SqliteConnection CreateConnection()
        {
            _connectionString ??= new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(GetAppDataPath(), StoreFileName),
                DefaultTimeout = BusyTimeoutMilliseconds / 1000
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
        {
#if TASKFLYOUT_TESTS
            if (!string.IsNullOrWhiteSpace(_testDataPath)) return _testDataPath;
#endif
            return AppDataPathHelper.LocalRoot;
        }

#if TASKFLYOUT_TESTS
        internal static void SetDataPathForTests(string? path)
            => SetDataPathsForTests(path, null);

        internal static void SetDataPathsForTests(string? localPath, string? roamingPath)
        {
            lock (InitLock)
            {
                SqliteConnection.ClearAllPools();
                _testDataPath = localPath;
                _testRoamingDataPath = roamingPath;
                _connectionString = null;
                _initialized = false;
                _checkpointPending = 0;
            }
        }
#endif

        private static void MigrateRoamingStoreIfNeeded(string localPath)
        {
            var roamingPath = GetRoamingDataPath();
            if (PathsEqual(localPath, roamingPath)) return;

            var sourcePath = Path.Combine(roamingPath, StoreFileName);
            var targetPath = Path.Combine(localPath, StoreFileName);
            if (!File.Exists(sourcePath)) return;

            Directory.CreateDirectory(localPath);
            using var migrationLock = AcquireMigrationLock(localPath);

            // Another Task Flyout instance may have completed the copy while we
            // waited for the lock. Re-check before touching either database.
            if (!File.Exists(sourcePath)) return;

            if (!File.Exists(targetPath))
            {
                CreateMigrationSnapshot(sourcePath, targetPath);
            }
            else
            {
                ValidateDatabase(targetPath);
            }
        }

        private static FileStream AcquireMigrationLock(string localPath)
        {
            var lockPath = Path.Combine(localPath, $"{StoreFileName}.migration.lock");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (true)
            {
                try
                {
                    return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(100);
                }
            }
        }

        private static void CreateMigrationSnapshot(string sourcePath, string targetPath)
        {
            var temporaryPath = $"{targetPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = sourcePath,
                    Mode = SqliteOpenMode.ReadWrite,
                    DefaultTimeout = BusyTimeoutMilliseconds / 1000
                }.ToString()))
                {
                    source.Open();
                    ValidateAndCheckpoint(source, sourcePath);

                    using var vacuum = source.CreateCommand();
                    vacuum.CommandText = "VACUUM INTO $targetPath;";
                    vacuum.Parameters.AddWithValue("$targetPath", temporaryPath);
                    vacuum.ExecuteNonQuery();
                }

                ValidateDatabase(temporaryPath);
                SqliteConnection.ClearAllPools();
                File.Move(temporaryPath, targetPath);
            }
            finally
            {
                TryDeleteFile(temporaryPath);
            }
        }

        private static void ValidateDatabase(string path)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadOnly,
                DefaultTimeout = BusyTimeoutMilliseconds / 1000
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            var result = command.ExecuteScalar() as string;
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"SQLite integrity check failed for '{path}'.");
        }

        private static void ValidateAndCheckpoint(SqliteConnection connection, string path)
        {
            using (var checkpoint = connection.CreateCommand())
            {
                checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                using var reader = checkpoint.ExecuteReader();
                if (!reader.Read() || reader.GetInt32(0) != 0)
                    throw new IOException($"SQLite WAL checkpoint is busy for '{path}'.");
            }

            using var integrity = connection.CreateCommand();
            integrity.CommandText = "PRAGMA integrity_check;";
            var result = integrity.ExecuteScalar() as string;
            if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"SQLite integrity check failed for '{path}'.");
        }

        private static void WriteMigrationMarker(string localPath)
        {
            var markerPath = Path.Combine(localPath, MigrationMarkerFileName);
            if (File.Exists(markerPath) &&
                string.Equals(File.ReadAllText(markerPath), MigrationMarkerContent, StringComparison.Ordinal))
                return;

            var temporaryPath = $"{markerPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 256,
                    options: FileOptions.WriteThrough))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 256, leaveOpen: true))
                {
                    writer.Write(MigrationMarkerContent);
                    writer.Flush();
                    stream.Flush(flushToDisk: true);
                }

                if (File.Exists(markerPath))
                    File.Replace(temporaryPath, markerPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
                else
                    File.Move(temporaryPath, markerPath);
            }
            finally
            {
                TryDeleteFile(temporaryPath);
            }
        }

        private static void CleanupRoamingStoreIfMigrated(string localPath)
        {
            var roamingPath = GetRoamingDataPath();
            if (PathsEqual(localPath, roamingPath)) return;

            var sourcePath = Path.Combine(roamingPath, StoreFileName);
            var markerPath = Path.Combine(localPath, MigrationMarkerFileName);
            if (!File.Exists(sourcePath) || !File.Exists(markerPath)) return;
            if (!string.Equals(File.ReadAllText(markerPath), MigrationMarkerContent, StringComparison.Ordinal)) return;

            foreach (var path in new[] { sourcePath, sourcePath + "-wal", sourcePath + "-shm" })
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (IOException ex)
                {
                    Debug.WriteLine($"Could not remove migrated roaming store '{path}': {ex.Message}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    Debug.WriteLine($"Could not remove migrated roaming store '{path}': {ex.Message}");
                }
            }
        }

        private static bool PathsEqual(string left, string right)
            => string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }

        private static string GetRoamingDataPath()
        {
#if TASKFLYOUT_TESTS
            if (!string.IsNullOrWhiteSpace(_testRoamingDataPath)) return _testRoamingDataPath;
            if (!string.IsNullOrWhiteSpace(_testDataPath))
                return Path.Combine(_testDataPath, "__legacy_roaming");
#endif
            return AppDataPathHelper.RoamingRoot;
        }
    }
}
