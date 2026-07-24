using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class WeatherLocationPolicyTests
{
    [Fact]
    public void Normalizes_legacy_location_values()
    {
        var location = WeatherLocationPolicy.Normalize("  London  ", 51.5072, -0.1276);

        Assert.Equal("London", location.City);
        Assert.Equal(51.5072, location.Latitude);
        Assert.Equal(-0.1276, location.Longitude);
        Assert.True(WeatherLocationPolicy.HasPersistableData(location));
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    public void Rejects_invalid_latitude(double latitude, double expected)
    {
        Assert.Equal(expected, WeatherLocationPolicy.Normalize("", latitude, 0).Latitude);
    }

    [Fact]
    public void Empty_location_is_not_persistable()
    {
        Assert.False(WeatherLocationPolicy.HasPersistableData(WeatherLocationPolicy.Normalize("  ", 0, 0)));
    }

    [Fact]
    public void Favorites_are_bounded_and_current_location_is_reused()
    {
        var store = new WeatherFavoritesStore();
        Assert.True(WeatherLocationPolicy.TryAdd(store, "Here", 1, 2, true, out var current));
        Assert.True(WeatherLocationPolicy.TryAdd(store, "New here", 3, 4, true, out var updated));
        Assert.Equal(current!.Id, updated!.Id);
        for (int i = 0; i < 4; i++)
            Assert.True(WeatherLocationPolicy.TryAdd(store, $"City {i}", i + 10, i + 20, false, out _));
        Assert.False(WeatherLocationPolicy.TryAdd(store, "Sixth", 30, 30, false, out _));
        Assert.Equal(5, store.Locations.Count);
    }

    [Fact]
    public void Removing_active_favorite_selects_a_stable_neighbor()
    {
        var store = new WeatherFavoritesStore();
        WeatherLocationPolicy.TryAdd(store, "A", 1, 1, false, out var first);
        WeatherLocationPolicy.TryAdd(store, "B", 2, 2, false, out var second);
        Assert.True(WeatherLocationPolicy.Remove(store, second!.Id));
        Assert.Equal(first!.Id, store.ActiveId);
    }

    [Fact]
    public void Duplicate_coordinates_reuse_existing_favorite()
    {
        var store = new WeatherFavoritesStore();
        WeatherLocationPolicy.TryAdd(store, "Old label", 12.3, 45.6, false, out var first);

        Assert.True(WeatherLocationPolicy.TryAdd(store, "New label", 12.3, 45.6, false, out var duplicate));
        Assert.Single(store.Locations);
        Assert.Equal(first!.Id, duplicate!.Id);
        Assert.Equal("New label", duplicate.DisplayLabel);
    }

    [Fact]
    public void Normalize_preserves_active_location_when_store_exceeds_limit()
    {
        var store = new WeatherFavoritesStore
        {
            ActiveId = "active",
            Locations = Enumerable.Range(0, 6)
                .Select(index => new SavedWeatherLocation
                {
                    Id = index == 5 ? "active" : $"location-{index}",
                    DisplayLabel = $"City {index}",
                    Latitude = index + 1,
                    Longitude = index + 10
                })
                .ToList()
        };

        var normalized = WeatherLocationPolicy.Normalize(store);

        Assert.Equal(WeatherLocationPolicy.MaxLocations, normalized.Locations.Count);
        Assert.Contains(normalized.Locations, location => location.Id == "active");
        Assert.Equal("active", normalized.ActiveId);
    }

    [Fact]
    public void Favorites_reject_invalid_coordinates_even_with_a_label()
    {
        var store = new WeatherFavoritesStore();

        Assert.False(WeatherLocationPolicy.TryAdd(store, "Invalid", double.NaN, 20, false, out _));
        Assert.Empty(store.Locations);
    }
}
