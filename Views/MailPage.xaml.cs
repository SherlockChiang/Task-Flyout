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
        private MailAccount? _selectedAccountForRemoval;
        private bool _isInitializing = true;

        public MailPage()
        {
            InitializeComponent();
            Loaded += MailPage_Loaded;
        }

        private async void MailPage_Loaded(object sender, RoutedEventArgs e)
        {
            _mailService = (App.Current as App)?.MailService;
            MailListView.ItemsSource = _items;
            _isInitializing = false;
            await RefreshAccountsAsync();
        }

        private async Task RefreshAccountsAsync(bool autoSelect = true)
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
                _selectedAccount = null;
                _selectedFolder = null;
                _selectedAccountForRemoval = null;
                RemoveMailButton.IsEnabled = false;
                ClearDetail();
                return;
            }

            if (autoSelect)
                await SelectFirstAvailableFolderAsync();
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
            await LoadFoldersForNodeAsync(args.Node);
        }

        private async Task LoadFoldersForNodeAsync(TreeViewNode node)
        {
            if (_mailService == null || !_accountNodes.TryGetValue(node, out var account)) return;
            if (node.Children.Count > 0 && !node.HasUnrealizedChildren) return;

            node.Children.Clear();
            node.HasUnrealizedChildren = false;

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
                    node.Children.Add(child);
                    _folderNodes[child] = (account, folder);
                }
            }
            catch (Exception ex)
            {
                AddStatusText.Text = ex.Message;
            }
        }

        private async Task SelectFirstAvailableFolderAsync()
        {
            if (AccountTree.RootNodes.Count == 0) return;

            var accountNode = AccountTree.RootNodes[0];
            await LoadFoldersForNodeAsync(accountNode);
            accountNode.IsExpanded = true;

            var folderNode = accountNode.Children.FirstOrDefault(node =>
                _folderNodes.TryGetValue(node, out var selection) && !selection.Folder.IsPlaceholder);
            if (folderNode == null) return;

            AccountTree.SelectedNode = folderNode;
            var selected = _folderNodes[folderNode];
            _selectedAccount = selected.Account;
            _selectedFolder = selected.Folder;
            await LoadMessagesAsync();
        }

        private async void AccountTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
        {
            if (args.InvokedItem is not TreeViewNode node) return;

            if (_accountNodes.TryGetValue(node, out var account))
            {
                _selectedAccountForRemoval = account;
                RemoveMailButton.IsEnabled = true;
                await SelectFirstFolderForAccountNodeAsync(node);
                return;
            }

            if (!_folderNodes.TryGetValue(node, out var selection)) return;

            _selectedAccount = selection.Account;
            _selectedFolder = selection.Folder;
            _selectedAccountForRemoval = selection.Account;
            RemoveMailButton.IsEnabled = true;
            await LoadMessagesAsync();
        }

        private async Task SelectFirstFolderForAccountNodeAsync(TreeViewNode accountNode)
        {
            await LoadFoldersForNodeAsync(accountNode);
            accountNode.IsExpanded = true;

            var folderNode = accountNode.Children.FirstOrDefault(node =>
                _folderNodes.TryGetValue(node, out var selection) && !selection.Folder.IsPlaceholder);
            if (folderNode == null)
            {
                _items.Clear();
                _selectedFolder = null;
                ClearDetail();
                return;
            }

            AccountTree.SelectedNode = folderNode;
            var selected = _folderNodes[folderNode];
            _selectedAccount = selected.Account;
            _selectedFolder = selected.Folder;
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
                await RefreshAccountsAsync();
                return;
            }

            await LoadMessagesAsync();
        }

        private void AddMailButton_Click(object sender, RoutedEventArgs e)
        {
            ShowAddAccountPanel();
        }

        private async void RemoveMailButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mailService == null || _selectedAccountForRemoval == null) return;

            var account = _selectedAccountForRemoval;
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "删除邮箱账户",
                Content = $"要从 Task Flyout 删除 {account.ProviderName} - {account.Subtitle} 吗？这不会删除邮箱服务器上的邮件。",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            _mailService.RemoveAccount(account.Id);
            _items.Clear();
            _selectedAccount = null;
            _selectedFolder = null;
            _selectedItem = null;
            _selectedAccountForRemoval = null;
            RemoveMailButton.IsEnabled = false;
            ClearDetail();
            await RefreshAccountsAsync();
        }

        private void ShowAddAccountPanel()
        {
            AddAccountPanel.Visibility = Visibility.Visible;
            DetailPanel.Visibility = Visibility.Collapsed;
            EmptyDetailPanel.Visibility = Visibility.Collapsed;
            AddStatusText.Text = "";
            ImapSettingsPanel.Visibility = Visibility.Collapsed;
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
                await RefreshAccountsAsync();
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
            ShowImapSettings();
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
                await RefreshAccountsAsync();
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

        private void ShowImapSettings()
        {
            ImapSettingsPanel.Visibility = Visibility.Visible;
            ImapDisplayNameBox.Text = "";
            ImapAddressBox.Text = "";
            ImapUserNameBox.Text = "";
            ImapPasswordBox.Password = "";
            ImapHostBox.Text = "";
            ImapPortBox.Value = 993;
            ImapSslToggle.IsOn = true;
            AddStatusText.Text = "请输入 IMAP 服务器信息。Gmail/Outlook 建议优先使用 OAuth。";
        }

        private void CancelImapButton_Click(object sender, RoutedEventArgs e)
        {
            ImapSettingsPanel.Visibility = Visibility.Collapsed;
            AddStatusText.Text = "";
        }

        private async void SaveImapButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mailService == null) return;

            string address = ImapAddressBox.Text.Trim();
            string host = ImapHostBox.Text.Trim();
            string userName = string.IsNullOrWhiteSpace(ImapUserNameBox.Text) ? address : ImapUserNameBox.Text.Trim();
            string password = ImapPasswordBox.Password;
            int port = double.IsNaN(ImapPortBox.Value) ? 993 : (int)ImapPortBox.Value;
            if (string.IsNullOrWhiteSpace(address))
            {
                AddStatusText.Text = "请输入邮箱地址。";
                return;
            }
            if (string.IsNullOrWhiteSpace(host))
            {
                AddStatusText.Text = "请输入 IMAP 服务器。";
                return;
            }
            if (string.IsNullOrWhiteSpace(password))
            {
                AddStatusText.Text = "请输入密码或应用专用密码。";
                return;
            }

            SetAddButtonsEnabled(false);
            AddStatusText.Text = "正在连接 IMAP 服务器...";

            try
            {
                var account = await _mailService.AddImapAccountAsync(
                    ImapDisplayNameBox.Text,
                    address,
                    userName,
                    password,
                    host,
                    port,
                    ImapSslToggle.IsOn);

                AddStatusText.Text = $"已添加 {account.DisplayTitle}";
                await RefreshAccountsAsync();
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

        private void SetAddButtonsEnabled(bool isEnabled)
        {
            AddOutlookButton.IsEnabled = isEnabled;
            AddGmailButton.IsEnabled = isEnabled;
            AddImapButton.IsEnabled = isEnabled;
            SaveImapButton.IsEnabled = isEnabled;
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
