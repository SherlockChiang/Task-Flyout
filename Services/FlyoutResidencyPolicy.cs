namespace Task_Flyout.Services
{
    internal static class FlyoutResidencyPolicy
    {
        internal const long AutomaticPrewarmUsageCeilingBytes = 512L * 1024 * 1024;
        internal const long MinimumMemoryHeadroomBytes = 768L * 1024 * 1024;

        public static bool ShouldPrewarm(
            bool? configured,
            bool underMemoryPressure,
            long currentUsageBytes,
            long usageLimitBytes)
        {
            if (configured != true || underMemoryPressure) return false;
            if (currentUsageBytes < 0 || usageLimitBytes <= 0 || currentUsageBytes > usageLimitBytes)
                return false;
            if (currentUsageBytes > AutomaticPrewarmUsageCeilingBytes) return false;
            if (usageLimitBytes - currentUsageBytes < MinimumMemoryHeadroomBytes)
                return false;
            return true;
        }
    }
}
