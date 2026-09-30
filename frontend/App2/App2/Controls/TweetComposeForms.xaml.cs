using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace WinUI3Twikit.Controls
{
    public sealed partial class TweetComposeForms : UserControl
    {
        public static readonly DependencyProperty TweetProperty =
            DependencyProperty.Register(
                nameof(Tweet),
                typeof(TweetViewModel),
                typeof(TweetComposeForms),
                new PropertyMetadata(null, OnTweetChanged));

        public TweetViewModel? Tweet
        {
            get => (TweetViewModel?)GetValue(TweetProperty);
            set => SetValue(TweetProperty, value);
        }

        private ITweetListActions? _hostActions;

        public TweetComposeForms()
        {
            InitializeComponent();
        }

        private static void OnTweetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TweetComposeForms forms)
            {
                forms.DataContext = e.NewValue;
                forms.Bindings?.Update();
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

        private async void SendReply_Click(object sender, RoutedEventArgs e)
        {
            if (ResolveActions() is not { } actions)
            {
                return;
            }

            await TweetReplySender.TrySendAsync(
                sender,
                actions.ReplyTweetAsync,
                actions.AddReplyToTimeline);
        }

        private void ReplyForm_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (!ReplyInputHelper.IsCtrlEnter(e) || sender is not DependencyObject depObj)
            {
                return;
            }

            if (ReplyInputHelper.TrySendReply(depObj, s => SendReply_Click(s, new RoutedEventArgs())))
            {
                e.Handled = true;
            }
        }

        private void CancelReply_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: TweetViewModel vm })
            {
                if (vm.IsReplySending)
                {
                    return;
                }

                vm.IsReplying = false;
                vm.ReplyText = string.Empty;
            }
        }

        private async void SendQuote_Click(object sender, RoutedEventArgs e)
        {
            if (ResolveActions() is not { } actions)
            {
                return;
            }

            await TweetQuoteSender.TrySendAsync(
                sender,
                actions.QuoteTweetAsync,
                actions.AddQuoteToTimeline);
        }

        private void QuoteForm_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (!ReplyInputHelper.IsCtrlEnter(e) || sender is not DependencyObject depObj)
            {
                return;
            }

            if (ReplyInputHelper.TrySendQuote(depObj, s => SendQuote_Click(s, new RoutedEventArgs())))
            {
                e.Handled = true;
            }
        }

        private void CancelQuote_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: TweetViewModel vm })
            {
                vm.CancelQuoting();
            }
        }
    }
}
