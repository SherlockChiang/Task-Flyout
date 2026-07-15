using System;
namespace Task_Flyout.Services
{
    [Flags]
    internal enum NotificationActionMask { None = 0, Open = 1, Snooze = 2, Complete = 4 }

    internal static class NotificationActionPolicy
    {
        public static NotificationActionMask GetActions(bool isTask, bool isCompleted)
        {
            if (isTask)
                return isCompleted ? NotificationActionMask.Open : NotificationActionMask.Open | NotificationActionMask.Snooze | NotificationActionMask.Complete;
            return NotificationActionMask.Open | NotificationActionMask.Snooze;
        }

        public static DateTime? GetReminderTime(bool isTask, bool isCompleted, DateTime? startDateTime, string? subtitle, string dateKey)
        {
            if (isTask)
            {
                if (isCompleted || !DateTime.TryParse(dateKey, out var dueDate)) return null;
                return dueDate.Date.AddHours(9);
            }
            if (startDateTime.HasValue) return startDateTime.Value;
            if (!DateTime.TryParse(dateKey, out var date)) return null;
            subtitle = subtitle?.Trim();
            if (string.IsNullOrEmpty(subtitle) || subtitle is "全天" or "All day") return null;
            var timePart = subtitle.Split('-')[0].Trim();
            return TimeSpan.TryParse(timePart, out var time) ? date.Add(time) : null;
        }
    }
}
