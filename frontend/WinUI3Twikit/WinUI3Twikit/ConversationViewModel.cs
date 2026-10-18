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
    /// <summary>会話ページの 1 行（ツイート / 取得できなかったツイート / 返信の見出し）。</summary>
    public sealed partial class ConversationEntry : INotifyPropertyChanged
    {
        private bool _hasLineAbove;
        private bool _hasLineBelow;
        private bool _showNoReplies;

        public TweetViewModel? Tweet { get; init; }

        /// <summary>開いているツイート。</summary>
        public bool IsFocal { get; init; }

        /// <summary>返信元のうち表示できないツイートの ID。この行はその代わりの表示。</summary>
        public string? MissingTweetId { get; init; }

        /// <summary>表示できない理由（X の説明文。X が返さなかっただけのときは既定の文言）。</summary>
        public string MissingText { get; init; } = "取得できなかったツイートがあります";

        /// <summary>
        /// X が流れの途中から返したなど、そのツイートの会話を開き直せば続きが出るかもしれない。
        /// 削除・凍結などで X が見られないと答えたものは開いても表示できないので false。
        /// </summary>
        public bool CanOpenMissing { get; init; }

        public bool IsRepliesHeader { get; init; }

        public bool IsTweet => Tweet is not null;

        public bool IsMissing => MissingTweetId is not null;

        /// <summary>上の行とつなぐ線（返信先が上の行）。</summary>
        public bool HasLineAbove
        {
            get => _hasLineAbove;
            set
            {
                if (_hasLineAbove != value)
                {
                    _hasLineAbove = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasLineAbove)));
                }
            }
        }

        /// <summary>下の行とつなぐ線（下の行がこの行への返信）。</summary>
        public bool HasLineBelow
        {
            get => _hasLineBelow;
            set
            {
                if (_hasLineBelow != value)
                {
                    _hasLineBelow = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasLineBelow)));
                }
            }
        }

        /// <summary>返信の見出しの下に「返信はありません」を出す（返信が 1 件も無いと確定したとき）。</summary>
        public bool ShowNoReplies
        {
            get => _showNoReplies;
            set
            {
                if (_showNoReplies != value)
                {
                    _showNoReplies = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowNoReplies)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// 会話ページ（ツイートをクリックしたときの詳細）の ViewModel。GET /tweets/{tweet_id}/conversation。
    /// 上から返信元の流れ（古い順）、開いたツイート、返信（X の会話のまとまりごと）を並べる。
    /// ページを開くたびに作り、Frame のナビゲーション引数として渡すので、戻ったときは読み込み済みの内容をそのまま使う。
    /// </summary>
    public sealed partial class ConversationViewModel : INotifyPropertyChanged, ITweetListActions
    {
        private readonly HttpClient _httpClient = new();
        private readonly HashSet<string> _seenIds = new(StringComparer.Ordinal);

        private bool _isLoading;
        private bool _isLoadingMore;
        private bool _hasLoaded;
        private bool _canLoadMore;
        private string _errorMessage = string.Empty;
        private string? _nextCursor;
        private int _replyCount;
        private ConversationEntry? _repliesHeader;

        public ConversationViewModel(string tweetId)
        {
            TweetId = tweetId;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        /// <summary>開いたツイートの ID。</summary>
        public string TweetId { get; }

        public ObservableCollection<ConversationEntry> Entries { get; } = [];

        public ConversationEntry? FocalEntry { get; private set; }

        public double ScrollVerticalOffset { get; set; }

        /// <summary>開いたツイートの位置までスクロールしたか。戻ってきたときは保存したスクロール位置を使う。</summary>
        public bool HasPositioned { get; set; }

        public bool IsLoading
        {
            get => _isLoading;
            private set
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
            private set
            {
                if (_isLoadingMore != value)
                {
                    _isLoadingMore = value;
                    OnPropertyChanged(nameof(IsLoadingMore));
                    UpdateNoReplies();
                }
            }
        }

        public bool CanLoadMore
        {
            get => _canLoadMore;
            private set
            {
                if (_canLoadMore != value)
                {
                    _canLoadMore = value;
                    OnPropertyChanged(nameof(CanLoadMore));
                    UpdateNoReplies();
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
                    OnPropertyChanged(nameof(HasError));
                    OnPropertyChanged(nameof(IsContentVisible));
                }
            }
        }

        public bool HasError => ErrorMessage.Length > 0;

        public bool IsContentVisible => FocalEntry is not null && !IsLoading && !HasError;

        /// <summary>
        /// 返信が 1 件も無いと確定したら、返信の見出しの下に「返信はありません」を出す。
        /// ListView の Footer は会話が短いと画面の最下部に置かれ、見出しから離れてしまうので見出しの行に出す。
        /// </summary>
        private void UpdateNoReplies()
        {
            if (_repliesHeader is not null)
            {
                _repliesHeader.ShowNoReplies = !IsLoadingMore && !CanLoadMore && _replyCount == 0;
            }
        }

        /// <summary>会話全体を取得する（1 回だけ）。</summary>
        public async Task LoadAsync()
        {
            if (IsLoading || _hasLoaded)
            {
                return;
            }

            IsLoading = true;
            ErrorMessage = string.Empty;
            try
            {
                var payload = await FetchAsync(null);
                if (payload?.tweet is null || string.IsNullOrEmpty(payload.tweet.id))
                {
                    ErrorMessage = payload?.error ?? "会話を読み込めませんでした";
                    return;
                }

                BuildEntries(payload);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"読み込みエラー: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Conversation Load Error: {ex.Message}");
            }
            finally
            {
                _hasLoaded = true;
                IsLoading = false;
            }
        }

        /// <summary>返信の続きを取得する。</summary>
        public async Task LoadMoreRepliesAsync()
        {
            if (IsLoading || IsLoadingMore || !CanLoadMore || string.IsNullOrEmpty(_nextCursor))
            {
                return;
            }

            IsLoadingMore = true;
            var requestedCursor = _nextCursor;
            try
            {
                var payload = await FetchAsync(requestedCursor);
                if (!string.IsNullOrEmpty(payload?.error))
                {
                    // 続きの取得失敗は、取得済みの会話を残したまま通知だけする（スクロールのたびに再送しない）。
                    AppNotifications.Error($"返信の続きを読み込めませんでした: {payload.error}");
                    CanLoadMore = false;
                    return;
                }

                var added = AddReplyThreads(payload?.replies);
                _nextCursor = payload?.next_cursor;

                // 新規0件、またはカーソルが進まない場合は打ち切り（同一ページの再取得ループ防止）
                CanLoadMore = added > 0
                    && !string.IsNullOrEmpty(_nextCursor)
                    && !string.Equals(_nextCursor, requestedCursor, StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Conversation LoadMore Error: {ex.Message}");
                AppNotifications.Error($"返信の続きを読み込めませんでした: {ex.Message}");
                CanLoadMore = false;
            }
            finally
            {
                IsLoadingMore = false;
            }
        }

        private async Task<ConversationApiResponse?> FetchAsync(string? cursor)
        {
            var url = $"http://localhost:8000/tweets/{Uri.EscapeDataString(TweetId)}/conversation";
            if (!string.IsNullOrEmpty(cursor))
            {
                url += $"?cursor={Uri.EscapeDataString(cursor)}";
            }

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<ConversationApiResponse>(json);
        }

        private void BuildEntries(ConversationApiResponse payload)
        {
            Entries.Clear();
            _seenIds.Clear();
            _replyCount = 0;

            ConversationEntry? previous = null;
            foreach (var dto in payload.ancestors ?? [])
            {
                previous = AppendChainLink(dto, previous, isFocal: false);
            }

            FocalEntry = AppendChainLink(payload.tweet!, previous, isFocal: true);
            OnPropertyChanged(nameof(FocalEntry));
            OnPropertyChanged(nameof(IsContentVisible));

            _repliesHeader = new ConversationEntry { IsRepliesHeader = true };
            Entries.Add(_repliesHeader);

            AddReplyThreads(payload.replies);
            _nextCursor = payload.next_cursor;
            CanLoadMore = !string.IsNullOrEmpty(_nextCursor);
        }

        /// <summary>返信元の流れに 1 件足す。返信先を X が返さなかった場合は、その位置に「取得できなかったツイート」を挟む。</summary>
        private ConversationEntry AppendChainLink(ConversationTweetDto dto, ConversationEntry? previous, bool isFocal)
        {
            if (!string.IsNullOrEmpty(dto.missing_parent_id))
            {
                previous = Append(new ConversationEntry { MissingTweetId = dto.missing_parent_id, CanOpenMissing = true }, previous);
            }

            if (!string.IsNullOrEmpty(dto.id))
            {
                _seenIds.Add(dto.id);
            }

            if (dto.is_unavailable)
            {
                return Append(new ConversationEntry
                {
                    MissingTweetId = dto.id ?? string.Empty,
                    MissingText = string.IsNullOrWhiteSpace(dto.unavailable_text) ? "このツイートは表示できません" : dto.unavailable_text,
                }, previous);
            }

            return Append(new ConversationEntry { Tweet = TweetViewModel.FromDto(dto), IsFocal = isFocal }, previous);
        }

        /// <summary>行を末尾に足し、<paramref name="replyTo"/>（返信先の行）があれば線でつなぐ。</summary>
        private ConversationEntry Append(ConversationEntry entry, ConversationEntry? replyTo)
        {
            if (replyTo is not null)
            {
                replyTo.HasLineBelow = true;
                entry.HasLineAbove = true;
            }

            Entries.Add(entry);
            return entry;
        }

        /// <summary>返信を会話のまとまりごとに足す。まとまりの中は上から順に返信の連鎖なので線でつなぐ。</summary>
        private int AddReplyThreads(List<ReplyThreadDto>? threads)
        {
            var added = 0;
            foreach (var thread in threads ?? [])
            {
                ConversationEntry? previous = null;
                foreach (var dto in thread.tweets ?? [])
                {
                    if (string.IsNullOrEmpty(dto.id) || !_seenIds.Add(dto.id))
                    {
                        continue;
                    }

                    previous = Append(new ConversationEntry { Tweet = TweetViewModel.FromDto(dto) }, previous);
                    added++;
                }
            }

            _replyCount += added;
            UpdateNoReplies();
            return added;
        }

        public TweetViewModel? FindTweetById(string tweetId)
            => Entries
                .Select(entry => entry.Tweet)
                .FirstOrDefault(tweet => tweet is not null && string.Equals(tweet.Id, tweetId, StringComparison.Ordinal));

        public Task<ActionOutcome> LikeTweetAsync(string tweetId, bool currentlyLiked)
            => TweetActionClient.LikeAsync(_httpClient, tweetId, currentlyLiked);

        public Task<ActionOutcome> RetweetTweetAsync(string tweetId, bool currentlyRetweeted)
            => TweetActionClient.RetweetAsync(_httpClient, tweetId, currentlyRetweeted);

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

        /// <summary>
        /// 開いたツイートへの返信は返信の先頭、返信欄のツイートへの返信はその直下に線でつないで出す。
        /// 返信元の流れのツイートへの返信は、この会話の流れには入らないので一覧には足さない。
        /// </summary>
        public void AddReplyToTimeline(TweetViewModel originalVm, string newTweetId, string replyText)
        {
            originalVm.NoteReplyPosted();

            var index = Entries.ToList().FindIndex(entry => ReferenceEquals(entry.Tweet, originalVm));
            var headerIndex = _repliesHeader is null ? -1 : Entries.IndexOf(_repliesHeader);
            if (index < 0 || headerIndex < 0 || (index < headerIndex && !Entries[index].IsFocal))
            {
                return;
            }

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
            _seenIds.Add(newTweetId);

            if (Entries[index].IsFocal)
            {
                Entries.Insert(headerIndex + 1, new ConversationEntry { Tweet = replyVm });
            }
            else
            {
                var original = Entries[index];
                var reply = new ConversationEntry { Tweet = replyVm, HasLineAbove = true, HasLineBelow = original.HasLineBelow };
                original.HasLineBelow = true;
                Entries.Insert(index + 1, reply);
            }

            _replyCount++;
            UpdateNoReplies();
        }

        /// <summary>引用ツイートは会話の一部ではないので一覧には足さない。</summary>
        public void AddQuoteToTimeline(TweetViewModel originalVm, string newTweetId, string quoteText)
        {
            _ = originalVm;
            _ = newTweetId;
            _ = quoteText;
        }

        private sealed class ConversationApiResponse
        {
            public List<ConversationTweetDto>? ancestors { get; set; }
            public ConversationTweetDto? tweet { get; set; }
            public List<ReplyThreadDto>? replies { get; set; }
            public string? next_cursor { get; set; }
            public string? error { get; set; }
        }

        private sealed class ConversationTweetDto : TweetDto
        {
            /// <summary>返信先のうち X が返さなかったツイートの ID（返信先が直前のツイートなら無し）。</summary>
            public string? missing_parent_id { get; set; }

            /// <summary>削除・凍結などで見られない返信元（<see cref="unavailable_text"/> が X の説明文）。</summary>
            public bool is_unavailable { get; set; }

            public string? unavailable_text { get; set; }
        }

        private sealed class ReplyThreadDto
        {
            public List<TweetDto>? tweets { get; set; }
        }
    }
}
