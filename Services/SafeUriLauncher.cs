using System;
using System.Threading.Tasks;
using Windows.System;

namespace Task_Flyout.Services
{
    public static class SafeUriLauncher
    {
        private const int MaxExternalUriLength = 4096;

        public static async Task<bool> TryLaunchExternalHttpUriAsync(string? uriText)
        {
            if (!TryCreateExternalHttpUri(uriText, out var uri))
                return false;

            return await Launcher.LaunchUriAsync(uri);
        }

        public static bool TryCreateExternalHttpUri(string? uriText, out Uri uri)
        {
            uri = null!;
            if (string.IsNullOrWhiteSpace(uriText)) return false;
            if (uriText.Length > MaxExternalUriLength) return false;
            if (!Uri.TryCreate(uriText, UriKind.Absolute, out var parsed)) return false;
            if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return false;

            uri = parsed;
            return true;
        }
    }
}
