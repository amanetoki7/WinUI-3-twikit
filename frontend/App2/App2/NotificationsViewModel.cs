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
        public ObservableCollection<NotificationViewModel> Notifications { get; } = [];

        private readonly HttpClient _httpClient = new();
        private bool _isLoading = false;
        private bool _isLoadingMore = false;
        private bool _hasMore = true;
        private double _scrollVerticalOffset;

        public double ScrollVerticalOffset
        {
            get => _scrollVerticalOffset;
            set => _scrollVerticalOffset = value;
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

        public bool HasMore
        {
            get => _hasMore;
            private set { _hasMore = value; OnPropertyChanged(nameof(HasMore)); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public async Task LoadNotificationsAsync()
        {
            IsLoading = true;
            Notifications.Clear();
            HasMore = true;
            await LoadMoreNotificationsAsync(refresh: true);
            IsLoading = false;
        }

        /// <summary>
        /// 通知を取得
        /// </summary>
        /// <param name="refresh">true: 最新から取得 / false: 続きを取得</param>
        public async Task LoadMoreNotificationsAsync(bool refresh = false)
        {
            if (IsLoadingMore || (!refresh && !HasMore)) return;

            IsLoadingMore = true;

            try
            {
                var url = $"http://localhost:8000/notifications?count=20&refresh={refresh.ToString().ToLower()}";
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var newNotifs = JsonSerializer.Deserialize<List<NotificationDto>>(json);

                if (newNotifs == null || newNotifs.Count == 0)
                {
                    if (!refresh)
                    {
                        HasMore = false;
                    }
                    return;
                }

                int added = 0;
                foreach (var dto in newNotifs)
                {
                    if (Notifications.Any(n => n.Id == dto.id)) continue;

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

                    if (vm.IsReply)
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

                    Notifications.Add(vm);
                    added++;
                }
                System.Diagnostics.Debug.WriteLine($"追加通知: {added}件 (refresh={refresh})");

                if (!refresh && added == 0)
                {
                    HasMore = false;
                }
                else if (refresh && added > 0)
                {
                    // 新着があった場合のみ時系列で Move して並び替え
                    SortNotificationsByTime();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("LoadMore Error: " + ex.Message);
            }
            finally
            {
                IsLoadingMore = false;
            }
        }

        private void SortNotificationsByTime()
        {
            var sorted = Notifications
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
                int currentIndex = Notifications.IndexOf(item);
                if (currentIndex != targetIndex)
                {
                    Notifications.Move(currentIndex, targetIndex);
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
        public bool ShowAggregateHeader => !IsReply;

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
