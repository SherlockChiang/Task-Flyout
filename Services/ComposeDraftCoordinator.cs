using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Task_Flyout.Services
{
    internal sealed record ComposeDraft(
        int SchemaVersion,
        string AccountId,
        string Recipient,
        string Subject,
        string Body,
        DateTimeOffset UpdatedAt)
    {
        public const int CurrentSchemaVersion = 1;
        public bool HasContent => !string.IsNullOrWhiteSpace(Recipient)
            || !string.IsNullOrWhiteSpace(Subject)
            || !string.IsNullOrWhiteSpace(Body);
    }

    [JsonSerializable(typeof(ComposeDraft))]
    internal partial class ComposeDraftJsonContext : JsonSerializerContext { }

    internal interface IComposeDraftRepository
    {
        ComposeDraft? Load();
        Task SaveAsync(ComposeDraft draft);
        Task DeleteAsync();
    }

    internal sealed class ComposeDraftRepository : IComposeDraftRepository
    {
        private const string Scope = "mail_compose";
        private const string Key = "active";

        public ComposeDraft? Load()
        {
            try
            {
                var json = LocalSqliteStore.ReadProtectedText(Scope, Key);
                if (string.IsNullOrWhiteSpace(json)) return null;
                var draft = JsonSerializer.Deserialize(json, ComposeDraftJsonContext.Default.ComposeDraft);
                return draft?.SchemaVersion == ComposeDraft.CurrentSchemaVersion && draft.HasContent ? draft : null;
            }
            catch { return null; }
        }

        public Task SaveAsync(ComposeDraft draft)
            => LocalSqliteStore.WriteProtectedTextAsync(
                Scope,
                Key,
                JsonSerializer.Serialize(draft, ComposeDraftJsonContext.Default.ComposeDraft));

        public Task DeleteAsync()
            => LocalSqliteStore.DeleteProtectedTextAsync(Scope, Key);
    }

    internal sealed class ComposeDraftCoordinator
    {
        private readonly IComposeDraftRepository _repository;
        private readonly TimeSpan _debounce;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly object _stateLock = new();
        private CancellationTokenSource? _debounceCts;
        private ComposeDraft? _pending;
        private long _generation;
        private bool _hasPendingOperation;

        public ComposeDraftCoordinator(IComposeDraftRepository? repository = null, TimeSpan? debounce = null)
        {
            _repository = repository ?? new ComposeDraftRepository();
            _debounce = debounce ?? TimeSpan.FromMilliseconds(600);
        }

        public Task<ComposeDraft?> LoadAsync() => Task.Run(_repository.Load);

        public void Schedule(ComposeDraft draft)
        {
            CancellationTokenSource cts;
            long generation;
            lock (_stateLock)
            {
                _pending = draft.HasContent ? draft : null;
                _hasPendingOperation = true;
                generation = ++_generation;
                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                cts = new CancellationTokenSource();
                _debounceCts = cts;
            }
            _ = PersistAfterDelayAsync(generation, _pending, cts.Token);
        }

        public async Task FlushAsync()
        {
            ComposeDraft? pending;
            long generation;
            lock (_stateLock)
            {
                if (!_hasPendingOperation) return;
                _debounceCts?.Cancel();
                pending = _pending;
                generation = _generation;
            }
            await PersistAsync(generation, pending);
        }

        public async Task DiscardAsync()
        {
            long generation;
            lock (_stateLock)
            {
                _debounceCts?.Cancel();
                _pending = null;
                _hasPendingOperation = true;
                generation = ++_generation;
            }
            await PersistAsync(generation, null);
        }

        public async Task DiscardForAccountAsync(string accountId)
        {
            var draft = await LoadAsync();
            lock (_stateLock)
                draft = _pending ?? draft;
            if (draft != null && string.Equals(draft.AccountId, accountId, StringComparison.Ordinal))
                await DiscardAsync();
        }

        private async Task PersistAfterDelayAsync(long generation, ComposeDraft? draft, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(_debounce, cancellationToken);
                await PersistAsync(generation, draft);
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        private async Task PersistAsync(long generation, ComposeDraft? draft)
        {
            await _gate.WaitAsync();
            try
            {
                lock (_stateLock)
                {
                    if (generation != _generation) return;
                }
                if (draft == null) await _repository.DeleteAsync();
                else await _repository.SaveAsync(draft);
            }
            finally { _gate.Release(); }
        }
    }
}
