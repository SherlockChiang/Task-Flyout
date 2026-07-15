using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class TaskEditCapabilityPolicyTests
{
    [Theory]
    [InlineData("Google")]
    [InlineData("Microsoft")]
    public void Connected_provider_contract_supports_only_round_trip_fields(string provider)
    {
        var capabilities = TaskEditCapabilityPolicy.ForProvider(provider);
        Assert.True(capabilities.SupportsTitle);
        Assert.True(capabilities.SupportsDueDate);
        Assert.True(capabilities.SupportsNotes);
        Assert.True(capabilities.SupportsCompletion);
        Assert.True(capabilities.SupportsDeletion);
        Assert.False(capabilities.SupportsDueTime);
        Assert.False(capabilities.SupportsRecurrence);
        Assert.False(capabilities.SupportsProviderMove);
        Assert.False(capabilities.SupportsListMove);
    }

    [Fact]
    public void Unknown_provider_exposes_no_mutations()
        => Assert.False(TaskEditCapabilityPolicy.ForProvider("Local").SupportsTitle);
}
