using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Task_Flyout.Tests;

public class LocalizationResourceTests
{
    [Fact]
    public void English_and_chinese_resources_have_unique_matching_keys()
    {
        string root = FindRepositoryRoot();
        var english = LoadResourceKeys(Path.Combine(root, "Strings", "en-US", "Resources.resw"));
        var chinese = LoadResourceKeys(Path.Combine(root, "Strings", "zh-Hans", "Resources.resw"));

        Assert.Equal(english.OrderBy(key => key), chinese.OrderBy(key => key));
    }

    [Fact]
    public void English_and_chinese_format_placeholders_match()
    {
        string root = FindRepositoryRoot();
        var english = LoadResourceValues(Path.Combine(root, "Strings", "en-US", "Resources.resw"));
        var chinese = LoadResourceValues(Path.Combine(root, "Strings", "zh-Hans", "Resources.resw"));
        var placeholder = new Regex(@"\{\d+(?:[^}]*)\}", RegexOptions.CultureInvariant);

        foreach (string key in english.Keys)
        {
            var englishIndexes = placeholder.Matches(english[key]).Select(match => Regex.Match(match.Value, @"\d+").Value).Order().ToArray();
            var chineseIndexes = placeholder.Matches(chinese[key]).Select(match => Regex.Match(match.Value, @"\d+").Value).Order().ToArray();
            Assert.Equal(englishIndexes, chineseIndexes);
        }
    }

    [Fact]
    public void Literal_resource_lookups_reference_existing_keys()
    {
        string root = FindRepositoryRoot();
        var keys = LoadResourceKeys(Path.Combine(root, "Strings", "en-US", "Resources.resw"));
        var lookup = new Regex("(?:GetStringOrDefault|GetSafeString|GetResourceStringOrDefault)\\(\\s*\"([^\"]+)\"", RegexOptions.CultureInvariant);
        var sourceFiles = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Tests{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        var missing = sourceFiles
            .SelectMany(path => lookup.Matches(File.ReadAllText(path))
                .Select(match => match.Groups[1].Value.Replace('/', '.'))
                .Where(key => !keys.Contains(key))
                .Select(key => $"{Path.GetRelativePath(root, path)}: {key}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Xaml_accessibility_properties_do_not_hard_code_chinese_text()
    {
        string root = FindRepositoryRoot();
        var xamlFiles = Directory.EnumerateFiles(root, "*.xaml", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "Views"), "*.xaml", SearchOption.TopDirectoryOnly));
        var hardCodedAccessibilityText = new Regex(
            "(?:AutomationProperties\\.Name|ToolTipService\\.ToolTip)=\"[^\"]*[\\u4e00-\\u9fff]",
            RegexOptions.CultureInvariant);

        var violations = xamlFiles
            .Where(path => hardCodedAccessibilityText.IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(root, path))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Packaged_smoke_surfaces_have_stable_automation_ids()
    {
        string root = FindRepositoryRoot();
        var requiredIds = new[]
        {
            "MainNavigation", "NavCalendar", "NavTasks", "NavMail",
            "CalendarToggleAccounts", "CalendarCollapseAccounts",
            "TasksToggleAccounts", "MailComposeButton"
        };
        string xaml = string.Join('\n', Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText));

        foreach (string automationId in requiredIds)
            Assert.Contains($"AutomationProperties.AutomationId=\"{automationId}\"", xaml, StringComparison.Ordinal);

        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var mainNavigation = XDocument.Load(Path.Combine(root, "MainWindow.xaml"))
            .Descendants()
            .Single(element => (string?)element.Attribute(x + "Name") == "MainNav");
        Assert.Equal("48", (string?)mainNavigation.Attribute("CompactPaneLength"));
        Assert.Equal("168", (string?)mainNavigation.Attribute("OpenPaneLength"));
        Assert.Equal("False", (string?)mainNavigation.Attribute("IsPaneOpen"));
    }

    [Fact]
    public void Mail_list_uses_single_selection_and_click_to_open()
    {
        string root = FindRepositoryRoot();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var list = XDocument.Load(Path.Combine(root, "Views", "MailPage.xaml"))
            .Descendants()
            .Single(element => (string?)element.Attribute(x + "Name") == "MailListView");

        Assert.Equal("Single", (string?)list.Attribute("SelectionMode"));
        Assert.Equal("True", (string?)list.Attribute("IsItemClickEnabled"));
        Assert.Equal("MailListView_ItemClick", (string?)list.Attribute("ItemClick"));
    }

    [Fact]
    public void Full_window_pages_stretch_and_calendar_status_stays_in_layout()
    {
        string root = FindRepositoryRoot();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var mainWindow = XDocument.Load(Path.Combine(root, "MainWindow.xaml"));
        var frame = mainWindow.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "ContentFrame");
        Assert.Equal("Stretch", (string?)frame.Attribute("HorizontalContentAlignment"));
        Assert.Equal("Stretch", (string?)frame.Attribute("VerticalContentAlignment"));

        var calendar = XDocument.Load(Path.Combine(root, "Views", "CalendarPage.xaml"));
        var status = calendar.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "CalendarStatusText");
        Assert.Equal("CharacterEllipsis", (string?)status.Attribute("TextTrimming"));
        Assert.Equal("0", (string?)status.Attribute("MinWidth"));

        var tasks = XDocument.Load(Path.Combine(root, "Views", "TasksPage.xaml"));
        var accountPane = tasks.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "AccountPane");
        Assert.Equal("*", accountPane.Elements().Single(element => element.Name.LocalName == "Grid.RowDefinitions")
            .Elements().ElementAt(1).Attribute("Height")?.Value);

        var calendarAccountPane = calendar.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "AccountPane");
        Assert.Equal("{ThemeResource TaskFlyoutSectionBackgroundBrush}", (string?)calendarAccountPane.Attribute("Background"));
        Assert.Equal("{StaticResource TaskFlyoutSectionCornerRadius}", (string?)calendarAccountPane.Attribute("CornerRadius"));
        var calendarTimelinePane = calendar.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "TimelinePane");
        Assert.Equal("{ThemeResource TaskFlyoutSectionBackgroundBrush}", (string?)calendarTimelinePane.Attribute("Background"));
        Assert.Equal("{StaticResource TaskFlyoutSectionCornerRadius}", (string?)calendarTimelinePane.Attribute("CornerRadius"));
        Assert.Equal("{ThemeResource TaskFlyoutSectionBackgroundBrush}", (string?)accountPane.Attribute("Background"));
        Assert.Equal("{StaticResource TaskFlyoutSectionCornerRadius}", (string?)accountPane.Attribute("CornerRadius"));
    }

    [Fact]
    public void Main_pages_use_shared_design_resources()
    {
        string root = FindRepositoryRoot();
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var appResources = XDocument.Load(Path.Combine(root, "App.xaml"))
            .Descendants()
            .Select(element => (string?)element.Attribute(x + "Key"))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        string[] requiredDesignKeys =
        {
            "TaskFlyoutPagePadding",
            "TaskFlyoutSectionCornerRadius",
            "TaskFlyoutPageBackgroundBrush",
            "TaskFlyoutSidePaneBackgroundBrush",
            "TaskFlyoutSectionBackgroundBrush",
            "TaskFlyoutCardBackgroundBrush",
            "TaskFlyoutSectionBorderBrush",
            "TaskFlyoutDividerBrush"
        };

        foreach (string key in requiredDesignKeys)
            Assert.Contains(key, appResources);

        string[] pageFiles =
        {
            "Views\\AddAccountPage.xaml",
            "Views\\CalendarPage.xaml",
            "Views\\MailPage.xaml",
            "Views\\RssPage.xaml",
            "Views\\SettingsPage.xaml",
            "Views\\TasksPage.xaml",
            "Views\\WeatherPage.xaml"
        };

        foreach (string relativePath in pageFiles)
        {
            var page = XDocument.Load(Path.Combine(root, relativePath)).Root!;
            Assert.Equal("{ThemeResource TaskFlyoutPageBackgroundBrush}", (string?)page.Attribute("Background"));
        }

        string settingsXaml = File.ReadAllText(Path.Combine(root, "Views", "SettingsPage.xaml"));
        Assert.True(Regex.Matches(settingsXaml, "Style=\"\\{StaticResource SettingsSectionStyle\\}\"").Count >= 8);
    }

    private static HashSet<string> LoadResourceKeys(string path)
    {
        var elements = XDocument.Load(path)
            .Root!
            .Elements("data")
            .ToList();
        Assert.DoesNotContain(elements, element => string.IsNullOrWhiteSpace(element.Element("value")?.Value));
        var keys = elements
            .Select(element => (string?)element.Attribute("name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        return keys.ToHashSet(StringComparer.Ordinal);
    }

    private static Dictionary<string, string> LoadResourceValues(string path)
        => XDocument.Load(path)
            .Root!
            .Elements("data")
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                element => element.Element("value")?.Value ?? "",
                StringComparer.Ordinal);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Task_Flyout.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Task Flyout repository root.");
    }
}
