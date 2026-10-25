using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
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
        AutomationProperties.SetAutomationId(RetryButton, automationPrefix + "Retry");
        AutomationProperties.SetAutomationId(Rows, automationPrefix + "Items");
        AutomationProperties.SetName(LoadingBar, title);
    }

    internal void SetLoading(string message)
    {
        LoadingBar.Visibility = Visibility.Visible;
        LoadingBar.IsIndeterminate = true;
        ErrorBar.IsOpen = false;
        RetryButton.Visibility = Visibility.Collapsed;
        Summary.Text = "";
        Detail.Text = message;
        Rows.ItemsSource = null;
        Rows.Visibility = Visibility.Collapsed;
    }

    internal void SetResult(string summary, string detail, IReadOnlyList<TodayCardRow>? rows = null)
    {
        FinishLoading();
        Summary.Text = summary;
        Detail.Text = detail;
        Rows.ItemsSource = rows;
        Rows.Visibility = rows?.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void SetError(string reason, string nextStep)
    {
        FinishLoading();
        Summary.Text = "—";
        Detail.Text = nextStep;
        ErrorBar.Message = reason;
        ErrorBar.IsOpen = true;
        RetryButton.Visibility = Visibility.Visible;
    }

    private void FinishLoading()
    {
        LoadingBar.IsIndeterminate = false;
        LoadingBar.Visibility = Visibility.Collapsed;
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e) => OpenRequested?.Invoke();
    private void RetryButton_Click(object sender, RoutedEventArgs e) => RetryRequested?.Invoke();
    private void Rows_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TodayCardRow row) ItemRequested?.Invoke(row.Item);
    }
}
