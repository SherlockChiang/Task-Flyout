using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;

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
        long LastFetchedUtcTicks);

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

    internal sealed class RssSqliteRepository
    {
        private const int SchemaVersion = 1;
        private readonly string _connectionString;
        private readonly Func<bool> _isUnavailable;
        private readonly object _initializationLock = new();
        private bool _initialized;

        public RssSqliteRepository(string databasePath, Func<bool>? isUnavailable = null)
        {
            _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
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
    last_fetched_ticks INTEGER NOT NULL DEFAULT 0
);
""");
                TryAddColumn(connection, "rss_subscriptions", "image_url", "TEXT NOT NULL DEFAULT ''");
                TryAddColumn(connection, "rss_subscriptions", "local_image_path", "TEXT NOT NULL DEFAULT ''");
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
            }
        }

        public List<RssArticleRecord> QueryArticlesPage(string? subscriptionId, string? folderId, int skip, int take)
        {
            Initialize();
            bool hasSubscription = !string.IsNullOrWhiteSpace(subscriptionId);
            bool hasFolder = folderId != null;

            using var connection = OpenConnection();
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
            {
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
            }

            return articles;
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
            command.CommandText = """
INSERT INTO rss_folders(id, name, sort_order) VALUES ($id, $name, $sortOrder)
ON CONFLICT(id) DO UPDATE SET name = excluded.name, sort_order = excluded.sort_order;
""";
            command.Parameters.AddWithValue("$id", folder.Id);
            command.Parameters.AddWithValue("$name", RssSensitiveDataProtector.Protect(folder.Name));
            command.Parameters.AddWithValue("$sortOrder", folder.SortOrder);
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
            try { ExecuteNonQuery(connection, "PRAGMA wal_checkpoint(TRUNCATE);"); }
            catch (SqliteException) { }
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

        private static void WriteSubscription(SqliteCommand command, RssSubscriptionRecord subscription)
        {
            command.CommandText = """
INSERT INTO rss_subscriptions(id, title, url, folder_id, image_url, local_image_path, last_fetched_ticks)
VALUES ($id, $title, $url, $folderId, $imageUrl, $localImagePath, $lastFetchedTicks)
ON CONFLICT(id) DO UPDATE SET
    title = excluded.title,
    url = excluded.url,
    folder_id = excluded.folder_id,
    image_url = excluded.image_url,
    local_image_path = excluded.local_image_path,
    last_fetched_ticks = excluded.last_fetched_ticks;
""";
            command.Parameters.AddWithValue("$id", subscription.Id);
            command.Parameters.AddWithValue("$title", RssSensitiveDataProtector.Protect(subscription.Title));
            command.Parameters.AddWithValue("$url", RssSensitiveDataProtector.Protect(subscription.Url));
            command.Parameters.AddWithValue("$folderId", subscription.FolderId);
            command.Parameters.AddWithValue("$imageUrl", RssSensitiveDataProtector.Protect(subscription.ImageUrl));
            command.Parameters.AddWithValue("$localImagePath", subscription.LocalImagePath);
            command.Parameters.AddWithValue("$lastFetchedTicks", subscription.LastFetchedUtcTicks);
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
    local_image_path = excluded.local_image_path,
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

        internal SqliteConnection OpenConnection()
        {
            if (_isUnavailable())
                throw new InvalidOperationException("RSS local data is being cleared.");

            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            ExecuteNonQuery(connection, "PRAGMA secure_delete=ON;");
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
