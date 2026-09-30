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
    public partial class TimelineViewModel : INotifyPropertyChanged, ITweetListActions
    {
        private const int DefaultTimelineFetchCount = 30;

        private readonly Dictionary<string, ObservableCollection<TweetViewModel>> _tweetsByType = new(StringComparer.OrdinalIgnoreCase);

        public ObservableCollection<TweetViewModel> Tweets =>
            GetOrCreateTweets(NormalizeTimelineType(CurrentTimelineType));

        private readonly Dictionary<string, int> _tweetCountsByType = new(StringComparer.OrdinalIgnoreCase)
        {
            ["for_you"] = 0,
            ["latest"] = 0
        };

        public int ForYouTweetCount => _tweetCountsByType.GetValueOrDefault("for_you");
        public int LatestTweetCount => _tweetCountsByType.GetValueOrDefault("latest");

        private static string NormalizeTimelineType(string? type)
            => string.IsNullOrWhiteSpace(type) ? "for_you" : type;

        private ObservableCollection<TweetViewModel> GetOrCreateTweets(string type)
        {
            var key = NormalizeTimelineType(type);
            if (!_tweetsByType.TryGetValue(key, out var collection))
            {
                collection = new TweetCollection();
                collection.CollectionChanged += (_, _) => UpdateTweetCount(key);
                _tweetsByType[key] = collection;
            }

            return collection;
        }

        private void UpdateTweetCount(string type)
        {
            if (_tweetsByType.TryGetValue(type, out var collection))
            {
                _tweetCountsByType[type] = collection.Count;
            }

            OnPropertyChanged(nameof(ForYouTweetCount));
            OnPropertyChanged(nameof(LatestTweetCount));
        }

        private readonly HttpClient _httpClient = new();

        private int _listChangeDepth;

        public bool IsApplyingListChange => _listChangeDepth > 0;

        internal Func<TimelineViewport?>? CaptureViewport { get; set; }

        internal event Action<TimelineScrollAnchor>? RestoreViewportRequested;

        private readonly Dictionary<string, HashSet<string>> _seenTweetIdsByType = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> _nextCursorByType = new(StringComparer.OrdinalIgnoreCase);

        private bool _isLoading = false;
        private bool _isLoadingMore = false;

        public bool IsLoading
        {
            get => _isLoading;
            set { if (_isLoading != value) { _isLoading = value; OnPropertyChanged(nameof(IsLoading)); } }
        }

        public bool IsLoadingMore
        {
            get => _isLoadingMore;
            set { if (_isLoadingMore != value) { _isLoadingMore = value; OnPropertyChanged(nameof(IsLoadingMore)); } }
        }

        private string _currentTimelineType = "for_you";

        public string CurrentTimelineType
        {
            get => _currentTimelineType;
            set
            {
                if (_currentTimelineType != value)
                {
                    _currentTimelineType = value;
                    OnPropertyChanged(nameof(CurrentTimelineType));
                    OnPropertyChanged(nameof(IsChronologicalTimeline));
                }
            }
        }

        public bool IsChronologicalTimeline => IsChronologicalType(CurrentTimelineType);

        private static bool IsChronologicalType(string? type)
            => !string.Equals(NormalizeTimelineType(type), "for_you", StringComparison.OrdinalIgnoreCase);

        private readonly Dictionary<string, TimelineScrollAnchor> _scrollAnchorByTimelineType = new(StringComparer.OrdinalIgnoreCase);

        public TimelineScrollAnchor ScrollAnchor
        {
            get => _scrollAnchorByTimelineType.GetValueOrDefault(NormalizeTimelineType(CurrentTimelineType));
            set => _scrollAnchorByTimelineType[NormalizeTimelineType(CurrentTimelineType)] = value;
        }

        private HashSet<string> GetSeenSet(string? timelineType = null)
        {
            var key = NormalizeTimelineType(timelineType ?? CurrentTimelineType);
            if (!_seenTweetIdsByType.TryGetValue(key, out var seenSet))
            {
                seenSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _seenTweetIdsByType[key] = seenSet;
            }

            return seenSet;
        }

        private string? GetNextCursor(string? timelineType = null)
        {
            var key = NormalizeTimelineType(timelineType ?? CurrentTimelineType);
            if (!_nextCursorByType.TryGetValue(key, out var cursor))
            {
                cursor = null;
                _nextCursorByType[key] = cursor;
            }

            return cursor;
        }

        private void SetNextCursor(string? cursor, string? timelineType = null)
        {
            var key = NormalizeTimelineType(timelineType ?? CurrentTimelineType);
            _nextCursorByType[key] = cursor;
        }

        private static TweetViewModel CreateTweetViewModel(TweetDto dto) => TweetViewModel.FromDto(dto);

        private static string GetDedupKey(TweetDto dto)
            => !string.IsNullOrWhiteSpace(dto.timeline_entry_id) ? dto.timeline_entry_id : dto.id ?? string.Empty;

        public async Task SwitchTimelineAsync(string newType)
        {
            var normalizedType = NormalizeTimelineType(newType);
            if (string.Equals(CurrentTimelineType, normalizedType, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            CurrentTimelineType = normalizedType;
            OnPropertyChanged(nameof(Tweets));

            if (GetOrCreateTweets(normalizedType).Count == 0)
            {
                await LoadTweetsAsync();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public async Task LoadTweetsAsync()
        {
            // 開始時点の種別を固定（読み込み中にタブ切替されても誤反映しない）
            var timelineType = NormalizeTimelineType(CurrentTimelineType);
            IsLoading = true;
            GetOrCreateTweets(timelineType).Clear();

            var seenSet = GetSeenSet(timelineType);
            seenSet.Clear();
            SetNextCursor(null, timelineType);

            await LoadMoreTweetsAsync(timelineType);
            IsLoading = false;
        }

        public async Task LoadMoreTweetsAsync(string? timelineType = null)
        {
            if (IsLoadingMore) return;

            var type = NormalizeTimelineType(timelineType ?? CurrentTimelineType);
            IsLoadingMore = true;

            try
            {
                var (tweetList, nextCursor) = await FetchTimelinePageAsync(type, GetNextCursor(type));
                SetNextCursor(nextCursor, type);

                if (tweetList != null)
                {
                    var seenSet = GetSeenSet(type);
                    var incoming = new List<TweetViewModel>();

                    foreach (var dto in tweetList)
                    {
                        var dedupKey = GetDedupKey(dto);
                        if (string.IsNullOrEmpty(dedupKey) || dedupKey == "error" || !seenSet.Add(dedupKey))
                        {
                            continue;
                        }

                        incoming.Add(CreateTweetViewModel(dto));
                    }

                    IntegrateTweets(type, incoming);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("追加取得エラー: " + ex.Message);
            }
            finally
            {
                IsLoadingMore = false;
            }
        }

        private void IntegrateTweets(string type, List<TweetViewModel> incoming)
        {
            if (incoming.Count == 0)
            {
                return;
            }

            var tweets = GetOrCreateTweets(type);
            if (!IsChronologicalType(type))
            {
                Mutate(() =>
                {
                    if (tweets.Count == 0)
                    {
                        ResetTweets(tweets, incoming);
                    }
                    else
                    {
                        foreach (var vm in incoming)
                        {
                            tweets.Add(vm);
                        }
                    }
                });
                return;
            }

            var merged = tweets.Count == 0
                ? SortNewestFirst(incoming)
                : SortNewestFirst(tweets.Concat(incoming));

            if (tweets.Count > 0 && HasSamePrefix(tweets, merged))
            {
                var start = tweets.Count;
                Mutate(() =>
                {
                    for (var i = start; i < merged.Count; i++)
                    {
                        tweets.Add(merged[i]);
                    }
                });
                return;
            }

            var viewport = tweets.Count > 0 ? CaptureIfVisible() : null;
            Mutate(
                () => ResetTweets(tweets, merged),
                viewport);
        }

        private void PrependBlock(ObservableCollection<TweetViewModel> tweets, List<TweetViewModel> items)
        {
            if (items.Count == 0)
            {
                return;
            }

            if (tweets.Count == 0)
            {
                Mutate(() => ResetTweets(tweets, items));
                return;
            }

            var viewport = CaptureIfVisible();
            var merged = new List<TweetViewModel>(items.Count + tweets.Count);
            merged.AddRange(items);
            merged.AddRange(tweets);
            Mutate(() => ResetTweets(tweets, merged), viewport);
        }

        private TimelineViewport? CaptureIfVisible()
        {
            if (IsLoading)
            {
                return null;
            }

            return CaptureViewport?.Invoke();
        }

        private void Mutate(Action body, TimelineViewport? viewport = null)
        {
            _listChangeDepth++;
            try
            {
                body();
                if (viewport is { AtTop: false, Anchor.IsEmpty: false } shot)
                {
                    RestoreViewportRequested?.Invoke(shot.Anchor);
                }
            }
            finally
            {
                _listChangeDepth--;
            }
        }

        private static void ResetTweets(ObservableCollection<TweetViewModel> tweets, IReadOnlyList<TweetViewModel> items)
        {
            if (tweets is TweetCollection collection)
            {
                collection.Reset(items);
                return;
            }

            tweets.Clear();
            foreach (var item in items)
            {
                tweets.Add(item);
            }
        }

        private static bool HasSamePrefix(IList<TweetViewModel> existing, IReadOnlyList<TweetViewModel> merged)
        {
            if (merged.Count < existing.Count)
            {
                return false;
            }

            for (var i = 0; i < existing.Count; i++)
            {
                if (!ReferenceEquals(existing[i], merged[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static List<TweetViewModel> SortNewestFirst(IEnumerable<TweetViewModel> source)
            => [.. source
                .OrderByDescending(t => t.SortKey)
                .ThenByDescending(t => TimeDisplayHelper.TryParse(t.CreatedAt) ?? DateTime.MinValue)];

        private async Task<(List<TweetDto> TweetList, string? NextCursor)> FetchTimelinePageAsync(string timelineType, string? cursor)
        {
            var type = NormalizeTimelineType(timelineType);
            var url = $"http://localhost:8000/timeline?pages=1&count={DefaultTimelineFetchCount}&type={Uri.EscapeDataString(type)}";
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                url += $"&cursor={Uri.EscapeDataString(cursor)}";
            }

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            try
            {
                var payload = JsonSerializer.Deserialize<TimelineApiResponse>(json);
                return (payload?.tweets ?? [], payload?.next_cursor);
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Timeline payload parse failed: {ex.Message}");
                var fallback = JsonSerializer.Deserialize<List<TweetDto>>(json);
                return (fallback ?? [], null);
            }
        }

        private sealed class TimelineApiResponse
        {
            public List<TweetDto>? tweets { get; set; }
            public string? next_cursor { get; set; }
        }

        /// <summary>
        /// 新着ツイートを指定（または現在）のタイムライン種別に応じて挿入する。
        /// おすすめ: API順のまま先頭へ挿入。最新: 時系列（新しい順）で並べ替えて上側へ挿入。
        /// timelineType を渡すと、取得中にタブが切り替わってもその種別のリストへだけ反映する。
        /// </summary>
        public void ApplyNewTweets(List<TweetViewModel> newTweets, string? timelineType = null)
        {
            var type = NormalizeTimelineType(timelineType ?? CurrentTimelineType);
            if (IsChronologicalType(type))
                MergeAndSortNewTweets(newTweets, type);
            else
                PrependNewTweets(newTweets, type);
        }

        /// <summary>
        /// おすすめ欄向け: 新着を先頭に挿入（相対順序は維持）。
        /// </summary>
        public void PrependNewTweets(List<TweetViewModel> newTweets, string? timelineType = null)
        {
            if (newTweets == null || newTweets.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("Prepend: 新着0件");
                return;
            }

            if (App.IsShuttingDown)
            {
                return;
            }

            var type = NormalizeTimelineType(timelineType ?? CurrentTimelineType);
            var dispatcher =
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()
                ?? App.MainWindow?.DispatcherQueue;

            if (dispatcher == null)
            {
                System.Diagnostics.Debug.WriteLine("DispatcherQueue が取得できませんでした");
                return;
            }

            dispatcher.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            {
                if (App.IsShuttingDown)
                {
                    return;
                }

                try
                {
                    var seenSet = GetSeenSet(type);
                    var tweets = GetOrCreateTweets(type);
                    var accepted = new List<TweetViewModel>();
                    var addedIds = new List<string>();

                    foreach (var newVm in newTweets)
                    {
                        if (string.IsNullOrEmpty(newVm.DedupKey) || !seenSet.Add(newVm.DedupKey))
                        {
                            continue;
                        }

                        accepted.Add(newVm);
                        addedIds.Add(newVm.DedupKey);
                    }

                    PrependBlock(tweets, accepted);

                    if (accepted.Count > 0)
                        System.Diagnostics.Debug.WriteLine($"おすすめ先頭挿入 ({type}): {accepted.Count}件 (合計{tweets.Count}件) | 例: {string.Join(", ", addedIds.Take(3))}");
                    else
                        System.Diagnostics.Debug.WriteLine($"Prepend ({type}): 追加0件（すべて重複） 取得数: {newTweets.Count}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Prepend例外: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 最新欄向け: 新着を時系列（新しい順）で上側へ挿入・全体を再整列。
        /// </summary>
        public void MergeAndSortNewTweets(List<TweetViewModel> newTweets, string? timelineType = null)
        {
            if (newTweets == null || newTweets.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("Merge: 新着0件");
                return;
            }

            if (App.IsShuttingDown)
            {
                return;
            }

            var type = NormalizeTimelineType(timelineType ?? CurrentTimelineType);
            Microsoft.UI.Dispatching.DispatcherQueue? dispatcher =
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()
                ?? App.MainWindow?.DispatcherQueue;


            if (dispatcher == null)
            {
                System.Diagnostics.Debug.WriteLine("DispatcherQueue が取得できませんでした");
                return;
            }

            dispatcher.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
            {
                if (App.IsShuttingDown)
                {
                    return;
                }

                try
                {
                    var seenSet = GetSeenSet(type);
                    var tweets = GetOrCreateTweets(type);
                    var accepted = new List<TweetViewModel>();
                    var addedIds = new List<string>();

                    foreach (var newVm in newTweets)
                    {
                        if (string.IsNullOrEmpty(newVm.DedupKey) || !seenSet.Add(newVm.DedupKey))
                        {
                            continue;
                        }

                        accepted.Add(newVm);
                        addedIds.Add(newVm.DedupKey);
                    }

                    IntegrateTweets(type, accepted);

                    if (accepted.Count > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"最新時系列挿入 ({type}): {accepted.Count}件 (合計{tweets.Count}件) | 例: {string.Join(", ", addedIds.Take(3))}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Merge ({type}): 本物追加0件（すべて重複） 取得数: {newTweets.Count}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Merge例外: {ex.Message}");
                }
            });
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
                TimelineEntryId = newTweetId,
                Text = replyText,
                CreatedAt = TimeDisplayHelper.FormatNowForStorage(),
                IsLiked = false,
                IsRetweeted = false,
                ReplyCount = 0,
                FavoriteCount = 0,
                RetweetCount = 0,
            };
            SessionAccount.CopyAuthorTo(replyVm);

            GetSeenSet().Add(replyVm.DedupKey);
            int index = Tweets.IndexOf(originalVm);
            if (index >= 0)
            {
                Tweets.Insert(index + 1, replyVm);
            }
            else
            {
                Tweets.Add(replyVm);
            }

            System.Diagnostics.Debug.WriteLine($"返信を挿入しました: {newTweetId}");
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

            GetSeenSet().Add(quoteVm.DedupKey);
            int index = Tweets.IndexOf(originalVm);
            if (index >= 0)
            {
                Tweets.Insert(index + 1, quoteVm);
            }
            else
            {
                Tweets.Add(quoteVm);
            }

            System.Diagnostics.Debug.WriteLine($"引用ツイートを挿入しました: {newTweetId}");
        }

        /// <summary>
        /// 新着を取得する。戻り値の TimelineType は取得開始時に固定した種別
        /// （await 中にタブ切替されても、呼び出し側はこの種別へだけ Apply する）。
        /// </summary>
        public async Task<(List<TweetViewModel> NewTweets, string TimelineType)> GetNewTweetsAsync(string? timelineType = null)
        {
            var type = NormalizeTimelineType(timelineType ?? CurrentTimelineType);
            var newVms = new List<TweetViewModel>();
            try
            {
                System.Diagnostics.Debug.WriteLine($"GetNewTweetsAsync 開始 (type: {type})");

                var (tweetList, _) = await FetchTimelinePageAsync(type, null);
                if (tweetList == null) return (newVms, type);

                // await 後も開始時の type の seen を使う（タブ切替で誤判定しない）
                var seenSet = GetSeenSet(type);
                var seenInResponse = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var dto in tweetList)
                {
                    var dedupKey = GetDedupKey(dto);
                    if (string.IsNullOrEmpty(dedupKey) || dedupKey == "error") continue;
                    if (seenSet.Contains(dedupKey) || seenInResponse.Contains(dedupKey)) continue;

                    seenInResponse.Add(dedupKey);
                    newVms.Add(CreateTweetViewModel(dto));
                }

                System.Diagnostics.Debug.WriteLine($"GetNewTweetsAsync 結果: {newVms.Count}件 (type: {type})");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetNewTweetsAsync Error: {ex.Message}");
            }

            return (newVms, type);
        }
    }
}
