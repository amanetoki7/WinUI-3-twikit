using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace WinUI3Twikit
{
    public class TweetDto
    {
        public string? id { get; set; }
        public string? timeline_entry_id { get; set; }
        public string? text { get; set; }
        public string? created_at { get; set; }
        public string? user_name { get; set; }
        public string? user_screen_name { get; set; }
        public string? user_profile_image { get; set; }
        public bool user_protected { get; set; }
        public bool user_verified { get; set; }
        public int favorite_count { get; set; }
        public int retweet_count { get; set; }
        public int reply_count { get; set; }
        public int view_count { get; set; }
        public bool is_liked { get; set; }
        public bool is_retweeted { get; set; }
        public bool is_retweet { get; set; }
        public string? retweeted_by_name { get; set; }
        public string? retweeted_by_screen_name { get; set; }
        public List<MediaItemDto>? media_items { get; set; }
        public QuotedTweetDto? quoted_tweet { get; set; }
    }

    public class QuotedTweetDto
    {
        public string? id { get; set; }
        public string? text { get; set; }
        public string? created_at { get; set; }
        public string? user_name { get; set; }
        public string? user_screen_name { get; set; }
        public string? user_profile_image { get; set; }
        public bool user_protected { get; set; }
        public bool user_verified { get; set; }
        public List<MediaItemDto>? media_items { get; set; }
        public bool is_unavailable { get; set; }
    }

    public class MediaItemDto
    {
        public string? type { get; set; }
        public string? url { get; set; }
        public string? thumbnail { get; set; }
    }

    public class MediaItem
    {
        public string Type { get; set; } = "image";
        public string Url { get; set; } = string.Empty;
        public string Thumbnail { get; set; } = string.Empty;
        public ImageSource? ThumbnailImage { get; set; }
        public bool IsVideo => Type is "video" or "animated_gif";
        public bool ShowVideoOverlay => IsVideo && !string.IsNullOrWhiteSpace(Thumbnail);
    }

    internal static class MediaItemMapper
    {
        public static MediaItem FromDto(MediaItemDto? dto, int decodePixelWidth)
        {
            var type = dto?.type ?? "image";
            var url = dto?.url ?? string.Empty;
            var thumbnailUrl = type is "video" or "animated_gif"
                ? dto?.thumbnail ?? string.Empty
                : dto?.thumbnail ?? dto?.url ?? string.Empty;
            var listThumbnail = TwitterImageUrls.ForListMedia(thumbnailUrl);

            return new MediaItem
            {
                Type = type,
                Url = url,
                Thumbnail = thumbnailUrl,
                ThumbnailImage = string.IsNullOrWhiteSpace(listThumbnail)
                    ? null
                    : ImageCache.GetOrCreate(listThumbnail, decodePixelWidth)
            };
        }
    }

    public class QuotedTweetViewModel
    {
        public string Id { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string UserScreenName { get; set; } = string.Empty;
        public bool IsUserProtected { get; set; }
        public bool IsUserVerified { get; set; }
        public string CreatedAt { get; set; } = string.Empty;
        public string CreatedAtDisplay => TimeDisplayHelper.FormatDisplay(CreatedAt);
        public string CreatedAtAbsoluteDisplay => TimeDisplayHelper.FormatAbsoluteDisplay(CreatedAt);
        public string CreatedAtRelativeDisplay => TimeDisplayHelper.FormatRelativeDisplay(CreatedAt);
        public ImageSource? UserProfileImage { get; set; }
        public List<MediaItem> MediaItems { get; set; } = [];
        public bool IsUnavailable { get; set; }
        public MediaItem? FirstMediaItem => MediaItems.FirstOrDefault();
        public bool HasMedia => MediaItems.Count > 0;
        public ImageSource? FirstMediaThumbnail => FirstMediaItem?.ThumbnailImage;
        public string FirstMediaUrl => FirstMediaItem?.Url ?? string.Empty;
        public string FirstMediaType => FirstMediaItem?.Type ?? "image";
    }

    public partial class TweetViewModel : INotifyPropertyChanged
    {
        public string Id { get; set; } = string.Empty;
        public string TimelineEntryId { get; set; } = string.Empty;
        public string DedupKey => !string.IsNullOrEmpty(TimelineEntryId) ? TimelineEntryId : Id;
        public ulong SortKey => TimeDisplayHelper.TryParseSnowflakeId(TimelineEntryId)
            ?? TimeDisplayHelper.TryParseSnowflakeId(Id)
            ?? 0UL;
        public string Text { get; set; } = string.Empty;
        public bool HasText => !string.IsNullOrWhiteSpace(Text);
        public string UserName { get; set; } = string.Empty;
        public string UserScreenName { get; set; } = string.Empty;
        public bool IsUserProtected { get; set; }
        public bool IsUserVerified { get; set; }
        public bool IsRetweetEnabled => !IsUserProtected;
        public string CreatedAt { get; set; } = string.Empty;
        public string CreatedAtDisplay => TimeDisplayHelper.FormatDisplay(CreatedAt);
        public string CreatedAtAbsoluteDisplay => TimeDisplayHelper.FormatAbsoluteDisplay(CreatedAt);
        public string CreatedAtRelativeDisplay => TimeDisplayHelper.FormatRelativeDisplay(CreatedAt);
        public ImageSource? UserProfileImage { get; set; }
        public int FavoriteCount { get; set; } = 0;
        public int RetweetCount { get; set; } = 0;
        public int ReplyCount { get; set; } = 0;
        public int ViewCount { get; set; } = 0;
        public string FavoriteCountDisplay => MetricDisplayHelper.Format(FavoriteCount);
        public string RetweetCountDisplay => MetricDisplayHelper.Format(RetweetCount);
        public string ReplyCountDisplay => MetricDisplayHelper.Format(ReplyCount);
        public string ViewCountDisplay => MetricDisplayHelper.Format(ViewCount);
        public List<MediaItem> MediaItems { get; set; } = [];
        public List<MediaItem> QuotedCardMediaItems { get; set; } = [];

        public bool IsLiked { get; set; } = false;
        public bool IsRetweeted { get; set; } = false;
        public bool IsRetweet { get; set; } = false;
        public string RetweetedByName { get; set; } = string.Empty;
        public string RetweetHeaderText => IsRetweet
            ? $"{RetweetedByName}さんがリツイートしました"
            : string.Empty;

        private bool _isReplying = false;
        public bool IsReplying
        {
            get => _isReplying;
            set { _isReplying = value; OnPropertyChanged(nameof(IsReplying)); }
        }

        private string _replyText = string.Empty;
        public string ReplyText
        {
            get => _replyText;
            set
            {
                if (_replyText == value)
                {
                    return;
                }

                _replyText = value;
                OnPropertyChanged(nameof(ReplyText));
            }
        }

        private bool _isReplySending = false;
        public bool IsReplySending
        {
            get => _isReplySending;
            set
            {
                _isReplySending = value;
                OnPropertyChanged(nameof(IsReplySending));
                OnPropertyChanged(nameof(IsReplySendEnabled));
            }
        }

        public bool IsReplySendEnabled => !_isReplySending;

        public TweetViewModel ReplyOwner => this;

        private bool _isQuoting;
        public bool IsQuoting
        {
            get => _isQuoting;
            set { _isQuoting = value; OnPropertyChanged(nameof(IsQuoting)); }
        }

        private string _quoteText = string.Empty;
        public string QuoteText
        {
            get => _quoteText;
            set
            {
                if (_quoteText == value)
                {
                    return;
                }

                _quoteText = value;
                OnPropertyChanged(nameof(QuoteText));
            }
        }

        private bool _isQuoteSending;
        public bool IsQuoteSending
        {
            get => _isQuoteSending;
            set
            {
                _isQuoteSending = value;
                OnPropertyChanged(nameof(IsQuoteSending));
                OnPropertyChanged(nameof(IsQuoteSendEnabled));
                OnPropertyChanged(nameof(CanAddQuoteMedia));
            }
        }

        public bool IsQuoteSendEnabled => !_isQuoteSending;

        public ObservableCollection<MediaFile> QuoteMediaFiles { get; } = [];

        public bool HasQuoteMedia => QuoteMediaFiles.Count > 0;

        public bool CanAddQuoteMedia => IsQuoteSendEnabled && QuoteMediaFiles.Count < MediaAttachmentHelper.MaxMediaCount;

        public string QuoteMediaSummary => HasQuoteMedia
            ? $"{QuoteMediaFiles.Count} 件のメディア"
            : string.Empty;

        public TweetViewModel()
        {
            QuoteMediaFiles.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasQuoteMedia));
                OnPropertyChanged(nameof(QuoteMediaSummary));
                OnPropertyChanged(nameof(CanAddQuoteMedia));
            };
        }

        public TweetViewModel QuoteOwner => this;

        private TweetViewModel? _quotePreviewTweet;
        public TweetViewModel? QuotePreviewTweet
        {
            get => _quotePreviewTweet;
            private set
            {
                _quotePreviewTweet = value;
                OnPropertyChanged(nameof(QuotePreviewTweet));
            }
        }

        public QuotedTweetViewModel? QuotedTweet { get; set; }
        public bool HasQuotedTweet => QuotedTweet != null;

        public bool HasOuterMedia => MediaItems.Count > 0;

        public bool QuotedCardHasMedia => QuotedCardMediaItems.Count > 0;

        public static TweetViewModel FromDto(TweetDto dto)
        {
            var vm = new TweetViewModel
            {
                Id = dto.id ?? string.Empty,
                TimelineEntryId = dto.timeline_entry_id ?? dto.id ?? string.Empty,
                Text = dto.text ?? string.Empty,
                UserName = dto.user_name ?? string.Empty,
                UserScreenName = string.IsNullOrEmpty(dto.user_screen_name)
                    ? string.Empty
                    : "@" + dto.user_screen_name,
                IsUserProtected = dto.user_protected,
                IsUserVerified = dto.user_verified,
                CreatedAt = dto.created_at ?? string.Empty,
                RetweetCount = dto.retweet_count,
                FavoriteCount = dto.favorite_count,
                ReplyCount = dto.reply_count,
                ViewCount = dto.view_count,
                IsLiked = dto.is_liked,
                IsRetweeted = dto.is_retweeted,
                IsRetweet = dto.is_retweet,
                RetweetedByName = dto.retweeted_by_name ?? string.Empty,
                MediaItems = dto.media_items?
                    .Select(m => MediaItemMapper.FromDto(m, 640))
                    .ToList() ?? [],
                UserProfileImage = ImageCache.GetAvatar(dto.user_profile_image)
            };

            if (dto.quoted_tweet != null)
            {
                vm.QuotedTweet = MapQuotedTweet(dto.quoted_tweet);
                FinalizeQuotedCardMedia(vm);
            }

            return vm;
        }

        internal static void FinalizeQuotedCardMedia(TweetViewModel vm)
        {
            if (vm.QuotedTweet is not { IsUnavailable: false })
            {
                vm.QuotedCardMediaItems = [];
                return;
            }

            vm.QuotedCardMediaItems = vm.QuotedTweet.HasMedia
                ? [.. vm.QuotedTweet.MediaItems]
                : [];
        }

        private static QuotedTweetViewModel MapQuotedTweet(QuotedTweetDto dto)
        {
            if (dto.is_unavailable)
            {
                return new QuotedTweetViewModel { IsUnavailable = true };
            }

            var quoted = new QuotedTweetViewModel
            {
                Id = dto.id ?? string.Empty,
                Text = dto.text ?? string.Empty,
                UserName = dto.user_name ?? string.Empty,
                UserScreenName = string.IsNullOrEmpty(dto.user_screen_name)
                    ? string.Empty
                    : "@" + dto.user_screen_name,
                IsUserProtected = dto.user_protected,
                IsUserVerified = dto.user_verified,
                CreatedAt = dto.created_at ?? string.Empty,
                MediaItems = dto.media_items?
                    .Select(m => MediaItemMapper.FromDto(m, 640))
                    .ToList() ?? [],
                UserProfileImage = ImageCache.GetAvatar(dto.user_profile_image)
            };
            return quoted;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public void ToggleLike()
        {
            IsLiked = !IsLiked;
            FavoriteCount += IsLiked ? 1 : -1;
            OnPropertyChanged(nameof(IsLiked));
            OnPropertyChanged(nameof(FavoriteCount));
            OnPropertyChanged(nameof(FavoriteCountDisplay));
        }

        public void ToggleRetweet()
        {
            IsRetweeted = !IsRetweeted;
            RetweetCount += IsRetweeted ? 1 : -1;
            OnPropertyChanged(nameof(IsRetweeted));
            OnPropertyChanged(nameof(RetweetCount));
            OnPropertyChanged(nameof(RetweetCountDisplay));
        }

        public void NoteReplyPosted()
        {
            ReplyCount++;
            OnPropertyChanged(nameof(ReplyCount));
            OnPropertyChanged(nameof(ReplyCountDisplay));
        }

        public void BeginQuoting()
        {
            if (IsUserProtected)
            {
                return;
            }

            IsReplying = false;
            ReplyText = string.Empty;
            QuoteText = string.Empty;
            QuoteMediaFiles.Clear();

            var preview = new TweetViewModel
            {
                QuotedTweet = CreateQuotedPreviewFrom(this),
            };
            FinalizeQuotedCardMedia(preview);
            QuotePreviewTweet = preview;
            IsQuoting = true;
        }

        public void CancelQuoting()
        {
            if (IsQuoteSending)
            {
                return;
            }

            IsQuoting = false;
            QuoteText = string.Empty;
            QuoteMediaFiles.Clear();
            QuotePreviewTweet = null;
        }

        internal static List<MediaItem> CreateMediaItemsFromAttachments(IEnumerable<MediaFile> files)
        {
            var items = new List<MediaItem>();
            foreach (var file in files)
            {
                if (string.IsNullOrWhiteSpace(file.FilePath))
                {
                    continue;
                }

                string url;
                try
                {
                    url = new Uri(file.FilePath).AbsoluteUri;
                }
                catch
                {
                    continue;
                }

                var isVideo = MediaAttachmentHelper.IsVideoExtension(Path.GetExtension(file.FilePath));
                items.Add(new MediaItem
                {
                    Type = isVideo ? "video" : "image",
                    Url = url,
                    Thumbnail = url,
                    ThumbnailImage = file.Preview,
                });
            }

            return items;
        }

        private static QuotedTweetViewModel CreateQuotedPreviewFrom(TweetViewModel source)
        {
            return new QuotedTweetViewModel
            {
                Id = source.Id,
                Text = source.Text,
                UserName = source.UserName,
                UserScreenName = source.UserScreenName,
                IsUserProtected = source.IsUserProtected,
                IsUserVerified = source.IsUserVerified,
                CreatedAt = source.CreatedAt,
                MediaItems = [.. source.MediaItems
                    .Select(m => new MediaItem
                    {
                        Type = m.Type,
                        Url = m.Url,
                        Thumbnail = m.Thumbnail,
                        ThumbnailImage = m.ThumbnailImage,
                    })],
                UserProfileImage = source.UserProfileImage,
            };
        }

        public QuotedTweetViewModel ToQuotedPreview()
            => CreateQuotedPreviewFrom(this);
    }
}
