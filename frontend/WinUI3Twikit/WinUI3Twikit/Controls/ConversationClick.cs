using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace WinUI3Twikit.Controls
{
    /// <summary>
    /// カードのクリックで会話ページを開く。<see cref="TweetCard"/> と、通知ページのように独自のカードを使う画面で共通。
    /// </summary>
    /// <remarks>
    /// 独自のカードでは、カードを包む Panel に <c>TweetId</c> を付け、その中にホバー用の重ね板
    /// （<c>IsHoverOverlay="True"</c>、<c>Opacity="0"</c>、<c>IsHitTestVisible="False"</c>）を置く。
    /// 重ね板の色は XAML の ThemeResource で指定し、ここでは Opacity だけを切り替える（テーマ切り替えに追従させるため）。
    /// </remarks>
    public static class ConversationClick
    {
        /// <summary>クリックで開くツイートの ID。空ならクリックもホバー表示もしない（フォロー通知など）。</summary>
        public static readonly DependencyProperty TweetIdProperty =
            DependencyProperty.RegisterAttached(
                "TweetId",
                typeof(string),
                typeof(ConversationClick),
                new PropertyMetadata(null, OnTweetIdChanged));

        public static readonly DependencyProperty IsHoverOverlayProperty =
            DependencyProperty.RegisterAttached(
                "IsHoverOverlay",
                typeof(bool),
                typeof(ConversationClick),
                new PropertyMetadata(false));

        private static readonly DependencyProperty IsAttachedProperty =
            DependencyProperty.RegisterAttached(
                "IsAttached",
                typeof(bool),
                typeof(ConversationClick),
                new PropertyMetadata(false));

        public static string? GetTweetId(DependencyObject element) => (string?)element.GetValue(TweetIdProperty);

        public static void SetTweetId(DependencyObject element, string? value) => element.SetValue(TweetIdProperty, value);

        public static bool GetIsHoverOverlay(DependencyObject element) => (bool)element.GetValue(IsHoverOverlayProperty);

        public static void SetIsHoverOverlay(DependencyObject element, bool value) => element.SetValue(IsHoverOverlayProperty, value);

        private static void OnTweetIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Panel panel)
            {
                return;
            }

            // ListView でカードが別の通知に使い回されたとき、前のホバー表示を残さない。
            SetHover(panel, false);
            if ((bool)panel.GetValue(IsAttachedProperty))
            {
                return;
            }

            panel.SetValue(IsAttachedProperty, true);
            // 本文（選択できる TextBlock）はタップを Handled にするので、Handled 済みも受け取って判断する。
            panel.AddHandler(UIElement.TappedEvent, new TappedEventHandler(Panel_Tapped), handledEventsToo: true);
            panel.PointerEntered += (_, _) => SetHover(panel, HasTweetId(panel));
            panel.PointerExited += (_, _) => SetHover(panel, false);
            panel.PointerCanceled += (_, _) => SetHover(panel, false);
            panel.PointerCaptureLost += (_, _) => SetHover(panel, false);
        }

        private static bool HasTweetId(DependencyObject element) => !string.IsNullOrEmpty(GetTweetId(element));

        private static void SetHover(Panel panel, bool hovered)
        {
            foreach (var child in panel.Children)
            {
                if (GetIsHoverOverlay(child))
                {
                    child.Opacity = hovered ? 1 : 0;
                }
            }
        }

        private static void Panel_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is not Panel panel
                || GetTweetId(panel) is not { Length: > 0 } tweetId
                || !IsCardTap(e, panel))
            {
                return;
            }

            e.Handled = true;
            App.MainWindow?.NavigateToConversation(tweetId);
        }

        /// <summary>
        /// カードの余白や本文のタップか。ボタン・入力欄・引用・本文のリンクの上、またはアイコンやメディアのように
        /// 自分でタップを処理した（Handled にした）部品の上なら false。
        /// </summary>
        /// <param name="card">クリックを受けるカードの要素。ここより上はたどらない。</param>
        public static bool IsCardTap(TappedRoutedEventArgs e, DependencyObject card)
        {
            TweetTextBlock? bodyText = null;
            var onSelectableText = false;
            for (var current = e.OriginalSource as DependencyObject;
                 current is not null && !ReferenceEquals(current, card);
                 current = VisualTreeHelper.GetParent(current))
            {
                if (current is ButtonBase or TextBox or QuotedTweetCard)
                {
                    return false;
                }

                bodyText ??= current as TweetTextBlock;
                onSelectableText |= current is TextBlock { IsTextSelectionEnabled: true };
            }

            if (bodyText is not null)
            {
                return !bodyText.IsLinkAt(e.GetPosition(bodyText));
            }

            // 選択できる TextBlock はタップを Handled にするが、クリックの処理ではない。
            return !e.Handled || onSelectableText;
        }
    }
}
