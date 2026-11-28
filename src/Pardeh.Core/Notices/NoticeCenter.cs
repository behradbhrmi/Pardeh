namespace Pardeh.Core.Notices;

/// <summary>
/// Holds the one message showing and the rules for it: the newest replaces the one before, the same key updates in place,
/// a fresh problem is not wiped by what happens by itself, and each leaves after its time. Used from one thread.
/// </summary>
public sealed class NoticeCenter
{
    /// <summary>How long a problem is protected from messages that nobody asked for.</summary>
    public const long ProblemProtectedMs = 2500;

    public const long FadeInMs = 90;

    public const long FadeOutMs = 220;

    private readonly Func<long> _clock;
    private long _shownAt;
    private long _postedAt;
    private long _until;

    public NoticeCenter(Func<long>? clock = null) => _clock = clock ?? (static () => Environment.TickCount64);

    /// <summary>Whether a group is to be shown at all (the user's settings).</summary>
    public Func<NoticeCategory, bool> Allowed { get; set; } = static _ => true;

    /// <summary>The user's "time on screen": 1 is normal.</summary>
    public double DurationScale { get; set; } = 1;

    /// <summary>The message to draw now, or null.</summary>
    public Notice? Current
    {
        get
        {
            if (_current is not null && !_current.Held && _clock() >= _until)
                _current = null;
            return _current;
        }
    }

    private Notice? _current;

    /// <summary>Changes whenever there is something new to draw.</summary>
    public int Version { get; private set; }

    /// <summary>0 to 1: the message fades in when it appears and out as its time ends.</summary>
    public double Opacity
    {
        get
        {
            if (Current is not { } notice)
                return 0;
            var now = _clock();
            var appearing = Math.Clamp((now - _shownAt) / (double)FadeInMs, 0, 1);
            var leaving = notice.Held ? 1 : Math.Clamp((_until - now) / (double)FadeOutMs, 0, 1);
            return Math.Min(appearing, leaving);
        }
    }

    /// <summary>Shows a message. False when it is not shown: its group is off, or a fresh problem is in the way.</summary>
    public bool Post(Notice notice)
    {
        if (!Allowed(notice.Category))
            return false;
        var now = _clock();
        var showing = Current;
        if (showing is { Category: NoticeCategory.Problem } && notice.Category != NoticeCategory.Problem && notice.Automatic
            && now - _postedAt < ProblemProtectedMs)
            return false;
        // One that updates what is showing does not fade in again.
        if (showing is null || showing.Key != notice.Key)
            _shownAt = showing is null ? now : now - FadeInMs;
        _current = notice;
        _postedAt = now;
        _until = now + (long)(Math.Max(0.1, notice.Seconds) * 1000 * Math.Max(0.1, DurationScale));
        Version++;
        return true;
    }

    /// <summary>New figures for the message showing (the running time); it leaves when it would have.</summary>
    public void Refresh(Notice notice)
    {
        if (Current is not { } showing || showing.Key != notice.Key || showing == notice)
            return;
        _current = notice;
        Version++;
    }

    /// <summary>Lets a held message go; it fades out.</summary>
    public void Release(string key)
    {
        if (Current is not { } showing || showing.Key != key || !showing.Held)
            return;
        _current = showing with { Held = false };
        _until = _clock() + FadeOutMs;
        Version++;
    }

    public void Clear()
    {
        if (_current is null)
            return;
        _current = null;
        Version++;
    }

    /// <summary>The message showing, if it is about <paramref name="key"/>.</summary>
    public Notice? Showing(string key) => Current is { } notice && notice.Key == key ? notice : null;
}
