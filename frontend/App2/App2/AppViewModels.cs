namespace WinUI3Twikit
{
    public sealed class AppViewModels
    {
        public TimelineViewModel Timeline { get; } = new();
        public SearchViewModel Search { get; } = new();
        public NotificationsViewModel Notifications { get; } = new();
        public ListsViewModel Lists { get; } = new();
        public ProfileSearchViewModel ProfileSearch { get; } = new();
        public MyProfileViewModel MyProfile { get; } = new();
    }
}
