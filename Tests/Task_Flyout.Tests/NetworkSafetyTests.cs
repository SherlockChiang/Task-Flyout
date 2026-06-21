using System;
using System.Net;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class NetworkSafetyTests
{
    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")] // example.com
    [InlineData("2606:4700:4700::1111")] // Cloudflare DNS
    public void IsPublicIpAddress_true_for_public(string ip)
    {
        Assert.True(NetworkSafety.IsPublicIpAddress(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("127.0.0.1")]    // loopback
    [InlineData("10.0.0.5")]     // RFC1918
    [InlineData("172.16.0.1")]   // RFC1918
    [InlineData("172.31.255.1")] // RFC1918 upper
    [InlineData("192.168.1.1")]  // RFC1918
    [InlineData("169.254.1.1")]  // link-local
    [InlineData("100.64.0.1")]   // CGNAT
    [InlineData("0.0.0.0")]      // unspecified
    [InlineData("255.255.255.255")] // broadcast
    [InlineData("224.0.0.1")]    // multicast
    [InlineData("192.0.2.5")]    // TEST-NET-1
    [InlineData("198.51.100.7")] // TEST-NET-2
    [InlineData("203.0.113.9")]  // TEST-NET-3
    [InlineData("198.18.0.1")]   // benchmarking
    [InlineData("::1")]          // IPv6 loopback
    [InlineData("fe80::1")]      // IPv6 link-local
    [InlineData("fc00::1")]      // IPv6 ULA
    [InlineData("2001:db8::1")]  // IPv6 documentation
    [InlineData("ff02::1")]      // IPv6 multicast
    public void IsPublicIpAddress_false_for_special_use(string ip)
    {
        Assert.False(NetworkSafety.IsPublicIpAddress(IPAddress.Parse(ip)));
    }

    [Fact]
    public void IsPublicIpAddress_false_for_ipv4_mapped_private()
    {
        // ::ffff:192.168.0.1 must be unwrapped and treated as private.
        Assert.False(NetworkSafety.IsPublicIpAddress(IPAddress.Parse("::ffff:192.168.0.1")));
    }

    [Theory]
    [InlineData("https://localhost/x")]
    [InlineData("https://app.localhost/x")]
    [InlineData("https://printer.local/x")]
    [InlineData("https://127.0.0.1/x")]
    [InlineData("https://10.0.0.1/x")]
    [InlineData("https://[::1]/x")]
    [InlineData("https://[fc00::1]/x")]
    public void IsUnsafeHost_true_for_private_hosts(string url)
    {
        Assert.True(NetworkSafety.IsUnsafeHost(new Uri(url)));
    }

    [Theory]
    [InlineData("https://example.com/x")]
    [InlineData("https://8.8.8.8/x")]      // public literal IP
    [InlineData("https://cdn.example.org/img.png")]
    public void IsUnsafeHost_false_for_public_hosts(string url)
    {
        Assert.False(NetworkSafety.IsUnsafeHost(new Uri(url)));
    }

    [Fact]
    public void IsUnsafeHost_does_not_resolve_dns_names()
    {
        // A domain that may resolve to a private IP still passes the host check —
        // DNS-time pinning is the fetcher's job, documented as a known limitation.
        Assert.False(NetworkSafety.IsUnsafeHost(new Uri("https://intranet.example.com/x")));
    }
}
