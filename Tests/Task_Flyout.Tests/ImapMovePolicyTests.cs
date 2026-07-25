using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class ImapMovePolicyTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void Safe_move_requires_native_move_and_uidplus(bool move, bool uidPlus, bool expected)
        => Assert.Equal(expected, ImapMovePolicy.SupportsSafeMove(move, uidPlus));

    [Theory]
    [InlineData("Projects/2026", "Inbox", false, false, true)]
    [InlineData("Inbox", "Inbox", false, false, false)]
    [InlineData("Projects", "Inbox", true, false, false)]
    [InlineData("Projects", "Inbox", false, true, false)]
    [InlineData("", "Inbox", false, false, false)]
    public void Destination_must_be_distinct_selectable_and_existing(
        string fullName, string current, bool noSelect, bool nonExistent, bool expected)
        => Assert.Equal(expected, ImapMovePolicy.IsSelectableDestination(fullName, current, noSelect, nonExistent));

    [Theory]
    [InlineData(10u, 42u, true)]
    [InlineData(0u, 42u, false)]
    [InlineData(10u, 0u, false)]
    public void Undo_requires_authoritative_target_uid_and_validity(uint uid, uint validity, bool expected)
        => Assert.Equal(expected, ImapMovePolicy.HasAuthoritativeIdentity(uid, validity));
}
