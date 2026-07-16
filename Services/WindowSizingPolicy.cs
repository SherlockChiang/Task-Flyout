using System;

namespace Task_Flyout.Services
{
    internal readonly record struct WindowPhysicalSize(int Width, int Height, int Margin);

    internal static class WindowSizingPolicy
    {
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
