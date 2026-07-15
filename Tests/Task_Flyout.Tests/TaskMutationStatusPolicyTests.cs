using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class TaskMutationStatusPolicyTests
{
    [Theory]
    [InlineData(TaskMutationPhase.Queued, "TextTaskMutationQueued", false)]
    [InlineData(TaskMutationPhase.Pending, "TextTaskMutationPending", false)]
    [InlineData(TaskMutationPhase.Succeeded, "TextTaskMutationSucceeded", false)]
    [InlineData(TaskMutationPhase.Failed, "TextTaskMutationFailed", true)]
    public void Describes_each_mutation_phase(TaskMutationPhase phase, string resourceKey, bool isError)
    {
        var result = TaskMutationStatusPolicy.Describe(phase);

        Assert.Equal(resourceKey, result.ResourceKey);
        Assert.Equal(isError, result.IsError);
        Assert.False(string.IsNullOrWhiteSpace(result.FallbackText));
    }
}
