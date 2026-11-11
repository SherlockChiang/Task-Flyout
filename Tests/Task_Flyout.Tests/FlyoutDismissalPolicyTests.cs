using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class FlyoutDismissalPolicyTests
{
    [Theory]
    [InlineData(false, true, true, false, true, false, false)]
    [InlineData(false, true, true, false, true, true, true)]
    [InlineData(false, true, true, false, false, false, true)]
    [InlineData(false, true, true, true, false, true, false)]
    [InlineData(true, true, true, false, false, true, false)]
    [InlineData(false, false, true, false, false, true, false)]
    [InlineData(false, true, false, false, false, true, false)]
    public void ShouldDismissAfterOpening_preserves_handoff_but_remembers_completed_activation(
        bool isPinned,
        bool hideOnLostFocus,
        bool focusStateKnown,
        bool isFlyoutForeground,
        bool isOpeningForegroundStillActive,
        bool flyoutWasForegroundDuringOpening,
        bool expected)
    {
        Assert.Equal(expected, FlyoutDismissalPolicy.ShouldDismissAfterOpening(
            isPinned,
            hideOnLostFocus,
            focusStateKnown,
            isFlyoutForeground,
            isOpeningForegroundStillActive,
            flyoutWasForegroundDuringOpening));
    }
}
