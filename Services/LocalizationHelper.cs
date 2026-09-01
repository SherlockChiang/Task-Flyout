using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Windows.Storage;

namespace Task_Flyout.Services
{
    /// <summary>
    /// Resolves the <see cref="CultureInfo"/> that matches the app's in-app language
    /// choice (Settings → Language, stored as <c>AppLang</c> / applied via
    /// <c>PrimaryLanguageOverride</c>), independent of the OS regional format.
    ///
    /// Date, month and weekday formatting must use this culture instead of
    /// <see cref="CultureInfo.CurrentUICulture"/>: the latter follows the system
    /// locale, so a Chinese OS running the app in English would still render
    /// "2026年3月" / "周一". Mapped to specific cultures so DateTimeFormat
    /// (month/day names) is always available.
    /// </summary>
    internal static class LocalizationHelper
    {
        private const uint LcmapTraditionalChinese = 0x04000000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int LCMapStringEx(
            string localeName,
            uint mapFlags,
            string source,
            int sourceLength,
            StringBuilder destination,
            int destinationLength,
            IntPtr versionInformation,
            IntPtr reserved,
            IntPtr sortHandle);

        public static CultureInfo AppCulture
        {
            get
            {
                try
                {
                    string? lang = ApplicationData.Current.LocalSettings.Values["AppLang"] as string;
                    if (string.IsNullOrEmpty(lang))
                        lang = Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride;

                    if (string.IsNullOrEmpty(lang))
                        return CultureInfo.CurrentUICulture; // "System" — follow the OS.

                    if (lang.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                        return CultureInfo.GetCultureInfo("en-US");
                    if (IsTraditionalChineseLanguage(lang))
                        return CultureInfo.GetCultureInfo("zh-TW");
                    if (IsChineseLanguage(lang))
                        return CultureInfo.GetCultureInfo("zh-CN");

                    return CultureInfo.GetCultureInfo(lang);
                }
                catch
                {
                    return CultureInfo.CurrentUICulture;
                }
            }
        }

        public static string SupportedLanguageCode
            => GetSupportedLanguageCode(AppCulture.Name);

        internal static string GetSupportedLanguageCode(string? language)
        {
            if (IsTraditionalChineseLanguage(language)) return "zh-Hant";
            return IsChineseLanguage(language) ? "zh-Hans" : "en";
        }

        internal static bool IsChineseLanguage(string? language)
            => !string.IsNullOrWhiteSpace(language)
               && language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

        internal static bool IsTraditionalChineseLanguage(string? language)
        {
            if (string.IsNullOrWhiteSpace(language)) return false;

            return language.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)
                   || language.StartsWith("zh-CHT", StringComparison.OrdinalIgnoreCase)
                   || language.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase)
                   || language.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase)
                   || language.StartsWith("zh-MO", StringComparison.OrdinalIgnoreCase);
        }

        internal static string LocalizeExternalText(string? value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? "";
            return IsTraditionalChineseLanguage(AppCulture.Name)
                ? ConvertToTraditionalChinese(value)
                : value;
        }

        internal static string ConvertToTraditionalChinese(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Length == 0) return value;

            try
            {
                var destination = new StringBuilder(value.Length * 2 + 1);
                int length = LCMapStringEx(
                    "zh-TW",
                    LcmapTraditionalChinese,
                    value,
                    value.Length,
                    destination,
                    destination.Capacity,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero);
                return length > 0 ? destination.ToString(0, length) : value;
            }
            catch
            {
                return value;
            }
        }

        internal static int GetDayOffset(DayOfWeek day, DayOfWeek firstDayOfWeek)
            => ((int)day - (int)firstDayOfWeek + 7) % 7;

        internal static DateTime GetWeekStart(DateTime date, DayOfWeek firstDayOfWeek)
            => date.Date.AddDays(-GetDayOffset(date.DayOfWeek, firstDayOfWeek));
    }
}
