using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Threading.Tasks;

namespace WinUI3Twikit
{
    public sealed partial class TimelinePage : Page
    {
        public TimelineViewModel ViewModel { get; }

        public TimelinePage()
        {
            ViewModel = App.ViewModels.Timeline;
            this.InitializeComponent();
            this.Loaded += TimelinePage_Loaded;
            this.Unloaded += TimelinePage_Unloaded;
        }

        private void TimelinePage_Unloaded(object sender, RoutedEventArgs e)
        {
            StopAutoUpdate();
            if (ViewModel.CaptureViewport == CaptureViewport)
            {
                ViewModel.CaptureViewport = null;
            }

            ViewModel.RestoreViewportRequested -= OnRestoreViewportRequested;
        }

        private TimelineViewport? CaptureViewport()
        {
            if (_scrollViewer == null || TimelineListView == null)
            {
                return null;
            }

            var atTop = _scrollViewer.VerticalOffset < 1;
            if (!TimelineScrollAnchorHelper.TryCaptureAnchor(TimelineListView, _scrollViewer, out var anchor))
            {
                return new TimelineViewport { AtTop = atTop };
            }

            return new TimelineViewport { Anchor = anchor, AtTop = atTop };
        }

        private void OnRestoreViewportRequested(TimelineScrollAnchor anchor)
        {
            if (_scrollViewer == null || TimelineListView == null || anchor.IsEmpty)
            {
                return;
            }

            _isRestoringScroll = true;
            TimelineScrollAnchorHelper.RestoreAnchor(
                TimelineListView,
                _scrollViewer,
                anchor,
                onComplete: () => _isRestoringScroll = false);
        }

        private async void TimelinePage_Loaded(object sender, RoutedEventArgs e)
        {
            ViewModel.CaptureViewport = CaptureViewport;
            ViewModel.RestoreViewportRequested -= OnRestoreViewportRequested;
            ViewModel.RestoreViewportRequested += OnRestoreViewportRequested;

            if (ViewModel.Tweets.Count == 0 && !ViewModel.IsLoading)
            {
                await ViewModel.LoadTweetsAsync();
            }
            AttachScrollHandler(restoreAnchor: !ViewModel.ScrollAnchor.IsEmpty);
        }
        // ==================== 自動更新機能 ====================
        private DispatcherTimer? _autoUpdateTimer;

        private void AutoUpdateToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (AutoUpdateToggle.IsOn)
                StartAutoUpdate();
            else
                StopAutoUpdate();
        }

        private void StartAutoUpdate()
        {
            StopAutoUpdate();
            int interval = (int)PollingIntervalBox.Value;
            if (interval < 5) interval = 15;

            _autoUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(interval) };
            _autoUpdateTimer.Tick += async (s, args) => await PollNewTweetsAsync();
            _autoUpdateTimer.Start();
            System.Diagnostics.Debug.WriteLine($"自動更新開始 ({interval}秒間隔)");
        }

        private void StopAutoUpdate()
        {
            _autoUpdateTimer?.Stop();
            _autoUpdateTimer = null;
            System.Diagnostics.Debug.WriteLine("自動更新停止");
        }

        private async void ForceRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            // 自動更新OFF時の手動新着取得（ON時も同様に動作）
            System.Diagnostics.Debug.WriteLine("手動新着取得ボタン押下");
            await PollNewTweetsAsync(force: true);
        }

        private async Task PollNewTweetsAsync(bool force = false)
        {
            if (!force && (ViewModel.IsLoading || ViewModel.IsLoadingMore))
            {
                System.Diagnostics.Debug.WriteLine("ポーリングスキップ (Loading中)");
                return;
            }

            if (force)
            {
                App.MainWindow?.ShowLoading(true);
            }

            try
            {
                // 開始時点の種別を固定。await 中にタブ切替してもその種別にだけ反映する
                var requestType = ViewModel.CurrentTimelineType;
                System.Diagnostics.Debug.WriteLine($"新着取得開始 (type: {requestType}, force: {force})");

                var (newTweets, resolvedType) = await ViewModel.GetNewTweetsAsync(requestType);

                System.Diagnostics.Debug.WriteLine($"GetNewTweetsAsync 結果: {newTweets.Count}件 (type: {resolvedType})");

                if (newTweets.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("新着なし");
                    return;
                }

                // おすすめ: 先頭挿入 / 最新: 時系列で上側へ挿入（取得開始時の種別へ）
                ViewModel.ApplyNewTweets(newTweets, resolvedType);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ポーリング例外: {ex.Message}");
            }
            finally
            {
                if (force)
                {
                    App.MainWindow?.ShowLoading(false);
                }
            }
        }        // SelectionChanged に変更
        private async void TimelineTabView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is TabView tabView && tabView.SelectedItem is TabViewItem tabItem &&
                tabItem.Tag is string type)
            {
                TimelineScrollAnchorHelper.SaveAnchor(TimelineListView, _scrollViewer, anchor => ViewModel.ScrollAnchor = anchor);
                await ViewModel.SwitchTimelineAsync(type);
                AttachScrollHandler(restoreAnchor: true);
            }
        }

        private ScrollViewer? _scrollViewer;
        private bool _isRestoringScroll;

        private void AttachScrollHandler(bool restoreAnchor = false)
        {
            if (TimelineListView == null) return;

            _scrollViewer = ScrollPositionHelper.FindScrollViewer(TimelineListView);
            if (_scrollViewer == null) return;

            _scrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
            _scrollViewer.ViewChanged += ScrollViewer_ViewChanged;

            if (restoreAnchor && !ViewModel.ScrollAnchor.IsEmpty)
            {
                _isRestoringScroll = true;
                TimelineScrollAnchorHelper.RestoreAnchor(
                    TimelineListView,
                    _scrollViewer,
                    ViewModel.ScrollAnchor,
                    onComplete: () => _isRestoringScroll = false);
            }
        }

        private async void ScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer)
            {
                return;
            }

            if (!e.IsIntermediate && !_isRestoringScroll)
            {
                TimelineScrollAnchorHelper.SaveAnchor(TimelineListView, scrollViewer, anchor => ViewModel.ScrollAnchor = anchor);
            }

            if (ViewModel.IsLoading || ViewModel.IsLoadingMore || ViewModel.IsApplyingListChange || _isRestoringScroll)
            {
                return;
            }

            if (scrollViewer.VerticalOffset + scrollViewer.ViewportHeight < scrollViewer.ExtentHeight - 150)
            {
                return;
            }

            App.MainWindow?.ShowLoading(true);
            try
            {
                await ViewModel.LoadMoreTweetsAsync();
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
            TimelineScrollAnchorHelper.SaveAnchor(TimelineListView, _scrollViewer, anchor => ViewModel.ScrollAnchor = anchor);
            StopAutoUpdate();
            base.OnNavigatedFrom(e);
        }
    }
}
