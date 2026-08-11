using System;

namespace Task_Flyout.Services;

internal static class StandaloneTaskbarLaunchPolicy
{
    public const string DisableEnvironmentVariable =
        "TASKFLYOUT_DISABLE_STANDALONE_TASKBAR";

    public static bool IsSuppressed(
        string? activationArgument,
        string? disableEnvironmentValue)
    {
        if (string.Equals(
                disableEnvironmentValue?.Trim(),
                "1",
                StringComparison.Ordinal))
        {
            return true;
        }

        var arguments = NotificationActivationParser.ParseArguments(
            activationArgument);
        return arguments.TryGetValue("action", out string? action) &&
            string.Equals(action, "smoke", StringComparison.Ordinal);
    }
}
