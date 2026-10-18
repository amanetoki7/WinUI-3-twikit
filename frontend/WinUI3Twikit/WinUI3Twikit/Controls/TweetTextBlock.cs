using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.System;

namespace WinUI3Twikit.Controls
{
    /// <summary>
    /// 本文の URL / ハッシュタグ / メンションをリンクにして表示する（WinUI-3-twikit-deck より移植）。
    /// URL は既定のブラウザで開き、ハッシュタグは検索ページ、メンションはプロフィール検索へ遷移する。
    /// </summary>
    public sealed class TweetTextBlock : UserControl
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(TweetTextBlock),
            new PropertyMetadata(string.Empty, (d, _) => ((TweetTextBlock)d).Rebuild()));

        public static readonly DependencyProperty UrlsProperty = DependencyProperty.Register(
            nameof(Urls),
            typeof(object),
            typeof(TweetTextBlock),
            new PropertyMetadata(null, (d, _) => ((TweetTextBlock)d).Rebuild()));

        public static readonly DependencyProperty TextFontSizeProperty = DependencyProperty.Register(
            nameof(TextFontSize),
            typeof(double),
            typeof(TweetTextBlock),
            new PropertyMetadata(15.0, (d, e) => ((TweetTextBlock)d)._textBlock.FontSize = (double)e.NewValue));

        private readonly TextBlock _textBlock;

        public TweetTextBlock()
        {
            _textBlock = new TextBlock
            {
                TextWrapping = TextWrapping.WrapWholeWords,
                IsTextSelectionEnabled = true,
                FontSize = 15,
                LineHeight = 20,
            };
            Content = _textBlock;
            IsTabStop = false;
        }

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        /// <summary><see cref="IReadOnlyList{TweetUrlEntity}"/>。t.co を展開後 URL で表示するために使う。</summary>
        public object? Urls
        {
            get => GetValue(UrlsProperty);
            set => SetValue(UrlsProperty, value);
        }

        public double TextFontSize
        {
            get => (double)GetValue(TextFontSizeProperty);
            set => SetValue(TextFontSizeProperty, value);
        }

        /// <summary>
        /// <paramref name="point"/>（この部品の座標）がリンクの文字の上か。ツイートカードはクリックで会話を開くが、
        /// リンクの上ではリンクを優先する。Hyperlink は UIElement ではないので、タップの発生元からは見分けられない。
        /// </summary>
        public bool IsLinkAt(Point point)
        {
            foreach (var link in _textBlock.Inlines.OfType<Hyperlink>())
            {
                // 文字ごとの位置を行ごとにまとめ、行ごとの範囲で判定する（リンクが折り返していてもよい）。
                // リンク末尾の位置（直後の文字の左端）まで含めると、最後の文字の右端までが範囲になる。
                var end = link.ContentEnd.Offset;
                Rect? line = null;
                for (var position = link.ContentStart;
                     position is not null && position.Offset <= end;
                     position = position.GetPositionAtOffset(1, LogicalDirection.Forward))
                {
                    var rect = position.GetCharacterRect(LogicalDirection.Forward);
                    if (line is { } current && Math.Abs(current.Y - rect.Y) < 1)
                    {
                        current.Union(rect);
                        line = current;
                        continue;
                    }

                    if (line is { } finished && LineContains(finished, point))
                    {
                        return true;
                    }

                    line = rect;
                }

                if (line is { } last && LineContains(last, point))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool LineContains(Rect line, Point point)
            => point.X >= line.Left && point.X <= line.Right && point.Y >= line.Top && point.Y <= line.Bottom;

        private void Rebuild()
        {
            _textBlock.Inlines.Clear();
            var text = Text;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            foreach (var segment in TweetTextParser.Parse(text, Urls as IReadOnlyList<TweetUrlEntity>))
            {
                if (segment.Kind == TextSegmentKind.Text)
                {
                    _textBlock.Inlines.Add(new Run { Text = segment.Text });
                    continue;
                }

                var link = new Hyperlink
                {
                    UnderlineStyle = UnderlineStyle.None,
                };
                link.Inlines.Add(new Run { Text = segment.Text });
                var captured = segment;
                link.Click += async (_, _) => await ActivateAsync(captured);
                _textBlock.Inlines.Add(link);
            }
        }

        private static async Task ActivateAsync(TextSegment segment)
        {
            try
            {
                switch (segment.Kind)
                {
                    case TextSegmentKind.Url:
                        if (Uri.TryCreate(segment.Target, UriKind.Absolute, out var uri))
                        {
                            await Launcher.LaunchUriAsync(uri);
                        }

                        break;
                    case TextSegmentKind.Hashtag:
                        App.MainWindow?.NavigateToSearch("#" + segment.Target);
                        break;
                    case TextSegmentKind.Mention:
                        App.MainWindow?.NavigateToProfileSearch(segment.Target);
                        break;
                }
            }
            catch (Exception ex)
            {
                AppNotifications.Error($"リンクを開けませんでした: {ex.Message}");
            }
        }
    }
}
