using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Twikit;

namespace WinUI3Twikit.Bridge
{
    /// <summary>ツイート投稿・引用ツイート・投稿ジョブ（旧 <c>backend/post_tweet.py</c>）。</summary>
    internal static class TweetPoster
    {
        // Keep in sync with TweetPage AllowedMediaExtensions (subset used for path validation).
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pjp", ".jfif", ".jpe", ".pjpeg", ".jpeg", ".jpg", ".png", ".webp", ".gif", ".m4v", ".mp4", ".mov",
        };

        private static string ValidateMediaPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Empty media path");
            }

            var trimmed = path.Trim();
            if (!Path.IsPathRooted(trimmed))
            {
                throw new ArgumentException($"Media path must be absolute: {path}");
            }

            var normalized = Path.GetFullPath(trimmed);
            if (!File.Exists(normalized))
            {
                throw new ArgumentException($"Media file not found: {normalized}");
            }

            var ext = Path.GetExtension(normalized).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext))
            {
                throw new ArgumentException($"Unsupported media extension: {ext}");
            }

            return normalized;
        }

        /// <summary>X / twikit のメディアエラーを読みやすい日本語にする。</summary>
        private static string FriendlyInvalidMediaMessage(Exception exc)
        {
            var raw = exc.Message ?? string.Empty;
            if (Regex.IsMatch(raw, "Duration too long", RegexOptions.IgnoreCase))
            {
                var maxMatch = Regex.Match(raw, @"maximum:\s*([^\s,]+)", RegexOptions.IgnoreCase);
                var actualMatch = Regex.Match(raw, @"actual:\s*([^\s,(]+)", RegexOptions.IgnoreCase);
                var maxText = maxMatch.Success ? maxMatch.Groups[1].Value : "不明";
                var actualText = actualMatch.Success ? actualMatch.Groups[1].Value : "不明";
                return "動画の長さがアカウントの上限を超えています。"
                       + $" 上限={maxText} / 実際={actualText}。"
                       + " 短い動画に切り出すか、X Premium 等で長い動画が許可されているアカウントを使ってください。"
                       + $" (詳細: {raw})";
            }

            return $"メディアが拒否されました: {raw}";
        }

        /// <summary>ローカルファイルをアップロードして media ID を返す。paths が空なら空リスト。</summary>
        public static async Task<List<string>> UploadMediaIdsAsync(IReadOnlyList<string>? paths)
        {
            var mediaIds = new List<string>();
            if (paths is null || paths.Count == 0)
            {
                return mediaIds;
            }

            var client = TwikitSession.Client;
            try
            {
                foreach (var rawPath in paths)
                {
                    var path = ValidateMediaPath(rawPath);
                    var mediaId = MediaUpload.IsVideoPath(path)
                        ? await MediaUpload.UploadMediaStreamingAsync(
                            client,
                            path,
                            mediaCategory: "tweet_video",
                            isLongVideo: true,
                            waitForCompletion: true).ConfigureAwait(false)
                        : await client.UploadMediaAsync(path).ConfigureAwait(false);
                    mediaIds.Add(mediaId);
                }
            }
            catch (InvalidMediaException e)
            {
                throw new ArgumentException(FriendlyInvalidMediaMessage(e), e);
            }

            return mediaIds;
        }

        /// <summary>同期（1 リクエストで完結）の投稿。進捗表示には StartTweetJob / RunTweetJobAsync を使う。</summary>
        public static async Task<string> TweetingWithMediaAsync(string? text, IReadOnlyList<string>? paths)
        {
            TwikitSession.Login();

            try
            {
                var mediaIds = await UploadMediaIdsAsync(paths).ConfigureAwait(false);
                var tweet = await TwikitSession.Client.CreateTweetAsync(text ?? string.Empty, mediaIds).ConfigureAwait(false);
                Debug.WriteLine($"投稿完了  ID: {tweet.Id} (メディア: {mediaIds.Count}件)");
                return tweet.Id;
            }
            catch (InvalidMediaException e)
            {
                throw new ArgumentException(FriendlyInvalidMediaMessage(e), e);
            }
        }

        /// <summary>引用ツイート（ローカルメディア添付可）。</summary>
        public static async Task<string> QuoteWithMediaAsync(string tweetId, string? text, IReadOnlyList<string>? paths)
        {
            TwikitSession.Login();
            var attachmentUrl = $"https://x.com/i/status/{tweetId}";

            try
            {
                var mediaIds = await UploadMediaIdsAsync(paths).ConfigureAwait(false);
                var tweet = await TwikitSession.Client.CreateTweetAsync(
                    text ?? string.Empty,
                    mediaIds.Count > 0 ? mediaIds : null,
                    attachmentUrl: attachmentUrl).ConfigureAwait(false);
                Debug.WriteLine($"引用ツイート完了  ID: {tweet.Id} (引用元: {tweetId}, メディア: {mediaIds.Count}件)");
                return tweet.Id;
            }
            catch (InvalidMediaException e)
            {
                throw new ArgumentException(FriendlyInvalidMediaMessage(e), e);
            }
        }

        /// <summary>パスを検証してジョブを作る（処理はまだ始めない）。</summary>
        public static TweetJob StartTweetJob(string? text, IReadOnlyList<string>? paths)
        {
            var validated = new List<string>();
            if (paths is not null)
            {
                foreach (var raw in paths)
                {
                    validated.Add(ValidateMediaPath(raw));
                }
            }

            return TweetJobStore.Instance.Create(text ?? string.Empty, validated);
        }

        /// <summary>バックグラウンド処理: 進捗を更新しながらメディアをアップロードし、ツイートを作成する。</summary>
        public static async Task RunTweetJobAsync(string jobId)
        {
            var store = TweetJobStore.Instance;
            var job = store.Get(jobId);
            if (job is null)
            {
                return;
            }

            store.SetState(jobId, "running", message: "投稿処理中...");

            // Mark all as waiting initially.
            for (var i = 0; i < job.Items.Count; i++)
            {
                store.UpdateItem(jobId, i, phase: "waiting", percent: 0);
            }

            var mediaIds = new List<string>();
            try
            {
                TwikitSession.Login();
                var client = TwikitSession.Client;

                for (var index = 0; index < job.Items.Count; index++)
                {
                    var path = job.Items[index].Path;
                    var itemIndex = index;
                    string mediaId;

                    if (MediaUpload.IsVideoPath(path))
                    {
                        store.UpdateItem(jobId, index, phase: "uploading", percent: 0);
                        mediaId = await MediaUpload.UploadMediaStreamingAsync(
                            client,
                            path,
                            mediaCategory: "tweet_video",
                            isLongVideo: true,
                            waitForCompletion: true,
                            onProgress: (phase, percent) => store.UpdateItem(jobId, itemIndex, phase: phase, percent: percent))
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        store.UpdateItem(jobId, index, phase: "uploading", percent: 0);
                        mediaId = await client.UploadMediaAsync(path).ConfigureAwait(false);
                        store.UpdateItem(jobId, index, phase: "uploading", percent: 100);
                    }

                    store.UpdateItem(jobId, index, phase: "done", percent: 100);
                    mediaIds.Add(mediaId);
                }

                store.SetState(jobId, "running", message: "ツイート作成中...");
                var tweet = await client.CreateTweetAsync(job.Text ?? string.Empty, mediaIds).ConfigureAwait(false);
                var message = $"ツイート完了 ID: {tweet.Id}";
                Debug.WriteLine($"投稿完了  ID: {tweet.Id} (メディア: {mediaIds.Count}件)");
                store.SetState(jobId, "succeeded", message: message, tweetId: tweet.Id);
            }
            catch (InvalidMediaException e)
            {
                var friendly = FriendlyInvalidMediaMessage(e);
                store.FailUnfinishedItems(jobId, friendly);
                store.SetState(jobId, "failed", message: friendly);
            }
            catch (Exception e)
            {
                store.FailUnfinishedItems(jobId, e.Message);
                store.SetState(jobId, "failed", message: e.Message);
                Debug.WriteLine($"投稿ジョブ失敗 {jobId}: {e}");
            }
        }
    }
}
