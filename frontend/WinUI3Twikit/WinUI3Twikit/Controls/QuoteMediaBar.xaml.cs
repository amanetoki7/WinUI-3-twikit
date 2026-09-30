using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace WinUI3Twikit.Controls
{
    public sealed partial class QuoteMediaBar : UserControl
    {
        private TextBox? _attachedTextBox;

        public QuoteMediaBar()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            DataContextChanged += OnDataContextChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e) => AttachToQuoteTextBox();

        private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
            => AttachToQuoteTextBox();

        private void OnUnloaded(object sender, RoutedEventArgs e) => DetachQuoteTextBox();

        private void AttachToQuoteTextBox()
        {
            var textBox = ReplyInputHelper.FindQuoteTextBox(this);
            if (ReferenceEquals(textBox, _attachedTextBox))
            {
                return;
            }

            DetachQuoteTextBox();
            if (textBox is null)
            {
                return;
            }

            textBox.Paste += QuoteTextBox_Paste;
            _attachedTextBox = textBox;
        }

        private void DetachQuoteTextBox()
        {
            if (_attachedTextBox is null)
            {
                return;
            }

            _attachedTextBox.Paste -= QuoteTextBox_Paste;
            _attachedTextBox = null;
        }

        private TweetViewModel? Tweet => DataContext as TweetViewModel;

        private void OnAddMediaClick(object sender, RoutedEventArgs e)
        {
            var vm = Tweet;
            if (vm is null || vm.IsQuoteSending)
            {
                return;
            }

            var hwnd = MediaAttachmentHelper.GetMainWindowHandle();
            MediaAttachmentHelper.AddFromPaths(vm.QuoteMediaFiles, MediaAttachmentHelper.PickMedia(hwnd));
        }

        private void RemoveMedia_Click(object sender, RoutedEventArgs e)
        {
            var vm = Tweet;
            if (vm is null || vm.IsQuoteSending)
            {
                return;
            }

            if (sender is Button { Tag: MediaFile media })
            {
                vm.QuoteMediaFiles.Remove(media);
            }
        }

        private async void QuoteTextBox_Paste(object sender, TextControlPasteEventArgs e)
        {
            var vm = Tweet;
            if (vm is null || vm.IsQuoteSending)
            {
                return;
            }

            try
            {
                if (await MediaAttachmentHelper.TryAddFromClipboardAsync(vm.QuoteMediaFiles))
                {
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Quote paste media failed: {ex.Message}");
            }
        }
    }
}
