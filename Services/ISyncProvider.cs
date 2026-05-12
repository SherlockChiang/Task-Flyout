using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Task_Flyout.Models;

namespace Task_Flyout.Services
{
    public enum EventRecurrenceKind
    {
        None,
        Daily,
        Weekly,
        Monthly,
        Yearly
    }

    public enum RecurringDeleteMode
    {
        Single,
        ThisAndFollowing,
        All
    }

    public interface ISyncProvider
    {
        string ProviderName { get; }
        Task EnsureAuthorizedAsync();
        Task<List<AgendaItem>> FetchDataAsync(DateTime min, DateTime max);
        Task<List<SubscribedCalendarInfo>> FetchCalendarListAsync();
        Task UpdateTaskStatusAsync(string taskId, bool isCompleted);
        Task UpdateItemAsync(string itemId, bool isEvent, string title, string location, string description, DateTime targetDate, TimeSpan? startTime, TimeSpan? endTime);

        Task CreateEventAsync(string title, DateTime targetDate, TimeSpan startTime, TimeSpan endTime, string location, bool isAllDay, EventRecurrenceKind recurrence = EventRecurrenceKind.None);

        Task CreateTaskAsync(string title, DateTime targetDate, TimeSpan startTime, bool isAllDay);
        Task DeleteItemAsync(string itemId, bool isEvent, RecurringDeleteMode recurringDeleteMode = RecurringDeleteMode.Single, DateTime? occurrenceDate = null, string recurringEventId = "");
    }
}
