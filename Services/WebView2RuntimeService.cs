using System;

namespace Task_Flyout.Services
{
    internal static class WebView2RuntimeService
    {
        private static bool _configured;

        public static void ConfigureSharedRuntime()
        {
            if (_configured) return;

            var cachePath = AppDataPathHelper.EnsureDirectory(AppDataPathHelper.ResolveLocal("WebView2Cache"));
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", cachePath, EnvironmentVariableTarget.Process);
            _configured = true;
        }
    }
}
