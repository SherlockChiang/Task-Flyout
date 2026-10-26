namespace Task_Flyout.Services;

internal enum PageShortcut { None, Calendar, Tasks, Weather, Mail, Rss, New, Edit, Refresh }

internal static class KeyboardShortcutPolicy
{
    public const string CharacterShortcutsSetting = "CharacterShortcutsEnabled";

    // Key values follow Windows VirtualKey; keep resolution independent of the UI.
    internal static PageShortcut Resolve(int key, bool control, bool shift, bool alt, bool windows,
        bool blocked, bool inputFocused, bool characterShortcutsEnabled, bool repeat)
    {
        if (blocked || inputFocused || repeat || shift || alt || windows) return PageShortcut.None;
        if (control) return key switch
        {
            49 => PageShortcut.Calendar,
            50 => PageShortcut.Tasks,
            51 => PageShortcut.Weather,
            52 => PageShortcut.Mail,
            53 => PageShortcut.Rss,
            _ => PageShortcut.None
        };
        if (!characterShortcutsEnabled) return PageShortcut.None;
        return key switch { 78 => PageShortcut.New, 69 => PageShortcut.Edit, 82 => PageShortcut.Refresh, _ => PageShortcut.None };
    }
}
