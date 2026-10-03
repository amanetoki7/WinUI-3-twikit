using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media.Core;

namespace WinUI3Twikit
{
    internal static class MediaPreviewHelper
    {
        public static async Task ShowFromThumbnailAsync(
            FrameworkElement host,
            Image image,
            MediaItem? fallbackItem = null)
        {
            var mediaItem = image.DataContext as MediaItem ?? fallbackItem;
            var mediaUrl = image.Tag as string ?? mediaItem?.Url;

            if (string.IsNullOrWhiteSpace(mediaUrl))
            {
                mediaUrl = image.Source switch
                {
                    BitmapImage bitmap => bitmap.UriSource?.ToString(),
                    _ => null
                };
            }

            if (string.IsNullOrWhiteSpace(mediaUrl))
            {
                return;
            }

            if (mediaItem?.IsVideo == true
                || string.Equals(mediaItem?.Type, "video", StringComparison.OrdinalIgnoreCase))
            {
                await ShowFullScreenVideo(host, mediaUrl);
            }
            else
            {
                await ShowFullScreenImage(host, mediaUrl);
            }
        }

        private static Size GetHostSize(FrameworkElement host)
        {
            if (host.XamlRoot is { Size.Width: > 0, Size.Height: > 0 } root)
            {
                return root.Size;
            }

            if (host.ActualWidth > 0 && host.ActualHeight > 0)
            {
                return new Size(host.ActualWidth, host.ActualHeight);
            }

            return new Size(800, 600);
        }

        private static ContentDialog CreateMediaDialog(FrameworkElement host, UIElement content, Size hostSize)
        {
            var dialog = new ContentDialog
            {
                Title = null,
                Content = content,
                CloseButtonText = "閉じる",
                RequestedTheme = host.ActualTheme,
                FullSizeDesired = true,
                XamlRoot = host.XamlRoot
            };

            dialog.Resources["ContentDialogMinWidth"] = hostSize.Width - 40;
            dialog.Resources["ContentDialogMinHeight"] = hostSize.Height - 40;
            return dialog;
        }

        private static async Task ShowFullScreenImage(FrameworkElement host, string imageUrl)
        {
            if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
            {
                return;
            }

            var hostSize = GetHostSize(host);
            var bitmap = new BitmapImage
            {
                DecodePixelWidth = Math.Max(1, (int)(hostSize.Width * 0.9)),
                CreateOptions = BitmapCreateOptions.IgnoreImageCache,
                UriSource = uri
            };

            var image = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxHeight = hostSize.Height * 0.9,
                MaxWidth = hostSize.Width * 0.9
            };

            var dialog = CreateMediaDialog(host, image, hostSize);
            await dialog.ShowAsync();
            image.Source = null;
        }

        private static async Task ShowFullScreenVideo(FrameworkElement host, string videoUrl)
        {
            if (!Uri.TryCreate(videoUrl, UriKind.Absolute, out var uri))
            {
                return;
            }

            var hostSize = GetHostSize(host);
            var fullScreenPlayer = new MediaPlayerElement
            {
                Source = MediaSource.CreateFromUri(uri),
                AutoPlay = true,
                AreTransportControlsEnabled = true,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxHeight = hostSize.Height * 0.9,
                MaxWidth = hostSize.Width * 0.9
            };

            var dialog = CreateMediaDialog(host, fullScreenPlayer, hostSize);
            await dialog.ShowAsync();

            fullScreenPlayer.MediaPlayer?.Pause();
            fullScreenPlayer.Source = null;
        }
    }
}
