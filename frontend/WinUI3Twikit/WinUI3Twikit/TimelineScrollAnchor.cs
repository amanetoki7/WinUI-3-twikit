namespace WinUI3Twikit
{
    public readonly struct TimelineScrollAnchor
    {
        public string TweetDedupKey { get; init; }
        public double OffsetWithinItem { get; init; }

        public bool IsEmpty => string.IsNullOrEmpty(TweetDedupKey);

        public static TimelineScrollAnchor Empty => default;
    }

    internal readonly struct TimelineViewport
    {
        public TimelineScrollAnchor Anchor { get; init; }
        public bool AtTop { get; init; }
    }
}
