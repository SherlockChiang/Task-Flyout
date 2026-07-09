using System;
using System.Text.RegularExpressions;

namespace Task_Flyout.Services
{
    internal static class UserSafeErrorMessage
    {
        private const int DefaultMaxLength = 240;

        public static string FromException(Exception? exception, string fallback = "An unexpected error occurred.", int maxLength = DefaultMaxLength)
            => Format(exception?.Message, fallback, maxLength);

        public static string Format(string? message, string fallback = "An unexpected error occurred.", int maxLength = DefaultMaxLength)
        {
            var safe = DiagnosticsRedactor.Redact(message);
            safe = Regex.Replace(safe, @"\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(safe))
                safe = fallback;

            if (maxLength > 0 && safe.Length > maxLength)
                safe = safe[..maxLength].TrimEnd() + "...";

            return safe;
        }
    }
}
