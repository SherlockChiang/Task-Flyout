using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class MailPersistentCachePolicyTests
{
    [Fact]
    public void Normalize_initializes_null_collections_and_marks_cache_dirty()
    {
        var cache = new MailPersistentCache
        {
            Folders = null!, Messages = null!, MessageCursors = null!, MessageHasMore = null!,
            PendingMutations = null!, LastSeenInboxTicks = null!, AccountOrder = null!, FolderOrder = null!
        };

        Assert.True(MailPersistentCachePolicy.Normalize(cache, DateTimeOffset.UtcNow, 200));
        Assert.NotNull(cache.Messages);
        Assert.NotNull(cache.PendingMutations);
        Assert.NotNull(cache.FolderOrder);
    }

    [Fact]
    public void Normalize_migrates_legacy_keys_deduplicates_and_keeps_newest_message()
    {
        var older = Item("same", 1);
        var newer = Item("same", 2);
        var cache = new MailPersistentCache();
        cache.Messages["account|folder|False"] = new() { older };
        cache.Messages["account|folder|False|25"] = new() { newer };

        Assert.True(MailPersistentCachePolicy.Normalize(cache, DateTimeOffset.UtcNow, 200));

        Assert.False(cache.Messages.ContainsKey("account|folder|False|25"));
        var retained = Assert.Single(cache.Messages["account|folder|False"]);
        Assert.Equal(newer.Id, retained.Id);
        Assert.Equal(newer.RawReceivedTime, retained.RawReceivedTime);
    }

    [Fact]
    public void Normalize_strips_bodies_limits_window_and_reports_dirty_snapshot()
    {
        var cache = new MailPersistentCache();
        cache.Messages["window"] = new() { Item("old", 1, "secret body"), Item("new", 2, "new secret") };

        Assert.True(MailPersistentCachePolicy.Normalize(cache, DateTimeOffset.UtcNow, maximumMessages: 1));

        var retained = Assert.Single(cache.Messages["window"]);
        Assert.Equal("new", retained.Id);
        Assert.Equal("", retained.BodyText);
        Assert.Equal("", retained.HtmlBody);
    }

    [Fact]
    public void Normalize_removes_expired_mutations_and_marks_dirty()
    {
        var now = DateTimeOffset.UtcNow;
        var cache = new MailPersistentCache();
        cache.PendingMutations.Add(new PendingMailMutation { CreatedUtcTicks = now.AddDays(-8).UtcTicks });

        Assert.True(MailPersistentCachePolicy.Normalize(cache, now, 200));
        Assert.Empty(cache.PendingMutations);
    }

    [Fact]
    public void Imap_window_requires_uid_validity_on_every_item()
    {
        Assert.False(MailPersistentCachePolicy.CanUseWindow(MailAccountKind.Imap, new[] { new MailItem { ImapUidValidity = null } }));
        Assert.True(MailPersistentCachePolicy.CanUseWindow(MailAccountKind.Imap, new[] { new MailItem { ImapUidValidity = 42 } }));
        Assert.True(MailPersistentCachePolicy.CanUseWindow(MailAccountKind.Google, new[] { new MailItem() }));
    }

    [Fact]
    public void Already_normalized_cache_is_not_marked_dirty()
    {
        var cache = new MailPersistentCache();
        cache.Messages["account|folder|False"] = new() { Item("message", 1) };

        Assert.False(MailPersistentCachePolicy.Normalize(cache, DateTimeOffset.UtcNow, 200));
    }

    private static MailItem Item(string id, int minute, string body = "")
        => new() { Id = id, RawReceivedTime = DateTimeOffset.UnixEpoch.AddMinutes(minute), BodyText = body, HtmlBody = body };
}
