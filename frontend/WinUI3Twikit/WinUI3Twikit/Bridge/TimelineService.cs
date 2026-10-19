using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Twikit;

namespace WinUI3Twikit.Bridge
{
    /// <summary>
    /// タイムライン・リスト・ユーザーツイートの取得
    /// （旧 <c>get_timeline_twikit.py</c> / <c>get_lists_twikit.py</c> / <c>get_user_profile_twikit.py</c> / <c>get_my_profile.py</c>）。
    /// </summary>
    internal static class TimelineService
    {
        private static readonly PageCursorStore<Tweet> TweetPages = new(capacity: 512);
        /// <summary>スクリーンネーム → ユーザー ID と固定ツイート ID。固定は変わるので一定時間で取り直す。</summary>
        private sealed record ResolvedUser(string Id, HashSet<string> PinnedTweetIds, DateTime ResolvedAtUtc);

        private static readonly TimeSpan ResolvedUserTtl = TimeSpan.FromMinutes(10);

        private static readonly Dictionary<string, ResolvedUser> UsersByScreenName = new(StringComparer.OrdinalIgnoreCase);

        private static string Describe(string? cursor) => string.IsNullOrEmpty(cursor) ? "なし" : "あり";

        private static JsonObject TweetsPayload(JsonArray tweets, string? nextCursor)
            => new() { ["tweets"] = tweets, ["next_cursor"] = nextCursor };

        private static JsonObject ErrorTweets(string message)
            => new() { ["error"] = message, ["tweets"] = new JsonArray(), ["next_cursor"] = null };

        /// <summary>1 ページ分を、同じ ID の重複を除いて JSON にする。</summary>
        private static JsonArray SerializeUnique(IEnumerable<Tweet> page)
        {
            var results = new JsonArray();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tweet in page)
            {
                if (!seen.Add(tweet.Id))
                {
                    continue;
                }

                results.Add(TweetSerializer.TweetToDict(tweet));
            }

            return results;
        }

        // --- ホームタイムライン ---
        public static async Task<JsonObject> GetTimelineTweetsAsync(int pages, int? countPerPage, string timelineType, string? cursor)
        {
            var results = new JsonArray();
            var currentCursor = string.IsNullOrEmpty(cursor) ? null : cursor;
            var count = countPerPage ?? 20;

            try
            {
                TwikitSession.Login();
                var client = TwikitSession.Client;
                var seen = new HashSet<string>(StringComparer.Ordinal);

                for (var i = 0; i < Math.Max(1, pages); i++)
                {
                    Debug.WriteLine($"{i + 1}ページ目取得中... (type: {timelineType}, cursor: {Describe(currentCursor)})");

                    var (page, nextCursor) = await TweetPages.FetchAsync(
                        currentCursor,
                        count,
                        c => timelineType == "latest"
                            ? client.GetLatestTimelineAsync(count, null, c)   // 最新タイムライン
                            : client.GetTimelineAsync(count, null, c)).ConfigureAwait(false);   // おすすめ（For You）

                    if (page.Count == 0)
                    {
                        // FastAPI 版と同じく、空ページでは next_cursor を進めない（同じ位置を再取得できる）。
                        // 続きが無い（期限切れトークンを含む）ときだけ終端として null にする。
                        Debug.WriteLine("これ以上取得できません");
                        if (nextCursor is null)
                        {
                            currentCursor = null;
                        }

                        break;
                    }

                    Debug.WriteLine($"  └─ 取得したツイート数: {page.Count}");
                    foreach (var tweet in page)
                    {
                        if (!seen.Add(tweet.Id))
                        {
                            continue;
                        }

                        results.Add(TweetSerializer.TweetToDict(tweet));
                    }

                    currentCursor = nextCursor;
                    if (nextCursor is null)
                    {
                        Debug.WriteLine("next_cursor がありません");
                        break;
                    }

                    Debug.WriteLine($"next_cursor 更新: {nextCursor}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"エラー: {ex.Message}");
            }

            Debug.WriteLine($"今回合計 {results.Count} 件取得");
            return TweetsPayload(results, currentCursor);
        }

        // --- リスト一覧 ---
        /// <summary>
        /// ログイン中ユーザーのリスト（作成したもの + フォロー中のもの）。
        /// twikit の <c>GetListsAsync</c> と同じ ListManagementPageTimeline を読むが、所有者（<c>user_results</c>）は
        /// <see cref="TwitterList"/> に残らないので応答から直接拾い、<c>owner_screen_name</c> / <c>is_owned</c> を付ける
        /// （ホームのタブ追加で「作成したリスト」を先に並べるため）。
        /// </summary>
        public static async Task<JsonObject> GetUserListsAsync(int count, string? cursor)
        {
            try
            {
                TwikitSession.Login();
                var client = TwikitSession.Client;
                Debug.WriteLine($"リスト一覧取得中... count={count} cursor={Describe(cursor)}");

                var response = await client.Gql.ListManagementPaceTimelineAsync(count, cursor).ConfigureAwait(false);

                // X は timeline の代わりに viewer の殻とエラー（code 214 など）を返すことがある
                var errors = JsonExtensions.FatalErrors(response.Json, "entries");
                if (errors is not null)
                {
                    throw new BridgeException(errors.ErrorMessage("リスト一覧を取得できませんでした"));
                }

                var me = await TryGetAuthenticatedScreenNameAsync().ConfigureAwait(false);
                var lists = new JsonArray();
                string? nextCursor = null;

                if (response.Json.FirstDict("entries") is JsonArray entries && entries.Count > 0)
                {
                    // カーソルは先に読む。リストが 1 件も無いページでも終端とは限らない
                    nextCursor = entries[^1].Sub("content").Str("value");

                    // items[0] はピン留め、items[1] が作成した + フォロー中のリスト（twikit の GetListsAsync と同じ）
                    var items = entries.FindDict("items");
                    var cells = items.Count >= 2 ? items[1] as JsonArray : null;
                    foreach (var cell in cells.Objects())
                    {
                        var listData = cell.Sub("item").Sub("itemContent").Obj("list");
                        if (listData is null)
                        {
                            continue;
                        }

                        TwitterList list;
                        try
                        {
                            list = new TwitterList(client, listData);
                        }
                        catch (NotFoundException)
                        {
                            // X が解決できなかったリスト（ID だけの殻）
                            continue;
                        }

                        var owner = listData.Sub("user_results").Sub("result");
                        var ownerScreenName = owner.Sub("core").Str("screen_name") ?? owner.Sub("legacy").Str("screen_name");

                        // 所有者が分からないときは、フォロー中でなければ自分のもの（作成したリストはフォローできない）
                        var isOwned = ownerScreenName is not null && me is not null
                            ? string.Equals(ownerScreenName, me, StringComparison.OrdinalIgnoreCase)
                            : !list.Following;

                        lists.Add(new JsonObject
                        {
                            ["id"] = list.Id,
                            ["name"] = list.Name ?? string.Empty,
                            ["description"] = list.Description ?? string.Empty,
                            ["mode"] = list.Mode,
                            ["member_count"] = list.MemberCount,
                            ["subscriber_count"] = list.SubscriberCount,
                            ["owner_screen_name"] = ownerScreenName,
                            ["is_owned"] = isOwned,
                        });
                    }
                }

                Debug.WriteLine($"リスト {lists.Count} 件取得");
                return new JsonObject { ["lists"] = lists, ["next_cursor"] = nextCursor };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"リスト一覧取得エラー: {ex.Message}");
                return new JsonObject { ["error"] = ex.Message, ["lists"] = new JsonArray(), ["next_cursor"] = null };
            }
        }

        /// <summary>ログイン中ユーザーのスクリーンネーム。取れないときは null（リストの所有判定は Following に任せる）。</summary>
        private static async Task<string?> TryGetAuthenticatedScreenNameAsync()
        {
            try
            {
                return await TwikitSession.GetAuthenticatedScreenNameAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ログイン中ユーザー名の取得に失敗: {ex.Message}");
                return null;
            }
        }

        // --- リストのタイムライン ---
        public static async Task<JsonObject> GetListTimelineAsync(string listId, int count, string? cursor)
        {
            var results = new JsonArray();
            var nextCursor = string.IsNullOrEmpty(cursor) ? null : cursor;

            try
            {
                TwikitSession.Login();
                var client = TwikitSession.Client;
                Debug.WriteLine($"リストタイムライン取得中... list_id={listId} count={count} cursor={Describe(cursor)}");

                var (page, token) = await TweetPages.FetchAsync(cursor, count, c => client.GetListTweetsAsync(listId, count, c)).ConfigureAwait(false);
                if (page.Count == 0)
                {
                    Debug.WriteLine("タイムラインが空です");
                    return TweetsPayload([], null);
                }

                results = SerializeUnique(page);
                nextCursor = token;
                Debug.WriteLine(token is null ? "next_cursor がありません" : $"next_cursor 更新: {token}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"リストタイムライン取得エラー: {ex.Message}");
            }

            Debug.WriteLine($"リストタイムライン {results.Count} 件取得");
            return TweetsPayload(results, nextCursor);
        }

        // --- 任意ユーザーのツイート ---
        /// <param name="tab">
        /// ユーザーカラムのタブ。<c>tweets</c>（RT を除いたオリジナルのみ）/ <c>replies</c>（返信のみ）/
        /// <c>retweets</c>（RT のみ）/ <c>media</c>（写真または動画）。省略時は従来どおり X の Tweets タブをそのまま返す。
        /// </param>
        /// <param name="mediaKind"><c>media</c> タブの種類。<c>photo</c>（既定）または <c>video</c>（GIF を含む）。</param>
        public static async Task<JsonObject> GetUserTweetsAsync(string screenName, int count, string? cursor, string? tab = null, string? mediaKind = null)
        {
            var name = ProfileService.NormalizeScreenName(screenName);
            if (name.Length == 0)
            {
                return ErrorTweets("ユーザー名が空です");
            }

            try
            {
                TwikitSession.Login();
                var client = TwikitSession.Client;
                var feed = ResolveUserFeed(tab, mediaKind);
                Debug.WriteLine($"ユーザーツイート取得中... screen_name={name} tab={tab ?? "(既定)"} type={feed.TweetType} count={count} cursor={Describe(cursor)}");

                // 1 ページ目（最新取得）のときは固定ツイートの変更を拾えるよう、期限切れなら取り直す
                var user = await ResolveUserAsync(client, name, allowRefresh: string.IsNullOrEmpty(cursor)).ConfigureAwait(false);
                return await UserTimelineAsync(client, user.Id, user.PinnedTweetIds, count, cursor, "ユーザーツイート", feed).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ユーザーツイート取得エラー: {ex.Message}");
                return ErrorTweets(ex.Message);
            }
        }

        /// <summary>twikit に渡す tweetType と、取得後に適用する絞り込み（null なら絞り込まない）。</summary>
        private sealed record UserFeed(string TweetType, Func<Tweet, bool>? Filter);

        private static readonly UserFeed DefaultUserFeed = new("Tweets", null);

        /// <summary>
        /// 絞り込みで 1 ページが空になったとき、続きを読む最大ページ数。
        /// RT をほとんどしないユーザーの「リポスト」タブなどで、空のページだけを返して終端扱いになるのを防ぐ。
        /// </summary>
        private const int MaxFilteredPages = 5;

        private static UserFeed ResolveUserFeed(string? tab, string? mediaKind)
        {
            switch ((tab ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "tweets":
                    // X の Tweets タブは RT を含むので、オリジナル（引用を含む）だけに絞る。
                    return new("Tweets", t => t.RetweetedTweet is null);
                case "replies":
                    // Replies タブ（TweetsAndReplies）には本人のオリジナルや RT も混ざるので、返信だけに絞る。
                    return new("Replies", t => t.RetweetedTweet is null && t.InReplyTo is not null);
                case "retweets":
                    return new("Tweets", t => t.RetweetedTweet is not null);
                case "media":
                    var video = string.Equals(mediaKind, "video", StringComparison.OrdinalIgnoreCase);
                    return new("Media", t => HasMediaOfKind(t.RetweetedTweet ?? t, video));
                default:
                    return DefaultUserFeed;
            }
        }

        /// <summary>表示側と同じ判定（<see cref="TweetSerializer.ExtractMedia(Tweet)"/>）で、写真または動画（GIF 含む）を持つか。</summary>
        private static bool HasMediaOfKind(Tweet tweet, bool video)
        {
            foreach (var item in TweetSerializer.ExtractMedia(tweet).Objects())
            {
                var isVideo = item.Str("type") is "video" or "animated_gif";
                if (isVideo == video)
                {
                    return true;
                }
            }

            return false;
        }

        // --- 自分のツイート（settings の screen_name → get_user_tweets） ---
        public static async Task<JsonObject> GetOwnTweetsAsync(int count, string? cursor)
        {
            try
            {
                TwikitSession.Login();
                var client = TwikitSession.Client;
                Debug.WriteLine($"自分のツイート取得中... count={count} cursor={Describe(cursor)}");

                var user = await TwikitSession.GetAuthenticatedUserAsync().ConfigureAwait(false);
                var pinned = new HashSet<string>(user.PinnedTweetIds, StringComparer.Ordinal);
                return await UserTimelineAsync(client, user.Id, pinned, count, cursor, "自分のツイート", DefaultUserFeed).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"自分のツイート取得エラー: {ex.Message}");
                return ErrorTweets(ex.Message);
            }
        }

        private static async Task<JsonObject> UserTimelineAsync(Client client, string userId, HashSet<string> pinnedTweetIds, int count, string? cursor, string label, UserFeed feed)
        {
            var results = new JsonArray();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var currentCursor = cursor;
            var pagesRead = 0;

            while (true)
            {
                var (page, nextCursor) = await TweetPages.FetchAsync(
                    currentCursor,
                    count,
                    c => client.GetUserTweetsAsync(userId, feed.TweetType, count, c)).ConfigureAwait(false);
                pagesRead++;

                if (page.Count == 0)
                {
                    // 空ページは終端として扱う（従来どおり next_cursor は null）。
                    Debug.WriteLine($"{label}が空です");
                    currentCursor = null;
                    break;
                }

                foreach (var tweet in page)
                {
                    if (feed.Filter is not null && !feed.Filter(tweet))
                    {
                        continue;
                    }

                    if (!seen.Add(tweet.Id))
                    {
                        continue;
                    }

                    var dict = TweetSerializer.TweetToDict(tweet);

                    // twikit は Tweets の 1 ページ目の先頭に固定ツイートを入れるが、固定であることは Tweet に残らない。
                    // プロフィールの pinned_tweet_ids と突き合わせて印を付ける。
                    if (feed.TweetType == "Tweets" && tweet.RetweetedTweet is null && pinnedTweetIds.Contains(tweet.Id))
                    {
                        dict["is_pinned"] = true;
                    }

                    results.Add(dict);
                }

                currentCursor = nextCursor;
                if (nextCursor is null)
                {
                    Debug.WriteLine("next_cursor がありません");
                    break;
                }

                // 絞り込みで 1 件も残らなかったときだけ、上限まで続きを読む。
                if (results.Count > 0 || feed.Filter is null || pagesRead >= MaxFilteredPages)
                {
                    Debug.WriteLine($"next_cursor 更新: {nextCursor}");
                    break;
                }

                Debug.WriteLine($"{label}: 絞り込み後 0 件のため続きを取得 ({pagesRead}/{MaxFilteredPages})");
            }

            Debug.WriteLine($"{label} {results.Count} 件取得");
            return TweetsPayload(results, currentCursor);
        }

        /// <summary>
        /// スクリーンネームからユーザー ID と固定ツイート ID を引く。
        /// <paramref name="allowRefresh"/> が true で期限切れなら取り直す（固定ツイートの変更を反映する）。
        /// </summary>
        private static async Task<ResolvedUser> ResolveUserAsync(Client client, string screenName, bool allowRefresh)
        {
            lock (UsersByScreenName)
            {
                if (UsersByScreenName.TryGetValue(screenName, out var cached)
                    && (!allowRefresh || DateTime.UtcNow - cached.ResolvedAtUtc < ResolvedUserTtl))
                {
                    return cached;
                }
            }

            var user = await client.GetUserByScreenNameAsync(screenName).ConfigureAwait(false);
            var resolved = new ResolvedUser(
                user.Id,
                new HashSet<string>(user.PinnedTweetIds, StringComparer.Ordinal),
                DateTime.UtcNow);
            lock (UsersByScreenName)
            {
                UsersByScreenName[screenName] = resolved;
            }

            return resolved;
        }
    }
}
