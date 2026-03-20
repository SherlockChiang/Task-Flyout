using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Task_Flyout.Models;

namespace Task_Flyout.Services
{
    public interface ISyncProvider
    {
        string ProviderName { get; }
        Task EnsureAuthorizedAsync();
        Task<List<AgendaItem>> FetchDataAsync(DateTime min, DateTime max);
        Task UpdateTaskStatusAsync(string taskId, bool isCompleted);
        Task UpdateItemAsync(string itemId, bool isEvent, string title, string location, string description, DateTime targetDate, TimeSpan? startTime, TimeSpan? endTime);

        Task CreateEventAsync(string title, DateTime targetDate, TimeSpan startTime, TimeSpan endTime, string location, bool isAllDay);

        Task CreateTaskAsync(string title, DateTime targetDate, TimeSpan startTime, bool isAllDay);
        Task DeleteItemAsync(string itemId, bool isEvent);
    }
}