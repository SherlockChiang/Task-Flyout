using System;

namespace Task_Flyout.Services
{
    internal readonly record struct MappedSyncEvent(
        string Id,
        string Title,
        string Subtitle,
        string Location,
        string Description,
        string Provider,
        string CalendarId,
        string CalendarName,
        string DateKey,
        DateTime? StartDateTime,
        DateTime? EndDateTime,
        bool IsRecurring,
        string RecurringEventId,
        string RecurrenceKind);

    internal readonly record struct MappedSyncTask(
        string Id,
        string Title,
        string Subtitle,
        string Description,
        string Provider,
        string CalendarId,
        string CalendarName,
        string DateKey,
        bool IsCompleted);

    internal static class SyncItemMappingPolicy
    {
        public static MappedSyncEvent MapEvent(
            string? id,
            string? title,
            string? location,
            string? description,
            string provider,
            string? calendarId,
            string? calendarName,
            DateTime date,
            DateTime? startDateTime,
            DateTime? endDateTime,
            bool isAllDay,
            string allDayText,
            string? recurringEventId,
            bool hasRecurrence,
            string? recurrenceKind)
        {
            var recurringId = recurringEventId ?? string.Empty;
            return new MappedSyncEvent(
                id ?? string.Empty,
                title ?? string.Empty,
                isAllDay ? allDayText : date.ToString("HH:mm"),
                location ?? string.Empty,
                description ?? string.Empty,
                provider,
                calendarId ?? string.Empty,
                calendarName ?? string.Empty,
                date.ToString("yyyy-MM-dd"),
                startDateTime,
                endDateTime,
                hasRecurrence || !string.IsNullOrWhiteSpace(recurringId),
                recurringId,
                string.IsNullOrWhiteSpace(recurrenceKind) ? "None" : recurrenceKind);
        }

        public static MappedSyncTask MapTask(
            string? id,
            string? title,
            string taskText,
            string? description,
            string provider,
            string? calendarId,
            string? calendarName,
            DateTime date,
            bool isCompleted)
            => new(
                id ?? string.Empty,
                title ?? string.Empty,
                taskText,
                description ?? string.Empty,
                provider,
                calendarId ?? string.Empty,
                calendarName ?? string.Empty,
                date.ToString("yyyy-MM-dd"),
                isCompleted);
    }
}
