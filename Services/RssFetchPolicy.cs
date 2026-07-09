using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Task_Flyout.Services
{
    internal static class RssFetchPolicy
    {
        public static bool IsAllowedFetchScheme(Uri uri)
            => uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

        public static bool IsRedirectStatus(HttpStatusCode code)
            => code is HttpStatusCode.Moved or HttpStatusCode.Found or HttpStatusCode.SeeOther
                or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect
                or (HttpStatusCode)308;

        public static bool ShouldFollowRedirect(HttpStatusCode code, int hop, int maxRedirects)
            => IsRedirectStatus(code) && hop < maxRedirects;

        public static bool AreResolvedAddressesSafe(IEnumerable<IPAddress> addresses)
        {
            var normalized = addresses.Select(NetworkSafety.Normalize).ToList();
            return normalized.Count > 0 && normalized.All(NetworkSafety.IsPublicIpAddress);
        }

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
