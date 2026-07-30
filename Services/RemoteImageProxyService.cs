using System.Threading;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    internal sealed class RemoteImageProxyService
    {
        public static RemoteImageProxyService Instance { get; } = new();

        private RemoteImageProxyService()
        {
        }

        public Task<RemoteImageStream?> FetchAsync(string url, CancellationToken cancellationToken = default)
            => RssService.FetchRemoteImageSafelyAsync(url, cancellationToken);

        public Task<RemoteImageStream?> FetchTrustedMailAsync(
            string url,
            CancellationToken cancellationToken = default)
            => RssService.FetchTrustedMailImageSafelyAsync(url, cancellationToken);
    }
}
