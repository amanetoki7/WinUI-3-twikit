using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace WinUI3Twikit
{
    public interface ITweetListActions
    {
        TweetViewModel? FindTweetById(string tweetId);
        Task<bool> LikeTweetAsync(string tweetId, bool currentlyLiked);
        Task<bool> RetweetTweetAsync(string tweetId);
        Task<HttpResponseMessage> ReplyTweetAsync(string tweetId, string replyText);
        Task<HttpResponseMessage> QuoteTweetAsync(string tweetId, string quoteText, IReadOnlyList<string> mediaPaths);
        void AddReplyToTimeline(TweetViewModel originalVm, string newTweetId, string replyText);
        void AddQuoteToTimeline(TweetViewModel originalVm, string newTweetId, string quoteText);
    }
}
