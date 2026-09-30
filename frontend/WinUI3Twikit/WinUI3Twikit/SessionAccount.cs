using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WinUI3Twikit
{
    /// <summary>
    /// ログイン中アカウント。GET /profile（settings の screen_name）から取る。
    /// 取得できないときだけ X_DISPLAY_NAME などの環境変数を使う。
    /// </summary>
    internal static class SessionAccount
    {
        private static readonly HttpClient Http = new();
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static bool _loaded;

        public static string DisplayName { get; private set; } = string.Empty;
        public static string ScreenName { get; private set; } = string.Empty;
        public static string? ProfileImageUrl { get; private set; }
        public static bool IsVerified { get; private set; }

        public static async Task EnsureLoadedAsync()
        {
            if (_loaded)
            {
                return;
            }

            await Gate.WaitAsync();
            try
            {
                if (_loaded)
                {
                    return;
                }

                await LoadAsync();
            }
            finally
            {
                Gate.Release();
            }
        }

        public static void CopyAuthorTo(TweetViewModel tweet)
        {
            tweet.UserName = DisplayName;
            tweet.UserScreenName = ScreenName;
            tweet.UserProfileImage = ImageCache.GetAvatar(ProfileImageUrl);
            tweet.IsUserVerified = IsVerified;
        }

        private static async Task LoadAsync()
        {
            try
            {
                var response = await Http.GetAsync("http://localhost:8000/profile");
                response.EnsureSuccessStatusCode();

                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var errorElement)
                    && errorElement.ValueKind != JsonValueKind.Null
                    && !string.IsNullOrWhiteSpace(errorElement.ToString()))
                {
                    Debug.WriteLine($"SessionAccount: {errorElement}");
                    ApplyEnvironmentFallback();
                    return;
                }

                var name = GetString(root, "name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    DisplayName = name;
                }

                var screenName = GetString(root, "screen_name");
                if (!string.IsNullOrWhiteSpace(screenName))
                {
                    ScreenName = screenName.StartsWith('@') ? screenName : "@" + screenName;
                }

                var image = GetString(root, "profile_image_url");
                if (!string.IsNullOrWhiteSpace(image))
                {
                    ProfileImageUrl = image;
                }

                IsVerified = GetBool(root, "verified");

                _loaded = !string.IsNullOrWhiteSpace(DisplayName)
                    || !string.IsNullOrWhiteSpace(ScreenName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SessionAccount: {ex.Message}");
                ApplyEnvironmentFallback();
            }
        }

        private static void ApplyEnvironmentFallback()
        {
            if (string.IsNullOrWhiteSpace(DisplayName))
            {
                DisplayName = AccountDefaults.DisplayName;
            }

            if (string.IsNullOrWhiteSpace(ScreenName))
            {
                var screenName = AccountDefaults.ScreenName;
                if (!string.IsNullOrWhiteSpace(screenName))
                {
                    ScreenName = screenName.StartsWith('@') ? screenName : "@" + screenName;
                }
            }

            if (string.IsNullOrWhiteSpace(ProfileImageUrl)
                && !string.IsNullOrWhiteSpace(AccountDefaults.ProfileImageUrl))
            {
                ProfileImageUrl = AccountDefaults.ProfileImageUrl;
            }
        }

        private static string? GetString(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
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
    }
}
