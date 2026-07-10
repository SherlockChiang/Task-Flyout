using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    internal sealed class MailCacheRepository
    {
        private const string Scope = "mail";
        private const string Key = "cache";

        public string? Load()
            => LocalSqliteStore.ReadProtectedText(Scope, Key);

        public Task SaveAsync(string json)
            => LocalSqliteStore.WriteProtectedTextAsync(Scope, Key, json);

        public Task DeleteAsync()
            => LocalSqliteStore.DeleteProtectedTextAsync(Scope, Key);
    }
}
