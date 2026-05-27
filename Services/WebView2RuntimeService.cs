using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Task_Flyout.Services
{
    internal static class WebView2RuntimeService
    {
        private const long MaxCacheBytes = 300L * 1024 * 1024;
        private const long TargetCacheBytes = 220L * 1024 * 1024;
        private static bool _configured;

        public static string CachePath =>
            AppDataPathHelper.EnsureDirectory(AppDataPathHelper.ResolveLocal("WebView2Cache"));

        public static void ConfigureSharedRuntime()
        {
            if (_configured) return;

            var cachePath = CachePath;
            PruneCacheIfNeeded(cachePath);
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", cachePath, EnvironmentVariableTarget.Process);
            _configured = true;
        }

        public static Task<long> GetCacheSizeBytesAsync()
            => Task.Run(() => GetDirectorySize(CachePath));

        public static Task<long> ClearCacheAsync()
            => Task.Run(() =>
            {
                var path = CachePath;
                var before = GetDirectorySize(path);
                DeleteDirectoryChildren(path);
                var after = GetDirectorySize(path);
                return Math.Max(0, before - after);
            });

        public static void BlockUnsafeEmbeddedResource(CoreWebView2 coreWebView, CoreWebView2WebResourceRequestedEventArgs args)
        {
            if (IsAllowedEmbeddedResource(args.Request?.Uri))
                return;

            args.Response = coreWebView.Environment.CreateWebResourceResponse(
                new InMemoryRandomAccessStream(),
                403,
                "Blocked",
                "Content-Type: text/plain");
        }

        public static void BlockUnsafeRssEmbeddedResource(CoreWebView2 coreWebView, CoreWebView2WebResourceRequestedEventArgs args)
        {
            if (IsAllowedRssEmbeddedResource(args.Request?.Uri))
                return;

            args.Response = coreWebView.Environment.CreateWebResourceResponse(
                new InMemoryRandomAccessStream(),
                403,
                "Blocked",
                "Content-Type: text/plain");
        }

        public static bool IsAllowedEmbeddedResource(string? uriText)
        {
            if (string.IsNullOrWhiteSpace(uriText))
                return false;

            if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
                return false;

            if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals("about", StringComparison.OrdinalIgnoreCase))
                return true;

            if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
                return ApplicationData.Current.LocalSettings.Values["AllowInsecureWebViewResources"] as bool? ?? false;

            return uri.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase) &&
                   (uriText.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase) ||
                    uriText.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsAllowedRssEmbeddedResource(string? uriText)
        {
            if (ApplicationData.Current.LocalSettings.Values["AllowRssRemoteResources"] as bool? ?? false)
                return IsAllowedEmbeddedResource(uriText);

            if (string.IsNullOrWhiteSpace(uriText))
                return false;

            if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
                return false;

            return uri.Scheme.Equals("about", StringComparison.OrdinalIgnoreCase) ||
                   (uri.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase) &&
                    (uriText.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase) ||
                     uriText.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)));
        }

        private static void PruneCacheIfNeeded(string path)
        {
            try
            {
                var size = GetDirectorySize(path);
                if (size <= MaxCacheBytes) return;

                foreach (var file in EnumerateFiles(path)
                             .OrderBy(file => file.LastWriteTimeUtc))
                {
                    TryDeleteFile(file.FullName);
                    size -= Math.Max(0, file.Length);
                    if (size <= TargetCacheBytes) break;
                }

                DeleteEmptyDirectories(path);
            }
            catch
            {
            }
        }

        private static long GetDirectorySize(string path)
            => EnumerateFiles(path).Sum(file => SafeLength(file));

        private static FileInfo[] EnumerateFiles(string path)
        {
            try
            {
                if (!Directory.Exists(path)) return Array.Empty<FileInfo>();
                return new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).ToArray();
            }
            catch
            {
                return Array.Empty<FileInfo>();
            }
        }

        private static long SafeLength(FileInfo file)
        {
            try { return file.Length; }
            catch { return 0; }
        }

        private static void DeleteDirectoryChildren(string path)
        {
            if (!Directory.Exists(path)) return;

            foreach (var file in EnumerateFiles(path))
                TryDeleteFile(file.FullName);

            DeleteEmptyDirectories(path);
        }

        private static void DeleteEmptyDirectories(string path)
        {
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                             .OrderByDescending(dir => dir.Length))
                {
                    try
                    {
                        if (!Directory.EnumerateFileSystemEntries(dir).Any())
                            Directory.Delete(dir);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private static void TryDeleteFile(string path)
        {
            try { File.Delete(path); }
            catch { }
        }
    }
}
