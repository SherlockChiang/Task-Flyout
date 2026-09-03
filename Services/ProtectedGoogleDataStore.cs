using Google.Apis.Json;
using Google.Apis.Util.Store;
using System;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    internal sealed class ProtectedGoogleDataStore : IDataStore
    {
        private const string LegacyStoreScope = "google_token";
        private readonly string _storeScope;
        private readonly bool _ownsLegacyScope;

        public ProtectedGoogleDataStore(string? accountId = null)
        {
            string resolvedAccountId = AccountIdentityPolicy.ResolveAccountId("Google", accountId);
            _storeScope = $"google_token:{resolvedAccountId}";
            _ownsLegacyScope = string.Equals(
                resolvedAccountId,
                AccountIdentityPolicy.CreateLegacyAccountId("Google"),
                StringComparison.OrdinalIgnoreCase);
        }

        public async Task StoreAsync<T>(string key, T value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (value == null)
            {
                await DeleteAsync<T>(key);
                return;
            }

            var json = NewtonsoftJsonSerializer.Instance.Serialize(value);
            await LocalSqliteStore.WriteProtectedTextAsync(_storeScope, BuildKey<T>(key), json);
        }

        public Task DeleteAsync<T>(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            return DeleteCoreAsync<T>(key);
        }

        public async Task<T> GetAsync<T>(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            string storageKey = BuildKey<T>(key);
            var json = LocalSqliteStore.ReadProtectedText(_storeScope, storageKey);
            if (string.IsNullOrWhiteSpace(json) && _ownsLegacyScope)
            {
                json = LocalSqliteStore.ReadProtectedText(LegacyStoreScope, storageKey);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    await LocalSqliteStore.WriteProtectedTextAsync(_storeScope, storageKey, json);
                    await LocalSqliteStore.DeleteProtectedTextAsync(LegacyStoreScope, storageKey);
                }
            }
            if (string.IsNullOrWhiteSpace(json))
                return default!;

            try
            {
                return NewtonsoftJsonSerializer.Instance.Deserialize<T>(json);
            }
            catch
            {
                return default!;
            }
        }

        public async Task ClearAsync()
        {
            await LocalSqliteStore.DeleteProtectedScopeAsync(_storeScope);
            if (_ownsLegacyScope)
                await LocalSqliteStore.DeleteProtectedScopeAsync(LegacyStoreScope);
        }

        private async Task DeleteCoreAsync<T>(string key)
        {
            await LocalSqliteStore.DeleteProtectedTextAsync(_storeScope, BuildKey<T>(key));
            if (_ownsLegacyScope)
                await LocalSqliteStore.DeleteProtectedTextAsync(LegacyStoreScope, BuildKey<T>(key));
        }

        private static string BuildKey<T>(string key)
            => $"{typeof(T).FullName}|{key}";
    }
}
