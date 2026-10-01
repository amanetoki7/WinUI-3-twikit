using System;
using System.Diagnostics;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace WinUI3Twikit.Bridge
{
    /// <summary>キューで順次実行する操作。</summary>
    /// <param name="Action">like / unlike / retweet / reply / quote。</param>
    /// <param name="TweetId">対象ツイートの ID。</param>
    /// <param name="Text">reply / quote の本文。</param>
    internal sealed record ActionJob(string Action, string TweetId, string? Text = null);

    /// <summary>
    /// いいね・RT・返信の非同期キュー（旧 <c>backend/action_queue.py</c>）。
    /// UI は即時反映し、X への送信はここで 1 件ずつ 0.5 秒間隔で行う。
    /// </summary>
    internal sealed class ActionQueue
    {
        public static ActionQueue Instance { get; } = new();

        private readonly Channel<ActionJob> _queue = Channel.CreateUnbounded<ActionJob>(
            new UnboundedChannelOptions { SingleReader = true });
        private readonly object _gate = new();
        private Task? _workerTask;

        public ValueTask EnqueueAsync(ActionJob job)
        {
            StartWorker();
            return _queue.Writer.WriteAsync(job);
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
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Action queue worker failed: action={job.Action} tweet_id={job.TweetId}: {ex.Message}");
                }
                finally
                {
                    await Task.Delay(500).ConfigureAwait(false);
                }
            }
        }

        private static async Task ExecuteAsync(ActionJob job)
        {
            TwikitSession.Login();
            var client = TwikitSession.Client;

            switch (job.Action)
            {
                case "like":
                    await client.FavoriteTweetAsync(job.TweetId).ConfigureAwait(false);
                    Debug.WriteLine($"Queued like completed: tweet_id={job.TweetId}");
                    break;
                case "unlike":
                    await client.UnfavoriteTweetAsync(job.TweetId).ConfigureAwait(false);
                    Debug.WriteLine($"Queued unlike completed: tweet_id={job.TweetId}");
                    break;
                case "retweet":
                    await client.RetweetAsync(job.TweetId).ConfigureAwait(false);
                    Debug.WriteLine($"Queued retweet completed: tweet_id={job.TweetId}");
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
                    Debug.WriteLine($"Action queue: unknown action {job.Action}");
                    break;
            }
        }
    }
}
