using System;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Twikit;

namespace WinUI3Twikit.Bridge
{
    /// <summary>
    /// プロフィールの取得（旧 <c>get_user_profile_twikit.py</c> / <c>get_my_profile.py</c>）。
    /// </summary>
    internal static class ProfileService
    {
        public static string NormalizeScreenName(string? screenName)
        {
            var name = (screenName ?? string.Empty).Trim();
            if (name.StartsWith('@'))
            {
                name = name[1..];
            }

            return name.Trim();
        }

        public static JsonObject UserToProfileDict(User user)
        {
            string createdStr;
            try
            {
                createdStr = user.CreatedAtDatetime.ToOffset(TweetSerializer.Jst).ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                createdStr = string.IsNullOrEmpty(user.CreatedAt) ? "不明" : user.CreatedAt;
            }

            // プロフィールの URL（t.co ではなく展開後のもの）。
            string? website = null;
            foreach (var url in user.Urls)
            {
                website = url.ExpandedUrl ?? url.Url;
                if (website is not null)
                {
                    break;
                }
            }

            website ??= user.Url;

            return new JsonObject
            {
                ["id"] = user.Id,
                ["name"] = user.Name,
                ["screen_name"] = user.ScreenName,
                ["bio"] = user.Description,
                ["followers_count"] = user.FollowersCount,
                ["following_count"] = user.FollowingCount,
                ["location"] = user.Location,
                ["created_str"] = createdStr,
                ["profile_image_url"] = user.ProfileImageUrl,
                ["profile_banner_url"] = user.ProfileBannerUrl,
                ["statuses_count"] = user.StatusesCount,
                ["favourites_count"] = user.FavouritesCount,
                ["verified"] = user.IsBlueVerified || user.Verified,
                // FastAPI 版には無かった項目。MyProfileViewModel が website / url を任意で読む。
                ["url"] = website,
            };
        }

        /// <summary>ログイン中アカウントのプロフィール。</summary>
        public static async Task<JsonObject> GetOwnProfileAsync()
        {
            try
            {
                TwikitSession.Login();   // 中央集中ログインを使用
                var user = await TwikitSession.GetAuthenticatedUserAsync().ConfigureAwait(false);
                return UserToProfileDict(user);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"プロフィール取得エラー: {ex.Message}");
                return new JsonObject { ["error"] = ex.Message };
            }
        }

        /// <summary>任意ユーザーのプロフィール。</summary>
        public static async Task<JsonObject> GetUserProfileAsync(string screenName)
        {
            var name = NormalizeScreenName(screenName);
            if (name.Length == 0)
            {
                return new JsonObject { ["error"] = "ユーザー名が空です" };
            }

            try
            {
                TwikitSession.Login();
                Debug.WriteLine($"ユーザープロフィール取得中... screen_name={name}");
                var user = await TwikitSession.Client.GetUserByScreenNameAsync(name).ConfigureAwait(false);
                return UserToProfileDict(user);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ユーザープロフィール取得エラー: {ex.Message}");
                return new JsonObject { ["error"] = ex.Message };
            }
        }
    }
}
