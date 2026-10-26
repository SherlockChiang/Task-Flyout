using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Task_Flyout.Models;

namespace Task_Flyout.Views;

public sealed record TodayCardRow(string Title, string Subtitle, AgendaItem Item);

public sealed partial class TodayCard : UserControl
{
    internal event Action? OpenRequested;
    internal event Action? RetryRequested;
    internal event Action<AgendaItem>? ItemRequested;

    public TodayCard() => InitializeComponent();

    internal void Configure(string title, string openLabel, string automationPrefix)
    {
        Heading.Text = title;
        OpenButton.Content = openLabel;
        AutomationProperties.SetAutomationId(OpenButton, automationPrefix + "Open");
        AutomationProperties.SetAutomationId(LoadState, automationPrefix + "State");
        AutomationProperties.SetAutomationId(Rows, automationPrefix + "Items");
    }

    internal void SetLoading(string message)
    {
        LoadState.SetLoading(message, !string.IsNullOrEmpty(Summary.Text));
    }

    internal void SetResult(string summary, string detail, IReadOnlyList<TodayCardRow>? rows = null)
    {
        LoadState.Clear();
        Summary.Text = summary;
        Detail.Text = detail;
        Rows.ItemsSource = rows;
        Rows.Visibility = rows?.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void SetError(string reason, string nextStep)
    {
        LoadState.SetError(reason, nextStep, ContentState.RetryLabel, () => { RetryRequested?.Invoke(); return Task.CompletedTask; });
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e) => OpenRequested?.Invoke();
    private void Rows_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TodayCardRow row) ItemRequested?.Invoke(row.Item);
    }
}
