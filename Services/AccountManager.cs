using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Task_Flyout.Models;

namespace Task_Flyout.Services
{
    public class AccountManager
    {
        private static readonly string _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TaskFlyout", "connected_accounts.json");

        public ObservableCollection<ConnectedAccountInfo> Accounts { get; } = new();

        public void Load()
        {
            Accounts.Clear();

            if (File.Exists(_filePath))
            {
                try
                {
                    var json = File.ReadAllText(_filePath);
                    var list = JsonSerializer.Deserialize<List<ConnectedAccountInfo>>(json);
                    if (list != null)
                        foreach (var a in list) Accounts.Add(a);
                }
                catch { }
            }
            else
            {
                MigrateFromLocalSettings();
            }
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
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath));
            var json = JsonSerializer.Serialize(Accounts, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);

            SyncToLegacySettings();
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

        public ConnectedAccountInfo GetAccount(string providerName)
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
    }
}
