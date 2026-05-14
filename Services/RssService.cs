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
        public string ImageUrl { get; set; } = "";
        public string LocalImagePath { get; set; } = "";
        public DateTimeOffset PublishedAt { get; set; }
        public string PublishedText => PublishedAt == default ? "" : PublishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        public string ImageSource => File.Exists(LocalImagePath) ? new Uri(LocalImagePath).AbsoluteUri : ImageUrl;
        public Microsoft.UI.Xaml.Visibility HasImage => string.IsNullOrWhiteSpace(ImageSource) ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
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
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private readonly string _appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TaskFlyout");

        private RssCache _cache = new();
        private bool _loaded;

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
            var summary = StripHtml(GetElementValue(item, "description") ?? GetElementValue(item, "encoded") ?? "");
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
            var summary = StripHtml(GetElementValue(entry, "summary") ?? GetElementValue(entry, "content") ?? "");
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
                var path = GetCachePath();
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    _cache = JsonSerializer.Deserialize(json, AppJsonContext.Default.RssCache) ?? new RssCache();
                }
            }
            catch
            {
                _cache = new RssCache();
            }

            _cache.Subscriptions ??= new List<RssSubscription>();
            _cache.Folders ??= new List<RssFolder>();
            _cache.Articles ??= new List<RssArticle>();
            foreach (var subscription in _cache.Subscriptions)
                subscription.FolderId ??= "";
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
            var json = JsonSerializer.Serialize(_cache, AppJsonContext.Default.RssCache);
            File.WriteAllText(GetCachePath(), json);
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

        private string GetCachePath()
            => Path.Combine(_appDataPath, "rss_cache.json");

        private static string HashText(string value)
        {
            var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
