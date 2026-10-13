using System.Collections.Generic;
using System.Linq;

namespace WinUI3Twikit
{
    /// <summary>ブリッジが返す <c>urls</c> の 1 件（t.co と展開後 URL の対応）。</summary>
    public class UrlEntityDto
    {
        public string? url { get; set; }
        public string? expanded_url { get; set; }
        public string? display_url { get; set; }
    }

    /// <summary>本文中の t.co リンクと展開後 URL の対応。リンク化の表示と遷移先に使う。</summary>
    public sealed class TweetUrlEntity
    {
        public string ShortUrl { get; init; } = string.Empty;

        public string ExpandedUrl { get; init; } = string.Empty;

        public string DisplayUrl { get; init; } = string.Empty;

        public static TweetUrlEntity? FromDto(UrlEntityDto? dto)
        {
            if (dto is null || string.IsNullOrEmpty(dto.url))
            {
                return null;
            }

            return new TweetUrlEntity
            {
                ShortUrl = dto.url,
                ExpandedUrl = string.IsNullOrEmpty(dto.expanded_url) ? dto.url : dto.expanded_url,
                DisplayUrl = dto.display_url ?? string.Empty,
            };
        }

        public static List<TweetUrlEntity> ListFromDto(IEnumerable<UrlEntityDto>? dtos)
            => dtos?.Select(FromDto).OfType<TweetUrlEntity>().ToList() ?? [];
    }
}
