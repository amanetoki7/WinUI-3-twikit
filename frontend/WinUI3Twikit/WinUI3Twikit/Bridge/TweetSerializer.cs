using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
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
            var mediaItems = new JsonArray();
            var seenUrls = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                var legacy = tweet.Legacy;
                foreach (var m in legacy.Sub("extended_entities").ArrOrEmpty("media").Objects())
                {
                    AppendMediaEntry(mediaItems, seenUrls, m);
                }

                foreach (var m in legacy.Sub("entities").ArrOrEmpty("media").Objects())
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
            return string.IsNullOrEmpty(text) ? tweet.Text : text;
        }

        public static JsonObject QuoteToDict(Tweet quoted)
        {
            var user = quoted.User;
            return new JsonObject
            {
                ["id"] = quoted.Id,
                ["text"] = NormalizeText(DisplayText(quoted)),
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

            var result = new JsonObject
            {
                ["id"] = display.Id,
                ["timeline_entry_id"] = tweet.Id,
                ["text"] = NormalizeText(DisplayText(display)),
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
