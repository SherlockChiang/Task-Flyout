using Task_Flyout.Services;

namespace Task_Flyout.Tests;

public class KeyboardShortcutPolicyTests
{
    [Theory]
    [InlineData(49, true, "Calendar")]
    [InlineData(50, true, "Tasks")]
    [InlineData(51, true, "Weather")]
    [InlineData(52, true, "Mail")]
    [InlineData(53, true, "Rss")]
    [InlineData(78, false, "New")]
    [InlineData(69, false, "Edit")]
    [InlineData(82, false, "Refresh")]
    public void Supported_keys_map_to_the_documented_action(int key, bool control, string expected)
        => Assert.Equal(expected, Resolve(key, control).ToString());

    [Theory]
    [InlineData(49, true)]
    [InlineData(78, false)]
    [InlineData(69, false)]
    [InlineData(82, false)]
    public void Typing_popups_and_held_keys_never_run_commands(int key, bool control)
    {
        Assert.Equal(PageShortcut.None, Resolve(key, control, input: true));
        Assert.Equal(PageShortcut.None, Resolve(key, control, blocked: true));
        Assert.Equal(PageShortcut.None, Resolve(key, control, repeat: true));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Shift_AltGr_and_Windows_combinations_are_left_to_the_control(bool shift, bool alt, bool windows)
    {
        Assert.Equal(PageShortcut.None, KeyboardShortcutPolicy.Resolve(49, true, shift, alt, windows, false, false, true, false));
        Assert.Equal(PageShortcut.None, KeyboardShortcutPolicy.Resolve(78, false, shift, alt, windows, false, false, true, false));
    }

    [Fact]
    public void Disabling_character_shortcuts_keeps_module_navigation()
    {
        foreach (int key in new[] { 78, 69, 82 }) Assert.Equal(PageShortcut.None, Resolve(key, false, enabled: false));
        Assert.Equal(PageShortcut.Calendar, Resolve(49, true, enabled: false));
    }

    [Theory]
    [InlineData(49, false)]
    [InlineData(78, true)]
    [InlineData(54, true)]
    [InlineData(13, false)]
    [InlineData(27, false)]
    public void Other_keys_keep_their_existing_behavior(int key, bool control)
        => Assert.Equal(PageShortcut.None, Resolve(key, control));

    private static PageShortcut Resolve(int key, bool control, bool input = false, bool blocked = false, bool repeat = false, bool enabled = true)
        => KeyboardShortcutPolicy.Resolve(key, control, false, false, false, blocked, input, enabled, repeat);
}
