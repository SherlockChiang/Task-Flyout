using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class FlyoutDismissalPolicyTests
{
    [Theory]
    [InlineData(false, true, true, false, false, true)]
    [InlineData(false, true, true, false, true, false)]
    [InlineData(false, true, true, true, false, false)]
    [InlineData(true, true, true, false, false, false)]
    [InlineData(false, false, true, false, false, false)]
    [InlineData(false, true, false, false, false, false)]
    public void ShouldDismissAfterOpening_distinguishes_activation_handoff_from_focus_loss(
        bool isPinned,
        bool hideOnLostFocus,
        bool focusStateKnown,
        bool isFlyoutForeground,
        bool isOpeningForegroundStillActive,
        bool expected)
    {
        Assert.Equal(expected, FlyoutDismissalPolicy.ShouldDismissAfterOpening(
            isPinned,
            hideOnLostFocus,
            focusStateKnown,
            isFlyoutForeground,
            isOpeningForegroundStillActive));
    }
}
