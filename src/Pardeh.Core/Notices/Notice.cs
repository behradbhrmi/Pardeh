namespace Pardeh.Core.Notices;

/// <summary>The group of an on-screen message. The user can turn groups off; <see cref="Problem"/> has no switch of its own.</summary>
public enum NoticeCategory
{
    Position,
    Playback,
    Sound,
    Tracks,
    Picture,
    Library,
    SongTitle,
    Problem,
}

public enum NoticeIcon
{
    Info,
    Done,
    Warning,
    Play,
    Pause,
    Stop,
    Previous,
    Next,
    FastForward,
    Rewind,
    Speed,
    Shuffle,
    Repeat,
    Sleep,
    Volume,
    Muted,
    Balance,
    Equalizer,
    AudioTrack,
    Subtitles,
    Lyrics,
    Picture,
    FullScreen,
    Window,
    Queue,
    Heart,
    Marker,
    Music,
    Video,
}

/// <summary>Where playback is, for messages that draw a progress bar. Times are in seconds; a duration of 0 is a live stream.</summary>
public sealed record NoticeTimeline(double Position, double Duration, double? LoopStart = null, double? LoopEnd = null)
{
    public double Remaining => Duration > 0 ? Math.Max(0, Duration - Position) : 0;

    /// <summary>0 to 1; 0 for a live stream.</summary>
    public double Fraction => Duration > 0 ? Math.Clamp(Position / Duration, 0, 1) : 0;
}

/// <summary>
/// One on-screen message. <see cref="Key"/> says what it is about: a message with the key of the one showing updates it in place.
/// </summary>
public sealed record Notice(string Key, NoticeCategory Category, NoticeIcon Icon, string Title)
{
    /// <summary>The time a message stays, unless it says otherwise.</summary>
    public const double NormalSeconds = 2;

    /// <summary>A second, smaller line.</summary>
    public string? Detail { get; init; }

    /// <summary>The value at the end of the line ("65%", "+0.3 s").</summary>
    public string? Value { get; init; }

    /// <summary>A bar from 0 to 1.</summary>
    public double? Level { get; init; }

    /// <summary>The bar starts in the middle (balance, brightness).</summary>
    public bool Centered { get; init; }

    public NoticeTimeline? Timeline { get; init; }

    /// <summary>The timeline follows playback while the message shows (off for a place that is only being aimed at).</summary>
    public bool Follows { get; init; } = true;

    /// <summary>How long it stays at the normal setting.</summary>
    public double Seconds { get; init; } = NormalSeconds;

    /// <summary>It stays until it is released (scanning, a swipe, a track being read).</summary>
    public bool Held { get; init; }

    /// <summary>Nobody asked for it just now (a new track, the end of the queue): it does not cover a fresh problem.</summary>
    public bool Automatic { get; init; }
}
