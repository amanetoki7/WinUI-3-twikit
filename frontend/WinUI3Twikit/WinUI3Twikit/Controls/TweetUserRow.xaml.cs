using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace WinUI3Twikit.Controls
{
    public sealed partial class TweetUserRow : UserControl
    {
        public TweetUserRow()
        {
            InitializeComponent();
        }

        private void ProfileImage_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;

            if (string.IsNullOrWhiteSpace(UserScreenName))
            {
                return;
            }

            App.MainWindow?.NavigateToProfileSearch(UserScreenName);
        }

        public static readonly DependencyProperty UserNameProperty =
            DependencyProperty.Register(
                nameof(UserName),
                typeof(string),
                typeof(TweetUserRow),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty UserScreenNameProperty =
            DependencyProperty.Register(
                nameof(UserScreenName),
                typeof(string),
                typeof(TweetUserRow),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty CreatedAtAbsoluteDisplayProperty =
            DependencyProperty.Register(
                nameof(CreatedAtAbsoluteDisplay),
                typeof(string),
                typeof(TweetUserRow),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty CreatedAtRelativeDisplayProperty =
            DependencyProperty.Register(
                nameof(CreatedAtRelativeDisplay),
                typeof(string),
                typeof(TweetUserRow),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty UserProfileImageProperty =
            DependencyProperty.Register(
                nameof(UserProfileImage),
                typeof(ImageSource),
                typeof(TweetUserRow),
                new PropertyMetadata(null));

        public static readonly DependencyProperty IsUserProtectedProperty =
            DependencyProperty.Register(
                nameof(IsUserProtected),
                typeof(bool),
                typeof(TweetUserRow),
                new PropertyMetadata(false));

        public static readonly DependencyProperty IsUserVerifiedProperty =
            DependencyProperty.Register(
                nameof(IsUserVerified),
                typeof(bool),
                typeof(TweetUserRow),
                new PropertyMetadata(false));

        public static readonly DependencyProperty ReplyToScreenNameProperty =
            DependencyProperty.Register(
                nameof(ReplyToScreenName),
                typeof(string),
                typeof(TweetUserRow),
                new PropertyMetadata(string.Empty, OnReplyToScreenNameChanged));

        public static readonly DependencyProperty ReplyToDisplayProperty =
            DependencyProperty.Register(
                nameof(ReplyToDisplay),
                typeof(string),
                typeof(TweetUserRow),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty HasReplyToProperty =
            DependencyProperty.Register(
                nameof(HasReplyTo),
                typeof(bool),
                typeof(TweetUserRow),
                new PropertyMetadata(false));

        private static void OnReplyToScreenNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var row = (TweetUserRow)d;
            var name = (e.NewValue as string ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                row.ReplyToDisplay = string.Empty;
                row.HasReplyTo = false;
                return;
            }

            if (!name.StartsWith('@'))
            {
                name = "@" + name;
            }

            row.ReplyToDisplay = name;
            row.HasReplyTo = true;
        }

        public string UserName
        {
            get => (string)GetValue(UserNameProperty);
            set => SetValue(UserNameProperty, value);
        }

        public string UserScreenName
        {
            get => (string)GetValue(UserScreenNameProperty);
            set => SetValue(UserScreenNameProperty, value);
        }

        public string CreatedAtAbsoluteDisplay
        {
            get => (string)GetValue(CreatedAtAbsoluteDisplayProperty);
            set => SetValue(CreatedAtAbsoluteDisplayProperty, value);
        }

        public string CreatedAtRelativeDisplay
        {
            get => (string)GetValue(CreatedAtRelativeDisplayProperty);
            set => SetValue(CreatedAtRelativeDisplayProperty, value);
        }

        public ImageSource? UserProfileImage
        {
            get => (ImageSource?)GetValue(UserProfileImageProperty);
            set => SetValue(UserProfileImageProperty, value);
        }

        public bool IsUserProtected
        {
            get => (bool)GetValue(IsUserProtectedProperty);
            set => SetValue(IsUserProtectedProperty, value);
        }

        public bool IsUserVerified
        {
            get => (bool)GetValue(IsUserVerifiedProperty);
            set => SetValue(IsUserVerifiedProperty, value);
        }

        public string ReplyToScreenName
        {
            get => (string)GetValue(ReplyToScreenNameProperty);
            set => SetValue(ReplyToScreenNameProperty, value);
        }

        public string ReplyToDisplay
        {
            get => (string)GetValue(ReplyToDisplayProperty);
            set => SetValue(ReplyToDisplayProperty, value);
        }

        public bool HasReplyTo
        {
            get => (bool)GetValue(HasReplyToProperty);
            set => SetValue(HasReplyToProperty, value);
        }
    }
}