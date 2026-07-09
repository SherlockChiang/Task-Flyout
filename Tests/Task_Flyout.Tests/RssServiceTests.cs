using System.Xml;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class RssServiceTests
{
    [Fact]
    public void Safe_xml_reader_settings_disable_external_resolution()
    {
        var settings = RssXmlSecurity.CreateReaderSettings(1024);

        Assert.Equal(DtdProcessing.Prohibit, settings.DtdProcessing);
        Assert.Equal(0, settings.MaxCharactersFromEntities);
        Assert.True(settings.MaxCharactersInDocument > 0);
    }

    [Fact]
    public void Safe_xml_reader_rejects_dtd()
    {
        var settings = RssXmlSecurity.CreateReaderSettings(1024);
        const string xml = "<!DOCTYPE rss [<!ENTITY xxe SYSTEM 'file:///c:/windows/win.ini'>]><rss><channel><title>&xxe;</title></channel></rss>";

        using var reader = XmlReader.Create(new StringReader(xml), settings);

        Assert.Throws<XmlException>(() =>
        {
            while (reader.Read()) { }
        });
    }

    [Fact]
    public void Feed_xml_loader_returns_false_for_malformed_xml()
    {
        const string xml = "<rss><channel><title>Broken</title><item></channel></rss>";

        Assert.False(RssFeedXml.TryLoadDocument(xml, 4096, out var document));
        Assert.Null(document);
    }

    [Fact]
    public void Feed_xml_loader_accepts_well_formed_xml()
    {
        const string xml = "<rss><channel><title>Ok</title></channel></rss>";

        Assert.True(RssFeedXml.TryLoadDocument(xml, 4096, out var document));
        Assert.NotNull(document);
        Assert.Equal("rss", document!.Root!.Name.LocalName);
    }

    [Theory]
    [InlineData("https://example.com/feed.xml", true)]
    [InlineData("http://example.com/feed.xml", true)]
    [InlineData("file:///c:/windows/win.ini", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/xml,<rss />", false)]
    public void Feed_fetch_scheme_allows_only_http_and_https(string uriText, bool expected)
    {
        Assert.Equal(expected, RssFetchPolicy.IsAllowedFetchScheme(new Uri(uriText)));
    }

    [Theory]
    [InlineData("https://example.com/feed.xml", "/next.xml", "https://example.com/next.xml")]
    [InlineData("https://example.com/folder/feed.xml", "../next.xml", "https://example.com/next.xml")]
    [InlineData("https://example.com/feed.xml", "http://cdn.example.com/next.xml", "http://cdn.example.com/next.xml")]
    public void Redirect_policy_resolves_http_relative_and_absolute_locations(string current, string location, string expected)
    {
        Assert.True(RssFetchPolicy.TryResolveHttpRedirect(new Uri(current), new Uri(location, UriKind.RelativeOrAbsolute), out var redirect));
        Assert.Equal(expected, redirect.AbsoluteUri);
    }

    [Theory]
    [InlineData("file:///c:/windows/win.ini")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/xml,<rss />")]
    public void Redirect_policy_blocks_non_http_locations(string location)
    {
        Assert.False(RssFetchPolicy.TryResolveHttpRedirect(new Uri("https://example.com/feed.xml"), new Uri(location, UriKind.RelativeOrAbsolute), out _));
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public void Redirect_policy_identifies_redirect_status_codes(int statusCode)
    {
        Assert.True(RssFetchPolicy.IsRedirectStatus((System.Net.HttpStatusCode)statusCode));
    }

    [Fact]
    public void Redirect_policy_stops_at_max_redirect_hops()
    {
        Assert.True(RssFetchPolicy.ShouldFollowRedirect(System.Net.HttpStatusCode.Found, hop: 4, maxRedirects: 5));
        Assert.False(RssFetchPolicy.ShouldFollowRedirect(System.Net.HttpStatusCode.Found, hop: 5, maxRedirects: 5));
        Assert.False(RssFetchPolicy.ShouldFollowRedirect(System.Net.HttpStatusCode.OK, hop: 0, maxRedirects: 5));
    }

    [Fact]
    public void Dns_policy_allows_only_non_empty_public_address_sets()
    {
        Assert.True(RssFetchPolicy.AreResolvedAddressesSafe(new[]
        {
            System.Net.IPAddress.Parse("8.8.8.8"),
            System.Net.IPAddress.Parse("2606:4700:4700::1111")
        }));

        Assert.False(RssFetchPolicy.AreResolvedAddressesSafe(Array.Empty<System.Net.IPAddress>()));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.5")]
    [InlineData("192.168.1.5")]
    [InlineData("169.254.1.5")]
    [InlineData("fc00::1")]
    public void Dns_policy_rejects_any_private_or_special_address(string unsafeAddress)
    {
        Assert.False(RssFetchPolicy.AreResolvedAddressesSafe(new[]
        {
            System.Net.IPAddress.Parse("8.8.8.8"),
            System.Net.IPAddress.Parse(unsafeAddress)
        }));
    }
}
