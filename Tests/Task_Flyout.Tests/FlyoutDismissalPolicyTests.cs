using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public sealed class FlyoutDismissalPolicyTests
{
    [Theory]
    [InlineData(false, true, true, false, true)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(false, false, true, false, false)]
    [InlineData(false, true, false, false, false)]
    public void ShouldDismissAfterOpening_respects_pin_and_known_focus_state(
        bool isPinned,
        bool hideOnLostFocus,
        bool focusStateKnown,
        bool isFlyoutForeground,
        bool expected)
    {
        Assert.Equal(expected, FlyoutDismissalPolicy.ShouldDismissAfterOpening(
            isPinned,
            hideOnLostFocus,
            focusStateKnown,
            isFlyoutForeground));
    }
}
