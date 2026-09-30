using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace WinUI3Twikit.Controls
{
    public sealed partial class TweetCard : UserControl
    {
        public static readonly DependencyProperty TweetProperty =
            DependencyProperty.Register(
                nameof(Tweet),
                typeof(TweetViewModel),
                typeof(TweetCard),
                new PropertyMetadata(null, OnTweetChanged));

        public static readonly DependencyProperty HostActionsProperty =
            DependencyProperty.RegisterAttached(
                "HostActions",
                typeof(ITweetListActions),
                typeof(TweetCard),
                new PropertyMetadata(null));

        public static void SetHostActions(DependencyObject element, ITweetListActions? value)
            => element.SetValue(HostActionsProperty, value);

        public static ITweetListActions? GetHostActions(DependencyObject element)
            => (ITweetListActions?)element.GetValue(HostActionsProperty);

        public TweetViewModel? Tweet
        {
            get => (TweetViewModel?)GetValue(TweetProperty);
            set => SetValue(TweetProperty, value);
        }

        public TweetCard()
        {
            InitializeComponent();
        }

        private static void OnTweetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TweetCard card)
            {
                card.DataContext = e.NewValue;
                card.Bindings?.Update();
            }
        }

        public static ITweetListActions? ResolveHostActions(DependencyObject start)
        {
            DependencyObject? current = start;
            while (current != null)
            {
                var host = GetHostActions(current);
                if (host != null)
                {
                    return host;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private async void MediaThumbnail_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is Image image)
            {
                await MediaPreviewHelper.ShowFromThumbnailAsync(this, image);
            }
        }
    }
}
