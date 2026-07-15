using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class LocalSearchMatcherTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_query_matches_everything(string? query)
        => Assert.True(LocalSearchMatcher.Matches(query, null, "value"));

    [Fact]
    public void Matches_any_field_case_insensitively()
        => Assert.True(LocalSearchMatcher.Matches("PROJECT", "sender", "Project update"));

    [Fact]
    public void Does_not_match_unrelated_fields()
        => Assert.False(LocalSearchMatcher.Matches("missing", "sender", "subject"));
}
