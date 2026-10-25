using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Task_Flyout.Services;

internal sealed class TaskMailArchiveService(MailService mail)
{
    private MailAccount Account(TaskMailSource source)
        => mail.GetAccounts().SingleOrDefault(account => account.Id == source.AccountId
            && account.Kind == source.Kind && account.IsSetupComplete
            && string.Equals(account.Address, source.Address, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The source mail account is no longer available.");

    public async Task<MailArchiveReceipt> ArchiveAsync(TaskMailSource source)
    {
        _ = Account(source);
        try { return await ArchiveCoreAsync(source); }
        catch (Exception ex) when (ex is not AuthorizationInteractionRequiredException and not NotSupportedException)
        { throw new TaskMailOutcomeUnknownException(ex); }
    }

    private async Task<MailArchiveReceipt> ArchiveCoreAsync(TaskMailSource source)
    {
        var account = Account(source);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var item = new MailItem
        {
            AccountId = source.AccountId,
            FolderId = source.FolderId,
            Id = source.MessageId,
            ImapUidValidity = source.UidValidity,
            Subject = source.Subject,
            IsRead = source.IsRead
        };
        switch (source.Kind)
        {
            case MailAccountKind.Google:
                var gmail = await mail.ArchiveGmailMessageAsync(account, item, timeout.Token);
                return new(gmail.Item, source.FolderId, Gmail: gmail);
            case MailAccountKind.Outlook:
                var outlook = await mail.ArchiveOutlookMessageAsync(account, item, timeout.Token);
                return new(outlook.Item, outlook.SourceFolderId);
            case MailAccountKind.Imap:
                var imap = await mail.ArchiveImapMessageAsync(account, item, timeout.Token);
                return new(imap.Item, imap.Source.FolderFullName, Imap: imap);
            default: throw new NotSupportedException();
        }
    }

    public async Task UndoAsync(TaskMailSource source, MailArchiveReceipt receipt)
    {
        var account = Account(source);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        switch (source.Kind)
        {
            case MailAccountKind.Google when receipt.Gmail != null:
                await mail.UndoGmailLabelMutationAsync(account, receipt.Gmail, timeout.Token);
                break;
            case MailAccountKind.Outlook:
                await mail.MoveOutlookMessageAsync(account, receipt.Item, receipt.ReturnFolderId, timeout.Token);
                break;
            case MailAccountKind.Imap when receipt.Imap != null:
                await mail.UndoImapMoveAsync(account, receipt.Imap, timeout.Token);
                break;
            default: throw new InvalidOperationException("The archive receipt is incomplete.");
        }
    }
}
