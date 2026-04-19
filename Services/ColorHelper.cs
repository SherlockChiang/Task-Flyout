using Windows.UI;

namespace Task_Flyout.Services
{
    public static class ColorHelper
    {
        public static readonly string[] MonetPalette = new[]
        {
            "#D5A5A1", "#B38B8D", "#C3D1C6", "#9CB4A3", "#758A7A", "#E6D4B8",
            "#D2B88F", "#B49665", "#BBD0D9", "#92A6B9", "#6A7B92", "#B19FB6"
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
