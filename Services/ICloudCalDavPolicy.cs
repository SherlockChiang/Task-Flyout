using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Task_Flyout.Services
{
    internal sealed record ICloudCalendarDescriptor(Uri Uri, string Name, string ColorHex, bool IsWritable);
    internal sealed record ICloudCalendarResource(Uri Uri, string ETag, string CalendarData);
    internal sealed record ICloudEventReference(
        Uri ResourceUri,
        string Uid,
        bool IsRecurring,
        bool IsAllDay,
        DateTime OccurrenceStart,
        string TimeZoneId);

    internal static class ICloudCalDavPolicy
    {
        internal const int MaxEndpointLength = 4096;
        internal const int MaxXmlCharacters = 16 * 1024 * 1024;
        internal const int MaxCalendarCount = 100;
        internal const int MaxResourceCount = 5000;

        private static readonly XNamespace Dav = "DAV:";
        private static readonly XNamespace CalDav = "urn:ietf:params:xml:ns:caldav";
        private static readonly XNamespace AppleIcal = "http://apple.com/ns/ical/";

        public static bool IsAllowedEndpoint(Uri? uri)
        {
            if (uri == null || !uri.IsAbsoluteUri || uri.AbsoluteUri.Length > MaxEndpointLength)
                return false;
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Fragment)
                || (!uri.IsDefaultPort && uri.Port != 443))
                return false;

            string host = uri.DnsSafeHost.TrimEnd('.');
            if (string.Equals(host, "caldav.icloud.com", StringComparison.OrdinalIgnoreCase))
                return true;

            const string shardSuffix = "-caldav.icloud.com";
            if (!host.EndsWith(shardSuffix, StringComparison.OrdinalIgnoreCase))
                return false;
            string shard = host[..^shardSuffix.Length];
            return shard.Length > 0 && !shard.Contains('.');
        }

        public static Uri ResolveEndpoint(Uri baseUri, string? href)
        {
            if (!IsAllowedEndpoint(baseUri) || string.IsNullOrWhiteSpace(href) || href.Length > MaxEndpointLength)
                throw new InvalidDataException("iCloud returned an invalid CalDAV address.");

            if (!Uri.TryCreate(href.Trim(), UriKind.RelativeOrAbsolute, out var parsed))
                throw new InvalidDataException("iCloud returned an invalid CalDAV address.");

            Uri resolved = parsed.IsAbsoluteUri ? parsed : new Uri(baseUri, parsed);
            if (!IsAllowedEndpoint(resolved))
                throw new InvalidDataException("iCloud redirected CalDAV outside the permitted Apple domain.");
            return resolved;
        }

        public static Uri ParseCurrentUserPrincipal(string xml, Uri responseUri)
        {
            var document = ParseXml(xml);
            string? href = SuccessfulProperties(document)
                .Elements(Dav + "current-user-principal")
                .Elements(Dav + "href")
                .Select(element => element.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            return ResolveEndpoint(responseUri, href);
        }

        public static Uri ParseCalendarHome(string xml, Uri responseUri)
        {
            var document = ParseXml(xml);
            string? href = SuccessfulProperties(document)
                .Elements(CalDav + "calendar-home-set")
                .Elements(Dav + "href")
                .Select(element => element.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            return ResolveEndpoint(responseUri, href);
        }

        public static IReadOnlyList<ICloudCalendarDescriptor> ParseCalendars(string xml, Uri responseUri)
        {
            var document = ParseXml(xml);
            var calendars = new List<ICloudCalendarDescriptor>();

            foreach (var response in document.Descendants(Dav + "response"))
            {
                var properties = SuccessfulProperties(response).ToList();
                bool isCalendar = properties
                    .Elements(Dav + "resourcetype")
                    .Elements(CalDav + "calendar")
                    .Any();
                if (!isCalendar) continue;

                var supportedComponents = properties
                    .Elements(CalDav + "supported-calendar-component-set")
                    .Descendants(CalDav + "comp")
                    .Select(element => (string?)element.Attribute("name"))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToList();
                if (supportedComponents.Count > 0
                    && !supportedComponents.Any(value => string.Equals(value, "VEVENT", StringComparison.OrdinalIgnoreCase)))
                    continue;

                string? href = response.Element(Dav + "href")?.Value;
                Uri calendarUri = ResolveEndpoint(responseUri, href);
                string name = properties.Elements(Dav + "displayname").Select(element => element.Value.Trim()).FirstOrDefault() ?? "";
                if (string.IsNullOrWhiteSpace(name))
                    name = GetLastPathSegment(calendarUri);
                if (name.Length > 256) name = name[..256];

                string color = NormalizeColor(properties.Elements(AppleIcal + "calendar-color").Select(element => element.Value).FirstOrDefault());
                var privileges = properties
                    .Elements(Dav + "current-user-privilege-set")
                    .Descendants(Dav + "privilege")
                    .Elements()
                    .Select(element => element.Name.LocalName)
                    .ToList();
                bool writable = privileges.Count == 0 || privileges.Any(value => value is "write" or "write-content" or "all");

                if (calendars.Count >= MaxCalendarCount)
                    throw new InvalidDataException("iCloud returned too many calendars.");
                calendars.Add(new ICloudCalendarDescriptor(calendarUri, name, color, writable));
            }

            return calendars;
        }

        public static IReadOnlyList<ICloudCalendarResource> ParseCalendarResources(string xml, Uri responseUri)
        {
            var document = ParseXml(xml);
            var resources = new List<ICloudCalendarResource>();

            foreach (var response in document.Descendants(Dav + "response"))
            {
                var properties = SuccessfulProperties(response).ToList();
                string? calendarData = properties.Elements(CalDav + "calendar-data")
                    .Select(element => element.Value)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                if (string.IsNullOrWhiteSpace(calendarData)) continue;

                string? href = response.Element(Dav + "href")?.Value;
                Uri resourceUri = ResolveEndpoint(responseUri, href);
                string etag = properties.Elements(Dav + "getetag").Select(element => element.Value.Trim()).FirstOrDefault() ?? "";
                if (resources.Count >= MaxResourceCount)
                    throw new InvalidDataException("iCloud returned too many calendar resources.");
                resources.Add(new ICloudCalendarResource(resourceUri, etag, calendarData));
            }

            return resources;
        }

        private static XDocument ParseXml(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml) || xml.Length > MaxXmlCharacters)
                throw new InvalidDataException("The iCloud CalDAV response was empty or too large.");

            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxXmlCharacters,
                MaxCharactersFromEntities = 0
            };
            using var textReader = new StringReader(xml);
            using var xmlReader = XmlReader.Create(textReader, settings);
            return XDocument.Load(xmlReader, LoadOptions.None);
        }

        private static IEnumerable<XElement> SuccessfulProperties(XContainer container)
            => container.Descendants(Dav + "propstat")
                .Where(propstat => IsSuccessStatus(propstat.Element(Dav + "status")?.Value))
                .Elements(Dav + "prop");

        private static bool IsSuccessStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status)) return false;
            var parts = status.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 && int.TryParse(parts[1], out int code) && code is >= 200 and < 300;
        }

        private static string NormalizeColor(string? value)
        {
            string color = value?.Trim() ?? "";
            if (color.Length >= 7 && color[0] == '#' && color.Skip(1).Take(6).All(Uri.IsHexDigit))
                return color[..7].ToUpperInvariant();
            if (color.Length >= 4 && color[0] == '#' && color.Skip(1).Take(3).All(Uri.IsHexDigit))
                return $"#{color[1]}{color[1]}{color[2]}{color[2]}{color[3]}{color[3]}".ToUpperInvariant();
            return "";
        }

        private static string GetLastPathSegment(Uri uri)
        {
            string segment = uri.Segments.LastOrDefault()?.Trim('/') ?? "";
            if (string.IsNullOrWhiteSpace(segment)) return "iCloud";
            try { return Uri.UnescapeDataString(segment); }
            catch { return segment; }
        }
    }

    internal static class ICloudEventReferenceCodec
    {
        private const string Prefix = "icloud:";
        internal const int MaxReferenceLength = 8192;
        internal const int MaxUidLength = 1024;
        internal const int MaxTimeZoneIdLength = 256;

        public static string Encode(ICloudEventReference reference)
        {
            string timeZoneId = reference.TimeZoneId ?? "";
            if (!ICloudCalDavPolicy.IsAllowedEndpoint(reference.ResourceUri))
                throw new ArgumentException("The iCloud event address is invalid.", nameof(reference));
            if (string.IsNullOrWhiteSpace(reference.Uid) || reference.Uid.Length > MaxUidLength)
                throw new ArgumentException("The iCloud event identifier is invalid.", nameof(reference));
            if (timeZoneId.Length > MaxTimeZoneIdLength)
                throw new ArgumentException("The iCloud event time zone is invalid.", nameof(reference));

            string encoded = Prefix + string.Join('|',
                EncodeText(reference.ResourceUri.AbsoluteUri),
                EncodeText(reference.Uid),
                reference.IsRecurring ? "1" : "0",
                reference.IsAllDay ? "1" : "0",
                reference.OccurrenceStart.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
                EncodeText(timeZoneId));
            if (encoded.Length > MaxReferenceLength)
                throw new ArgumentException("The iCloud event reference is too large.", nameof(reference));
            return encoded;
        }

        public static bool TryDecode(string? value, out ICloudEventReference reference)
        {
            reference = null!;
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaxReferenceLength || !value.StartsWith(Prefix, StringComparison.Ordinal))
                return false;

            string[] parts = value[Prefix.Length..].Split('|');
            if (parts.Length != 6
                || !TryDecodeText(parts[0], out string uriText)
                || !TryDecodeText(parts[1], out string uid)
                || !long.TryParse(parts[4], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long ticks)
                || !TryDecodeText(parts[5], out string timeZoneId)
                || !Uri.TryCreate(uriText, UriKind.Absolute, out var uri)
                || !ICloudCalDavPolicy.IsAllowedEndpoint(uri)
                || string.IsNullOrWhiteSpace(uid)
                || uid.Length > MaxUidLength
                || timeZoneId.Length > MaxTimeZoneIdLength
                || parts[2] is not ("0" or "1")
                || parts[3] is not ("0" or "1"))
                return false;

            try
            {
                reference = new ICloudEventReference(
                    uri,
                    uid,
                    parts[2] == "1",
                    parts[3] == "1",
                    new DateTime(ticks, DateTimeKind.Unspecified),
                    timeZoneId);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        private static string EncodeText(string value)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        private static bool TryDecodeText(string value, out string decoded)
        {
            decoded = "";
            try
            {
                string base64 = value.Replace('-', '+').Replace('_', '/');
                base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
                decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
