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
        public const double MediumMinimumWidth = 720;
        public const double WideMinimumWidth = 1100;
        public const double ComfortableTaskbarMinimumWidth = 1400;

        public static ResponsiveLayoutMode GetMode(double width)
            => width >= WideMinimumWidth
                ? ResponsiveLayoutMode.Wide
                : width >= MediumMinimumWidth
                    ? ResponsiveLayoutMode.Medium
                    : ResponsiveLayoutMode.Narrow;

        public static double GetFlyoutCalendarHeight(double availableHeight)
            => availableHeight < 520 ? 190 : availableHeight < 650 ? 250 : 354;

        public static double GetCalendarCellMinimumHeight(double availableHeight)
            => availableHeight < 420 ? 40 : 56;

        public static double GetWeatherBarMaximumWidth(double taskbarLogicalWidth)
            => Math.Clamp(taskbarLogicalWidth * 0.32, 80, 420);

        public static bool UseCompactWeatherBar(double taskbarLogicalWidth)
            => taskbarLogicalWidth < ComfortableTaskbarMinimumWidth;

        public static int GetWeatherBarPhysicalWidth(int detectedWidgetWidth, int fallbackWidth, int minimumWidth, int maximumWidth)
            => detectedWidgetWidth > 0
                ? detectedWidgetWidth
                : Math.Clamp(fallbackWidth, minimumWidth, maximumWidth);
    }
}
