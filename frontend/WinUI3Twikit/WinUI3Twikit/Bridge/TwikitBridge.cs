using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Twikit;

namespace WinUI3Twikit.Bridge
{
    /// <summary>
    /// ブリッジの起動・停止・再起動。旧 <see cref="ServerManager"/> が uvicorn を起動していた処理の置き換えで、
    /// Cookie を読み込んでログインできることを確認する。
    /// </summary>
    internal static class TwikitBridge
    {
        private static readonly object Gate = new();
        private static TaskCompletionSource<bool>? _startup;

        public static bool IsReady { get; private set; }

        public static string? AuthenticatedScreenName { get; private set; }

        public static async Task<BridgeStatus> StartAsync()
        {
            var startup = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (Gate)
            {
                _startup = startup;
                IsReady = false;
            }

            try
            {
                ActionQueue.Instance.StartWorker();
                var status = await StartCoreAsync().ConfigureAwait(false);
                IsReady = status.Ok;
                Debug.WriteLine($"TwikitBridge: {status.Message}");
                return status;
            }
            finally
            {
                startup.TrySetResult(true);
            }
        }

        private static async Task<BridgeStatus> StartCoreAsync()
        {
            var cookiesPath = RepositoryPaths.CookiesPath;
            if (!File.Exists(cookiesPath))
            {
                Debug.WriteLine($"TwikitBridge: cookies file not found: {cookiesPath}");
                return BridgeStatus.Failed("Twikit: cookies.json がありません ❌");
            }

            try
            {
                TwikitSession.Login();
            }
            catch (Exception ex)
            {
                return BridgeStatus.Failed($"Twikit: Cookie 読み込み失敗 ❌ {ex.Message}");
            }

            try
            {
                var screenName = await TwikitSession.GetAuthenticatedScreenNameAsync().ConfigureAwait(false);
                AuthenticatedScreenName = screenName;
                return BridgeStatus.Succeeded($"Twikit: Ready ✅ (@{screenName})");
            }
            catch (Exception ex) when (ex is InvalidSessionException
                                       or UnauthorizedException
                                       or ForbiddenException
                                       or AccountLockedException
                                       or AccountSuspendedException)
            {
                Debug.WriteLine($"TwikitBridge: login check failed: {ex.Message}");
                return BridgeStatus.Failed("Twikit: Cookie が無効です ❌（設定で auth_token / ct0 を更新）");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TwikitBridge: login check failed: {ex}");
                return BridgeStatus.Failed($"Twikit: 接続失敗 ❌ {ex.Message}");
            }
        }

        /// <summary>
        /// 起動処理（Cookie 読み込みと最初のハンドシェイク）が終わるまで待つ。
        /// 起動前に ViewModel からリクエストが来ても、ハンドシェイクが二重に走らないようにする。
        /// </summary>
        public static async Task WaitForStartupAsync(CancellationToken cancellationToken)
        {
            Task? startup;
            lock (Gate)
            {
                startup = _startup?.Task;
            }

            if (startup is null || startup.IsCompleted)
            {
                return;
            }

            await Task.WhenAny(startup, Task.Delay(TimeSpan.FromSeconds(60), cancellationToken)).ConfigureAwait(false);
        }

        public static void Stop()
        {
            lock (Gate)
            {
                IsReady = false;
            }
        }

        /// <summary>Cookie 更新後の反映用。セッションを作り直して起動処理をやり直す。</summary>
        public static Task<BridgeStatus> RestartAsync()
        {
            TwikitSession.Reset();
            AuthenticatedScreenName = null;
            return StartAsync();
        }
    }

    internal readonly record struct BridgeStatus(bool Ok, string Message)
    {
        public static BridgeStatus Succeeded(string message) => new(true, message);

        public static BridgeStatus Failed(string message) => new(false, message);
    }
}
