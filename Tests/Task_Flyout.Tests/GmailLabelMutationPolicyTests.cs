using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class GmailLabelMutationPolicyTests
{
    [Fact]
    public void Destinations_include_only_distinct_non_current_user_labels()
    {
        var labels = new[]
        {
            new GmailLabelDestination { Id = "INBOX", DisplayName = "Inbox" },
            new GmailLabelDestination { Id = "a", DisplayName = "Projects/2026", IsUserLabel = true },
            new GmailLabelDestination { Id = "a", DisplayName = "Duplicate", IsUserLabel = true },
            new GmailLabelDestination { Id = "current", DisplayName = "Current", IsUserLabel = true }
        };

        var result = GmailLabelMutationPolicy.Destinations(labels, "current");

        Assert.Single(result);
        Assert.Equal("a", result[0].Id);
    }

    [Theory]
    [InlineData("INBOX", false, true)]
    [InlineData("custom", true, true)]
    [InlineData("SENT", false, false)]
    [InlineData("TRASH", false, false)]
    [InlineData("STARRED", false, false)]
    public void Source_removal_is_limited_to_inbox_and_user_labels(string id, bool isUser, bool expected)
        => Assert.Equal(expected, GmailLabelMutationPolicy.CanRemoveSource(id, isUser));

    [Fact]
    public void Actual_delta_preserves_preexisting_destination_on_undo()
    {
        var before = new[] { "INBOX", "destination" };

        Assert.Empty(GmailLabelMutationPolicy.ActualAddedLabels(before, new[] { "destination" }));
        Assert.Equal(new[] { "INBOX" }, GmailLabelMutationPolicy.ActualRemovedLabels(before, new[] { "INBOX" }));
    }

    [Fact]
    public void Invalidated_keys_cover_both_windows_and_deduplicate_labels()
    {
        var keys = GmailLabelMutationPolicy.InvalidatedWindowKeys("account", new[] { "INBOX", "INBOX", "custom" });

        Assert.Equal(4, keys.Count);
        Assert.Contains("account|INBOX|False", keys);
        Assert.Contains("account|INBOX|True", keys);
        Assert.Contains("account|custom|False", keys);
        Assert.Contains("account|custom|True", keys);
    }

    [Theory]
    [InlineData(2, false, true, false, 1)]
    [InlineData(0, false, true, false, 0)]
    [InlineData(2, false, false, true, 3)]
    [InlineData(2, true, true, false, 2)]
    public void Unread_count_tracks_membership(int count, bool isRead, bool before, bool after, int expected)
        => Assert.Equal(expected, GmailLabelMutationPolicy.AdjustUnreadCount(count, isRead, before, after));

    [Fact]
    public void Unknown_unread_count_stays_unknown()
        => Assert.Null(GmailLabelMutationPolicy.AdjustUnreadCount(null, false, true, false));
}
