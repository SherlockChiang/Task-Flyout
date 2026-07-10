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
