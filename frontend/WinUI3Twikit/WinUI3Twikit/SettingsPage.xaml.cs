using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using WinUI3Twikit.Bridge;

namespace WinUI3Twikit
{
    public sealed partial class SettingsPage : Page
    {
        private readonly string jsonPath = ResolveCookiesPath();

        // 保存先はブリッジ（RepositoryPaths）と同じ解決順。単一 exe では exe の隣の data\cookies.json になる。
        private static string ResolveCookiesPath() => RepositoryPaths.CookiesPath;

        public SettingsPage()
        {
            this.InitializeComponent();

            LoadJsonValues();
        }

        private void LoadJsonValues()
        {
            // ファイルが無い・壊れているときは何もしない（例外で落とさない）
            if (!CookiesFile.TryRead(jsonPath, out var auth, out var ct0))
            {
                return;
            }

            // TextBox に初期値をセット
            TextBoxA.Text = auth;
            TextBoxB.Text = ct0;
        }

        private async void OnApplyClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // フォルダーが無ければ作って保存する（単一 exe の初回は exe の隣に data\ がまだ無い）
                CookiesFile.Save(jsonPath, TextBoxA.Text, TextBoxB.Text);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cookie 保存失敗: {ex}");
                await ShowMessageAsync("保存に失敗しました", $"{ex.Message}\n\n保存先: {jsonPath}");
                return;
            }

            // 保存した Cookie でログインし直し、左ペインの状態表示を更新する（「サーバーを再起動」と同じ処理）
            if (App.MainWindow is MainWindow mainWindow)
            {
                await mainWindow.RestartServerAsync();
            }
        }

        private async Task ShowMessageAsync(string title, string message)
        {
            if (XamlRoot is null)
            {
                return;
            }

            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "閉じる",
                XamlRoot = XamlRoot,
            };

            try
            {
                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ダイアログ表示失敗: {ex.Message}");
            }
        }

        private async void OnRestartServerClick(object sender, RoutedEventArgs e)
        {
            if (App.MainWindow is not MainWindow mainWindow)
            {
                return;
            }

            RestartServerButton.IsEnabled = false;
            try
            {
                await mainWindow.RestartServerAsync();
            }
            finally
            {
                if (!App.IsShuttingDown)
                {
                    RestartServerButton.IsEnabled = true;
                }
            }
        }
    }
}
