namespace Task_Flyout.Services;

// UI-thread state: a replacement or unload invalidates the previous action's error.
// Replacements do not unlock an action that is still running.
internal sealed class ContentStateActionGate
{
    private int _version;
    public bool IsRunning { get; private set; }
    public void Replace() => _version++;
    public bool IsCurrent(int version) => version == _version;
    public bool TryBegin(out int version)
    {
        version = _version;
        if (IsRunning) return false;
        IsRunning = true;
        return true;
    }
    public void End() => IsRunning = false;
}
