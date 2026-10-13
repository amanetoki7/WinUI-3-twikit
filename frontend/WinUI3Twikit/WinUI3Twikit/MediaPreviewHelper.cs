using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.System;

namespace WinUI3Twikit
{
    /// <summary>
    /// 画像のフルサイズ表示（ズーム可）と動画・GIF の再生。ダイアログの「ブラウザで開く」でメディアの URL を既定ブラウザで開く。
    /// </summary>
    internal static class MediaPreviewHelper
    {
        public static async Task ShowAsync(FrameworkElement host, MediaItem? item)
        {
            if (item is null || host.XamlRoot is null || string.IsNullOrWhiteSpace(item.PreviewUrl))
            {
                return;
            }

            if (item.IsVideo)
            {
                await ShowVideoAsync(host, item);
            }
            else
            {
                await ShowImageAsync(host, item);
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

        private static ContentDialog CreateDialog(FrameworkElement host, UIElement content, Size hostSize, string url)
        {
            var dialog = new ContentDialog
            {
                Title = null,
                Content = content,
                CloseButtonText = "閉じる",
                PrimaryButtonText = "ブラウザで開く",
                DefaultButton = ContentDialogButton.Close,
                RequestedTheme = host.ActualTheme,
                FullSizeDesired = true,
                XamlRoot = host.XamlRoot,
            };

            var width = Math.Max(320, hostSize.Width - 48);
            var height = Math.Max(240, hostSize.Height - 48);
            dialog.Resources["ContentDialogMinWidth"] = width;
            dialog.Resources["ContentDialogMaxWidth"] = width;
            dialog.Resources["ContentDialogMinHeight"] = height;
            dialog.Resources["ContentDialogMaxHeight"] = height;
            dialog.PrimaryButtonClick += async (_, _) =>
            {
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    try
                    {
                        await Launcher.LaunchUriAsync(uri);
                    }
                    catch (Exception ex)
                    {
                        AppNotifications.Error($"ブラウザで開けませんでした: {ex.Message}");
                    }
                }
            };
            return dialog;
        }

        private static async Task ShowImageAsync(FrameworkElement host, MediaItem item)
        {
            if (!Uri.TryCreate(item.PreviewUrl, UriKind.Absolute, out var uri))
            {
                return;
            }

            var hostSize = GetHostSize(host);
            var bitmap = new BitmapImage
            {
                CreateOptions = BitmapCreateOptions.IgnoreImageCache,
                UriSource = uri,
            };

            var image = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxHeight = hostSize.Height * 0.82,
                MaxWidth = hostSize.Width * 0.9,
            };

            // ホイール / ピンチで最大 4 倍まで拡大できる
            var scroll = new ScrollViewer
            {
                Content = image,
                ZoomMode = ZoomMode.Enabled,
                MinZoomFactor = 1,
                MaxZoomFactor = 4,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };

            var dialog = CreateDialog(host, scroll, hostSize, item.PreviewUrl);
            await DialogHelper.ShowAsync(dialog);
            image.Source = null;
        }

        private static async Task ShowVideoAsync(FrameworkElement host, MediaItem item)
        {
            if (!Uri.TryCreate(item.PreviewUrl, UriKind.Absolute, out var uri))
            {
                return;
            }

            var hostSize = GetHostSize(host);
            var player = new MediaPlayerElement
            {
                Source = MediaSource.CreateFromUri(uri),
                AutoPlay = true,
                AreTransportControlsEnabled = !item.IsGif,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxHeight = hostSize.Height * 0.82,
                MaxWidth = hostSize.Width * 0.9,
            };

            var dialog = CreateDialog(host, player, hostSize, item.PreviewUrl);
            dialog.Opened += (_, _) =>
            {
                // GIF（X は mp4 で配信）はループ・ミュートで再生する
                if (player.MediaPlayer is { } mediaPlayer)
                {
                    mediaPlayer.IsLoopingEnabled = item.IsGif;
                    mediaPlayer.IsMuted = item.IsGif;
                }
            };

            await DialogHelper.ShowAsync(dialog);

            player.MediaPlayer?.Pause();
            player.Source = null;
        }
    }
}
