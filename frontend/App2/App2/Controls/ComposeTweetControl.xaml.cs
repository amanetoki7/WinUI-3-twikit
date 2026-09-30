using CommunityToolkit.WinUI.Notifications;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace WinUI3Twikit.Controls
{
    public sealed partial class ComposeTweetControl : UserControl
    {
        public ObservableCollection<MediaFile> SelectedFiles { get; } = [];

        public bool IsPosting { get; private set; }

        public event EventHandler? TweetPosted;

        private sealed class TweetStartResponse
        {
            [JsonPropertyName("job_id")]
            public string? job_id { get; set; }

            [JsonPropertyName("state")]
            public string? state { get; set; }
        }

        private sealed class TweetJobItemDto
        {
            [JsonPropertyName("path")]
            public string? path { get; set; }

            [JsonPropertyName("file_name")]
            public string? file_name { get; set; }

            [JsonPropertyName("phase")]
            public string? phase { get; set; }

            [JsonPropertyName("percent")]
            public int percent { get; set; }

            [JsonPropertyName("error")]
            public string? error { get; set; }
        }

        private sealed class TweetJobStatusDto
        {
            [JsonPropertyName("job_id")]
            public string? job_id { get; set; }

            [JsonPropertyName("state")]
            public string? state { get; set; }

            [JsonPropertyName("message")]
            public string? message { get; set; }

            [JsonPropertyName("tweet_id")]
            public string? tweet_id { get; set; }

            [JsonPropertyName("items")]
            public List<TweetJobItemDto>? items { get; set; }
        }

        public ComposeTweetControl()
        {
            InitializeComponent();
        }

        public void FocusInput()
        {
            InputBox.Focus(FocusState.Programmatic);
        }

        private void RemoveMedia_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is MediaFile media)
            {
                SelectedFiles.Remove(media);
                UpdateSelectedMediaInfo();
            }
        }

        private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            int len = InputBox.Text?.Length ?? 0;
            CountText.Text = $"{len} 文字";
        }

        private void InputBox_CtrlEnterInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            if (SendButton?.IsEnabled == true)
            {
                OnSendClick(SendButton, new RoutedEventArgs());
            }
            args.Handled = true;
        }

        private void UpdateSelectedMediaInfo()
        {
            if (SelectedFiles.Count > 0)
            {
                long totalBytes = 0;
                foreach (var media in SelectedFiles)
                {
                    try
                    {
                        var fileInfo = new FileInfo(media.FilePath);
                        totalBytes += fileInfo.Length;
                    }
                    catch { }
                }

                string countText = $"{SelectedFiles.Count} 件のメディア";
                string sizeText = FormatFileSize(totalBytes);

                SelectedMediaCount.Text = $"{countText} ({sizeText})";
            }
            else
            {
                SelectedMediaCount.Text = "";
            }
        }

        private static string FormatFileSize(long bytes)
        {
            const long KB = 1024;
            const long MB = KB * 1024;
            const long GB = MB * 1024;

            return bytes switch
            {
                < KB => $"{bytes} B",
                < MB => $"{(double)bytes / KB:F1} KB",
                < GB => $"{(double)bytes / MB:F1} MB",
                _ => $"{(double)bytes / GB:F2} GB"
            };
        }

        private static readonly TimeSpan JobPollInterval = TimeSpan.FromSeconds(1);

        private void OnAddMediaClick(object sender, RoutedEventArgs e)
        {
            var hwnd = MediaAttachmentHelper.GetMainWindowHandle();
            MediaAttachmentHelper.AddFromPaths(SelectedFiles, MediaAttachmentHelper.PickMedia(hwnd));
            UpdateSelectedMediaInfo();
        }

        private async void InputBox_Paste(object sender, TextControlPasteEventArgs e)
        {
            try
            {
                if (await MediaAttachmentHelper.TryAddFromClipboardAsync(SelectedFiles))
                {
                    UpdateSelectedMediaInfo();
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Paste media failed: {ex.Message}");
            }
        }

        private async void OnSendClick(object sender, RoutedEventArgs e)
        {
            var text = InputBox.Text.Replace("\r", "\r\n");
            if (string.IsNullOrWhiteSpace(text) && SelectedFiles.Count == 0)
                return;

            App.MainWindow?.ShowLoading(true);
            StartPosting();

            var success = await SendTweetWithMediaAsync(text);

            EndPosting();
            App.MainWindow?.ShowLoading(false);

            if (success)
            {
                InputBox.Text = string.Empty;
                SelectedFiles.Clear();
                SelectedMediaCount.Text = "";
                CountText.Text = "0 文字";
                TweetPosted?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// パス渡しでジョブを開始し、1秒間隔で進捗をポーリングする。
        /// </summary>
        private async Task<bool> SendTweetWithMediaAsync(string text)
        {
            var paths = SelectedFiles
                .Select(m => m.FilePath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToArray();

            foreach (var media in SelectedFiles)
            {
                media.ApplyJobProgress(paths.Length == 0 ? "done" : "waiting", 0, null);
            }

            var payload = new { text, paths };
            var json = JsonSerializer.Serialize(payload);
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromHours(2)
            };

            try
            {
                using var startContent = new StringContent(json, Encoding.UTF8, "application/json");
                var startResponse = await httpClient.PostAsync("http://localhost:8000/tweet/start", startContent);
                var startJson = await startResponse.Content.ReadAsStringAsync();

                if (!startResponse.IsSuccessStatusCode)
                {
                    OutputTextBlock.Text = $"ツイート失敗: {(int)startResponse.StatusCode} {startJson}";
                    return false;
                }

                var start = JsonSerializer.Deserialize<TweetStartResponse>(startJson);
                var jobId = start?.job_id;
                if (string.IsNullOrWhiteSpace(jobId))
                {
                    OutputTextBlock.Text = "ツイート失敗: job_id がありません";
                    return false;
                }

                OutputTextBlock.Text = "投稿処理中...";

                while (true)
                {
                    var statusResponse = await httpClient.GetAsync($"http://localhost:8000/tweet/jobs/{jobId}");
                    var statusJson = await statusResponse.Content.ReadAsStringAsync();

                    if (!statusResponse.IsSuccessStatusCode)
                    {
                        OutputTextBlock.Text = $"ツイート失敗: {(int)statusResponse.StatusCode} {statusJson}";
                        return false;
                    }

                    var status = JsonSerializer.Deserialize<TweetJobStatusDto>(statusJson);
                    if (status is null)
                    {
                        OutputTextBlock.Text = "ツイート失敗: ジョブ状態を読めません";
                        return false;
                    }

                    ApplyJobStatusToUi(status);

                    var state = status.state ?? "";
                    if (state is "succeeded")
                    {
                        var message = string.IsNullOrWhiteSpace(status.message)
                            ? "ツイート完了"
                            : status.message!;
                        OutputTextBlock.Text = message;
                        try
                        {
                            new ToastContentBuilder().AddText(message).Show();
                        }
                        catch { }
                        return true;
                    }

                    if (state is "failed")
                    {
                        var message = string.IsNullOrWhiteSpace(status.message)
                            ? "ツイート失敗"
                            : status.message!;
                        OutputTextBlock.Text = $"ツイート失敗: {message}";
                        return false;
                    }

                    await Task.Delay(JobPollInterval);
                }
            }
            catch (Exception ex)
            {
                OutputTextBlock.Text = $"ツイート失敗: {ex.Message}";
                return false;
            }
        }

        private void ApplyJobStatusToUi(TweetJobStatusDto status)
        {
            var items = status.items ?? [];
            if (items.Count == 0)
                return;

            // Match by path (case-insensitive on Windows).
            var byPath = items
                .Where(i => !string.IsNullOrWhiteSpace(i.path))
                .GroupBy(i => i.path!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < SelectedFiles.Count; i++)
            {
                var media = SelectedFiles[i];
                if (byPath.TryGetValue(media.FilePath, out var dto))
                {
                    media.ApplyJobProgress(dto.phase, dto.percent, dto.error);
                }
                else if (i < items.Count)
                {
                    var dto2 = items[i];
                    media.ApplyJobProgress(dto2.phase, dto2.percent, dto2.error);
                }
            }
        }

        private void StartPosting()
        {
            IsPosting = true;

            if (SendButton != null)
            {
                SendButton.IsEnabled = false;
                SendButton.Content = "ツイート中...";
            }

            if (PostingProgressRing != null)
            {
                PostingProgressRing.IsActive = true;
                PostingProgressRing.Visibility = Visibility.Visible;
            }
        }

        private void EndPosting()
        {
            IsPosting = false;

            if (SendButton != null)
            {
                SendButton.IsEnabled = true;
                SendButton.Content = "ツイートする";
            }

            if (PostingProgressRing != null)
            {
                PostingProgressRing.IsActive = false;
                PostingProgressRing.Visibility = Visibility.Collapsed;
            }
        }
    }
}
