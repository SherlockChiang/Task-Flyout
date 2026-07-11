using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Task_Flyout.Services
{
    public class MailFolder
    {
        public string AccountId { get; set; } = "";
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int? UnreadCount { get; set; }
        public bool IsPlaceholder { get; set; }
        public string CountText => UnreadCount.HasValue && UnreadCount.Value > 0 ? UnreadCount.Value.ToString() : "";
    }

    public class MailItem : INotifyPropertyChanged
    {
        private bool _isRead;
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
        public bool HasAttachments { get; set; }
        public string Importance { get; set; } = "";
        public string WebLink { get; set; } = "";
        public string ReadMarker => IsRead ? "" : "●";
        public string AttachmentMarker => HasAttachments ? "📎" : "";
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class MailPersistentCache
    {
        public Dictionary<string, List<MailFolder>> Folders { get; set; } = new();
        public Dictionary<string, List<MailItem>> Messages { get; set; } = new();
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
