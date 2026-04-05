using Windows.UI;

namespace Task_Flyout.Services
{
    public static class ColorHelper
    {
        public static readonly string[] MonetPalette = new[]
        {
            "#4285F4", // Blue
            "#EA4335", // Red
            "#34A853", // Green
            "#FBBC05", // Amber
            "#FF6D01", // Orange
            "#46BDC6", // Teal
            "#7B1FA2", // Purple
            "#F06292", // Pink
            "#0097A7", // Cyan
            "#689F38", // Light Green
            "#5C6BC0", // Indigo
            "#8D6E63", // Brown
            "#00ACC1", // Dark Cyan
            "#C0CA33", // Lime
            "#AB47BC", // Medium Purple
            "#FF8A65", // Light Orange
        };

        public static Color ParseHex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.FromArgb(255, 150, 150, 150);

            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                return Color.FromArgb(255, r, g, b);
            }
            return Color.FromArgb(255, 150, 150, 150);
        }

        public static double GetLuminance(Color color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            r = r <= 0.03928 ? r / 12.92 : System.Math.Pow((r + 0.055) / 1.055, 2.4);
            g = g <= 0.03928 ? g / 12.92 : System.Math.Pow((g + 0.055) / 1.055, 2.4);
            b = b <= 0.03928 ? b / 12.92 : System.Math.Pow((b + 0.055) / 1.055, 2.4);

            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

        public static bool ShouldUseWhiteText(Color backgroundColor)
        {
            return GetLuminance(backgroundColor) < 0.4;
        }

        public static bool ShouldUseWhiteText(string hex)
        {
            return ShouldUseWhiteText(ParseHex(hex));
        }

        public static string GetDefaultColorForIndex(int index)
        {
            return MonetPalette[index % MonetPalette.Length];
        }
    }
}
