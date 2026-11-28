namespace Pardeh.Core.Playback;

/// <summary>The sleep timer and A-B repeat: small playback tools driven by <see cref="MediaPlayer.Tick"/>.</summary>
public sealed partial class MediaPlayer
{
    // ---- Sleep timer ------------------------------------------------------------------------------------

    /// <summary>The last seconds before the sleep timer ends are a fade-out of the volume.</summary>
    public const double SleepFadeSeconds = 12;

    /// <summary>The minutes offered by the sleep timer menus.</summary>
    public static readonly int[] SleepPresetMinutes = [5, 10, 15, 30, 45, 60, 90];

    private TimeSpan? _sleepRemaining;
    private bool _sleepAtEndOfTrack;

    /// <summary>Raised when the timer is started, cancelled or has run out (not on every tick).</summary>
    public event EventHandler? SleepTimerChanged;

    /// <summary>Time left until playback pauses, or null when no timed sleep timer runs.</summary>
    public TimeSpan? SleepRemaining => _sleepRemaining;

    /// <summary>The player pauses when the current track ends.</summary>
    public bool SleepAtEndOfTrack => _sleepAtEndOfTrack;

    public bool SleepTimerActive => _sleepRemaining is not null || _sleepAtEndOfTrack;

    /// <summary>Pauses playback after <paramref name="after"/> (the volume fades out over the last seconds).</summary>
    public void StartSleepTimer(TimeSpan after)
    {
        if (after <= TimeSpan.Zero)
        {
            CancelSleepTimer();
            return;
        }

        _sleepAtEndOfTrack = false;
        _sleepRemaining = after;
        SetStatus($"Sleep timer: {Math.Ceiling(after.TotalMinutes):0} min");
        SleepTimerChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Pauses playback when the current track has finished (the next one is loaded, not played).</summary>
    public void StartSleepAtEndOfTrack()
    {
        _sleepRemaining = null;
        _sleepAtEndOfTrack = true;
        SetStatus("Sleep timer: end of this track");
        SleepTimerChanged?.Invoke(this, EventArgs.Empty);
    }

    public void CancelSleepTimer()
    {
        var was = SleepTimerActive;
        _sleepRemaining = null;
        _sleepAtEndOfTrack = false;
        RestoreEngineVolume();
        if (was)
        {
            SetStatus("Sleep timer off");
            SleepTimerChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RestoreEngineVolume() => _engine.Volume = (float)(_volume / 100);

    /// <summary>The volume factor of the sleep fade at <paramref name="remaining"/> (1 until the last seconds, then down to 0).</summary>
    public static double SleepFade(TimeSpan remaining) => Math.Clamp(remaining.TotalSeconds / SleepFadeSeconds, 0, 1);

    private void TickSleepTimer(TimeSpan elapsed)
    {
        if (_sleepRemaining is not { } left)
            return;
        left -= elapsed;
        if (left <= TimeSpan.Zero)
        {
            _sleepRemaining = null;
            Pause();
            RestoreEngineVolume(); // the next Play starts at the user's volume, not at the faded one
            SetStatus("Sleep timer: paused");
            SleepTimerChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        _sleepRemaining = left;
        if (left.TotalSeconds <= SleepFadeSeconds && _playState == PlayState.Playing)
            _engine.Volume = (float)(_volume / 100 * SleepFade(left));
    }

    // ---- Volume levelling (ReplayGain) -----------------------------------------------------------------------

    private ReplayGainMode _replayGain;

    /// <summary>Evens out loudness between tracks: from the ReplayGain tags, else from the measured loudness (see <see cref="Loudness"/>).</summary>
    public ReplayGainMode ReplayGain
    {
        get => _replayGain;
        set
        {
            if (_replayGain == value)
                return;
            _replayGain = value;
            DropPrepared(); // it was prepared with the old loudness factor; it is prepared again on the next tick
            ApplyReplayGain();
        }
    }

    private void ApplyReplayGain()
    {
        RequestLoudness();
        _engine.SetLevelFactor((float)LevelFactorFor(CurrentMedia));
    }

    // ---- A-B repeat ---------------------------------------------------------------------------------------

    /// <summary>The start of the repeated section (seconds), or null.</summary>
    public double? LoopStart { get; private set; }

    /// <summary>The end of the repeated section (seconds), or null while only the start is set.</summary>
    public double? LoopEnd { get; private set; }

    /// <summary>A section is repeating.</summary>
    public bool SectionLoopActive => LoopStart is not null && LoopEnd is not null;

    /// <summary>Raised when a section start / end is set or cleared.</summary>
    public event EventHandler? SectionLoopChanged;

    /// <summary>
    /// One control for A-B repeat: the first use marks A at the current position, the second marks B and starts repeating A to B,
    /// the third clears the section. B must lie at least half a second after A (otherwise B is not set yet).
    /// </summary>
    public void CycleSectionLoop()
    {
        if (!_engine.IsOpen)
            return;
        var now = Snapped(CurrentPosition);
        if (LoopStart is null)
        {
            LoopStart = now;
            SetStatus($"A-B repeat: A at {FormatTime(now)}");
        }
        else if (LoopEnd is null)
        {
            if (now < LoopStart + 0.5)
                now = CurrentPosition; // the marker it would snap to is A itself
            if (now < LoopStart + 0.5)
                return;
            LoopEnd = now;
            SetStatus($"A-B repeat: {FormatTime(LoopStart.Value)} to {FormatTime(now)}");
        }
        else
        {
            ClearSectionLoop();
            return;
        }

        SectionLoopChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Marks A at the current position (the "Set A" button). A B that no longer lies at least half a second after it is forgotten.
    /// False when nothing is open.
    /// </summary>
    public bool SetSectionStart() => _engine.IsOpen && SetSectionStartAt(Snapped(CurrentPosition));

    private bool SetSectionStartAt(double now)
    {
        LoopStart = now;
        if (LoopEnd is { } end && end < now + 0.5)
            LoopEnd = null;
        SetStatus(LoopEnd is { } b ? $"A-B repeat: {FormatTime(now)} to {FormatTime(b)}" : $"A-B repeat: A at {FormatTime(now)}");
        SectionLoopChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Marks B at the current position and starts repeating (the "Set B" button). False without an A at least half a second before.</summary>
    public bool SetSectionEnd()
    {
        if (!_engine.IsOpen || LoopStart is not { } a)
            return false;
        var snapped = Snapped(CurrentPosition);
        return SetSectionEndAt(snapped >= a + 0.5 ? snapped : CurrentPosition);
    }

    private bool SetSectionEndAt(double now)
    {
        if (LoopStart is not { } a || now < a + 0.5)
            return false;
        LoopEnd = now;
        SetStatus($"A-B repeat: {FormatTime(a)} to {FormatTime(now)}");
        SectionLoopChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void ClearSectionLoop()
    {
        if (LoopStart is null && LoopEnd is null)
            return;
        LoopStart = LoopEnd = null;
        SetStatus("A-B repeat off");
        SectionLoopChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TickSectionLoop()
    {
        if (LoopStart is not { } a || LoopEnd is not { } b || _playState != PlayState.Playing)
            return;
        var position = CurrentPosition;
        if (position >= b)
            CurrentPosition = a; // past B: back to A (a seek to before A simply plays on up to B)
    }
}
