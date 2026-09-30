using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace WinUI3Twikit
{
    internal static class MediaAttachmentHelper
    {
        public const int MaxMediaCount = 4;

        public static readonly string[] AllowedMediaExtensions =
        [
            ".pjp",
            ".jfif",
            ".jpe",
            ".pjpeg",
            ".jpeg",
            ".jpg",
            ".png",
            ".webp",
            ".gif",
            ".m4v",
            ".mp4",
            ".mov"
        ];

        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4",
            ".mov",
            ".m4v"
        };

        public static string MediaFileFilterSpec =>
            string.Join(';', AllowedMediaExtensions.Select(ext => $"*{ext}"));

        public static IntPtr GetMainWindowHandle()
        {
            if (App.MainWindow is null)
            {
                return IntPtr.Zero;
            }

            return WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        }

        public static IReadOnlyList<string> PickMedia(IntPtr hwnd)
        {
            return CommonOpenFileDialog.Show(
                hwnd,
                filters:
                [
                    ("メディアファイル", MediaFileFilterSpec),
                    ("すべてのファイル", "*.*")
                ],
                multiSelect: true,
                title: "メディアを選択",
                initialDirectory: Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        }

        public static bool IsAllowedMediaExtension(string? extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
            {
                return false;
            }

            return AllowedMediaExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        public static bool IsVideoExtension(string? extension)
        {
            return !string.IsNullOrWhiteSpace(extension) && VideoExtensions.Contains(extension);
        }

        public static int AddFromPaths(ObservableCollection<MediaFile> selectedFiles, IEnumerable<string> paths)
        {
            var remaining = MaxMediaCount - selectedFiles.Count;
            if (remaining <= 0)
            {
                return 0;
            }

            var added = 0;
            foreach (var path in paths.Where(p => !string.IsNullOrWhiteSpace(p)).Take(remaining))
            {
                var media = new MediaFile { FilePath = path };
                try
                {
                    var uri = new Uri(path);
                    media.Preview = new BitmapImage(uri);
                    System.Diagnostics.Debug.WriteLine($"Preview loaded: {Path.GetFileName(path)}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Preview failed: {ex.Message}");
                }

                selectedFiles.Add(media);
                added++;
            }

            return added;
        }

        public static async Task<bool> TryAddFromClipboardAsync(ObservableCollection<MediaFile> selectedFiles)
        {
            if (selectedFiles.Count >= MaxMediaCount)
            {
                return false;
            }

            var content = Clipboard.GetContent();
            if (content is null)
            {
                return false;
            }

            if (content.Contains(StandardDataFormats.Bitmap))
            {
                var path = await SaveClipboardBitmapToTempPngAsync(content);
                if (path is not null)
                {
                    AddFromPaths(selectedFiles, [path]);
                    return true;
                }
            }

            if (content.Contains(StandardDataFormats.StorageItems))
            {
                var items = await content.GetStorageItemsAsync();
                var paths = items
                    .OfType<StorageFile>()
                    .Where(f => IsAllowedMediaExtension(f.FileType))
                    .Select(f => f.Path)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .ToList();

                if (paths.Count > 0)
                {
                    AddFromPaths(selectedFiles, paths);
                    return true;
                }
            }

            return false;
        }

        private static async Task<string?> SaveClipboardBitmapToTempPngAsync(DataPackageView content)
        {
            var streamRef = await content.GetBitmapAsync();
            using var input = await streamRef.OpenReadAsync();

            var decoder = await BitmapDecoder.CreateAsync(input);
            var softwareBitmap = await decoder.GetSoftwareBitmapAsync();

            var fileName = $"media_paste_{Guid.NewGuid():N}.png";
            var tempFolder = await StorageFolder.GetFolderFromPathAsync(Path.GetTempPath());
            var file = await tempFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);

            using (var output = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);

                if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8 ||
                    softwareBitmap.BitmapAlphaMode == BitmapAlphaMode.Straight)
                {
                    softwareBitmap = SoftwareBitmap.Convert(
                        softwareBitmap,
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied);
                }

                encoder.SetSoftwareBitmap(softwareBitmap);
                await encoder.FlushAsync();
            }

            return file.Path;
        }
    }
}
