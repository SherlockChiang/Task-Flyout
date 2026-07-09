using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace Task_Flyout.Services
{
    internal static class NotificationActivationParser
    {
        private const int MaxActivationArgumentLength = 4096;
        private const int MaxActivationIdLength = 256;

        public static Dictionary<string, string> ParseArguments(string? argument)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(argument)) return result;
            if (argument.Length > MaxActivationArgumentLength) return result;

            argument = WebUtility.HtmlDecode(argument).Trim().Trim('"');
            const string activationPrefix = "----AppNotificationActivated:";
            var prefixIndex = argument.IndexOf(activationPrefix, StringComparison.OrdinalIgnoreCase);
            if (prefixIndex >= 0)
                argument = argument[(prefixIndex + activationPrefix.Length)..].Trim().Trim('"');

            foreach (var pair in argument.Split(new[] { '&', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length != 2) continue;
                result[WebUtility.UrlDecode(parts[0])] = WebUtility.UrlDecode(parts[1]);
            }

            return result;
        }

        public static bool IsVerificationCode(string? value)
            => !string.IsNullOrEmpty(value) && value.Length is >= 4 and <= 8 && value.All(char.IsDigit);

        public static bool IsSafeIdToken(string? value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaxActivationIdLength) return false;
            foreach (var c in value)
            {
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == '@' || c == '/' || c == '+' || c == '='))
                    return false;
            }
            return true;
        }
    }
}
