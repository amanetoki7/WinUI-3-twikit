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

        public static readonly DependencyProperty IsFocalProperty =
            DependencyProperty.Register(
                nameof(IsFocal),
                typeof(bool),
                typeof(TweetCard),
                new PropertyMetadata(false, OnIsFocalChanged));

        public static readonly DependencyProperty ShowReplyToProperty =
            DependencyProperty.Register(
                nameof(ShowReplyTo),
                typeof(bool),
                typeof(TweetCard),
                new PropertyMetadata(true));

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

        /// <summary>会話ページで開いているツイート。枠を強調し、クリックしても遷移しない。</summary>
        public bool IsFocal
        {
            get => (bool)GetValue(IsFocalProperty);
            set => SetValue(IsFocalProperty, value);
        }

        /// <summary>
        /// 「返信先: @名前」を出すか。会話ページは返信の連鎖を線で表すので出さない。
        /// </summary>
        public bool ShowReplyTo
        {
            get => (bool)GetValue(ShowReplyToProperty);
            set => SetValue(ShowReplyToProperty, value);
        }

        public string ReplyToFor(string? replyToScreenName, bool showReplyTo)
            => showReplyTo ? replyToScreenName ?? string.Empty : string.Empty;

        public TweetCard()
        {
            InitializeComponent();
            // 本文（選択できる TextBlock）はタップを Handled にするので、Handled 済みも受け取って判断する。
            CardBorder.AddHandler(TappedEvent, new TappedEventHandler(CardBorder_Tapped), handledEventsToo: true);
            // 読み込み前に IsFocal が設定されると GoToState が効かないので、表示時にも合わせる。
            Loaded += (_, _) => ApplyFocalState();
        }

        private static void OnTweetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TweetCard card)
            {
                card.DataContext = e.NewValue;
                card.Bindings?.Update();
                // ListView でカードが別のツイートに使い回されたとき、前のホバー表示を残さない。
                VisualStateManager.GoToState(card, "Normal", false);
            }
        }

        private static void OnIsFocalChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TweetCard card)
            {
                card.ApplyFocalState();
            }
        }

        private void ApplyFocalState()
        {
            VisualStateManager.GoToState(this, IsFocal ? "Focal" : "NotFocal", false);
            if (IsFocal)
            {
                VisualStateManager.GoToState(this, "Normal", false);
            }
        }

        private void CardBorder_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (!IsFocal)
            {
                VisualStateManager.GoToState(this, "PointerOver", true);
            }
        }

        private void CardBorder_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            VisualStateManager.GoToState(this, "Normal", true);
        }

        private void CardBorder_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (IsFocal
                || Tweet is not { Id.Length: > 0 } tweet
                || !ConversationClick.IsCardTap(e, CardBorder))
            {
                return;
            }

            e.Handled = true;
            App.MainWindow?.NavigateToConversation(tweet.Id);
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
    }
}
