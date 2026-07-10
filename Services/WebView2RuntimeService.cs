using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
            // Prune off the UI thread — at the 300 MB cap a sync sweep across ~10k
            // entries was blocking first-open of mail / RSS pages noticeably.
            _ = Task.Run(() => PruneCacheIfNeeded(cachePath));
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

        public static bool IsAllowedEmbeddedResource(string? uriText)
            => WebResourcePolicy.IsAllowedEmbeddedResource(
                uriText,
                ApplicationData.Current.LocalSettings.Values["AllowInsecureWebViewResources"] as bool? ?? false);

        // about:/data: RSS resources need no network and pass through unchanged.
        public static bool IsAllowedRssNonRemoteResource(string? uriText)
            => WebResourcePolicy.IsAllowedRssNonRemoteResource(uriText);

        // A remote https resource the user has enabled, whose host isn't obviously private.
        // The caller must fetch it through the app's IP-pinned HTTP client rather than letting
        // the WebView connect directly (the host-string check alone can't stop DNS rebinding).
        public static bool ShouldProxyRssRemoteResource(string? uriText)
        {
            if (string.IsNullOrWhiteSpace(uriText)) return false;
            if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri)) return false;
            if (!(ApplicationData.Current.LocalSettings.Values["AllowRssRemoteResources"] as bool? ?? false))
                return false;

            return WebResourcePolicy.IsAllowedRssRemoteResource(uriText);
        }

        public static bool IsAllowedMailNonRemoteResource(string? uriText)
            => WebResourcePolicy.IsAllowedMailNonRemoteResource(uriText);

        public static bool ShouldProxyMailRemoteImage(string? uriText)
            => WebResourcePolicy.ShouldProxyMailRemoteImage(uriText);

        private static void PruneCacheIfNeeded(string path)
        {
            try
            {
                // Two passes: one to measure (stream), one to delete in age order
                // (only realised when we actually need to prune). The naive
                // single-pass alloc of all FileInfos cost > 10 MB at the cap.
                long size = 0;
                foreach (var file in EnumerateFiles(path))
                    size += SafeLength(file);

                if (size <= MaxCacheBytes) return;

                var files = EnumerateFiles(path).ToList(); // only realised when we actually need to delete
                var deletePaths = CachePrunePolicy.SelectOldestUntilTarget(
                    files.Select(file => new CachePruneEntry(file.FullName, SafeLength(file), SafeLastWriteUtc(file))),
                    MaxCacheBytes,
                    TargetCacheBytes);

                foreach (var filePath in deletePaths)
                {
                    TryDeleteFile(filePath);
                }

                DeleteEmptyDirectories(path);
            }
            catch
            {
            }
        }

        private static long GetDirectorySize(string path)
        {
            long total = 0;
            foreach (var file in EnumerateFiles(path))
                total += SafeLength(file);
            return total;
        }

        private static IEnumerable<FileInfo> EnumerateFiles(string path)
        {
            if (!Directory.Exists(path)) yield break;

            IEnumerable<FileInfo> source;
            try
            {
                source = new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories);
            }
            catch
            {
                yield break;
            }

            foreach (var file in source)
                yield return file;
        }

        private static long SafeLength(FileInfo file)
        {
            try { return file.Length; }
            catch { return 0; }
        }

        private static DateTime SafeLastWriteUtc(FileInfo file)
        {
            try { return file.LastWriteTimeUtc; }
            catch { return DateTime.MinValue; }
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
