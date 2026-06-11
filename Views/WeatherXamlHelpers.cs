using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Task_Flyout.Views
{
    /// <summary>
    /// Helpers for x:Bind in weather DataTemplates. Converts string URIs coming from
    /// IconPackService into ImageSource / Visibility values without needing a
    /// DependencyProperty converter.
    /// </summary>
    public static class WeatherXamlHelpers
    {
        private const int TemplateIconDecodePixelWidth = 64;
        private const int MaxTemplateIconCacheSize = 128;
        private static readonly Dictionary<string, BitmapImage> TemplateIconCache = new(StringComparer.Ordinal);

        public static ImageSource? UriToImageSource(string uri)
        {
            if (string.IsNullOrEmpty(uri)) return null;
            if (TemplateIconCache.TryGetValue(uri, out var cached)) return cached;

            try
            {
                if (TemplateIconCache.Count >= MaxTemplateIconCacheSize)
                    TemplateIconCache.Clear();

                var image = new BitmapImage(new Uri(uri))
                {
                    DecodePixelWidth = TemplateIconDecodePixelWidth
                };
                TemplateIconCache[uri] = image;
                return image;
            }
            catch { return null; }
        }

        public static Visibility UriToVisible(string uri) =>
            string.IsNullOrEmpty(uri) ? Visibility.Collapsed : Visibility.Visible;

        public static Visibility UriToCollapsed(string uri) =>
            string.IsNullOrEmpty(uri) ? Visibility.Visible : Visibility.Collapsed;
    }
}
