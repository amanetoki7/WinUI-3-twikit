using System;
using System.Threading.Tasks;

namespace WinUI3Twikit
{
    /// <summary>
    /// いいね・リツイートの楽観的更新。UI を先に反転し、送信に失敗したら元に戻して通知する。
    /// UI スレッドから呼ぶこと（await 後も UI スレッドに戻る）。
    /// </summary>
    internal static class TweetActionHandler
    {
        public static async Task HandleLikeAsync(
            TweetViewModel vm,
            Func<string, bool, Task<ActionOutcome>> executeLike,
            Func<string, TweetViewModel?> findTweet)
        {
            if (string.IsNullOrEmpty(vm.Id) || !TweetActionRequestGuard.TryBeginLike(vm.Id))
            {
                return;
            }

            var target = findTweet(vm.Id) ?? vm;
            bool wasLiked = target.IsLiked;
            target.ToggleLike();
            var label = wasLiked ? "いいね解除" : "いいね";

            try
            {
                var outcome = await executeLike(vm.Id, wasLiked);
                if (!outcome.Ok)
                {
                    (findTweet(vm.Id) ?? target).ToggleLike();
                    AppNotifications.Error($"{label}に失敗しました: {outcome.Error}");
                }
            }
            catch (Exception ex)
            {
                (findTweet(vm.Id) ?? target).ToggleLike();
                System.Diagnostics.Debug.WriteLine($"Like handler error: {ex.Message}");
                AppNotifications.Error($"{label}に失敗しました: {ex.Message}");
            }
            finally
            {
                TweetActionRequestGuard.EndLike(vm.Id);
            }
        }

        public static async Task HandleRetweetAsync(
            TweetViewModel vm,
            Func<string, bool, Task<ActionOutcome>> executeRetweet,
            Func<string, TweetViewModel?> findTweet)
        {
            if (string.IsNullOrEmpty(vm.Id) || vm.IsUserProtected || !TweetActionRequestGuard.TryBeginRetweet(vm.Id))
            {
                return;
            }

            var target = findTweet(vm.Id) ?? vm;
            bool wasRetweeted = target.IsRetweeted;
            target.ToggleRetweet();
            var label = wasRetweeted ? "リツイート取り消し" : "リツイート";

            try
            {
                var outcome = await executeRetweet(vm.Id, wasRetweeted);
                if (!outcome.Ok)
                {
                    (findTweet(vm.Id) ?? target).ToggleRetweet();
                    AppNotifications.Error($"{label}に失敗しました: {outcome.Error}");
                }
            }
            catch (Exception ex)
            {
                (findTweet(vm.Id) ?? target).ToggleRetweet();
                System.Diagnostics.Debug.WriteLine($"Retweet handler error: {ex.Message}");
                AppNotifications.Error($"{label}に失敗しました: {ex.Message}");
            }
            finally
            {
                TweetActionRequestGuard.EndRetweet(vm.Id);
            }
        }
    }
}
