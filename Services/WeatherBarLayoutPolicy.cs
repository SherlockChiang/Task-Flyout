using System;
using System.Collections.Generic;
using System.Linq;

namespace Task_Flyout.Services
{
    internal enum WeatherBarOptionalField
    {
        Description,
        Location,
        FeelsLike,
        Humidity,
        Wind
    }

    internal readonly record struct WeatherBarFieldRequest(
        WeatherBarOptionalField Field,
        bool Requested,
        double NaturalWidth,
        double MinimumWidth);

    internal readonly record struct WeatherBarLayoutPlan(
        bool ShouldShow,
        double Width,
        double DescriptionWidth,
        double LocationWidth,
        double FeelsLikeWidth,
        double HumidityWidth,
        double WindWidth)
    {
        public double GetWidth(WeatherBarOptionalField field) => field switch
        {
            WeatherBarOptionalField.Description => DescriptionWidth,
            WeatherBarOptionalField.Location => LocationWidth,
            WeatherBarOptionalField.FeelsLike => FeelsLikeWidth,
            WeatherBarOptionalField.Humidity => HumidityWidth,
            _ => WindWidth
        };
    }

    internal static class WeatherBarLayoutPolicy
    {
        public static double GetAvailableWidth(
            double taskbarWidth,
            double occupiedOffset,
            double rightBoundary,
            double leftGap,
            double rightGap)
        {
            taskbarWidth = Math.Max(0, taskbarWidth);
            occupiedOffset = Math.Clamp(occupiedOffset, 0, taskbarWidth);
            rightBoundary = rightBoundary > 0
                ? Math.Clamp(rightBoundary, 0, taskbarWidth)
                : taskbarWidth;
            leftGap = Math.Max(0, leftGap);
            rightGap = Math.Max(0, rightGap);
            double start = occupiedOffset + (occupiedOffset > 0 ? leftGap : 0);
            return Math.Max(0, rightBoundary - rightGap - start);
        }

        public static WeatherBarLayoutPlan Compute(
            double maximumWidth,
            double horizontalMargins,
            double itemSpacing,
            double iconWidth,
            double temperatureWidth,
            IReadOnlyList<WeatherBarFieldRequest> optionalFields)
        {
            maximumWidth = Math.Max(0, maximumWidth);
            horizontalMargins = Math.Max(0, horizontalMargins);
            itemSpacing = Math.Max(0, itemSpacing);
            iconWidth = Math.Max(0, iconWidth);
            temperatureWidth = Math.Max(0, temperatureWidth);

            var widths = new Dictionary<WeatherBarOptionalField, double>();
            foreach (var field in optionalFields)
            {
                widths[field.Field] = field.Requested
                    ? Math.Max(0, field.NaturalWidth)
                    : 0;
            }

            double DesiredWidth()
            {
                int itemCount = (iconWidth > 0 ? 1 : 0)
                    + (temperatureWidth > 0 ? 1 : 0)
                    + widths.Values.Count(width => width > 0);
                if (itemCount == 0) return 0;
                return horizontalMargins + iconWidth + temperatureWidth
                    + widths.Values.Sum() + itemSpacing * (itemCount - 1);
            }

            double essentialWidth = horizontalMargins + iconWidth + temperatureWidth;
            int essentialCount = (iconWidth > 0 ? 1 : 0) + (temperatureWidth > 0 ? 1 : 0);
            if (essentialCount > 1)
                essentialWidth += itemSpacing * (essentialCount - 1);
            if (essentialCount == 0 && widths.Values.All(width => width <= 0)
                || essentialWidth > maximumWidth)
                return default;

            // Lower-value fields yield width first, then disappear in the same order.
            var removalOrder = new[]
            {
                WeatherBarOptionalField.Wind,
                WeatherBarOptionalField.Humidity,
                WeatherBarOptionalField.FeelsLike,
                WeatherBarOptionalField.Location,
                WeatherBarOptionalField.Description
            };
            var minimums = new Dictionary<WeatherBarOptionalField, double>();
            foreach (var field in optionalFields)
                minimums[field.Field] = Math.Max(0, Math.Min(field.NaturalWidth, field.MinimumWidth));

            foreach (var field in removalOrder)
            {
                if (DesiredWidth() <= maximumWidth) break;
                if (!widths.TryGetValue(field, out double width) || width <= 0) continue;
                double minimum = minimums.GetValueOrDefault(field);
                double reduction = Math.Min(width - minimum, DesiredWidth() - maximumWidth);
                widths[field] = width - Math.Max(0, reduction);
            }

            foreach (var field in removalOrder)
            {
                if (DesiredWidth() <= maximumWidth) break;
                if (widths.GetValueOrDefault(field) > 0)
                    widths[field] = 0;
            }

            double desiredWidth = DesiredWidth();
            if (desiredWidth <= 0 || desiredWidth > maximumWidth)
                return default;

            return new WeatherBarLayoutPlan(
                true,
                desiredWidth,
                widths.GetValueOrDefault(WeatherBarOptionalField.Description),
                widths.GetValueOrDefault(WeatherBarOptionalField.Location),
                widths.GetValueOrDefault(WeatherBarOptionalField.FeelsLike),
                widths.GetValueOrDefault(WeatherBarOptionalField.Humidity),
                widths.GetValueOrDefault(WeatherBarOptionalField.Wind));
        }
    }
}
