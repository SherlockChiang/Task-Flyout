using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Task_Flyout.Models;

namespace Task_Flyout.Services
{
    public class AccountManager
    {
        private const string StoreScope = "calendar";
        private const string AccountsKey = "connected_accounts";
        private readonly object _saveQueueLock = new();
        private Task _saveQueue = Task.CompletedTask;

        public ObservableCollection<ConnectedAccountInfo> Accounts { get; } = new();

        public void Load()
        {
            Accounts.Clear();

            var json = LocalSqliteStore.ReadProtectedText(StoreScope, AccountsKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var list = JsonFallbackPolicy.DeserializeOrDefault(
                    json,
                    value => JsonSerializer.Deserialize(value, AppJsonContext.Default.ListConnectedAccountInfo),
                    () => new List<ConnectedAccountInfo>());
                foreach (var a in list) Accounts.Add(a);
            }
            else
            {
                MigrateFromLocalSettings();
            }

            EnsureDefaultColors();
        }

        private void MigrateFromLocalSettings()
        {
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
            bool isGoogle = settings.Values["IsGoogleConnected"] as bool? ?? false;
            bool isMs = settings.Values["IsMSConnected"] as bool? ?? false;

            if (isGoogle)
            {
                Accounts.Add(new ConnectedAccountInfo
                {
                    ProviderName = "Google",
                    ShowEvents = settings.Values["ShowGoogleEvents"] as bool? ?? true,
                    ShowTasks = settings.Values["ShowGoogleTasks"] as bool? ?? true
                });
            }
            if (isMs)
            {
                Accounts.Add(new ConnectedAccountInfo
                {
                    ProviderName = "Microsoft",
                    ShowEvents = settings.Values["ShowMSEvents"] as bool? ?? true,
                    ShowTasks = settings.Values["ShowMSTasks"] as bool? ?? true
                });
            }

            if (Accounts.Count > 0) Save();
        }

        public void Save()
        {
            var json = JsonSerializer.Serialize(Accounts.ToList(), AppJsonContext.Default.ListConnectedAccountInfo);
            SyncToLegacySettings();
            QueueProtectedStoreWrite(json);
        }

        private void QueueProtectedStoreWrite(string json)
        {
            lock (_saveQueueLock)
            {
                _saveQueue = _saveQueue.ContinueWith(
                    _ =>
                    {
                        try
                        {
                            LocalSqliteStore.WriteProtectedText(StoreScope, AccountsKey, json);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Account save failed: {ex.Message}");
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
            }
        }

        public Task FlushPendingSavesAsync()
        {
            lock (_saveQueueLock)
                return _saveQueue;
        }

        private void SyncToLegacySettings()
        {
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
            var google = GetAccount("Google");
            var ms = GetAccount("Microsoft");

            settings.Values["IsGoogleConnected"] = google != null;
            settings.Values["IsMSConnected"] = ms != null;

            settings.Values["ShowGoogleEvents"] = google?.ShowEvents ?? true;
            settings.Values["ShowGoogleTasks"] = google?.ShowTasks ?? true;
            settings.Values["ShowMSEvents"] = ms?.ShowEvents ?? true;
            settings.Values["ShowMSTasks"] = ms?.ShowTasks ?? true;
        }

        public ConnectedAccountInfo? GetAccount(string providerName)
            => Accounts.FirstOrDefault(a => a.ProviderName == providerName);

        public bool IsConnected(string providerName)
            => Accounts.Any(a => a.ProviderName == providerName);

        public void AddAccount(ConnectedAccountInfo account)
        {
            Accounts.Add(account);
            Save();
        }

        public void RemoveAccount(string providerName)
        {
            var account = GetAccount(providerName);
            if (account != null)
            {
                Accounts.Remove(account);
                Save();
            }
        }

        public List<string> GetVisibleCalendarIds(string providerName)
        {
            var account = GetAccount(providerName);
            if (account == null) return new List<string>();
            return account.Calendars.Where(c => c.IsVisible).Select(c => c.Id).ToList();
        }

        public bool IsItemVisible(AgendaItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Provider)) return true;

            var account = GetAccount(item.Provider);
            if (account == null) return false;

            if (item.IsTask) return account.ShowTasks;

            if (item.IsEvent)
            {
                if (!string.IsNullOrEmpty(item.CalendarId) && account.Calendars.Count > 0)
                {
                    var cal = account.Calendars.FirstOrDefault(c => c.Id == item.CalendarId);
                    if (cal != null)
                    {
                        return cal.IsVisible;
                    }
                }
            }

            return true;
        }

        public string? GetColorForItem(AgendaItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Provider)) return null;

            var account = GetAccount(item.Provider);
            if (account == null) return null;

            if (item.IsTask && !string.IsNullOrEmpty(account.TaskColorHex))
                return account.TaskColorHex;

            if (item.IsEvent && !string.IsNullOrEmpty(item.CalendarId) && account.Calendars.Count > 0)
            {
                var cal = account.Calendars.FirstOrDefault(c => c.Id == item.CalendarId);
                if (cal?.ColorHex != null) return cal.ColorHex;
            }

            return null;
        }

        public void EnsureDefaultColors()
        {
            int colorIndex = 0;
            foreach (var account in Accounts)
            {
                foreach (var cal in account.Calendars)
                {
                    if (string.IsNullOrEmpty(cal.ColorHex))
                    {
                        cal.ColorHex = ColorHelper.GetDefaultColorForIndex(colorIndex);
                    }
                    colorIndex++;
                }
                if (string.IsNullOrEmpty(account.TaskColorHex))
                {
                    account.TaskColorHex = ColorHelper.GetDefaultColorForIndex(colorIndex);
                    colorIndex++;
                }
            }
        }

        public void PopulateItemColor(AgendaItem item)
        {
            var color = GetColorForItem(item);
            if (color != null) item.ColorHex = color;
        }
    }
}
