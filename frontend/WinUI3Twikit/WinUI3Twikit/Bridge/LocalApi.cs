using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Twikit;

namespace WinUI3Twikit.Bridge
{
    /// <summary>
    /// 旧 <c>backend/api.py</c>（FastAPI）のエンドポイントをプロセス内で再現する。
    /// パスとレスポンスの形は FastAPI 版と同じなので、ViewModel 側は変更不要。
    /// </summary>
    internal static class LocalApi
    {
        private sealed record Route(string Method, string[] Pattern, Func<RouteContext, Task<HttpResponseMessage>> Handler);

        private sealed class RouteContext
        {
            public required HttpRequestMessage Request { get; init; }

            public required Dictionary<string, string> Params { get; init; }

            public required Dictionary<string, string> Query { get; init; }

            public CancellationToken CancellationToken { get; init; }

            public async Task<JsonObject> ReadJsonBodyAsync()
            {
                if (Request.Content is null)
                {
                    return [];
                }

                var text = await Request.Content.ReadAsStringAsync(CancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(text))
                {
                    return [];
                }

                return JsonNode.Parse(text) as JsonObject ?? [];
            }
        }

        private static readonly Route[] Routes =
        [
            new("GET", [], _ => Task.FromResult(JsonResponses.Ok(new JsonObject { ["status"] = "ok" }))),
            new("GET", ["favicon.ico"], Favicon),
            new("POST", ["tweet"], Tweet),
            new("POST", ["tweet", "start"], TweetStart),
            new("GET", ["tweet", "jobs", "{job_id}"], TweetJobStatus),
            new("GET", ["timeline"], Timeline),
            new("POST", ["like", "{tweet_id}"], ctx => EnqueueAction("like", ctx)),
            new("DELETE", ["like", "{tweet_id}"], ctx => EnqueueAction("unlike", ctx)),
            new("POST", ["retweet", "{tweet_id}"], ctx => EnqueueAction("retweet", ctx)),
            new("POST", ["reply", "{tweet_id}"], Reply),
            new("POST", ["quote", "{tweet_id}"], Quote),
            new("GET", ["profile"], Profile),
            new("GET", ["profile", "tweets"], ProfileTweets),
            new("GET", ["notifications"], Notifications),
            new("GET", ["lists"], Lists),
            new("GET", ["lists", "{list_id}", "tweets"], ListTweets),
            new("GET", ["search"], Search),
            new("GET", ["users", "{screen_name}"], UserProfile),
            new("GET", ["users", "{screen_name}", "tweets"], UserTweets),
        ];

        public static async Task<HttpResponseMessage> DispatchAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // 起動処理（Cookie 読み込み・ハンドシェイク）が終わるまで待つ。
            await TwikitBridge.WaitForStartupAsync(cancellationToken).ConfigureAwait(false);

            var uri = request.RequestUri!;
            var segments = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.UnescapeDataString)
                .ToArray();

            var pathMatched = false;
            foreach (var route in Routes)
            {
                if (!TryMatch(route.Pattern, segments, out var parameters))
                {
                    continue;
                }

                pathMatched = true;
                if (!string.Equals(route.Method, request.Method.Method, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var context = new RouteContext
                {
                    Request = request,
                    Params = parameters,
                    Query = QueryString.Parse(uri.Query),
                    CancellationToken = cancellationToken,
                };
                return await route.Handler(context).ConfigureAwait(false);
            }

            return pathMatched
                ? JsonResponses.Error(HttpStatusCode.MethodNotAllowed, "Method Not Allowed")
                : JsonResponses.Error(HttpStatusCode.NotFound, "Not Found");
        }

        private static bool TryMatch(string[] pattern, string[] segments, out Dictionary<string, string> parameters)
        {
            parameters = new Dictionary<string, string>(StringComparer.Ordinal);
            if (pattern.Length != segments.Length)
            {
                return false;
            }

            for (var i = 0; i < pattern.Length; i++)
            {
                var part = pattern[i];
                if (part.Length > 2 && part[0] == '{' && part[^1] == '}')
                {
                    parameters[part[1..^1]] = segments[i];
                    continue;
                }

                if (!string.Equals(part, segments[i], StringComparison.Ordinal))
                {
                    parameters.Clear();
                    return false;
                }
            }

            return true;
        }

        /// <summary>FastAPI 版で 400 にしていた例外（ValueError / FileNotFoundError / InvalidMedia / Twitter* / TooMany*）。</summary>
        private static bool IsClientError(Exception ex)
            => ex is ArgumentException or FileNotFoundException or TwitterException;

        /// <summary>FastAPI 版の <c>TweetIn</c> / <c>QuoteIn</c>（text + ローカルパス一覧）。</summary>
        private static (string Text, List<string> Paths) ReadTweetIn(JsonObject body)
            => (body.Str("text") ?? string.Empty, body.ArrOrEmpty("paths").Strings());

        // --- 投稿（ローカルパス渡し・同期・互換用） ---
        private static async Task<HttpResponseMessage> Tweet(RouteContext ctx)
        {
            TwikitSession.Login();
            var (text, paths) = ReadTweetIn(await ctx.ReadJsonBodyAsync().ConfigureAwait(false));
            try
            {
                var tweetId = await TweetPoster.TweetingWithMediaAsync(text, paths).ConfigureAwait(false);
                return JsonResponses.Ok(new JsonObject { ["result"] = $"ツイート完了 ID: {tweetId}" });
            }
            catch (Exception ex) when (IsClientError(ex))
            {
                // Surface media / API failures without opaque 500 when possible.
                return JsonResponses.Error(HttpStatusCode.BadRequest, ex.Message);
            }
        }

        // --- 投稿ジョブ（進捗ポーリング用） ---
        private static async Task<HttpResponseMessage> TweetStart(RouteContext ctx)
        {
            TwikitSession.Login();
            var (text, paths) = ReadTweetIn(await ctx.ReadJsonBodyAsync().ConfigureAwait(false));

            TweetJob job;
            try
            {
                job = TweetPoster.StartTweetJob(text, paths);
            }
            catch (Exception ex) when (ex is ArgumentException or FileNotFoundException)
            {
                return JsonResponses.Error(HttpStatusCode.BadRequest, ex.Message);
            }

            _ = Task.Run(() => TweetPoster.RunTweetJobAsync(job.JobId));
            return JsonResponses.Ok(new JsonObject { ["job_id"] = job.JobId, ["state"] = job.State });
        }

        private static Task<HttpResponseMessage> TweetJobStatus(RouteContext ctx)
        {
            var snapshot = TweetJobStore.Instance.Snapshot(ctx.Params["job_id"]);
            return Task.FromResult(snapshot is null
                ? JsonResponses.Error(HttpStatusCode.NotFound, "job not found")
                : JsonResponses.Ok(snapshot));
        }

        // --- タイムライン ---
        private static async Task<HttpResponseMessage> Timeline(RouteContext ctx)
        {
            var result = await TimelineService.GetTimelineTweetsAsync(
                pages: ctx.Query.GetInt("pages", 1),
                countPerPage: ctx.Query.GetNullableInt("count"),
                timelineType: ctx.Query.GetString("type") ?? "for_you",
                cursor: ctx.Query.GetString("cursor")).ConfigureAwait(false);
            return JsonResponses.Ok(result);
        }

        // --- いいね / いいね解除 / リツイート（キュー処理） ---
        private static async Task<HttpResponseMessage> EnqueueAction(string action, RouteContext ctx)
        {
            await ActionQueue.Instance.EnqueueAsync(new ActionJob(action, ctx.Params["tweet_id"])).ConfigureAwait(false);
            return JsonResponses.Ok(new JsonObject { ["success"] = true, ["action"] = action, ["queued"] = true });
        }

        // --- リプライ ---
        private static async Task<HttpResponseMessage> Reply(RouteContext ctx)
        {
            TwikitSession.Login();
            var tweetId = ctx.Params["tweet_id"];
            var body = await ctx.ReadJsonBodyAsync().ConfigureAwait(false);
            var tweet = await TwikitSession.Client.CreateTweetAsync(
                text: body.Str("text") ?? string.Empty,
                replyTo: tweetId).ConfigureAwait(false);
            return JsonResponses.Ok(new JsonObject
            {
                ["success"] = true,
                ["action"] = "reply",
                ["tweet_id"] = tweetId,
                ["new_tweet_id"] = tweet.Id,
                ["queued"] = false,
            });
        }

        // --- 引用ツイート ---
        private static async Task<HttpResponseMessage> Quote(RouteContext ctx)
        {
            var tweetId = ctx.Params["tweet_id"];
            var (text, paths) = ReadTweetIn(await ctx.ReadJsonBodyAsync().ConfigureAwait(false));
            try
            {
                var newTweetId = await TweetPoster.QuoteWithMediaAsync(tweetId, text, paths).ConfigureAwait(false);
                return JsonResponses.Ok(new JsonObject
                {
                    ["success"] = true,
                    ["action"] = "quote",
                    ["tweet_id"] = tweetId,
                    ["new_tweet_id"] = newTweetId,
                    ["queued"] = false,
                });
            }
            catch (Exception ex) when (IsClientError(ex))
            {
                return JsonResponses.Error(HttpStatusCode.BadRequest, ex.Message);
            }
        }

        // --- プロフィール ---
        private static async Task<HttpResponseMessage> Profile(RouteContext ctx)
            => JsonResponses.Ok(await ProfileService.GetOwnProfileAsync().ConfigureAwait(false));

        private static async Task<HttpResponseMessage> ProfileTweets(RouteContext ctx)
        {
            try
            {
                return JsonResponses.Ok(await TimelineService.GetOwnTweetsAsync(
                    ctx.Query.GetInt("count", 20),
                    ctx.Query.GetString("cursor")).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Own Profile Tweets API Error: {ex.Message}");
                return JsonResponses.Ok(new JsonObject { ["error"] = ex.Message, ["tweets"] = new JsonArray(), ["next_cursor"] = null });
            }
        }

        // --- 通知 ---
        private static async Task<HttpResponseMessage> Notifications(RouteContext ctx)
            => JsonResponses.Ok(await NotificationsService.GetNotificationsAsync(
                ctx.Query.GetInt("count", 20),
                ctx.Query.GetBool("refresh", true),
                ctx.Query.GetString("type"),
                ctx.Query.GetBool("keepCursor", false)).ConfigureAwait(false));

        // --- リスト ---
        private static async Task<HttpResponseMessage> Lists(RouteContext ctx)
        {
            try
            {
                return JsonResponses.Ok(await TimelineService.GetUserListsAsync(
                    ctx.Query.GetInt("count", 100),
                    ctx.Query.GetString("cursor")).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Lists API Error: {ex.Message}");
                return JsonResponses.Ok(new JsonObject { ["error"] = ex.Message, ["lists"] = new JsonArray(), ["next_cursor"] = null });
            }
        }

        private static async Task<HttpResponseMessage> ListTweets(RouteContext ctx)
        {
            try
            {
                return JsonResponses.Ok(await TimelineService.GetListTimelineAsync(
                    ctx.Params["list_id"],
                    ctx.Query.GetInt("count", 30),
                    ctx.Query.GetString("cursor")).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"List Tweets API Error: {ex.Message}");
                return JsonResponses.Ok(new JsonObject { ["error"] = ex.Message, ["tweets"] = new JsonArray(), ["next_cursor"] = null });
            }
        }

        // --- 検索 ---
        private static async Task<HttpResponseMessage> Search(RouteContext ctx)
        {
            var query = ctx.Query.GetString("query");
            if (query is null)
            {
                return JsonResponses.Error(HttpStatusCode.UnprocessableEntity, "query is required");
            }

            try
            {
                return JsonResponses.Ok(await SearchService.SearchTweetsAsync(
                    query,
                    ctx.Query.GetInt("count", 20),
                    ctx.Query.GetString("product") ?? "Latest",
                    ctx.Query.GetString("cursor")).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Search API Error: {ex.Message}");
                return JsonResponses.Ok(new JsonObject { ["error"] = ex.Message });
            }
        }

        // --- ユーザープロフィール検索 ---
        private static async Task<HttpResponseMessage> UserProfile(RouteContext ctx)
        {
            try
            {
                return JsonResponses.Ok(await ProfileService.GetUserProfileAsync(ctx.Params["screen_name"]).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"User Profile API Error: {ex.Message}");
                return JsonResponses.Ok(new JsonObject { ["error"] = ex.Message });
            }
        }

        private static async Task<HttpResponseMessage> UserTweets(RouteContext ctx)
        {
            try
            {
                return JsonResponses.Ok(await TimelineService.GetUserTweetsAsync(
                    ctx.Params["screen_name"],
                    ctx.Query.GetInt("count", 20),
                    ctx.Query.GetString("cursor")).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"User Tweets API Error: {ex.Message}");
                return JsonResponses.Ok(new JsonObject { ["error"] = ex.Message, ["tweets"] = new JsonArray(), ["next_cursor"] = null });
            }
        }

        // --- favicon（旧 API 互換） ---
        private static Task<HttpResponseMessage> Favicon(RouteContext ctx)
        {
            var path = RepositoryPaths.FaviconPath;
            if (path is null || !File.Exists(path))
            {
                return Task.FromResult(JsonResponses.Error(HttpStatusCode.NotFound, "Not Found"));
            }

            return Task.FromResult(JsonResponses.File(File.ReadAllBytes(path), "image/x-icon"));
        }
    }
}
