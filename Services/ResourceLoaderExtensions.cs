using Microsoft.Windows.ApplicationModel.Resources;
using System;

namespace Task_Flyout.Services
{
    internal static class ResourceLoaderExtensions
    {
        public static string? GetStringOrDefault(this ResourceLoader loader, string resourceId)
        {
            try
            {
                var value = loader.GetString(resourceId);
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Resource lookup failed for {resourceId}: {ex.Message}");
                return null;
            }
        }

        public static string GetStringOrDefault(this ResourceLoader loader, string resourceId, string fallback)
            => loader.GetStringOrDefault(resourceId) ?? fallback;
    }
}
