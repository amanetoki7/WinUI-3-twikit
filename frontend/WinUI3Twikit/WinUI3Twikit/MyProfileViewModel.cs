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
    /// <summary>
    /// 自分のプロフィール用 ViewModel。
    /// プロフィールは GET /profile、ツイートは GET /profile/tweets
    /// （バックエンドは get_user_by_screen_name でユーザー解決）。
    /// </summary>
    public partial class MyProfileViewModel : INotifyPropertyChanged, ITweetListActions
    {
        private const int DefaultTweetFetchCount = 20;

        public ObservableCollection<TweetViewModel> Tweets { get; } = [];

        private readonly HttpClient _httpClient = new();
        private readonly HashSet<string> _seenIds = new(StringComparer.OrdinalIgnoreCase);

        private bool _isLoading;
        private bool _isLoadingMore;
        private bool _hasProfile;
        private bool _hasError;
        private bool _hasLoadedOnce;
        private string? _nextCursor;
        private bool _canLoadMore = true;
        private double _scrollVerticalOffset;

        private string _displayName = string.Empty;
        private string _screenNameDisplay = string.Empty;
        private string _bio = string.Empty;
        private string _tweetsCount = "0";
        private string _followingCount = "0";
        private string _followersCount = "0";
        private string _locationText = string.Empty;
        private string _websiteText = string.Empty;
        private string _joinedText = string.Empty;
        private string? _profileImageUrl;
        private string? _bannerImageUrl;
        private string _errorMessage = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged(nameof(IsLoading));
                    OnPropertyChanged(nameof(IsContentVisible));
                }
            }
        }

        public bool IsLoadingMore
        {
            get => _isLoadingMore;
            set
            {
                if (_isLoadingMore != value)
                {
                    _isLoadingMore = value;
                    OnPropertyChanged(nameof(IsLoadingMore));
                }
            }
        }

        public bool HasProfile
        {
            get => _hasProfile;
            private set
            {
                if (_hasProfile != value)
                {
                    _hasProfile = value;
                    OnPropertyChanged(nameof(HasProfile));
                    OnPropertyChanged(nameof(IsContentVisible));
                }
            }
        }

        public bool HasError
        {
            get => _hasError;
            private set
            {
                if (_hasError != value)
                {
                    _hasError = value;
                    OnPropertyChanged(nameof(HasError));
                }
            }
        }

        public bool IsContentVisible => HasProfile && !IsLoading;

        public string DisplayName
        {
            get => _displayName;
            private set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public string ScreenNameDisplay
        {
            get => _screenNameDisplay;
            private set
            {
                if (_screenNameDisplay != value)
                {
                    _screenNameDisplay = value;
                    OnPropertyChanged(nameof(ScreenNameDisplay));
                }
            }
        }

        public string Bio
        {
            get => _bio;
            private set
            {
                if (_bio != value)
                {
                    _bio = value;
                    OnPropertyChanged(nameof(Bio));
                }
            }
        }

        public string TweetsCount
        {
            get => _tweetsCount;
            private set
            {
                if (_tweetsCount != value)
                {
                    _tweetsCount = value;
                    OnPropertyChanged(nameof(TweetsCount));
                }
            }
        }

        public string FollowingCount
        {
            get => _followingCount;
            private set
            {
                if (_followingCount != value)
                {
                    _followingCount = value;
                    OnPropertyChanged(nameof(FollowingCount));
                }
            }
        }

        public string FollowersCount
        {
            get => _followersCount;
            private set
            {
                if (_followersCount != value)
                {
                    _followersCount = value;
                    OnPropertyChanged(nameof(FollowersCount));
                }
            }
        }

        public string LocationText
        {
            get => _locationText;
            private set
            {
                if (_locationText != value)
                {
                    _locationText = value;
                    OnPropertyChanged(nameof(LocationText));
                }
            }
        }

        public string WebsiteText
        {
            get => _websiteText;
            private set
            {
                if (_websiteText != value)
                {
                    _websiteText = value;
                    OnPropertyChanged(nameof(WebsiteText));
                    OnPropertyChanged(nameof(HasWebsite));
                }
            }
        }

        public bool HasWebsite => !string.IsNullOrWhiteSpace(WebsiteText);

        public string JoinedText
        {
            get => _joinedText;
            private set
            {
                if (_joinedText != value)
                {
                    _joinedText = value;
                    OnPropertyChanged(nameof(JoinedText));
                }
            }
        }

        public string? ProfileImageUrl
        {
            get => _profileImageUrl;
            private set
            {
                if (_profileImageUrl != value)
                {
                    _profileImageUrl = value;
                    OnPropertyChanged(nameof(ProfileImageUrl));
                }
            }
        }

        public string? BannerImageUrl
        {
            get => _bannerImageUrl;
            private set
            {
                if (_bannerImageUrl != value)
                {
                    _bannerImageUrl = value;
                    OnPropertyChanged(nameof(BannerImageUrl));
                }
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set
            {
                if (_errorMessage != value)
                {
                    _errorMessage = value;
                    OnPropertyChanged(nameof(ErrorMessage));
                }
            }
        }

        public double ScrollVerticalOffset
        {
            get => _scrollVerticalOffset;
            set => _scrollVerticalOffset = value;
        }

        /// <summary>
        /// 自分のプロフィールとツイート一覧を読み込む。
        /// </summary>
        public async Task LoadAsync(bool forceReload = false)
        {
            if (IsLoading)
            {
                return;
            }

            if (_hasLoadedOnce && HasProfile && !forceReload)
            {
                return;
            }

            IsLoading = true;
            HasError = false;
            ErrorMessage = string.Empty;
            Tweets.Clear();
            _seenIds.Clear();
            _nextCursor = null;
            _canLoadMore = true;
            ScrollVerticalOffset = 0;

            try
            {
                var response = await _httpClient.GetAsync("http://localhost:8000/profile");
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("error", out var errorElement))
                {
                    HasProfile = false;
                    HasError = true;
                    ErrorMessage = errorElement.GetString() ?? "プロフィールを取得できませんでした";
                    ClearProfileFields();
                    return;
                }

                DisplayName = GetString(root, "name") ?? "名前なし";
                var sn = GetString(root, "screen_name") ?? string.Empty;
                ScreenNameDisplay = string.IsNullOrEmpty(sn)
                    ? string.Empty
                    : (sn.StartsWith('@') ? sn : $"@{sn}");
                Bio = GetString(root, "bio") ?? "自己紹介文がありません";
                TweetsCount = GetCountString(root, "statuses_count");
                FollowingCount = GetCountString(root, "following_count");
                FollowersCount = GetCountString(root, "followers_count");

                var location = GetString(root, "location");
                LocationText = string.IsNullOrWhiteSpace(location)
                    ? "📍 未設定"
                    : $"📍 {location}";

                var website = GetString(root, "website") ?? GetString(root, "url");
                WebsiteText = string.IsNullOrWhiteSpace(website) ? string.Empty : website;

                var created = GetString(root, "created_str") ?? "不明";
                JoinedText = $"登録日: {created}";

                ProfileImageUrl = GetString(root, "profile_image_url");
                BannerImageUrl = GetString(root, "profile_banner_url");

                HasProfile = true;
                HasError = false;
                ErrorMessage = string.Empty;
                _hasLoadedOnce = true;

                // 初回ツイート一覧（GET /profile/tweets → get_user_by_screen_name）
                await LoadMoreTweetsAsync();
            }
            catch (Exception ex)
            {
                HasProfile = false;
                HasError = true;
                ErrorMessage = $"読み込みエラー: {ex.Message}";
                ClearProfileFields();
                System.Diagnostics.Debug.WriteLine($"MyProfile LoadAsync Error: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task LoadMoreTweetsAsync()
        {
            if (IsLoadingMore || !_canLoadMore || !HasProfile)
            {
                return;
            }

            IsLoadingMore = true;

            try
            {
                var url = $"http://localhost:8000/profile/tweets?count={DefaultTweetFetchCount}";
                if (!string.IsNullOrWhiteSpace(_nextCursor))
                {
                    url += $"&cursor={Uri.EscapeDataString(_nextCursor)}";
                }

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var payload = JsonSerializer.Deserialize<UserTweetsApiResponse>(json);

                if (!string.IsNullOrEmpty(payload?.error) && (payload.tweets == null || payload.tweets.Count == 0))
                {
                    System.Diagnostics.Debug.WriteLine($"MyProfile tweets error: {payload.error}");
                    _nextCursor = null;
                    _canLoadMore = false;
                    return;
                }

                _nextCursor = payload?.next_cursor;
                _canLoadMore = !string.IsNullOrEmpty(_nextCursor);

                if (payload?.tweets != null)
                {
                    foreach (var dto in payload.tweets)
                    {
                        if (string.IsNullOrEmpty(dto.id) || _seenIds.Contains(dto.id))
                        {
                            continue;
                        }

                        _seenIds.Add(dto.id);
                        Tweets.Add(TweetViewModel.FromDto(dto));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MyProfile LoadMoreTweets Error: {ex.Message}");
            }
            finally
            {
                IsLoadingMore = false;
            }
        }

        public TweetViewModel? FindTweetById(string tweetId)
            => Tweets.FirstOrDefault(t => string.Equals(t.Id, tweetId, StringComparison.Ordinal));

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

        public Task<HttpResponseMessage> QuoteTweetAsync(string tweetId, string quoteText, IReadOnlyList<string> mediaPaths)
            => TweetActionClient.QuoteAsync(_httpClient, tweetId, quoteText, mediaPaths);

        public void AddReplyToTimeline(TweetViewModel originalVm, string newTweetId, string replyText)
        {
            var replyVm = new TweetViewModel
            {
                Id = newTweetId,
                Text = replyText,
                UserName = string.IsNullOrEmpty(DisplayName) ? AccountDefaults.DisplayName : DisplayName,
                UserScreenName = string.IsNullOrEmpty(ScreenNameDisplay) ? AccountDefaults.ScreenName : ScreenNameDisplay,
                CreatedAt = TimeDisplayHelper.FormatNowForStorage(),
                IsLiked = false,
                IsRetweeted = false,
                ReplyCount = 0,
                FavoriteCount = 0,
                RetweetCount = 0,
                UserProfileImage = ImageCache.GetAvatar(
                    ProfileImageUrl
                    ?? AccountDefaults.ProfileImageUrl)
            };

            var index = Tweets.IndexOf(originalVm);
            if (index >= 0)
            {
                Tweets.Insert(index + 1, replyVm);
            }
            else
            {
                Tweets.Add(replyVm);
            }
        }

        public void AddQuoteToTimeline(TweetViewModel originalVm, string newTweetId, string quoteText)
        {
            var quoteVm = new TweetViewModel
            {
                Id = newTweetId,
                TimelineEntryId = newTweetId,
                Text = quoteText,
                UserName = string.IsNullOrEmpty(DisplayName) ? AccountDefaults.DisplayName : DisplayName,
                UserScreenName = string.IsNullOrEmpty(ScreenNameDisplay) ? AccountDefaults.ScreenName : ScreenNameDisplay,
                CreatedAt = TimeDisplayHelper.FormatNowForStorage(),
                IsLiked = false,
                IsRetweeted = false,
                ReplyCount = 0,
                FavoriteCount = 0,
                RetweetCount = 0,
                QuotedTweet = originalVm.ToQuotedPreview(),
                MediaItems = TweetViewModel.CreateMediaItemsFromAttachments(originalVm.QuoteMediaFiles),
                UserProfileImage = ImageCache.GetAvatar(
                    ProfileImageUrl
                    ?? "https://pbs.twimg.com/profile_images/1938605137813282816/u5D3g9W3_400x400.jpg")
            };
            TweetViewModel.FinalizeQuotedCardMedia(quoteVm);

            var index = Tweets.IndexOf(originalVm);
            if (index >= 0)
            {
                Tweets.Insert(index + 1, quoteVm);
            }
            else
            {
                Tweets.Add(quoteVm);
            }
        }

        private void ClearProfileFields()
        {
            DisplayName = string.Empty;
            ScreenNameDisplay = string.Empty;
            Bio = string.Empty;
            TweetsCount = "0";
            FollowingCount = "0";
            FollowersCount = "0";
            LocationText = string.Empty;
            WebsiteText = string.Empty;
            JoinedText = string.Empty;
            ProfileImageUrl = null;
            BannerImageUrl = null;
        }

        private static string? GetString(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Number => el.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => el.ToString()
            };
        }

        private static string GetCountString(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
            {
                return "0";
            }

            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var n))
            {
                return n.ToString();
            }

            return el.ToString() ?? "0";
        }

        private sealed class UserTweetsApiResponse
        {
            public List<TweetDto>? tweets { get; set; }
            public string? next_cursor { get; set; }
            public string? error { get; set; }
        }
    }
}
