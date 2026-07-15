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
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(argument)) return result;
            if (argument.Length > MaxActivationArgumentLength) return result;

            argument = WebUtility.HtmlDecode(argument).Trim().Trim('"');
            const string activationPrefix = "----AppNotificationActivated:";
            if (argument.StartsWith(activationPrefix, StringComparison.Ordinal))
                argument = argument[activationPrefix.Length..].Trim().Trim('"');
            else if (argument.Contains(activationPrefix, StringComparison.Ordinal) || argument.Contains(';'))
                return result;

            foreach (var pair in argument.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length != 2 || !HasValidPercentEncoding(parts[0]) || !HasValidPercentEncoding(parts[1])) return new();
                var key = WebUtility.UrlDecode(parts[0]);
                var value = WebUtility.UrlDecode(parts[1]);
                if (string.IsNullOrEmpty(key) || key.Length > 64 || value.Length > 512
                    || key.Any(char.IsControl) || value.Any(char.IsControl) || !result.TryAdd(key, value)) return new();
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

        public static bool IsOpaqueToken(string? value)
            => value?.Length == 32 && value.All(character => char.IsAsciiHexDigit(character) && !char.IsUpper(character));

        public static bool TryParseAgendaAction(string? argument, out string action, out string token)
        {
            action = "";
            token = "";
            var arguments = ParseArguments(argument);
            if (arguments.Count != 2 || !arguments.TryGetValue("action", out action!) || !arguments.TryGetValue("token", out token!))
                return false;
            return action is "openAgenda" or "snoozeAgenda" or "completeTask" && IsOpaqueToken(token);
        }

        private static bool HasValidPercentEncoding(string value)
        {
            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] != '%') continue;
                if (index + 2 >= value.Length || !char.IsAsciiHexDigit(value[index + 1]) || !char.IsAsciiHexDigit(value[index + 2]))
                    return false;
                index += 2;
            }
            return true;
        }
    }
}
