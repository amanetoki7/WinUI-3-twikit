using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Linq;
using System.Threading.Tasks;
using WinUI3Twikit.Controls;

namespace WinUI3Twikit
{
    public sealed partial class TimelinePage : Page
    {
        public TimelineViewModel ViewModel { get; }

        public TimelinePage()
        {
            ViewModel = App.ViewModels.Timeline;
            this.InitializeComponent();
            SyncTabsFromViewModel();
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

            // TabView は読み込み時に自分で先頭タブを選ぶことがあるので、ViewModel の種別にもう一度合わせる
            SyncTabsFromViewModel();

            if (ViewModel.Tweets.Count == 0 && !ViewModel.IsLoading)
            {
                await ViewModel.LoadTweetsAsync();
            }
            AttachScrollHandler(restoreAnchor: !ViewModel.ScrollAnchor.IsEmpty);
        }

        // ==================== タブ（おすすめ / 最新 / 追加したリスト） ====================

        /// <summary>TabView の選択を ViewModel と同期している間は SelectionChanged を無視する。</summary>
        private bool _tabReady;

        /// <summary>
        /// ViewModel が保持するリストのタブを TabView に並べ、表示中のタイムライン種別のタブを選ぶ。
        /// ページは遷移のたびに作り直されるが、タブとタイムラインは ViewModel に残っているのでここで戻す。
        /// 何度呼んでもタブは増えない。
        /// </summary>
        private void SyncTabsFromViewModel()
        {
            if (TimelineTabView == null)
            {
                return;
            }

            _tabReady = false;
            try
            {
                foreach (var tab in ViewModel.ListTabs)
                {
                    if (FindTabItem(tab.TimelineType) is null)
                    {
                        TimelineTabView.TabItems.Add(CreateListTabItem(tab));
                    }
                }

                var current = FindTabItem(ViewModel.CurrentTimelineType) ?? TimelineTabView.TabItems.FirstOrDefault();
                if (current is not null && !ReferenceEquals(TimelineTabView.SelectedItem, current))
                {
                    TimelineTabView.SelectedItem = current;
                }
            }
            finally
            {
                _tabReady = true;
            }
        }

        private TabViewItem? FindTabItem(string? timelineType)
        {
            foreach (var item in TimelineTabView.TabItems)
            {
                if (item is TabViewItem tab
                    && tab.Tag is string tag
                    && string.Equals(tag, timelineType, StringComparison.OrdinalIgnoreCase))
                {
                    return tab;
                }
            }

            return null;
        }

        private static TabViewItem CreateListTabItem(HomeListTab tab)
        {
            var item = new TabViewItem
            {
                Header = tab.Name,
                Tag = tab.TimelineType,
                IsClosable = true,
                IconSource = new SymbolIconSource { Symbol = Symbol.List },
            };
            ToolTipService.SetToolTip(item, $"リスト: {tab.Name}");
            return item;
        }

        private void TimelineTabView_Loaded(object sender, RoutedEventArgs e)
        {
            // 「+」の既定のツールチップ「新しいタブの追加」はテンプレート適用時に固定されるので、読み込み後に差し替える
            if (FindDescendant<Button>(TimelineTabView, "AddButton") is { } addButton)
            {
                ToolTipService.SetToolTip(addButton, "リストをタブに追加");
            }
        }

        private static T? FindDescendant<T>(DependencyObject root, string name) where T : FrameworkElement
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T match && string.Equals(match.Name, name, StringComparison.Ordinal))
                {
                    return match;
                }

                if (FindDescendant<T>(child, name) is { } found)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>「+」: 自分のリストから選んでタブに追加し、そのタブへ切り替える（追加済みのリストならそのタブへ移るだけ）。</summary>
        private async void TimelineTabView_AddTabButtonClick(TabView sender, object args)
        {
            if (XamlRoot is null || DialogHelper.IsDialogOpen)
            {
                return;
            }

            var added = ViewModel.ListTabs.Select(t => t.ListId).ToHashSet(StringComparer.Ordinal);
            var dialog = new ListPickerDialog(added)
            {
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme,
            };

            var result = await DialogHelper.ShowAsync(dialog);
            if (!dialog.WasConfirmed(result) || dialog.SelectedList is not { } list)
            {
                return;
            }

            var tab = ViewModel.AddListTab(list.Id, list.Name);
            var item = FindTabItem(tab.TimelineType);
            if (item is null)
            {
                item = CreateListTabItem(tab);
                sender.TabItems.Add(item);
            }

            // SelectionChanged → SwitchTimelineAsync（初めて開くタブはそこで読み込む）
            sender.SelectedItem = item;
        }

        /// <summary>リストのタブの ×: タブと、保持していたツイートを捨てる。</summary>
        private async void TimelineTabView_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        {
            if (TimelineViewModel.ListIdOf(args.Tab.Tag as string) is not { } listId)
            {
                return;
            }

            // 表示中のタブを消すと TabView が隣のタブを選ぶ（SelectionChanged で切り替わる）
            sender.TabItems.Remove(args.Tab);
            ViewModel.RemoveListTab(listId);

            if (sender.SelectedItem is null || ReferenceEquals(sender.SelectedItem, args.Tab))
            {
                // 隣が選ばれなかったときは ViewModel が戻したおすすめへ
                SyncTabsFromViewModel();
                if (ViewModel.Tweets.Count == 0 && !ViewModel.IsLoading)
                {
                    await ViewModel.LoadTweetsAsync();
                }

                AttachScrollHandler(restoreAnchor: true);
            }
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
            if (!_tabReady)
            {
                return;
            }

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
