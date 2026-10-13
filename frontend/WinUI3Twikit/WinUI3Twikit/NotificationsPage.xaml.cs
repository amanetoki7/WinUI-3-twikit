using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;

namespace WinUI3Twikit
{
    public sealed partial class NotificationsPage : Page
    {
        public NotificationsViewModel ViewModel { get; }

        private bool _tabReady;

        public NotificationsPage()
        {
            ViewModel = App.ViewModels.Notifications;
            this.InitializeComponent();
            this.Loaded += NotificationsPage_Loaded;
        }

        private async void NotificationsPage_Loaded(object sender, RoutedEventArgs e)
        {
            _tabReady = false;
            SelectSavedTab();
            _tabReady = true;

            if (ViewModel.Notifications.Count == 0 && !ViewModel.IsLoading)
            {
                await ViewModel.LoadNotificationsAsync();
                AttachScrollHandler();
                return;
            }

            // 既存リストがある再訪問時: 見えているタブだけ最新から取り直す
            AttachScrollHandler();

            if (!ViewModel.IsLoading && !ViewModel.IsLoadingMore)
            {
                System.Diagnostics.Debug.WriteLine("通知ページ再訪問 → バックグラウンドで新着取得");
                await ViewModel.LoadMoreNotificationsAsync(refresh: true);
            }
        }

        private void SelectSavedTab()
        {
            foreach (var item in NotificationsTabView.TabItems)
            {
                if (item is TabViewItem tab
                    && tab.Tag is string tag
                    && string.Equals(tag, ViewModel.CurrentNotificationType, StringComparison.OrdinalIgnoreCase))
                {
                    NotificationsTabView.SelectedItem = tab;
                    return;
                }
            }
        }

        private async void NotificationsTabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_tabReady)
            {
                return;
            }

            if (sender is not TabView tabView
                || tabView.SelectedItem is not TabViewItem tabItem
                || tabItem.Tag is not string type)
            {
                return;
            }

            ScrollPositionHelper.SaveOffset(_scrollViewer, offset => ViewModel.ScrollVerticalOffset = offset);
            await ViewModel.SwitchNotificationTypeAsync(type);
            AttachScrollHandler();
        }

        private ScrollViewer? _scrollViewer;

        private void AttachScrollHandler()
        {
            if (NotificationsListView == null) return;

            _scrollViewer = ScrollPositionHelper.FindScrollViewer(NotificationsListView);
            if (_scrollViewer == null)
            {
                System.Diagnostics.Debug.WriteLine("ScrollViewer が見つかりませんでした");
                return;
            }

            _scrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
            _scrollViewer.ViewChanged += ScrollViewer_ViewChanged;
            ScrollPositionHelper.RestoreOffset(_scrollViewer, ViewModel.ScrollVerticalOffset);
            System.Diagnostics.Debug.WriteLine("ScrollViewer ハンドラ登録完了");
        }

        private async void ScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer)
            {
                return;
            }

            if (!e.IsIntermediate)
            {
                ViewModel.ScrollVerticalOffset = scrollViewer.VerticalOffset;
            }

            if (ViewModel.IsLoading || ViewModel.IsLoadingMore || !ViewModel.HasMore)
            {
                return;
            }

            if (scrollViewer.VerticalOffset + scrollViewer.ViewportHeight < scrollViewer.ExtentHeight - 150)
            {
                return;
            }

            System.Diagnostics.Debug.WriteLine("下までスクロール → 追加取得");
            App.MainWindow?.ShowLoading(true);
            try
            {
                await ViewModel.LoadMoreNotificationsAsync(refresh: false);
            }
            finally
            {
                if (!ViewModel.IsLoadingMore)
                {
                    App.MainWindow?.ShowLoading(false);
                }
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            ScrollPositionHelper.SaveOffset(_scrollViewer, offset => ViewModel.ScrollVerticalOffset = offset);
            base.OnNavigatedFrom(e);
        }
    }
}
