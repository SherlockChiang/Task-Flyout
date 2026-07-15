namespace Task_Flyout.Services
{
    internal readonly record struct TaskMutationStatusDescriptor(
        string ResourceKey,
        string FallbackText,
        bool IsError);

    internal static class TaskMutationStatusPolicy
    {
        public static TaskMutationStatusDescriptor Describe(TaskMutationPhase phase)
            => phase switch
            {
                TaskMutationPhase.Queued => new("TextTaskMutationQueued", "Task change queued...", false),
                TaskMutationPhase.Pending => new("TextTaskMutationPending", "Saving task change...", false),
                TaskMutationPhase.Failed => new("TextTaskMutationFailed", "Task change failed. Retry is available.", true),
                _ => new("TextTaskMutationSucceeded", "Task change saved.", false)
            };
    }
}
