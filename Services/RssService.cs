using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Task_Flyout.Services
{
    public class RssSubscription
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public string FolderId { get; set; } = "";
        public DateTimeOffset LastFetchedAt { get; set; }
    }

    public class RssFolder
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public int SortOrder { get; set; }
    }

    public class RssArticle
    {
        public string Id { get; set; } = "";
        public string SubscriptionId { get; set; } = "";
        public string FeedTitle { get; set; } = "";
        public string Title { get; set; } = "";
        public string Link { get; set; } = "";
        public string Summary { get; set; } = "";
        public string HtmlContent { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        public string LocalImagePath { get; set; } = "";
        public DateTimeOffset PublishedAt { get; set; }
        public string PublishedText => PublishedAt == default ? "" : PublishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        public Microsoft.UI.Xaml.Media.ImageSource? ImageSource
        {
            get
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(LocalImagePath) && File.Exists(LocalImagePath))
                        return new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(LocalImagePath));
                }
                catch { }

                return null;
            }
        }
        public Microsoft.UI.Xaml.Visibility HasImage
        {
            get
            {
                try
                {
                    return !string.IsNullOrWhiteSpace(LocalImagePath) && File.Exists(LocalImagePath)
                        ? Microsoft.UI.Xaml.Visibility.Visible
                        : Microsoft.UI.Xaml.Visibility.Collapsed;
                }
                catch
                {
                    return Microsoft.UI.Xaml.Visibility.Collapsed;
                }
            }
        }
        public Microsoft.UI.Xaml.Visibility HasSummary => string.IsNullOrWhiteSpace(Summary) ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    }

    public class RssCache
    {
        public List<RssFolder> Folders { get; set; } = new();
        public List<RssSubscription> Subscriptions { get; set; } = new();
        public List<RssArticle> Articles { get; set; } = new();
    }

    public class RssService
    {
        private const int FeedRefreshMinutes = 30;
        private const int SchemaVersion = 1;
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private readonly string _appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskFlyout");

        private RssCache _cache = new();
        private bool _loaded;
        private bool _databaseInitialized;

        public IReadOnlyList<RssSubscription> GetSubscriptions()
        {
            EnsureLoaded();
            return _cache.Subscriptions;
        }

        public IReadOnlyList<RssFolder> GetFolders()
        {
            EnsureLoaded();
            return _cache.Folders.OrderBy(folder => folder.SortOrder).ThenBy(folder => folder.Name).ToList();
        }

        public IReadOnlyList<RssArticle> GetCachedArticles(string? subscriptionId = null, string? folderId = null)
        {
            EnsureLoaded();
            var folderSubscriptionIds = GetSubscriptionIdsForFolder(folderId);
            return _cache.Articles
                .Where(article =>
                    (subscriptionId == null || article.SubscriptionId == subscriptionId) &&
                    (folderSubscriptionIds == null || folderSubscriptionIds.Contains(article.SubscriptionId)))
                .OrderByDescending(article => article.PublishedAt)
                .ToList();
        }

        public RssFolder AddFolder(string name)
        {
            EnsureLoaded();
            name = name.Trim();
            var folder = new RssFolder
            {
                Name = string.IsNullOrWhiteSpace(name) ? "新文件夹" : name,
                SortOrder = _cache.Folders.Count == 0 ? 0 : _cache.Folders.Max(item => item.SortOrder) + 1
            };
            _cache.Folders.Add(folder);
            Save();
            return folder;
        }

        public void RemoveFolder(string folderId)
        {
            EnsureLoaded();
            _cache.Folders.RemoveAll(folder => folder.Id == folderId);
            foreach (var subscription in _cache.Subscriptions.Where(item => item.FolderId == folderId))
                subscription.FolderId = "";
            Save();
        }

        public void SaveFolderOrder(IEnumerable<string> folderIds)
        {
            EnsureLoaded();
            var index = 0;
            foreach (var folderId in folderIds)
            {
                var folder = _cache.Folders.FirstOrDefault(item => item.Id == folderId);
                if (folder != null)
                    folder.SortOrder = index++;
            }
            Save();
        }

        public void MoveSubscriptionToFolder(string subscriptionId, string folderId)
        {
            EnsureLoaded();
            var subscription = _cache.Subscriptions.FirstOrDefault(item => item.Id == subscriptionId);
            if (subscription == null) return;
            subscription.FolderId = _cache.Folders.Any(folder => folder.Id == folderId) ? folderId : "";
            Save();
        }

        public void RenameFolder(string folderId, string name)
        {
            EnsureLoaded();
            var folder = _cache.Folders.FirstOrDefault(item => item.Id == folderId);
            if (folder == null) return;
            folder.Name = string.IsNullOrWhiteSpace(name) ? folder.Name : name.Trim();
            Save();
        }

        public void RenameSubscription(string subscriptionId, string title)
        {
            EnsureLoaded();
            var subscription = _cache.Subscriptions.FirstOrDefault(item => item.Id == subscriptionId);
            if (subscription == null) return;
            subscription.Title = string.IsNullOrWhiteSpace(title) ? subscription.Title : title.Trim();
            foreach (var article in _cache.Articles.Where(item => item.SubscriptionId == subscriptionId))
                article.FeedTitle = subscription.Title;
            Save();
        }

        public async Task<RssSubscription> AddSubscriptionAsync(string url, string folderId = "")
        {
            EnsureLoaded();
            url = NormalizeFeedUrl(url);
            var existing = _cache.Subscriptions.FirstOrDefault(item => string.Equals(item.Url, url, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (!string.IsNullOrWhiteSpace(folderId))
                {
                    existing.FolderId = folderId;
                    Save();
                }
                return existing;
            }

            var subscription = new RssSubscription
            {
                Url = url,
                Title = url,
                FolderId = _cache.Folders.Any(folder => folder.Id == folderId) ? folderId : ""
            };
            _cache.Subscriptions.Add(subscription);
            await RefreshSubscriptionAsync(subscription, force: true);
            Save();
            return subscription;
        }

        public void RemoveSubscription(string subscriptionId)
        {
            EnsureLoaded();
            _cache.Subscriptions.RemoveAll(item => item.Id == subscriptionId);
            _cache.Articles.RemoveAll(item => item.SubscriptionId == subscriptionId);
            Save();
        }

        public async Task<List<RssArticle>> LoadMoreArticlesAsync(string? subscriptionId, string? folderId, int skip, int take)
        {
            EnsureLoaded();
            var eligibleSubscriptions = GetSubscriptionsForFilter(subscriptionId, folderId).ToList();

            if (subscriptionId == null)
            {
                var nextSubscriptions = eligibleSubscriptions
                    .Where(ShouldRefresh)
                    .Take(3)
                    .ToList();
                foreach (var subscription in nextSubscriptions)
                    await RefreshSubscriptionAsync(subscription, force: false);
            }
            else if (_cache.Subscriptions.FirstOrDefault(item => item.Id == subscriptionId) is { } subscription && ShouldRefresh(subscription))
            {
                await RefreshSubscriptionAsync(subscription, force: false);
            }

            Save();
            return GetCachedArticles(subscriptionId, folderId)
                .Skip(skip)
                .Take(take)
                .ToList();
        }

        public async Task RefreshSubscriptionAsync(RssSubscription subscription, bool force)
        {
            EnsureLoaded();
            if (!force && !ShouldRefresh(subscription)) return;

            using var response = await HttpClient.GetAsync(subscription.Url);
            response.EnsureSuccessStatusCode();
            var xml = await response.Content.ReadAsStringAsync();
            var parsed = ParseFeed(subscription, xml);

            if (!string.IsNullOrWhiteSpace(parsed.FeedTitle))
                subscription.Title = parsed.FeedTitle;

            foreach (var article in parsed.Articles)
            {
                article.LocalImagePath = await CacheImageAsync(article.ImageUrl);
                var existing = _cache.Articles.FirstOrDefault(item => item.Id == article.Id);
                if (existing == null)
                {
                    _cache.Articles.Add(article);
                }
                else
                {
                    existing.Title = article.Title;
                    existing.Link = article.Link;
                    existing.Summary = article.Summary;
                    existing.HtmlContent = article.HtmlContent;
                    existing.ImageUrl = article.ImageUrl;
                    existing.LocalImagePath = article.LocalImagePath;
                    existing.PublishedAt = article.PublishedAt;
                    existing.FeedTitle = article.FeedTitle;
                }
            }

            subscription.LastFetchedAt = DateTimeOffset.Now;
            TrimCache();
        }

        private bool ShouldRefresh(RssSubscription subscription)
            => subscription.LastFetchedAt == default || DateTimeOffset.Now - subscription.LastFetchedAt > TimeSpan.FromMinutes(FeedRefreshMinutes);

        private static string NormalizeFeedUrl(string url)
        {
            url = url.Trim();
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }
            return url;
        }

        private (string FeedTitle, List<RssArticle> Articles) ParseFeed(RssSubscription subscription, string xml)
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root == null) return ("", new List<RssArticle>());

            var feedTitle = GetElementValue(root.Element("channel"), "title")
                            ?? GetElementValue(root, "title")
                            ?? subscription.Title;

            var items = root.Element("channel")?.Elements("item") ?? Enumerable.Empty<XElement>();
            var articles = items.Any()
                ? items.Select(item => ParseRssItem(subscription, feedTitle, item)).ToList()
                : root.Elements().Where(element => element.Name.LocalName == "entry")
                    .Select(entry => ParseAtomEntry(subscription, feedTitle, entry))
                    .ToList();

            return (feedTitle, articles.Where(article => !string.IsNullOrWhiteSpace(article.Title)).ToList());
        }

        private static RssArticle ParseRssItem(RssSubscription subscription, string feedTitle, XElement item)
        {
            var title = GetElementValue(item, "title") ?? "(无标题)";
            string link = GetElementValue(item, "link") ?? "";
            var html = GetElementValue(item, "encoded") ?? GetElementValue(item, "description") ?? "";
            var summary = StripHtml(html);
            var published = ParseDate(GetElementValue(item, "pubDate") ?? GetElementValue(item, "date"));
            var id = GetElementValue(item, "guid") ?? link ?? $"{subscription.Id}:{title}";
            var imageUrl = FindImageUrl(item, summary);

            return new RssArticle
            {
                Id = $"{subscription.Id}:{HashText(id)}",
                SubscriptionId = subscription.Id,
                FeedTitle = feedTitle,
                Title = StripHtml(title),
                Link = link ?? string.Empty,
                Summary = summary,
                HtmlContent = html,
                ImageUrl = imageUrl,
                PublishedAt = published
            };
        }

        private static RssArticle ParseAtomEntry(RssSubscription subscription, string feedTitle, XElement entry)
        {
            var title = GetElementValue(entry, "title") ?? "(无标题)";
            string link = entry.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "link" &&
                ((string?)element.Attribute("rel") == null || (string?)element.Attribute("rel") == "alternate"))?.Attribute("href")?.Value ?? "";
            var html = GetElementValue(entry, "content") ?? GetElementValue(entry, "summary") ?? "";
            var summary = StripHtml(html);
            var published = ParseDate(GetElementValue(entry, "published") ?? GetElementValue(entry, "updated"));
            var id = GetElementValue(entry, "id") ?? link ?? $"{subscription.Id}:{title}";
            var imageUrl = FindImageUrl(entry, summary);

            return new RssArticle
            {
                Id = $"{subscription.Id}:{HashText(id)}",
                SubscriptionId = subscription.Id,
                FeedTitle = feedTitle,
                Title = StripHtml(title),
                Link = link ?? string.Empty,
                Summary = summary,
                HtmlContent = html,
                ImageUrl = imageUrl,
                PublishedAt = published
            };
        }

        private static string? GetElementValue(XElement? parent, string localName)
            => parent?.Elements().FirstOrDefault(element => element.Name.LocalName == localName)?.Value?.Trim();

        private static DateTimeOffset ParseDate(string? value)
            => DateTimeOffset.TryParse(value, out var date) ? date : DateTimeOffset.Now;

        private static string StripHtml(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var text = System.Text.RegularExpressions.Regex.Replace(value, "<.*?>", " ");
            text = System.Net.WebUtility.HtmlDecode(text);
            return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        }

        private static string FindImageUrl(XElement item, string fallbackText)
        {
            var media = item.Descendants().FirstOrDefault(element =>
                element.Name.LocalName is "thumbnail" or "content" &&
                element.Attribute("url") != null);
            if (media?.Attribute("url")?.Value is { Length: > 0 } mediaUrl)
                return mediaUrl;

            var enclosure = item.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "enclosure" &&
                (element.Attribute("type")?.Value.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ?? false));
            if (enclosure?.Attribute("url")?.Value is { Length: > 0 } enclosureUrl)
                return enclosureUrl;

            var match = System.Text.RegularExpressions.Regex.Match(fallbackText, @"https?://[^\s'""]+\.(png|jpe?g|webp|gif)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Value : "";
        }

        private async Task<string> CacheImageAsync(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl) || !Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
                return "";
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return "";

            try
            {
                var imagesPath = Path.Combine(_appDataPath, "RssImages");
                Directory.CreateDirectory(imagesPath);
                var extension = Path.GetExtension(uri.AbsolutePath);
                if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
                    extension = ".img";
                var path = Path.Combine(imagesPath, HashText(imageUrl) + extension);
                if (File.Exists(path)) return path;

                var bytes = await HttpClient.GetByteArrayAsync(uri);
                await File.WriteAllBytesAsync(path, bytes);
                return path;
            }
            catch
            {
                return "";
            }
        }

        private void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            try
            {
                InitializeDatabase();
                _cache = LoadFromDatabase();
                TryMigrateLegacyJsonCache();
            }
            catch
            {
                _cache = new RssCache();
            }

            _cache.Subscriptions ??= new List<RssSubscription>();
            _cache.Folders ??= new List<RssFolder>();
            _cache.Articles ??= new List<RssArticle>();
            foreach (var subscription in _cache.Subscriptions)
            {
                subscription.Id ??= Guid.NewGuid().ToString("N");
                subscription.Title ??= "";
                subscription.Url ??= "";
                subscription.FolderId ??= "";
            }

            foreach (var folder in _cache.Folders)
            {
                folder.Id ??= Guid.NewGuid().ToString("N");
                folder.Name ??= "文件夹";
            }

            foreach (var article in _cache.Articles)
            {
                article.Id ??= Guid.NewGuid().ToString("N");
                article.SubscriptionId ??= "";
                article.FeedTitle ??= "";
                article.Title ??= "";
                article.Link ??= "";
                article.Summary ??= "";
                article.HtmlContent ??= "";
                article.ImageUrl ??= "";
                article.LocalImagePath ??= "";
            }
        }

        private HashSet<string>? GetSubscriptionIdsForFolder(string? folderId)
        {
            if (folderId == null) return null;
            return _cache.Subscriptions
                .Where(subscription => string.Equals(subscription.FolderId, folderId, StringComparison.Ordinal))
                .Select(subscription => subscription.Id)
                .ToHashSet(StringComparer.Ordinal);
        }

        private IEnumerable<RssSubscription> GetSubscriptionsForFilter(string? subscriptionId, string? folderId)
        {
            if (subscriptionId != null)
                return _cache.Subscriptions.Where(subscription => subscription.Id == subscriptionId);

            if (folderId != null)
                return _cache.Subscriptions.Where(subscription => subscription.FolderId == folderId);

            return _cache.Subscriptions;
        }

        private void Save()
        {
            Directory.CreateDirectory(_appDataPath);
            InitializeDatabase();
            SaveToDatabase();
        }

        private void TrimCache()
        {
            _cache.Articles = _cache.Articles
                .GroupBy(article => article.Id)
                .Select(group => group.OrderByDescending(article => article.PublishedAt).First())
                .OrderByDescending(article => article.PublishedAt)
                .Take(1000)
                .ToList();
        }

        private void InitializeDatabase()
        {
            if (_databaseInitialized) return;
            _databaseInitialized = true;

            Directory.CreateDirectory(_appDataPath);
            using var connection = OpenConnection();
            ExecuteNonQuery(connection, "PRAGMA journal_mode=WAL;");
            ExecuteNonQuery(connection, "PRAGMA synchronous=NORMAL;");
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
    last_fetched_ticks INTEGER NOT NULL DEFAULT 0
);
""");
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

            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR REPLACE INTO metadata(key, value) VALUES ('schema_version', $version);";
            command.Parameters.AddWithValue("$version", SchemaVersion.ToString());
            command.ExecuteNonQuery();
        }

        private RssCache LoadFromDatabase()
        {
            var cache = new RssCache();
            using var connection = OpenConnection();

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, name, sort_order FROM rss_folders ORDER BY sort_order, name;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    cache.Folders.Add(new RssFolder
                    {
                        Id = reader.GetString(0),
                        Name = reader.GetString(1),
                        SortOrder = reader.GetInt32(2)
                    });
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, title, url, folder_id, last_fetched_ticks FROM rss_subscriptions ORDER BY title;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    cache.Subscriptions.Add(new RssSubscription
                    {
                        Id = reader.GetString(0),
                        Title = reader.GetString(1),
                        Url = reader.GetString(2),
                        FolderId = reader.GetString(3),
                        LastFetchedAt = FromTicks(reader.GetInt64(4))
                    });
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
SELECT id, subscription_id, feed_title, title, link, summary, html_content, image_url, local_image_path, published_ticks
FROM rss_articles
ORDER BY published_ticks DESC
LIMIT 1000;
""";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    cache.Articles.Add(new RssArticle
                    {
                        Id = reader.GetString(0),
                        SubscriptionId = reader.GetString(1),
                        FeedTitle = reader.GetString(2),
                        Title = reader.GetString(3),
                        Link = reader.GetString(4),
                        Summary = reader.GetString(5),
                        HtmlContent = reader.GetString(6),
                        ImageUrl = reader.GetString(7),
                        LocalImagePath = reader.GetString(8),
                        PublishedAt = FromTicks(reader.GetInt64(9))
                    });
                }
            }

            return cache;
        }

        private void SaveToDatabase()
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            ExecuteNonQuery(connection, "DELETE FROM rss_folders;", transaction);
            ExecuteNonQuery(connection, "DELETE FROM rss_subscriptions;", transaction);
            ExecuteNonQuery(connection, "DELETE FROM rss_articles;", transaction);

            foreach (var folder in _cache.Folders)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO rss_folders(id, name, sort_order) VALUES ($id, $name, $sortOrder);";
                command.Parameters.AddWithValue("$id", folder.Id ?? "");
                command.Parameters.AddWithValue("$name", folder.Name ?? "");
                command.Parameters.AddWithValue("$sortOrder", folder.SortOrder);
                command.ExecuteNonQuery();
            }

            foreach (var subscription in _cache.Subscriptions)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
INSERT INTO rss_subscriptions(id, title, url, folder_id, last_fetched_ticks)
VALUES ($id, $title, $url, $folderId, $lastFetchedTicks);
""";
                command.Parameters.AddWithValue("$id", subscription.Id ?? "");
                command.Parameters.AddWithValue("$title", subscription.Title ?? "");
                command.Parameters.AddWithValue("$url", subscription.Url ?? "");
                command.Parameters.AddWithValue("$folderId", subscription.FolderId ?? "");
                command.Parameters.AddWithValue("$lastFetchedTicks", ToTicks(subscription.LastFetchedAt));
                command.ExecuteNonQuery();
            }

            foreach (var article in _cache.Articles.OrderByDescending(item => item.PublishedAt).Take(1000))
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
INSERT INTO rss_articles(id, subscription_id, feed_title, title, link, summary, html_content, image_url, local_image_path, published_ticks)
VALUES ($id, $subscriptionId, $feedTitle, $title, $link, $summary, $htmlContent, $imageUrl, $localImagePath, $publishedTicks);
""";
                command.Parameters.AddWithValue("$id", article.Id ?? "");
                command.Parameters.AddWithValue("$subscriptionId", article.SubscriptionId ?? "");
                command.Parameters.AddWithValue("$feedTitle", article.FeedTitle ?? "");
                command.Parameters.AddWithValue("$title", article.Title ?? "");
                command.Parameters.AddWithValue("$link", article.Link ?? "");
                command.Parameters.AddWithValue("$summary", article.Summary ?? "");
                command.Parameters.AddWithValue("$htmlContent", article.HtmlContent ?? "");
                command.Parameters.AddWithValue("$imageUrl", article.ImageUrl ?? "");
                command.Parameters.AddWithValue("$localImagePath", article.LocalImagePath ?? "");
                command.Parameters.AddWithValue("$publishedTicks", ToTicks(article.PublishedAt));
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        private void TryMigrateLegacyJsonCache()
        {
            if (_cache.Subscriptions.Count > 0 || _cache.Articles.Count > 0) return;

            var legacyPath = GetLegacyCachePath();
            if (!File.Exists(legacyPath)) return;

            try
            {
                var json = File.ReadAllText(legacyPath);
                var legacy = JsonSerializer.Deserialize(json, AppJsonContext.Default.RssCache);
                if (legacy == null) return;

                _cache = legacy;
                _cache.Subscriptions ??= new List<RssSubscription>();
                _cache.Folders ??= new List<RssFolder>();
                _cache.Articles ??= new List<RssArticle>();
                NormalizeLoadedCache();
                SaveToDatabase();
            }
            catch { }
        }

        private void NormalizeLoadedCache()
        {
            _cache.Subscriptions ??= new List<RssSubscription>();
            _cache.Folders ??= new List<RssFolder>();
            _cache.Articles ??= new List<RssArticle>();
            foreach (var subscription in _cache.Subscriptions)
            {
                subscription.Id ??= Guid.NewGuid().ToString("N");
                subscription.Title ??= "";
                subscription.Url ??= "";
                subscription.FolderId ??= "";
            }

            foreach (var folder in _cache.Folders)
            {
                folder.Id ??= Guid.NewGuid().ToString("N");
                folder.Name ??= "文件夹";
            }

            foreach (var article in _cache.Articles)
            {
                article.Id ??= Guid.NewGuid().ToString("N");
                article.SubscriptionId ??= "";
                article.FeedTitle ??= "";
                article.Title ??= "";
                article.Link ??= "";
                article.Summary ??= "";
                article.HtmlContent ??= "";
                article.ImageUrl ??= "";
                article.LocalImagePath ??= "";
            }
        }

        private SqliteConnection OpenConnection()
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = GetDatabasePath()
            };
            var connection = new SqliteConnection(builder.ToString());
            connection.Open();
            return connection;
        }

        private static void ExecuteNonQuery(SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static long ToTicks(DateTimeOffset value)
            => value == default ? 0 : value.UtcTicks;

        private static DateTimeOffset FromTicks(long ticks)
            => ticks <= 0 ? default : new DateTimeOffset(ticks, TimeSpan.Zero);

        private string GetDatabasePath()
            => Path.Combine(_appDataPath, "rss_cache.db");

        private string GetLegacyCachePath()
            => Path.Combine(_appDataPath, "rss_cache.json");

        private static string HashText(string value)
        {
            var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
