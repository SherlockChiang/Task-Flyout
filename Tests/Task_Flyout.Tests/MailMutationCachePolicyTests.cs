using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class MailMutationCachePolicyTests
{
    [Fact]
    public void Applies_only_matching_item_and_kind()
    {
        var target = new MailItem { AccountId = "a", FolderId = "f", Id = "m" };
        var other = new MailItem { AccountId = "a", FolderId = "f", Id = "other" };

        Assert.True(MailMutationCachePolicy.Apply(new[] { target, other }, "a", "f", "m", MailMutationKind.SetFlagged, true));
        Assert.True(target.IsFlagged);
        Assert.False(other.IsFlagged);
        Assert.False(target.IsRead);
    }

    [Theory]
    [InlineData(3, false, true, 2)]
    [InlineData(3, true, false, 4)]
    [InlineData(0, false, true, 0)]
    public void Unread_count_is_adjusted_safely(int count, bool before, bool after, int expected)
        => Assert.Equal(expected, MailMutationCachePolicy.AdjustUnreadCount(count, before, after));
}
