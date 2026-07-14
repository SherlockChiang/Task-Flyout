using System;

namespace Task_Flyout.Services
{
    internal static class OnboardingPolicy
    {
        public const int CurrentVersion = 1;
        public const string CompletedVersionKey = "OnboardingVersionCompleted";
        public const string LegacyCompletedKey = "OnboardingChecklistCompleted";

        public static bool ShouldShow(object? completedVersion, object? legacyCompleted)
        {
            if (TryReadVersion(completedVersion, out int version) && version >= CurrentVersion)
                return false;

            return legacyCompleted is not true;
        }

        private static bool TryReadVersion(object? value, out int version)
        {
            switch (value)
            {
                case int number:
                    version = number;
                    return true;
                case long number when number >= int.MinValue && number <= int.MaxValue:
                    version = (int)number;
                    return true;
                default:
                    version = 0;
                    return false;
            }
        }
    }
}
