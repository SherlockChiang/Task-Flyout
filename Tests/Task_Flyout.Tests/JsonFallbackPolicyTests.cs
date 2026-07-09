using System.Text.Json;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class JsonFallbackPolicyTests
{
    private sealed class Sample
    {
        public string Value { get; set; } = "default";
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DeserializeOrDefault_returns_default_for_empty_input(string? json)
    {
        var result = JsonFallbackPolicy.DeserializeOrDefault(
            json,
            value => JsonSerializer.Deserialize<Sample>(value),
            () => new Sample());

        Assert.Equal("default", result.Value);
    }

    [Fact]
    public void DeserializeOrDefault_returns_default_for_malformed_json()
    {
        var result = JsonFallbackPolicy.DeserializeOrDefault(
            "{not-json",
            value => JsonSerializer.Deserialize<Sample>(value),
            () => new Sample());

        Assert.Equal("default", result.Value);
    }

    [Fact]
    public void DeserializeOrDefault_returns_default_for_null_deserialize_result()
    {
        var result = JsonFallbackPolicy.DeserializeOrDefault<Sample>(
            "null",
            value => JsonSerializer.Deserialize<Sample>(value),
            () => new Sample());

        Assert.Equal("default", result.Value);
    }

    [Fact]
    public void DeserializeOrDefault_returns_deserialized_value()
    {
        var result = JsonFallbackPolicy.DeserializeOrDefault(
            "{\"Value\":\"loaded\"}",
            value => JsonSerializer.Deserialize<Sample>(value),
            () => new Sample());

        Assert.Equal("loaded", result.Value);
    }
}
