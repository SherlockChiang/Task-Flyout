using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class MailBodyCachePolicyTests
{
    [Fact]
    public void Retained_bytes_are_deterministic_utf16_bytes()
    {
        Assert.Equal(10L, MailBodyCachePolicy.GetRetainedUtf16Bytes("abc", "de"));
        Assert.Equal(4L, MailBodyCachePolicy.GetRetainedUtf16Bytes("😀", null));
    }

    [Fact]
    public void Under_maximum_does_not_trim_to_target()
    {
        var result = Select(new[] { new Entry("a", "one", 90, 1) }, perMax: 100, perTarget: 50, globalMax: 100, globalTarget: 50);

        Assert.Empty(result);
    }

    [Fact]
    public void Per_account_overage_trims_that_account_lru_to_target()
    {
        var result = Select(new[]
        {
            new Entry("new", "one", 40, 3),
            new Entry("old", "one", 30, 1),
            new Entry("middle", "one", 40, 2),
            new Entry("other", "two", 80, 0)
        });

        Assert.Equal(new[] { "old", "middle" }, result);
    }

    [Fact]
    public void Global_eviction_prefers_other_accounts_before_active_account()
    {
        var result = Select(new[]
        {
            new Entry("active-old", "active", 60, 1),
            new Entry("other-new", "other", 60, 9),
            new Entry("active-new", "active", 60, 10)
        },
            activeAccount: "active",
            perMax: 1_000,
            perTarget: 900,
            globalMax: 150,
            globalTarget: 60);

        Assert.Equal(new[] { "other-new", "active-old" }, result);
    }

    [Fact]
    public void Active_message_is_protected_during_account_and_global_pruning()
    {
        var result = Select(new[]
        {
            new Entry("protected", "active", 90, 1),
            new Entry("old", "active", 40, 2),
            new Entry("other", "other", 40, 3)
        },
            activeAccount: "active",
            protectedKey: "protected",
            perMax: 100,
            perTarget: 80,
            globalMax: 100,
            globalTarget: 80);

        Assert.DoesNotContain("protected", result);
        Assert.Equal(new[] { "old", "other" }, result);
    }

    [Fact]
    public void Equal_access_is_resolved_by_ordinal_key()
    {
        var result = Select(new[]
        {
            new Entry("b", "one", 60, 1),
            new Entry("a", "one", 60, 1)
        },
            perMax: 100,
            perTarget: 60);

        Assert.Equal(new[] { "a" }, result);
    }

    private static IReadOnlyList<string> Select(
        Entry[] entries,
        string? activeAccount = null,
        string? protectedKey = null,
        long perMax = 100,
        long perTarget = 50,
        long globalMax = 10_000,
        long globalTarget = 9_000)
        => MailBodyCachePolicy.SelectEvictions(
            entries.Select(entry => new MailBodyCacheEntry(entry.Key, entry.Account, entry.Bytes, entry.Access)),
            perMax,
            perTarget,
            globalMax,
            globalTarget,
            activeAccount,
            protectedKey);

    private readonly record struct Entry(string Key, string Account, long Bytes, long Access);
}
