using System;
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
        public static ImageSource? UriToImageSource(string uri)
        {
            if (string.IsNullOrEmpty(uri)) return null;
            try { return new BitmapImage(new Uri(uri)); }
            catch { return null; }
        }

        public static Visibility UriToVisible(string uri) =>
            string.IsNullOrEmpty(uri) ? Visibility.Collapsed : Visibility.Visible;

        public static Visibility UriToCollapsed(string uri) =>
            string.IsNullOrEmpty(uri) ? Visibility.Visible : Visibility.Collapsed;
    }
}
