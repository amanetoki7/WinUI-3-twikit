using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.ComponentModel;
using System.Threading.Tasks;
using Windows.Foundation;

namespace WinUI3Twikit
{
    /// <summary>
    /// ツイートをクリックしたときの会話ページ。ViewModel はナビゲーション引数で受け取る
    /// （<see cref="MainWindow.NavigateToConversation"/>）。戻る / 進むでは同じ ViewModel が渡されるので、
    /// 読み込み済みの会話とスクロール位置をそのまま表示する。
    /// </summary>
    public sealed partial class ConversationPage : Page
    {
        public ConversationViewModel? ViewModel { get; private set; }

        private ScrollViewer? _scrollViewer;
        private bool _loadingMoreFromScroll;
        private int _attachAttempts;
        private int _layoutWaits;

        public ConversationPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ViewModel = e.Parameter as ConversationViewModel;
            Bindings.Update();
            if (ViewModel is null)
            {
                return;
            }

            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            AttachScrollHandler();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (ViewModel is not null)
            {
                ScrollPositionHelper.SaveOffset(_scrollViewer, offset => ViewModel.ScrollVerticalOffset = offset);
                ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            }

            if (_scrollViewer != null)
            {
                _scrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
                _scrollViewer = null;
            }

            _attachAttempts = 0;
            base.OnNavigatedFrom(e);
        }

        private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ConversationViewModel.IsContentVisible) && ViewModel is { IsContentVisible: true })
            {
                AttachScrollHandler();
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (Frame?.CanGoBack == true)
            {
                Frame.GoBack();
            }
        }

        private void OpenMissingTweet_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string tweetId })
            {
                App.MainWindow?.NavigateToConversation(tweetId);
            }
        }

        private void AttachScrollHandler()
        {
            if (ConversationListView == null || ViewModel is not { IsContentVisible: true } vm)
            {
                return;
            }

            var scrollViewer = ScrollPositionHelper.FindScrollViewer(ConversationListView);
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

            if (vm.HasPositioned)
            {
                ScrollPositionHelper.RestoreOffset(_scrollViewer, vm.ScrollVerticalOffset);
            }
            else
            {
                vm.HasPositioned = true;
                if (vm.FocalEntry is { } focal && vm.Entries.IndexOf(focal) > 0)
                {
                    DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => ScrollToFocal(focal));
                }
            }

            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _ = LoadMoreIfNearBottomAsync());
        }

        /// <summary>
        /// 初めて開いたときは、返信元の流れの下にある開いたツイートを先頭に出す（X の Web 版と同じ）。
        /// ScrollIntoView の Leading は短い会話だと末尾に余白を足してしまうので、表示済みなら位置を測って動かす。
        /// </summary>
        private void ScrollToFocal(ConversationEntry focal)
        {
            if (_scrollViewer is null)
            {
                return;
            }

            if (ConversationListView.ContainerFromItem(focal) is not UIElement container)
            {
                // 返信元が長く、開いたツイートの行がまだ作られていない。
                ConversationListView.ScrollIntoView(focal, ScrollIntoViewAlignment.Leading);
                return;
            }

            var top = container.TransformToVisual(_scrollViewer).TransformPoint(new Point(0, 0)).Y;
            _scrollViewer.ChangeView(null, _scrollViewer.VerticalOffset + top, null, true);
        }

        private async void ScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
        {
            if (sender is not ScrollViewer sv || ViewModel is null)
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
            if (_loadingMoreFromScroll || _scrollViewer is not ScrollViewer sv || ViewModel is not { } vm)
            {
                return;
            }

            if (!vm.IsContentVisible || vm.IsLoadingMore || !vm.CanLoadMore)
            {
                return;
            }

            if (sv.ViewportHeight <= 0 || sv.ExtentHeight <= 0)
            {
                // 表示直後でまだレイアウトされていない。返信が少なくスクロールできない会話でも
                // 続きを確かめられるよう（スクロールが起きないので ViewChanged も来ない）、少し後で見直す。
                if (_layoutWaits++ < 20)
                {
                    DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _ = LoadMoreIfNearBottomAsync());
                }
                return;
            }

            _layoutWaits = 0;

            if (sv.VerticalOffset + sv.ViewportHeight < sv.ExtentHeight - 200)
            {
                return;
            }

            _loadingMoreFromScroll = true;
            App.MainWindow?.ShowLoading(true);
            try
            {
                await vm.LoadMoreRepliesAsync();
            }
            finally
            {
                _loadingMoreFromScroll = false;
                App.MainWindow?.ShowLoading(false);
            }

            // 追加後も最下部付近なら、レイアウト後にもう一度判定する
            if (vm.CanLoadMore)
            {
                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => _ = LoadMoreIfNearBottomAsync());
            }
        }
    }
}
