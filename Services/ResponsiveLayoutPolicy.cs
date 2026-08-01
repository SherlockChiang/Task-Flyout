using System;

namespace Task_Flyout.Services
{
    internal readonly record struct ResponsivePopupMetrics(
        double ContentWidth,
        double MaxContentHeight,
        int PaletteColumns);

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
        public const double CalendarAgendaMinimumWidth = 820;
        public const double CalendarAgendaMinimumHeight = 560;
        public const double TasksWideMinimumWidth = 860;
        public const double SettingsMasonryMinimumWidth = 900;
        public const double ThreePaneMinimumWidth = 1120;
        public const double RssTwoPaneMinimumWidth = 720;

        public static ResponsiveLayoutMode GetMode(double width)
            => GetMode(width, WideMinimumWidth);

        public static ResponsiveLayoutMode GetCalendarMode(double width)
            => GetMode(width, ThreePaneMinimumWidth);

        public static ResponsiveLayoutMode GetMailMode(double width)
            => GetMode(width, ThreePaneMinimumWidth);

        public static bool ShouldAutoSelectFirstMail(double width)
            => GetMailMode(width) == ResponsiveLayoutMode.Wide;

        public static ResponsiveLayoutMode GetTasksMode(double width)
            => GetMode(width, TasksWideMinimumWidth);

        public static bool ShouldShowCalendarAgendaOnly(double width, double height)
            => !double.IsFinite(width)
               || !double.IsFinite(height)
               || width < CalendarAgendaMinimumWidth
               || height < CalendarAgendaMinimumHeight;

        public static bool ShouldUseSettingsMasonry(double width)
            => double.IsFinite(width) && width >= SettingsMasonryMinimumWidth;

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

        // Width and height are WinUI logical pixels (DIPs). XamlRoot has already applied
        // display scaling, so high-DPI windows naturally enter these compact tiers earlier.
        public static double GetPagePadding(double availableWidth, double availableHeight)
        {
            if (availableWidth < MediumMinimumWidth || availableHeight < 560)
                return 12;
            if (availableWidth < WideMinimumWidth || availableHeight < 720)
                return 20;
            return 28;
        }

        public static double GetPageSectionSpacing(double availableWidth, double availableHeight)
            => GetPagePadding(availableWidth, availableHeight) switch
            {
                <= 12 => 16,
                <= 20 => 22,
                _ => 28
            };

        public static ResponsivePopupMetrics GetColorPickerPopupMetrics(
            double availableWidth,
            double availableHeight)
        {
            availableWidth = NormalizeLogicalLength(availableWidth, 640);
            availableHeight = NormalizeLogicalLength(availableHeight, 720);
            double contentWidth = Math.Clamp(availableWidth - 48, 280, 420);
            double maxContentHeight = Math.Clamp(availableHeight - 64, 180, 620);
            int paletteColumns = contentWidth < 360 ? 4 : 6;
            return new ResponsivePopupMetrics(contentWidth, maxContentHeight, paletteColumns);
        }

        public static double GetWeatherBarMaximumWidth(double taskbarLogicalWidth)
            => Math.Min(320, Math.Max(0, taskbarLogicalWidth * 0.32));

        public static int GetWeatherBarPhysicalHeight(int detectedWidgetHeight, int taskbarHeight)
        {
            taskbarHeight = Math.Max(1, taskbarHeight);
            if (detectedWidgetHeight > 0)
                return Math.Clamp(detectedWidgetHeight, 1, taskbarHeight);
            return taskbarHeight;
        }

        private static double NormalizeLogicalLength(double value, double fallback)
            => double.IsFinite(value) && value > 0 ? value : fallback;
    }
}
