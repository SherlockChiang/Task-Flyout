using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Task_Flyout.Services
{
    internal sealed record RssArticleRecord(
        string Id,
        string SubscriptionId,
        string FeedTitle,
        string Title,
        string Link,
        string Summary,
        string ImageUrl,
        string LocalImagePath,
        long PublishedUtcTicks);

    internal sealed record RssFolderRecord(string Id, string Name, int SortOrder);

    internal sealed record RssSubscriptionRecord(
        string Id,
        string Title,
        string Url,
        string FolderId,
        string ImageUrl,
        string LocalImagePath,
        long LastFetchedUtcTicks,
        bool AllowInsecureHttp = false);

    internal sealed record RssArticleWriteRecord(
        string Id,
        string SubscriptionId,
        string FeedTitle,
        string Title,
        string Link,
        string Summary,
        string HtmlContent,
        string ImageUrl,
        string LocalImagePath,
        long PublishedUtcTicks);

    internal sealed record RssRepositorySnapshot(
        List<RssFolderRecord> Folders,
        List<RssSubscriptionRecord> Subscriptions,
        List<RssArticleRecord> Articles);

    internal sealed class RssSqliteRepository
    {
        private const int SchemaVersion = 1;
        private const int BusyTimeoutMilliseconds = 5000;
        private const string SensitiveDataMigrationKey = "sensitive_data_dpapi_v1";
        private readonly string _connectionString;
        private readonly Func<bool> _isUnavailable;
        private readonly object _initializationLock = new();
        private bool _initialized;
        private int _checkpointPending;

        public RssSqliteRepository(string databasePath, Func<bool>? isUnavailable = null)
        {
            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                DefaultTimeout = BusyTimeoutMilliseconds / 1000
            }.ToString();
            _isUnavailable = isUnavailable ?? (() => false);
        }

        public void Initialize()
        {
            if (_initialized) return;

            lock (_initializationLock)
            {
                if (_initialized) return;

                var databasePath = new SqliteConnectionStringBuilder(_connectionString).DataSource;
                var directory = Path.GetDirectoryName(databasePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                using var connection = OpenConnection();
                ExecuteNonQuery(connection, "PRAGMA journal_mode=WAL;");
                ExecuteNonQuery(connection, "PRAGMA synchronous=NORMAL;");
                ExecuteNonQuery(connection, "PRAGMA secure_delete=ON;");
                ExecuteNonQuery(connection, """
CREATE TABLE IF NOT EXISTS metadata (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
""");
                ExecuteNonQuery(connection, """
CREATE TABLE IF NOT EXISTS rss_folders (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    sort_order INTEGER NOT NULL DEFAULT 0
);
""");
                ExecuteNonQuery(connection, """
CREATE TABLE IF NOT EXISTS rss_subscriptions (
    id TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    url TEXT NOT NULL,
    folder_id TEXT NOT NULL DEFAULT '',
    image_url TEXT NOT NULL DEFAULT '',
    local_image_path TEXT NOT NULL DEFAULT '',
    last_fetched_ticks INTEGER NOT NULL DEFAULT 0,
    allow_insecure_http INTEGER NOT NULL DEFAULT 0
);
""");
                TryAddColumn(connection, "rss_subscriptions", "image_url", "TEXT NOT NULL DEFAULT ''");
                TryAddColumn(connection, "rss_subscriptions", "local_image_path", "TEXT NOT NULL DEFAULT ''");
                TryAddColumn(connection, "rss_subscriptions", "allow_insecure_http", "INTEGER NOT NULL DEFAULT 0");
                ExecuteNonQuery(connection, """
CREATE TABLE IF NOT EXISTS rss_articles (
    id TEXT PRIMARY KEY,
    subscription_id TEXT NOT NULL,
    feed_title TEXT NOT NULL,
    title TEXT NOT NULL,
    link TEXT NOT NULL,
    summary TEXT NOT NULL,
    html_content TEXT NOT NULL,
    image_url TEXT NOT NULL,
    local_image_path TEXT NOT NULL,
    published_ticks INTEGER NOT NULL DEFAULT 0
);
""");
                ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS idx_rss_articles_published ON rss_articles(published_ticks DESC);");
                ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS idx_rss_articles_subscription_published ON rss_articles(subscription_id, published_ticks DESC);");
                ExecuteNonQuery(connection, "CREATE INDEX IF NOT EXISTS idx_rss_subscriptions_folder ON rss_subscriptions(folder_id);");

                using var command = connection.CreateCommand();
                command.CommandText = "INSERT OR REPLACE INTO metadata(key, value) VALUES ('schema_version', $version);";
                command.Parameters.AddWithValue("$version", SchemaVersion.ToString());
                command.ExecuteNonQuery();
                _initialized = true;
                TryCheckpointDeletedData(connection);
            }
        }

        public List<RssArticleRecord> QueryArticlesPage(string? subscriptionId, string? folderId, int skip, int take)
        {
            Initialize();
            using var connection = OpenConnection();
            return ReadArticles(connection, subscriptionId, folderId, skip, take);
        }

        public RssRepositorySnapshot LoadSnapshot(int maximumArticleCount)
        {
            Initialize();
            var folders = new List<RssFolderRecord>();
            var subscriptions = new List<RssSubscriptionRecord>();
            using var connection = OpenConnection();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, name, sort_order FROM rss_folders ORDER BY sort_order;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    folders.Add(new RssFolderRecord(
                        reader.GetString(0),
                        RssSensitiveDataProtector.Unprotect(reader.GetString(1)),
                        reader.GetInt32(2)));
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, title, url, folder_id, image_url, local_image_path, last_fetched_ticks, allow_insecure_http FROM rss_subscriptions;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    subscriptions.Add(new RssSubscriptionRecord(
                        reader.GetString(0),
                        RssSensitiveDataProtector.Unprotect(reader.GetString(1)),
                        RssSensitiveDataProtector.Unprotect(reader.GetString(2)),
                        reader.GetString(3),
                        RssSensitiveDataProtector.Unprotect(reader.GetString(4)),
                        reader.GetString(5),
                        reader.GetInt64(6),
                        reader.GetInt64(7) != 0));
            }

            return new RssRepositorySnapshot(
                folders,
                subscriptions,
                ReadArticles(connection, null, null, 0, maximumArticleCount));
        }

        public List<string> LoadArticleImagePaths()
        {
            Initialize();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT local_image_path FROM rss_articles WHERE local_image_path <> '';";
            using var reader = command.ExecuteReader();
            var paths = new List<string>();
            while (reader.Read())
                paths.Add(reader.GetString(0));
            return paths;
        }

        public string GetArticleHtml(string articleId)
        {
            if (string.IsNullOrWhiteSpace(articleId)) return "";
            Initialize();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT html_content FROM rss_articles WHERE id = $id LIMIT 1;";
            command.Parameters.AddWithValue("$id", articleId);
            return RssSensitiveDataProtector.Unprotect(command.ExecuteScalar() as string ?? "");
        }

        public void UpsertFolder(RssFolderRecord folder)
        {
            Initialize();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            WriteFolder(command, folder);
            command.ExecuteNonQuery();
        }

        public void UpsertSubscription(RssSubscriptionRecord subscription)
        {
            Initialize();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            WriteSubscription(command, subscription);
            command.ExecuteNonQuery();
        }

        public void UpsertArticle(RssArticleWriteRecord article)
        {
            Initialize();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            WriteArticle(command, article);
            command.ExecuteNonQuery();
        }

        public void SaveRefresh(RssSubscriptionRecord subscription, IEnumerable<RssArticleWriteRecord> articles)
        {
            Initialize();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using (var subscriptionCommand = connection.CreateCommand())
            {
                subscriptionCommand.Transaction = transaction;
                WriteSubscription(subscriptionCommand, subscription);
                subscriptionCommand.ExecuteNonQuery();
            }

            foreach (var article in articles)
            {
                using var articleCommand = connection.CreateCommand();
                articleCommand.Transaction = transaction;
                WriteArticle(articleCommand, article);
                articleCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        public void MigrateSensitiveData()
        {
            Initialize();
            using var connection = OpenConnection();
            using (var migrationCheck = connection.CreateCommand())
            {
                migrationCheck.CommandText = "SELECT value FROM metadata WHERE key = $key LIMIT 1;";
                migrationCheck.Parameters.AddWithValue("$key", SensitiveDataMigrationKey);
                if (string.Equals(migrationCheck.ExecuteScalar() as string, "complete", StringComparison.Ordinal))
                    return;
            }

            using var transaction = connection.BeginTransaction();
            ProtectColumns(connection, transaction, "rss_folders", new[] { "name" });
            ProtectColumns(connection, transaction, "rss_subscriptions", new[] { "title", "url", "image_url" });
            ProtectColumns(connection, transaction, "rss_articles", new[] { "feed_title", "title", "link", "summary", "html_content", "image_url" });

            using var migrationComplete = connection.CreateCommand();
            migrationComplete.Transaction = transaction;
            migrationComplete.CommandText = "INSERT OR REPLACE INTO metadata(key, value) VALUES ($key, 'complete');";
            migrationComplete.Parameters.AddWithValue("$key", SensitiveDataMigrationKey);
            migrationComplete.ExecuteNonQuery();
            transaction.Commit();
            TryCheckpointDeletedData(connection);
        }

        public void TrimArticles(int maximumCount)
        {
            Initialize();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
DELETE FROM rss_articles
WHERE id NOT IN (
    SELECT id FROM rss_articles ORDER BY published_ticks DESC LIMIT $maximumCount
);
""";
            command.Parameters.AddWithValue("$maximumCount", Math.Max(0, maximumCount));
            command.ExecuteNonQuery();
            TryCheckpointDeletedData(connection);
        }

        public void SaveSnapshot(
            IEnumerable<RssFolderRecord> folders,
            IEnumerable<RssSubscriptionRecord> subscriptions,
            IEnumerable<RssArticleWriteRecord> articles,
            int maximumArticleCount)
        {
            Initialize();
            var folderList = folders.ToList();
            var subscriptionList = subscriptions.ToList();
            var articleList = articles
                .OrderByDescending(article => article.PublishedUtcTicks)
                .Take(Math.Max(0, maximumArticleCount))
                .ToList();

            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            foreach (var folder in folderList)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                WriteFolder(command, folder);
                command.ExecuteNonQuery();
            }
            DeleteRowsNotIn(connection, transaction, "rss_folders", folderList.Select(folder => folder.Id).ToHashSet(StringComparer.Ordinal));

            foreach (var subscription in subscriptionList)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                WriteSubscription(command, subscription);
                command.ExecuteNonQuery();
            }
            DeleteRowsNotIn(connection, transaction, "rss_subscriptions", subscriptionList.Select(subscription => subscription.Id).ToHashSet(StringComparer.Ordinal));

            foreach (var article in articleList)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                WriteArticle(command, article);
                command.ExecuteNonQuery();
            }
            DeleteRowsNotIn(connection, transaction, "rss_articles", articleList.Select(article => article.Id).ToHashSet(StringComparer.Ordinal));
            transaction.Commit();
            TryCheckpointDeletedData(connection);
        }

        public void RemoveFolder(string folderId)
        {
            Initialize();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = "UPDATE rss_subscriptions SET folder_id = '' WHERE folder_id = $folderId;";
                update.Parameters.AddWithValue("$folderId", folderId);
                update.ExecuteNonQuery();
            }
            using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM rss_folders WHERE id = $folderId;";
                delete.Parameters.AddWithValue("$folderId", folderId);
                delete.ExecuteNonQuery();
            }
            transaction.Commit();
            TryCheckpointDeletedData(connection);
        }

        public void RemoveSubscription(string subscriptionId)
        {
            Initialize();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            using (var deleteArticles = connection.CreateCommand())
            {
                deleteArticles.Transaction = transaction;
                deleteArticles.CommandText = "DELETE FROM rss_articles WHERE subscription_id = $subscriptionId;";
                deleteArticles.Parameters.AddWithValue("$subscriptionId", subscriptionId);
                deleteArticles.ExecuteNonQuery();
            }
            using (var deleteSubscription = connection.CreateCommand())
            {
                deleteSubscription.Transaction = transaction;
                deleteSubscription.CommandText = "DELETE FROM rss_subscriptions WHERE id = $subscriptionId;";
                deleteSubscription.Parameters.AddWithValue("$subscriptionId", subscriptionId);
                deleteSubscription.ExecuteNonQuery();
            }
            transaction.Commit();
            TryCheckpointDeletedData(connection);
        }

        public void UpdateArticleFeedTitle(string subscriptionId, string title)
        {
            Initialize();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE rss_articles SET feed_title = $title WHERE subscription_id = $subscriptionId;";
            command.Parameters.AddWithValue("$title", RssSensitiveDataProtector.Protect(title));
            command.Parameters.AddWithValue("$subscriptionId", subscriptionId);
            command.ExecuteNonQuery();
        }

        public void FlushCheckpoint()
        {
            if (!_initialized || _isUnavailable()) return;
            using var connection = OpenConnection();
            TryCheckpointDeletedData(connection);
        }

        private static void WriteSubscription(SqliteCommand command, RssSubscriptionRecord subscription)
        {
            command.CommandText = """
INSERT INTO rss_subscriptions(id, title, url, folder_id, image_url, local_image_path, last_fetched_ticks, allow_insecure_http)
VALUES ($id, $title, $url, $folderId, $imageUrl, $localImagePath, $lastFetchedTicks, $allowInsecureHttp)
ON CONFLICT(id) DO UPDATE SET
    title = excluded.title,
    url = excluded.url,
    folder_id = excluded.folder_id,
    image_url = excluded.image_url,
    local_image_path = CASE
        WHEN excluded.local_image_path = '' THEN rss_subscriptions.local_image_path
        ELSE excluded.local_image_path
    END,
    last_fetched_ticks = excluded.last_fetched_ticks,
    allow_insecure_http = excluded.allow_insecure_http;
""";
            command.Parameters.AddWithValue("$id", subscription.Id);
            command.Parameters.AddWithValue("$title", RssSensitiveDataProtector.Protect(subscription.Title));
            command.Parameters.AddWithValue("$url", RssSensitiveDataProtector.Protect(subscription.Url));
            command.Parameters.AddWithValue("$folderId", subscription.FolderId);
            command.Parameters.AddWithValue("$imageUrl", RssSensitiveDataProtector.Protect(subscription.ImageUrl));
            command.Parameters.AddWithValue("$localImagePath", subscription.LocalImagePath);
            command.Parameters.AddWithValue("$lastFetchedTicks", subscription.LastFetchedUtcTicks);
            command.Parameters.AddWithValue("$allowInsecureHttp", subscription.AllowInsecureHttp ? 1 : 0);
        }

        private static void WriteFolder(SqliteCommand command, RssFolderRecord folder)
        {
            command.CommandText = """
INSERT INTO rss_folders(id, name, sort_order) VALUES ($id, $name, $sortOrder)
ON CONFLICT(id) DO UPDATE SET name = excluded.name, sort_order = excluded.sort_order;
""";
            command.Parameters.AddWithValue("$id", folder.Id);
            command.Parameters.AddWithValue("$name", RssSensitiveDataProtector.Protect(folder.Name));
            command.Parameters.AddWithValue("$sortOrder", folder.SortOrder);
        }

        private static void WriteArticle(SqliteCommand command, RssArticleWriteRecord article)
        {
            command.CommandText = """
INSERT INTO rss_articles(id, subscription_id, feed_title, title, link, summary, html_content, image_url, local_image_path, published_ticks)
VALUES ($id, $subscriptionId, $feedTitle, $title, $link, $summary, $htmlContent, $imageUrl, $localImagePath, $publishedTicks)
ON CONFLICT(id) DO UPDATE SET
    subscription_id = excluded.subscription_id,
    feed_title = excluded.feed_title,
    title = excluded.title,
    link = excluded.link,
    summary = excluded.summary,
    html_content = excluded.html_content,
    image_url = excluded.image_url,
    local_image_path = CASE
        WHEN excluded.local_image_path = '' THEN rss_articles.local_image_path
        ELSE excluded.local_image_path
    END,
    published_ticks = excluded.published_ticks;
""";
            command.Parameters.AddWithValue("$id", article.Id);
            command.Parameters.AddWithValue("$subscriptionId", article.SubscriptionId);
            command.Parameters.AddWithValue("$feedTitle", RssSensitiveDataProtector.Protect(article.FeedTitle));
            command.Parameters.AddWithValue("$title", RssSensitiveDataProtector.Protect(article.Title));
            command.Parameters.AddWithValue("$link", RssSensitiveDataProtector.Protect(article.Link));
            command.Parameters.AddWithValue("$summary", RssSensitiveDataProtector.Protect(article.Summary));
            command.Parameters.AddWithValue("$htmlContent", RssSensitiveDataProtector.Protect(article.HtmlContent));
            command.Parameters.AddWithValue("$imageUrl", RssSensitiveDataProtector.Protect(article.ImageUrl));
            command.Parameters.AddWithValue("$localImagePath", article.LocalImagePath);
            command.Parameters.AddWithValue("$publishedTicks", article.PublishedUtcTicks);
        }

        private static void ProtectColumns(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string table,
            IReadOnlyList<string> columns)
        {
            using var select = connection.CreateCommand();
            select.Transaction = transaction;
            select.CommandText = $"SELECT id, {string.Join(", ", columns)} FROM {table};";
            using var reader = select.ExecuteReader();
            var rows = new List<(string Id, string[] Values)>();
            while (reader.Read())
            {
                var values = new string[columns.Count];
                for (int index = 0; index < columns.Count; index++)
                    values[index] = reader.GetString(index + 1);
                rows.Add((reader.GetString(0), values));
            }
            reader.Close();

            foreach (var row in rows)
            {
                if (row.Values.All(RssSensitiveDataProtector.IsProtected)) continue;

                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                var assignments = new string[columns.Count];
                for (int index = 0; index < columns.Count; index++)
                {
                    string parameter = $"$value{index}";
                    assignments[index] = $"{columns[index]} = {parameter}";
                    update.Parameters.AddWithValue(parameter, RssSensitiveDataProtector.Protect(
                        RssSensitiveDataProtector.Unprotect(row.Values[index])));
                }
                update.CommandText = $"UPDATE {table} SET {string.Join(", ", assignments)} WHERE id = $id;";
                update.Parameters.AddWithValue("$id", row.Id);
                update.ExecuteNonQuery();
            }
        }

        private static List<RssArticleRecord> ReadArticles(
            SqliteConnection connection,
            string? subscriptionId,
            string? folderId,
            int skip,
            int take)
        {
            bool hasSubscription = !string.IsNullOrWhiteSpace(subscriptionId);
            bool hasFolder = folderId != null;
            using var command = connection.CreateCommand();
            command.CommandText = """
SELECT a.id, a.subscription_id, a.feed_title, a.title, a.link, a.summary, a.image_url, a.local_image_path, a.published_ticks
FROM rss_articles a
WHERE ($hasSubscription = 0 OR a.subscription_id = $subscriptionId)
  AND ($hasFolder = 0 OR EXISTS (
      SELECT 1 FROM rss_subscriptions s
      WHERE s.id = a.subscription_id AND s.folder_id = $folderId
  ))
ORDER BY a.published_ticks DESC
LIMIT $take OFFSET $skip;
""";
            command.Parameters.AddWithValue("$hasSubscription", hasSubscription ? 1 : 0);
            command.Parameters.AddWithValue("$subscriptionId", subscriptionId ?? "");
            command.Parameters.AddWithValue("$hasFolder", hasFolder ? 1 : 0);
            command.Parameters.AddWithValue("$folderId", folderId ?? "");
            command.Parameters.AddWithValue("$take", Math.Max(0, take));
            command.Parameters.AddWithValue("$skip", Math.Max(0, skip));
            var articles = new List<RssArticleRecord>(Math.Max(0, take));
            using var reader = command.ExecuteReader();
            while (reader.Read())
                articles.Add(new RssArticleRecord(
                    reader.GetString(0),
                    reader.GetString(1),
                    RssSensitiveDataProtector.Unprotect(reader.GetString(2)),
                    RssSensitiveDataProtector.Unprotect(reader.GetString(3)),
                    RssSensitiveDataProtector.Unprotect(reader.GetString(4)),
                    RssSensitiveDataProtector.Unprotect(reader.GetString(5)),
                    RssSensitiveDataProtector.Unprotect(reader.GetString(6)),
                    reader.GetString(7),
                    reader.GetInt64(8)));
            return articles;
        }

        private static void DeleteRowsNotIn(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string tableName,
            IReadOnlyCollection<string> ids)
        {
            if (ids.Count == 0)
            {
                using var deleteAll = connection.CreateCommand();
                deleteAll.Transaction = transaction;
                deleteAll.CommandText = $"DELETE FROM {tableName};";
                deleteAll.ExecuteNonQuery();
                return;
            }

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            var parameterNames = ids.Select((id, index) =>
            {
                var name = $"$id{index}";
                command.Parameters.AddWithValue(name, id);
                return name;
            });
            command.CommandText = $"DELETE FROM {tableName} WHERE id NOT IN ({string.Join(",", parameterNames)});";
            command.ExecuteNonQuery();
        }

        private void TryCheckpointDeletedData(SqliteConnection connection)
        {
            bool completed = false;
            try
            {
                ExecuteNonQuery(connection, "PRAGMA busy_timeout=0;");
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                using var reader = command.ExecuteReader();
                completed = reader.Read() && reader.GetInt32(0) == 0;
                if (!completed)
                    System.Diagnostics.Debug.WriteLine("RSS WAL checkpoint deferred because a reader is active.");
            }
            catch (SqliteException ex)
            {
                System.Diagnostics.Debug.WriteLine($"RSS WAL checkpoint failed: {ex.Message}");
            }
            finally
            {
                try { ExecuteNonQuery(connection, $"PRAGMA busy_timeout={BusyTimeoutMilliseconds};"); }
                catch (SqliteException) { }
            }

            Interlocked.Exchange(ref _checkpointPending, completed ? 0 : 1);
        }

        internal SqliteConnection OpenConnection()
        {
            if (_isUnavailable())
                throw new InvalidOperationException("RSS local data is being cleared.");

            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            ExecuteNonQuery(connection, "PRAGMA secure_delete=ON;");
            ExecuteNonQuery(connection, $"PRAGMA busy_timeout={BusyTimeoutMilliseconds};");
            if (Volatile.Read(ref _checkpointPending) != 0)
                TryCheckpointDeletedData(connection);
            return connection;
        }

        private static void ExecuteNonQuery(SqliteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static void TryAddColumn(SqliteConnection connection, string table, string column, string definition)
        {
            try
            {
                ExecuteNonQuery(connection, $"ALTER TABLE {table} ADD COLUMN {column} {definition};");
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 1)
            {
            }
        }
    }
}
