using System;

namespace Task_Flyout.Services
{
    internal enum WeatherBarRecoveryAction
    {
        Stop,
        WaitForTaskbar,
        DiscardAndWaitForTaskbar,
        Healthy,
        Reattach,
        Recreate,
        Cooldown
    }

    internal static class WeatherBarRecoveryPolicy
    {
        public static WeatherBarRecoveryAction Decide(
            bool enabled,
            bool taskbarAvailable,
            bool barExists,
            bool barAlive,
            bool attachedToCurrentTaskbar,
            bool reattachAttempted,
            TimeSpan elapsedSinceRecreation,
            TimeSpan recreationCooldown)
        {
            if (!enabled)
                return WeatherBarRecoveryAction.Stop;
            if (!taskbarAvailable)
            {
                return barExists && !barAlive
                    ? WeatherBarRecoveryAction.DiscardAndWaitForTaskbar
                    : WeatherBarRecoveryAction.WaitForTaskbar;
            }
            if (barExists && barAlive && attachedToCurrentTaskbar)
                return WeatherBarRecoveryAction.Healthy;
            if (barExists && barAlive && !reattachAttempted)
                return WeatherBarRecoveryAction.Reattach;

            elapsedSinceRecreation = elapsedSinceRecreation < TimeSpan.Zero
                ? TimeSpan.Zero
                : elapsedSinceRecreation;
            recreationCooldown = recreationCooldown < TimeSpan.Zero
                ? TimeSpan.Zero
                : recreationCooldown;
            return elapsedSinceRecreation >= recreationCooldown
                ? WeatherBarRecoveryAction.Recreate
                : WeatherBarRecoveryAction.Cooldown;
        }
    }
}
