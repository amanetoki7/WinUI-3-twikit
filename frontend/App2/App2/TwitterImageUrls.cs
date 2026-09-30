using System;
using System.Collections.Generic;

namespace WinUI3Twikit
{
    internal static class TwitterImageUrls
    {
        public static string? ForAvatar(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return url;
            }

            return url
                .Replace("_400x400.", "_200x200.", StringComparison.Ordinal)
                .Replace("_original.", "_200x200.", StringComparison.Ordinal);
        }

        public static string? ForListMedia(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return url;
            }

            if (url.Contains("/profile_images/", StringComparison.OrdinalIgnoreCase))
            {
                return ForAvatar(url);
            }

            return ToNamedSize(url, "small");
        }

        private static string ToNamedSize(string url, string size)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return url;
            }

            if (!uri.Host.Equals("pbs.twimg.com", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            var path = uri.AbsolutePath;
            if (path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            if (!path.Contains("/media/", StringComparison.OrdinalIgnoreCase)
                && !path.Contains("video_thumb", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            string[] legacySuffixes = [":orig", ":large", ":medium", ":small", ":thumb"];
            foreach (var suffix in legacySuffixes)
            {
                if (path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    path = path[..^suffix.Length];
                    break;
                }
            }

            string? format = null;
            var dot = path.LastIndexOf('.');
            var slash = path.LastIndexOf('/');
            if (dot > slash)
            {
                var ext = path[(dot + 1)..];
                if (ext.Equals("jpg", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals("jpeg", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals("png", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals("webp", StringComparison.OrdinalIgnoreCase))
                {
                    format = ext.Equals("jpeg", StringComparison.OrdinalIgnoreCase) ? "jpg" : ext.ToLowerInvariant();
                    path = path[..dot];
                }
            }

            var query = BuildQuery(uri.Query, format, size);
            var builder = new UriBuilder(uri)
            {
                Path = path,
                Query = query
            };
            return builder.Uri.AbsoluteUri;
        }

        private static string BuildQuery(string existingQuery, string? formatIfMissing, string size)
        {
            var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var trimmed = existingQuery.StartsWith('?') ? existingQuery[1..] : existingQuery;
            if (!string.IsNullOrEmpty(trimmed))
            {
                foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = part.IndexOf('=');
                    if (eq < 0)
                    {
                        pairs[Uri.UnescapeDataString(part)] = string.Empty;
                        continue;
                    }

                    pairs[Uri.UnescapeDataString(part[..eq])] = Uri.UnescapeDataString(part[(eq + 1)..]);
                }
            }

            if (!pairs.ContainsKey("format") && !string.IsNullOrEmpty(formatIfMissing))
            {
                pairs["format"] = formatIfMissing;
            }

            pairs["name"] = size;

            var pieces = new List<string>(pairs.Count);
            if (pairs.TryGetValue("format", out var formatValue))
            {
                pieces.Add("format=" + Uri.EscapeDataString(formatValue));
            }

            pieces.Add("name=" + Uri.EscapeDataString(size));
            foreach (var pair in pairs)
            {
                if (pair.Key.Equals("format", StringComparison.OrdinalIgnoreCase)
                    || pair.Key.Equals("name", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                pieces.Add(Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value));
            }

            return string.Join("&", pieces);
        }
    }
}
