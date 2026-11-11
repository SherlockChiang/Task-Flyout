namespace Task_Flyout.Services
{
    internal static class FlyoutDismissalPolicy
    {
        public static bool ShouldDismissAfterOpening(
            bool isPinned,
            bool hideOnLostFocus,
            bool focusStateKnown,
            bool isFlyoutForeground,
            bool isOpeningForegroundStillActive,
            bool flyoutWasForegroundDuringOpening)
            => !isPinned &&
               hideOnLostFocus &&
               focusStateKnown &&
               !isFlyoutForeground &&
               (flyoutWasForegroundDuringOpening || !isOpeningForegroundStillActive);
    }
}
