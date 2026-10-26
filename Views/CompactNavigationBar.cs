using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using Task_Flyout.Services;

namespace Task_Flyout.Views;

internal interface ICompactNavigationPage
{
    bool CanGoBackWithinPage { get; }
    bool TryGoBackWithinPage();
}

public sealed class CompactNavigationBar : UserControl
{
    private readonly ResourceLoader _loader = new();
    private readonly TextBlock _step = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _back = new() { MinHeight = VisualResources.Number("ButtonHeight") };
    private readonly Button _continue = new() { MinHeight = VisualResources.Number("ButtonHeight") };
    public event EventHandler? BackRequested;
    public event EventHandler? ContinueRequested;

    public CompactNavigationBar()
    {
        var grid = new Grid { ColumnSpacing = VisualResources.Number("SpaceSmall"), Padding = new Thickness(12, 8, 12, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _back.Content = Text("Compact_Back", "Back");
        _continue.Content = Text("Compact_ShowList", "Show list");
        AutomationProperties.SetAutomationId(_back, "CompactBack");
        AutomationProperties.SetAutomationId(_continue, "CompactShowList");
        AutomationProperties.SetLiveSetting(_step, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        _back.Click += (_, _) => BackRequested?.Invoke(this, EventArgs.Empty);
        _continue.Click += (_, _) => ContinueRequested?.Invoke(this, EventArgs.Empty);
        Grid.SetColumn(_step, 1);
        Grid.SetColumn(_continue, 2);
        grid.Children.Add(_back);
        grid.Children.Add(_step);
        grid.Children.Add(_continue);
        Content = grid;
    }

    private string Text(string key, string fallback) => _loader.GetStringOrDefault(key) ?? fallback;

    internal void Update(bool compact, CompactNavigationState state, string accountsLabel, string listLabel, bool canContinue = true)
    {
        Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        _back.Visibility = state.CanGoBack ? Visibility.Visible : Visibility.Collapsed;
        _continue.Visibility = state.Current == CompactPane.Accounts && canContinue ? Visibility.Visible : Visibility.Collapsed;
        _step.Text = state.Current switch
        {
            CompactPane.Accounts => "1 / 3 · " + accountsLabel,
            CompactPane.List => "2 / 3 · " + listLabel,
            _ => "3 / 3 · " + Text("Compact_Detail", "Details")
        };
    }
}
