using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace WinUI3Twikit
{
    /// <summary>いいね・RT の送信結果。失敗したら <see cref="Error"/> に理由。</summary>
    public readonly record struct ActionOutcome(bool Ok, string? Error)
    {
        public static ActionOutcome Succeeded { get; } = new(true, null);

        public static ActionOutcome Failed(string error) => new(false, error);
    }

    public interface ITweetListActions
    {
        TweetViewModel? FindTweetById(string tweetId);
        Task<ActionOutcome> LikeTweetAsync(string tweetId, bool currentlyLiked);
        Task<ActionOutcome> RetweetTweetAsync(string tweetId, bool currentlyRetweeted);
        Task<HttpResponseMessage> ReplyTweetAsync(string tweetId, string replyText);
        Task<HttpResponseMessage> QuoteTweetAsync(string tweetId, string quoteText, IReadOnlyList<string> mediaPaths);
        void AddReplyToTimeline(TweetViewModel originalVm, string newTweetId, string replyText);
        void AddQuoteToTimeline(TweetViewModel originalVm, string newTweetId, string quoteText);
    }
}
