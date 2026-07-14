using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Windows.Storage;

namespace Task_Flyout.Services
{
    public class RssSubscription
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public string FolderId { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        private string _localImagePath = "";
        public string LocalImagePath
        {
            get => _localImagePath;
            set
            {
                if (_localImagePath == value) return;
                _localImagePath = value ?? "";
                _imageCache = null;
                _imageCacheStamped = false;
            }
        }
        public DateTimeOffset LastFetchedAt { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        private Microsoft.UI.Xaml.Media.ImageSource? _imageCache;
        [System.Text.Json.Serialization.JsonIgnore]
        private bool _imageCacheStamped;

        public Microsoft.UI.Xaml.Media.ImageSource? ImageSource
        {
            get
            {
                if (_imageCacheStamped) return _imageCache;
                _imageCacheStamped = true;
                try
                {
                    if (!string.IsNullOrWhiteSpace(_localImagePath) && File.Exists(_localImagePath))
                    {
                        if (!RssService.IsPlausibleCachedImageFile(_localImagePath))
                            return null;

                        _imageCache = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(_localImagePath))
                        {
                            DecodePixelWidth = 32
                        };
                    }
                }
                catch { _imageCache = null; }
                return _imageCache;
            }
        }

        public Microsoft.UI.Xaml.Visibility HasImage =>
            ImageSource != null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
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
        private string _localImagePath = "";
        public string LocalImagePath
        {
            get => _localImagePath;
            set
            {
                if (_localImagePath == value) return;
                _localImagePath = value ?? "";
                _imageCache = null;
                _imageCacheStamped = false;
            }
        }
        public DateTimeOffset PublishedAt { get; set; }
        public string PublishedText => PublishedAt == default ? "" : PublishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        [System.Text.Json.Serialization.JsonIgnore]
        private Microsoft.UI.Xaml.Media.ImageSource? _imageCache;
        [System.Text.Json.Serialization.JsonIgnore]
        private bool _imageCacheStamped;

        public Microsoft.UI.Xaml.Media.ImageSource? ImageSource
        {
            get
            {
                if (_imageCacheStamped) return _imageCache;
                _imageCacheStamped = true;
                try
                {
                    if (!string.IsNullOrWhiteSpace(_localImagePath) && File.Exists(_localImagePath))
                    {
                        if (!RssService.IsPlausibleCachedImageFile(_localImagePath))
                            return null;

                        _imageCache = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(_localImagePath))
                        {
                            DecodePixelWidth = 120
                        };
                    }
                }
                catch { _imageCache = null; }
                return _imageCache;
            }
        }

        public Microsoft.UI.Xaml.Visibility HasImage =>
            ImageSource != null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

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
        private readonly ResourceLoader _loader = new();
        private const int FeedRefreshMinutes = 30;
        private const long MaxImageBytes = 5 * 1024 * 1024;
        private const long MaxFeedBytes = 5 * 1024 * 1024;
        private const int MaxRedirects = 5;
        private const int MaxFeedUrlLength = 4096;
        private const string ImageCacheDirectoryName = "RssImages";
        // This flows only while fetching a user-entered local source. Redirects always
        // clear it so a public feed cannot pivot into a private network address.
        private static readonly AsyncLocal<bool> AllowCurrentLocalNetworkRequest = new();
        private static readonly object InstancesLock = new();
        private static readonly List<WeakReference<RssService>> Instances = new();
        private static readonly SemaphoreSlim GlobalDataClearGate = new(1, 1);
        private static volatile bool GlobalDataClearInProgress;
        private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectCallback = ConnectToPublicAddressAsync
        })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private readonly string _appDataPath = AppDataPathHelper.LocalRoot;

        private RssCache _cache = new();
        private volatile bool _loaded;
        private DateTimeOffset _lastImageCachePrunedAt = DateTimeOffset.MinValue;
        private readonly object _loadLock = new();
        private RssSqliteRepository? _repository;
        private readonly RssDataLifecycle _dataLifecycle = new();

        public static event EventHandler? LocalDataCleared;

        public RssService()
        {
            lock (InstancesLock)
            {
                Instances.RemoveAll(reference => !reference.TryGetTarget(out _));
                Instances.Add(new WeakReference<RssService>(this));
            }
        }

        public static async Task ClearLocalDataAsync()
        {
            await GlobalDataClearGate.WaitAsync();
            try
            {
                await Task.Run(() =>
                {
                    GlobalDataClearInProgress = true;
                    var instances = GetLiveInstances();
                    foreach (var instance in instances)
                        instance.BeginDataClear();

                    bool clearedSuccessfully = false;
                    try
                    {
                        var databasePath = AppDataPathHelper.ResolveLocal("rss_cache.db");
                        var imageCachePath = AppDataPathHelper.ResolveLocal(ImageCacheDirectoryName);
                        RssStorageCleanup.DeleteAll(
                            databasePath,
                            AppDataPathHelper.ResolveLocal("rss_cache.json"),
                            imageCachePath);
                        clearedSuccessfully = true;
                    }
                    finally
                    {
                        foreach (var instance in instances)
                            instance.EndDataClear();
                        GlobalDataClearInProgress = false;
                    }

                    if (clearedSuccessfully)
                        RaiseLocalDataCleared();
                });
            }
            finally
            {
                GlobalDataClearGate.Release();
            }
        }

        private static void RaiseLocalDataCleared()
        {
            if (LocalDataCleared == null) return;

            foreach (EventHandler handler in LocalDataCleared.GetInvocationList())
            {
                try { handler(null, EventArgs.Empty); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"RSS data-cleared handler failed: {ex.Message}"); }
            }
        }

        public static Task FlushPendingCheckpointsAsync()
            => Task.Run(() =>
            {
                foreach (var instance in GetLiveInstances())
                {
                    lock (instance._loadLock)
                        instance._repository?.FlushCheckpoint();
                }
            });

        private static List<RssService> GetLiveInstances()
        {
            lock (InstancesLock)
            {
                var live = new List<RssService>();
                Instances.RemoveAll(reference =>
                {
                    if (!reference.TryGetTarget(out var instance)) return true;
                    live.Add(instance);
                    return false;
                });
                return live;
            }
        }

        private void BeginDataClear()
        {
            lock (_loadLock)
            {
                _dataLifecycle.BeginClear();
                _cache = new RssCache();
                _loaded = false;
                _repository = null;
                _lastImageCachePrunedAt = DateTimeOffset.MinValue;
            }
        }

        private void EndDataClear()
        {
            lock (_loadLock)
                _dataLifecycle.EndClear();
        }

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

        public IReadOnlyList<RssArticle> GetCachedArticlesPage(string? subscriptionId, string? folderId, int skip, int take)
        {
            EnsureLoaded();
            skip = Math.Max(0, skip);
            take = Math.Clamp(take, 1, 100);

            try
            {
                InitializeDatabase();
                return QueryCachedArticlesPage(subscriptionId, folderId, skip, take);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RSS SQL paging failed, falling back to in-memory: {ex.Message}");
                return GetCachedArticles(subscriptionId, folderId)
                    .Skip(skip)
                    .Take(take)
                    .ToList();
            }
        }

        public RssFolder AddFolder(string name)
        {
            EnsureLoaded();
            name = name.Trim();
            var folder = new RssFolder
            {
                Name = string.IsNullOrWhiteSpace(name) ? (_loader.GetStringOrDefault("TextNewFolder") ?? "New folder") : name,
                SortOrder = _cache.Folders.Count == 0 ? 0 : _cache.Folders.Max(item => item.SortOrder) + 1
            };
            _cache.Folders.Add(folder);
            SaveFolder(folder);
            return folder;
        }

        public void RemoveFolder(string folderId)
        {
            EnsureLoaded();
            _cache.Folders.RemoveAll(folder => folder.Id == folderId);
            foreach (var subscription in _cache.Subscriptions.Where(item => item.FolderId == folderId))
                subscription.FolderId = "";
            Repository.RemoveFolder(folderId);
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
            foreach (var folder in _cache.Folders)
                SaveFolder(folder);
        }

        public void MoveSubscriptionToFolder(string subscriptionId, string folderId)
        {
            EnsureLoaded();
            var subscription = _cache.Subscriptions.FirstOrDefault(item => item.Id == subscriptionId);
            if (subscription == null) return;
            subscription.FolderId = _cache.Folders.Any(folder => folder.Id == folderId) ? folderId : "";
            SaveSubscription(subscription);
        }

        public void RenameFolder(string folderId, string name)
        {
            EnsureLoaded();
            var folder = _cache.Folders.FirstOrDefault(item => item.Id == folderId);
            if (folder == null) return;
            folder.Name = string.IsNullOrWhiteSpace(name) ? folder.Name : name.Trim();
            SaveFolder(folder);
        }

        public void RenameSubscription(string subscriptionId, string title)
        {
            EnsureLoaded();
            var subscription = _cache.Subscriptions.FirstOrDefault(item => item.Id == subscriptionId);
            if (subscription == null) return;
            subscription.Title = string.IsNullOrWhiteSpace(title) ? subscription.Title : title.Trim();
            foreach (var article in _cache.Articles.Where(item => item.SubscriptionId == subscriptionId))
                article.FeedTitle = subscription.Title;
            SaveSubscription(subscription);
            UpdateArticleFeedTitle(subscriptionId, subscription.Title);
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
                    SaveSubscription(existing);
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
            SaveSubscription(subscription);
            await RefreshSubscriptionAsync(subscription, force: true);
            return subscription;
        }

        public void RemoveSubscription(string subscriptionId)
        {
            EnsureLoaded();
            _cache.Subscriptions.RemoveAll(item => item.Id == subscriptionId);
            _cache.Articles.RemoveAll(item => item.SubscriptionId == subscriptionId);
            Repository.RemoveSubscription(subscriptionId);
            PruneOrphanedImageCache(force: true);
        }

        public Task<List<RssArticle>> LoadMoreArticlesAsync(string? subscriptionId, string? folderId, int skip, int take)
        {
            // SQLite paging may block on disk or antivirus scans; callers update the UI only
            // after awaiting this operation, so keep the synchronous database work off it.
            return Task.Run(() => GetCachedArticlesPage(subscriptionId, folderId, skip, take).ToList());
        }

        public Task InitializeAsync()
        {
            // Initialization includes SQLite setup, migration, and DPAPI work. Keep it off
            // the dispatcher while preserving EnsureLoaded's existing single-loader lock.
            return Task.Run(EnsureLoaded);
        }

        public async Task RefreshSubscriptionAsync(
            RssSubscription subscription,
            bool force,
            CancellationToken cancellationToken = default)
        {
            EnsureLoaded();
            cancellationToken.ThrowIfCancellationRequested();
            if (!force && !ShouldRefresh(subscription)) return;

            RssDataLease lease;
            lock (_loadLock)
            {
                if (!_cache.Subscriptions.Any(item => ReferenceEquals(item, subscription))) return;
                lease = _dataLifecycle.Capture();
            }
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                lease.CancellationToken,
                cancellationToken);
            var refreshCancellationToken = linkedCancellation.Token;

            var xml = await FetchFeedAsync(subscription.Url, refreshCancellationToken);
            var parsed = ParseFeed(subscription, xml);

            string localFeedImagePath = subscription.LocalImagePath;
            if (!string.IsNullOrWhiteSpace(parsed.FeedImageUrl))
                localFeedImagePath = await CacheImageAsync(parsed.FeedImageUrl, refreshCancellationToken);

            foreach (var article in parsed.Articles)
                article.LocalImagePath = await CacheImageAsync(article.ImageUrl, refreshCancellationToken);

            refreshCancellationToken.ThrowIfCancellationRequested();
            lock (_loadLock)
            {
                if (!_dataLifecycle.IsCurrent(lease) || cancellationToken.IsCancellationRequested) return;

                if (!string.IsNullOrWhiteSpace(parsed.FeedTitle))
                    subscription.Title = parsed.FeedTitle;
                if (!string.IsNullOrWhiteSpace(parsed.FeedImageUrl))
                {
                    subscription.ImageUrl = parsed.FeedImageUrl;
                    subscription.LocalImagePath = localFeedImagePath;
                }

                foreach (var article in parsed.Articles)
                {
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
                SaveRefresh(subscription, parsed.Articles);
                TrimDatabaseArticles();
            }
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

            if (url.Length > MaxFeedUrlLength ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                !string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new InvalidOperationException("Feed URL is invalid or too long.");
            }

            return url;
        }

        private (string FeedTitle, string FeedImageUrl, List<RssArticle> Articles) ParseFeed(RssSubscription subscription, string xml)
        {
            if (!RssFeedXml.TryLoadDocument(xml, MaxFeedBytes * 2, out var doc))
                return (subscription.Title, "", new List<RssArticle>());

            var root = doc!.Root;
            if (root == null) return ("", "", new List<RssArticle>());

            var feedTitle = GetElementValue(root.Element("channel"), "title")
                            ?? GetElementValue(root, "title")
                            ?? subscription.Title;
            var feedImageUrl = GetElementValue(root.Element("channel")?.Element("image"), "url")
                               ?? GetElementValue(root, "logo")
                               ?? GetElementValue(root, "icon")
                               ?? "";

            var items = root.Element("channel")?.Elements("item") ?? Enumerable.Empty<XElement>();
            var articles = items.Any()
                ? items.Select(item => ParseRssItem(subscription, feedTitle, item)).ToList()
                : root.Elements().Where(element => element.Name.LocalName == "entry")
                    .Select(entry => ParseAtomEntry(subscription, feedTitle, entry))
                    .ToList();

            return (feedTitle, feedImageUrl, articles.Where(article => !string.IsNullOrWhiteSpace(article.Title)).ToList());
        }

        private static RssArticle ParseRssItem(RssSubscription subscription, string feedTitle, XElement item)
        {
            var title = GetElementValue(item, "title") ?? "(Untitled)";
            string link = GetElementValue(item, "link") ?? "";
            var html = GetElementValue(item, "encoded") ?? GetElementValue(item, "description") ?? "";
            var summary = StripHtml(html);
            var published = ParseDate(GetElementValue(item, "pubDate") ?? GetElementValue(item, "date"));
            var id = GetElementValue(item, "guid") ?? link ?? $"{subscription.Id}:{title}";
            var imageUrl = FindImageUrl(item, html, link, subscription.Url);

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
            var title = GetElementValue(entry, "title") ?? "(Untitled)";
            string link = entry.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "link" &&
                ((string?)element.Attribute("rel") == null || (string?)element.Attribute("rel") == "alternate"))?.Attribute("href")?.Value ?? "";
            var html = GetElementValue(entry, "content") ?? GetElementValue(entry, "summary") ?? "";
            var summary = StripHtml(html);
            var published = ParseDate(GetElementValue(entry, "published") ?? GetElementValue(entry, "updated"));
            var id = GetElementValue(entry, "id") ?? link ?? $"{subscription.Id}:{title}";
            var imageUrl = FindImageUrl(entry, html, link, subscription.Url);

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

        private static string FindImageUrl(XElement item, string htmlContent, string? articleUrl, string feedUrl)
        {
            var baseUrl = !string.IsNullOrWhiteSpace(articleUrl) ? articleUrl : feedUrl;

            foreach (var media in item.Descendants().Where(element =>
                         element.Name.LocalName is "thumbnail" or "content" &&
                         element.Attribute("url") != null))
            {
                var mediaUrl = media.Attribute("url")?.Value;
                if (IsLikelyImageElement(media, mediaUrl) &&
                    TryResolveImageUrl(mediaUrl, baseUrl, out var resolvedMediaUrl))
                    return resolvedMediaUrl;
            }

            var enclosure = item.Elements().FirstOrDefault(element =>
                element.Name.LocalName == "enclosure" && element.Attribute("url") != null &&
                ((element.Attribute("type")?.Value.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ?? false) ||
                 IsLikelyImageUrl(element.Attribute("url")?.Value)));
            if (TryResolveImageUrl(enclosure?.Attribute("url")?.Value, baseUrl, out var enclosureUrl))
                return enclosureUrl;

            var itemXml = item.ToString();
            var rawHtml = string.Join(" ", htmlContent, itemXml, WebUtility.HtmlDecode(itemXml));
            foreach (var attrName in new[] { "src", "data-src", "data-original", "data-lazy-src" })
            {
                var imgMatch = System.Text.RegularExpressions.Regex.Match(
                    rawHtml,
                    $@"<\s*img\b[^>]+\b{attrName}\s*=\s*['""]([^'""]+)['""]",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (imgMatch.Success &&
                    TryResolveImageUrl(imgMatch.Groups[1].Value, baseUrl, out var imgUrl))
                    return imgUrl;
            }

            var srcSetMatch = System.Text.RegularExpressions.Regex.Match(
                rawHtml,
                @"<\s*(?:img|source)\b[^>]+\bsrcset\s*=\s*['""]([^'""]+)['""]",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (srcSetMatch.Success)
            {
                foreach (var candidate in srcSetMatch.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    var src = candidate.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (TryResolveImageUrl(src, baseUrl, out var srcSetUrl))
                        return srcSetUrl;
                }
            }

            var match = System.Text.RegularExpressions.Regex.Match(rawHtml, @"https?://[^\s'""]+\.(png|jpe?g|webp|gif)(?:\?[^\s'""]*)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success && TryResolveImageUrl(match.Value, baseUrl, out var fallbackUrl) ? fallbackUrl : "";
        }

        private static bool IsLikelyImageElement(XElement element, string? url)
        {
            if (element.Name.LocalName == "thumbnail") return true;
            if (element.Attribute("type")?.Value.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true) return true;
            if (string.Equals(element.Attribute("medium")?.Value, "image", StringComparison.OrdinalIgnoreCase)) return true;
            return IsLikelyImageUrl(url);
        }

        private static bool IsLikelyImageUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            var path = url.Split('?', '#')[0];
            return path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
                   || path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryResolveImageUrl(string? value, string baseUrl, out string resolved)
        {
            resolved = "";
            if (string.IsNullOrWhiteSpace(value)) return false;

            var candidate = WebUtility.HtmlDecode(value).Trim();
            if (candidate.Length == 0) return false;
            if (candidate.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return false;
            if (candidate.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) return false;

            if (candidate.StartsWith("//", StringComparison.Ordinal))
                candidate = "https:" + candidate;

            Uri? uri = null;
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var absolute))
            {
                uri = absolute;
            }
            else if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) &&
                     Uri.TryCreate(baseUri, candidate, out var relative))
            {
                uri = relative;
            }

            if (uri == null) return false;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

            resolved = uri.ToString();
            return true;
        }

        private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, System.Threading.CancellationToken cancellationToken)
        {
            var host = context.DnsEndPoint.Host;
            var port = context.DnsEndPoint.Port;

            var addresses = await ResolveConnectAddressesAsync(host, cancellationToken);
            if (addresses.Count == 0)
                throw new InvalidOperationException("URL resolved to a non-public IP address.");

            Exception? lastError = null;
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    socket.Dispose();
                }
            }

            throw lastError ?? new InvalidOperationException("Unable to connect to feed host.");
        }

        private static async Task<bool> IsSafeToFetchAsync(Uri uri)
        {
            if (AllowCurrentLocalNetworkRequest.Value)
                return true;

            if (uri.HostNameType is not (UriHostNameType.IPv4 or UriHostNameType.IPv6))
            {
                try
                {
                    var addresses = await Dns.GetHostAddressesAsync(uri.Host);
                    return RssFetchPolicy.AreResolvedAddressesSafe(addresses);
                }
                catch
                {
                    return false;
                }
            }

            if (IPAddress.TryParse(uri.Host, out var parsed))
                return NetworkSafety.IsPublicIpAddress(parsed);

            return false;
        }

        private static bool AllowRssLocalNetworkAccess
            => ApplicationData.Current.LocalSettings.Values["AllowRssLocalNetworkAccess"] as bool? ?? false;

        private static async Task<List<IPAddress>> ResolveConnectAddressesAsync(string host, System.Threading.CancellationToken cancellationToken)
        {
            if (!AllowCurrentLocalNetworkRequest.Value)
                return await ResolvePublicAddressesAsync(host, cancellationToken);

            IPAddress[] addresses = IPAddress.TryParse(host, out var parsed)
                ? new[] { parsed }
                : await Dns.GetHostAddressesAsync(host, cancellationToken);

            return addresses
                .Select(NetworkSafety.Normalize)
                .Distinct()
                .ToList();
        }

        private static async Task<List<IPAddress>> ResolvePublicAddressesAsync(string host, System.Threading.CancellationToken cancellationToken)
        {
            IPAddress[] addresses;
            if (IPAddress.TryParse(host, out var parsed))
            {
                addresses = new[] { parsed };
            }
            else
            {
                addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
                if (!RssFetchPolicy.AreResolvedAddressesSafe(addresses))
                    return new List<IPAddress>();
            }

            return addresses
                .Select(NetworkSafety.Normalize)
                .Where(NetworkSafety.IsPublicIpAddress)
                .Distinct()
                .ToList();
        }

        private const long MaxRemoteImageBytes = 10L * 1024 * 1024;
        private const int MaxRemoteImageFetchesPerHost = 2;
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> RemoteImageHostGates = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Downloads a remote RSS article image through the app's SSRF-guarded HTTP client
        /// (each connection is DNS-pinned to a public IP), so the WebView never connects to the
        /// host directly — closing the DNS-rebinding gap a host-string check can't. Returns null
        /// (caller should block) on any failure, redirect to a non-public host, non-image
        /// content type, or oversize payload.
        /// </summary>
        public async Task<(byte[] Bytes, string ContentType)?> FetchRemoteImageSafelyAsync(string url, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                    return null;

                var gate = RemoteImageHostGates.GetOrAdd(uri.IdnHost, _ => new SemaphoreSlim(MaxRemoteImageFetchesPerHost, MaxRemoteImageFetchesPerHost));
                await gate.WaitAsync(cancellationToken);
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    using var response = await SendWithRedirectsAsync(request, cancellationToken);
                    if (!response.IsSuccessStatusCode) return null;

                    var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                    if (!contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
                    if (response.Content.Headers.ContentLength is long declared && declared > MaxRemoteImageBytes) return null;

                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using var buffer = new MemoryStream();
                    var chunk = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await stream.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken)) > 0)
                    {
                        total += read;
                        if (total > MaxRemoteImageBytes) return null;
                        buffer.Write(chunk, 0, read);
                    }

                    return (buffer.ToArray(), contentType);
                }
                finally
                {
                    gate.Release();
                }
            }
            catch
            {
                return null;
            }
        }

        private static async Task<HttpResponseMessage> SendWithRedirectsAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            var currentUri = request.RequestUri!;
            var requestHeaders = request.Headers
                .Select(header => (header.Key, Values: header.Value.ToArray()))
                .ToList();
            if (!await IsSafeToFetchAsync(currentUri))
                throw new InvalidOperationException("URL resolved to a non-public IP address.");

            HttpResponseMessage response;
            for (var hop = 0; ; hop++)
            {
                response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (!RssFetchPolicy.ShouldFollowRedirect(response.StatusCode, hop, MaxRedirects))
                    return response;

                var headerLocation = response.Headers.Location;
                if (headerLocation == null)
                    return response;

                if (!RssFetchPolicy.TryResolveHttpRedirect(currentUri, headerLocation, out var location))
                {
                    response.Dispose();
                    throw new InvalidOperationException("Redirect to non-HTTP(S) scheme.");
                }

                if (!await IsSafeToFetchAsync(location))
                {
                    response.Dispose();
                    throw new InvalidOperationException("Redirect URL resolved to a non-public IP address.");
                }

                currentUri = location;
                response.Dispose();
                AllowCurrentLocalNetworkRequest.Value = false;
                request = new HttpRequestMessage(HttpMethod.Get, currentUri);
                foreach (var header in requestHeaders)
                    request.Headers.TryAddWithoutValidation(header.Key, header.Values);
            }
        }

        private async Task<string> FetchFeedAsync(string url, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                throw new InvalidOperationException("Invalid feed URL.");
            if (!RssFetchPolicy.IsAllowedFetchScheme(uri))
                throw new InvalidOperationException("Feed URL must use HTTP or HTTPS.");

            var priorLocalNetworkPermission = AllowCurrentLocalNetworkRequest.Value;
            AllowCurrentLocalNetworkRequest.Value = AllowRssLocalNetworkAccess && IsLocalNetworkUri(uri);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await SendWithRedirectsAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                if (response.Content.Headers.ContentLength is > MaxFeedBytes)
                    throw new InvalidOperationException("Feed response exceeds maximum size.");

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var buffer = new MemoryStream();
                var chunk = new byte[81920];
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(chunk, cancellationToken)) > 0)
                {
                    if (buffer.Length + bytesRead > MaxFeedBytes)
                        throw new InvalidOperationException("Feed response exceeds maximum size.");
                    buffer.Write(chunk, 0, bytesRead);
                }

                return DecodeFeedBytes(buffer.ToArray(), response.Content.Headers.ContentType?.CharSet);
            }
            finally
            {
                AllowCurrentLocalNetworkRequest.Value = priorLocalNetworkPermission;
            }
        }

        private static bool IsLocalNetworkUri(Uri uri)
        {
            if (NetworkSafety.IsUnsafeHost(uri)) return true;

            try
            {
                return Dns.GetHostAddresses(uri.Host).Any(address => !NetworkSafety.IsPublicIpAddress(address));
            }
            catch
            {
                return false;
            }
        }

        static RssService()
        {
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); }
            catch { }

            try
            {
                HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("TaskFlyout/1.0");
            }
            catch { }
        }

        private static readonly HashSet<string> AllowedFeedCharsets = new(StringComparer.OrdinalIgnoreCase)
        {
            "utf-8", "utf8",
            "utf-16", "utf-16le", "utf-16be",
            "us-ascii", "ascii",
            "iso-8859-1", "latin1",
            "iso-8859-2", "iso-8859-15",
            "windows-1250", "windows-1251", "windows-1252", "windows-1253", "windows-1254",
            "gb2312", "gbk", "gb18030",
            "big5",
            "shift_jis", "shift-jis", "euc-jp", "iso-2022-jp",
            "euc-kr"
        };

        private static string DecodeFeedBytes(byte[] bytes, string? charset)
        {
            if (!string.IsNullOrWhiteSpace(charset))
            {
                var normalizedCharset = charset.Trim().Trim('"', '\'').ToLowerInvariant();
                if (AllowedFeedCharsets.Contains(normalizedCharset))
                {
                    try
                    {
                        return Encoding.GetEncoding(normalizedCharset).GetString(bytes);
                    }
                    catch
                    {
                    }
                }
            }

            using var stream = new MemoryStream(bytes);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }

        private async Task<string> CacheImageAsync(string imageUrl, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(imageUrl) || !Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
                return "";
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return "";

            if (!await IsSafeToFetchAsync(uri))
                return "";

            try
            {
                var imagesPath = GetImageCacheRoot();
                var extension = Path.GetExtension(uri.AbsolutePath);
                if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
                    extension = ".img";
                var path = AppDataPathHelper.ResolveUnderRoot(imagesPath, HashText(imageUrl) + extension);
                if (File.Exists(path))
                {
                    if (IsPlausibleCachedImageFile(path)) return path;
                    try { File.Delete(path); } catch { return ""; }
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.ParseAdd("image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
                using var response = await SendWithRedirectsAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                if (!IsAllowedImageContentType(contentType))
                    return "";

                if (response.Content.Headers.ContentLength is > MaxImageBytes)
                    return "";

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var buffer = new MemoryStream();
                var chunk = new byte[81920];
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(chunk, cancellationToken)) > 0)
                {
                    if (buffer.Length + bytesRead > MaxImageBytes)
                        return "";
                    buffer.Write(chunk, 0, bytesRead);
                }

                var bytes = buffer.ToArray();
                if (!IsPlausibleImageBytes(bytes))
                    return "";

                await File.WriteAllBytesAsync(path, bytes, cancellationToken);
                return path;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch
            {
                return "";
            }
        }

        internal static bool IsPlausibleCachedImageFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return false;
                if (!IsPathUnderRoot(GetImageCacheRoot(), path)) return false;

                var info = new FileInfo(path);
                if (info.Length <= 8 || info.Length > MaxImageBytes) return false;

                using var stream = File.OpenRead(path);
                Span<byte> header = stackalloc byte[16];
                var read = stream.Read(header);
                return IsPlausibleImageHeader(header[..read]);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAllowedImageContentType(string contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType)) return true;
            if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return true;
            return contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)
                   || contentType.Equals("binary/octet-stream", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPlausibleImageBytes(byte[] bytes)
            => bytes.Length > 8 && IsPlausibleImageHeader(bytes.AsSpan(0, Math.Min(bytes.Length, 16)));

        private static bool IsPlausibleImageHeader(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length < 4) return false;

            if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return true;
            if (bytes.Length >= 8 &&
                bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
                bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A) return true;
            if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38) return true;
            if (bytes.Length >= 12 &&
                bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
                bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50) return true;
            if (bytes[0] == 0x42 && bytes[1] == 0x4D) return true;
            if (bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0x01 && bytes[3] == 0x00) return true;

            return false;
        }

        private void EnsureLoaded()
        {
            if (GlobalDataClearInProgress)
                throw new InvalidOperationException("RSS local data is being cleared.");
            if (_loaded) return;

            lock (_loadLock)
            {
                if (_dataLifecycle.IsClearInProgress || GlobalDataClearInProgress)
                    throw new InvalidOperationException("RSS local data is being cleared.");
                if (_loaded) return;

                RssCache loaded;
                try
                {
                    InitializeDatabase();
                    Repository.MigrateSensitiveData();
                    loaded = LoadFromDatabase();
                }
                catch
                {
                    loaded = new RssCache();
                }

                loaded.Subscriptions ??= new List<RssSubscription>();
                loaded.Folders ??= new List<RssFolder>();
                loaded.Articles ??= new List<RssArticle>();
                foreach (var subscription in loaded.Subscriptions)
                {
                    subscription.Id ??= Guid.NewGuid().ToString("N");
                    subscription.Title ??= "";
                    subscription.Url ??= "";
                    subscription.FolderId ??= "";
                    subscription.ImageUrl ??= "";
                    subscription.LocalImagePath ??= "";
                }

                foreach (var folder in loaded.Folders)
                {
                    folder.Id ??= Guid.NewGuid().ToString("N");
                    folder.Name ??= (_loader.GetStringOrDefault("TextFolder") ?? "Folder");
                }

                foreach (var article in loaded.Articles)
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

                _cache = loaded;

                try
                {
                    TryMigrateLegacyJsonCache();
                }
                catch
                {
                }

                _loaded = true;
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
            PruneOrphanedImageCache();
        }

        private void PruneOrphanedImageCache(bool force = false)
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                if (!force && now - _lastImageCachePrunedAt < TimeSpan.FromHours(6))
                    return;
                _lastImageCachePrunedAt = now;

                var imagesPath = GetImageCacheRoot();
                if (!Directory.Exists(imagesPath)) return;

                var referencedPaths = _cache.Subscriptions
                    .Select(subscription => subscription.LocalImagePath)
                    .Concat(Repository.LoadArticleImagePaths())
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path =>
                    {
                        try
                        {
                            return IsPathUnderRoot(imagesPath, path) ? Path.GetFullPath(path) : "";
                        }
                        catch
                        {
                            return "";
                        }
                    })
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var path in Directory.EnumerateFiles(imagesPath))
                {
                    try
                    {
                        var fullPath = Path.GetFullPath(path);
                        if (!referencedPaths.Contains(fullPath) || !IsPlausibleCachedImageFile(fullPath))
                            File.Delete(fullPath);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"RSS image cache cleanup failed for {path}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RSS image cache cleanup failed: {ex.Message}");
            }
        }

        private static string GetImageCacheRoot()
            => AppDataPathHelper.EnsureDirectory(AppDataPathHelper.ResolveLocal(ImageCacheDirectoryName));

        private static bool IsPathUnderRoot(string root, string path)
        {
            var fullRoot = Path.GetFullPath(root);
            var fullPath = Path.GetFullPath(path);
            var rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar)
                ? fullRoot
                : fullRoot + Path.DirectorySeparatorChar;

            return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        }

        private void InitializeDatabase()
        {
            Repository.Initialize();
        }

        private RssCache LoadFromDatabase()
        {
            var snapshot = Repository.LoadSnapshot(0);
            return new RssCache
            {
                Folders = snapshot.Folders.Select(folder => new RssFolder
                {
                    Id = folder.Id,
                    Name = folder.Name,
                    SortOrder = folder.SortOrder
                }).ToList(),
                Subscriptions = snapshot.Subscriptions.Select(subscription => new RssSubscription
                {
                    Id = subscription.Id,
                    Title = subscription.Title,
                    Url = subscription.Url,
                    FolderId = subscription.FolderId,
                    ImageUrl = subscription.ImageUrl,
                    LocalImagePath = subscription.LocalImagePath,
                    LastFetchedAt = FromTicks(subscription.LastFetchedUtcTicks)
                }).ToList(),
                Articles = snapshot.Articles.Select(ToArticleListItem).ToList()
            };
        }

        private List<RssArticle> QueryCachedArticlesPage(string? subscriptionId, string? folderId, int skip, int take)
        {
            return Repository.QueryArticlesPage(subscriptionId, folderId, skip, take)
                .Select(record => new RssArticle
                {
                    Id = record.Id,
                    SubscriptionId = record.SubscriptionId,
                    FeedTitle = record.FeedTitle,
                    Title = record.Title,
                    Link = record.Link,
                    Summary = record.Summary,
                    HtmlContent = "",
                    ImageUrl = record.ImageUrl,
                    LocalImagePath = record.LocalImagePath,
                    PublishedAt = FromTicks(record.PublishedUtcTicks)
                })
                .ToList();
        }

        /// <summary>
        /// Fetch the full HTML body for a single article (cached column not loaded by
        /// the list-paging code path). Returns empty string if missing.
        /// </summary>
        public string GetArticleHtml(string? articleId)
        {
            if (string.IsNullOrWhiteSpace(articleId)) return "";
            EnsureLoaded();
            try
            {
                return Repository.GetArticleHtml(articleId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetArticleHtml failed for {articleId}: {ex.Message}");
                return "";
            }
        }

        private static RssArticle ToArticleListItem(RssArticleRecord record)
            => new()
            {
                Id = record.Id,
                SubscriptionId = record.SubscriptionId,
                FeedTitle = record.FeedTitle,
                Title = record.Title,
                Link = record.Link,
                Summary = record.Summary,
                HtmlContent = "",
                ImageUrl = record.ImageUrl,
                LocalImagePath = record.LocalImagePath,
                PublishedAt = FromTicks(record.PublishedUtcTicks)
            };

        private void SaveFolder(RssFolder folder)
        {
            Repository.UpsertFolder(new RssFolderRecord(folder.Id ?? "", folder.Name ?? "", folder.SortOrder));
        }

        private void SaveSubscription(RssSubscription subscription)
        {
            Repository.UpsertSubscription(ToSubscriptionRecord(subscription));
        }

        private void SaveArticle(RssArticle article)
        {
            Repository.UpsertArticle(ToArticleWriteRecord(article));
        }

        private void SaveRefresh(RssSubscription subscription, IEnumerable<RssArticle> articles)
            => Repository.SaveRefresh(ToSubscriptionRecord(subscription), articles.Select(ToArticleWriteRecord));

        private void UpdateArticleFeedTitle(string subscriptionId, string title)
            => Repository.UpdateArticleFeedTitle(subscriptionId, title);

        private static RssSubscriptionRecord ToSubscriptionRecord(RssSubscription subscription)
            => new(
                subscription.Id ?? "",
                subscription.Title ?? "",
                subscription.Url ?? "",
                subscription.FolderId ?? "",
                subscription.ImageUrl ?? "",
                subscription.LocalImagePath ?? "",
                ToTicks(subscription.LastFetchedAt));

        private static RssArticleWriteRecord ToArticleWriteRecord(RssArticle article)
            => new(
                article.Id ?? "",
                article.SubscriptionId ?? "",
                article.FeedTitle ?? "",
                article.Title ?? "",
                article.Link ?? "",
                article.Summary ?? "",
                article.HtmlContent ?? "",
                article.ImageUrl ?? "",
                article.LocalImagePath ?? "",
                ToTicks(article.PublishedAt));

        private void TrimDatabaseArticles()
            => Repository.TrimArticles(1000);

        private void SaveToDatabase()
            => Repository.SaveSnapshot(
                _cache.Folders.Select(folder => new RssFolderRecord(folder.Id ?? "", folder.Name ?? "", folder.SortOrder)),
                _cache.Subscriptions.Select(ToSubscriptionRecord),
                _cache.Articles.Select(ToArticleWriteRecord),
                1000);

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
                subscription.ImageUrl ??= "";
                subscription.LocalImagePath ??= "";
            }

            foreach (var folder in _cache.Folders)
            {
                folder.Id ??= Guid.NewGuid().ToString("N");
                folder.Name ??= (_loader.GetStringOrDefault("TextFolder") ?? "Folder");
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

        private RssSqliteRepository Repository
            => _repository ??= new RssSqliteRepository(
                GetDatabasePath(),
                () => _dataLifecycle.IsClearInProgress || GlobalDataClearInProgress);

        private static long ToTicks(DateTimeOffset value)
            => value == default ? 0 : value.UtcTicks;

        private static DateTimeOffset FromTicks(long ticks)
            => ticks <= 0 ? default : new DateTimeOffset(ticks, TimeSpan.Zero);

        private string GetDatabasePath()
            => AppDataPathHelper.ResolveLocal("rss_cache.db");

        private string GetLegacyCachePath()
            => AppDataPathHelper.ResolveLocal("rss_cache.json");

        private static string HashText(string value)
        {
            var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
