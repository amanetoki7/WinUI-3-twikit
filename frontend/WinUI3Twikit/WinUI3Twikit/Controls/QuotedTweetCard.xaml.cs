using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using Windows.System;

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

        /// <summary>引用元を既定のブラウザで開く。</summary>
        private async void QuoteBorder_Tapped(object sender, TappedRoutedEventArgs e)
        {
            var quoted = Tweet?.QuotedTweet;
            if (quoted is null || quoted.IsUnavailable || string.IsNullOrEmpty(quoted.Id))
            {
                return;
            }

            e.Handled = true;
            try
            {
                await Launcher.LaunchUriAsync(new Uri($"https://x.com/i/status/{quoted.Id}"));
            }
            catch (Exception ex)
            {
                AppNotifications.Error($"ブラウザで開けませんでした: {ex.Message}");
            }
        }
    }
}
