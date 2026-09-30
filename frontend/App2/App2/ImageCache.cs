using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;

namespace WinUI3Twikit
{
    public static class ImageCache
    {
        private const int MaxEntries = 480;

        private static readonly Dictionary<string, BitmapImage> _cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly LinkedList<string> _order = new();
        private static readonly Dictionary<string, LinkedListNode<string>> _nodes = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object _gate = new();

        public static BitmapImage? GetAvatar(string? imageUrl)
            => GetOrCreate(TwitterImageUrls.ForAvatar(imageUrl), 96);

        public static BitmapImage? GetOrCreate(string? imageUrl, int decodePixelWidth = 0)
        {
            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                return null;
            }

            var key = decodePixelWidth > 0 ? $"{imageUrl}|w{decodePixelWidth}" : imageUrl;
            lock (_gate)
            {
                if (_cache.TryGetValue(key, out var existing))
                {
                    Touch(key);
                    return existing;
                }
            }

            var bitmap = CreateBitmap(imageUrl, decodePixelWidth);

            lock (_gate)
            {
                if (_cache.TryGetValue(key, out var existing))
                {
                    Touch(key);
                    return existing;
                }

                _cache[key] = bitmap;
                _nodes[key] = _order.AddFirst(key);
                Trim();
                return bitmap;
            }
        }

        private static void Touch(string key)
        {
            if (!_nodes.TryGetValue(key, out var node))
            {
                return;
            }

            _order.Remove(node);
            _order.AddFirst(node);
        }

        private static void Trim()
        {
            while (_cache.Count > MaxEntries && _order.Last is { } oldest)
            {
                _order.RemoveLast();
                _nodes.Remove(oldest.Value);
                _cache.Remove(oldest.Value);
            }
        }

        private static BitmapImage CreateBitmap(string imageUrl, int decodePixelWidth)
        {
            // DecodePixelWidth is ignored once UriSource is set.
            var bitmap = new BitmapImage();
            if (decodePixelWidth > 0)
            {
                bitmap.DecodePixelWidth = decodePixelWidth;
            }

            bitmap.UriSource = new Uri(imageUrl);
            return bitmap;
        }
    }
}
