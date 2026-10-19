namespace WinUI3Twikit
{
    /// <summary>
    /// ホームのタブとして追加したリスト 1 件。タブの並びは <see cref="TimelineViewModel.ListTabs"/> が持ち、
    /// <c>data/home_tabs.json</c>（<see cref="Bridge.HomeTabsStore"/>）に保存されて次回起動時に復元される。
    /// </summary>
    public sealed class HomeListTab
    {
        public string ListId { get; init; } = string.Empty;

        /// <summary>タブの見出し（追加した時点のリスト名）。</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary><see cref="TimelineViewModel"/> のタイムライン種別（<c>list:リストID</c>）。</summary>
        public string TimelineType => TimelineViewModel.ListTimelineType(ListId);
    }
}
