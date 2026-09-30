using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System;

namespace WinUI3Twikit
{
    public sealed partial class SettingsPage : Page
    {
        private readonly string jsonPath = ResolveCookiesPath();

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private static string ResolveCookiesPath()
        {
            var configured = Environment.GetEnvironmentVariable("COOKIES_FILE");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }

            var root = Environment.GetEnvironmentVariable("WINUI3TWIKIT_ROOT");
            if (!string.IsNullOrWhiteSpace(root))
            {
                return Path.Combine(root, "data", "cookies.json");
            }

            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "data", "cookies.json");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return Path.Combine(AppContext.BaseDirectory, "data", "cookies.json");
        }

        public SettingsPage()
        {
            this.InitializeComponent();

            LoadJsonValues();
        }

        private void LoadJsonValues()
        {
            if (!File.Exists(jsonPath))
            {
                return; // ファイルが無ければ何もしない
            }

            var json = File.ReadAllText(jsonPath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

            if (dict == null) return;

            // TextBox に初期値をセット
            if (dict.TryGetValue("auth_token", out var auth))
            {
                TextBoxA.Text = auth;
            }

            if (dict.TryGetValue("ct0", out var ct0))
            {
                TextBoxB.Text = ct0;
            }
        }

        private void OnApplyClick(object sender, RoutedEventArgs e)
        {
            // 保存処理（前回のコードと同じ）
            var dict = new Dictionary<string, string>
            {
                ["auth_token"] = TextBoxA.Text,
                ["ct0"] = TextBoxB.Text
            };

            File.WriteAllText(jsonPath, JsonSerializer.Serialize(dict, JsonOptions));
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
