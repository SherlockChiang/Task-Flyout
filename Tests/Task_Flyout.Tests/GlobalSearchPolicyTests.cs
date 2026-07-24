using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class GlobalSearchPolicyTests
{
    [Fact]
    public void EmptyQueryReturnsCommandsOnlyInStableIdOrder()
    {
        var candidates = new[]
        {
            Candidate("task", GlobalSearchGroupKind.Tasks, "Task"),
            Candidate("command-b", GlobalSearchGroupKind.Commands, "B"),
            Candidate("command-a", GlobalSearchGroupKind.Commands, "A")
        };

        Assert.Equal(new[] { "command-a", "command-b" }, GlobalSearchPolicy.Search(candidates, "").Select(item => item.Id));
    }

    [Fact]
    public void RankingPrefersExactThenPrefixThenTitleThenMetadata()
    {
        var candidates = new[]
        {
            Candidate("metadata", GlobalSearchGroupKind.Tasks, "Other", "needle"),
            Candidate("contains", GlobalSearchGroupKind.Tasks, "A needle here"),
            Candidate("prefix", GlobalSearchGroupKind.Tasks, "Needle begins"),
            Candidate("exact", GlobalSearchGroupKind.Tasks, "needle")
        };

        Assert.Equal(new[] { "exact", "prefix", "contains", "metadata" }, GlobalSearchPolicy.Search(candidates, "needle").Select(item => item.Id));
    }

    [Fact]
    public void CapsEachContentGroupAndUsesDeterministicTieBreakers()
    {
        var candidates = Enumerable.Range(0, 12)
            .Select(index => new GlobalSearchCandidate(
                $"task-{index:D2}", GlobalSearchGroupKind.Tasks, "match", "", "match",
                new DateTimeOffset(2026, 1, 1, 0, index, 0, TimeSpan.Zero)))
            .Concat(Enumerable.Range(0, 10).Select(index => Candidate($"mail-{index:D2}", GlobalSearchGroupKind.Mail, "match")))
            .ToList();

        var result = GlobalSearchPolicy.Search(candidates, "match");

        Assert.Equal(GlobalSearchPolicy.ContentGroupCap, result.Count(item => item.Group == GlobalSearchGroupKind.Tasks));
        Assert.Equal(GlobalSearchPolicy.ContentGroupCap, result.Count(item => item.Group == GlobalSearchGroupKind.Mail));
        Assert.Equal("task-11", result.First(item => item.Group == GlobalSearchGroupKind.Tasks).Id);
    }

    [Fact]
    public void GroupOrderIsFixedRegardlessOfInputOrder()
    {
        var candidates = new[]
        {
            Candidate("rss", GlobalSearchGroupKind.Rss, "match"),
            Candidate("mail", GlobalSearchGroupKind.Mail, "match"),
            Candidate("event", GlobalSearchGroupKind.Calendar, "match"),
            Candidate("task", GlobalSearchGroupKind.Tasks, "match"),
            Candidate("command", GlobalSearchGroupKind.Commands, "match")
        };

        Assert.Equal(Enum.GetValues<GlobalSearchGroupKind>(), GlobalSearchPolicy.Search(candidates, "match").Select(item => item.Group));
    }

    private static GlobalSearchCandidate Candidate(string id, GlobalSearchGroupKind group, string title, string detail = "")
        => new(id, group, title, detail, $"{title} {detail}");
}
