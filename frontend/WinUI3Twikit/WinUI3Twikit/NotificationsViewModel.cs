using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace WinUI3Twikit
{
    public partial class NotificationsViewModel : INotifyPropertyChanged, ITweetListActions
    {
        private readonly Dictionary<string, ObservableCollection<NotificationViewModel>> _notificationsByType = new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _hasMoreByType = new(StringComparer.Ordinal)
        {
            ["all"] = true,
            ["mentions"] = true,
        };
        private readonly Dictionary<string, double> _scrollByType = new(StringComparer.Ordinal);

        public ObservableCollection<NotificationViewModel> Notifications => ListFor(CurrentNotificationType);

        private string _currentNotificationType = "all";
        public string CurrentNotificationType => _currentNotificationType;

        private readonly HttpClient _httpClient = new();
        private bool _isLoading = false;
        private bool _isLoadingMore = false;
        private bool _drainingQueue;
        private string? _inflightType;
        private string? _queuedType;

        public double ScrollVerticalOffset
        {
            get => _scrollByType.GetValueOrDefault(_currentNotificationType);
            set => _scrollByType[_currentNotificationType] = value;
        }

        private static string NormalizeNotificationType(string? type)
            => string.Equals(type, "mentions", StringComparison.OrdinalIgnoreCase) ? "mentions" : "all";

        private ObservableCollection<NotificationViewModel> ListFor(string type)
        {
            if (!_notificationsByType.TryGetValue(type, out var list))
            {
                list = [];
                _notificationsByType[type] = list;
            }

            return list;
        }

        private bool HasMoreFor(string type) => _hasMoreByType.GetValueOrDefault(type, true);

        private void SetHasMore(string type, bool value)
        {
            _hasMoreByType[type] = value;
            if (string.Equals(type, _currentNotificationType, StringComparison.Ordinal))
            {
                OnPropertyChanged(nameof(HasMore));
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(nameof(IsLoading)); }
        }

        public bool IsLoadingMore
        {
            get => _isLoadingMore;
            set { _isLoadingMore = value; OnPropertyChanged(nameof(IsLoadingMore)); }
        }

        public bool HasMore => HasMoreFor(_currentNotificationType);

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public async Task LoadNotificationsAsync()
        {
            var type = _currentNotificationType;
            IsLoading = true;
            try
            {
                ListFor(type).Clear();
                SetHasMore(type, true);
                await LoadMoreNotificationsAsync(refresh: true, notificationType: type);
            }
            finally
            {
                IsLoading = false;
            }

            await DrainQueuedTabAsync();
        }

        /// <summary>
        /// 表示中のタブを切り替える。空でも取得済みでも最新から取り直す。カーソルは送らない。
        /// 読み込み中なら、終わってから切り替え先だけ取得する。
        /// </summary>
        public async Task SwitchNotificationTypeAsync(string newType)
        {
            var type = NormalizeNotificationType(newType);
            if (string.Equals(type, _currentNotificationType, StringComparison.Ordinal))
            {
                return;
            }

            _currentNotificationType = type;
            OnPropertyChanged(nameof(Notifications));
            OnPropertyChanged(nameof(HasMore));

            if (IsLoading || IsLoadingMore)
            {
                _queuedType = string.Equals(type, _inflightType, StringComparison.Ordinal) ? null : type;
                return;
            }

            await LoadTabFromLatestAsync(type);
        }

        private async Task LoadTabFromLatestAsync(string type)
        {
            if (ListFor(type).Count == 0)
            {
                await LoadNotificationsAsync();
                return;
            }

            await LoadMoreNotificationsAsync(refresh: true, notificationType: type);
        }

        /// <summary>
        /// 通知を取得
        /// </summary>
        /// <param name="refresh">true: 最新から取得 / false: 続きを取得</param>
        /// <param name="notificationType">取得先。省略時は呼び出した時点の表示タブ。</param>
        public async Task LoadMoreNotificationsAsync(bool refresh = false, string? notificationType = null)
        {
            var type = NormalizeNotificationType(notificationType ?? _currentNotificationType);
            if (IsLoadingMore || (!refresh && !HasMoreFor(type))) return;

            // 既に件数が残っているタブの最新取得は、下端の続きカーソルを捨てない。
            var keepCursor = refresh && ListFor(type).Count > 0;
            if (refresh && !keepCursor)
            {
                SetHasMore(type, true);
            }

            IsLoadingMore = true;
            _inflightType = type;

            try
            {
                var refreshText = refresh ? "true" : "false";
                var keepCursorText = keepCursor ? "true" : "false";
                var url = $"http://localhost:8000/notifications?count=20&refresh={refreshText}&type={type}&keepCursor={keepCursorText}";
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var newNotifs = JsonSerializer.Deserialize<List<NotificationDto>>(json);
                var list = ListFor(type);

                if (newNotifs == null || newNotifs.Count == 0)
                {
                    if (!refresh)
                    {
                        SetHasMore(type, false);
                    }
                }
                else
                {
                    int added = 0;
                    foreach (var dto in newNotifs)
                    {
                        if (list.Any(n => n.Id == dto.id)) continue;

                        var vm = new NotificationViewModel
                        {
                            Id = dto.id ?? "",
                            Type = dto.type ?? "",
                            Text = dto.text ?? "",
                            ActorName = dto.actor_name ?? "",
                            ActorScreenName = dto.actor_screen_name ?? "",
                            CreatedAt = dto.created_at ?? "",
                            TargetTweetText = dto.target_tweet_text ?? "",
                            IsActorProtected = dto.user_protected,
                            IsActorVerified = dto.user_verified,
                            ReplyToScreenName = dto.reply_to_screen_name ?? ""
                        };

                        if (vm.IsTweetCard)
                        {
                            vm.ActionTweet = new TweetViewModel
                            {
                                Id = vm.Id,
                                Text = vm.Text,
                                UserName = vm.ActorName,
                                UserScreenName = string.IsNullOrEmpty(vm.ActorScreenName)
                                    ? string.Empty
                                    : "@" + vm.ActorScreenName,
                                CreatedAt = vm.CreatedAt,
                                ReplyCount = dto.reply_count,
                                RetweetCount = dto.retweet_count,
                                FavoriteCount = dto.favorite_count,
                                ViewCount = dto.view_count,
                                IsLiked = dto.is_liked,
                                IsRetweeted = dto.is_retweeted,
                                IsUserProtected = dto.user_protected,
                                IsUserVerified = dto.user_verified,
                            };
                        }

                        if (!string.IsNullOrEmpty(dto.actor_profile_image))
                        {
                            try
                            {
                                vm.ActorProfileImage = new BitmapImage(new Uri(dto.actor_profile_image));
                                if (vm.ActionTweet != null)
                                {
                                    vm.ActionTweet.UserProfileImage = vm.ActorProfileImage;
                                }
                            }
                            catch { }
                        }

                        list.Add(vm);
                        added++;
                    }
                    System.Diagnostics.Debug.WriteLine($"追加通知: {added}件 (refresh={refresh}, type={type})");

                    if (!refresh && added == 0)
                    {
                        SetHasMore(type, false);
                    }
                    else if (refresh && added > 0)
                    {
                        // 新着があった場合のみ時系列で Move して並び替え
                        SortNotificationsByTime(list);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadMore Error: " + ex.Message);
            }
            finally
            {
                IsLoadingMore = false;
                _inflightType = null;
            }

            await DrainQueuedTabAsync();
        }

        private async Task DrainQueuedTabAsync()
        {
            if (_drainingQueue || IsLoading || IsLoadingMore || _queuedType is null)
            {
                return;
            }

            _drainingQueue = true;
            try
            {
                while (_queuedType is string queued && !IsLoading && !IsLoadingMore)
                {
                    _queuedType = null;
                    if (!string.Equals(queued, _currentNotificationType, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    await LoadTabFromLatestAsync(queued);
                }
            }
            finally
            {
                _drainingQueue = false;
            }
        }

        private static void SortNotificationsByTime(ObservableCollection<NotificationViewModel> notifications)
        {
            var sorted = notifications
                .OrderByDescending(vm => TimeDisplayHelper.TryParse(vm.CreatedAt) ?? DateTime.MinValue)
                .ThenByDescending(vm => vm.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (sorted.Count <= 1)
            {
                return;
            }

            for (int targetIndex = 0; targetIndex < sorted.Count; targetIndex++)
            {
                var item = sorted[targetIndex];
                int currentIndex = notifications.IndexOf(item);
                if (currentIndex != targetIndex)
                {
                    notifications.Move(currentIndex, targetIndex);
                }
            }
        }

        public TweetViewModel? FindTweetById(string tweetId)
        {
            foreach (var notification in Notifications)
            {
                if (notification.ActionTweet != null
                    && string.Equals(notification.ActionTweet.Id, tweetId, StringComparison.Ordinal))
                {
                    return notification.ActionTweet;
                }
            }

            return null;
        }

        public Task<bool> LikeTweetAsync(string tweetId, bool currentlyLiked)
            => TweetActionClient.LikeAsync(_httpClient, tweetId, currentlyLiked);

        public Task<bool> RetweetTweetAsync(string tweetId)
            => TweetActionClient.RetweetAsync(_httpClient, tweetId);

        public async Task<HttpResponseMessage> ReplyTweetAsync(string tweetId, string replyText)
        {
            try
            {
                var content = new StringContent(
                    JsonSerializer.Serialize(new { text = replyText }),
                    Encoding.UTF8,
                    "application/json");
                return await _httpClient.PostAsync($"http://localhost:8000/reply/{tweetId}", content);
            }
            catch
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest);
            }
        }

        public Task<HttpResponseMessage> QuoteTweetAsync(
            string tweetId,
            string quoteText,
            IReadOnlyList<string> mediaPaths)
            => TweetActionClient.QuoteAsync(_httpClient, tweetId, quoteText, mediaPaths);

        public void AddReplyToTimeline(TweetViewModel originalVm, string newTweetId, string replyText)
        {
            _ = newTweetId;
            _ = replyText;
            originalVm.NoteReplyPosted();
        }

        public void AddQuoteToTimeline(TweetViewModel originalVm, string newTweetId, string quoteText)
        {
            _ = originalVm;
            _ = newTweetId;
            _ = quoteText;
        }

    }

    public class NotificationDto
    {
        public string? id { get; set; }
        public string? type { get; set; }
        public string? text { get; set; }
        public string? actor_name { get; set; }
        public string? actor_screen_name { get; set; }
        public string? actor_profile_image { get; set; }
        public string? created_at { get; set; }
        public string? target_tweet_text { get; set; }
        public int reply_count { get; set; }
        public int retweet_count { get; set; }
        public int favorite_count { get; set; }
        public int view_count { get; set; }
        public bool is_liked { get; set; }
        public bool is_retweeted { get; set; }
        public bool user_protected { get; set; }
        public bool user_verified { get; set; }
        public string? reply_to_screen_name { get; set; }
    }

    public partial class NotificationViewModel : INotifyPropertyChanged
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string ActorName { get; set; } = string.Empty;
        public string ActorScreenName { get; set; } = string.Empty;
        public string ActorScreenNameDisplay =>
            string.IsNullOrEmpty(ActorScreenName)
                ? string.Empty
                : ActorScreenName.StartsWith('@') ? ActorScreenName : "@" + ActorScreenName;
        public string CreatedAt { get; set; } = string.Empty;
        public string CreatedAtDisplay => TimeDisplayHelper.FormatDisplay(CreatedAt);
        public string CreatedAtAbsoluteDisplay => TimeDisplayHelper.FormatAbsoluteDisplay(CreatedAt);
        public string CreatedAtRelativeDisplay => TimeDisplayHelper.FormatRelativeDisplay(CreatedAt);
        public string TargetTweetText { get; set; } = string.Empty;
        public bool IsActorProtected { get; set; }
        public bool IsActorVerified { get; set; }
        public string ReplyToScreenName { get; set; } = string.Empty;
        public ImageSource? ActorProfileImage { get; set; }
        public TweetViewModel? ActionTweet { get; set; }
        public bool IsReply => string.Equals(Type, "reply", StringComparison.OrdinalIgnoreCase);
        public bool IsMention => string.Equals(Type, "mention", StringComparison.OrdinalIgnoreCase);
        public bool IsTweetCard => IsReply || IsMention;
        public bool ShowAggregateHeader => !IsTweetCard;

        /// <summary>返信・メンションの本文には付けない。いいね・リポスト・フォローだけ。</summary>
        private string Category => IsTweetCard ? string.Empty : NormalizeCategory(Type, Text);

        public bool HasTypeIcon => Category is "like" or "retweet" or "follow";

        public string IconGlyph => Category switch
        {
            "like" => "\uEB51",
            "retweet" => "\uE72A",
            "follow" => "\uE77B",
            _ => string.Empty
        };

        public Brush IconForeground => Category switch
        {
            "like" => LikeBrush,
            "retweet" => RetweetBrush,
            _ => FollowBrush
        };

        private static readonly Brush LikeBrush = new SolidColorBrush(Microsoft.UI.Colors.Red);
        private static readonly Brush RetweetBrush = new SolidColorBrush(Microsoft.UI.Colors.LimeGreen);
        private static readonly Brush FollowBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray);

        private static string NormalizeCategory(string? type, string? text)
        {
            var normalizedType = type?.Trim().ToLowerInvariant();
            if (normalizedType is "favorite" or "like") return "like";
            if (normalizedType is "retweet" or "repost") return "retweet";
            if (normalizedType is "follow") return "follow";

            var normalizedText = text?.ToLowerInvariant() ?? string.Empty;
            if (normalizedText.Contains("いいね") || normalizedText.Contains("お気に入り")
                || normalizedText.Contains("liked") || normalizedText.Contains("favorite"))
            {
                return "like";
            }

            if (normalizedText.Contains("リツイート") || normalizedText.Contains("リポスト")
                || normalizedText.Contains("retweeted") || normalizedText.Contains("reposted"))
            {
                return "retweet";
            }

            if (normalizedText.Contains("フォロー") || normalizedText.Contains("followed"))
            {
                return "follow";
            }

            return string.Empty;
        }

        public string TypeText => Type?.ToLower() switch
        {
            "favorite" or "like" => "いいねしました",
            "retweet" or "repost" => "リポストしました",
            "reply" => "返信しました",
            "follow" => "フォローしました",
            _ => Type ?? "通知"
        };

        public bool HasTargetTweet => !string.IsNullOrEmpty(TargetTweetText);

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
