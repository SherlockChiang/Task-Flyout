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

            bool identitiesChanged = EnsureAccountIdentities();
            EnsureDefaultColors();
            if (identitiesChanged) Save();
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
                    AccountId = AccountIdentityPolicy.CreateLegacyAccountId("Google"),
                    ShowEvents = settings.Values["ShowGoogleEvents"] as bool? ?? true,
                    ShowTasks = settings.Values["ShowGoogleTasks"] as bool? ?? true
                });
            }
            if (isMs)
            {
                Accounts.Add(new ConnectedAccountInfo
                {
                    ProviderName = "Microsoft",
                    AccountId = AccountIdentityPolicy.CreateLegacyAccountId("Microsoft"),
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
            => Accounts.FirstOrDefault(a => string.Equals(
                ProviderAuthorizationLifecycle.NormalizeProviderName(a.ProviderName),
                ProviderAuthorizationLifecycle.NormalizeProviderName(providerName),
                StringComparison.OrdinalIgnoreCase));

        public ConnectedAccountInfo? GetAccount(string providerName, string? accountId)
        {
            if (string.IsNullOrWhiteSpace(accountId)) return GetAccount(providerName);
            return Accounts.FirstOrDefault(account => AccountIdentityPolicy.Matches(
                account.ProviderName,
                account.AccountId,
                providerName,
                accountId));
        }

        public ConnectedAccountInfo? GetAccountById(string? accountId)
            => string.IsNullOrWhiteSpace(accountId)
                ? null
                : Accounts.FirstOrDefault(account => string.Equals(account.AccountId, accountId, StringComparison.OrdinalIgnoreCase));

        public bool IsConnected(string providerName)
            => GetAccount(providerName) != null;

        public bool IsConnected(string providerName, string? accountId)
            => GetAccount(providerName, accountId) != null;

        public void AddAccount(ConnectedAccountInfo account)
        {
            account.ProviderName = ProviderAuthorizationLifecycle.NormalizeProviderName(account.ProviderName);
            if (string.IsNullOrWhiteSpace(account.AccountId))
            {
                account.AccountId = IsConnected(account.ProviderName)
                    ? AccountIdentityPolicy.CreateAccountId()
                    : AccountIdentityPolicy.CreateLegacyAccountId(account.ProviderName);
            }
            else
            {
                account.AccountId = account.AccountId.Trim();
            }

            if (GetAccountById(account.AccountId) != null)
                throw new InvalidOperationException("An account with the same identity is already connected.");

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

        public bool RemoveAccountById(string accountId)
        {
            var account = GetAccountById(accountId);
            if (account == null) return false;

            Accounts.Remove(account);
            Save();
            return true;
        }

        public List<string> GetVisibleCalendarIds(string providerName, string? accountId = null)
        {
            var account = GetAccount(providerName, accountId);
            if (account == null) return new List<string>();
            return account.Calendars.Where(c => c.IsVisible).Select(c => c.Id).ToList();
        }

        public bool IsItemVisible(AgendaItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Provider)) return true;

            var account = GetAccount(item.Provider, item.AccountId);
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

            var account = GetAccount(item.Provider, item.AccountId);
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
                if (SyncProviderCapabilityPolicy.ForProvider(account.ProviderName).SupportsTasks
                    && string.IsNullOrEmpty(account.TaskColorHex))
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

        private bool EnsureAccountIdentities()
        {
            bool changed = false;
            var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var account in Accounts)
            {
                string providerName = ProviderAuthorizationLifecycle.NormalizeProviderName(account.ProviderName);
                if (!string.Equals(account.ProviderName, providerName, StringComparison.Ordinal))
                {
                    account.ProviderName = providerName;
                    changed = true;
                }

                string accountId = account.AccountId?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(accountId) || usedIds.Contains(accountId))
                {
                    accountId = !seenProviders.Contains(providerName)
                        ? AccountIdentityPolicy.CreateLegacyAccountId(providerName)
                        : AccountIdentityPolicy.CreateAccountId();
                    while (!usedIds.Add(accountId))
                        accountId = AccountIdentityPolicy.CreateAccountId();
                    account.AccountId = accountId;
                    changed = true;
                }
                else
                {
                    usedIds.Add(accountId);
                    if (!string.Equals(account.AccountId, accountId, StringComparison.Ordinal))
                    {
                        account.AccountId = accountId;
                        changed = true;
                    }
                }

                seenProviders.Add(providerName);
            }

            return changed;
        }
    }
}
