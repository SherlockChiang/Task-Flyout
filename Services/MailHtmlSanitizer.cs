using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

namespace Task_Flyout.Services
{
    /// <summary>
    /// Regex-based defence-in-depth sanitizer for mail HTML before it is handed to the
    /// (script-disabled, resource-filtered) WebView2. Two profiles: <see cref="SanitizeUntrusted"/>
    /// for unknown senders (also strips remote resources to block tracking pixels) and
    /// <see cref="SanitizeTrusted"/> for sender-approved mail (keeps remote images). Both strip
    /// scripts, event handlers, and script/data navigation URIs. Pure and unit-tested.
    /// </summary>
    internal static class MailHtmlSanitizer
    {
        private static readonly Regex RxPairedDangerousTags = new(@"<\s*(script|iframe|object|embed|form|input|button|textarea|select|svg|noscript|template|base)\b[^>]*>.*?<\s*/\s*\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxSelfClosingDangerousTags = new(@"<\s*(script|iframe|object|embed|form|input|button|textarea|select|svg|noscript|template|base)\b[^>]*/?\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxEventHandlerQuoted = new(@"\s+on\w+\s*=\s*(['""]).*?\1", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxEventHandlerUnquoted = new(@"\s+on\w+\s*=\s*[^\s>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxScriptUriQuoted = new(@"(href|src|action|formaction|data)\s*=\s*(['""])\s*(javascript|vbscript):.*?\2", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxScriptUriUnquoted = new(@"(href|src|action|formaction|data)\s*=\s*(javascript|vbscript):[^\s>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxDataUriNavigationQuoted = new(@"(href|action|formaction|data)\s*=\s*(['""])\s*data:.*?\2", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxDataUriNavigationUnquoted = new(@"(href|action|formaction|data)\s*=\s*data:[^\s>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxDangerousCssStyle = new(@"style\s*=\s*(['""])[^'""]*\b(expression|-moz-binding|behavior)\b[^'""]*\1", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxUntrustedRemoteResourceQuoted = new(@"\s+(src|srcset|poster|background)\s*=\s*(['""])\s*(?:https?:)?//.*?\2", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxUntrustedRemoteResourceUnquoted = new(@"\s+(src|srcset|poster|background)\s*=\s*(?:https?:)?//[^\s>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxUntrustedRemoteSrcSetQuoted = new(@"\s+srcset\s*=\s*(['""])[^'"">]*(?:https?:)?//[^'"">]*\1", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxUntrustedRemoteSrcSetUnquoted = new(@"\s+srcset\s*=\s*[^\s>]*(?:https?:)?//[^\s>]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxUntrustedRemoteCssStyle = new(@"\s+style\s*=\s*(['""])[^'""]*(?:@import|url\s*\(\s*['""]?\s*(?:https?:)?//)[^'""]*\1", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxUntrustedRemoteStyleBlock = new(@"<\s*style\b[^>]*>.*?(?:@import|url\s*\(\s*['""]?\s*(?:https?:)?//).*?<\s*/\s*style\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxUntrustedExternalLinkTag = new(@"<\s*link\b[^>]*\bhref\s*=\s*(['""])\s*(?:https?:)?//.*?\1[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxUntrustedMetaRefreshTag = new(@"<\s*meta\b[^>]*\bhttp-equiv\s*=\s*(['""]?)refresh\1[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxTrustedScriptUriQuoted = new(@"(href|action|formaction)\s*=\s*(['""])\s*(javascript|vbscript):.*?\2", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxTrustedScriptUriUnquoted = new(@"(href|action|formaction)\s*=\s*(javascript|vbscript):[^\s>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxTrustedDangerousCss = new(@"style\s*=\s*(['""])[^'""]*\b(expression|-moz-binding|behavior)\b[^'""]*\1", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxStyleAttributeQuoted = new(@"\s+style\s*=\s*(['""])(.*?)\1", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxUrlAttributeQuoted = new(@"\b(href|src|action|formaction|data)\s*=\s*(['""])(.*?)\2", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex RxUrlAttributeUnquoted = new(@"\b(href|src|action|formaction|data)\s*=\s*([^\s>]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxHtmlContentTags = new(@"<\s*(html|head|body|style|table|div|p|span|br|img|a|meta)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxHtmlTagsOnly = new("<.*?>", RegexOptions.Compiled);
        private static readonly Regex RxCssSelector = new(@"^[.#][\w\-#.:\s,>+~\[\]=""']+\{?$", RegexOptions.Compiled);
        private static readonly Regex RxCssProperty = new(@"^[a-zA-Z\-]+\s*:\s*[^。！？；，、]*;?$", RegexOptions.Compiled);
        private static readonly Regex RxCssRuleStart = new(@"^[a-zA-Z][\w\-#.\s,>+~\[\]=""']+\s*\{", RegexOptions.Compiled);
        private static readonly Regex RxRemoteResource = new(@"\s(?:src|srcset|poster|background)\s*=\s*['""]?\s*(?:https?:)?//|url\s*\(\s*['""]?\s*(?:https?:)?//|@import\s+['""]?\s*(?:https?:)?//", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// True if the HTML references any remote (http/https or protocol-relative) resource —
        /// used to decide whether to surface a "remote images blocked" banner to the user.
        /// </summary>
        public static bool HasRemoteResources(string? html)
            => !string.IsNullOrEmpty(html) && RxRemoteResource.IsMatch(html);

        /// <summary>Sanitize HTML from an untrusted sender (strips scripts AND remote resources).</summary>
        public static string SanitizeUntrusted(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return "";

            try
            {
                return SanitizeUntrustedCore(html);
            }
            catch (RegexMatchTimeoutException)
            {
                // Malicious/pathological markup tripped ReDoS protection — fall back to a
                // fully-escaped plain-text view, which can never execute anything.
                return $"<pre>{WebUtility.HtmlEncode(StripAllTagsSafe(html))}</pre>";
            }
        }

        /// <summary>Sanitize HTML from a sender the user has approved (keeps remote images).</summary>
        public static string SanitizeTrusted(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return "";

            try
            {
                var value = html;
                value = RxPairedDangerousTags.Replace(value, "");
                value = RxSelfClosingDangerousTags.Replace(value, "");
                value = RxEventHandlerQuoted.Replace(value, "");
                value = RxEventHandlerUnquoted.Replace(value, "");
                value = RxTrustedScriptUriQuoted.Replace(value, "$1=\"#\"");
                value = RxTrustedScriptUriUnquoted.Replace(value, "$1=\"#\"");
                value = RxDataUriNavigationQuoted.Replace(value, "$1=\"#\"");
                value = RxDataUriNavigationUnquoted.Replace(value, "$1=\"#\"");
                value = NeutralizeDangerousUrlAttributes(value);
                value = RxTrustedDangerousCss.Replace(value, "");
                value = StripDangerousCssStyles(value);
                value = RxUntrustedMetaRefreshTag.Replace(value, "");

                if (!RxHtmlContentTags.IsMatch(value))
                    value = $"<pre>{WebUtility.HtmlEncode(RemoveCssNoise(value))}</pre>";

                return value;
            }
            catch (RegexMatchTimeoutException)
            {
                return $"<pre>{WebUtility.HtmlEncode(StripAllTagsSafe(html))}</pre>";
            }
        }

        private static string SanitizeUntrustedCore(string html)
        {
            var value = html;
            value = RxPairedDangerousTags.Replace(value, "");
            value = RxSelfClosingDangerousTags.Replace(value, "");
            value = RxEventHandlerQuoted.Replace(value, "");
            value = RxEventHandlerUnquoted.Replace(value, "");
            value = RxScriptUriQuoted.Replace(value, "$1=\"#\"");
            value = RxScriptUriUnquoted.Replace(value, "$1=\"#\"");
            value = RxDataUriNavigationQuoted.Replace(value, "$1=\"#\"");
            value = RxDataUriNavigationUnquoted.Replace(value, "$1=\"#\"");
            value = NeutralizeDangerousUrlAttributes(value);
            value = RxDangerousCssStyle.Replace(value, "");
            value = StripDangerousCssStyles(value);
            value = StripRemoteResources(value);

            if (!RxHtmlContentTags.IsMatch(value))
                value = $"<pre>{WebUtility.HtmlEncode(RemoveCssNoise(value))}</pre>";

            return value;
        }

        private static string StripRemoteResources(string html)
        {
            html = RxUntrustedExternalLinkTag.Replace(html, "");
            html = RxUntrustedMetaRefreshTag.Replace(html, "");
            html = RxUntrustedRemoteStyleBlock.Replace(html, "");
            html = RxUntrustedRemoteCssStyle.Replace(html, "");
            html = RxUntrustedRemoteSrcSetQuoted.Replace(html, "");
            html = RxUntrustedRemoteSrcSetUnquoted.Replace(html, "");
            html = RxUntrustedRemoteResourceQuoted.Replace(html, "");
            html = RxUntrustedRemoteResourceUnquoted.Replace(html, "");
            return html;
        }

        private static string StripDangerousCssStyles(string html)
            => RxStyleAttributeQuoted.Replace(html, match => IsDangerousCss(match.Groups[2].Value) ? "" : match.Value);

        private static bool IsDangerousCss(string style)
        {
            var decoded = DecodeCssEscapes(WebUtility.HtmlDecode(style) ?? "");
            return decoded.Contains("expression", StringComparison.OrdinalIgnoreCase) ||
                   decoded.Contains("-moz-binding", StringComparison.OrdinalIgnoreCase) ||
                   decoded.Contains("behavior", StringComparison.OrdinalIgnoreCase);
        }

        private static string DecodeCssEscapes(string value)
        {
            if (value.IndexOf('\\') < 0) return value;

            var result = new char[value.Length];
            var length = 0;
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] != '\\' || i + 1 >= value.Length)
                {
                    result[length++] = value[i];
                    continue;
                }

                var start = i + 1;
                var end = start;
                while (end < value.Length && end - start < 6 && IsHexDigit(value[end]))
                    end++;

                if (end == start)
                {
                    i++;
                    result[length++] = value[i];
                    continue;
                }

                var hex = value.Substring(start, end - start);
                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var codePoint))
                    result[length++] = codePoint <= char.MaxValue ? (char)codePoint : ' ';

                i = end - 1;
                if (i + 1 < value.Length && char.IsWhiteSpace(value[i + 1]))
                    i++;
            }

            return new string(result, 0, length);
        }

        private static bool IsHexDigit(char c)
            => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';

        private static string NeutralizeDangerousUrlAttributes(string html)
        {
            html = RxUrlAttributeQuoted.Replace(html, match =>
            {
                var attribute = match.Groups[1].Value;
                var quote = match.Groups[2].Value;
                var value = match.Groups[3].Value;
                return IsDangerousUrlAttribute(attribute, value) ? $"{attribute}={quote}#{quote}" : match.Value;
            });

            return RxUrlAttributeUnquoted.Replace(html, match =>
            {
                var attribute = match.Groups[1].Value;
                var value = match.Groups[2].Value;
                return IsDangerousUrlAttribute(attribute, value) ? $"{attribute}=\"#\"" : match.Value;
            });
        }

        private static bool IsDangerousUrlAttribute(string attribute, string value)
        {
            var decoded = WebUtility.HtmlDecode(value)?.TrimStart() ?? "";
            var scheme = GetNormalizedScheme(decoded);
            if (scheme.Equals("javascript", StringComparison.OrdinalIgnoreCase) ||
                scheme.Equals("vbscript", StringComparison.OrdinalIgnoreCase))
                return true;

            return IsNavigationAttribute(attribute) && scheme.Equals("data", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetNormalizedScheme(string value)
        {
            var colon = value.IndexOf(':');
            if (colon < 0) return "";

            Span<char> buffer = stackalloc char[Math.Min(colon, 64)];
            var length = 0;
            for (var i = 0; i < colon && length < buffer.Length; i++)
            {
                var c = value[i];
                if (!char.IsWhiteSpace(c) && !char.IsControl(c))
                    buffer[length++] = c;
            }

            return new string(buffer[..length]);
        }

        private static bool IsNavigationAttribute(string attribute)
            => attribute.Equals("href", StringComparison.OrdinalIgnoreCase) ||
               attribute.Equals("action", StringComparison.OrdinalIgnoreCase) ||
               attribute.Equals("formaction", StringComparison.OrdinalIgnoreCase) ||
               attribute.Equals("data", StringComparison.OrdinalIgnoreCase);

        private static string StripAllTagsSafe(string html)
        {
            try { return RxHtmlTagsOnly.Replace(html, " "); }
            catch (RegexMatchTimeoutException) { return html; }
        }

        private static string RemoveCssNoise(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";

            var lines = value
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');

            var kept = new List<string>();
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;
                if (IsCssNoiseLine(trimmed)) continue;
                kept.Add(line);
            }

            return string.Join("\n", kept).Trim();
        }

        private static bool IsCssNoiseLine(string line)
        {
            if (line == "{" || line == "}") return true;
            if (line.StartsWith("@media", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("@font-face", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("@-moz", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("@supports", StringComparison.OrdinalIgnoreCase))
                return true;
            if (RxCssSelector.IsMatch(line)) return true;
            if (RxCssProperty.IsMatch(line)) return true;
            if (RxCssRuleStart.IsMatch(line)) return true;
            return false;
        }
    }
}
