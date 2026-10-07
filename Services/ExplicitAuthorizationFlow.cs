using System;
using System.Threading;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    internal static class ExplicitAuthorizationFlow
    {
        // Runtime restoration passes no connection callback. Only a user action
        // supplies one, so expired consent cannot open a browser in the background.
        public static async Task<T> AcquireAsync<T>(
            Func<CancellationToken, Task<T>> restoreAsync,
            Func<CancellationToken, Task<T>>? connectAsync = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(restoreAsync);
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await restoreAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (AuthorizationInteractionRequiredException) when (connectAsync != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await connectAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
