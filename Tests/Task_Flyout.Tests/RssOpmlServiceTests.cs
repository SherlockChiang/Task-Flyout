using System.Xml;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class RssOpmlServiceTests
{
    [Fact]
    public void Export_and_parse_round_trip_folders_and_feeds()
    {
        var entries = new[]
        {
            new RssOpmlEntry("News", "https://example.test/news.xml", "Reading"),
            new RssOpmlEntry("Updates", "https://example.test/updates.xml", ""),
            new RssOpmlEntry("HTTP", "http://example.test/feed", "Reading")
        };

        var parsed = RssOpmlService.Parse(RssOpmlService.Export(entries));

        Assert.Equal(3, parsed.Count);
        Assert.Contains(parsed, entry => entry.Title == "News" && entry.FolderName == "Reading");
        Assert.Contains(parsed, entry => entry.Title == "Updates" && entry.FolderName == "");
    }

    [Fact]
    public void Preview_excludes_existing_and_import_duplicates()
    {
        var preview = RssOpmlService.Preview(new[]
        {
            new RssOpmlEntry("Existing", "https://example.test/existing", ""),
            new RssOpmlEntry("New", "https://example.test/new", "Folder"),
            new RssOpmlEntry("Duplicate", "https://example.test/new/", "Folder"),
            new RssOpmlEntry("HTTP", "http://example.test/feed", "")
        }, new[] { "https://example.test/existing/" });

        Assert.Equal(2, preview.NewEntries.Count);
        Assert.Equal(2, preview.DuplicateCount);
        Assert.Equal(1, preview.InsecureHttpCount);
        Assert.Equal(1, preview.FolderCount);
    }

    [Fact]
    public void Parser_rejects_dtd_and_external_entities()
    {
        const string xml = "<!DOCTYPE opml [<!ENTITY xxe SYSTEM 'file:///c:/windows/win.ini'>]><opml><body><outline xmlUrl='https://example.test/feed' text='&xxe;'/></body></opml>";

        Assert.Throws<XmlException>(() => RssOpmlService.Parse(xml));
    }

    [Fact]
    public void Parser_ignores_non_http_feed_schemes()
    {
        const string xml = "<opml><body><outline xmlUrl='file:///c:/private.xml'/><outline xmlUrl='javascript:alert(1)'/></body></opml>";

        Assert.Empty(RssOpmlService.Parse(xml));
    }

    [Fact]
    public void Parser_rejects_non_opml_documents()
    {
        Assert.Throws<XmlException>(() => RssOpmlService.Parse("<rss />"));
    }

    [Fact]
    public void Parser_limits_subscription_count()
    {
        var outlines = string.Concat(Enumerable.Range(0, RssOpmlService.MaximumEntries + 1)
            .Select(index => $"<outline xmlUrl='https://example.test/{index}'/>"));

        Assert.Throws<InvalidOperationException>(() => RssOpmlService.Parse($"<opml><body>{outlines}</body></opml>"));
    }
}
