using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class SyncProviderCapabilityPolicyTests
{
    [Theory]
    [InlineData("Google", true, true)]
    [InlineData("Microsoft", true, true)]
    [InlineData("iCloud", true, false)]
    [InlineData("unknown", false, false)]
    public void Returns_provider_specific_capabilities(string provider, bool events, bool tasks)
    {
        var capabilities = SyncProviderCapabilityPolicy.ForProvider(provider);

        Assert.Equal(events, capabilities.SupportsEvents);
        Assert.Equal(tasks, capabilities.SupportsTasks);
    }
}
