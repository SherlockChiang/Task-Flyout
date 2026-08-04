using System.Net;
using System.Net.Http.Headers;
using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class ICloudCalDavClientTests
{
    private static readonly Uri ResourceUri =
        new("https://p01-caldav.icloud.com/123/calendars/work/event.ics");

    [Theory]
    [InlineData("a:b@example.com", "abcd-efgh")]
    [InlineData("person@example.com", "abc\ndef")]
    public void Rejects_credentials_that_cannot_be_safely_encoded_as_basic_auth(
        string accountName,
        string password)
    {
        var credentials = new ICloudCredentialEnvelope(accountName, password);

        Assert.Throws<ArgumentException>(() => ICloudCalDavClient.ValidateCredentials(credentials));
    }

    [Fact]
    public void Credential_diagnostics_are_fully_redacted()
    {
        var credentials = new ICloudCredentialEnvelope(
            "person@example.com",
            "abcd-efgh-ijkl-mnop");

        string diagnostic = credentials.ToString();

        Assert.DoesNotContain(credentials.AccountName, diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain(credentials.AppSpecificPassword, diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_redirects_outside_the_Apple_CalDav_hosts()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal("Basic", request.Headers.Authorization?.Scheme);
            var response = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            response.Headers.Location = new Uri("https://example.com/stolen.ics");
            return response;
        });
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.GetResourceAsync(ResourceUri, CancellationToken.None));

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Replays_conditional_updates_only_across_allowed_redirects()
    {
        var redirectedUri = new Uri("https://p02-caldav.icloud.com/123/calendars/work/event.ics");
        var handler = new StubHandler((request, call) =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Contains("\"v1\"", request.Headers.GetValues("If-Match"));
            Assert.NotNull(request.Content);

            if (call == 1)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
                redirect.Headers.Location = redirectedUri;
                return redirect;
            }

            Assert.Equal(redirectedUri, request.RequestUri);
            return new HttpResponseMessage(HttpStatusCode.NoContent)
            {
                Content = new StringContent("")
            };
        });
        using var client = CreateClient(handler);

        var response = await client.UpdateResourceAsync(
            ResourceUri,
            "BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n",
            "\"v1\"",
            CancellationToken.None);

        Assert.Equal(redirectedUri, response.ResponseUri);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Maps_authentication_failures_to_reconnect_required()
    {
        using var client = CreateClient(new StubHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var error = await Assert.ThrowsAsync<AuthorizationInteractionRequiredException>(() =>
            client.GetResourceAsync(ResourceUri, CancellationToken.None));

        Assert.Equal("iCloud", error.ProviderName);
    }

    [Fact]
    public async Task Does_not_treat_calendar_permission_denial_as_bad_credentials()
    {
        using var client = CreateClient(new StubHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.Forbidden)));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetResourceAsync(ResourceUri, CancellationToken.None));

        Assert.IsNotType<AuthorizationInteractionRequiredException>(error);
        Assert.Contains("permission", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Maps_concurrency_conflicts_to_a_refreshable_error()
    {
        using var client = CreateClient(new StubHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.PreconditionFailed)));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GetResourceAsync(ResourceUri, CancellationToken.None));

        Assert.Contains("Refresh", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_resources_larger_than_the_bounded_response_limit()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[(2 * 1024 * 1024) + 1])
        });
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.GetResourceAsync(ResourceUri, CancellationToken.None));
    }

    [Fact]
    public async Task Request_timeout_also_cancels_a_stalled_response_body()
    {
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new HangingReadStream())
        });
        using var client = CreateClient(handler, TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetResourceAsync(ResourceUri, CancellationToken.None));
    }

    [Fact]
    public async Task Returns_the_server_ETag_for_follow_up_mutations()
    {
        var handler = new StubHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n")
            };
            response.Headers.ETag = new EntityTagHeaderValue("\"v2\"");
            return response;
        });
        using var client = CreateClient(handler);

        var response = await client.GetResourceAsync(ResourceUri, CancellationToken.None);

        Assert.Equal("\"v2\"", response.ETag);
        Assert.DoesNotContain("BEGIN:VCALENDAR", response.ToString(), StringComparison.Ordinal);
    }

    private static ICloudCalDavClient CreateClient(
        HttpMessageHandler handler,
        TimeSpan? requestTimeout = null)
        => new(
            new ICloudCredentialEnvelope("person@example.com", "abcd-efgh-ijkl-mnop"),
            handler,
            requestTimeout);

    private sealed class StubHandler(
        Func<HttpRequestMessage, int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int call = Interlocked.Increment(ref _callCount);
            return Task.FromResult(responder(request, call));
        }
    }

    private sealed class HangingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();
        public override void SetLength(long value)
            => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
