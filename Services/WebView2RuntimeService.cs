using System;
using Microsoft.Web.WebView2.Core;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Task_Flyout.Services
{
    internal static class WebView2RuntimeService
    {
        private static bool _configured;

        public static void ConfigureSharedRuntime()
        {
            if (_configured) return;

            var cachePath = AppDataPathHelper.EnsureDirectory(AppDataPathHelper.ResolveLocal("WebView2Cache"));
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", cachePath, EnvironmentVariableTarget.Process);
            _configured = true;
        }

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
    }
}
