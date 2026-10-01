using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Twikit;

namespace WinUI3Twikit.Bridge
{
    /// <summary>
    /// ファイル全体をメモリに載せないチャンクアップロード（旧 <c>backend/media_upload.py</c>）。
    /// twikit-dotnet の <c>UploadMediaAsync</c> はファイル全体を読み込み進捗も返さないため、
    /// 動画は INIT / APPEND / FINALIZE を自前で順に呼ぶ。
    /// </summary>
    internal static class MediaUpload
    {
        /// <summary>Twitter media upload segment size (same as twikit Client.UploadMediaAsync).</summary>
        public const int ChunkSize = 8 * 1024 * 1024;

        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mov", ".m4v",
        };

        public static bool IsVideoPath(string path) => VideoExtensions.Contains(Path.GetExtension(path));

        public static string GuessMediaType(string path)
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".mp4" or ".m4v" => "video/mp4",
                ".mov" => "video/quicktime",
                ".gif" => "image/gif",
                ".png" => "image/png",
                ".jpg" or ".jpeg" or ".jfif" or ".pjp" or ".pjpeg" or ".jpe" => "image/jpeg",
                ".webp" => "image/webp",
                _ => "application/octet-stream",
            };
        }

        private static void Emit(Action<string, int>? onProgress, string phase, int percent)
            => onProgress?.Invoke(phase, Math.Max(0, Math.Min(100, percent)));

        /// <summary>
        /// INIT / APPEND / FINALIZE でローカルファイルをアップロードする。
        /// APPEND は順次実行なので、使用メモリは 1 チャンク（8MB）程度に収まる。
        /// </summary>
        /// <param name="onProgress">(phase, percent) を受け取る。phase は "uploading" | "processing"。</param>
        /// <returns>media ID。</returns>
        public static async Task<string> UploadMediaStreamingAsync(
            Client client,
            string path,
            string? mediaCategory = "tweet_video",
            bool isLongVideo = true,
            bool waitForCompletion = true,
            double? statusCheckInterval = null,
            Action<string, int>? onProgress = null)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Media file not found: {path}", path);
            }

            var totalBytes = new FileInfo(path).Length;
            if (totalBytes <= 0)
            {
                throw new ArgumentException($"Media file is empty: {path}");
            }

            var mediaType = GuessMediaType(path);

            // 2:20 を超える動画も、アカウント（X Premium 等）が許せば通るよう長尺用エンドポイントを使う。
            // 無料枠などでは STATUS か create_tweet の時点で X が拒否する。
            Emit(onProgress, "uploading", 0);

            // ---- INIT ----
            var initResponse = await client.V11.UploadMediaInitAsync(mediaType, totalBytes, mediaCategory, isLongVideo).ConfigureAwait(false);
            var mediaId = initResponse.Object.Str("media_id_string")
                          ?? initResponse.Object.Str("media_id")
                          ?? throw new InvalidMediaException("media/upload INIT returned no media_id.");

            // ---- APPEND (sequential, one chunk in memory at a time) ----
            var segmentIndex = 0;
            long bytesSent = 0;
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true))
            {
                var buffer = new byte[ChunkSize];
                while (true)
                {
                    var read = await ReadChunkAsync(file, buffer).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    var chunk = new byte[read];
                    Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                    await client.V11.UploadMediaAppendAsync(isLongVideo, mediaId, segmentIndex, chunk).ConfigureAwait(false);

                    segmentIndex++;
                    bytesSent += read;
                    Emit(onProgress, "uploading", (int)(bytesSent * 100 / totalBytes));
                }
            }

            Emit(onProgress, "uploading", 100);

            // ---- FINALIZE ----
            await client.V11.UploadMediaFinalizeAsync(isLongVideo, mediaId).ConfigureAwait(false);

            // ---- STATUS (processing) ----
            if (waitForCompletion)
            {
                Emit(onProgress, "processing", 100);
                while (true)
                {
                    var state = await client.CheckMediaStatusAsync(mediaId, isLongVideo).ConfigureAwait(false);
                    var processingInfo = state.Obj("processing_info");
                    if (processingInfo is null)
                    {
                        break;
                    }

                    if (processingInfo.ContainsKey("error"))
                    {
                        var error = processingInfo["error"];
                        var message = error is JsonObject errorObject
                            ? errorObject.Str("message") ?? errorObject.ToJsonString()
                            : error?.ToString() ?? "unknown error";
                        throw new InvalidMediaException(message);
                    }

                    if (processingInfo.Str("state") == "succeeded")
                    {
                        break;
                    }

                    Emit(onProgress, "processing", 100);
                    var delay = statusCheckInterval ?? processingInfo.Double("check_after_secs") ?? 2.0;
                    await Task.Delay(TimeSpan.FromSeconds(delay)).ConfigureAwait(false);
                }
            }

            return mediaId;
        }

        /// <summary>バッファが埋まるかファイル終端に達するまで読む。</summary>
        private static async Task<int> ReadChunkAsync(FileStream file, byte[] buffer)
        {
            var total = 0;
            while (total < buffer.Length)
            {
                var read = await file.ReadAsync(buffer.AsMemory(total, buffer.Length - total)).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            return total;
        }
    }
}
