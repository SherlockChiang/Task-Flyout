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
}
