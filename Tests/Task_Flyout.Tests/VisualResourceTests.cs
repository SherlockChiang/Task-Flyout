using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class VisualResourceTests
{
    private static string Root
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "Task_Flyout.csproj"))) return dir.FullName;
            throw new DirectoryNotFoundException();
        }
    }

    [Fact]
    public void Icon_codepoints_resolve_to_the_intended_glyph_in_the_shipped_font()
    {
        var font = new FontMap(File.ReadAllBytes(Path.Combine(Root, "Assets", "FluentSystemIcons-Filled.ttf")));
        string catalog = File.ReadAllText(Path.Combine(Root, "Services", "AppIcons.cs"));
        var entries = Regex.Matches(catalog, @"public const string ([A-Za-z0-9_]+) = .*; // (ic_fluent_[A-Za-z0-9_]+)");
        var fields = typeof(AppIcons).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.Name != nameof(AppIcons.FontFamily)).ToDictionary(field => field.Name);
        Assert.Equal(fields.Count, entries.Count);
        foreach (Match entry in entries)
        {
            var glyph = Assert.IsType<string>(fields[entry.Groups[1].Value].GetRawConstantValue());
            Assert.Single(glyph);
            Assert.Equal(entry.Groups[2].Value, font.NameFor(glyph[0]));
        }
    }

    [Fact]
    public void Xaml_and_code_share_the_same_font_and_glyph_catalog()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var resources = XDocument.Load(Path.Combine(Root, "App.xaml")).Descendants()
            .Where(element => element.Attribute(x + "Key")?.Value.StartsWith("AppIcon") == true)
            .ToDictionary(element => element.Attribute(x + "Key")!.Value, element => element.Value);
        var fields = typeof(AppIcons).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.Name != nameof(AppIcons.FontFamily)).ToArray();
        Assert.Equal(fields.Length, resources.Count);
        foreach (var field in fields)
            Assert.Equal(field.GetRawConstantValue(), resources["AppIcon" + field.Name]);
        var font = XDocument.Load(Path.Combine(Root, "App.xaml")).Descendants()
            .Single(element => element.Attribute(x + "Key")?.Value == "TaskFlyoutIconFont");
        Assert.Equal(AppIcons.FontFamily, font.Value);
    }

    [Fact]
    public void Shared_visual_resource_references_are_resolvable()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var app = XDocument.Load(Path.Combine(Root, "App.xaml"));
        var keys = app.Descendants().Select(element => element.Attribute(x + "Key")?.Value)
            .Where(key => key != null).ToHashSet();
        var files = Directory.EnumerateFiles(Root, "*.xaml")
            .Concat(Directory.EnumerateFiles(Path.Combine(Root, "Views"), "*.xaml"));
        foreach (string file in files)
        {
            var references = Regex.Matches(File.ReadAllText(file), @"[{](?:StaticResource|ThemeResource) ((?:TaskFlyout|AppIcon)[A-Za-z0-9_]+)[}]");
            foreach (Match reference in references)
                Assert.True(keys.Contains(reference.Groups[1].Value), $"{file}: {reference.Value}");
        }
    }

    // Read the font shipped in the app, so an asset update cannot silently change
    // an icon to a missing glyph or a different semantic symbol.
    private sealed class FontMap
    {
        private readonly byte[] _data;
        private readonly Dictionary<string, int> _tables = new();
        private readonly string[] _names;

        public FontMap(byte[] data)
        {
            _data = data;
            for (int i = 0; i < U16(4); i++)
            {
                int record = 12 + i * 16;
                _tables.Add(Encoding.ASCII.GetString(data, record, 4), U32(record + 8));
            }
            int post = _tables["post"];
            Assert.Equal(0x20000, U32(post));
            int count = U16(post + 32);
            int[] indexes = Enumerable.Range(0, count).Select(i => U16(post + 34 + i * 2)).ToArray();
            int position = post + 34 + count * 2;
            var custom = new List<string>();
            for (int i = 258; i <= indexes.Max(); i++)
            {
                int length = data[position++];
                custom.Add(Encoding.ASCII.GetString(data, position, length));
                position += length;
            }
            _names = indexes.Select(index => index >= 258 ? custom[index - 258] : "").ToArray();
        }

        public string NameFor(char character)
        {
            int cmap = _tables["cmap"];
            for (int record = 0; record < U16(cmap + 2); record++)
            {
                int table = cmap + U32(cmap + 4 + record * 8 + 4);
                if (U16(table) != 4) continue;
                int segments = U16(table + 6) / 2;
                for (int i = 0; i < segments; i++)
                {
                    int end = U16(table + 14 + i * 2);
                    int start = U16(table + 16 + segments * 2 + i * 2);
                    if (character < start || character > end) continue;
                    int delta = U16(table + 16 + segments * 4 + i * 2);
                    int offsetPosition = table + 16 + segments * 6 + i * 2;
                    int offset = U16(offsetPosition);
                    int glyph = offset == 0 ? character : U16(offsetPosition + offset + (character - start) * 2);
                    if (offset == 0 || glyph != 0) glyph = (glyph + delta) & 0xffff;
                    Assert.NotEqual(0, glyph);
                    return _names[glyph];
                }
            }
            throw new InvalidDataException($"No cmap entry for U+{(int)character:X4}");
        }

        private int U16(int offset) => BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(offset, 2));
        private int U32(int offset) => checked((int)BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(offset, 4)));
    }
}
