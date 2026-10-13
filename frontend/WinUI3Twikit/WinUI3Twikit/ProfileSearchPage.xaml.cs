using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace WinUI3Twikit
{
    public sealed partial class ProfileSearchPage : Page
    {
        public ProfileSearchViewModel ViewModel { get; }

        private ScrollViewer? _scrollViewer;
        private bool _loadingMoreFromScroll;
        private int _attachAttempts;

        /// <summary>TabView の選択を ViewModel と同期している間は SelectionChanged を無視する。</summary>
        private bool _tabReady;

        public ProfileSearchPage()
        {
            ViewModel = App.ViewModels.ProfileSearch;
            this.InitializeComponent();
            this.Loaded += ProfileSearchPage_Loaded;
        }

        private void ProfileSearchPage_Loaded(object sender, RoutedEventArgs e)
        {
            SyncSearchBoxFromViewModel();
            SyncTabFromViewModel();
            SearchBox?.Focus(FocusState.Programmatic);
            SubscribeViewModel();
            SubscribeListViewLayout();
            AttachScrollHandler();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            // 他ページからのプロフィール遷移直後に検索ボックスへ LastQuery を反映
            SyncSearchBoxFromViewModel();
            SyncTabFromViewModel();
            SubscribeViewModel();
            SubscribeListViewLayout();
            AttachScrollHandler();
        }

        private void SyncSearchBoxFromViewModel()
        {
            if (SearchBox != null && !string.IsNullOrEmpty(ViewModel.LastQuery))
            {
                SearchBox.Text = ViewModel.LastQuery;
            }
        }

        /// <summary>ViewModel が保持するタブ（ページ再訪問や新しいユーザーの検索でリセットされる）を TabView に反映する。</summary>
        private void SyncTabFromViewModel()
        {
            if (TweetsTabView == null)
            {
                return;
            }

            _tabReady = false;
            try
            {
                foreach (var item in TweetsTabView.TabItems)
                {
                    if (item is TabViewItem tab
                        && tab.Tag is string tag
                        && string.Equals(tag, ViewModel.CurrentTab, StringComparison.OrdinalIgnoreCase))
                    {
                        TweetsTabView.SelectedItem = tab;
                        break;
                    }
                }
            }
            finally
            {
                _tabReady = true;
            }
        }

        private async void TweetsTabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_tabReady)
            {
                return;
            }

            if (sender is not TabView tabView
                || tabView.SelectedItem is not TabViewItem tabItem
                || tabItem.Tag is not string tab
                || string.Equals(tab, ViewModel.CurrentTab, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await SwitchFeedAsync(() => ViewModel.SwitchTabAsync(tab));
        }

        private async void MediaKindMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuFlyoutItem { Tag: string kind })
            {
                return;
            }

            await SwitchFeedAsync(() => ViewModel.SwitchMediaKindAsync(kind));
        }

        /// <summary>
        /// タブ（またはメディアの写真 / 動画）を切り替える。
        /// 切り替え前のスクロール位置を保存し、切り替え後はタブが見える位置（保存済みならその位置）に戻す。
        /// </summary>
        private async Task SwitchFeedAsync(Func<Task> switchAsync)
        {
            ScrollPositionHelper.SaveOffset(_scrollViewer, offset => ViewModel.ScrollVerticalOffset = offset);

            App.MainWindow?.ShowLoading(true);
            try
            {
                await switchAsync();
            }
            finally
            {
                if (!ViewModel.IsLoadingMore)
                {
                    App.MainWindow?.ShowLoading(false);
                }
            }

            KeepTabStripVisible();
            ScrollPositionHelper.RestoreOffset(_scrollViewer, ViewModel.ScrollVerticalOffset);
            AttachScrollHandler();
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _ = LoadMoreIfNearBottomAsync());
        }

        /// <summary>プロフィールの下までスクロールした状態でタブを切り替えたとき、タブ列が画面外に残らないようにする。</summary>
        private void KeepTabStripVisible()
        {
            if (_scrollViewer == null || TweetsTabView == null)
            {
                return;
            }

            try
            {
                var tabTop = TweetsTabView.TransformToVisual(_scrollViewer)
                    .TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
                if (tabTop < 0)
                {
                    _scrollViewer.ChangeView(null, _scrollViewer.VerticalOffset + tabTop, null, true);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ProfileSearch KeepTabStripVisible: {ex.Message}");
            }
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            await PerformSearch();
        }

        private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                await PerformSearch();
            }
        }

        private async Task PerformSearch()
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                return;
            }

            App.MainWindow?.ShowLoading(true);
            try
            {
                await ViewModel.SearchAsync(SearchBox.Text);
            }
            finally
            {
                App.MainWindow?.ShowLoading(false);
            }

            AttachScrollHandler();
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _ = LoadMoreIfNearBottomAsync());
        }

        private void SubscribeViewModel()
        {
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        }

        private void SubscribeListViewLayout()
        {
            if (TweetsListView == null)
            {
                return;
            }

            TweetsListView.LayoutUpdated -= TweetsListView_LayoutUpdated;
            TweetsListView.LayoutUpdated += TweetsListView_LayoutUpdated;
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.CurrentTab))
            {
                // 新しいユーザーの検索でツイートタブへ戻ったときなど、ViewModel 側の変更を TabView に反映する
                SyncTabFromViewModel();
                return;
            }

            if (e.PropertyName is nameof(ViewModel.IsContentVisible) or nameof(ViewModel.IsLoading))
            {
                if (ViewModel.IsContentVisible)
                {
                    AttachScrollHandler();
                    DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _ = LoadMoreIfNearBottomAsync());
                }
            }
        }

        private void TweetsListView_LayoutUpdated(object? sender, object e)
        {
            if (_scrollViewer == null && ViewModel.IsContentVisible)
            {
                AttachScrollHandler();
            }
        }

        private void AttachScrollHandler()
        {
            if (TweetsListView == null || !ViewModel.IsContentVisible)
            {
                return;
            }

            var scrollViewer = ScrollPositionHelper.FindScrollViewer(TweetsListView);
            if (scrollViewer == null)
            {
                if (_attachAttempts < 20)
                {
                    _attachAttempts++;
                    DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, AttachScrollHandler);
                }
                return;
            }

            _attachAttempts = 0;
            if (ReferenceEquals(_scrollViewer, scrollViewer))
            {
                return;
            }

            if (_scrollViewer != null)
            {
                _scrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
            }

            _scrollViewer = scrollViewer;
            _scrollViewer.ViewChanged += ScrollViewer_ViewChanged;
            ScrollPositionHelper.RestoreOffset(_scrollViewer, ViewModel.ScrollVerticalOffset);
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _ = LoadMoreIfNearBottomAsync());
        }

        private async void ScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (sender is not ScrollViewer sv)
            {
                return;
            }

            if (!e.IsIntermediate)
            {
                ViewModel.ScrollVerticalOffset = sv.VerticalOffset;
            }

            await LoadMoreIfNearBottomAsync();
        }

        private async Task LoadMoreIfNearBottomAsync()
        {
            if (_loadingMoreFromScroll || _scrollViewer is not ScrollViewer sv)
            {
                return;
            }

            if (!ViewModel.IsContentVisible || ViewModel.IsLoading || ViewModel.IsLoadingMore || !ViewModel.CanLoadMore)
            {
                return;
            }

            if (sv.ViewportHeight <= 0 || sv.ExtentHeight <= 0)
            {
                return;
            }

            if (sv.VerticalOffset + sv.ViewportHeight < sv.ExtentHeight - 200)
            {
                return;
            }

            _loadingMoreFromScroll = true;
            App.MainWindow?.ShowLoading(true);
            try
            {
                await ViewModel.LoadMoreTweetsAsync();
            }
            finally
            {
                _loadingMoreFromScroll = false;
                if (!ViewModel.IsLoadingMore)
                {
                    App.MainWindow?.ShowLoading(false);
                }
            }

            // 追加後も最下部付近なら、レイアウト後にもう一度判定する
            if (ViewModel.CanLoadMore)
            {
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _ = LoadMoreIfNearBottomAsync());
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            ScrollPositionHelper.SaveOffset(_scrollViewer, offset => ViewModel.ScrollVerticalOffset = offset);
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            if (TweetsListView != null)
            {
                TweetsListView.LayoutUpdated -= TweetsListView_LayoutUpdated;
            }

            if (_scrollViewer != null)
            {
                _scrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
                _scrollViewer = null;
            }

            _attachAttempts = 0;
            base.OnNavigatedFrom(e);
        }
    }
}
