using System;

namespace Task_Flyout.Services
{
    internal static class WebResourcePolicy
    {
        public static bool IsAllowedEmbeddedResource(string? uriText, bool allowInsecureHttp)
        {
            if (string.IsNullOrWhiteSpace(uriText))
                return false;

            if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri))
                return false;

            if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
                uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
            {
                if (NetworkSafety.IsUnsafeHost(uri))
                    return false;

                return uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) || allowInsecureHttp;
            }

            if (uri.Scheme.Equals("about", StringComparison.OrdinalIgnoreCase))
                return true;

            return uri.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase) &&
                   (uriText.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase) ||
                    uriText.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsAllowedRssNonRemoteResource(string? uriText)
        {
            if (string.IsNullOrWhiteSpace(uriText)) return false;
            if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri)) return false;

            if (uri.Scheme.Equals("about", StringComparison.OrdinalIgnoreCase))
                return true;

            if (uri.Scheme.Equals("data", StringComparison.OrdinalIgnoreCase))
                return uriText.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase) ||
                       uriText.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);

            return false;
        }

        public static bool IsAllowedRssRemoteResource(string? uriText)
        {
            if (string.IsNullOrWhiteSpace(uriText)) return false;
            if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri)) return false;
            if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return false;

            return !NetworkSafety.IsUnsafeHost(uri);
        }
    }
}
