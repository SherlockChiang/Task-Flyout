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
