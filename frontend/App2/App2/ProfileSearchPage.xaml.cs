using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
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

        public ProfileSearchPage()
        {
            ViewModel = App.ViewModels.ProfileSearch;
            this.InitializeComponent();
            this.Loaded += ProfileSearchPage_Loaded;
        }

        private void ProfileSearchPage_Loaded(object sender, RoutedEventArgs e)
        {
            SyncSearchBoxFromViewModel();
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
