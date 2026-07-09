using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class PersistedOrderPolicyTests
{
    private sealed record Item(string Id, string Name);

    [Fact]
    public void Apply_orders_known_items_and_keeps_unknown_items_in_original_order()
    {
        var items = new[]
        {
            new Item("a", "A"),
            new Item("b", "B"),
            new Item("c", "C"),
            new Item("d", "D")
        };

        var result = PersistedOrderPolicy.Apply(items, new[] { "c", "a" }, item => item.Id);

        Assert.Equal(new[] { "c", "a", "b", "d" }, result.Select(item => item.Id));
    }

    [Fact]
    public void Apply_ignores_duplicate_order_entries_after_first()
    {
        var items = new[]
        {
            new Item("a", "A"),
            new Item("b", "B"),
            new Item("c", "C")
        };

        var result = PersistedOrderPolicy.Apply(items, new[] { "b", "a", "b" }, item => item.Id);

        Assert.Equal(new[] { "b", "a", "c" }, result.Select(item => item.Id));
    }

    [Fact]
    public void Apply_keeps_original_order_when_order_is_null()
    {
        AssertOriginalOrder(null);
    }

    [Fact]
    public void Apply_keeps_original_order_when_order_is_empty()
    {
        AssertOriginalOrder(Array.Empty<string>());
    }

    [Fact]
    public void Apply_keeps_original_order_when_order_has_no_valid_entries()
    {
        AssertOriginalOrder(new[] { "", "   " });
    }

    private static void AssertOriginalOrder(string[]? order)
    {
        var items = new[]
        {
            new Item("a", "A"),
            new Item("b", "B")
        };

        var result = PersistedOrderPolicy.Apply(items, order, item => item.Id);

        Assert.Equal(new[] { "a", "b" }, result.Select(item => item.Id));
    }
}
