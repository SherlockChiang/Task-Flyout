using System.Text.Json;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    internal static class ICloudCredentialStore
    {
        private const string Scope = "icloud-caldav";
        private const string CredentialsKey = "credentials";

        public static ICloudCredentialEnvelope? Load()
        {
            string? json = LocalSqliteStore.ReadProtectedText(Scope, CredentialsKey);
            return JsonFallbackPolicy.DeserializeOrDefault<ICloudCredentialEnvelope?>(
                json,
                value => JsonSerializer.Deserialize(value, AppJsonContext.Default.ICloudCredentialEnvelope),
                () => null);
        }

        public static Task SaveAsync(ICloudCredentialEnvelope credentials)
        {
            string json = JsonSerializer.Serialize(credentials, AppJsonContext.Default.ICloudCredentialEnvelope);
            return LocalSqliteStore.WriteProtectedTextAsync(Scope, CredentialsKey, json);
        }

        public static Task ClearAsync()
            => LocalSqliteStore.DeleteProtectedScopeAsync(Scope);
    }
}
