using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    internal sealed record ICloudCalDavTextResponse(Uri ResponseUri, string Content, string ETag)
    {
        public override string ToString()
            => $"{nameof(ICloudCalDavTextResponse)} {{ ResponseUri = {ResponseUri}, ContentLength = {Content.Length}, ETag = {ETag} }}";
    }

    internal sealed class ICloudCalDavClient : IDisposable
    {
        private const int MaxRedirects = 5;
        private const int MaxDiscoveryBytes = 2 * 1024 * 1024;
        private const int MaxQueryBytes = 8 * 1024 * 1024;
        private const int MaxResourceBytes = 2 * 1024 * 1024;
        private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(35);
        private static readonly Uri ServiceRoot = new("https://caldav.icloud.com/");
        private static readonly HttpMethod PropFindMethod = new("PROPFIND");
        private static readonly HttpMethod ReportMethod = new("REPORT");

        private readonly HttpClient _httpClient;
        private readonly string _authorizationParameter;
        private readonly TimeSpan _requestTimeout;
        private bool _disposed;

        public ICloudCalDavClient(
            ICloudCredentialEnvelope credentials,
            HttpMessageHandler? handler = null,
            TimeSpan? requestTimeout = null)
        {
            ValidateCredentials(credentials);
            _requestTimeout = requestTimeout ?? DefaultRequestTimeout;
            if (_requestTimeout <= TimeSpan.Zero && _requestTimeout != System.Threading.Timeout.InfiniteTimeSpan)
                throw new ArgumentOutOfRangeException(nameof(requestTimeout));
            _authorizationParameter = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{credentials.AccountName}:{credentials.AppSpecificPassword}"));

            handler ??= new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
                CheckCertificateRevocationList = true,
                UseCookies = false
            };
            _httpClient = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan
            };
        }

        public static void ValidateCredentials(ICloudCredentialEnvelope credentials)
        {
            string account = credentials.AccountName?.Trim() ?? "";
            string password = credentials.AppSpecificPassword?.Trim() ?? "";
            if (account.Length is < 3 or > 320 || password.Length is < 4 or > 256)
                throw new ArgumentException("Enter a valid Apple Account and app-specific password.");
            if (account.Contains(':')
                || ContainsControlCharacters(account)
                || ContainsControlCharacters(password))
                throw new ArgumentException("The Apple Account credentials contain unsupported characters.");
        }

        public async Task<IReadOnlyList<ICloudCalendarDescriptor>> DiscoverCalendarsAsync(CancellationToken cancellationToken)
        {
            const string principalRequest = """
<?xml version="1.0" encoding="utf-8"?>
<D:propfind xmlns:D="DAV:">
  <D:prop><D:current-user-principal /></D:prop>
</D:propfind>
""";
            var principalResponse = await SendAsync(
                PropFindMethod, ServiceRoot, principalRequest, "application/xml", MaxDiscoveryBytes,
                new Dictionary<string, string> { ["Depth"] = "0" }, cancellationToken);
            Uri principal = ICloudCalDavPolicy.ParseCurrentUserPrincipal(principalResponse.Content, principalResponse.ResponseUri);

            const string homeRequest = """
<?xml version="1.0" encoding="utf-8"?>
<D:propfind xmlns:D="DAV:" xmlns:C="urn:ietf:params:xml:ns:caldav">
  <D:prop><C:calendar-home-set /></D:prop>
</D:propfind>
""";
            var homeResponse = await SendAsync(
                PropFindMethod, principal, homeRequest, "application/xml", MaxDiscoveryBytes,
                new Dictionary<string, string> { ["Depth"] = "0" }, cancellationToken);
            Uri calendarHome = ICloudCalDavPolicy.ParseCalendarHome(homeResponse.Content, homeResponse.ResponseUri);

            const string calendarsRequest = """
<?xml version="1.0" encoding="utf-8"?>
<D:propfind xmlns:D="DAV:" xmlns:C="urn:ietf:params:xml:ns:caldav" xmlns:A="http://apple.com/ns/ical/">
  <D:prop>
    <D:resourcetype />
    <D:displayname />
    <D:current-user-privilege-set />
    <C:supported-calendar-component-set />
    <A:calendar-color />
  </D:prop>
</D:propfind>
""";
            var calendarsResponse = await SendAsync(
                PropFindMethod, calendarHome, calendarsRequest, "application/xml", MaxDiscoveryBytes,
                new Dictionary<string, string> { ["Depth"] = "1" }, cancellationToken);
            return ICloudCalDavPolicy.ParseCalendars(calendarsResponse.Content, calendarsResponse.ResponseUri);
        }

        public async Task<IReadOnlyList<ICloudCalendarResource>> QueryCalendarAsync(
            Uri calendarUri,
            DateTime rangeStart,
            DateTime rangeEnd,
            CancellationToken cancellationToken)
        {
            EnsureAllowed(calendarUri);
            string start = ToCalDavUtc(rangeStart);
            string end = ToCalDavUtc(rangeEnd);
            string report = $"""
<?xml version="1.0" encoding="utf-8"?>
<C:calendar-query xmlns:D="DAV:" xmlns:C="urn:ietf:params:xml:ns:caldav">
  <D:prop><D:getetag /><C:calendar-data /></D:prop>
  <C:filter>
    <C:comp-filter name="VCALENDAR">
      <C:comp-filter name="VEVENT">
        <C:time-range start="{start}" end="{end}" />
      </C:comp-filter>
    </C:comp-filter>
  </C:filter>
</C:calendar-query>
""";
            var response = await SendAsync(
                ReportMethod, calendarUri, report, "application/xml", MaxQueryBytes,
                new Dictionary<string, string> { ["Depth"] = "1" }, cancellationToken);
            return ICloudCalDavPolicy.ParseCalendarResources(response.Content, response.ResponseUri);
        }

        public Task<ICloudCalDavTextResponse> GetResourceAsync(Uri resourceUri, CancellationToken cancellationToken)
            => SendAsync(HttpMethod.Get, resourceUri, null, null, MaxResourceBytes, null, cancellationToken);

        public Task<ICloudCalDavTextResponse> CreateResourceAsync(
            Uri resourceUri,
            string calendarData,
            CancellationToken cancellationToken)
            => SendAsync(
                HttpMethod.Put, resourceUri, calendarData, "text/calendar", MaxDiscoveryBytes,
                new Dictionary<string, string> { ["If-None-Match"] = "*" }, cancellationToken);

        public Task<ICloudCalDavTextResponse> UpdateResourceAsync(
            Uri resourceUri,
            string calendarData,
            string etag,
            CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(etag)) headers["If-Match"] = etag;
            return SendAsync(HttpMethod.Put, resourceUri, calendarData, "text/calendar", MaxDiscoveryBytes, headers, cancellationToken);
        }

        public Task<ICloudCalDavTextResponse> DeleteResourceAsync(
            Uri resourceUri,
            string etag,
            CancellationToken cancellationToken)
        {
            var headers = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(etag)) headers["If-Match"] = etag;
            return SendAsync(HttpMethod.Delete, resourceUri, null, null, MaxDiscoveryBytes, headers, cancellationToken);
        }

        private async Task<ICloudCalDavTextResponse> SendAsync(
            HttpMethod method,
            Uri requestUri,
            string? body,
            string? contentType,
            int maxResponseBytes,
            IReadOnlyDictionary<string, string>? headers,
            CancellationToken cancellationToken)
        {
            EnsureAllowed(requestUri);
            Uri currentUri = requestUri;
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (_requestTimeout != System.Threading.Timeout.InfiniteTimeSpan)
                timeoutSource.CancelAfter(_requestTimeout);
            CancellationToken requestToken = timeoutSource.Token;

            for (int redirect = 0; redirect <= MaxRedirects; redirect++)
            {
                using var request = new HttpRequestMessage(method, currentUri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authorizationParameter);
                request.Headers.UserAgent.ParseAdd("TaskFlyout/1.0");
                request.Headers.Accept.ParseAdd("application/xml, text/calendar;q=0.9, */*;q=0.1");
                if (headers != null)
                {
                    foreach (var header in headers)
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                if (body != null)
                    request.Content = new StringContent(body, new UTF8Encoding(false), contentType ?? "application/octet-stream");

                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    requestToken);

                if (IsRedirect(response.StatusCode))
                {
                    if (redirect == MaxRedirects || response.Headers.Location == null)
                        throw new HttpRequestException("iCloud returned too many CalDAV redirects.");
                    currentUri = ICloudCalDavPolicy.ResolveEndpoint(currentUri, response.Headers.Location.ToString());
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    throw new AuthorizationInteractionRequiredException(
                        "iCloud",
                        "iCloud sign-in failed. Check the Apple Account and app-specific password.");
                if (response.StatusCode == HttpStatusCode.Forbidden)
                    throw new InvalidOperationException("iCloud denied permission for this calendar operation.");
                if (response.StatusCode == HttpStatusCode.PreconditionFailed)
                    throw new InvalidOperationException("The iCloud event changed on another device. Refresh and try again.");
                if (response.StatusCode == HttpStatusCode.Conflict)
                    throw new InvalidOperationException("iCloud rejected the calendar change because its state conflicts with the request. Refresh and try again.");
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"iCloud CalDAV request failed with HTTP {(int)response.StatusCode}.");

                string content = await ReadBoundedContentAsync(response, maxResponseBytes, requestToken);
                string etag = response.Headers.ETag?.ToString() ?? "";
                return new ICloudCalDavTextResponse(currentUri, content, etag);
            }

            throw new HttpRequestException("iCloud CalDAV redirect handling failed.");
        }

        private static async Task<string> ReadBoundedContentAsync(
            HttpResponseMessage response,
            int maxBytes,
            CancellationToken cancellationToken)
        {
            if (response.Content.Headers.ContentLength is long length && length > maxBytes)
                throw new InvalidDataException("The iCloud CalDAV response was too large.");

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var destination = new MemoryStream(Math.Min(maxBytes, 64 * 1024));
            byte[] buffer = new byte[16 * 1024];
            int total = 0;
            while (true)
            {
                int read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0) break;
                total += read;
                if (total > maxBytes)
                    throw new InvalidDataException("The iCloud CalDAV response was too large.");
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            string? charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
            Encoding encoding = Encoding.UTF8;
            if (!string.IsNullOrWhiteSpace(charset))
            {
                try { encoding = Encoding.GetEncoding(charset); }
                catch (ArgumentException) { }
            }
            return encoding.GetString(destination.GetBuffer(), 0, total);
        }

        private static bool IsRedirect(HttpStatusCode statusCode)
            => statusCode is HttpStatusCode.MovedPermanently
                or HttpStatusCode.Found
                or HttpStatusCode.TemporaryRedirect
                or HttpStatusCode.PermanentRedirect;

        private static string ToCalDavUtc(DateTime value)
        {
            DateTime utc = value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Local => value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
            };
            return utc.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static bool ContainsControlCharacters(string value)
        {
            foreach (char character in value)
            {
                if (char.IsControl(character)) return true;
            }
            return false;
        }

        private static void EnsureAllowed(Uri uri)
        {
            if (!ICloudCalDavPolicy.IsAllowedEndpoint(uri))
                throw new InvalidDataException("The iCloud CalDAV address is not permitted.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _httpClient.Dispose();
        }
    }
}
