namespace Task_Flyout.Services
{
    public enum ResponsiveLayoutMode
    {
        Narrow,
        Medium,
        Wide
    }

    internal static class ResponsiveLayoutPolicy
    {
        public const double MediumMinimumWidth = 720;
        public const double WideMinimumWidth = 1100;

        public static ResponsiveLayoutMode GetMode(double width)
            => width >= WideMinimumWidth
                ? ResponsiveLayoutMode.Wide
                : width >= MediumMinimumWidth
                    ? ResponsiveLayoutMode.Medium
                    : ResponsiveLayoutMode.Narrow;
    }
}
