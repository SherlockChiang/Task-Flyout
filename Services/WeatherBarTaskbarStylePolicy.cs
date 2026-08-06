using System;
using System.Collections.Generic;
using System.Globalization;

namespace Task_Flyout.Services
{
    internal readonly record struct WeatherBarTaskbarStyleProfile(
        bool MatchesTaskbarSurface,
        double LeftInset,
        double RightInset,
        double DockHeight,
        double TopInset,
        double BottomInset,
        double CornerRadius)
    {
        public static WeatherBarTaskbarStyleProfile SystemDefault => default;
    }

    internal readonly record struct WeatherBarTaskbarSlot(
        bool IsThemed,
        int Left,
        int Right,
        int Top,
        int Height)
    {
        public int Width => Math.Max(0, Right - Left);
    }

    internal static class WeatherBarTaskbarStylePolicy
    {
        public const string LuminosityDockTheme = "Luminosity_variant_Dock";

        private const double DefaultDockMargin = 250;
        private const double DefaultDockMarginFix = 500;
        private const double DefaultDockHeight = 58;
        private const double DefaultTopInset = 5;
        private const double DefaultBottomInset = 5;
        private const double DefaultCornerRadius = 10;
        private const double MinimumUsableWidth = 92;

        public static WeatherBarTaskbarStyleProfile Resolve(
            bool modEnabled,
            string? theme,
            IEnumerable<string?>? styleConstants)
        {
            if (!modEnabled || !string.Equals(theme, LuminosityDockTheme, StringComparison.Ordinal))
                return WeatherBarTaskbarStyleProfile.SystemDefault;

            var values = ParseStyleConstants(styleConstants);
            double dockMargin = ReadBounded(values, "DockMargin", DefaultDockMargin, 0, 2048);
            double dockMarginFix = ReadBounded(values, "DockMarginFix", DefaultDockMarginFix, 0, 4096);
            double dockHeight = ReadBounded(values, "DockHeight", DefaultDockHeight, 32, 160);
            double topInset = ReadBounded(values, "DockTopGap", DefaultTopInset, 0, 48);
            double cornerRadius = ReadBounded(values, "bcr", DefaultCornerRadius, 0, 48);

            // Luminosity's Dock variant fixes the RootGrid bottom margin at 5 DIPs.
            // Reject combinations that would leave too little usable vertical space.
            if (dockHeight - topInset - DefaultBottomInset < 24)
            {
                dockHeight = DefaultDockHeight;
                topInset = DefaultTopInset;
            }

            return new WeatherBarTaskbarStyleProfile(
                MatchesTaskbarSurface: true,
                LeftInset: dockMargin,
                RightInset: dockMargin + dockMarginFix,
                DockHeight: dockHeight,
                TopInset: topInset,
                BottomInset: DefaultBottomInset,
                CornerRadius: cornerRadius);
        }

        public static WeatherBarTaskbarSlot GetSlot(
            WeatherBarTaskbarStyleProfile profile,
            int taskbarWidth,
            int taskbarHeight,
            double scaleFactor)
        {
            taskbarWidth = Math.Max(0, taskbarWidth);
            taskbarHeight = Math.Max(0, taskbarHeight);
            if (!profile.MatchesTaskbarSurface || taskbarWidth == 0 || taskbarHeight == 0 ||
                !double.IsFinite(scaleFactor) || scaleFactor <= 0)
            {
                return new WeatherBarTaskbarSlot(false, 0, taskbarWidth, 0, taskbarHeight);
            }

            double logicalWidth = taskbarWidth / scaleFactor;
            double availableForInsets = Math.Max(0, logicalWidth - MinimumUsableWidth);
            double requestedInsets = profile.LeftInset + profile.RightInset;
            double insetScale = requestedInsets > availableForInsets && requestedInsets > 0
                ? availableForInsets / requestedInsets
                : 1;
            int physicalLeftInset = Math.Max(0, (int)Math.Round(profile.LeftInset * insetScale * scaleFactor));
            int physicalRightInset = Math.Max(0, (int)Math.Round(profile.RightInset * insetScale * scaleFactor));
            if (physicalLeftInset + physicalRightInset >= taskbarWidth)
            {
                physicalLeftInset = 0;
                physicalRightInset = 0;
            }

            int dockHeight = Math.Clamp(
                (int)Math.Round(profile.DockHeight * scaleFactor),
                1,
                taskbarHeight);
            int dockTop = Math.Max(0, (taskbarHeight - dockHeight) / 2);
            int topInset = Math.Max(0, (int)Math.Round(profile.TopInset * scaleFactor));
            int bottomInset = Math.Max(0, (int)Math.Round(profile.BottomInset * scaleFactor));
            int availableHeight = Math.Max(1, dockHeight - topInset - bottomInset);
            int top = Math.Min(taskbarHeight - 1, dockTop + topInset);
            int height = Math.Clamp(availableHeight, 1, taskbarHeight - top);

            return new WeatherBarTaskbarSlot(
                true,
                physicalLeftInset,
                taskbarWidth - physicalRightInset,
                top,
                height);
        }

        private static Dictionary<string, double> ParseStyleConstants(IEnumerable<string?>? styleConstants)
        {
            var values = new Dictionary<string, double>(StringComparer.Ordinal);
            if (styleConstants == null)
                return values;

            foreach (string? raw in styleConstants)
            {
                // Windhawk reads numeric indexes in order and stops at the first
                // missing or empty entry, leaving any stale higher indexes unused.
                if (string.IsNullOrEmpty(raw)) break;
                if (string.IsNullOrWhiteSpace(raw)) continue;
                int separator = raw.IndexOf('=');
                if (separator <= 0 || separator == raw.Length - 1) continue;

                string name = raw[..separator].Trim();
                string text = raw[(separator + 1)..].Trim();
                if (name.Length == 0 || !double.TryParse(
                        text,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double value) || !double.IsFinite(value))
                {
                    continue;
                }

                values[name] = value;
            }

            return values;
        }

        private static double ReadBounded(
            IReadOnlyDictionary<string, double> values,
            string name,
            double fallback,
            double minimum,
            double maximum)
        {
            return values.TryGetValue(name, out double value) && value >= minimum && value <= maximum
                ? value
                : fallback;
        }
    }
}
