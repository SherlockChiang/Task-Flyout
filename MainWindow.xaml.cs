using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Task_Flyout.Views; // 假设你会创建一个 Views 文件夹

namespace Task_Flyout
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();

            // 1. 设置 Mica 材质背景
            SystemBackdrop = new MicaBackdrop() { Kind = MicaKind.BaseAlt };

            // 2. 沉浸式标题栏
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(null); // 让整个顶部区域可拖拽

            // 3. 默认跳转到日历页
            ContentFrame.Navigate(typeof(CalendarPage));
        }

        private void MainNav_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.IsSettingsInvoked)
            {
                ContentFrame.Navigate(typeof(SettingsPage));
            }
            else if (args.InvokedItemContainer is NavigationViewItem item)
            {
                switch (item.Tag?.ToString())
                {
                    case "GoogleAccount":
                        ContentFrame.Navigate(typeof(CalendarPage));
                        break;
                }
            }
        }
    }
}