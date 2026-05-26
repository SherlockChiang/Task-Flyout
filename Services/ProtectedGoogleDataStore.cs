using Google.Apis.Json;
using Google.Apis.Util.Store;
using System;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    internal sealed class ProtectedGoogleDataStore : IDataStore
    {
        private const string StoreScope = "google_token";

        public async Task StoreAsync<T>(string key, T value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (value == null)
            {
                await DeleteAsync<T>(key);
                return;
            }

            var json = NewtonsoftJsonSerializer.Instance.Serialize(value);
            await LocalSqliteStore.WriteProtectedTextAsync(StoreScope, BuildKey<T>(key), json);
        }

        public Task DeleteAsync<T>(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            return LocalSqliteStore.DeleteProtectedTextAsync(StoreScope, BuildKey<T>(key));
        }

        public Task<T> GetAsync<T>(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            var json = LocalSqliteStore.ReadProtectedText(StoreScope, BuildKey<T>(key));
            if (string.IsNullOrWhiteSpace(json))
                return Task.FromResult(default(T)!);

            try
            {
                return Task.FromResult(NewtonsoftJsonSerializer.Instance.Deserialize<T>(json));
            }
            catch
            {
                return Task.FromResult(default(T)!);
            }
        }

        public Task ClearAsync()
            => LocalSqliteStore.DeleteProtectedScopeAsync(StoreScope);

        private static string BuildKey<T>(string key)
            => $"{typeof(T).FullName}|{key}";
    }
}
