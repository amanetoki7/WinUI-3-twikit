using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinUI3Twikit.Controls
{
    public sealed partial class TweetActionBar : UserControl
    {
        public static readonly DependencyProperty TweetProperty =
            DependencyProperty.Register(
                nameof(Tweet),
                typeof(TweetViewModel),
                typeof(TweetActionBar),
                new PropertyMetadata(null, OnTweetChanged));

        public TweetViewModel? Tweet
        {
            get => (TweetViewModel?)GetValue(TweetProperty);
            set => SetValue(TweetProperty, value);
        }

        private ITweetListActions? _hostActions;

        public TweetActionBar()
        {
            InitializeComponent();
        }

        private static void OnTweetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TweetActionBar bar)
            {
                bar.DataContext = e.NewValue;
                bar.Bindings?.Update();
            }
        }

        private ITweetListActions? ResolveActions()
        {
            if (_hostActions != null)
            {
                return _hostActions;
            }

            _hostActions = TweetCard.ResolveHostActions(this);
            return _hostActions;
        }

        private async void LikeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: TweetViewModel vm }
                && ResolveActions() is { } actions)
            {
                await TweetActionHandler.HandleLikeAsync(
                    vm,
                    actions.LikeTweetAsync,
                    actions.FindTweetById);
            }
        }

        private async void RetweetMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem { Tag: TweetViewModel vm }
                && ResolveActions() is { } actions)
            {
                await TweetActionHandler.HandleRetweetAsync(
                    vm,
                    actions.RetweetTweetAsync,
                    actions.FindTweetById);
            }
        }

        private void QuoteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem { Tag: TweetViewModel vm })
            {
                vm.BeginQuoting();
            }
        }

        private void ReplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: TweetViewModel vm })
            {
                vm.IsReplying = !vm.IsReplying;
                if (vm.IsReplying)
                {
                    vm.ReplyText = string.Empty;
                    vm.CancelQuoting();
                }
            }
        }
    }
}
