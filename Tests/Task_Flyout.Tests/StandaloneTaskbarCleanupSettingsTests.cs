using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class StandaloneTaskbarCleanupSettingsTests
{
    [Fact]
    public void Missing_or_wrong_typed_values_are_not_cleanup_leases()
    {
        var values = new Dictionary<string, object>();
        Assert.False(StandaloneTaskbarCleanupSettings.IsPending(values));

        values[StandaloneTaskbarCleanupSettings.SettingKey] = "true";
        Assert.False(StandaloneTaskbarCleanupSettings.IsPending(values));
    }

    [Fact]
    public void Lease_is_marked_and_only_explicitly_cleared()
    {
        var values = new Dictionary<string, object>();

        StandaloneTaskbarCleanupSettings.MarkPending(values);
        Assert.True(StandaloneTaskbarCleanupSettings.IsPending(values));

        StandaloneTaskbarCleanupSettings.Clear(values);
        Assert.False(StandaloneTaskbarCleanupSettings.IsPending(values));
        Assert.DoesNotContain(StandaloneTaskbarCleanupSettings.SettingKey, values.Keys);
    }
}
