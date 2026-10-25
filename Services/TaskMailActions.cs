using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Task_Flyout.Services;

internal sealed record TaskMailSource(string AccountId, string Address, MailAccountKind Kind,
    string FolderId, string MessageId, uint? UidValidity, string Subject, bool IsRead);
internal sealed record MailArchiveReceipt(MailItem Item, string ReturnFolderId,
    GmailLabelMutationResult? Gmail = null, ImapMoveResult? Imap = null);
internal enum TaskMailPhase { Ready, Archiving, Archived, Failed, Unknown, Undoing, Undone }
internal sealed class TaskMailOutcomeUnknownException(Exception inner) : Exception("Mail archive outcome is unknown.", inner);
internal sealed record TaskMailLink(string Key, string ProviderKey, string ListId, string TaskId,
    string TaskTitle, TaskMailSource Source, TaskMailPhase Phase = TaskMailPhase.Ready, MailArchiveReceipt? Receipt = null);

// Remote mail operations are independent of task completion. An uncertain move is
// never replayed automatically, including after a crash or an undo failure.
internal sealed class TaskMailActions
{
    private readonly Func<string?> _read;
    private readonly Action<string> _write;
    private readonly Func<TaskMailSource, Task<MailArchiveReceipt>> _archive;
    private readonly Func<TaskMailSource, MailArchiveReceipt, Task> _undo;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<TaskMailLink>? _links;
    public event EventHandler? Changed;
    public bool StorageFailed { get; private set; }

    public TaskMailActions(Func<string?> read, Action<string> write,
        Func<TaskMailSource, Task<MailArchiveReceipt>> archive, Func<TaskMailSource, MailArchiveReceipt, Task> undo)
    { _read = read; _write = write; _archive = archive; _undo = undo; }

    internal static string Key(string providerKey, string listId, string taskId)
        => JsonSerializer.Serialize(new[] { providerKey.ToUpperInvariant(), listId, taskId });

    private List<TaskMailLink> Links
    {
        get
        {
            if (_links != null) return _links;
            string? json = _read();
            var loaded = json == null ? new List<TaskMailLink>() : JsonSerializer.Deserialize<List<TaskMailLink>>(json)
                ?? throw new InvalidOperationException("Task mail links are unreadable.");
            _links = loaded.Select(link => link.Phase is TaskMailPhase.Archiving or TaskMailPhase.Undoing
                ? link with { Phase = TaskMailPhase.Unknown } : link).ToList();
            return _links;
        }
    }

    public IReadOnlyList<TaskMailLink> Snapshot()
    {
        try { return Links.ToArray(); }
        catch { StorageFailed = true; return Array.Empty<TaskMailLink>(); }
    }

    public async Task AddAsync(string providerKey, CreatedTaskReference task, string title, TaskMailSource source)
    {
        if (string.IsNullOrWhiteSpace(task.Id) || string.IsNullOrWhiteSpace(task.ListId)
            || string.IsNullOrWhiteSpace(source.AccountId) || string.IsNullOrWhiteSpace(source.MessageId)
            || string.IsNullOrWhiteSpace(source.FolderId)) throw new ArgumentException("A complete task and mail identity is required.");
        await _gate.WaitAsync();
        try
        {
            string key = Key(providerKey, task.ListId, task.Id);
            var next = Links.Where(link => link.Key != key).ToList();
            next.Add(new TaskMailLink(key, providerKey, task.ListId, task.Id, title, source));
            Save(next);
        }
        finally { _gate.Release(); Changed?.Invoke(this, EventArgs.Empty); }
    }

    public async Task CompleteAsync(string providerKey, string listId, string taskId)
    {
        try { await RunAsync(Key(providerKey, listId, taskId), retry: false, undo: false); }
        catch { StorageFailed = true; Changed?.Invoke(this, EventArgs.Empty); }
    }

    public Task RetryAsync(string key) => RunAsync(key, retry: true, undo: false);
    public Task UndoAsync(string key) => RunAsync(key, retry: false, undo: true);

    public async Task ReopenAsync(string providerKey, string listId, string taskId)
    {
        try
        {
            await _gate.WaitAsync();
            try
            {
                string key = Key(providerKey, listId, taskId);
                var link = Links.FirstOrDefault(value => value.Key == key);
                if (link?.Phase is TaskMailPhase.Ready or TaskMailPhase.Failed)
                    Replace(link with { Phase = TaskMailPhase.Ready });
            }
            finally { _gate.Release(); }
        }
        catch { StorageFailed = true; }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task ForgetAccountAsync(string? mailAccountId = null, string? providerKey = null)
    {
        try
        {
            await _gate.WaitAsync();
            try
            {
                Save(Links.Where(link => link.Source.AccountId != mailAccountId
                && !string.Equals(link.ProviderKey, providerKey, StringComparison.OrdinalIgnoreCase)).ToList());
            }
            finally { _gate.Release(); }
        }
        catch { StorageFailed = true; }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunAsync(string key, bool retry, bool undo)
    {
        await _gate.WaitAsync();
        try
        {
            var link = Links.FirstOrDefault(value => value.Key == key);
            if (link == null) return;
            if (undo ? link.Phase != TaskMailPhase.Archived || link.Receipt == null
                : retry ? link.Phase != TaskMailPhase.Failed : link.Phase != TaskMailPhase.Ready) return;
            try { Replace(link with { Phase = undo ? TaskMailPhase.Undoing : TaskMailPhase.Archiving }); }
            catch
            {
                // Nothing was sent. Keep an explicit retry available in this session.
                if (!undo) _links = Links.Select(value => value.Key == key ? link with { Phase = TaskMailPhase.Failed } : value).ToList();
                throw;
            }
            Changed?.Invoke(this, EventArgs.Empty);
            TaskMailLink next;
            try
            {
                if (undo)
                {
                    await _undo(link.Source, link.Receipt!);
                    next = link with { Phase = TaskMailPhase.Undone, Receipt = null };
                }
                else
                {
                    var receipt = await _archive(link.Source);
                    next = link with { Phase = TaskMailPhase.Archived, Receipt = receipt };
                }
            }
            catch (Exception ex)
            {
                bool uncertain = undo || ex is TaskMailOutcomeUnknownException or OperationCanceledException;
                next = link with { Phase = uncertain ? TaskMailPhase.Unknown : TaskMailPhase.Failed };
            }
            // Keep the authoritative result in memory if writing it fails. The
            // durable in-flight marker prevents replay on the next launch.
            try { Replace(next); }
            catch { _links = Links.Select(value => value.Key == key ? next : value).ToList(); throw; }
        }
        finally { _gate.Release(); Changed?.Invoke(this, EventArgs.Empty); }
    }

    public async Task ForgetAsync(string key)
    {
        await _gate.WaitAsync();
        try { Save(Links.Where(link => link.Key != key).ToList()); }
        finally { _gate.Release(); Changed?.Invoke(this, EventArgs.Empty); }
    }

    private void Replace(TaskMailLink link) => Save(Links.Select(value => value.Key == link.Key ? link : value).ToList());
    private void Save(List<TaskMailLink> next)
    {
        try { _write(JsonSerializer.Serialize(next)); _links = next; StorageFailed = false; }
        catch { StorageFailed = true; throw; }
    }
}
