using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class VersionedBucketSlicePolicyTests
{
    [Fact]
    public void SameVersion_DoesNotEnumerateKeysOrCloneItems()
    {
        var source = new Dictionary<string, List<Item>>
        {
            ["2026-08-06"] = [new Item("source")]
        };
        var cloneCount = 0;

        var result = VersionedBucketSlicePolicy.CreateIfChanged(
            knownVersion: 7,
            publishedVersion: 7,
            source,
            ThrowWhenEnumerated(),
            item =>
            {
                cloneCount++;
                return new Item(item.Name);
            });

        Assert.Null(result);
        Assert.Equal(0, cloneCount);
    }

    [Fact]
    public void NewVersion_NormalizesKeysAndReturnsIsolatedClones()
    {
        var sourceItem = new Item("source");
        var source = new Dictionary<string, List<Item>>(StringComparer.Ordinal)
        {
            ["2026-08-06"] = [sourceItem]
        };
        var cloneCount = 0;

        var result = VersionedBucketSlicePolicy.CreateIfChanged(
            knownVersion: 7,
            publishedVersion: 8,
            source,
            ["", "2026-08-06", "2026-08-06", "missing"],
            item =>
            {
                cloneCount++;
                return new Item(item.Name);
            });

        Assert.NotNull(result);
        Assert.Equal(8, result.Version);
        var pair = Assert.Single(result.Buckets);
        Assert.Equal("2026-08-06", pair.Key);
        var clonedItem = Assert.Single(pair.Value);
        Assert.NotSame(sourceItem, clonedItem);
        clonedItem.Name = "changed";
        Assert.Equal("source", sourceItem.Name);
        Assert.Equal(1, cloneCount);
    }

    [Fact]
    public void NewVersion_IgnoresMissingKeys()
    {
        var result = VersionedBucketSlicePolicy.CreateIfChanged(
            knownVersion: 1,
            publishedVersion: 2,
            new Dictionary<string, List<Item>>(),
            ["missing"],
            item => new Item(item.Name));

        Assert.NotNull(result);
        Assert.Empty(result.Buckets);
    }

    [Fact]
    public void NullKeys_AreRejectedEvenWhenVersionIsUnchanged()
    {
        Assert.Throws<ArgumentNullException>(() =>
            VersionedBucketSlicePolicy.CreateIfChanged(
                knownVersion: 1,
                publishedVersion: 1,
                new Dictionary<string, List<Item>>(),
                null!,
                item => new Item(item.Name)));
    }

    private static IEnumerable<string> ThrowWhenEnumerated() => new ThrowingEnumerable();

    private sealed class ThrowingEnumerable : IEnumerable<string>
    {
        public IEnumerator<string> GetEnumerator()
            => throw new InvalidOperationException("Keys should not be enumerated.");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }

    private sealed class Item(string name)
    {
        public string Name { get; set; } = name;
    }
}
