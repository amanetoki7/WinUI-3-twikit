using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.UI;
using WinUI3Twikit.Bridge;

namespace WinUI3Twikit
{
    /// <summary>
    /// バックエンドの起動・停止・再起動。
    /// 以前は Python の FastAPI サーバー（uvicorn）を子プロセスとして起動していたが、
    /// 現在は twikit-dotnet をプロセス内で直接使う（<see cref="TwikitBridge"/>）ため、
    /// ここでは Cookie の読み込みとログイン確認だけを行い、左ペインの状態表示を更新する。
    /// 公開メソッドの名前と引数は以前のまま（MainWindow / SettingsPage からの呼び出しを変えないため）。
    /// </summary>
    public class ServerManager
    {
        public static async Task StartServerAsync(TextBlock statusTextBlock)
        {
            UpdateStatus(statusTextBlock, "Twikit: Starting…", Colors.Yellow);

            try
            {
                var status = await TwikitBridge.StartAsync();
                UpdateStatus(statusTextBlock, status.Message, status.Ok ? Colors.LimeGreen : Colors.Red);
            }
            catch (Exception ex)
            {
                UpdateStatus(statusTextBlock, $"Twikit Error: {ex.Message}", Colors.Red);
                Debug.WriteLine($"Bridge start failed: {ex}");
            }
        }

        private static void UpdateStatus(TextBlock? textBlock, string message, Color color)
        {
            if (App.IsShuttingDown || textBlock == null)
            {
                return;
            }

            textBlock.Text = message;
            textBlock.Foreground = new SolidColorBrush(color);
        }

        public static void StopServer()
        {
            TwikitBridge.Stop();
        }

        /// <summary>
        /// auth_token / ct0 など cookie 更新後に反映するため、セッションを作り直して起動処理をやり直す。
        /// </summary>
        public static async Task RestartServerAsync(TextBlock statusTextBlock)
        {
            UpdateStatus(statusTextBlock, "Twikit: Restarting…", Colors.Yellow);

            try
            {
                var status = await TwikitBridge.RestartAsync();
                UpdateStatus(statusTextBlock, status.Message, status.Ok ? Colors.LimeGreen : Colors.Red);
            }
            catch (Exception ex)
            {
                UpdateStatus(statusTextBlock, $"Twikit Error: {ex.Message}", Colors.Red);
                Debug.WriteLine($"Bridge restart failed: {ex}");
            }
        }
    }
}
