using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace Task_Flyout.Services
{
    internal static class NotificationActivationParser
    {
        private const int MaxActivationArgumentLength = 4096;
        private const int MaxActivationIdLength = 256;

        public static Dictionary<string, string> ParseArguments(string? argument, bool sdkEncoded = false)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(argument)) return result;
            if (argument.Length > MaxActivationArgumentLength) return result;

            // SDK event arguments have already been XML-decoded by Windows.
            // Keep the legacy query-string path for old activation callers.
            if (!sdkEncoded) argument = WebUtility.HtmlDecode(argument);
            argument = argument.Trim();
            if (argument.Length >= 2 && argument[0] == '"' && argument[^1] == '"')
                argument = argument[1..^1];
            const string activationPrefix = "----AppNotificationActivated:";
            if (argument.StartsWith(activationPrefix, StringComparison.Ordinal))
                argument = argument[activationPrefix.Length..].Trim().Trim('"');
            else if (argument.Contains(activationPrefix, StringComparison.Ordinal))
                return result;

            bool useSdkEncoding = argument.Contains(';') || (sdkEncoded && !argument.Contains('&'));
            foreach (var pair in argument.Split(useSdkEncoding ? ';' : '&'))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length != 2 || !HasValidPercentEncoding(parts[0]) || !HasValidPercentEncoding(parts[1])) return new();
                var key = useSdkEncoding ? DecodeSdkArgument(parts[0]) : WebUtility.UrlDecode(parts[0]);
                var value = useSdkEncoding ? DecodeSdkArgument(parts[1]) : WebUtility.UrlDecode(parts[1]);
                if (string.IsNullOrEmpty(key) || key.Length > 64 || value.Length > 512
                    || key.Any(char.IsControl) || value.Any(char.IsControl) || !result.TryAdd(key, value)) return new();
            }

            return result;
        }

        private static string DecodeSdkArgument(string value)
        {
            // AppNotificationBuilder escapes only %, ; and =. URL decoding would
            // corrupt literal '+' in Outlook IDs and decode escapes a second time.
            var result = new StringBuilder(value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                if (value[index] == '%' && index + 2 < value.Length)
                {
                    char decoded = value.Substring(index, 3) switch
                    {
                        "%25" => '%', "%3B" => ';', "%3D" => '=', _ => '\0'
                    };
                    if (decoded != '\0')
                    {
                        result.Append(decoded);
                        index += 2;
                        continue;
                    }
                }
                result.Append(value[index]);
            }
            return result.ToString();
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
