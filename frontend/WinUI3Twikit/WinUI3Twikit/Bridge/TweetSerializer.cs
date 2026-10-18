using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Twikit;

namespace WinUI3Twikit.Bridge
{
    /// <summary>
    /// ツイートを WinUI 側（<c>TweetDto</c>）が期待する JSON に変換する（旧 <c>backend/tweet_serializer.py</c> と同じ形）。
    /// </summary>
    internal static class TweetSerializer
    {
        /// <summary>日時は FastAPI 版と同じく日本時間で整形する。</summary>
        public static readonly TimeSpan Jst = TimeSpan.FromHours(9);

        public static string NormalizeText(string? text)
            => string.IsNullOrEmpty(text) ? string.Empty : WebUtility.HtmlDecode(text);

        public static int MetricCount(long? value)
            => value is > 0 ? (int)Math.Min(value.Value, int.MaxValue) : 0;

        public static string FormatCreatedAt(Tweet tweet)
        {
            try
            {
                return tweet.CreatedAtDatetime.ToOffset(Jst).ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return tweet.CreatedAt ?? "不明";
            }
        }

        private static string? NonEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

        private static void AppendMediaEntry(JsonArray mediaItems, HashSet<string> seenUrls, JsonObject m)
        {
            var mediaType = m.Str("type") ?? "photo";
            if (mediaType == "photo")
            {
                var url = NonEmpty(m.Str("media_url_https")) ?? m.Str("media_url");
                if (!string.IsNullOrEmpty(url) && seenUrls.Add(url))
                {
                    mediaItems.Add(new JsonObject { ["type"] = "image", ["url"] = url });
                }
            }
            else if (mediaType is "video" or "animated_gif")
            {
                var variants = m.Sub("video_info").ArrOrEmpty("variants");
                string? videoUrl = null;
                if (variants.Count > 0)
                {
                    // video/mp4 のうち最高ビットレートのもの。無ければ先頭の variant。
                    JsonObject? best = null;
                    long bestBitrate = -1;
                    foreach (var variant in variants.Objects())
                    {
                        if (variant.Str("content_type") != "video/mp4")
                        {
                            continue;
                        }

                        var bitrate = variant.Long("bitrate") ?? 0;
                        if (best is null || bitrate > bestBitrate)
                        {
                            best = variant;
                            bestBitrate = bitrate;
                        }
                    }

                    best ??= variants[0] as JsonObject;
                    videoUrl = best?.Str("url");
                }

                var thumb = NonEmpty(m.Str("media_url_https")) ?? m.Str("media_url");
                if (string.IsNullOrEmpty(videoUrl))
                {
                    videoUrl = thumb;
                }

                if (!string.IsNullOrEmpty(videoUrl) && seenUrls.Add(videoUrl))
                {
                    var item = new JsonObject
                    {
                        ["type"] = mediaType == "animated_gif" ? "animated_gif" : "video",
                        ["url"] = videoUrl,
                    };
                    if (!string.IsNullOrEmpty(thumb))
                    {
                        item["thumbnail"] = thumb;
                    }

                    mediaItems.Add(item);
                }
            }
        }

        public static JsonArray ExtractMedia(Tweet tweet)
        {
            return ExtractMedia(tweet.Legacy);
        }

        internal static JsonArray ExtractMedia(JsonObject tweet)
        {
            var mediaItems = new JsonArray();
            var seenUrls = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (var m in tweet.Sub("extended_entities").ArrOrEmpty("media").Objects())
                {
                    AppendMediaEntry(mediaItems, seenUrls, m);
                }

                foreach (var m in tweet.Sub("entities").ArrOrEmpty("media").Objects())
                {
                    AppendMediaEntry(mediaItems, seenUrls, m);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"メディア抽出エラー: {ex.Message}");
            }

            return mediaItems;
        }

        private static bool IsVerified(User? user) => user is not null && (user.IsBlueVerified || user.Verified);

        private static string DisplayText(Tweet tweet)
        {
            var text = tweet.FullText;
            if (string.IsNullOrEmpty(text))
            {
                text = tweet.Text;
            }

            // X は添付メディア用の t.co を本文末尾へ足す。サムネイル側で見せるので本文からは除く。
            return StripTrailingMediaUrls(text, tweet.Legacy);
        }

        /// <summary>
        /// 本文末尾に並ぶ t.co のうち、メディアエンティティの短縮 URL だけを除く。
        /// 引用や本文中のリンクは残す。メディア用 URL が無ければ本文は変えない。
        /// </summary>
        internal static string StripTrailingMediaUrls(string? text, JsonObject tweet)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }

            var mediaUrls = new HashSet<string>(StringComparer.Ordinal);
            foreach (var shortUrl in MediaShortUrls(tweet))
            {
                if (!string.IsNullOrEmpty(shortUrl))
                {
                    mediaUrls.Add(shortUrl);
                }
            }

            if (mediaUrls.Count == 0)
            {
                return text;
            }

            var end = text.TrimEnd();
            var kept = new List<string>();
            var removed = false;
            var index = end.Length;
            while (index > 0)
            {
                var tokenEnd = index;
                while (index > 0 && !char.IsWhiteSpace(end[index - 1]))
                {
                    index--;
                }

                if (index == tokenEnd)
                {
                    break;
                }

                var token = end[index..tokenEnd];
                if (mediaUrls.Contains(token))
                {
                    removed = true;
                }
                else if (IsTcoUrl(token))
                {
                    // 引用用など、メディアではない末尾の t.co は残して、その手前も見る。
                    kept.Add(token);
                }
                else
                {
                    index = tokenEnd;
                    break;
                }

                while (index > 0 && char.IsWhiteSpace(end[index - 1]))
                {
                    index--;
                }
            }

            if (!removed)
            {
                return text;
            }

            var head = end[..index].TrimEnd();
            if (kept.Count == 0)
            {
                return head;
            }

            kept.Reverse();
            return head.Length == 0 ? string.Join(" ", kept) : head + " " + string.Join(" ", kept);
        }

        private static IEnumerable<string?> MediaShortUrls(JsonObject tweet)
        {
            // entities.media は先頭の 1 件だけなので、extended_entities を優先する。
            var extended = tweet.Sub("extended_entities").ArrOrEmpty("media");
            var media = extended.Count > 0 ? extended : tweet.Sub("entities").ArrOrEmpty("media");
            foreach (var item in media.Objects())
            {
                yield return item.Str("url");
            }
        }

        private static bool IsTcoUrl(string token)
            => token.StartsWith("https://t.co/", StringComparison.OrdinalIgnoreCase)
               || token.StartsWith("http://t.co/", StringComparison.OrdinalIgnoreCase);

        /// <summary>本文中の t.co と展開後 URL の対応（リンク化に使う）。</summary>
        public static JsonArray UrlEntities(Tweet tweet)
        {
            var urls = new JsonArray();
            try
            {
                foreach (var entity in tweet.Urls)
                {
                    AppendUrlEntity(urls, entity.Url, entity.ExpandedUrl, entity.DisplayUrl);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"URL 抽出エラー: {ex.Message}");
            }

            return urls;
        }

        /// <summary>REST 形式のツイート（通知 API など）の <c>entities.urls</c> から同じ形を作る。</summary>
        public static JsonArray UrlEntities(JsonObject tweet)
        {
            var urls = new JsonArray();
            try
            {
                foreach (var entity in tweet.Sub("entities").ArrOrEmpty("urls").Objects())
                {
                    AppendUrlEntity(urls, entity.Str("url"), entity.Str("expanded_url"), entity.Str("display_url"));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"URL 抽出エラー: {ex.Message}");
            }

            return urls;
        }

        private static void AppendUrlEntity(JsonArray urls, string? shortUrl, string? expandedUrl, string? displayUrl)
        {
            if (string.IsNullOrEmpty(shortUrl))
            {
                return;
            }

            urls.Add(new JsonObject
            {
                ["url"] = shortUrl,
                ["expanded_url"] = expandedUrl ?? shortUrl,
                ["display_url"] = displayUrl ?? string.Empty,
            });
        }

        /// <summary>引用ツイートの URL（本文末尾の t.co）は引用カードで表示するため本文から外す。</summary>
        private static string StripQuotedUrl(string text, Tweet tweet)
        {
            var quotedId = tweet.QuotedStatusId;
            if (string.IsNullOrEmpty(quotedId) || string.IsNullOrEmpty(text))
            {
                return text;
            }

            try
            {
                foreach (var entity in tweet.Urls)
                {
                    var expanded = entity.ExpandedUrl ?? string.Empty;
                    if (expanded.Contains($"/status/{quotedId}", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrEmpty(entity.Url))
                    {
                        text = text.Replace(entity.Url, string.Empty);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"引用 URL 除去エラー: {ex.Message}");
            }

            return text.Trim();
        }

        /// <summary>返信先のスクリーンネーム（@ 無し）。返信でなければ null。</summary>
        private static string? ReplyToScreenName(Tweet tweet)
        {
            var name = (tweet.Legacy.Str("in_reply_to_screen_name") ?? string.Empty).Trim().TrimStart('@');
            return name.Length == 0 ? null : name;
        }

        /// <summary>本文先頭の「@名前」の並び（空白区切り）だけでできているか。</summary>
        private static readonly Regex LeadingMentions = new(@"^(?:@\w+\s*)+$");

        private static readonly Regex Mention = new(@"@(\w+)");

        /// <summary>
        /// 返信で X が本文の先頭に自動で付ける「@名前」（<c>display_text_range</c> より前の部分）。
        /// X の Web 版と同じく本文からは外し、返信先として表示する。返信でなければ空。
        /// </summary>
        /// <param name="tweet">ツイートの <c>legacy</c>、または REST 形式のツイート（通知 API など）。</param>
        internal static string HiddenReplyPrefix(JsonObject tweet)
        {
            var fullText = tweet.Str("full_text");
            var start = tweet.Arr("display_text_range").At(0).AsInt() ?? 0;
            if (string.IsNullOrEmpty(fullText) || start <= 0)
            {
                return string.Empty;
            }

            // display_text_range はコードポイント単位。
            var length = 0;
            for (var codePoints = 0; codePoints < start && length < fullText.Length; codePoints++)
            {
                length += char.IsSurrogatePair(fullText, length) ? 2 : 1;
            }

            var prefix = fullText[..length];
            return LeadingMentions.IsMatch(prefix) ? prefix : string.Empty;
        }

        /// <summary>本文の先頭から <see cref="HiddenReplyPrefix"/> を外す。先頭がそれと違えば本文はそのまま。</summary>
        internal static string StripHiddenReplyPrefix(string text, string hiddenPrefix)
        {
            // 本文がメンションと画像だけだと、画像の URL を外したときに末尾の空白も消えているので空白抜きで比べる。
            var mentions = hiddenPrefix.TrimEnd();
            if (mentions.Length == 0
                || !text.StartsWith(mentions, StringComparison.Ordinal)
                || (text.Length > mentions.Length && !char.IsWhiteSpace(text[mentions.Length])))
            {
                return text;
            }

            return text[mentions.Length..].TrimStart();
        }

        /// <summary>
        /// 返信先の表示用（@ 無し・空白区切り）。直接の返信先 <paramref name="replyTo"/> を先頭に、
        /// 本文から外した「@名前」を続ける。返信でなければ null。
        /// </summary>
        internal static string? ReplyToScreenNames(string? replyTo, string hiddenPrefix)
        {
            var names = new List<string>();
            if (!string.IsNullOrEmpty(replyTo))
            {
                names.Add(replyTo);
            }

            foreach (Match match in Mention.Matches(hiddenPrefix))
            {
                var name = match.Groups[1].Value;
                if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(name);
                }
            }

            return names.Count == 0 ? null : string.Join(" ", names);
        }

        public static JsonObject QuoteToDict(Tweet quoted)
        {
            var user = quoted.User;
            return new JsonObject
            {
                ["id"] = quoted.Id,
                ["text"] = NormalizeText(DisplayText(quoted)),
                ["urls"] = UrlEntities(quoted),
                ["created_at"] = FormatCreatedAt(quoted),
                ["user_name"] = user?.Name ?? "Unknown",
                ["user_screen_name"] = user?.ScreenName ?? string.Empty,
                ["user_profile_image"] = user?.ProfileImageUrl ?? string.Empty,
                ["user_protected"] = user?.Protected ?? false,
                ["user_verified"] = IsVerified(user),
                ["media_items"] = ExtractMedia(quoted),
                ["is_unavailable"] = false,
            };
        }

        public static JsonObject TweetToDict(Tweet tweet)
        {
            var retweeted = tweet.RetweetedTweet;
            var display = retweeted ?? tweet;
            var user = display.User;

            var hiddenReplyPrefix = HiddenReplyPrefix(display.Legacy);
            var text = NormalizeText(StripHiddenReplyPrefix(DisplayText(display), hiddenReplyPrefix));
            if (display.IsQuoteStatus)
            {
                text = StripQuotedUrl(text, display);
            }

            var result = new JsonObject
            {
                ["id"] = display.Id,
                ["timeline_entry_id"] = tweet.Id,
                ["text"] = text,
                ["urls"] = UrlEntities(display),
                ["reply_to_screen_name"] = ReplyToScreenNames(ReplyToScreenName(display), hiddenReplyPrefix),
                ["created_at"] = FormatCreatedAt(display),
                ["user_name"] = user?.Name ?? "Unknown",
                ["user_screen_name"] = user?.ScreenName ?? string.Empty,
                ["user_profile_image"] = user?.ProfileImageUrl ?? string.Empty,
                ["user_protected"] = user?.Protected ?? false,
                ["user_verified"] = IsVerified(user),
                ["favorite_count"] = display.FavoriteCount,
                ["retweet_count"] = display.RetweetCount,
                ["reply_count"] = display.ReplyCount,
                ["view_count"] = MetricCount(display.ViewCount),
                ["media_items"] = ExtractMedia(display),
                ["is_liked"] = display.Favorited,
                ["is_retweeted"] = display.Legacy.BoolOr("retweeted", false),
                ["is_retweet"] = retweeted is not null,
                ["retweeted_by_name"] = retweeted is not null ? tweet.User?.Name : null,
                ["retweeted_by_screen_name"] = retweeted is not null ? tweet.User?.ScreenName : null,
            };

            if (display.IsQuoteStatus)
            {
                var quoted = display.Quote;
                result["quoted_tweet"] = quoted is not null
                    ? QuoteToDict(quoted)
                    : new JsonObject { ["is_unavailable"] = true };
            }

            return result;
        }
    }
}
