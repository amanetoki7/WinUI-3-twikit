using System;

namespace WinUI3Twikit
{
    public enum AppNotificationSeverity
    {
        Informational,
        Success,
        Warning,
        Error,
    }

    public sealed record AppNotification(string Message, AppNotificationSeverity Severity);

    /// <summary>
    /// 画面上部の InfoBar に出す通知（WinUI-3-twikit-deck より移植）。
    /// どのスレッドから呼んでもよい（MainWindow 側で UI スレッドに載せ替える）。
    /// </summary>
    public static class AppNotifications
    {
        public static event Action<AppNotification>? Posted;

        public static void Show(string message, AppNotificationSeverity severity = AppNotificationSeverity.Informational)
            => Posted?.Invoke(new AppNotification(message, severity));

        public static void Error(string message) => Show(message, AppNotificationSeverity.Error);

        public static void Success(string message) => Show(message, AppNotificationSeverity.Success);
    }
}
