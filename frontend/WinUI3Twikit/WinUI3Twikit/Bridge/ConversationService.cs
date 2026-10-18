using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Twikit;

namespace WinUI3Twikit.Bridge
{
    /// <summary>
    /// ツイートの会話（返信元の流れ・ツイート本体・返信）。X の Web 版がツイート詳細で使う TweetDetail を取得する。
    /// </summary>
    /// <remarks>
    /// twikit-dotnet の <see cref="Client.GetTweetByIdAsync"/> は使わず、応答を自前で読む。
    /// GetTweetByIdAsync は削除済みなどの返信元（TweetTombstone）を黙って捨て、返信の 2 ページ目以降では
    /// 会話のまとまりの先頭 1 件しか返さないため、流れが途中で切れて見える。
    /// </remarks>
    internal static class ConversationService
    {
        /// <summary>
        /// <paramref name="cursor"/> が無ければ会話全体（<c>ancestors</c> / <c>tweet</c> / <c>replies</c>）、
        /// あれば返信の続き（<c>replies</c> のみ）を返す。<c>next_cursor</c> は X のカーソルそのまま。
        /// </summary>
        public static async Task<JsonObject> GetConversationAsync(string tweetId, string? cursor)
        {
            try
            {
                TwikitSession.Login();
                var client = TwikitSession.Client;
                var continuing = !string.IsNullOrEmpty(cursor);
                Debug.WriteLine($"会話取得中... tweet_id={tweetId} cursor={(continuing ? "あり" : "なし")}");

                var response = await client.Gql.TweetDetailAsync(tweetId, continuing ? cursor : null).ConfigureAwait(false);
                if (JsonExtensions.FatalErrors(response.Json, "entries") is { } errors)
                {
                    Debug.WriteLine($"会話取得: {errors.ErrorMessage("unavailable")}");
                    return Error(UnavailableMessage);
                }

                var entries = response.Json.FirstDict("entries") as JsonArray ?? new JsonArray();
                return continuing ? MoreReplies(client, entries) : Conversation(client, tweetId, entries);
            }
            catch (TweetNotAvailableException ex)
            {
                Debug.WriteLine($"会話取得: ツイートを表示できません: {ex.Message}");
                return Error(UnavailableMessage);
            }
            catch (TooManyRequestsException ex)
            {
                var reset = ex.RateLimitReset is { } unix
                    ? $"（{DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime():HH:mm} に解除）"
                    : string.Empty;
                return Error($"X の取得回数の上限に達しました{reset}。しばらく待ってから開き直してください");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"会話取得エラー: {ex.Message}");
                return Error(ex.Message);
            }
        }

        private const string UnavailableMessage = "このツイートは表示できません（削除済み、または非公開アカウントの可能性があります）";

        /// <summary>
        /// 1 ページ目。ツイート本体（<c>tweet-{id}</c>）より前の <c>tweet-</c> が返信元の流れ（古い順）、
        /// 後ろの <c>conversationthread-</c> が返信。関連ツイートや広告など、それ以外の項目は使わない。
        /// </summary>
        private static JsonObject Conversation(Client client, string tweetId, JsonArray entries)
        {
            var focalEntryId = $"tweet-{tweetId}";
            var ancestors = new JsonArray();
            var threads = new JsonArray();
            JsonObject? focal = null;
            string? previousId = null;

            foreach (var entry in entries.Objects())
            {
                var entryId = entry.EntryId();
                if (focal is not null)
                {
                    AddThread(client, entry, threads);
                    continue;
                }

                if (!entryId.StartsWith("tweet-", StringComparison.Ordinal))
                {
                    continue;
                }

                var isFocal = string.Equals(entryId, focalEntryId, StringComparison.Ordinal);
                if (TombstoneText(entry) is { } tombstone)
                {
                    if (isFocal)
                    {
                        return Error(tombstone);
                    }

                    // 削除・凍結などで見られない返信元。X の説明文をそのまま出す。
                    previousId = entryId["tweet-".Length..];
                    ancestors.Add(new JsonObject
                    {
                        ["id"] = previousId,
                        ["is_unavailable"] = true,
                        ["unavailable_text"] = tombstone,
                    });
                    continue;
                }

                if (Tweet.FromData(client, entry) is not { } tweet)
                {
                    continue;
                }

                var link = ChainLink(tweet, previousId);
                previousId = tweet.Id;
                if (isFocal)
                {
                    focal = link;
                }
                else
                {
                    ancestors.Add(link);
                }
            }

            if (focal is null)
            {
                // X は存在しない・見られない ID に、ツイート本体の無いタイムラインを errors 無しで返す。
                return Error(UnavailableMessage);
            }

            Debug.WriteLine($"会話取得: 返信元 {ancestors.Count} 件 / 返信のまとまり {threads.Count} 件");
            return new JsonObject
            {
                ["ancestors"] = ancestors,
                ["tweet"] = focal,
                ["replies"] = threads,
                ["next_cursor"] = BottomCursor(entries),
            };
        }

        private static JsonObject MoreReplies(Client client, JsonArray entries)
        {
            var threads = new JsonArray();
            foreach (var entry in entries.Objects())
            {
                AddThread(client, entry, threads);
            }

            Debug.WriteLine($"会話: 返信の続き {threads.Count} 件");
            return new JsonObject
            {
                ["replies"] = threads,
                ["next_cursor"] = BottomCursor(entries),
            };
        }

        /// <summary>
        /// 返信元の流れの 1 件。返信先が直前の項目と一致しないもの（X が流れの途中から返したときなど）には
        /// <c>missing_parent_id</c> を付け、表示側でその位置に「取得できなかったツイート」を挟む。
        /// </summary>
        private static JsonObject ChainLink(Tweet tweet, string? previousId)
        {
            var dict = TweetSerializer.TweetToDict(tweet);
            var parentId = tweet.InReplyTo;
            if (!string.IsNullOrEmpty(parentId) && !string.Equals(parentId, previousId, StringComparison.Ordinal))
            {
                dict["missing_parent_id"] = parentId;
            }

            return dict;
        }

        /// <summary>
        /// 返信のまとまり（<c>conversationthread-</c>）を 1 つ足す。中身は上から順に返信の連鎖
        /// （先頭がこのツイートへの返信、続きはその下に付いた返信）。投稿欄・「さらに表示」・広告・見られない返信は除く。
        /// </summary>
        private static void AddThread(Client client, JsonObject entry, JsonArray threads)
        {
            if (!entry.EntryId().StartsWith("conversationthread-", StringComparison.Ordinal))
            {
                return;
            }

            var tweets = new JsonArray();
            foreach (var item in entry.Sub("content").ArrOrEmpty("items").Objects())
            {
                if (!item.EntryId().Contains("-tweet-", StringComparison.Ordinal)
                    || item.Sub("item").Sub("itemContent").ContainsKey("promotedMetadata"))
                {
                    continue;
                }

                if (Tweet.FromData(client, item) is { } tweet)
                {
                    tweets.Add(TweetSerializer.TweetToDict(tweet));
                }
            }

            if (tweets.Count > 0)
            {
                threads.Add(new JsonObject { ["tweets"] = tweets });
            }
        }

        /// <summary>
        /// 返信の続きのカーソル。通常の続き（<c>cursor-bottom-</c>）のほか、
        /// 「さらに返信を表示」で開く返信（<c>cursor-showmorethreads</c>）も続きとして読む。
        /// </summary>
        private static string? BottomCursor(JsonArray entries)
        {
            var cursor = entries.Objects().LastOrDefault(entry =>
            {
                var entryId = entry.EntryId();
                return entryId.StartsWith("cursor-bottom", StringComparison.Ordinal)
                    || entryId.StartsWith("cursor-showmore", StringComparison.Ordinal);
            });
            var content = cursor.Sub("content");
            return content.Str("value") ?? content.Sub("itemContent").Str("value");
        }

        /// <summary>
        /// 削除・凍結などで見られないツイート（TweetTombstone）なら、X の説明文（末尾の「詳細はこちら」のリンクは除く）。
        /// </summary>
        private static string? TombstoneText(JsonObject entry)
        {
            var found = entry.FindDict("result", findOne: true);
            if (found.Count == 0 || found[0] is not JsonObject result || result.Str("__typename") != "TweetTombstone")
            {
                return null;
            }

            var text = result.Sub("tombstone").Sub("text");
            var message = text.Str("text") ?? string.Empty;
            var linkStart = text.ArrOrEmpty("entities").Objects()
                .Select(entity => entity.Int("fromIndex") ?? message.Length)
                .DefaultIfEmpty(message.Length)
                .Min();
            message = message[..Math.Clamp(linkStart, 0, message.Length)].Trim();
            return message.Length > 0 ? message : "このツイートは表示できません";
        }

        private static JsonObject Error(string message)
            => new() { ["error"] = message, ["replies"] = new JsonArray(), ["next_cursor"] = null };
    }
}
