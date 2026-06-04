using System.Linq;
using System.Text.RegularExpressions;

namespace Task_Flyout.Services
{
    /// <summary>
    /// Best-effort detection of a one-time / verification code (OTP) in an incoming
    /// email so the new-mail notification can offer a "copy code" button.
    ///
    /// To avoid attaching a copy button to ordinary mail that merely contains a number,
    /// a code is only returned when the text also contains an OTP-related keyword. The
    /// digit group nearest a keyword is preferred (so footer numbers like a postcode are
    /// not picked over the actual code).
    /// </summary>
    internal static class VerificationCodeDetector
    {
        // Chinese + English cues commonly used by OTP / 2FA messages.
        private static readonly Regex KeywordRegex = new(
            "验证码|校验码|安全代码|安全码|动态密码|动态码|确认码|验证代码|一次性密码|短信码|" +
            @"verification|security code|one[\- ]?time|passcode|\bOTP\b|\bcode\b|2FA|two[\- ]?factor",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // A 4–8 digit code, optionally split once by a space or hyphen (e.g. "123 456").
        // Negative look-arounds keep it from matching inside a longer number.
        private static readonly Regex CodeRegex = new(
            @"(?<!\d)\d{3}[ \-]\d{3}(?!\d)|(?<!\d)\d{4,8}(?!\d)",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// Returns true and sets <paramref name="code"/> (digits only) when the subject
        /// or preview looks like it carries a verification code.
        /// </summary>
        public static bool TryExtract(string? subject, string? preview, out string code)
        {
            code = "";
            string text = $"{subject}\n{preview}".Trim();
            if (text.Length == 0) return false;

            var keywords = KeywordRegex.Matches(text);
            if (keywords.Count == 0) return false;

            var candidates = CodeRegex.Matches(text);
            if (candidates.Count == 0) return false;

            Match? best = null;
            int bestDistance = int.MaxValue;
            foreach (Match candidate in candidates)
            {
                int distance = int.MaxValue;
                foreach (Match keyword in keywords)
                {
                    int keywordEnd = keyword.Index + keyword.Length;
                    // Codes that appear right after a keyword are the strongest signal;
                    // ones before it are penalised so they only win when nothing follows.
                    int d = candidate.Index >= keywordEnd
                        ? candidate.Index - keywordEnd
                        : (keyword.Index - candidate.Index) + 1000;
                    if (d < distance) distance = d;
                }
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            if (best == null) return false;

            code = new string(best.Value.Where(char.IsDigit).ToArray());
            return code.Length is >= 4 and <= 8;
        }
    }
}
