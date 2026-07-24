using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class MailMoveCachePolicyTests
{
    [Fact]
    public void Source_and_destination_windows_are_invalidated_for_both_filters()
    {
        var keys = MailMoveCachePolicy.InvalidatedWindowKeys("account", "source", "destination");

        Assert.Equal(new[]
        {
            "account|source|False", "account|source|True",
            "account|destination|False", "account|destination|True"
        }, keys);
    }

    [Fact]
    public void Same_folder_is_deduplicated()
        => Assert.Equal(2, MailMoveCachePolicy.InvalidatedWindowKeys("account", "folder", "folder").Count);

    [Theory]
    [InlineData(3, false, 2, 4)]
    [InlineData(3, true, 3, 3)]
    [InlineData(0, false, 0, 1)]
    public void Unread_counts_follow_the_moved_message(int count, bool isRead, int expectedSource, int expectedDestination)
    {
        Assert.Equal(expectedSource, MailMoveCachePolicy.AdjustSourceUnreadCount(count, isRead));
        Assert.Equal(expectedDestination, MailMoveCachePolicy.AdjustDestinationUnreadCount(count, isRead));
    }

    [Fact]
    public void Unknown_unread_counts_remain_unknown()
    {
        Assert.Null(MailMoveCachePolicy.AdjustSourceUnreadCount(null, false));
        Assert.Null(MailMoveCachePolicy.AdjustDestinationUnreadCount(null, false));
    }
}
