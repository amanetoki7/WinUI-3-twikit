using Microsoft.UI.Xaml.Media.Imaging;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace WinUI3Twikit
{
    public partial class MediaFile : INotifyPropertyChanged
    {
        public string FilePath { get; set; } = string.Empty;

        public string FileName =>
            string.IsNullOrWhiteSpace(FilePath) ? string.Empty : Path.GetFileName(FilePath);

        private BitmapImage? _preview;
        public BitmapImage? Preview
        {
            get => _preview;
            set
            {
                if (_preview == value) return;
                _preview = value;
                OnPropertyChanged();
            }
        }

        private string _progressText = string.Empty;
        public string ProgressText
        {
            get => _progressText;
            set
            {
                if (_progressText == value) return;
                _progressText = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>
        /// phase: pending|waiting|uploading|processing|done|failed
        /// </summary>
        public void ApplyJobProgress(string? phase, int percent, string? error)
        {
            var name = string.IsNullOrEmpty(FileName) ? "メディア" : FileName;
            ProgressText = (phase ?? "").ToLowerInvariant() switch
            {
                "waiting" or "pending" => $"{name}: 待機中",
                "uploading" => $"{name}: アップロード中 ({System.Math.Clamp(percent, 0, 100)}%)",
                "processing" => $"{name}: 処理中",
                "done" => $"{name}: 処理完了",
                "failed" => string.IsNullOrWhiteSpace(error)
                    ? $"{name}: 失敗"
                    : $"{name}: 失敗",
                _ => ProgressText
            };
        }

        public void ClearProgress() => ProgressText = string.Empty;
    }
}
