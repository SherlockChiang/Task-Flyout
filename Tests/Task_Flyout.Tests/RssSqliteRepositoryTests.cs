using Microsoft.Data.Sqlite;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class RssSqliteRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "TaskFlyoutRssRepositoryTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Initialize_creates_schema_and_secure_delete_setting()
    {
        var databasePath = Path.Combine(_root, "rss.db");
        var repository = new RssSqliteRepository(databasePath);

        repository.Initialize();

        using var connection = repository.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM metadata WHERE key = 'schema_version';";
        Assert.Equal("1", command.ExecuteScalar());
        command.CommandText = "PRAGMA secure_delete;";
        Assert.Equal(1L, command.ExecuteScalar());
        command.CommandText = "PRAGMA busy_timeout;";
        Assert.Equal(5000L, command.ExecuteScalar());
    }

    [Fact]
    public void Query_pages_decrypts_values_and_filters_by_folder()
    {
        var databasePath = Path.Combine(_root, "rss.db");
        var repository = new RssSqliteRepository(databasePath);
        repository.Initialize();

        using (var connection = Open(databasePath))
        {
            Execute(connection, "INSERT INTO rss_subscriptions(id, title, url, folder_id) VALUES ('sub-a', '', '', 'folder-a'), ('sub-b', '', '', 'folder-b');");
            using var command = connection.CreateCommand();
            command.CommandText = """
INSERT INTO rss_articles(id, subscription_id, feed_title, title, link, summary, html_content, image_url, local_image_path, published_ticks)
VALUES ($id, 'sub-a', $feedTitle, $title, $link, $summary, $html, $imageUrl, '', 20),
       ('older', 'sub-b', $feedTitle, $title, $link, $summary, $html, $imageUrl, '', 10);
""";
            command.Parameters.AddWithValue("$id", "newer");
            command.Parameters.AddWithValue("$feedTitle", RssSensitiveDataProtector.Protect("Private feed"));
            command.Parameters.AddWithValue("$title", RssSensitiveDataProtector.Protect("Private title"));
            command.Parameters.AddWithValue("$link", RssSensitiveDataProtector.Protect("https://example.test/private"));
            command.Parameters.AddWithValue("$summary", RssSensitiveDataProtector.Protect("Private summary"));
            command.Parameters.AddWithValue("$html", RssSensitiveDataProtector.Protect("<p>Private body</p>"));
            command.Parameters.AddWithValue("$imageUrl", RssSensitiveDataProtector.Protect("https://example.test/image.png"));
            command.ExecuteNonQuery();
        }

        var articles = repository.QueryArticlesPage(null, "folder-a", 0, 10);

        var article = Assert.Single(articles);
        Assert.Equal("newer", article.Id);
        Assert.Equal("Private title", article.Title);
        Assert.Equal("Private summary", article.Summary);
        Assert.Equal("<p>Private body</p>", repository.GetArticleHtml(article.Id));
    }

    [Fact]
    public void Repository_rejects_access_during_data_clear()
    {
        bool unavailable = true;
        var repository = new RssSqliteRepository(Path.Combine(_root, "rss.db"), () => unavailable);

        Assert.Throws<InvalidOperationException>(() => repository.Initialize());
        unavailable = false;
        repository.Initialize();
    }

    [Fact]
    public void Upserts_encrypt_sensitive_values_and_round_trip()
    {
        var databasePath = Path.Combine(_root, "rss.db");
        var repository = new RssSqliteRepository(databasePath);
        repository.UpsertFolder(new RssFolderRecord("folder", "Private folder", 2));
        repository.UpsertSubscription(new RssSubscriptionRecord(
            "subscription", "Private feed", "https://example.test/private?token=secret", "folder", "https://example.test/image", "local.png", 10));
        repository.UpsertArticle(new RssArticleWriteRecord(
            "article", "subscription", "Private feed", "Private title", "https://example.test/article", "Private summary", "<p>Private body</p>", "https://example.test/article.png", "article.png", 20));

        using (var connection = Open(databasePath))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT title FROM rss_subscriptions WHERE id = 'subscription';";
            var storedTitle = Assert.IsType<string>(command.ExecuteScalar());
            Assert.True(RssSensitiveDataProtector.IsProtected(storedTitle));
            Assert.DoesNotContain("Private feed", storedTitle, StringComparison.Ordinal);
        }

        var article = Assert.Single(repository.QueryArticlesPage("subscription", null, 0, 10));
        Assert.Equal("Private title", article.Title);
        Assert.Equal("<p>Private body</p>", repository.GetArticleHtml("article"));
    }

    [Fact]
    public void Removing_folder_atomically_unassigns_subscriptions()
    {
        var databasePath = Path.Combine(_root, "rss.db");
        var repository = new RssSqliteRepository(databasePath);
        repository.UpsertFolder(new RssFolderRecord("folder", "Folder", 0));
        repository.UpsertSubscription(new RssSubscriptionRecord("subscription", "Feed", "https://example.test", "folder", "", "", 0));

        repository.RemoveFolder("folder");

        using var connection = Open(databasePath);
        Assert.Equal(0L, ScalarInt64(connection, "SELECT COUNT(*) FROM rss_folders WHERE id = 'folder';"));
        Assert.Equal("", ScalarString(connection, "SELECT folder_id FROM rss_subscriptions WHERE id = 'subscription';"));
    }

    [Fact]
    public void Removing_subscription_atomically_deletes_its_articles()
    {
        var databasePath = Path.Combine(_root, "rss.db");
        var repository = new RssSqliteRepository(databasePath);
        repository.UpsertSubscription(new RssSubscriptionRecord("subscription", "Feed", "https://example.test", "", "", "", 0));
        repository.UpsertArticle(new RssArticleWriteRecord("article", "subscription", "Feed", "Title", "", "", "", "", "", 0));

        repository.RemoveSubscription("subscription");

        using var connection = Open(databasePath);
        Assert.Equal(0L, ScalarInt64(connection, "SELECT COUNT(*) FROM rss_subscriptions WHERE id = 'subscription';"));
        Assert.Equal(0L, ScalarInt64(connection, "SELECT COUNT(*) FROM rss_articles WHERE subscription_id = 'subscription';"));
    }

    [Fact]
    public void Refresh_saves_subscription_and_articles_together()
    {
        var databasePath = Path.Combine(_root, "rss.db");
        var repository = new RssSqliteRepository(databasePath);

        repository.SaveRefresh(
            new RssSubscriptionRecord("subscription", "Updated feed", "https://example.test", "", "", "", 50),
            new[]
            {
                new RssArticleWriteRecord("article-a", "subscription", "Updated feed", "First", "", "", "", "", "", 20),
                new RssArticleWriteRecord("article-b", "subscription", "Updated feed", "Second", "", "", "", "", "", 10)
            });

        Assert.Equal(2, repository.QueryArticlesPage("subscription", null, 0, 10).Count);
        using var connection = Open(databasePath);
        var encryptedTitle = ScalarString(connection, "SELECT title FROM rss_subscriptions WHERE id = 'subscription';");
        Assert.Equal("Updated feed", RssSensitiveDataProtector.Unprotect(encryptedTitle));
    }

    [Fact]
    public void Refresh_rolls_back_subscription_and_articles_when_a_write_fails()
    {
        var databasePath = Path.Combine(_root, "rss.db");
        var repository = new RssSqliteRepository(databasePath);
        repository.UpsertSubscription(new RssSubscriptionRecord("subscription", "Original feed", "https://example.test", "", "", "", 10));

        Assert.ThrowsAny<Exception>(() => repository.SaveRefresh(
            new RssSubscriptionRecord("subscription", "Updated feed", "https://example.test", "", "", "", 20),
            new[]
            {
                new RssArticleWriteRecord("valid", "subscription", "Updated feed", "Valid", "", "", "", "", "", 20),
                new RssArticleWriteRecord(null!, "subscription", "Updated feed", "Invalid", "", "", "", "", "", 10)
            }));

        using var connection = Open(databasePath);
        var encryptedTitle = ScalarString(connection, "SELECT title FROM rss_subscriptions WHERE id = 'subscription';");
        Assert.Equal("Original feed", RssSensitiveDataProtector.Unprotect(encryptedTitle));
        Assert.Equal(0L, ScalarInt64(connection, "SELECT COUNT(*) FROM rss_articles;"));
    }

    [Fact]
    public void Sensitive_data_migration_encrypts_legacy_plaintext_and_is_idempotent()
    {
        var databasePath = Path.Combine(_root, "rss.db");
        var repository = new RssSqliteRepository(databasePath);
        repository.Initialize();
        using (var connection = Open(databasePath))
        {
            Execute(connection, "INSERT INTO rss_folders(id, name) VALUES ('folder', 'Legacy folder');");
            Execute(connection, "INSERT INTO rss_subscriptions(id, title, url, image_url) VALUES ('subscription', 'Legacy feed', 'https://example.test/private', 'https://example.test/feed.png');");
            Execute(connection, """
INSERT INTO rss_articles(id, subscription_id, feed_title, title, link, summary, html_content, image_url, local_image_path)
VALUES ('article', 'subscription', 'Legacy feed', 'Legacy title', 'https://example.test/article', 'Legacy summary', '<p>Legacy body</p>', 'https://example.test/article.png', '');
""");
        }

        repository.MigrateSensitiveData();

        string protectedFolder;
        using (var connection = Open(databasePath))
        {
            protectedFolder = ScalarString(connection, "SELECT name FROM rss_folders WHERE id = 'folder';");
            Assert.True(RssSensitiveDataProtector.IsProtected(protectedFolder));
            Assert.Equal("Legacy folder", RssSensitiveDataProtector.Unprotect(protectedFolder));
            Assert.True(RssSensitiveDataProtector.IsProtected(ScalarString(connection, "SELECT url FROM rss_subscriptions WHERE id = 'subscription';")));
            Assert.True(RssSensitiveDataProtector.IsProtected(ScalarString(connection, "SELECT html_content FROM rss_articles WHERE id = 'article';")));
            Assert.Equal("complete", ScalarString(connection, "SELECT value FROM metadata WHERE key = 'sensitive_data_dpapi_v1';"));
        }

        repository.MigrateSensitiveData();

        using var verification = Open(databasePath);
        Assert.Equal(protectedFolder, ScalarString(verification, "SELECT name FROM rss_folders WHERE id = 'folder';"));
    }

    [Fact]
    public void Sensitive_data_migration_rolls_back_and_does_not_mark_failure_complete()
    {
        var databasePath = Path.Combine(_root, "rss.db");
        var repository = new RssSqliteRepository(databasePath);
        repository.Initialize();
        using (var connection = Open(databasePath))
        {
            Execute(connection, "INSERT INTO rss_folders(id, name) VALUES ('folder', 'Legacy folder');");
            Execute(connection, "INSERT INTO rss_subscriptions(id, title, url) VALUES ('subscription', 'Legacy feed', 'https://example.test');");
            Execute(connection, """
CREATE TRIGGER fail_sensitive_migration
BEFORE UPDATE OF title ON rss_subscriptions
WHEN NEW.title LIKE 'dpapi:v1:%'
BEGIN
    SELECT RAISE(ABORT, 'injected migration failure');
END;
""");
        }

        Assert.Throws<SqliteException>(() => repository.MigrateSensitiveData());

        using var verification = Open(databasePath);
        Assert.Equal("Legacy folder", ScalarString(verification, "SELECT name FROM rss_folders WHERE id = 'folder';"));
        Assert.Equal("Legacy feed", ScalarString(verification, "SELECT title FROM rss_subscriptions WHERE id = 'subscription';"));
        Assert.Equal(0L, ScalarInt64(verification, "SELECT COUNT(*) FROM metadata WHERE key = 'sensitive_data_dpapi_v1';"));
    }

    [Fact]
    public void Snapshot_replaces_stale_rows_and_keeps_newest_articles()
    {
        var databasePath = Path.Combine(_root, "rss.db");
        var repository = new RssSqliteRepository(databasePath);
        repository.UpsertFolder(new RssFolderRecord("stale-folder", "Stale", 0));
        repository.UpsertSubscription(new RssSubscriptionRecord("stale-subscription", "Stale", "https://stale.test", "", "", "", 0));
        repository.UpsertArticle(new RssArticleWriteRecord("stale-article", "stale-subscription", "Stale", "Stale", "", "", "", "", "", 1));

        repository.SaveSnapshot(
            new[] { new RssFolderRecord("folder", "Current", 1) },
            new[] { new RssSubscriptionRecord("subscription", "Current", "https://example.test", "folder", "", "", 0) },
            new[]
            {
                new RssArticleWriteRecord("old", "subscription", "Current", "Old", "", "", "", "", "", 10),
                new RssArticleWriteRecord("new", "subscription", "Current", "New", "", "", "", "", "", 20)
            },
            maximumArticleCount: 1);

        using var connection = Open(databasePath);
        Assert.Equal(0L, ScalarInt64(connection, "SELECT COUNT(*) FROM rss_folders WHERE id = 'stale-folder';"));
        Assert.Equal(0L, ScalarInt64(connection, "SELECT COUNT(*) FROM rss_subscriptions WHERE id = 'stale-subscription';"));
        Assert.Equal(1L, ScalarInt64(connection, "SELECT COUNT(*) FROM rss_articles;"));
        Assert.Equal("new", ScalarString(connection, "SELECT id FROM rss_articles;"));
    }

    [Fact]
    public void Trim_articles_keeps_newest_rows()
    {
        var databasePath = Path.Combine(_root, "rss.db");
        var repository = new RssSqliteRepository(databasePath);
        repository.UpsertArticle(new RssArticleWriteRecord("old", "subscription", "Feed", "Old", "", "", "", "", "", 10));
        repository.UpsertArticle(new RssArticleWriteRecord("new", "subscription", "Feed", "New", "", "", "", "", "", 20));

        repository.TrimArticles(1);

        using var connection = Open(databasePath);
        Assert.Equal(1L, ScalarInt64(connection, "SELECT COUNT(*) FROM rss_articles;"));
        Assert.Equal("new", ScalarString(connection, "SELECT id FROM rss_articles;"));
    }

    [Fact]
    public void Load_snapshot_decrypts_all_entity_types_and_limits_articles()
    {
        var databasePath = Path.Combine(_root, "rss.db");
        var repository = new RssSqliteRepository(databasePath);
        repository.UpsertFolder(new RssFolderRecord("folder", "Private folder", 3));
        repository.UpsertSubscription(new RssSubscriptionRecord("subscription", "Private feed", "https://example.test/private", "folder", "https://example.test/feed.png", "feed.png", 30));
        repository.UpsertArticle(new RssArticleWriteRecord("old", "subscription", "Private feed", "Old", "", "", "<p>Old body</p>", "", "", 10));
        repository.UpsertArticle(new RssArticleWriteRecord("new", "subscription", "Private feed", "New", "", "", "<p>New body</p>", "", "", 20));

        var snapshot = repository.LoadSnapshot(1);

        Assert.Equal("Private folder", Assert.Single(snapshot.Folders).Name);
        Assert.Equal("https://example.test/private", Assert.Single(snapshot.Subscriptions).Url);
        var article = Assert.Single(snapshot.Articles);
        Assert.Equal("new", article.Id);
        Assert.Equal("Private feed", article.FeedTitle);
    }

    [Fact]
    public async Task Writer_waits_for_short_lock_contention_and_then_commits()
    {
        var databasePath = Path.Combine(_root, "rss.db");
        var repository = new RssSqliteRepository(databasePath);
        repository.Initialize();
        using var blocker = repository.OpenConnection();
        using var transaction = blocker.BeginTransaction();
        using (var command = blocker.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO rss_folders(id, name) VALUES ('blocking', 'blocking');";
            command.ExecuteNonQuery();
        }

        var pendingWrite = Task.Run(() => repository.UpsertFolder(new RssFolderRecord("waiting", "Waiting", 0)));
        await Task.Delay(150);
        Assert.False(pendingWrite.IsCompleted);
        transaction.Commit();
        await pendingWrite.WaitAsync(TimeSpan.FromSeconds(3));

        using var verification = Open(databasePath);
        Assert.Equal(1L, ScalarInt64(verification, "SELECT COUNT(*) FROM rss_folders WHERE id = 'waiting';"));
    }

    [Fact]
    public void Wal_reader_does_not_block_refresh_transaction()
    {
        var databasePath = Path.Combine(_root, "rss.db");
        var repository = new RssSqliteRepository(databasePath);
        repository.UpsertSubscription(new RssSubscriptionRecord("subscription", "Feed", "https://example.test", "", "", "", 0));
        repository.UpsertArticle(new RssArticleWriteRecord("existing", "subscription", "Feed", "Existing", "", "", "", "", "", 10));

        using var readerConnection = repository.OpenConnection();
        using var readerTransaction = readerConnection.BeginTransaction(deferred: true);
        using var readerCommand = readerConnection.CreateCommand();
        readerCommand.Transaction = readerTransaction;
        readerCommand.CommandText = "SELECT title FROM rss_articles WHERE id = 'existing';";
        using var reader = readerCommand.ExecuteReader();
        Assert.True(reader.Read());

        repository.SaveRefresh(
            new RssSubscriptionRecord("subscription", "Updated", "https://example.test", "", "", "", 20),
            new[] { new RssArticleWriteRecord("new", "subscription", "Updated", "New", "", "", "", "", "", 20) });

        Assert.Equal("New", Assert.Single(repository.QueryArticlesPage("subscription", null, 0, 1)).Title);
    }

    [Fact]
    public void Active_repository_operations_honor_data_clear_barrier()
    {
        bool unavailable = false;
        var repository = new RssSqliteRepository(Path.Combine(_root, "rss.db"), () => unavailable);
        repository.Initialize();
        unavailable = true;

        Assert.Throws<InvalidOperationException>(() => repository.QueryArticlesPage(null, null, 0, 10));
        Assert.Throws<InvalidOperationException>(() => repository.UpsertFolder(new RssFolderRecord("folder", "Folder", 0)));
        Assert.Throws<InvalidOperationException>(() => repository.SaveSnapshot(Array.Empty<RssFolderRecord>(), Array.Empty<RssSubscriptionRecord>(), Array.Empty<RssArticleWriteRecord>(), 1000));
    }

    [Fact]
    public void Sensitive_delete_truncates_wal_when_no_reader_is_active()
    {
        var databasePath = Path.Combine(_root, "rss.db");
        var repository = new RssSqliteRepository(databasePath);
        repository.UpsertArticle(new RssArticleWriteRecord("old", "subscription", "Feed", new string('x', 20_000), "", "", "", "", "", 10));
        repository.UpsertArticle(new RssArticleWriteRecord("new", "subscription", "Feed", "New", "", "", "", "", "", 20));

        repository.TrimArticles(1);

        var walPath = databasePath + "-wal";
        Assert.True(!File.Exists(walPath) || new FileInfo(walPath).Length == 0);
    }

    [Fact]
    public void Busy_reader_defers_checkpoint_without_rolling_back_delete()
    {
        var databasePath = Path.Combine(_root, "rss.db");
        var repository = new RssSqliteRepository(databasePath);
        repository.UpsertArticle(new RssArticleWriteRecord("old", "subscription", "Feed", new string('x', 20_000), "", "", "", "", "", 10));
        repository.UpsertArticle(new RssArticleWriteRecord("new", "subscription", "Feed", "New", "", "", "", "", "", 20));

        using (var readerConnection = repository.OpenConnection())
        using (var readerTransaction = readerConnection.BeginTransaction(deferred: true))
        using (var command = readerConnection.CreateCommand())
        {
            command.Transaction = readerTransaction;
            command.CommandText = "SELECT title FROM rss_articles WHERE id = 'old';";
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());

            repository.TrimArticles(1);
            Assert.Equal("new", Assert.Single(repository.QueryArticlesPage(null, null, 0, 10)).Id);
        }

        repository.TrimArticles(1);
        var walPath = databasePath + "-wal";
        Assert.True(!File.Exists(walPath) || new FileInfo(walPath).Length == 0);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static SqliteConnection Open(string databasePath)
    {
        var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long ScalarInt64(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)command.ExecuteScalar()!;
    }

    private static string ScalarString(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)command.ExecuteScalar()!;
    }
}
