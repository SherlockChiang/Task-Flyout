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
}
