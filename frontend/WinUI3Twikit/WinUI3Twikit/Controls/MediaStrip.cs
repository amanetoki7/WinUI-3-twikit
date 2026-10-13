using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Collections.Generic;
using System.Linq;
using Windows.UI;

namespace WinUI3Twikit.Controls
{
    /// <summary>
    /// メディアのサムネイルを横に並べる。画像は必ず全体を表示する（Uniform。切り抜かない）。
    /// 動画・GIF にはサムネイルの上に再生アイコンとバッジを重ねる。クリックで拡大プレビュー。
    /// </summary>
    public sealed class MediaStrip : StackPanel
    {
        public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
            nameof(Items),
            typeof(object),
            typeof(MediaStrip),
            new PropertyMetadata(null, (d, _) => ((MediaStrip)d).Rebuild()));

        public static readonly DependencyProperty ThumbnailMaxHeightProperty = DependencyProperty.Register(
            nameof(ThumbnailMaxHeight),
            typeof(double),
            typeof(MediaStrip),
            new PropertyMetadata(260.0, (d, _) => ((MediaStrip)d).Rebuild()));

        public static readonly DependencyProperty ThumbnailMaxWidthProperty = DependencyProperty.Register(
            nameof(ThumbnailMaxWidth),
            typeof(double),
            typeof(MediaStrip),
            new PropertyMetadata(320.0, (d, _) => ((MediaStrip)d).Rebuild()));

        public MediaStrip()
        {
            Orientation = Orientation.Horizontal;
            Spacing = 8;
            Visibility = Visibility.Collapsed;
        }

        /// <summary><see cref="IEnumerable{MediaItem}"/>。空なら非表示。</summary>
        public object? Items
        {
            get => GetValue(ItemsProperty);
            set => SetValue(ItemsProperty, value);
        }

        /// <summary>サムネイル 1 枚の最大の高さ。</summary>
        public double ThumbnailMaxHeight
        {
            get => (double)GetValue(ThumbnailMaxHeightProperty);
            set => SetValue(ThumbnailMaxHeightProperty, value);
        }

        /// <summary>サムネイル 1 枚の最大の幅。</summary>
        public double ThumbnailMaxWidth
        {
            get => (double)GetValue(ThumbnailMaxWidthProperty);
            set => SetValue(ThumbnailMaxWidthProperty, value);
        }

        private void Rebuild()
        {
            Children.Clear();

            var items = (Items as IEnumerable<MediaItem>)?.ToList() ?? [];
            if (items.Count == 0)
            {
                Visibility = Visibility.Collapsed;
                return;
            }

            Visibility = Visibility.Visible;
            foreach (var item in items)
            {
                Children.Add(CreateTile(item));
            }
        }

        private FrameworkElement CreateTile(MediaItem item)
        {
            // 以前の XAML と同じ: Uniform で全体を表示し、高さ・幅の上限だけ掛ける
            var image = new Image
            {
                Source = item.ThumbnailImage,
                Stretch = Stretch.Uniform,
                MaxHeight = ThumbnailMaxHeight,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var content = new Grid();
            content.Children.Add(image);

            if (item.IsVideo)
            {
                var overlay = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)),
                    CornerRadius = new CornerRadius(32),
                    Width = 64,
                    Height = 64,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                    Child = new FontIcon
                    {
                        Glyph = "",
                        FontSize = 28,
                        Foreground = new SolidColorBrush(Colors.White),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                content.Children.Add(overlay);

                var badge = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(6),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    IsHitTestVisible = false,
                    Child = new TextBlock
                    {
                        Text = item.IsGif ? "GIF" : "動画",
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Colors.White),
                    },
                };
                content.Children.Add(badge);
            }

            var border = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0x80, 0x80, 0x80)),
                Margin = new Thickness(0, 4, 0, 4),
                MaxWidth = ThumbnailMaxWidth,
                Child = content,
            };
            ToolTipService.SetToolTip(border, item.IsVideo ? (item.IsGif ? "GIF を再生" : "動画を再生") : "画像を拡大");
            border.Tapped += async (sender, e) =>
            {
                e.Handled = true;
                await MediaPreviewHelper.ShowAsync(this, item);
            };

            return border;
        }
    }
}
