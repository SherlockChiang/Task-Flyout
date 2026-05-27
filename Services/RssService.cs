using Microsoft.Data.Sqlite;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

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
                _imageCacheMiss = false;
            }
        }
        public DateTimeOffset LastFetchedAt { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        private WeakReference<Microsoft.UI.Xaml.Media.ImageSource>? _imageCache;
        [System.Text.Json.Serialization.JsonIgnore]
        private bool _imageCacheMiss;

        public Microsoft.UI.Xaml.Media.ImageSource? ImageSource
        {
            get
            {
                if (_imageCache?.TryGetTarget(out var cached) == true) return cached;
                if (_imageCacheMiss) return null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(_localImagePath) && File.Exists(_localImagePath))
                    {
                        var image = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(_localImagePath))
                        {
                            DecodePixelWidth = 32
                        };
                        _imageCache = new WeakReference<Microsoft.UI.Xaml.Media.ImageSource>(image);
                        return image;
                    }
                }
                catch { }

                _imageCacheMiss = true;
                return null;
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
                _imageCacheMiss = false;
            }
        }
        public DateTimeOffset PublishedAt { get; set; }
        public string PublishedText => PublishedAt == default ? "" : PublishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        [System.Text.Json.Serialization.JsonIgnore]
        private WeakReference<Microsoft.UI.Xaml.Media.ImageSource>? _imageCache;
        [System.Text.Json.Serialization.JsonIgnore]
        private bool _imageCacheMiss;

        public Microsoft.UI.Xaml.Media.ImageSource? ImageSource
        {
            get
            {
                if (_imageCache?.TryGetTarget(out var cached) == true) return cached;
                if (_imageCacheMiss) return null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(_localImagePath) && File.Exists(_localImagePath))
                    {
                        var image = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(_localImagePath))
                        {
                            DecodePixelWidth = 120
                        };
                        _imageCache = new WeakReference<Microsoft.UI.Xaml.Media.ImageSource>(image);
                        return image;
                    }
                }
                catch { }

                _imageCacheMiss = true;
                return null;
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
        private const int SchemaVersion = 1;
        private const long MaxImageBytes = 5 * 1024 * 1024;
        private const long MaxFeedBytes = 5 * 1024 * 1024;
        private const int MaxRedirects = 5;
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
        private readonly object _loadLock = new();
        private bool _databaseInitialized;
        private string? _connectionString;

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
            catch
            {
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
                Name = string.IsNullOrWhiteSpace(name) ? (_loader.GetString("TextNewFolder") ?? "New folder") : name,
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

        public Task<List<RssArticle>> LoadMoreArticlesAsync(string? subscriptionId, string? folderId, int skip, int take)
        {
            return Task.FromResult(GetCachedArticlesPage(subscriptionId, folderId, skip, take).ToList());
        }

        public async Task RefreshSubscriptionAsync(RssSubscription subscription, bool force)
        {
            EnsureLoaded();
            if (!force && !ShouldRefresh(subscription)) return;

            var xml = await FetchFeedAsync(subscription.Url);
            var parsed = ParseFeed(subscription, xml);

            if (!string.IsNullOrWhiteSpace(parsed.FeedTitle))
                subscription.Title = parsed.FeedTitle;
            if (!string.IsNullOrWhiteSpace(parsed.FeedImageUrl))
            {
                subscription.ImageUrl = parsed.FeedImageUrl;
                subscription.LocalImagePath = await CacheImageAsync(parsed.FeedImageUrl);
            }

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
            Save();
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

        private (string FeedTitle, string FeedImageUrl, List<RssArticle> Articles) ParseFeed(RssSubscription subscription, string xml)
        {
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            var doc = XDocument.Load(reader);
            var root = doc.Root;
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
            var title = GetElementValue(entry, "title") ?? "(Untitled)";
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

            var imgMatch = System.Text.RegularExpressions.Regex.Match(
                item.ToString(),
                @"<\s*img\b[^>]+\bsrc\s*=\s*['""]([^'""]+)['""]",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (imgMatch.Success && imgMatch.Groups[1].Value is { Length: > 0 } imgSrc &&
                (imgSrc.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                 imgSrc.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                return imgSrc;

            var match = System.Text.RegularExpressions.Regex.Match(fallbackText, @"https?://[^\s'""]+\.(png|jpe?g|webp|gif)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success ? match.Value : "";
        }

        private static async ValueTask<Stream> ConnectToPublicAddressAsync(SocketsHttpConnectionContext context, System.Threading.CancellationToken cancellationToken)
        {
            var host = context.DnsEndPoint.Host;
            var port = context.DnsEndPoint.Port;

            var addresses = await ResolvePublicAddressesAsync(host, cancellationToken);
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
            if (uri.HostNameType is not (UriHostNameType.IPv4 or UriHostNameType.IPv6))
            {
                try
                {
                    var addresses = await Dns.GetHostAddressesAsync(uri.Host);
                    return addresses.All(addr => IsPublicIpAddress(addr));
                }
                catch
                {
                    return false;
                }
            }

            if (IPAddress.TryParse(uri.Host, out var parsed))
                return IsPublicIpAddress(parsed);

            return false;
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
                if (addresses.Any(address => !IsPublicIpAddress(address)))
                    return new List<IPAddress>();
            }

            return addresses
                .Select(NormalizeIpAddress)
                .Where(IsPublicIpAddress)
                .Distinct()
                .ToList();
        }

        private static bool IsPublicIpAddress(IPAddress address)
        {
            address = NormalizeIpAddress(address);

            if (IPAddress.IsLoopback(address) ||
                address.Equals(IPAddress.Any) ||
                address.Equals(IPAddress.Broadcast) ||
                address.Equals(IPAddress.None) ||
                address.Equals(IPAddress.IPv6Any) ||
                address.Equals(IPAddress.IPv6None))
                return false;

            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var bytes = address.GetAddressBytes();
                return bytes[0] switch
                {
                    0 => false,
                    10 => false,
                    127 => false,
                    100 when bytes[1] >= 64 && bytes[1] <= 127 => false,
                    169 when bytes[1] == 254 => false,
                    172 when bytes[1] >= 16 && bytes[1] <= 31 => false,
                    192 when bytes[1] == 0 => false,
                    192 when bytes[1] == 0 && bytes[2] == 2 => false,
                    192 when bytes[1] == 88 && bytes[2] == 99 => false,
                    192 when bytes[1] == 168 => false,
                    198 when bytes[1] == 18 || bytes[1] == 19 => false,
                    198 when bytes[1] == 51 && bytes[2] == 100 => false,
                    203 when bytes[1] == 0 && bytes[2] == 113 => false,
                    >= 224 => false,
                    _ => true
                };
            }

            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                var bytes = address.GetAddressBytes();
                if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
                    return false;
                if ((bytes[0] & 0xfe) == 0xfc)
                    return false;
                if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8)
                    return false;
            }

            return true;
        }

        private static IPAddress NormalizeIpAddress(IPAddress address)
            => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        private static bool IsRedirect(HttpStatusCode code)
            => code is HttpStatusCode.Moved or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect
                or (HttpStatusCode)308;

        private static async Task<HttpResponseMessage> SendWithRedirectsAsync(HttpRequestMessage request)
        {
            var currentUri = request.RequestUri!;
            if (!await IsSafeToFetchAsync(currentUri))
                throw new InvalidOperationException("URL resolved to a non-public IP address.");

            HttpResponseMessage response;
            for (var hop = 0; ; hop++)
            {
                response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (!IsRedirect(response.StatusCode) || hop >= MaxRedirects)
                    return response;

                var location = response.Headers.Location;
                if (location == null)
                    return response;

                if (!location.IsAbsoluteUri)
                    location = new Uri(currentUri, location);

                if (location.Scheme != "http" && location.Scheme != "https")
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
                request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            }
        }

        private async Task<string> FetchFeedAsync(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                throw new InvalidOperationException("Invalid feed URL.");
            if (uri.Scheme != "http" && uri.Scheme != "https")
                throw new InvalidOperationException("Feed URL must use HTTP or HTTPS.");

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await SendWithRedirectsAsync(request);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is > MaxFeedBytes)
                throw new InvalidOperationException("Feed response exceeds maximum size.");

            using var stream = await response.Content.ReadAsStreamAsync();
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(chunk)) > 0)
            {
                if (buffer.Length + bytesRead > MaxFeedBytes)
                    throw new InvalidOperationException("Feed response exceeds maximum size.");
                buffer.Write(chunk, 0, bytesRead);
            }

            return DecodeFeedBytes(buffer.ToArray(), response.Content.Headers.ContentType?.CharSet);
        }

        static RssService()
        {
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); }
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

        private async Task<string> CacheImageAsync(string imageUrl)
        {
            if (string.IsNullOrWhiteSpace(imageUrl) || !Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
                return "";
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return "";

            if (!await IsSafeToFetchAsync(uri))
                return "";

            try
            {
                var imagesPath = AppDataPathHelper.EnsureDirectory(AppDataPathHelper.ResolveLocal("RssImages"));
                var extension = Path.GetExtension(uri.AbsolutePath);
                if (string.IsNullOrWhiteSpace(extension) || extension.Length > 8)
                    extension = ".img";
                var path = AppDataPathHelper.ResolveUnderRoot(imagesPath, HashText(imageUrl) + extension);
                if (File.Exists(path)) return path;

                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await SendWithRedirectsAsync(request);
                response.EnsureSuccessStatusCode();

                if (response.Content.Headers.ContentLength is > MaxImageBytes)
                    return "";

                using var stream = await response.Content.ReadAsStreamAsync();
                using var buffer = new MemoryStream();
                var chunk = new byte[81920];
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(chunk)) > 0)
                {
                    if (buffer.Length + bytesRead > MaxImageBytes)
                        return "";
                    buffer.Write(chunk, 0, bytesRead);
                }

                await File.WriteAllBytesAsync(path, buffer.ToArray());
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

            lock (_loadLock)
            {
                if (_loaded) return;

                RssCache loaded;
                try
                {
                    InitializeDatabase();
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
                    folder.Name ??= (_loader.GetString("TextFolder") ?? "Folder");
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
                command.CommandText = "SELECT id, title, url, folder_id, image_url, local_image_path, last_fetched_ticks FROM rss_subscriptions ORDER BY title;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    cache.Subscriptions.Add(new RssSubscription
                    {
                        Id = reader.GetString(0),
                        Title = reader.GetString(1),
                        Url = reader.GetString(2),
                        FolderId = reader.GetString(3),
                        ImageUrl = reader.GetString(4),
                        LocalImagePath = reader.GetString(5),
                        LastFetchedAt = FromTicks(reader.GetInt64(6))
                    });
                }
            }

            using (var command = connection.CreateCommand())
            {
                // Listing only — html_content is hydrated on demand via GetArticleHtml
                // to keep startup memory in check (1000 articles × ~50 KB html = 50 MB).
                command.CommandText = """
SELECT id, subscription_id, feed_title, title, link, summary, image_url, local_image_path, published_ticks
FROM rss_articles
ORDER BY published_ticks DESC
LIMIT 1000;
""";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    cache.Articles.Add(ReadArticleListItem(reader));
            }

            return cache;
        }

        private List<RssArticle> QueryCachedArticlesPage(string? subscriptionId, string? folderId, int skip, int take)
        {
            var hasSubscription = !string.IsNullOrWhiteSpace(subscriptionId);
            var hasFolder = folderId != null;

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
            command.Parameters.AddWithValue("$take", take);
            command.Parameters.AddWithValue("$skip", skip);

            var articles = new List<RssArticle>(take);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                articles.Add(ReadArticleListItem(reader));

            return articles;
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
                InitializeDatabase();
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT html_content FROM rss_articles WHERE id = $id LIMIT 1;";
                command.Parameters.AddWithValue("$id", articleId);
                var value = command.ExecuteScalar();
                return value as string ?? "";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetArticleHtml failed for {articleId}: {ex.Message}");
                return "";
            }
        }

        // Listing rows: column order matches the abbreviated SELECT above (no html_content).
        private static RssArticle ReadArticleListItem(SqliteDataReader reader)
            => new()
            {
                Id = reader.GetString(0),
                SubscriptionId = reader.GetString(1),
                FeedTitle = reader.GetString(2),
                Title = reader.GetString(3),
                Link = reader.GetString(4),
                Summary = reader.GetString(5),
                HtmlContent = "",
                ImageUrl = reader.GetString(6),
                LocalImagePath = reader.GetString(7),
                PublishedAt = FromTicks(reader.GetInt64(8))
            };

        private void SaveToDatabase()
        {
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();

            foreach (var folder in _cache.Folders)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
INSERT OR REPLACE INTO rss_folders(id, name, sort_order)
VALUES ($id, $name, $sortOrder);
""";
                command.Parameters.AddWithValue("$id", folder.Id ?? "");
                command.Parameters.AddWithValue("$name", folder.Name ?? "");
                command.Parameters.AddWithValue("$sortOrder", folder.SortOrder);
                command.ExecuteNonQuery();
            }

            var currentFolderIds = _cache.Folders.Select(f => f.Id ?? "").ToHashSet(StringComparer.Ordinal);
            DeleteRowsNotIn(connection, transaction, "rss_folders", currentFolderIds);

            foreach (var subscription in _cache.Subscriptions)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
INSERT OR REPLACE INTO rss_subscriptions(id, title, url, folder_id, image_url, local_image_path, last_fetched_ticks)
VALUES ($id, $title, $url, $folderId, $imageUrl, $localImagePath, $lastFetchedTicks);
""";
                command.Parameters.AddWithValue("$id", subscription.Id ?? "");
                command.Parameters.AddWithValue("$title", subscription.Title ?? "");
                command.Parameters.AddWithValue("$url", subscription.Url ?? "");
                command.Parameters.AddWithValue("$folderId", subscription.FolderId ?? "");
                command.Parameters.AddWithValue("$imageUrl", subscription.ImageUrl ?? "");
                command.Parameters.AddWithValue("$localImagePath", subscription.LocalImagePath ?? "");
                command.Parameters.AddWithValue("$lastFetchedTicks", ToTicks(subscription.LastFetchedAt));
                command.ExecuteNonQuery();
            }

            var currentSubIds = _cache.Subscriptions.Select(s => s.Id ?? "").ToHashSet(StringComparer.Ordinal);
            DeleteRowsNotIn(connection, transaction, "rss_subscriptions", currentSubIds);

            var keptArticleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var article in _cache.Articles.OrderByDescending(item => item.PublishedAt).Take(1000))
            {
                keptArticleIds.Add(article.Id ?? "");
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
INSERT OR REPLACE INTO rss_articles(id, subscription_id, feed_title, title, link, summary, html_content, image_url, local_image_path, published_ticks)
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

            DeleteRowsNotIn(connection, transaction, "rss_articles", keptArticleIds);

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
                subscription.ImageUrl ??= "";
                subscription.LocalImagePath ??= "";
            }

            foreach (var folder in _cache.Folders)
            {
                folder.Id ??= Guid.NewGuid().ToString("N");
                folder.Name ??= (_loader.GetString("TextFolder") ?? "Folder");
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
            _connectionString ??= new SqliteConnectionStringBuilder
            {
                DataSource = GetDatabasePath()
            }.ToString();
            var connection = new SqliteConnection(_connectionString);
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

        private static void DeleteRowsNotIn(
            SqliteConnection connection,
            SqliteTransaction transaction,
            string tableName,
            IReadOnlyCollection<string> ids)
        {
            if (ids.Count == 0)
            {
                ExecuteNonQuery(connection, $"DELETE FROM {tableName};", transaction);
                return;
            }

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            var parameterNames = ids
                .Select((id, index) =>
                {
                    var name = $"$id{index}";
                    command.Parameters.AddWithValue(name, id);
                    return name;
                })
                .ToArray();

            command.CommandText = $"DELETE FROM {tableName} WHERE id NOT IN ({string.Join(",", parameterNames)});";
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
