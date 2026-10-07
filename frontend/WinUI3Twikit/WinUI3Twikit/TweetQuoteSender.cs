using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace WinUI3Twikit
{
    internal static class TweetQuoteSender
    {
        public static bool HasContent(TweetViewModel vm)
        {
            return !string.IsNullOrWhiteSpace(vm.QuoteText) || vm.QuoteMediaFiles.Count > 0;
        }

        public static IReadOnlyList<string> GetMediaPaths(TweetViewModel vm)
        {
            return [.. vm.QuoteMediaFiles
                .Select(m => m.FilePath)
                .Where(p => !string.IsNullOrWhiteSpace(p))];
        }

        public static async Task TrySendAsync(
            object sender,
            Func<string, string, IReadOnlyList<string>, Task<HttpResponseMessage>> quoteAsync,
            Action<TweetViewModel, string, string> addToTimeline)
        {
            if (sender is not FrameworkElement element)
            {
                return;
            }

            var vm = ReplyInputHelper.FindTweetViewModel(element);
            if (vm == null || vm.IsQuoteSending || string.IsNullOrEmpty(vm.Id))
            {
                return;
            }

            if (!TweetActionRequestGuard.TryBeginQuote(vm.Id))
            {
                return;
            }

            ReplyInputHelper.SyncQuoteTextFromInput(element, vm);
            if (!HasContent(vm))
            {
                TweetActionRequestGuard.EndQuote(vm.Id);
                return;
            }

            var quoteText = vm.QuoteText ?? string.Empty;
            var mediaPaths = GetMediaPaths(vm);
            var quoteSucceeded = false;
            vm.IsQuoteSending = true;
            try
            {
                var response = await quoteAsync(vm.Id, quoteText, mediaPaths);
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                    if (result.TryGetProperty("new_tweet_id", out var newIdElement))
                    {
                        var newTweetId = newIdElement.GetString() ?? string.Empty;
                        await SessionAccount.EnsureLoadedAsync();
                        addToTimeline(vm, newTweetId, quoteText);
                    }

                    quoteSucceeded = true;
                    Debug.WriteLine($"引用ツイート送信＆表示成功: {vm.Id}");
                }
                else
                {
                    var body = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"引用ツイート失敗: {(int)response.StatusCode} {body}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"引用ツイート失敗: {ex.Message}");
            }
            finally
            {
                vm.IsQuoteSending = false;
                TweetActionRequestGuard.EndQuote(vm.Id);
                if (quoteSucceeded)
                {
                    vm.CancelQuoting();
                }
            }
        }
    }
}
