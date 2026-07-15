using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class IncrementalBucketSnapshotTests
{
    [Fact]
    public void Unchanged_buckets_are_reused_without_cloning()
    {
        var source = new Dictionary<string, List<Item>>
        {
            ["2026-07-15"] = [new("first", false)],
            ["2026-07-16"] = [new("second", false)]
        };
        var first = IncrementalBucketSnapshot.Create(source, null, Equal, Clone);
        var cloneCount = 0;

        var second = IncrementalBucketSnapshot.Create(source, first, Equal, item =>
        {
            cloneCount++;
            return Clone(item);
        });

        Assert.Same(first["2026-07-15"], second["2026-07-15"]);
        Assert.Same(first["2026-07-16"], second["2026-07-16"]);
        Assert.Equal(0, cloneCount);
    }

    [Fact]
    public void Only_changed_bucket_is_cloned_and_isolated()
    {
        var source = new Dictionary<string, List<Item>>
        {
            ["2026-07-15"] = [new("first", false)],
            ["2026-07-16"] = [new("second", false)]
        };
        var first = IncrementalBucketSnapshot.Create(source, null, Equal, Clone);
        source["2026-07-16"][0].Completed = true;
        var cloneCount = 0;

        var second = IncrementalBucketSnapshot.Create(source, first, Equal, item =>
        {
            cloneCount++;
            return Clone(item);
        });

        Assert.Same(first["2026-07-15"], second["2026-07-15"]);
        Assert.NotSame(first["2026-07-16"], second["2026-07-16"]);
        Assert.False(first["2026-07-16"][0].Completed);
        Assert.True(second["2026-07-16"][0].Completed);
        Assert.Equal(1, cloneCount);
    }

    [Fact]
    public void Removed_buckets_are_not_retained()
    {
        var source = new Dictionary<string, List<Item>> { ["old"] = [new("first", false)] };
        var first = IncrementalBucketSnapshot.Create(source, null, Equal, Clone);

        var second = IncrementalBucketSnapshot.Create(new Dictionary<string, List<Item>>(), first, Equal, Clone);

        Assert.Empty(second);
    }

    [Fact]
    public void Unchanged_large_snapshot_allocates_substantially_less_than_full_clone()
    {
        var source = Enumerable.Range(0, 200).ToDictionary(
            day => $"day-{day}",
            day => Enumerable.Range(0, 25).Select(item => new Item($"{day}-{item}", false)).ToList(),
            StringComparer.Ordinal);
        var first = IncrementalBucketSnapshot.Create(source, null, Equal, Clone);

        _ = IncrementalBucketSnapshot.Create(source, first, Equal, Clone);
        _ = FullClone(source);

        var beforeIncremental = GC.GetAllocatedBytesForCurrentThread();
        _ = IncrementalBucketSnapshot.Create(source, first, Equal, Clone);
        var incrementalBytes = GC.GetAllocatedBytesForCurrentThread() - beforeIncremental;

        var beforeFull = GC.GetAllocatedBytesForCurrentThread();
        _ = FullClone(source);
        var fullCloneBytes = GC.GetAllocatedBytesForCurrentThread() - beforeFull;

        Assert.True(incrementalBytes < fullCloneBytes / 4,
            $"Incremental snapshot allocated {incrementalBytes} bytes versus {fullCloneBytes} for a full clone.");
    }

    private static bool Equal(Item left, Item right)
        => left.Name == right.Name && left.Completed == right.Completed;

    private static Item Clone(Item item) => new(item.Name, item.Completed);

    private static Dictionary<string, List<Item>> FullClone(IReadOnlyDictionary<string, List<Item>> source)
        => source.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Select(Clone).ToList(),
            StringComparer.Ordinal);

    private sealed class Item(string name, bool completed)
    {
        public string Name { get; } = name;
        public bool Completed { get; set; } = completed;
    }
}
