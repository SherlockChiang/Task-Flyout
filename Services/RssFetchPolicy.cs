using System;

namespace Task_Flyout.Services
{
    internal static class RssFetchPolicy
    {
        public static bool IsAllowedFetchScheme(Uri uri)
            => uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

        public static bool TryResolveHttpRedirect(Uri currentUri, Uri? location, out Uri redirectUri)
        {
            redirectUri = currentUri;
            if (location == null)
                return false;

            redirectUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
            return IsAllowedFetchScheme(redirectUri);
        }
    }
}
