using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Threading.Tasks;

namespace WinUI3Twikit
{
    /// <summary>
    /// 自分のプロフィールページ。プロフィール + ツイート一覧（無限スクロール）。
    /// </summary>
    public sealed partial class MyProfilePage : Page
    {
        public MyProfileViewModel ViewModel { get; }

        private ScrollViewer? _scrollViewer;

        public MyProfilePage()
        {
            ViewModel = App.ViewModels.MyProfile;
            this.InitializeComponent();
            this.Loaded += MyProfilePage_Loaded;
        }

        private async void MyProfilePage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadProfileAsync();
            AttachScrollHandler();
        }

        private async Task LoadProfileAsync()
        {
            App.MainWindow?.ShowLoading(true);
            try
            {
                await ViewModel.LoadAsync();
            }
            finally
            {
                App.MainWindow?.ShowLoading(false);
            }
        }

        private void AttachScrollHandler()
        {
            if (TweetsListView == null)
            {
                return;
            }

            _scrollViewer = ScrollPositionHelper.FindScrollViewer(TweetsListView);
            if (_scrollViewer == null)
            {
                return;
            }

            _scrollViewer.ViewChanged -= ScrollViewer_ViewChanged;
            _scrollViewer.ViewChanged += ScrollViewer_ViewChanged;
            ScrollPositionHelper.RestoreOffset(_scrollViewer, ViewModel.ScrollVerticalOffset);
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

            if (!ViewModel.IsContentVisible || ViewModel.IsLoading || ViewModel.IsLoadingMore)
            {
                return;
            }

            if (sv.VerticalOffset + sv.ViewportHeight < sv.ExtentHeight - 200)
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
            ScrollPositionHelper.SaveOffset(_scrollViewer, offset => ViewModel.ScrollVerticalOffset = offset);
            base.OnNavigatedFrom(e);
        }
    }
}
