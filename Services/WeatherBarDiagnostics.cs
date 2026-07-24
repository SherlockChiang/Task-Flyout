namespace Task_Flyout.Services
{
    public sealed record WeatherBarDiagnostics(
        string TaskbarClass,
        string WidgetsBridgeSource,
        string MonitorRect,
        uint Dpi,
        string TaskbarRect,
        string BarRect,
        string FallbackReason)
    {
        public static WeatherBarDiagnostics Unavailable(string reason = "Weather bar is not running")
            => new("Not attached", "None", "Unavailable", 0, "Unavailable", "Unavailable", reason);
    }
}
