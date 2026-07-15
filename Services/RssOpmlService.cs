using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Task_Flyout.Services
{
    internal sealed record RssOpmlEntry(string Title, string Url, string FolderName);
    internal sealed record RssOpmlPreview(
        IReadOnlyList<RssOpmlEntry> NewEntries,
        int DuplicateCount,
        int InsecureHttpCount,
        int FolderCount);

    internal static class RssOpmlService
    {
        public const int MaximumEntries = 2000;
        public const long MaximumDocumentCharacters = 5L * 1024 * 1024;

        public static IReadOnlyList<RssOpmlEntry> Parse(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return Array.Empty<RssOpmlEntry>();
            using var reader = XmlReader.Create(
                new StringReader(xml),
                RssXmlSecurity.CreateReaderSettings(MaximumDocumentCharacters));
            var document = XDocument.Load(reader, LoadOptions.None);
            var root = document.Root;
            if (root is null || !string.Equals(root.Name.LocalName, "opml", StringComparison.OrdinalIgnoreCase))
                throw new XmlException("The selected file is not an OPML document.");

            var entries = new List<RssOpmlEntry>();
            var body = root.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "body", StringComparison.OrdinalIgnoreCase));
            if (body == null) return entries;

            foreach (var outline in body.Elements().Where(IsOutline))
                AddOutline(outline, "", entries);
            return entries;
        }

        public static RssOpmlPreview Preview(
            IEnumerable<RssOpmlEntry> imported,
            IEnumerable<string> existingUrls)
        {
            var knownUrls = existingUrls
                .Select(NormalizeUrl)
                .Where(url => url.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newEntries = new List<RssOpmlEntry>();
            int duplicates = 0;

            foreach (var entry in imported)
            {
                var normalized = NormalizeUrl(entry.Url);
                if (normalized.Length == 0 || !knownUrls.Add(normalized))
                {
                    duplicates++;
                    continue;
                }
                newEntries.Add(entry);
            }

            return new RssOpmlPreview(
                newEntries,
                duplicates,
                newEntries.Count(entry => Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri)
                    && uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)),
                newEntries.Select(entry => entry.FolderName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .Count());
        }

        public static string Export(
            IEnumerable<RssOpmlEntry> entries,
            string title = "Task Flyout RSS Subscriptions")
        {
            var entryList = entries.ToList();
            var body = new XElement("body");
            foreach (var folderGroup in entryList
                         .Where(entry => !string.IsNullOrWhiteSpace(entry.FolderName))
                         .GroupBy(entry => entry.FolderName.Trim(), StringComparer.CurrentCultureIgnoreCase)
                         .OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var folder = new XElement("outline",
                    new XAttribute("text", folderGroup.Key),
                    new XAttribute("title", folderGroup.Key));
                foreach (var entry in folderGroup.OrderBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase))
                    folder.Add(CreateFeedOutline(entry));
                body.Add(folder);
            }

            foreach (var entry in entryList
                         .Where(entry => string.IsNullOrWhiteSpace(entry.FolderName))
                         .OrderBy(entry => entry.Title, StringComparer.CurrentCultureIgnoreCase))
                body.Add(CreateFeedOutline(entry));

            var document = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("opml",
                    new XAttribute("version", "2.0"),
                    new XElement("head", new XElement("title", title)),
                    body));
            return document.ToString(SaveOptions.DisableFormatting);
        }

        private static void AddOutline(XElement outline, string folderName, List<RssOpmlEntry> entries)
        {
            var xmlUrl = GetAttribute(outline, "xmlUrl");
            if (!string.IsNullOrWhiteSpace(xmlUrl))
            {
                if (Uri.TryCreate(xmlUrl.Trim(), UriKind.Absolute, out var uri)
                    && (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                        || uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
                {
                    if (entries.Count >= MaximumEntries)
                        throw new InvalidOperationException($"OPML files may contain at most {MaximumEntries} subscriptions.");
                    var title = GetAttribute(outline, "title");
                    if (string.IsNullOrWhiteSpace(title)) title = GetAttribute(outline, "text");
                    entries.Add(new RssOpmlEntry(
                        string.IsNullOrWhiteSpace(title) ? uri.Host : title.Trim(),
                        uri.AbsoluteUri,
                        folderName));
                }
                return;
            }

            var name = GetAttribute(outline, "title");
            if (string.IsNullOrWhiteSpace(name)) name = GetAttribute(outline, "text");
            var childFolder = string.IsNullOrWhiteSpace(name) ? folderName : name.Trim();
            foreach (var child in outline.Elements().Where(IsOutline))
                AddOutline(child, childFolder, entries);
        }

        private static XElement CreateFeedOutline(RssOpmlEntry entry)
            => new("outline",
                new XAttribute("type", "rss"),
                new XAttribute("text", entry.Title ?? ""),
                new XAttribute("title", entry.Title ?? ""),
                new XAttribute("xmlUrl", entry.Url ?? ""));

        private static bool IsOutline(XElement element)
            => string.Equals(element.Name.LocalName, "outline", StringComparison.OrdinalIgnoreCase);

        private static string GetAttribute(XElement element, string name)
            => element.Attributes().FirstOrDefault(attribute =>
                string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value ?? "";

        private static string NormalizeUrl(string? value)
            => Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
                ? uri.AbsoluteUri.TrimEnd('/')
                : "";
    }
}
