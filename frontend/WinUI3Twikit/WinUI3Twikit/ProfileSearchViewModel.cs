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
    /// ユーザーカラム（プロフィール検索）の ViewModel。
    /// プロフィールは GET /users/{screen_name}、ツイートは GET /users/{screen_name}/tweets?type=...。
    /// 一覧はタブ（ツイート / 返信 / リポスト / メディア）ごとに別々に保持し、タブを切り替えても取得済みの内容は捨てない。
    /// </summary>
    public partial class ProfileSearchViewModel : INotifyPropertyChanged, ITweetListActions
    {
        private const int DefaultTweetFetchCount = 20;

        /// <summary>タブ（API の <c>type</c> クエリと同じ値）。</summary>
        public const string TabTweets = "tweets";
        public const string TabReplies = "replies";
        public const string TabRetweets = "retweets";
        public const string TabMedia = "media";

        /// <summary>メディアタブのプルダウン（API の <c>media</c> クエリと同じ値）。</summary>
        public const string MediaPhoto = "photo";
        public const string MediaVideo = "video";

        /// <summary>タブ（メディアは写真・動画別）ごとの一覧とページ送り状態。</summary>
        private sealed class Feed
        {
            public ObservableCollection<TweetViewModel> Tweets { get; } = [];
            public HashSet<string> SeenIds { get; } = new(StringComparer.OrdinalIgnoreCase);
            public string? NextCursor { get; set; }
            public bool CanLoadMore { get; set; } = true;

            /// <summary>初回取得を試みたか（失敗も含む）。空メッセージの表示判定に使う。</summary>
            public bool HasLoaded { get; set; }

            public double ScrollOffset { get; set; }
        }

        private readonly HttpClient _httpClient = new();
        private readonly Dictionary<string, Feed> _feeds = new(StringComparer.Ordinal);

        private bool _isLoading;
        private bool _isLoadingMore;
        private bool _hasProfile;
        private bool _hasError;
        private string? _currentScreenName;
        private string _currentTab = TabTweets;
        private string _mediaKind = MediaPhoto;

        private string _displayName = string.Empty;
        private string _screenNameDisplay = string.Empty;
        private string _bio = string.Empty;
        private string _tweetsCount = "0";
        private string _followingCount = "0";
        private string _followersCount = "0";
        private string _locationText = string.Empty;
        private string _joinedText = string.Empty;
        private string? _profileImageUrl;
        private string? _bannerImageUrl;
        private bool _isVerified;
        private string _errorMessage = string.Empty;
        private string _lastQuery = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>表示中のタブの一覧。タブ切り替え時は <see cref="PropertyChanged"/> で差し替わる。</summary>
        public ObservableCollection<TweetViewModel> Tweets => CurrentFeed.Tweets;

        public string CurrentTab => _currentTab;

        public string MediaKind => _mediaKind;

        public bool IsMediaTabSelected => string.Equals(_currentTab, TabMedia, StringComparison.Ordinal);

        /// <summary>メディアタブのプルダウンに表示する現在の種類。</summary>
        public string MediaKindLabel => string.Equals(_mediaKind, MediaVideo, StringComparison.Ordinal) ? "動画" : "写真";

        /// <summary>表示中のタブが取得済みで 0 件のときに出すメッセージ。</summary>
        public string EmptyMessage => _currentTab switch
        {
            TabReplies => "返信がありません",
            TabRetweets => "リポストがありません",
            TabMedia => string.Equals(_mediaKind, MediaVideo, StringComparison.Ordinal) ? "動画がありません" : "写真がありません",
            _ => "ポストがありません",
        };

        public bool ShowEmptyMessage =>
            HasProfile && !IsLoading && !IsLoadingMore && CurrentFeed.HasLoaded && CurrentFeed.Tweets.Count == 0;

        private string CurrentFeedKey => FeedKey(_currentTab, _mediaKind);

        private Feed CurrentFeed => FeedFor(CurrentFeedKey);

        private static string FeedKey(string tab, string mediaKind)
            => string.Equals(tab, TabMedia, StringComparison.Ordinal) ? $"{tab}:{mediaKind}" : tab;

        private Feed FeedFor(string key)
        {
            if (!_feeds.TryGetValue(key, out var feed))
            {
                feed = new Feed();
                _feeds[key] = feed;
            }

            return feed;
        }

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
                    OnPropertyChanged(nameof(ShowEmptyMessage));
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
                    OnPropertyChanged(nameof(ShowEmptyMessage));
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
                    OnPropertyChanged(nameof(ShowEmptyMessage));
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

        /// <summary>表示中のタブに続きがあるか。</summary>
        public bool CanLoadMore => CurrentFeed.CanLoadMore;

        private void SetCanLoadMore(Feed feed, bool value)
        {
            if (feed.CanLoadMore == value)
            {
                return;
            }

            feed.CanLoadMore = value;
            if (ReferenceEquals(feed, CurrentFeed))
            {
                OnPropertyChanged(nameof(CanLoadMore));
            }
        }

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

        public bool IsVerified
        {
            get => _isVerified;
            private set
            {
                if (_isVerified != value)
                {
                    _isVerified = value;
                    OnPropertyChanged(nameof(IsVerified));
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

        public string LastQuery
        {
            get => _lastQuery;
            set
            {
                if (_lastQuery != value)
                {
                    _lastQuery = value;
                    OnPropertyChanged(nameof(LastQuery));
                }
            }
        }

        /// <summary>表示中のタブのスクロール位置（タブごとに保持）。</summary>
        public double ScrollVerticalOffset
        {
            get => CurrentFeed.ScrollOffset;
            set => CurrentFeed.ScrollOffset = value;
        }

        public static string NormalizeScreenName(string? query)
        {
            var name = (query ?? string.Empty).Trim();
            if (name.StartsWith('@'))
            {
                name = name[1..];
            }
            return name.Trim();
        }

        public static string NormalizeTab(string? tab)
        {
            var value = (tab ?? string.Empty).Trim().ToLowerInvariant();
            return value is TabReplies or TabRetweets or TabMedia ? value : TabTweets;
        }

        public static string NormalizeMediaKind(string? kind)
            => string.Equals((kind ?? string.Empty).Trim(), MediaVideo, StringComparison.OrdinalIgnoreCase) ? MediaVideo : MediaPhoto;

        public async Task SearchAsync(string query)
        {
            var screenName = NormalizeScreenName(query);
            if (string.IsNullOrWhiteSpace(screenName))
            {
                return;
            }

            LastQuery = query.Trim();
            IsLoading = true;
            HasError = false;
            ErrorMessage = string.Empty;
            HasProfile = false;
            _currentScreenName = screenName;

            // 別のユーザーを開くときは、全タブの一覧を捨ててツイートタブから表示する。
            _feeds.Clear();
            _currentTab = TabTweets;
            NotifyFeedChanged();

            try
            {
                var profileOk = await LoadProfileAsync(screenName);
                if (!profileOk)
                {
                    return;
                }

                await LoadMoreTweetsAsync();
            }
            finally
            {
                IsLoading = false;
            }

            // 読み込み中に別のタブへ切り替えられていた場合、そのタブの初回分を取得する。
            await EnsureCurrentFeedLoadedAsync();
        }

        private async Task<bool> LoadProfileAsync(string screenName)
        {
            try
            {
                var url = $"http://localhost:8000/users/{Uri.EscapeDataString(screenName)}";
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("error", out var errorElement))
                {
                    HasProfile = false;
                    HasError = true;
                    ErrorMessage = errorElement.GetString() ?? "ユーザーが見つかりません";
                    ClearProfileFields();
                    return false;
                }

                DisplayName = GetString(root, "name") ?? "名前なし";
                var sn = GetString(root, "screen_name") ?? screenName;
                ScreenNameDisplay = sn.StartsWith('@') ? sn : $"@{sn}";
                Bio = GetString(root, "bio") ?? "自己紹介文がありません";
                TweetsCount = GetCountString(root, "statuses_count");
                FollowingCount = GetCountString(root, "following_count");
                FollowersCount = GetCountString(root, "followers_count");

                var location = GetString(root, "location");
                LocationText = string.IsNullOrWhiteSpace(location)
                    ? "📍 未設定"
                    : $"📍 {location}";

                var created = GetString(root, "created_str") ?? "不明";
                JoinedText = $"登録日: {created}";

                ProfileImageUrl = GetString(root, "profile_image_url");
                BannerImageUrl = GetString(root, "profile_banner_url");
                IsVerified = GetBool(root, "verified");

                HasProfile = true;
                HasError = false;
                ErrorMessage = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                HasProfile = false;
                HasError = true;
                ErrorMessage = $"読み込みエラー: {ex.Message}";
                ClearProfileFields();
                System.Diagnostics.Debug.WriteLine($"ProfileSearch LoadProfile Error: {ex.Message}");
                return false;
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
            JoinedText = string.Empty;
            ProfileImageUrl = null;
            BannerImageUrl = null;
            IsVerified = false;
        }

        /// <summary>
        /// 表示するタブを切り替える。未取得のタブなら初回分を取得する。
        /// 取得中なら、その取得の終了後に切り替え先を取得する。
        /// </summary>
        public async Task SwitchTabAsync(string? tab)
        {
            var normalized = NormalizeTab(tab);
            if (string.Equals(normalized, _currentTab, StringComparison.Ordinal))
            {
                return;
            }

            _currentTab = normalized;
            NotifyFeedChanged();
            await EnsureCurrentFeedLoadedAsync();
        }

        /// <summary>メディアタブの写真 / 動画を切り替える。</summary>
        public async Task SwitchMediaKindAsync(string? kind)
        {
            var normalized = NormalizeMediaKind(kind);
            if (string.Equals(normalized, _mediaKind, StringComparison.Ordinal))
            {
                return;
            }

            _mediaKind = normalized;
            NotifyFeedChanged();
            if (IsMediaTabSelected)
            {
                await EnsureCurrentFeedLoadedAsync();
            }
        }

        private void NotifyFeedChanged()
        {
            OnPropertyChanged(nameof(CurrentTab));
            OnPropertyChanged(nameof(MediaKind));
            OnPropertyChanged(nameof(IsMediaTabSelected));
            OnPropertyChanged(nameof(MediaKindLabel));
            OnPropertyChanged(nameof(Tweets));
            OnPropertyChanged(nameof(CanLoadMore));
            OnPropertyChanged(nameof(EmptyMessage));
            OnPropertyChanged(nameof(ShowEmptyMessage));
        }

        private async Task EnsureCurrentFeedLoadedAsync()
        {
            if (!HasProfile || IsLoading || IsLoadingMore || CurrentFeed.HasLoaded)
            {
                return;
            }

            await LoadMoreTweetsAsync();
        }

        public async Task LoadMoreTweetsAsync()
        {
            if (IsLoadingMore || string.IsNullOrEmpty(_currentScreenName))
            {
                return;
            }

            // 取得中にタブが切り替わっても、結果は要求したタブへ入れる。
            var feed = CurrentFeed;
            var feedKey = CurrentFeedKey;
            var tab = _currentTab;
            var mediaKind = _mediaKind;
            if (!feed.CanLoadMore)
            {
                return;
            }

            IsLoadingMore = true;
            var requestedCursor = feed.NextCursor;

            try
            {
                var url =
                    $"http://localhost:8000/users/{Uri.EscapeDataString(_currentScreenName)}/tweets?count={DefaultTweetFetchCount}&type={tab}";
                if (string.Equals(tab, TabMedia, StringComparison.Ordinal))
                {
                    url += $"&media={mediaKind}";
                }
                if (!string.IsNullOrWhiteSpace(requestedCursor))
                {
                    url += $"&cursor={Uri.EscapeDataString(requestedCursor)}";
                }

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var payload = JsonSerializer.Deserialize<UserTweetsApiResponse>(json);

                if (!string.IsNullOrEmpty(payload?.error) && (payload.tweets == null || payload.tweets.Count == 0))
                {
                    // プロフィールは表示したまま、ツイート取得失敗はログのみ（UI を壊さない）
                    System.Diagnostics.Debug.WriteLine($"ProfileSearch tweets error ({feedKey}): {payload.error}");
                    feed.NextCursor = null;
                    SetCanLoadMore(feed, false);
                    return;
                }

                var added = 0;
                if (payload?.tweets != null)
                {
                    foreach (var dto in payload.tweets)
                    {
                        if (string.IsNullOrEmpty(dto.id) || !feed.SeenIds.Add(dto.id))
                        {
                            continue;
                        }

                        feed.Tweets.Add(TweetViewModel.FromDto(dto));
                        added++;
                    }
                }

                var nextCursor = payload?.next_cursor;
                feed.NextCursor = nextCursor;

                // 新規0件、またはカーソルが進まない場合は打ち切り（同一ページの再取得ループ防止）
                var canLoadMore = added > 0
                    && !string.IsNullOrEmpty(nextCursor)
                    && !string.Equals(nextCursor, requestedCursor, StringComparison.Ordinal);
                SetCanLoadMore(feed, canLoadMore);
                if (!canLoadMore)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"ProfileSearch LoadMore stop ({feedKey}): added={added}, nextCursor={(string.IsNullOrEmpty(nextCursor) ? "なし" : "あり")}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ProfileSearch LoadMore Error ({feedKey}): {ex.Message}");
            }
            finally
            {
                feed.HasLoaded = true;
                IsLoadingMore = false;
            }

            // 取得中に別のタブへ切り替えられていたら、そのタブの初回分を続けて取得する。
            await EnsureCurrentFeedLoadedAsync();
        }

        public TweetViewModel? FindTweetById(string tweetId)
            => Tweets.FirstOrDefault(t => string.Equals(t.Id, tweetId, StringComparison.Ordinal))
               ?? _feeds.Values
                   .SelectMany(feed => feed.Tweets)
                   .FirstOrDefault(t => string.Equals(t.Id, tweetId, StringComparison.Ordinal));

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
                CreatedAt = TimeDisplayHelper.FormatNowForStorage(),
                IsLiked = false,
                IsRetweeted = false,
                ReplyCount = 0,
                FavoriteCount = 0,
                RetweetCount = 0,
            };
            SessionAccount.CopyAuthorTo(replyVm);

            var tweets = Tweets;
            var index = tweets.IndexOf(originalVm);
            if (index >= 0)
            {
                tweets.Insert(index + 1, replyVm);
            }
            else
            {
                tweets.Add(replyVm);
            }
        }

        public void AddQuoteToTimeline(TweetViewModel originalVm, string newTweetId, string quoteText)
        {
            var quoteVm = new TweetViewModel
            {
                Id = newTweetId,
                TimelineEntryId = newTweetId,
                Text = quoteText,
                CreatedAt = TimeDisplayHelper.FormatNowForStorage(),
                IsLiked = false,
                IsRetweeted = false,
                ReplyCount = 0,
                FavoriteCount = 0,
                RetweetCount = 0,
                QuotedTweet = originalVm.ToQuotedPreview(),
                MediaItems = TweetViewModel.CreateMediaItemsFromAttachments(originalVm.QuoteMediaFiles),
            };
            SessionAccount.CopyAuthorTo(quoteVm);
            TweetViewModel.FinalizeQuotedCardMedia(quoteVm);

            var tweets = Tweets;
            var index = tweets.IndexOf(originalVm);
            if (index >= 0)
            {
                tweets.Insert(index + 1, quoteVm);
            }
            else
            {
                tweets.Add(quoteVm);
            }
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

        private static bool GetBool(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
            {
                return false;
            }

            return el.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(el.GetString(), out var value) && value,
                _ => false
            };
        }

        private sealed class UserTweetsApiResponse
        {
            public List<TweetDto>? tweets { get; set; }
            public string? next_cursor { get; set; }
            public string? error { get; set; }
        }
    }
}
