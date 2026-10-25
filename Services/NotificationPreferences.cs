using System;
using Windows.Storage;

namespace Task_Flyout.Services
{
    internal static class NotificationPreferences
    {
        public static int SnoozeMinutes => NotificationDeliveryPolicy.SnoozeMinutes(
            ApplicationData.Current.LocalSettings.Values["NotificationSnoozeMinutes"] as int? ?? 10);
        public static bool HighPriorityOnly => ApplicationData.Current.LocalSettings.Values["NotificationHighPriorityOnly"] as bool? ?? false;
        public static bool IsQuietNow => NotificationDeliveryPolicy.IsQuiet(
            ApplicationData.Current.LocalSettings.Values["NotificationQuietEnabled"] as bool? ?? false,
            ApplicationData.Current.LocalSettings.Values["NotificationQuietStart"] as int? ?? 1320,
            ApplicationData.Current.LocalSettings.Values["NotificationQuietEnd"] as int? ?? 480,
            DateTime.Now.TimeOfDay);
    }
}
