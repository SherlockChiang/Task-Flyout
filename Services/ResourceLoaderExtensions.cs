using Microsoft.Windows.ApplicationModel.Resources;
using System;

namespace Task_Flyout.Services
{
    internal static class ResourceLoaderExtensions
    {
        public static string? GetStringOrDefault(this ResourceLoader loader, string resourceId)
        {
            var value = TryGetString(loader, resourceId);
            if (string.IsNullOrWhiteSpace(value) && resourceId.Contains('.'))
                value = TryGetString(loader, resourceId.Replace('.', '/'));
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        public static string GetStringOrDefault(this ResourceLoader loader, string resourceId, string fallback)
            => loader.GetStringOrDefault(resourceId) ?? fallback;

        private static string? TryGetString(ResourceLoader loader, string resourceId)
        {
            try { return loader.GetString(resourceId); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Resource lookup failed for {resourceId}: {ex.Message}");
                return null;
            }
        }
    }
}
