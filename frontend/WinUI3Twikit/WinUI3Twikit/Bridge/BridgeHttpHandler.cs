using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace WinUI3Twikit.Bridge
{
    /// <summary>
    /// 旧 FastAPI バックエンド（<c>http://localhost:8000</c>）宛てのリクエストを、
    /// プロセス内で twikit-dotnet を呼び出して処理する <see cref="HttpMessageHandler"/>。
    /// </summary>
    /// <remarks>
    /// ルーティングと各エンドポイントの実装は <see cref="LocalApi"/>（旧 <c>backend/api.py</c>）にある。
    /// それ以外の URL（画像など）は通常の <see cref="SocketsHttpHandler"/> へそのまま流す。
    /// </remarks>
    internal sealed partial class BridgeHttpHandler : HttpMessageHandler
    {
        /// <summary>旧バックエンドが待ち受けていたポート。ViewModel 側の URL と一致させる。</summary>
        public const int Port = 8000;

        public static BridgeHttpHandler Shared { get; } = new();

        private readonly HttpMessageInvoker _passThrough = new(new SocketsHttpHandler(), disposeHandler: true);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri;
            if (uri is null || !IsBridgeUri(uri))
            {
                return await _passThrough.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }

            var stopwatch = Stopwatch.StartNew();
            HttpResponseMessage response;
            try
            {
                // UI スレッドから呼ばれても、twikit の処理や JSON 化はスレッドプールで行う。
                response = await Task.Run(() => LocalApi.DispatchAsync(request, cancellationToken), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Bridge: {request.Method} {uri.PathAndQuery} failed: {ex}");
                response = JsonResponses.Error(HttpStatusCode.InternalServerError, ex.Message);
            }

            response.RequestMessage = request;
            Debug.WriteLine($"Bridge: {request.Method} {uri.PathAndQuery} -> {(int)response.StatusCode} ({stopwatch.ElapsedMilliseconds} ms)");
            return response;
        }

        private static bool IsBridgeUri(Uri uri)
            => uri.IsAbsoluteUri
               && uri.IsLoopback
               && uri.Port == Port
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _passThrough.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
