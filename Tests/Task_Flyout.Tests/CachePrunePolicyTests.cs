using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class CachePrunePolicyTests
{
    [Fact]
    public void SelectOldestUntilTarget_returns_empty_when_under_limit()
    {
        var result = CachePrunePolicy.SelectOldestUntilTarget(new[]
        {
            new CachePruneEntry("a", 10, new DateTime(2024, 1, 1)),
            new CachePruneEntry("b", 20, new DateTime(2024, 1, 2))
        }, maxBytes: 100, targetBytes: 50);

        Assert.Empty(result);
    }

    [Fact]
    public void SelectOldestUntilTarget_deletes_oldest_files_until_target()
    {
        var result = CachePrunePolicy.SelectOldestUntilTarget(new[]
        {
            new CachePruneEntry("new", 40, new DateTime(2024, 1, 3)),
            new CachePruneEntry("oldest", 30, new DateTime(2024, 1, 1)),
            new CachePruneEntry("middle", 40, new DateTime(2024, 1, 2))
        }, maxBytes: 100, targetBytes: 50);

        Assert.Equal(new[] { "oldest", "middle" }, result);
    }

    [Fact]
    public void SelectOldestUntilTarget_ignores_zero_length_entries()
    {
        var result = CachePrunePolicy.SelectOldestUntilTarget(new[]
        {
            new CachePruneEntry("empty", 0, new DateTime(2024, 1, 1)),
            new CachePruneEntry("old", 80, new DateTime(2024, 1, 2)),
            new CachePruneEntry("new", 80, new DateTime(2024, 1, 3))
        }, maxBytes: 100, targetBytes: 80);

        Assert.Equal(new[] { "old" }, result);
    }
}
