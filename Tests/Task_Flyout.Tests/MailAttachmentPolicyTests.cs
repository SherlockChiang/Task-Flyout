using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class MailAttachmentPolicyTests
{
    [Fact]
    public void Accepts_files_at_exact_limits()
    {
        var files = Enumerable.Range(0, 3)
            .Select(index => new MailAttachmentData($"file-{index}.bin", "application/octet-stream", new byte[MailAttachmentPolicy.MaximumFileBytes]))
            .ToList();
        Assert.Null(MailAttachmentPolicy.Validate(files));
    }

    [Fact]
    public void Rejects_empty_oversize_total_and_count()
    {
        Assert.NotNull(MailAttachmentPolicy.Validate(new[] { Attachment(0) }));
        Assert.NotNull(MailAttachmentPolicy.Validate(new[] { Attachment(MailAttachmentPolicy.MaximumFileBytes + 1) }));
        Assert.NotNull(MailAttachmentPolicy.Validate(Enumerable.Range(0, 4).Select(_ => Attachment(MailAttachmentPolicy.MaximumFileBytes))));
        Assert.NotNull(MailAttachmentPolicy.Validate(Enumerable.Range(0, 11).Select(_ => Attachment(1))));
    }

    [Fact]
    public void Normalizes_paths_and_control_characters()
    {
        Assert.Equal("secret.txt", MailAttachmentPolicy.NormalizeFileName("C:\\private\\sec\0ret.txt"));
        Assert.NotNull(MailAttachmentPolicy.Validate(new[] { new MailAttachmentData("C:\\private\\secret.txt", "application/octet-stream", new byte[1]) }));
    }

    private static MailAttachmentData Attachment(long size)
        => new("file.bin", "application/octet-stream", new byte[checked((int)size)]);
}
