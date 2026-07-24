namespace Task_Flyout.Services
{
    internal static class TaskbarSelectionPolicy
    {
        public const string PrimaryClass = "Shell_TrayWnd";
        public const string SecondaryClass = "Shell_SecondaryTrayWnd";

        public static bool IsSupportedClass(string? className)
            => className is PrimaryClass or SecondaryClass;

        public static long Select(
            long currentHandle,
            bool currentIsValid,
            string? currentClass,
            long primaryHandle,
            long secondaryHandle)
        {
            if (currentHandle != 0 && currentIsValid && IsSupportedClass(currentClass))
                return currentHandle;

            return primaryHandle != 0 ? primaryHandle : secondaryHandle;
        }
    }
}
