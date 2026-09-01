using System.Text;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class ICloudCalDavPolicyTests
{
    [Theory]
    [InlineData("https://caldav.icloud.com/")]
    [InlineData("https://p123-caldav.icloud.com/123/calendars/home/")]
    [InlineData("https://caldav.icloud.com:443/")]
    public void Allows_only_Apple_CalDav_endpoints(string value)
        => Assert.True(ICloudCalDavPolicy.IsAllowedEndpoint(new Uri(value)));

    [Theory]
    [InlineData("http://caldav.icloud.com/")]
    [InlineData("https://caldav.icloud.com.evil.example/")]
    [InlineData("https://user@caldav.icloud.com/")]
    [InlineData("https://caldav.icloud.com:8443/")]
    [InlineData("https://caldav.icloud.com/#fragment")]
    [InlineData("https://evil.p01-caldav.icloud.com/")]
    [InlineData("https://127.0.0.1/")]
    public void Rejects_non_Apple_or_unsafe_endpoints(string value)
        => Assert.False(ICloudCalDavPolicy.IsAllowedEndpoint(new Uri(value)));

    [Fact]
    public void Resolves_relative_hrefs_without_leaving_the_Apple_host()
    {
        var result = ICloudCalDavPolicy.ResolveEndpoint(
            new Uri("https://p01-caldav.icloud.com/123/principal/"),
            "/123/calendars/home/");

        Assert.Equal("https://p01-caldav.icloud.com/123/calendars/home/", result.AbsoluteUri);
        Assert.Throws<InvalidDataException>(() => ICloudCalDavPolicy.ResolveEndpoint(result, "https://example.com/calendar/"));
    }

    [Fact]
    public void Parses_event_calendars_colors_and_write_privileges()
    {
        const string xml = """
<?xml version="1.0" encoding="utf-8"?>
<D:multistatus xmlns:D="DAV:" xmlns:C="urn:ietf:params:xml:ns:caldav" xmlns:A="http://apple.com/ns/ical/">
  <D:response>
    <D:href>/123/calendars/work/</D:href>
    <D:propstat><D:prop>
      <D:resourcetype><D:collection/><C:calendar/></D:resourcetype>
      <D:displayname>Work</D:displayname>
      <A:calendar-color>#aabbccff</A:calendar-color>
      <C:supported-calendar-component-set><C:comp name="VEVENT"/></C:supported-calendar-component-set>
      <D:current-user-privilege-set><D:privilege><D:write-content/></D:privilege></D:current-user-privilege-set>
    </D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat>
  </D:response>
  <D:response>
    <D:href>/123/calendars/reminders/</D:href>
    <D:propstat><D:prop>
      <D:resourcetype><D:collection/><C:calendar/></D:resourcetype>
      <D:displayname>Reminders</D:displayname>
      <C:supported-calendar-component-set><C:comp name="VTODO"/></C:supported-calendar-component-set>
    </D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat>
  </D:response>
</D:multistatus>
""";

        var calendars = ICloudCalDavPolicy.ParseCalendars(xml, new Uri("https://p01-caldav.icloud.com/"));

        var calendar = Assert.Single(calendars);
        Assert.Equal("Work", calendar.Name);
        Assert.Equal("#AABBCC", calendar.ColorHex);
        Assert.True(calendar.IsWritable);
    }

    [Fact]
    public void Parses_principal_and_calendar_home_discovery_hrefs()
    {
        const string principalXml = """
<D:multistatus xmlns:D="DAV:"><D:response><D:propstat><D:prop>
  <D:current-user-principal><D:href>/123/principal/</D:href></D:current-user-principal>
</D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat></D:response></D:multistatus>
""";
        const string homeXml = """
<D:multistatus xmlns:D="DAV:" xmlns:C="urn:ietf:params:xml:ns:caldav"><D:response><D:propstat><D:prop>
  <C:calendar-home-set><D:href>/123/calendars/</D:href></C:calendar-home-set>
</D:prop><D:status>HTTP/1.1 200 OK</D:status></D:propstat></D:response></D:multistatus>
""";
        var shard = new Uri("https://p01-caldav.icloud.com/");

        Uri principal = ICloudCalDavPolicy.ParseCurrentUserPrincipal(principalXml, shard);
        Uri home = ICloudCalDavPolicy.ParseCalendarHome(homeXml, shard);

        Assert.Equal("https://p01-caldav.icloud.com/123/principal/", principal.AbsoluteUri);
        Assert.Equal("https://p01-caldav.icloud.com/123/calendars/", home.AbsoluteUri);
    }

    [Fact]
    public void Rejects_DTDs_in_CalDav_XML()
    {
        const string xml = "<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///etc/passwd'>]><D:multistatus xmlns:D='DAV:'>&e;</D:multistatus>";

        Assert.Throws<System.Xml.XmlException>(() =>
            ICloudCalDavPolicy.ParseCalendars(xml, new Uri("https://caldav.icloud.com/")));
    }

    [Fact]
    public void Event_reference_round_trips_and_rejects_external_hosts()
    {
        var expected = new ICloudEventReference(
            new Uri("https://p01-caldav.icloud.com/123/calendars/work/event.ics"),
            "event-uid",
            true,
            false,
            new DateTime(2026, 8, 4, 9, 30, 0),
            "Asia/Shanghai");

        string encoded = ICloudEventReferenceCodec.Encode(expected);

        Assert.True(ICloudEventReferenceCodec.TryDecode(encoded, out var actual));
        Assert.Equal(expected, actual);

        string hostileUri = Base64Url("https://example.com/event.ics");
        string[] parts = encoded["icloud:".Length..].Split('|');
        parts[0] = hostileUri;
        Assert.False(ICloudEventReferenceCodec.TryDecode("icloud:" + string.Join('|', parts), out _));
    }

    [Fact]
    public void Event_reference_rejects_identifiers_that_cannot_round_trip()
    {
        var reference = new ICloudEventReference(
            new Uri("https://p01-caldav.icloud.com/123/calendars/work/event.ics"),
            new string('u', ICloudEventReferenceCodec.MaxUidLength + 1),
            true,
            false,
            new DateTime(2026, 8, 4, 9, 30, 0),
            "Asia/Shanghai");

        Assert.Throws<ArgumentException>(() => ICloudEventReferenceCodec.Encode(reference));
    }

    private static string Base64Url(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
