using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace WinUI3Twikit
{
    internal static class TweetActionClient
    {
        private sealed class ActionResponse
        {
            public bool success { get; set; } = true;
            public string? error { get; set; }
            public string? detail { get; set; }
        }

        public static async Task<ActionOutcome> LikeAsync(HttpClient client, string tweetId, bool currentlyLiked)
        {
            if (string.IsNullOrEmpty(tweetId))
            {
                return ActionOutcome.Failed("ツイート ID がありません");
            }

            try
            {
                var response = currentlyLiked
                    ? await client.DeleteAsync($"http://localhost:8000/like/{tweetId}")
                    : await client.PostAsync($"http://localhost:8000/like/{tweetId}", null);
                return await ReadOutcomeAsync(response);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Like API Error: {ex.Message}");
                return ActionOutcome.Failed(ex.Message);
            }
        }

        /// <summary>RT 済みなら取り消し（DELETE）、未 RT なら RT（POST）。</summary>
        public static async Task<ActionOutcome> RetweetAsync(HttpClient client, string tweetId, bool currentlyRetweeted)
        {
            if (string.IsNullOrEmpty(tweetId))
            {
                return ActionOutcome.Failed("ツイート ID がありません");
            }

            try
            {
                var response = currentlyRetweeted
                    ? await client.DeleteAsync($"http://localhost:8000/retweet/{tweetId}")
                    : await client.PostAsync($"http://localhost:8000/retweet/{tweetId}", null);
                return await ReadOutcomeAsync(response);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Retweet API Error: {ex.Message}");
                return ActionOutcome.Failed(ex.Message);
            }
        }

        private static readonly HttpClient QuoteHttpClient = new()
        {
            Timeout = TimeSpan.FromHours(2)
        };

        public static async Task<HttpResponseMessage> QuoteAsync(
            HttpClient client,
            string tweetId,
            string quoteText,
            IReadOnlyList<string>? mediaPaths = null)
        {
            if (string.IsNullOrEmpty(tweetId))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest);
            }

            var paths = mediaPaths?
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToArray()
                ?? [];

            var http = paths.Length > 0 ? QuoteHttpClient : client;

            try
            {
                var content = new StringContent(
                    JsonSerializer.Serialize(new { text = quoteText ?? string.Empty, paths }),
                    Encoding.UTF8,
                    "application/json");
                return await http.PostAsync($"http://localhost:8000/quote/{tweetId}", content);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Quote API Error: {ex.Message}");
                return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest);
            }
        }

        private static async Task<ActionOutcome> ReadOutcomeAsync(HttpResponseMessage response)
        {
            var json = await response.Content.ReadAsStringAsync();
            ActionResponse? result = null;
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    result = JsonSerializer.Deserialize<ActionResponse>(json);
                }
                catch (JsonException)
                {
                    // JSON でなければ HTTP ステータスだけで判断する
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                var detail = result?.detail ?? result?.error;
                return ActionOutcome.Failed(string.IsNullOrEmpty(detail)
                    ? $"HTTP {(int)response.StatusCode}"
                    : detail);
            }

            if (result is null || result.success)
            {
                return ActionOutcome.Succeeded;
            }

            return ActionOutcome.Failed(result.error ?? "送信に失敗しました");
        }
    }
}
