using System;
using System.Globalization;
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
                    if (lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                        return CultureInfo.GetCultureInfo("zh-CN");

                    return CultureInfo.GetCultureInfo(lang);
                }
                catch
                {
                    return CultureInfo.CurrentUICulture;
                }
            }
        }
    }
}
