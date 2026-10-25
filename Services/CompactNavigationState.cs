namespace Task_Flyout.Services;

internal enum CompactPane { Accounts, List, Detail }

// Resize only changes presentation. It must never infer navigation from stale
// selection, otherwise returning to a list and resizing reopens old detail.
internal sealed class CompactNavigationState(CompactPane initial = CompactPane.List)
{
    public CompactPane Current { get; private set; } = initial;
    private CompactPane _detailReturn = CompactPane.List;
    public int Version { get; private set; }
    public bool CanGoBack => Current != CompactPane.Accounts;

    public void Show(CompactPane pane, CompactPane? detailReturn = null)
    {
        if (pane == CompactPane.Detail && (Current != CompactPane.Detail || detailReturn.HasValue))
            _detailReturn = detailReturn ?? (Current == CompactPane.Accounts ? CompactPane.Accounts : CompactPane.List);
        Current = pane;
        Version++;
    }

    public bool Back()
    {
        if (!CanGoBack) return false;
        Show(Current == CompactPane.Detail ? _detailReturn : CompactPane.Accounts);
        return true;
    }

    public bool IsCurrent(int version) => version == Version;
}
