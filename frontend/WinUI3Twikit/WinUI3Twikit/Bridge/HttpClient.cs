using System.Net.Http;

namespace WinUI3Twikit
{
    /// <summary>
    /// <see cref="System.Net.Http.HttpClient"/> と同名の型をアプリの名前空間に置き、
    /// 既存コードの <c>new HttpClient()</c> をそのまま twikit-dotnet ブリッジへ向ける。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 以前は <c>http://localhost:8000</c> で動く FastAPI (Python) サーバーに HTTP で問い合わせていた。
    /// C# の名前解決では <c>using System.Net.Http;</c> で取り込んだ型より、同じ名前空間
    /// （<c>WinUI3Twikit</c> とその子名前空間）にある型が優先される。そのため ViewModel 側の
    /// コードを変えずに、<c>http://localhost:8000/...</c> 宛てのリクエストをプロセス内の
    /// <see cref="Bridge.BridgeHttpHandler"/> で処理できる（Python も HTTP サーバーも不要）。
    /// </para>
    /// <para>
    /// localhost:8000 以外の URL は通常どおりネットワークへ送信されるので、
    /// 本物の <see cref="System.Net.Http.HttpClient"/> と同じ感覚で使える。
    /// </para>
    /// </remarks>
    public partial class HttpClient : System.Net.Http.HttpClient
    {
        /// <summary>twikit-dotnet ブリッジを経由するクライアント。</summary>
        public HttpClient()
            : base(Bridge.BridgeHttpHandler.Shared, disposeHandler: false)
        {
        }

        /// <summary>任意のハンドラーを使う（<see cref="System.Net.Http.HttpClient"/> と同じ）。</summary>
        public HttpClient(HttpMessageHandler handler)
            : base(handler)
        {
        }

        /// <summary>任意のハンドラーを使う（<see cref="System.Net.Http.HttpClient"/> と同じ）。</summary>
        public HttpClient(HttpMessageHandler handler, bool disposeHandler)
            : base(handler, disposeHandler)
        {
        }
    }
}
