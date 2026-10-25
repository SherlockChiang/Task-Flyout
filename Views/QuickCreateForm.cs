using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Task_Flyout.Models;
using Task_Flyout.Services;
using Windows.Storage;

namespace Task_Flyout.Views;

public sealed class QuickCreateForm : UserControl
{
    private static bool _dialogOpen;
    private readonly ResourceLoader _loader = new();
    private readonly StackPanel _fields = new() { Spacing = 10 };
    private readonly ComboBox _type = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _account = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _title = new() { MaxLength = 512 };
    private readonly CalendarDatePicker _date = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TimePicker _start = new() { ClockIdentifier = "24HourClock", MinuteIncrement = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _duration = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _allDay = new();
    private readonly TextBox _location = new();
    private readonly TextBox _notes = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 8000, MinHeight = 64 };
    private readonly ComboBox _reminder = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _recurrence = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _remember = new();
    private readonly CheckBox _archiveMail = new();
    private readonly CheckBox _confirmDestination = new();
    private bool _fromSource;
    private TaskMailSource? _mailSource;
    private bool _created;
    private readonly TextBlock _hint = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly Button _save = new();
    private readonly Button _cancel = new();
    private bool _updating;
    private bool _hasDraft;
    private string? _preferredKey;
    private string _requestId = Guid.NewGuid().ToString("N");
    private string? _submittedFingerprint;
    internal bool IsBusy { get; private set; }
    internal event Action? Saved;
    internal event Action? Canceled;
    internal event Action? LayoutChanged;

    public QuickCreateForm()
    {
        _type.Header = Text("TextNewItem", "New Event / Task");
        _type.Items.Add(Text("Flyout_RadioEvent.Content", "Event"));
        _type.Items.Add(Text("Flyout_RadioTask.Content", "Task"));
        _account.Header = Text("TextAccount", "Account");
        _title.Header = Text("TextTitle", "Title");
        _title.PlaceholderText = Text("QuickCreate_Example", "Tomorrow 09:00 review the proposal");
        _date.Header = Text("TextDate", "Date");
        _start.Header = Text("TextStartTime", "Start time");
        _allDay.Content = Text("Flyout_ChkAllDay.Content", "All day");
        _duration.Header = Text("QuickCreate_Duration", "Duration");
        foreach (int minutes in new[] { 15, 30, 60, 90, 120 })
            _duration.Items.Add(new ComboBoxItem { Content = string.Format(Text("QuickCreate_Minutes", "{0} minutes"), minutes), Tag = minutes });
        _location.Header = Text("Flyout_TxtLocation.PlaceholderText", "Location");
        _notes.Header = Text("TextDescription", "Notes");
        _reminder.Header = Text("QuickCreate_Reminder", "Provider reminder");
        _reminder.Items.Add(new ComboBoxItem { Content = Text("QuickCreate_ProviderDefault", "Provider default") });
        _reminder.Items.Add(new ComboBoxItem { Content = Text("QuickCreate_NoReminder", "No reminder"), Tag = -1 });
        foreach (int minutes in new[] { 0, 5, 15, 30, 60, 1440 })
            _reminder.Items.Add(new ComboBoxItem { Content = string.Format(Text("QuickCreate_Before", "{0} minutes before"), minutes), Tag = minutes });
        _recurrence.Header = Text("CalendarDialog_RecurrenceCombo.Header", "Recurrence");
        _recurrence.Items.Add(Text("CalendarDialog_RecurrenceNone.Content", "Does not repeat"));
        _recurrence.Items.Add(Text("CalendarDialog_RecurrenceDaily.Content", "Daily"));
        _recurrence.Items.Add(Text("CalendarDialog_RecurrenceWeekly.Content", "Weekly"));
        _recurrence.Items.Add(Text("CalendarDialog_RecurrenceMonthly.Content", "Monthly"));
        _recurrence.Items.Add(Text("CalendarDialog_RecurrenceYearly.Content", "Yearly"));
        _remember.Content = new TextBlock { Text = Text("QuickCreate_Remember", "Use this account and duration by default"), TextWrapping = TextWrapping.Wrap };
        var parse = new Button { Content = Text("QuickCreate_Parse", "Recognize date and time") };
        parse.Click += (_, _) => ParseTitle();
        _save.Content = Text("CalendarDialog.PrimaryButtonText", "Save");
        _save.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        _save.Click += async (_, _) => { if (_created) Saved?.Invoke(); else await SaveAsync(); };
        _cancel.Content = Text("CalendarDialog.CloseButtonText", "Cancel");
        _cancel.Click += (_, _) => { Reset(); Canceled?.Invoke(); };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(_save);
        actions.Children.Add(_cancel);
        foreach (var control in new UIElement[] { _type, _account, _title, parse, _date, _allDay, _start, _duration, _location, _notes, _reminder, _recurrence, _remember, _archiveMail, _confirmDestination, _hint })
            _fields.Children.Add(control);
        var root = new StackPanel { Spacing = 10 };
        root.Children.Add(_fields);
        root.Children.Add(_status);
        root.Children.Add(actions);
        Content = root;
        AutomationProperties.SetAutomationId(_title, "QuickCreateTitle");
        AutomationProperties.SetAutomationId(_save, "QuickCreateSave");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        _type.SelectionChanged += (_, _) => { if (!_updating) PopulateAccounts(); };
        _account.SelectionChanged += (_, _) => { if (!_updating) { _confirmDestination.IsChecked = false; ApplyCapabilities(); } };
        _allDay.Checked += (_, _) => ApplyCapabilities();
        _allDay.Unchecked += (_, _) => ApplyCapabilities();
    }

    private string Text(string key, string fallback) => _loader.GetStringOrDefault(key) ?? fallback;
    private bool IsEvent => _type.SelectedIndex == 0;
    private ConnectedAccountInfo? Account => (_account.SelectedItem as ComboBoxItem)?.DataContext as ConnectedAccountInfo;
    private string DefaultAccountSetting => IsEvent ? "QuickCreate.EventAccount" : "QuickCreate.TaskAccount";

    internal void Initialize(DateTime date, bool isTask = false, QuickCreateSeed? seed = null)
    {
        if (_hasDraft) return;
        _updating = true;
        _hasDraft = true;
        _type.SelectedIndex = isTask ? 1 : 0;
        _date.Date = new DateTimeOffset(date.Date);
        _start.Time = TimeSpan.FromMinutes(Math.Floor(DateTime.Now.TimeOfDay.TotalMinutes / 5) * 5);
        int duration = QuickCreatePolicy.Duration(ApplicationData.Current.LocalSettings.Values["QuickCreate.Duration"] as int? ?? 60);
        _duration.SelectedItem = _duration.Items.OfType<ComboBoxItem>().Single(item => (int)item.Tag == duration);
        _allDay.IsChecked = false;
        _reminder.SelectedIndex = 0;
        _recurrence.SelectedIndex = 0;
        _title.Text = seed?.Title ?? "";
        _notes.Text = seed?.Notes ?? "";
        _location.Text = "";
        _remember.IsChecked = false;
        _preferredKey = seed?.PreferredProviderKey;
        _mailSource = seed?.MailSource;
        _fromSource = seed != null;
        _confirmDestination.IsChecked = false;
        _archiveMail.IsChecked = false;
        _archiveMail.Content = new TextBlock { Text = string.Format(Text("Cross_ArchiveOption", "When I complete this task in Task Flyout, archive the source mail in {0}. Manage or undo in Tasks."), _mailSource?.Address), TextWrapping = TextWrapping.Wrap };
        _status.Text = "";
        _updating = false;
        PopulateAccounts();
    }

    internal void FocusTitle() => _title.Focus(FocusState.Programmatic);
    internal void Reset() { if (IsBusy) return; _hasDraft = false; _requestId = Guid.NewGuid().ToString("N"); _submittedFingerprint = null; }

    private void PopulateAccounts()
    {
        if (IsBusy) return;
        _confirmDestination.IsChecked = false;
        _updating = true;
        string? previous = Account?.ProviderKey;
        _account.Items.Clear();
        if (App.Current is App app)
            foreach (var account in app.SyncManager.AccountManager.Accounts.Where(account => QuickCreatePolicy.Capabilities(account.ProviderName, IsEvent).Supported))
                _account.Items.Add(new ComboBoxItem { Content = account.DisplayTitle + " · " + account.ProviderName, DataContext = account });
        string? preferred = _preferredKey ?? ApplicationData.Current.LocalSettings.Values[DefaultAccountSetting] as string ?? previous;
        _account.SelectedItem = _account.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            item.DataContext is ConnectedAccountInfo account && string.Equals(account.ProviderKey, preferred, StringComparison.OrdinalIgnoreCase));
        if (_account.SelectedItem == null && _account.Items.Count > 0) _account.SelectedIndex = 0;
        _preferredKey = null;
        _updating = false;
        ApplyCapabilities();
    }

    private void ApplyCapabilities()
    {
        if (_updating) return;
        var capability = QuickCreatePolicy.Capabilities(Account?.ProviderName, IsEvent);
        _allDay.Visibility = capability.Time ? Visibility.Visible : Visibility.Collapsed;
        _start.Visibility = capability.Time && _allDay.IsChecked != true ? Visibility.Visible : Visibility.Collapsed;
        _start.Header = IsEvent ? Text("TextStartTime", "Start time") : Text("TextDueTime", "Due time");
        _duration.Visibility = IsEvent && capability.Time && _allDay.IsChecked != true ? Visibility.Visible : Visibility.Collapsed;
        _location.Visibility = IsEvent ? Visibility.Visible : Visibility.Collapsed;
        _archiveMail.Visibility = !IsEvent && _mailSource != null ? Visibility.Visible : Visibility.Collapsed;
        _confirmDestination.Visibility = _fromSource ? Visibility.Visible : Visibility.Collapsed;
        _confirmDestination.Content = new TextBlock { Text = string.Format(Text("Cross_ConfirmDestination", "I confirm creation in {0} ({1})."), Account?.DisplayTitle, Account?.ProviderName), TextWrapping = TextWrapping.Wrap };
        _reminder.Visibility = capability.Reminder ? Visibility.Visible : Visibility.Collapsed;
        _recurrence.Visibility = capability.Recurrence ? Visibility.Visible : Visibility.Collapsed;
        if (!capability.Reminder) _reminder.SelectedIndex = 0;
        if (!capability.Recurrence) _recurrence.SelectedIndex = 0;
        _hint.Text = Account == null ? Text("TextNoAccountAvailable", "No Account Available")
            : !capability.Reminder ? Text("QuickCreate_GoogleTaskHint", "Google Tasks saves a due date only; time, reminders and recurrence are unavailable.")
            : !IsEvent ? Text("QuickCreate_TaskHint", "Tasks use a due date. Reminders are relative to 09:00 on that date and are saved to the provider.")
            : Text("QuickCreate_ReminderHint", "Reminders are saved to the provider. All-day events use midnight. App notification settings remain separate.");
        _save.IsEnabled = capability.Supported && !IsBusy;
        LayoutChanged?.Invoke();
    }

    private void ParseTitle()
    {
        var parsed = QuickCreatePolicy.Parse(_title.Text, DateTime.Today);
        if (parsed == null) { _status.Text = Text("QuickCreate_ParseFailed", "Use a leading date such as tomorrow 09:00 or 2026-10-25 14:30, followed by a title. Your input was kept."); return; }
        if (parsed.Time.HasValue && !QuickCreatePolicy.Capabilities(Account?.ProviderName, IsEvent).Time)
        { _status.Text = Text("QuickCreate_DateOnly", "Tasks use a due date only. Remove the time or choose Event, then recognize again. Your input was kept."); return; }
        if (parsed.Date < _date.MinDate.Date || parsed.Date > _date.MaxDate.Date)
        { _status.Text = Text("QuickCreate_DateRange", "Choose a date within the date picker's range. Your input was kept."); return; }
        _title.Text = parsed.Title;
        _date.Date = new DateTimeOffset(parsed.Date);
        if (parsed.Time.HasValue) { _allDay.IsChecked = false; _start.Time = parsed.Time.Value; }
        _status.Text = Text("QuickCreate_ReviewParsed", "Date and time applied. Review the fields before saving.");
    }

    private async Task SaveAsync()
    {
        if (IsBusy || App.Current is not App app) return;
        var account = Account;
        if (_fromSource && _confirmDestination.IsChecked != true)
        { _status.Text = Text("Cross_ConfirmRequired", "Confirm the destination account before saving."); return; }
        if (account == null || !_date.Date.HasValue || string.IsNullOrWhiteSpace(_title.Text))
        { _status.Text = Text("QuickCreate_Required", "Choose an account and date, and enter a title."); return; }
        var capability = QuickCreatePolicy.Capabilities(account.ProviderName, IsEvent);
        int duration = (_duration.SelectedItem as ComboBoxItem)?.Tag as int? ?? 60;
        var recurrence = capability.Recurrence ? (EventRecurrenceKind)Math.Max(0, _recurrence.SelectedIndex) : EventRecurrenceKind.None;
        int? reminder = capability.Reminder ? (_reminder.SelectedItem as ComboBoxItem)?.Tag as int? : null;
        bool isAllDay = !capability.Time || _allDay.IsChecked == true;
        var date = _date.Date.Value.Date;
        var eventWindow = SyncEventTimePolicy.Create(date, isAllDay ? null : _start.Time, QuickCreatePolicy.EndTime(_start.Time, duration));
        if (capability.Time && !isAllDay && (TimeZoneInfo.Local.IsInvalidTime(eventWindow.Start) || TimeZoneInfo.Local.IsInvalidTime(eventWindow.End)))
        { _status.Text = Text("QuickCreate_InvalidTime", "This time does not exist because the clocks change. Choose another time."); return; }
        string fingerprint = System.Text.Json.JsonSerializer.Serialize(new
        {
            account.ProviderKey,
            IsEvent,
            Date = date.Ticks,
            Start = _start.Time,
            Duration = duration,
            AllDay = isAllDay,
            Title = _title.Text,
            Notes = _notes.Text,
            Location = _location.Text,
            Reminder = reminder,
            Recurrence = recurrence
        });
        if (_submittedFingerprint != null && _submittedFingerprint != fingerprint) _requestId = Guid.NewGuid().ToString("N");
        _submittedFingerprint = fingerprint;
        bool succeeded = false;
        IsBusy = true;
        foreach (var field in _fields.Children.OfType<Control>()) field.IsEnabled = false;
        _save.IsEnabled = false;
        _cancel.IsEnabled = false;
        _status.Text = Text("TextSyncing", "Syncing...");
        try
        {
            var created = await app.SyncManager.CreateItemAsync(_title.Text.Trim(), IsEvent, isAllDay, date, _start.Time,
                QuickCreatePolicy.EndTime(_start.Time, duration), _location.Text, recurrence, account.ProviderName, account.AccountId,
                new QuickCreateOptions(_notes.Text, reminder, recurrence, _requestId));
            succeeded = true;
            if (!IsEvent && _archiveMail.IsChecked == true && _mailSource != null)
            {
                try
                {
                    if (created == null) throw new InvalidOperationException("The provider did not return a task identity.");
                    await app.TaskMailActions.AddAsync(account.ProviderKey, created, _title.Text.Trim(), _mailSource);
                }
                catch
                {
                    _created = true;
                    _status.Text = Text("Cross_LinkFailed", "The task was created, but automatic mail archiving could not be enabled. Do not create it again; archive the mail manually.");
                }
            }
            if (_remember.IsChecked == true)
            {
                try
                {
                    ApplicationData.Current.LocalSettings.Values[DefaultAccountSetting] = account.ProviderKey;
                    if (IsEvent) ApplicationData.Current.LocalSettings.Values["QuickCreate.Duration"] = duration;
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Quick create preferences: {ex.GetType().Name}"); }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Quick create failed: {ex.GetType().Name}");
            _status.Text = Text("QuickCreate_SaveFailed", "Could not confirm creation. Your input is kept. Check the destination for duplicates before retrying Save.");
        }
        finally
        {
            IsBusy = false;
            foreach (var field in _fields.Children.OfType<Control>()) field.IsEnabled = true;
            _cancel.IsEnabled = true;
            ApplyCapabilities();
        }
        if (succeeded)
        {
            Reset();
            _ = RefreshCreatedAccountAsync(app, account, date);
            if (_created)
            {
                foreach (var field in _fields.Children.OfType<Control>()) field.IsEnabled = false;
                _save.Content = Text("TextConfirm", "OK");
                _cancel.Visibility = Visibility.Collapsed;
            }
            else Saved?.Invoke();
        }
    }

    // Refresh failure must never send the successful create request again.
    private static async Task RefreshCreatedAccountAsync(App app, ConnectedAccountInfo account, DateTime date)
    {
        try
        {
            await app.SyncManager.SyncAccountAsync(account.ProviderName, account.AccountId, date.AddDays(-1), date.AddDays(2));
            App.MyFlyoutWindow?.ReloadFilters();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Quick create refresh: {ex.GetType().Name}"); }
    }

    internal static async Task<bool> ShowDialogAsync(XamlRoot root, DateTime date, bool isTask, QuickCreateSeed? seed = null)
    {
        if (_dialogOpen) return false;
        var form = new QuickCreateForm();
        form.Initialize(date, isTask, seed);
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = form.Text("TextNewItem", "New Event / Task"),
            Content = new ScrollViewer { Content = form, MaxHeight = Math.Max(180, root.Size.Height - 200), VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        };
        bool saved = false;
        form.Saved += () => { saved = true; dialog.Hide(); };
        form.Canceled += () => dialog.Hide();
        dialog.Closing += (_, args) => { if (form.IsBusy) args.Cancel = true; };
        dialog.Opened += (_, _) => form.FocusTitle();
        _dialogOpen = true;
        try { await dialog.ShowAsync(); }
        catch (COMException ex) { System.Diagnostics.Debug.WriteLine($"Quick create dialog unavailable: {ex.GetType().Name}"); }
        finally { _dialogOpen = false; }
        return saved;
    }
}
