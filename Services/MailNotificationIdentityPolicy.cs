using System;
using System.Security.Cryptography;
using System.Text;

namespace Task_Flyout.Services
{
    internal static class MailNotificationIdentityPolicy
    {
        public const string Group = "mail";

        public static string BuildTag(MailAccountKind providerKind, MailItem item)
        {
            string providerIdentity = providerKind == MailAccountKind.Imap
                ? $"{item.AccountId}\u001f{(int)providerKind}\u001f{item.FolderId}\u001f{item.ImapUidValidity?.ToString() ?? "?"}\u001f{item.Id}"
                : $"{item.AccountId}\u001f{(int)providerKind}\u001f{item.Id}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(providerIdentity));
            return Convert.ToHexString(hash.AsSpan(0, 8));
        }
    }
}
