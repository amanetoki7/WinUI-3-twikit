using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Threading.Tasks;

namespace WinUI3Twikit
{
    public sealed partial class ListsPage : Page
    {
        public ListsViewModel ViewModel { get; }

        public ListsPage()
        {
            ViewModel = App.ViewModels.Lists;
            InitializeComponent();
            Loaded += ListsPage_Loaded;
        }

        private async void ListsPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel.Lists.Count == 0 && !ViewModel.IsLoading)
            {
                await ViewModel.LoadListsAsync();
            }

            AttachScrollHandler();
        }

        private void BreadcrumbList_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.BackToListCatalog();
            AttachScrollHandler();
        }

        private async void ListsListView_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is not ListItemViewModel listItem)
            {
                return;
            }

            await ViewModel.SelectListAsync(listItem.Id, listItem.Name);
            AttachScrollHandler();
        }

        private ScrollViewer? _scrollViewer;

        private void AttachScrollHandler()
        {
            var targetListView = ViewModel.ShowTimeline ? TimelineListView : ListsListView;
            if (targetListView == null)
            {
                return;
            }

            _scrollViewer = ScrollPositionHelper.FindScrollViewer(targetListView);
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

            if (!ViewModel.ShowTimeline || ViewModel.IsLoading || ViewModel.IsLoadingMore)
            {
                return;
            }

            if (sv.VerticalOffset + sv.ViewportHeight < sv.ExtentHeight - 150)
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
