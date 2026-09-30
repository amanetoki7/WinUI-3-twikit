using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using System.Threading.Tasks;

namespace WinUI3Twikit
{
    public sealed partial class SearchPage : Page
    {
        public SearchViewModel ViewModel { get; }

        public SearchPage()
        {
            ViewModel = App.ViewModels.Search;
            this.InitializeComponent();
            this.Loaded += SearchPage_Loaded;
        }

        private void SearchPage_Loaded(object sender, RoutedEventArgs e)
        {
            SearchBox.Focus(FocusState.Programmatic);
            AttachScrollHandler();
        }

        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            await PerformSearch();
        }

        private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
                await PerformSearch();
        }

        private async Task PerformSearch()
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text)) return;
            await ViewModel.SearchAsync(SearchBox.Text.Trim());
        }

        private ScrollViewer? _scrollViewer;

        private void AttachScrollHandler()
        {
            if (SearchListView == null) return;

            _scrollViewer = ScrollPositionHelper.FindScrollViewer(SearchListView);
            if (_scrollViewer == null) return;

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

            if (!ViewModel.IsLoadingMore &&
                sv.VerticalOffset + sv.ViewportHeight >= sv.ExtentHeight - 200)
            {
                App.MainWindow?.ShowLoading(true);
                await ViewModel.LoadMoreAsync();
                App.MainWindow?.ShowLoading(false);
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            ScrollPositionHelper.SaveOffset(_scrollViewer, offset => ViewModel.ScrollVerticalOffset = offset);
            base.OnNavigatedFrom(e);
        }
    }
}
