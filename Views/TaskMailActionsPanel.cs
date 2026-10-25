using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Linq;
using System.Threading.Tasks;
using Task_Flyout.Services;

namespace Task_Flyout.Views;

public sealed class TaskMailActionsPanel : UserControl
{
    private readonly ResourceLoader _loader = new();
    private readonly StackPanel _rows = new() { Spacing = 10 };
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Expander _expander;
    private TaskMailActions? Actions => (App.Current as App)?.TaskMailActions;

    public TaskMailActionsPanel()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(_error);
        panel.Children.Add(_rows);
        _expander = new Expander
        {
            Header = Text("Cross_Actions", "Linked mail actions"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = new ScrollViewer { Content = panel, MaxHeight = 240 }
        };
        Content = _expander;
        Loaded += (_, _) => { if (Actions != null) Actions.Changed += ActionsChanged; Render(); };
        Unloaded += (_, _) => { if (Actions != null) Actions.Changed -= ActionsChanged; };
    }

    private string Text(string key, string fallback) => _loader.GetStringOrDefault(key) ?? fallback;
    private void ActionsChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() => { if (IsLoaded) Render(); });

    private void Render()
    {
        _rows.Children.Clear();
        var actions = Actions;
        if (actions == null) return;
        var links = actions.Snapshot();
        Visibility = links.Count > 0 || actions.StorageFailed ? Visibility.Visible : Visibility.Collapsed;
        _error.Text = actions.StorageFailed ? Text("Cross_StorageFailed", "Linked mail actions could not be saved or loaded. Check local storage; automatic retry is paused.") : "";
        if (actions.StorageFailed || links.Any(link => link.Phase is TaskMailPhase.Failed or TaskMailPhase.Unknown)) _expander.IsExpanded = true;
        foreach (var link in links)
        {
            var row = new StackPanel { Spacing = 4 };
            row.Children.Add(new TextBlock { Text = link.TaskTitle + " · " + link.Source.Address, TextWrapping = TextWrapping.Wrap });
            row.Children.Add(new TextBlock { Text = link.Source.Subject, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            row.Children.Add(new TextBlock { Text = Text("Cross_State" + link.Phase, link.Phase.ToString()), TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            var buttons = new StackPanel { Spacing = 4 };
            bool busy = link.Phase is TaskMailPhase.Archiving or TaskMailPhase.Undoing;
            if (link.Phase == TaskMailPhase.Failed)
                AddButton(buttons, "Cross_RetryArchive", "Retry archive", () => actions.RetryAsync(link.Key));
            if (link.Phase == TaskMailPhase.Archived)
                AddButton(buttons, "Cross_UndoArchive", "Undo archive", () => actions.UndoAsync(link.Key));
            if (!busy)
            {
                AddButton(buttons, "Cross_CheckMail", "Check mail", () =>
                {
                    // Unknown outcomes may have a new server identity: open the mail page
                    // for reconciliation instead of issuing another move request.
                    if (link.Phase is TaskMailPhase.Unknown or TaskMailPhase.Undone || link.Source.Kind == MailAccountKind.Google && link.Receipt != null) App.MyMainWindow?.NavigateToMail();
                    else App.MyMainWindow?.NavigateToMailMessage(link.Source.AccountId,
                        link.Receipt?.Item.FolderId ?? link.Source.FolderId, link.Receipt?.Item.Id ?? link.Source.MessageId);
                    return Task.CompletedTask;
                });
                AddButton(buttons, "Cross_Forget", "Disable / dismiss", () => actions.ForgetAsync(link.Key));
            }
            row.Children.Add(buttons);
            _rows.Children.Add(row);
        }
    }

    private void AddButton(StackPanel panel, string key, string fallback, Func<Task> action)
    {
        var button = new Button { Content = Text(key, fallback) };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            _error.Text = "";
            try { await action(); }
            catch { _error.Text = Text("Cross_StorageFailed", "Linked mail actions could not be saved or loaded. Check local storage; automatic retry is paused."); }
            finally { if (IsLoaded) Render(); }
        };
        panel.Children.Add(button);
    }
}
