using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Task_Flyout.Services
{
    public sealed class MailAttachmentData
    {
        public MailAttachmentData(string fileName, string contentType, byte[] content)
        {
            FileName = fileName;
            ContentType = contentType;
            Content = content;
        }

        public string FileName { get; set; }
        public string ContentType { get; set; }
        public byte[] Content { get; set; }
        public long Size => Content?.LongLength ?? 0;
        public string SizeText => MailAttachmentPolicy.FormatSize(Size);
    }

    public enum MailSendStage { Preparing, UploadingAttachments, Sending, Confirming }
    public sealed record MailSendProgress(MailSendStage Stage, int CompletedFiles, int TotalFiles);
    internal enum MailAttachmentValidationError { TooMany, Empty, FileTooLarge, InvalidFileName, TotalTooLarge }

    internal static class MailAttachmentPolicy
    {
        public const int MaximumCount = 10;
        public const long MaximumFileBytes = 3L * 1024 * 1024;
        public const long MaximumTotalBytes = 10L * 1024 * 1024;

        public static MailAttachmentValidationError? Validate(IEnumerable<MailAttachmentData> attachments)
        {
            var list = attachments.ToList();
            if (list.Count > MaximumCount) return MailAttachmentValidationError.TooMany;
            long total = 0;
            foreach (var attachment in list)
            {
                if (attachment.Content == null || attachment.Size <= 0) return MailAttachmentValidationError.Empty;
                if (attachment.Size > MaximumFileBytes) return MailAttachmentValidationError.FileTooLarge;
                if (string.IsNullOrWhiteSpace(attachment.FileName)) return MailAttachmentValidationError.InvalidFileName;
                if (!string.Equals(attachment.FileName, NormalizeFileName(attachment.FileName), StringComparison.Ordinal))
                    return MailAttachmentValidationError.InvalidFileName;
                try { total = checked(total + attachment.Size); }
                catch (OverflowException) { return MailAttachmentValidationError.TotalTooLarge; }
            }
            return total > MaximumTotalBytes ? MailAttachmentValidationError.TotalTooLarge : null;
        }

        public static string NormalizeFileName(string? value)
        {
            var name = Path.GetFileName(value ?? "").Trim();
            name = new string(name.Where(character => !char.IsControl(character)).ToArray());
            return string.IsNullOrWhiteSpace(name) ? "attachment.bin" : name;
        }

        public static string FormatSize(long bytes)
            => bytes >= 1024 * 1024
                ? $"{bytes / (1024d * 1024d):0.##} MB"
                : $"{Math.Max(0, bytes) / 1024d:0.##} KB";
    }
}
