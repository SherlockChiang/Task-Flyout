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
        private static readonly Regex BasicAuthRegex = new(
            @"(?i)\bBasic\s+[A-Za-z0-9+/=]+",
            RegexOptions.Compiled);
        private static readonly Regex CookieHeaderRegex = new(
            @"(?im)^\s*(Set-Cookie|Cookie)\s*:\s*.*$",
            RegexOptions.Compiled);
        private static readonly Regex UrlUserInfoRegex = new(
            @"(?i)\b(https?://)([^\s/@?#]+)@([^\s/?#]+)",
            RegexOptions.Compiled);

        public static string Redact(string? value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? "";

            try
            {
                var redacted = BearerRegex.Replace(value, "Bearer " + Redacted);
                redacted = BasicAuthRegex.Replace(redacted, "Basic " + Redacted);
                redacted = CookieHeaderRegex.Replace(redacted, match => match.Groups[1].Value + ": " + Redacted);
                redacted = UrlUserInfoRegex.Replace(redacted, match => match.Groups[1].Value + Redacted + "@" + match.Groups[3].Value);
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
