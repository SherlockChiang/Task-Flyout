using System;

namespace Task_Flyout.Services
{
    internal readonly record struct WindowPhysicalSize(int Width, int Height, int Margin);

    internal static class WindowSizingPolicy
    {
        public static WindowPhysicalSize CalculateInitialMainWindow(int workAreaWidth, int workAreaHeight)
        {
            int maxWidth = Math.Max(1, Math.Min(1480, workAreaWidth));
            int maxHeight = Math.Max(1, Math.Min(920, workAreaHeight));
            int minWidth = Math.Max(1, Math.Min(1080, maxWidth));
            int minHeight = Math.Max(1, Math.Min(700, maxHeight));
            int width = Math.Clamp((int)Math.Round(workAreaWidth * 0.82), minWidth, maxWidth);
            int height = Math.Clamp((int)Math.Round(workAreaHeight * 0.84), minHeight, maxHeight);
            return new WindowPhysicalSize(width, height, 0);
        }

        public static WindowPhysicalSize Calculate(
            double desiredWidth,
            double desiredHeight,
            double scale,
            int workAreaWidth,
            int workAreaHeight,
            double margin)
        {
            scale = Math.Max(1, scale);
            int physicalMargin = (int)Math.Ceiling(Math.Max(0, margin) * scale);
            int availableWidth = Math.Max(1, workAreaWidth - physicalMargin * 2);
            int availableHeight = Math.Max(1, workAreaHeight - physicalMargin * 2);
            return new WindowPhysicalSize(
                Math.Min((int)Math.Ceiling(desiredWidth * scale), availableWidth),
                Math.Min((int)Math.Ceiling(desiredHeight * scale), availableHeight),
                physicalMargin);
        }
    }
}
