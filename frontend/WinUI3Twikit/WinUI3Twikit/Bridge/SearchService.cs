using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Twikit;

namespace WinUI3Twikit.Bridge
{
    /// <summary>
    /// ツイート検索（旧 <c>get_search_twikit.py</c>）。
    /// FastAPI 版と同じく、同じクエリの続きはサーバー側（ここ）が覚えていて、
    /// 2 回目以降の呼び出しで次のページを返す。重複もここで除く。
    /// </summary>
    internal static class SearchService
    {
        private static readonly object Gate = new();
        private static Result<Tweet>? _lastPage;          // 旧 last_search_cursor（続きの取得元）
        private static string? _currentQuery;
        private static readonly HashSet<string> SeenSearchTweets = new(StringComparer.Ordinal);   // グローバルで永続的に重複防止

        public static async Task<JsonArray> SearchTweetsAsync(string query, int count, string product, string? cursor)
        {
            var results = new JsonArray();

            try
            {
                TwikitSession.Login();
                var client = TwikitSession.Client;

                Result<Tweet>? previous;
                lock (Gate)
                {
                    if (!string.Equals(_currentQuery, query, StringComparison.Ordinal))
                    {
                        _lastPage = null;
                        _currentQuery = query;
                        SeenSearchTweets.Clear();   // 新しいクエリではリセット
                        Debug.WriteLine($"新しい検索クエリ: {query} → seenクリア");
                    }

                    previous = _lastPage;
                }

                Debug.WriteLine($"検索取得中... query='{query}' cursor={(!string.IsNullOrEmpty(cursor) || previous is not null ? "あり" : "なし")}");

                Result<Tweet> page;
                if (!string.IsNullOrEmpty(cursor))
                {
                    page = await client.SearchTweetAsync(query, product, count, cursor).ConfigureAwait(false);
                }
                else if (previous is not null)
                {
                    page = await previous.NextAsync().ConfigureAwait(false);
                }
                else
                {
                    page = await client.SearchTweetAsync(query, product, count).ConfigureAwait(false);
                }

                if (page.Count == 0)
                {
                    Debug.WriteLine("これ以上検索結果はありません");
                    lock (Gate)
                    {
                        _lastPage = null;
                    }

                    return results;
                }

                Debug.WriteLine($"  └─ 取得したツイート数: {page.Count}");

                foreach (var tweet in page)
                {
                    lock (Gate)
                    {
                        if (!SeenSearchTweets.Add(tweet.Id))
                        {
                            continue;
                        }
                    }

                    results.Add(TweetSerializer.TweetToDict(tweet));
                }

                // カーソル更新（続きがあるときだけページを保持）
                var hasMore = page.NextCursor is not null || page.Count >= count;
                lock (Gate)
                {
                    _lastPage = hasMore ? page : null;
                }

                Debug.WriteLine(hasMore ? "next_cursor 更新" : "これ以上結果なし");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"検索エラー: {ex.Message}");
                lock (Gate)
                {
                    _lastPage = null;
                }
            }

            Debug.WriteLine($"検索完了: {results.Count} 件");
            return results;
        }
    }
}
