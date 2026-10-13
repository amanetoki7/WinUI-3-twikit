using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace WinUI3Twikit.Bridge
{
    /// <summary>キューの実行結果。失敗したら <see cref="Error"/> に理由。</summary>
    internal readonly record struct ActionResult(bool Ok, string? Error)
    {
        public static ActionResult Succeeded { get; } = new(true, null);

        public static ActionResult Failed(string error) => new(false, error);
    }

    /// <summary>キューで順次実行する操作。</summary>
    /// <param name="Action">like / unlike / retweet / unretweet / reply / quote。</param>
    /// <param name="TweetId">対象ツイートの ID。</param>
    /// <param name="Text">reply / quote の本文。</param>
    internal sealed record ActionJob(string Action, string TweetId, string? Text = null)
    {
        internal TaskCompletionSource<ActionResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>
    /// いいね・RT・返信の非同期キュー（旧 <c>backend/action_queue.py</c>）。
    /// UI は即時反映し、X への送信はここで 1 件ずつ 0.5 秒間隔で行う。
    /// 失敗は <see cref="EnqueueAsync"/> の戻り値で呼び出し側に返し、UI 側で状態を戻す。
    /// </summary>
    internal sealed class ActionQueue
    {
        public static ActionQueue Instance { get; } = new();

        private readonly Channel<ActionJob> _queue = Channel.CreateUnbounded<ActionJob>(
            new UnboundedChannelOptions { SingleReader = true });
        private readonly object _gate = new();
        private Task? _workerTask;

        /// <summary>ジョブを投入し、送信結果を待てる Task を返す。</summary>
        public Task<ActionResult> EnqueueAsync(ActionJob job)
        {
            StartWorker();
            if (!_queue.Writer.TryWrite(job))
            {
                job.Completion.TrySetResult(ActionResult.Failed("キューに追加できませんでした"));
            }

            return job.Completion.Task;
        }

        public void StartWorker()
        {
            lock (_gate)
            {
                if (_workerTask is not null)
                {
                    return;
                }

                _workerTask = Task.Run(WorkerLoopAsync);
                Debug.WriteLine("Action queue worker started");
            }
        }

        private async Task WorkerLoopAsync()
        {
            await foreach (var job in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    await ExecuteAsync(job).ConfigureAwait(false);
                    job.Completion.TrySetResult(ActionResult.Succeeded);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Action queue worker failed: action={job.Action} tweet_id={job.TweetId}: {ex.Message}");
                    job.Completion.TrySetResult(ActionResult.Failed(ex.Message));
                }
                finally
                {
                    await Task.Delay(500).ConfigureAwait(false);
                }
            }
        }

        private static void EnsureSuccess(HttpResponseMessage response, string action)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new BridgeException($"{action} に失敗しました (HTTP {(int)response.StatusCode})");
            }
        }

        private static async Task ExecuteAsync(ActionJob job)
        {
            TwikitSession.Login();
            var client = TwikitSession.Client;

            switch (job.Action)
            {
                case "like":
                    EnsureSuccess(await client.FavoriteTweetAsync(job.TweetId).ConfigureAwait(false), "いいね");
                    Debug.WriteLine($"Queued like completed: tweet_id={job.TweetId}");
                    break;
                case "unlike":
                    EnsureSuccess(await client.UnfavoriteTweetAsync(job.TweetId).ConfigureAwait(false), "いいね解除");
                    Debug.WriteLine($"Queued unlike completed: tweet_id={job.TweetId}");
                    break;
                case "retweet":
                    EnsureSuccess(await client.RetweetAsync(job.TweetId).ConfigureAwait(false), "リツイート");
                    Debug.WriteLine($"Queued retweet completed: tweet_id={job.TweetId}");
                    break;
                case "unretweet":
                    // 元ツイートの ID を渡す（リツイート自身の ID では何も起きない）。
                    EnsureSuccess(await client.DeleteRetweetAsync(job.TweetId).ConfigureAwait(false), "リツイート取り消し");
                    Debug.WriteLine($"Queued unretweet completed: tweet_id={job.TweetId}");
                    break;
                case "reply":
                    await client.CreateTweetAsync(text: job.Text ?? string.Empty, replyTo: job.TweetId).ConfigureAwait(false);
                    Debug.WriteLine($"Queued reply completed: tweet_id={job.TweetId}");
                    break;
                case "quote":
                    await client.CreateTweetAsync(
                        text: job.Text ?? string.Empty,
                        attachmentUrl: $"https://x.com/i/status/{job.TweetId}").ConfigureAwait(false);
                    Debug.WriteLine($"Queued quote completed: tweet_id={job.TweetId}");
                    break;
                default:
                    throw new BridgeException($"不明な操作です: {job.Action}");
            }
        }
    }
}
