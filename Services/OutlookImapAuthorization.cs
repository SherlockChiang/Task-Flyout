using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    internal sealed class OutlookImapAccountMismatchException : InvalidOperationException
    {
        public OutlookImapAccountMismatchException()
            : base("The signed-in Microsoft account does not match the selected mailbox.") { }
    }

    // One instance/cache per IMAP mail account. It never reads or overwrites the
    // Graph/calendar cache, and runtime token acquisition is always silent.
    internal sealed class OutlookImapAuthorization
    {
        private readonly string _cachePath;
        private readonly string _address;
        private readonly OutlookMailboxType _mailboxType;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private string _homeAccountId;
        private IPublicClientApplication? _client;
        private MsalCacheHelper? _cacheHelper;
        private bool _cleared;

        public OutlookImapAuthorization(string accountId, OutlookMailboxType mailboxType,
            string address, string homeAccountId)
        {
            _cachePath = AppDataPathHelper.ResolveLocal("OutlookImapAuth", OutlookImapPolicy.GetCacheFileName(accountId));
            _mailboxType = mailboxType;
            _address = address;
            _homeAccountId = homeAccountId;
        }

        public async Task<string> ConnectImapInteractivelyAsync(CancellationToken cancellationToken)
        {
            await AcquireAsync(sending: false, allowInteraction: true, cancellationToken);
            return _homeAccountId;
        }

        public Task<string> GetTokenAsync(bool sending, bool allowInteraction, CancellationToken cancellationToken)
            => AcquireAsync(sending, allowInteraction, cancellationToken);

        private async Task<string> AcquireAsync(bool sending, bool allowInteraction, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (_cleared) throw ReconnectRequired();
                var client = await GetClientAsync();
                cancellationToken.ThrowIfCancellationRequested();
                var account = string.IsNullOrWhiteSpace(_homeAccountId)
                    ? null : await client.GetAccountAsync(_homeAccountId);
                var scopes = OutlookImapPolicy.GetScopes(sending);

                async Task<AuthenticationResult> RestoreAsync(CancellationToken token)
                {
                    if (account == null) throw ReconnectRequired();
                    try { return await client.AcquireTokenSilent(scopes, account).ExecuteAsync(token); }
                    catch (MsalUiRequiredException ex) { throw ReconnectRequired(ex); }
                }

                async Task<AuthenticationResult> ConnectAsync(CancellationToken token)
                {
                    var builder = client.AcquireTokenInteractive(scopes).WithUseEmbeddedWebView(false);
                    builder = account != null
                        ? builder.WithAccount(account)
                        : builder.WithLoginHint(_address).WithPrompt(Prompt.SelectAccount);
                    return await builder.ExecuteAsync(token);
                }

                var result = await ExplicitAuthorizationFlow.AcquireAsync(
                    RestoreAsync, allowInteraction ? ConnectAsync : null, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!OutlookImapPolicy.MatchesIdentity(_homeAccountId, result.Account.HomeAccountId.Identifier,
                    _address, result.Account.Username))
                {
                    // An unsuccessful account selection must not replace a known
                    // identity or leave the other user's tokens in this cache.
                    await client.RemoveAsync(result.Account);
                    throw new OutlookImapAccountMismatchException();
                }
                _homeAccountId = result.Account.HomeAccountId.Identifier;
                return result.AccessToken;
            }
            finally { _gate.Release(); }
        }

        private async Task<IPublicClientApplication> GetClientAsync()
        {
            if (_client != null) return _client;
            var storage = new StorageCreationPropertiesBuilder(
                Path.GetFileName(_cachePath), Path.GetDirectoryName(_cachePath)!).Build();
            var helper = await MsalCacheHelper.CreateAsync(storage);
            // Windows MSAL persistence uses DPAPI. Do not fall back to plaintext.
            await Task.Run(helper.VerifyPersistence);
            var client = PublicClientApplicationBuilder.Create(Secrets.MicrosoftClientId)
                .WithAuthority(AzureCloudInstance.AzurePublic, OutlookImapPolicy.GetAuthorityTenant(_mailboxType))
                .WithRedirectUri("http://localhost")
                .Build();
            helper.RegisterCache(client.UserTokenCache);
            _cacheHelper = helper;
            _client = client;
            return client;
        }

        public async Task ClearLocalAuthorizationAsync()
        {
            await _gate.WaitAsync();
            try
            {
                if (_cleared) return;
                if (_client == null && !File.Exists(_cachePath))
                {
                    _cleared = true;
                    return;
                }
                // Only touch this account's dedicated cache. Cleanup failure is
                // propagated so the caller retains the business account for retry.
                var client = await GetClientAsync();
                foreach (var account in await client.GetAccountsAsync())
                    await client.RemoveAsync(account);
                await Task.Run(() => File.Delete(_cachePath));
                _cacheHelper!.UnregisterCache(client.UserTokenCache);
                _client = null;
                _cacheHelper = null;
                _homeAccountId = "";
                _cleared = true;
            }
            finally { _gate.Release(); }
        }

        private static AuthorizationInteractionRequiredException ReconnectRequired(Exception? inner = null)
            => new("Outlook IMAP", "Outlook IMAP authorization is required. Reconnect explicitly in Mail.", inner);
    }
}
