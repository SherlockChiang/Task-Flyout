using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Task_Flyout.Services
{
    [Flags]
    internal enum MailCacheRefreshKind
    {
        None = 0,
        Folders = 1,
        Messages = 2
    }

    internal readonly record struct MailCacheRefreshScope(
        string AccountId,
        string? FolderId,
        MailCacheRefreshKind Kind);

    internal sealed class MailCachePublishedEventArgs : EventArgs
    {
        public MailCachePublishedEventArgs(long version, MailCacheRefreshScope scope)
        {
            Version = version;
            Scope = scope;
        }

        public long Version { get; }
        public MailCacheRefreshScope Scope { get; }
    }

    public class MailFolder
    {
        public string AccountId { get; set; } = "";
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int? UnreadCount { get; set; }
        public bool IsPlaceholder { get; set; }
        public bool IsUserLabel { get; set; }
        public string CountText => UnreadCount.HasValue && UnreadCount.Value > 0 ? UnreadCount.Value.ToString() : "";
    }

    public sealed class OutlookMoveDestination
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Breadcrumb { get; set; } = "";
        public override string ToString() => Breadcrumb;
    }

    public sealed record MailMoveResult(MailItem Item, string SourceFolderId, string DestinationFolderId);

    public sealed class ImapMoveDestination
    {
        public string FullName { get; set; } = "";
        public string Breadcrumb { get; set; } = "";
        public override string ToString() => Breadcrumb;
    }

    public sealed record ImapMessageIdentity(string FolderFullName, uint UidValidity, uint Uid);
    public sealed record ImapMoveResult(MailItem Item, ImapMessageIdentity Source, ImapMessageIdentity Destination);

    public sealed class GmailLabelDestination
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool IsUserLabel { get; set; }
        public override string ToString() => DisplayName;
    }

    public enum GmailOnlineActionKind { Archive, MoveToLabel, Trash }

    public sealed record GmailLabelMutationResult(
        MailItem Item,
        GmailOnlineActionKind Kind,
        string SourceLabelId,
        string? DestinationLabelId,
        IReadOnlyList<string> AddedLabelIds,
        IReadOnlyList<string> RemovedLabelIds,
        IReadOnlyList<string> BeforeLabelIds,
        IReadOnlyList<string> AfterLabelIds,
        bool CanRestoreSourceLabel,
        bool RemovedFromSource);

    public class MailItem : INotifyPropertyChanged
    {
        private bool _isRead;
        private bool _isFlagged;
        public string AccountId { get; set; } = "";
        public string FolderId { get; set; } = "";
        public string Id { get; set; } = "";
        public uint? ImapUidValidity { get; set; }
        public string Subject { get; set; } = "";
        public string Sender { get; set; } = "";
        public string SenderAddress { get; set; } = "";
        public string Recipient { get; set; } = "";
        public string Preview { get; set; } = "";
        public string BodyText { get; set; } = "";
        public string HtmlBody { get; set; } = "";
        public string ReceivedTime { get; set; } = "";
        public DateTimeOffset? RawReceivedTime { get; set; }
        public bool IsRead { get => _isRead; set { if (_isRead == value) return; _isRead = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReadMarker)); } }
        public bool IsFlagged { get => _isFlagged; set { if (_isFlagged == value) return; _isFlagged = value; OnPropertyChanged(); OnPropertyChanged(nameof(FlagMarker)); } }
        public bool HasAttachments { get; set; }
        public string Importance { get; set; } = "";
        public string WebLink { get; set; } = "";
        public string ReadMarker => IsRead ? "" : "●";
        public string FlagMarker => IsFlagged ? "★" : "";
        public string AttachmentMarker => HasAttachments ? "📎" : "";
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class MailPersistentCache
    {
        public Dictionary<string, List<MailFolder>> Folders { get; set; } = new();
        public Dictionary<string, long> FolderFetchedUtcTicks { get; set; } = new();
        public Dictionary<string, List<MailItem>> Messages { get; set; } = new();
        public Dictionary<string, long> MessageFetchedUtcTicks { get; set; } = new();
        public Dictionary<string, MailCursor> MessageCursors { get; set; } = new();
        public Dictionary<string, bool> MessageHasMore { get; set; } = new();
        public List<PendingMailMutation> PendingMutations { get; set; } = new();
        public Dictionary<string, long> LastSeenInboxTicks { get; set; } = new();
        public List<string> AccountOrder { get; set; } = new();
        public Dictionary<string, List<string>> FolderOrder { get; set; } = new();
    }

    public sealed class MailCursor
    {
        public MailAccountKind ProviderKind { get; set; }
        public string Value { get; set; } = "";
        public uint? UidValidity { get; set; }
        public uint? BeforeUid { get; set; }
    }

    public sealed class MailMessageWindow { public List<MailItem> Items { get; set; } = new(); public bool HasMore { get; set; } }
}
