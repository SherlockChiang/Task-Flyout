using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Task_Flyout.Models;

namespace Task_Flyout.Services
{
    public class SyncManager
    {
        private readonly List<ISyncProvider> _providers = new();
        public IReadOnlyList<ISyncProvider> Providers => _providers;
        public void RegisterProvider(ISyncProvider provider) => _providers.Add(provider);

        public async Task<List<AgendaItem>> GetAllDataAsync(DateTime min, DateTime max)
        {
            var allItems = new List<AgendaItem>();
            var fetchTasks = _providers.Select(async provider =>
            {
                try
                {
                    await provider.EnsureAuthorizedAsync();
                    return await provider.FetchDataAsync(min, max);
                }
                catch { return new List<AgendaItem>(); }
            });

            var results = await Task.WhenAll(fetchTasks);
            foreach (var result in results) allItems.AddRange(result);

            return allItems;
        }

        public async Task UpdateTaskStatusAsync(string providerName, string taskId, bool isCompleted)
        {
            var provider = _providers.FirstOrDefault(p => p.ProviderName == providerName);
            if (provider != null) await provider.UpdateTaskStatusAsync(taskId, isCompleted);
        }

        public async Task UpdateItemAsync(string providerName, string itemId, bool isEvent, string title, string location, string description, DateTime targetDate, TimeSpan? startTime, TimeSpan? endTime)
        {
            var provider = _providers.FirstOrDefault(p => p.ProviderName == providerName);
            if (provider != null) await provider.UpdateItemAsync(itemId, isEvent, title, location, description, targetDate, startTime, endTime);
        }

        public async Task CreateItemAsync(string title, bool isEvent, bool isAllDay, DateTime targetDate, TimeSpan startTime, TimeSpan endTime, string location, string providerName = null)
        {
            var provider = providerName != null ? _providers.FirstOrDefault(p => p.ProviderName == providerName) : _providers.FirstOrDefault();
            if (provider == null) return;

            if (isEvent)
                await provider.CreateEventAsync(title, targetDate, startTime, endTime, location, isAllDay);
            else
                await provider.CreateTaskAsync(title, targetDate, startTime, isAllDay);
        }

        public async Task DeleteItemAsync(string providerName, string itemId, bool isEvent)
        {
            var provider = _providers.FirstOrDefault(p => p.ProviderName == providerName);
            if (provider != null)
            {
                await provider.DeleteItemAsync(itemId, isEvent);
            }
        }
    }
}