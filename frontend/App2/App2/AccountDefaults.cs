using System;

namespace WinUI3Twikit;

internal static class AccountDefaults
{
    public static string DisplayName =>
        Environment.GetEnvironmentVariable("X_DISPLAY_NAME") ?? string.Empty;

    public static string ScreenName =>
        Environment.GetEnvironmentVariable("X_SCREEN_NAME") ?? string.Empty;

    public static string ProfileImageUrl =>
        Environment.GetEnvironmentVariable("X_PROFILE_IMAGE_URL") ?? string.Empty;
}
