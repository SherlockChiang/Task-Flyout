using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    public enum SyncFailureKind { None, Authorization, Network, Timeout, Storage, Unknown }
    internal enum AccountSyncState { Unknown, Syncing, Ready, Canceled, Failed }

    internal sealed record AccountSyncHealth(
        AccountSyncState State,
        DateTimeOffset? LastAttemptUtc,
        DateTimeOffset? LastSuccessUtc,
        SyncFailureKind Failure = SyncFailureKind.None);

    internal static class AccountSyncSelection
    {
        public static List<T> Select<T>(IEnumerable<T> accounts, Func<T, string> key, Func<T, bool> connected, string? selectedKey)
            => accounts.Where(account => connected(account) && (selectedKey == null ||
                string.Equals(key(account), selectedKey, StringComparison.OrdinalIgnoreCase))).ToList();

        public static bool MatchesMailbox(string expected, string? address, string? principalName)
            => !string.IsNullOrWhiteSpace(expected) &&
                (string.Equals(expected.Trim(), address?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(expected.Trim(), principalName?.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    internal static class SyncFailurePolicy
    {
        public static SyncFailureKind Classify(Exception exception) => exception switch
        {
            AuthorizationInteractionRequiredException => SyncFailureKind.Authorization,
            HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden }
                => SyncFailureKind.Authorization,
            OperationCanceledException or TimeoutException => SyncFailureKind.Timeout,
            HttpRequestException or SocketException => SyncFailureKind.Network,
            IOException or UnauthorizedAccessException => SyncFailureKind.Storage,
            _ => SyncFailureKind.Unknown
        };
    }

    // Session-only observations. A cache read never invents a successful sync time.
    // Newer requests own the visible state even if older work finishes afterwards.
    internal sealed class AccountSyncHealthTracker
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, (long Generation, AccountSyncHealth Health)> _states;
        private long _generation;
        public event EventHandler? Changed;

        public AccountSyncHealthTracker(StringComparer? comparer = null) => _states = new(comparer ?? StringComparer.Ordinal);

        public AccountSyncHealth Get(string accountKey)
        {
            lock (_gate)
                return _states.TryGetValue(accountKey, out var entry)
                    ? entry.Health : new(AccountSyncState.Unknown, null, null);
        }

        public long Begin(string accountKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(accountKey);
            long generation;
            lock (_gate)
            {
                generation = ++_generation;
                _states[accountKey] = (generation, new(AccountSyncState.Syncing, DateTimeOffset.UtcNow, Get(accountKey).LastSuccessUtc));
            }
            Notify();
            return generation;
        }

        public void Complete(string accountKey, long generation, SyncFailureKind failure = SyncFailureKind.None, bool canceled = false)
        {
            lock (_gate)
            {
                if (!_states.TryGetValue(accountKey, out var current) || current.Generation != generation) return;
                _states[accountKey] = (generation, current.Health with
                {
                    State = canceled ? AccountSyncState.Canceled : failure == SyncFailureKind.None ? AccountSyncState.Ready : AccountSyncState.Failed,
                    LastSuccessUtc = !canceled && failure == SyncFailureKind.None ? DateTimeOffset.UtcNow : current.Health.LastSuccessUtc,
                    Failure = canceled ? SyncFailureKind.None : failure
                });
            }
            Notify();
        }

        public void Remove(string accountKey)
        {
            lock (_gate) _states.Remove(accountKey);
            Notify();
        }

        public async Task<T> RunAsync<T>(string accountKey, Func<Task<T>> operation, CancellationToken cancellationToken = default,
            Func<Exception, SyncFailureKind>? classify = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long generation = Begin(accountKey);
            try
            {
                T result = await operation();
                cancellationToken.ThrowIfCancellationRequested();
                Complete(accountKey, generation);
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Complete(accountKey, generation, canceled: true);
                throw;
            }
            catch (Exception ex)
            {
                Complete(accountKey, generation, (classify ?? SyncFailurePolicy.Classify)(ex));
                throw;
            }
        }

        private void Notify()
        {
            if (Changed is not { } handlers) return;
            foreach (EventHandler handler in handlers.GetInvocationList())
            {
                try { handler(this, EventArgs.Empty); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Sync status observer failed: {ex.GetType().Name}"); }
            }
        }
    }
}
