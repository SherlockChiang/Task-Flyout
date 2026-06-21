using System;
using System.Net;
using System.Net.Sockets;

namespace Task_Flyout.Services
{
    /// <summary>
    /// Shared SSRF guard: classifies IP addresses / hosts as public vs. private or
    /// special-use (loopback, RFC1918, link-local, CGNAT, ULA, documentation/test ranges,
    /// multicast, …). Used by the RSS fetcher's DNS-pinned connect and the WebView2 RSS
    /// remote-resource gate so both share one audited, unit-tested implementation.
    /// </summary>
    internal static class NetworkSafety
    {
        public static IPAddress Normalize(IPAddress address)
            => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        /// <summary>True only for routable public addresses; everything special-use is false.</summary>
        public static bool IsPublicIpAddress(IPAddress address)
        {
            address = Normalize(address);

            if (IPAddress.IsLoopback(address) ||
                address.Equals(IPAddress.Any) ||
                address.Equals(IPAddress.Broadcast) ||
                address.Equals(IPAddress.None) ||
                address.Equals(IPAddress.IPv6Any) ||
                address.Equals(IPAddress.IPv6None))
                return false;

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                var b = address.GetAddressBytes();
                return b[0] switch
                {
                    0 => false,
                    10 => false,
                    127 => false,
                    100 when b[1] >= 64 && b[1] <= 127 => false,   // 100.64/10 CGNAT
                    169 when b[1] == 254 => false,                 // link-local
                    172 when b[1] >= 16 && b[1] <= 31 => false,    // 172.16/12
                    192 when b[1] == 0 => false,                   // 192.0.0/24 + 192.0.2/24 (TEST-NET-1)
                    192 when b[1] == 88 && b[2] == 99 => false,    // 6to4 relay anycast
                    192 when b[1] == 168 => false,                 // 192.168/16
                    198 when b[1] == 18 || b[1] == 19 => false,    // 198.18/15 benchmarking
                    198 when b[1] == 51 && b[2] == 100 => false,   // TEST-NET-2
                    203 when b[1] == 0 && b[2] == 113 => false,    // TEST-NET-3
                    >= 224 => false,                               // multicast / reserved
                    _ => true
                };
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                var b = address.GetAddressBytes();
                if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
                    return false;
                if ((b[0] & 0xFE) == 0xFC) return false;           // fc00::/7 ULA
                if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8) return false; // 2001:db8::/32 doc
                return true;
            }

            return false; // unknown address family — deny by default
        }

        /// <summary>
        /// True when the host is obviously private/special: localhost, .local / .localhost,
        /// or a literal private/special-use IP. Domain names are NOT resolved here — callers
        /// that need DNS-time pinning (the RSS fetcher) do that separately.
        /// </summary>
        public static bool IsUnsafeHost(Uri uri)
        {
            var host = uri.Host.Trim('[', ']');
            if (string.IsNullOrWhiteSpace(host)) return true;
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
            if (host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)) return true;
            if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return true;

            return IPAddress.TryParse(host, out var address) && !IsPublicIpAddress(address);
        }
    }
}
