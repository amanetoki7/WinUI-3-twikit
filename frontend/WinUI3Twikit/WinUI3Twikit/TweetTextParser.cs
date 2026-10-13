using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace WinUI3Twikit
{
    public enum TextSegmentKind
    {
        Text,
        Url,
        Hashtag,
        Mention,
    }

    /// <summary>本文の一部。<see cref="Target"/> は URL なら開く先、ハッシュタグ / メンションなら記号を除いた名前。</summary>
    public sealed record TextSegment(TextSegmentKind Kind, string Text, string Target);

    /// <summary>
    /// 本文から URL / ハッシュタグ / メンションを切り出す（WinUI-3-twikit-deck より移植）。
    /// t.co の短縮 URL はエンティティの展開後 URL に置き換えて表示する。
    /// </summary>
    public static partial class TweetTextParser
    {
        [GeneratedRegex(
            @"(?<url>https?://[^\s<>""']+)|(?<![\w@])@(?<mention>[A-Za-z0-9_]{1,15})(?![\w])|(?<![\w#＃])[#＃](?<hashtag>[\p{L}\p{N}_]+)",
            RegexOptions.CultureInvariant)]
        private static partial Regex TokenRegex();

        public static IReadOnlyList<TextSegment> Parse(string? text, IReadOnlyList<TweetUrlEntity>? urls = null)
        {
            var segments = new List<TextSegment>();
            if (string.IsNullOrEmpty(text))
            {
                return segments;
            }

            Dictionary<string, TweetUrlEntity>? byShortUrl = null;
            if (urls is { Count: > 0 })
            {
                byShortUrl = new Dictionary<string, TweetUrlEntity>(StringComparer.OrdinalIgnoreCase);
                foreach (var url in urls)
                {
                    if (!string.IsNullOrEmpty(url.ShortUrl))
                    {
                        byShortUrl[url.ShortUrl] = url;
                    }
                }
            }

            var last = 0;
            foreach (Match match in TokenRegex().Matches(text))
            {
                if (match.Index > last)
                {
                    segments.Add(new TextSegment(TextSegmentKind.Text, text[last..match.Index], string.Empty));
                }

                if (match.Groups["url"].Success)
                {
                    var raw = TrimTrailingPunctuation(match.Value);
                    var trailing = match.Value[raw.Length..];
                    var display = raw;
                    var target = raw;
                    if (byShortUrl is not null && byShortUrl.TryGetValue(raw, out var entity))
                    {
                        display = string.IsNullOrEmpty(entity.DisplayUrl) ? entity.ExpandedUrl : entity.DisplayUrl;
                        target = string.IsNullOrEmpty(entity.ExpandedUrl) ? raw : entity.ExpandedUrl;
                    }

                    segments.Add(new TextSegment(TextSegmentKind.Url, display, target));
                    if (trailing.Length > 0)
                    {
                        segments.Add(new TextSegment(TextSegmentKind.Text, trailing, string.Empty));
                    }
                }
                else if (match.Groups["mention"].Success)
                {
                    segments.Add(new TextSegment(TextSegmentKind.Mention, match.Value, match.Groups["mention"].Value));
                }
                else if (match.Groups["hashtag"].Success)
                {
                    segments.Add(new TextSegment(TextSegmentKind.Hashtag, match.Value, match.Groups["hashtag"].Value));
                }

                last = match.Index + match.Length;
            }

            if (last < text.Length)
            {
                segments.Add(new TextSegment(TextSegmentKind.Text, text[last..], string.Empty));
            }

            return segments;
        }

        private static string TrimTrailingPunctuation(string url)
        {
            var end = url.Length;
            while (end > 0 && url[end - 1] is '.' or ',' or ')' or '。' or '、' or '」' or '）' or '!' or '?')
            {
                end--;
            }

            return url[..end];
        }
    }
}
