using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using Task_Flyout.Models;
using Task_Flyout.Services;

namespace Task_Flyout.Views;

public sealed class AgendaDetailsPane : UserControl
{
    private readonly ResourceLoader _loader = new();
    private readonly TextBlock _title = new() { FontSize = 24, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly TextBlock _identity = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _time = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _location = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly TextBlock _notes = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
    private readonly Button _edit = new();
    public event EventHandler? EditRequested;
    internal AgendaItem? Item { get; private set; }
    internal UIElement FocusTarget => _edit.IsEnabled ? _edit : this;

    public AgendaDetailsPane()
    {
        var panel = new StackPanel { Spacing = 12, Padding = new Thickness(16) };
        _edit.Content = _loader.GetStringOrDefault("Compact_Edit") ?? "Edit";
        IsTabStop = true;
        AutomationProperties.SetName(this, _loader.GetStringOrDefault("Compact_Detail") ?? "Details");
        AutomationProperties.SetAutomationId(_edit, "CompactEditItem");
        _edit.Click += (_, _) => EditRequested?.Invoke(this, EventArgs.Empty);
        foreach (var child in new UIElement[] { _title, _identity, _time, _status, _location, _notes, _edit }) panel.Children.Add(child);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    internal void Show(AgendaItem item)
    {
        Item = item;
        _title.Text = item.Title;
        var account = (App.Current as App)?.SyncManager.AccountManager.GetAccount(item.Provider, item.AccountId);
        _identity.Text = (account?.DisplayTitle ?? item.Provider) + " · " + item.CalendarName;
        _time.Text = item.DateKey + " · " + item.Subtitle;
        _status.Text = item.IsTask ? (_loader.GetStringOrDefault(item.IsCompleted ? "Compact_Completed" : "Compact_Pending") ?? (item.IsCompleted ? "Completed" : "Pending")) : "";
        _location.Text = item.Location;
        _notes.Text = item.Description;
        _edit.IsEnabled = true;
    }

    internal void Clear()
    {
        Item = null;
        _title.Text = _loader.GetStringOrDefault("Compact_Unavailable") ?? "This item is no longer in the current list. Go back to choose another item.";
        _identity.Text = _time.Text = _status.Text = _location.Text = _notes.Text = "";
        _edit.IsEnabled = false;
    }
}
