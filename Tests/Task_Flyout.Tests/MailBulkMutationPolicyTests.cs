using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class MailBulkMutationPolicyTests
{
    [Fact]
    public void Selection_is_bounded()
        => Assert.Equal(MailBulkMutationPolicy.MaximumItems, MailBulkMutationPolicy.SelectBounded(Enumerable.Range(0, 200)).Count);

    [Fact]
    public void Rollback_is_blocked_by_newer_intent()
    {
        Assert.True(MailBulkMutationPolicy.ShouldRollback(4, 4));
        Assert.False(MailBulkMutationPolicy.ShouldRollback(4, 5));
    }
}
