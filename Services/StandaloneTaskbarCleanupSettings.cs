using System;
using System.Collections.Generic;

namespace Task_Flyout.Services;

internal static class StandaloneTaskbarCleanupSettings
{
    public const string SettingKey = "StandaloneTaskbarCleanupPending";

    public static bool IsPending(IDictionary<string, object> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.TryGetValue(SettingKey, out object? value) &&
            value is true;
    }

    public static void MarkPending(IDictionary<string, object> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        values[SettingKey] = true;
    }

    public static void Clear(IDictionary<string, object> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        values.Remove(SettingKey);
    }
}
