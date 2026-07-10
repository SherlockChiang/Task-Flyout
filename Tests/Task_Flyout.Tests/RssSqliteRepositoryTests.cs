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
