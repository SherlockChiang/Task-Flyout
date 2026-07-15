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

        public static bool CanFetchFeed(Uri uri, bool allowInsecureHttp)
            => uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
               (allowInsecureHttp && uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase));

        public static string GetLocalNetworkAuthority(Uri uri)
        {
            var host = uri.HostNameType == UriHostNameType.IPv6 ? $"[{uri.Host}]" : uri.IdnHost;
            var defaultPort = uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80;
            var portSuffix = uri.Port == defaultPort ? "" : $":{uri.Port}";
            return $"{uri.Scheme.ToLowerInvariant()}://{host.ToLowerInvariant()}{portSuffix}";
        }

        public static bool HasLocalNetworkApproval(Uri uri, string? approvedAuthority)
            => !string.IsNullOrWhiteSpace(approvedAuthority) &&
               string.Equals(GetLocalNetworkAuthority(uri), approvedAuthority, StringComparison.OrdinalIgnoreCase);

        public static bool HasLocalNetworkEndpointApproval(string host, int port, string? approvedAuthority)
        {
            if (string.IsNullOrWhiteSpace(approvedAuthority) ||
                !Uri.TryCreate(approvedAuthority, UriKind.Absolute, out var approvedUri)) return false;

            return string.Equals(NormalizeHost(host), NormalizeHost(approvedUri.Host), StringComparison.OrdinalIgnoreCase) &&
                   port == approvedUri.Port;
        }

        private static string NormalizeHost(string host)
        {
            host = host.Trim('[', ']');
            return IPAddress.TryParse(host, out var address)
                ? NetworkSafety.Normalize(address).ToString()
                : host;
        }

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
            if (!IsAllowedFetchScheme(redirectUri)) return false;
            return !currentUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                   !redirectUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
        }
    }
}
