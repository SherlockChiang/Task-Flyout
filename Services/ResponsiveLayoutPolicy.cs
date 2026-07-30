using System;

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
        public const double MediumMinimumWidth = 680;
        public const double WideMinimumWidth = 1040;
        public const double TasksWideMinimumWidth = 860;
        public const double ThreePaneMinimumWidth = 1120;
        public const double RssTwoPaneMinimumWidth = 720;

        public static ResponsiveLayoutMode GetMode(double width)
            => GetMode(width, WideMinimumWidth);

        public static ResponsiveLayoutMode GetCalendarMode(double width)
            => GetMode(width, ThreePaneMinimumWidth);

        public static ResponsiveLayoutMode GetMailMode(double width)
            => GetMode(width, ThreePaneMinimumWidth);

        public static ResponsiveLayoutMode GetTasksMode(double width)
            => GetMode(width, TasksWideMinimumWidth);

        private static ResponsiveLayoutMode GetMode(double width, double wideMinimumWidth)
            => width >= wideMinimumWidth
                ? ResponsiveLayoutMode.Wide
                : width >= MediumMinimumWidth
                    ? ResponsiveLayoutMode.Medium
                    : ResponsiveLayoutMode.Narrow;

        public static double GetFlyoutCalendarHeight(double availableHeight)
            => availableHeight < 520 ? 190 : availableHeight < 650 ? 250 : 354;

        public static double GetCalendarCellMinimumHeight(double availableHeight)
            => availableHeight < 420 ? 40 : 56;

        public static double GetWeatherBarMaximumWidth(double taskbarLogicalWidth)
            => Math.Min(320, Math.Max(0, taskbarLogicalWidth * 0.32));

        public static int GetWeatherBarPhysicalHeight(int detectedWidgetHeight, int taskbarHeight)
        {
            taskbarHeight = Math.Max(1, taskbarHeight);
            if (detectedWidgetHeight > 0)
                return Math.Clamp(detectedWidgetHeight, 1, taskbarHeight);
            return taskbarHeight;
        }
    }
}
