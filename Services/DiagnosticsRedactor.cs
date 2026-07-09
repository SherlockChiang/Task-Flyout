using System;
using System.Text.RegularExpressions;

namespace Task_Flyout.Services
{
    internal static class DiagnosticsRedactor
    {
        private const string Redacted = "[redacted]";
        private static readonly Regex SensitivePairRegex = new(
            @"(?i)\b(access_token|refresh_token|id_token|client_secret|password|passwd|secret|token|code)\b\s*([=:])\s*([^\s&;,'""<>]+)",
            RegexOptions.Compiled);
        private static readonly Regex SensitiveQueryRegex = new(
            @"(?i)([?&;](?:access_token|refresh_token|id_token|client_secret|password|passwd|secret|token|code)=)([^&;\s#]+)",
            RegexOptions.Compiled);
        private static readonly Regex BearerRegex = new(
            @"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+",
            RegexOptions.Compiled);

        public static string Redact(string? value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? "";

            try
            {
                var redacted = BearerRegex.Replace(value, "Bearer " + Redacted);
                redacted = SensitiveQueryRegex.Replace(redacted, match => match.Groups[1].Value + Redacted);
                redacted = SensitivePairRegex.Replace(redacted, match =>
                    match.Groups[1].Value + match.Groups[2].Value + Redacted);
                return redacted;
            }
            catch (RegexMatchTimeoutException)
            {
                return Redacted;
            }
        }
    }
}
