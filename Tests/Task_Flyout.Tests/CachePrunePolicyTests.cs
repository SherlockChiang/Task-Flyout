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

    [Fact]
    public void SelectLeastRecentlyUsedUntilTarget_prunes_by_access_ticks()
    {
        var result = CachePrunePolicy.SelectLeastRecentlyUsedUntilTarget(new[]
        {
            new SizedCacheEntry("new", 40, 30),
            new SizedCacheEntry("oldest", 30, 10),
            new SizedCacheEntry("middle", 40, 20)
        }, maxBytes: 100, targetBytes: 50);

        Assert.Equal(new[] { "oldest", "middle" }, result);
    }

    [Fact]
    public void SelectLeastRecentlyUsedUntilTarget_skips_protected_current_key()
    {
        var result = CachePrunePolicy.SelectLeastRecentlyUsedUntilTarget(new[]
        {
            new SizedCacheEntry("current", 90, 10),
            new SizedCacheEntry("old", 30, 20),
            new SizedCacheEntry("new", 30, 30)
        }, maxBytes: 100, targetBytes: 120, protectedKey: "current");

        Assert.Equal(new[] { "old" }, result);
    }

    [Fact]
    public void SelectLeastRecentlyUsedUntilTarget_returns_empty_when_under_limit()
    {
        var result = CachePrunePolicy.SelectLeastRecentlyUsedUntilTarget(new[]
        {
            new SizedCacheEntry("a", 10, 1),
            new SizedCacheEntry("b", 20, 2)
        }, maxBytes: 100, targetBytes: 50);

        Assert.Empty(result);
    }
}
