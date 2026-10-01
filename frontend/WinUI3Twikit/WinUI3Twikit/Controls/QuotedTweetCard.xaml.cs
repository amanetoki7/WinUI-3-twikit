using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Linq;

namespace WinUI3Twikit.Controls
{
    public sealed partial class QuotedTweetCard : UserControl
    {
        public static readonly DependencyProperty TweetProperty =
            DependencyProperty.Register(
                nameof(Tweet),
                typeof(TweetViewModel),
                typeof(QuotedTweetCard),
                new PropertyMetadata(null, OnTweetChanged));

        private static void OnTweetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is QuotedTweetCard card)
            {
                card.Visibility = e.NewValue is TweetViewModel tweet && tweet.HasQuotedTweet
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                card.Bindings?.Update();
            }
        }

        public TweetViewModel? Tweet
        {
            get => (TweetViewModel?)GetValue(TweetProperty);
            set => SetValue(TweetProperty, value);
        }

        public QuotedTweetCard()
        {
            InitializeComponent();
            Visibility = Visibility.Collapsed;
        }

        private void QuoteBorder_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (Tweet?.QuotedTweet?.IsUnavailable != false)
            {
                return;
            }

            // ホバー色は XAML の VisualState (ThemeResource) で切り替える
            VisualStateManager.GoToState(this, "PointerOver", true);
        }

        private void QuoteBorder_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            VisualStateManager.GoToState(this, "Normal", true);
        }

        private void QuoteBorder_Tapped(object sender, TappedRoutedEventArgs e)
        {
            // 将来: 引用元ツイート詳細へ遷移
        }

        private async void MediaThumbnail_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is Image image)
            {
                await MediaPreviewHelper.ShowFromThumbnailAsync(
                    this,
                    image,
                    Tweet?.QuotedCardMediaItems.FirstOrDefault());
            }
        }
    }
}