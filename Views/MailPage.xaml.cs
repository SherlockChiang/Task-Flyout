using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Task_Flyout.Services;
using Windows.System;

namespace Task_Flyout.Views
{
    public sealed partial class MailPage : Page
    {
        private readonly ObservableCollection<MailItem> _items = new();
        private readonly Dictionary<string, MailAccount> _accountsById = new();
        private readonly Dictionary<TreeViewNode, MailAccount> _accountNodes = new();
        private readonly Dictionary<TreeViewNode, (MailAccount Account, MailFolder Folder)> _folderNodes = new();
        private MailService? _mailService;
        private MailAccount? _selectedAccount;
        private MailFolder? _selectedFolder;
        private MailItem? _selectedItem;
        private MailAccountKind? _draftKind;
        private bool _isInitializing = true;

        public MailPage()
        {
            InitializeComponent();
            Loaded += MailPage_Loaded;
        }

        private void MailPage_Loaded(object sender, RoutedEventArgs e)
        {
            _mailService = (App.Current as App)?.MailService;
            MailListView.ItemsSource = _items;
            _isInitializing = false;
            RefreshAccounts();
        }

        private void RefreshAccounts()
        {
            if (_mailService == null) return;

            AccountTree.RootNodes.Clear();
            _accountsById.Clear();
            _accountNodes.Clear();
            _folderNodes.Clear();

            var accounts = _mailService.GetAccounts().ToList();
            foreach (var account in accounts)
            {
                _accountsById[account.Id] = account;
                var node = new TreeViewNode
                {
                    Content = FormatAccountContent(account),
                    IsExpanded = false,
                    HasUnrealizedChildren = true
                };
                AccountTree.RootNodes.Add(node);
                _accountNodes[node] = account;
            }

            bool hasAccounts = accounts.Count > 0;
            AddAccountPanel.Visibility = hasAccounts ? Visibility.Collapsed : Visibility.Visible;
            EmptyDetailPanel.Visibility = hasAccounts ? EmptyDetailPanel.Visibility : Visibility.Collapsed;
            MessageListSubtitle.Text = hasAccounts ? "请选择左侧文件夹" : "先添加一个邮箱账户";
            if (!hasAccounts)
            {
                _items.Clear();
                ClearDetail();
            }
        }

        private static string FormatAccountContent(MailAccount account)
        {
            string suffix = account.SetupText.Length > 0 ? $" · {account.SetupText}" : "";
            return $"{account.ProviderName} - {account.Subtitle}{suffix}";
        }

        private static string FormatFolderContent(MailFolder folder)
        {
            return string.IsNullOrWhiteSpace(folder.CountText)
                ? folder.DisplayName
                : $"{folder.DisplayName} ({folder.CountText})";
        }

        private async void AccountTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
        {
            if (_mailService == null || !_accountNodes.TryGetValue(args.Node, out var account)) return;
            if (args.Node.Children.Count > 0 && !args.Node.HasUnrealizedChildren) return;

            args.Node.Children.Clear();
            args.Node.HasUnrealizedChildren = false;

            try
            {
                var folders = await _mailService.FetchFoldersAsync(account);
                foreach (var folder in folders)
                {
                    var child = new TreeViewNode
                    {
                        Content = FormatFolderContent(folder),
                        HasUnrealizedChildren = false
                    };
                    args.Node.Children.Add(child);
                    _folderNodes[child] = (account, folder);
                }
            }
            catch (Exception ex)
            {
                AddStatusText.Text = ex.Message;
            }
        }

        private async void AccountTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is not TreeViewNode node) return;
            if (!_folderNodes.TryGetValue(node, out var selection)) return;

            _selectedAccount = selection.Account;
            _selectedFolder = selection.Folder;
            await LoadMessagesAsync();
        }

        private async Task LoadMessagesAsync()
        {
            if (_mailService == null || _selectedAccount == null || _selectedFolder == null) return;

            AddAccountPanel.Visibility = Visibility.Collapsed;
            LoadingRing.IsActive = true;
            RefreshButton.IsEnabled = false;
            MessageListTitle.Text = _selectedFolder.DisplayName;
            MessageListSubtitle.Text = $"{_selectedAccount.DisplayTitle} · 正在加载";

            try
            {
                var messages = await _mailService.FetchMessagesAsync(_selectedAccount, _selectedFolder, UnreadOnlyToggle.IsOn);
                _items.Clear();
                foreach (var item in messages.OrderByDescending(item => item.RawReceivedTime))
                    _items.Add(item);

                MessageListSubtitle.Text = $"{_selectedAccount.DisplayTitle} · {_items.Count} 封邮件";
                MailListView.SelectedIndex = _items.Count > 0 ? 0 : -1;
                if (_items.Count == 0)
                    ClearDetail();
            }
            catch (Exception ex)
            {
                MessageListSubtitle.Text = ex.Message;
            }
            finally
            {
                LoadingRing.IsActive = false;
                RefreshButton.IsEnabled = true;
            }
        }

        private async void UnreadOnlyToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_isInitializing || _selectedFolder == null) return;
            await LoadMessagesAsync();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFolder == null)
            {
                RefreshAccounts();
                return;
            }

            await LoadMessagesAsync();
        }

        private void AddMailButton_Click(object sender, RoutedEventArgs e)
        {
            ShowAddAccountPanel();
        }

        private void ShowAddAccountPanel()
        {
            AddAccountPanel.Visibility = Visibility.Visible;
            DetailPanel.Visibility = Visibility.Collapsed;
            EmptyDetailPanel.Visibility = Visibility.Collapsed;
            AddStatusText.Text = "";
            DraftAddressPanel.Visibility = Visibility.Collapsed;
            _draftKind = null;
        }

        private async void AddOutlookButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mailService == null) return;

            SetAddButtonsEnabled(false);
            AddStatusText.Text = "正在打开 Microsoft 授权...";

            try
            {
                var account = await _mailService.AddOutlookAccountAsync();
                AddStatusText.Text = $"已添加 {account.DisplayTitle}";
                RefreshAccounts();
            }
            catch (Exception ex)
            {
                AddStatusText.Text = ex.Message;
            }
            finally
            {
                SetAddButtonsEnabled(true);
            }
        }

        private void AddGmailButton_Click(object sender, RoutedEventArgs e)
        {
            _ = AddGoogleAccountAsync();
        }

        private void AddImapButton_Click(object sender, RoutedEventArgs e)
        {
            StartDraftAccount(MailAccountKind.Imap);
        }

        private async Task AddGoogleAccountAsync()
        {
            if (_mailService == null) return;

            SetAddButtonsEnabled(false);
            AddStatusText.Text = "正在打开 Google 授权...";

            try
            {
                var account = await _mailService.AddGoogleAccountAsync();
                AddStatusText.Text = $"已添加 {account.DisplayTitle}";
                RefreshAccounts();
            }
            catch (Exception ex)
            {
                AddStatusText.Text = ex.Message;
            }
            finally
            {
                SetAddButtonsEnabled(true);
            }
        }

        private void StartDraftAccount(MailAccountKind kind)
        {
            _draftKind = kind;
            DraftAddressPanel.Visibility = Visibility.Visible;
            DraftAddressBox.Text = "";
            DraftAddressBox.PlaceholderText = kind == MailAccountKind.Google ? "name@gmail.com" : "name@example.com";
            AddStatusText.Text = kind == MailAccountKind.Google
                ? "Gmail 授权读取还未接入，先保存为待配置账户。"
                : "IMAP 服务器设置还未接入，先保存为待配置账户。";
        }

        private void CancelDraftButton_Click(object sender, RoutedEventArgs e)
        {
            DraftAddressPanel.Visibility = Visibility.Collapsed;
            _draftKind = null;
            AddStatusText.Text = "";
        }

        private void SaveDraftButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mailService == null || _draftKind == null) return;

            string address = DraftAddressBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(address))
            {
                AddStatusText.Text = "请输入邮箱地址。";
                return;
            }

            _mailService.AddDraftAccount(_draftKind.Value, address);
            AddStatusText.Text = "已保存待配置账户。";
            RefreshAccounts();
        }

        private void SetAddButtonsEnabled(bool isEnabled)
        {
            AddOutlookButton.IsEnabled = isEnabled;
            AddGmailButton.IsEnabled = isEnabled;
            AddImapButton.IsEnabled = isEnabled;
            SaveDraftButton.IsEnabled = isEnabled;
        }

        private void MailListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MailListView.SelectedItem is not MailItem item)
            {
                ClearDetail();
                return;
            }

            _selectedItem = item;
            AddAccountPanel.Visibility = Visibility.Collapsed;
            EmptyDetailPanel.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Visible;
            DetailSubject.Text = item.Subject;
            DetailSender.Text = item.Sender;
            DetailTime.Text = item.RawReceivedTime?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? item.ReceivedTime;
            DetailPreview.Text = string.IsNullOrWhiteSpace(item.Preview) ? "没有可用预览。" : item.Preview;
            OpenInBrowserButton.IsEnabled = !string.IsNullOrWhiteSpace(item.WebLink);
        }

        private void ClearDetail()
        {
            _selectedItem = null;
            DetailPanel.Visibility = Visibility.Collapsed;
            if (AddAccountPanel.Visibility != Visibility.Visible)
                EmptyDetailPanel.Visibility = Visibility.Visible;
        }

        private async void OpenInBrowserButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null || string.IsNullOrWhiteSpace(_selectedItem.WebLink)) return;
            await Launcher.LaunchUriAsync(new Uri(_selectedItem.WebLink));
        }
    }
}
