using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class SyncPaginationPolicyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetNextPageToken_stops_on_empty_next_token(string? nextToken)
    {
        var seen = new HashSet<string>();

        var result = SyncPaginationPolicy.GetNextPageToken("current", nextToken, seen);

        Assert.Null(result);
        Assert.Empty(seen);
    }

    [Fact]
    public void GetNextPageToken_returns_new_token_and_marks_it_seen()
    {
        var seen = new HashSet<string>();

        var result = SyncPaginationPolicy.GetNextPageToken(null, "page-1", seen);

        Assert.Equal("page-1", result);
        Assert.Contains("page-1", seen);
    }

    [Fact]
    public void GetNextPageToken_stops_when_next_token_repeats_current_token()
    {
        var seen = new HashSet<string> { "page-1" };

        var result = SyncPaginationPolicy.GetNextPageToken("page-1", "page-1", seen);

        Assert.Null(result);
    }

    [Fact]
    public void GetNextPageToken_stops_when_next_token_was_seen_before()
    {
        var seen = new HashSet<string> { "page-1", "page-2" };

        var result = SyncPaginationPolicy.GetNextPageToken("page-3", "page-1", seen);

        Assert.Null(result);
    }
}
