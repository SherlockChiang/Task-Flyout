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
        public AccountManager AccountManager { get; } = new AccountManager();

        public void RegisterProvider(ISyncProvider provider) => _providers.Add(provider);

        public async Task SyncAllCalendarsAsync()
        {
            var activeProviders = _providers.Where(p => AccountManager.IsConnected(p.ProviderName)).ToList();

            foreach (var provider in activeProviders)
            {
                try
                {
                    await provider.EnsureAuthorizedAsync();
                    var remoteCalendars = await provider.FetchCalendarListAsync();
                    var account = AccountManager.GetAccount(provider.ProviderName);

                    if (account != null && remoteCalendars != null && remoteCalendars.Count > 0)
                    {
                        bool changed = false;

                        foreach (var rCal in remoteCalendars)
                        {
                            var existing = account.Calendars.FirstOrDefault(c => c.Id == rCal.Id);
                            if (existing == null)
                            {
                                account.Calendars.Add(new SubscribedCalendarInfo { Id = rCal.Id, Name = rCal.Name, IsVisible = true });
                                changed = true;
                            }
                            else if (existing.Name != rCal.Name)
                            {
                                existing.Name = rCal.Name;
                                changed = true;
                            }
                        }

                        var toRemove = account.Calendars.Where(c => !remoteCalendars.Any(r => r.Id == c.Id)).ToList();
                        foreach (var c in toRemove)
                        {
                            account.Calendars.Remove(c);
                            changed = true;
                        }

                        if (changed)
                        {
                            AccountManager.EnsureDefaultColors();
                            AccountManager.Save();
                        }
                    }
                }
                catch { }
            }

            AccountManager.EnsureDefaultColors();
        }

        public async Task<List<AgendaItem>> GetAllDataAsync(DateTime min, DateTime max)
        {
            var allItems = new List<AgendaItem>();
            var activeProviders = _providers.Where(p => AccountManager.IsConnected(p.ProviderName)).ToList();

            var fetchTasks = activeProviders.Select(async provider =>
            {
                try
                {
                    await provider.EnsureAuthorizedAsync();
                    return await provider.FetchDataAsync(min, max);
                }
                catch { return new List<AgendaItem>(); }
            });

            var results = await Task.WhenAll(fetchTasks);
            foreach (var result in results)
            {
                if (result != null) allItems.AddRange(result);
            }

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

        public ISyncProvider GetProvider(string providerName)
            => _providers.FirstOrDefault(p => p.ProviderName == providerName);
    }
}