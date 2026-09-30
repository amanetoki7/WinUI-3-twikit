using Microsoft.UI.Xaml;
using System;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace WinUI3Twikit
{
    internal static class TweetReplySender
    {
        public static async Task TrySendAsync(
            object sender,
            Func<string, string, Task<System.Net.Http.HttpResponseMessage>> replyAsync,
            Action<TweetViewModel, string, string> addToTimeline)
        {
            if (sender is not FrameworkElement element)
            {
                return;
            }

            var vm = ReplyInputHelper.FindTweetViewModel(element);
            if (vm == null || vm.IsReplySending)
            {
                return;
            }

            ReplyInputHelper.SyncReplyTextFromInput(element, vm);
            if (string.IsNullOrWhiteSpace(vm.ReplyText))
            {
                return;
            }

            var replyText = vm.ReplyText;
            var replySucceeded = false;
            vm.IsReplySending = true;
            try
            {
                var response = await replyAsync(vm.Id, replyText);
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<JsonElement>();
                    if (result.TryGetProperty("new_tweet_id", out var newIdElement))
                    {
                        var newTweetId = newIdElement.GetString() ?? string.Empty;
                        addToTimeline(vm, newTweetId, replyText);
                    }

                    replySucceeded = true;
                    Debug.WriteLine($"リプライ送信＆表示成功: {vm.Id}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"リプライ失敗: {ex.Message}");
            }
            finally
            {
                vm.IsReplySending = false;
                if (replySucceeded)
                {
                    vm.IsReplying = false;
                    vm.ReplyText = string.Empty;
                }
            }
        }
    }
}
