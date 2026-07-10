using System;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    internal static class VerificationCodeStore
    {
        private const string Scope = "notification-verification-code";
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

        public static string Store(string code)
        {
            var token = Guid.NewGuid().ToString("N");
            LocalSqliteStore.WriteProtectedText(Scope, token, $"{DateTimeOffset.UtcNow.UtcTicks}|{code}");
            return token;
        }

        public static string? Take(string? token)
        {
            if (!NotificationActivationParser.IsSafeIdToken(token)) return null;

            var value = LocalSqliteStore.ReadProtectedText(Scope, token!);
            _ = LocalSqliteStore.DeleteProtectedTextAsync(Scope, token!);
            if (string.IsNullOrWhiteSpace(value)) return null;

            var separator = value.IndexOf('|');
            if (separator <= 0 ||
                !long.TryParse(value[..separator], out var ticks) ||
                DateTimeOffset.UtcNow - new DateTimeOffset(ticks, TimeSpan.Zero) > Lifetime)
                return null;

            var code = value[(separator + 1)..];
            return NotificationActivationParser.IsVerificationCode(code) ? code : null;
        }
    }
}
