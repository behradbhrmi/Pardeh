namespace Pardeh.Core.Playback;

/// <summary>
/// Gapless playback: near the end of an audio track the engine is told what comes next, and when it has run on into it the player
/// only moves its own state forward (it does not open anything). Crossfading, the sleep timer's "end of track", A-B repeat, video and
/// streams keep the ordinary change of track.
/// </summary>
public sealed partial class MediaPlayer
{
    /// <summary>How long before the end the next track is prepared.</summary>
    public const double GaplessLeadSeconds = 10;

    private MediaItem? _prepared;
    private bool _gapless = true;

    /// <summary>Play consecutive tracks without a gap (where the engine and the files allow it).</summary>
    public bool Gapless
    {
        get => _gapless;
        set
        {
            _gapless = value;
            if (!value)
                DropPrepared();
        }
    }

    /// <summary>The track queued to follow without a gap, if any.</summary>
    public MediaItem? PreparedNext => _prepared;

    private void DropPrepared()
    {
        if (_prepared is null)
            return;
        _prepared = null;
        _engine.DropNext();
    }

    /// <summary>The item that may follow without a gap right now, or null.</summary>
    private MediaItem? GaplessCandidate()
    {
        if (!_gapless || (Equalizer.CrossFade && Equalizer.CrossFadeSeconds > 0) || _playState is not (PlayState.Playing or PlayState.Paused)
            || HasVideo || _sleepAtEndOfTrack || SectionLoopActive)
            return null;
        return CurrentPlaylist.PeekNext() is { } next && !Library.MediaLibrary.VideoExtensions.Contains(Path.GetExtension(next.Source)) ? next : null;
    }

    private void CheckGapless()
    {
        var next = GaplessCandidate();
        // Repeat, shuffle or the queue changed what comes next (or gapless no longer applies): what was prepared is forgotten.
        if (_prepared is not null && !ReferenceEquals(next, _prepared))
            DropPrepared();
        if (next is null || _prepared is not null || _playState != PlayState.Playing)
            return;
        var duration = Duration;
        if (duration <= 0 || CurrentPosition < duration - GaplessLeadSeconds * Math.Max(1, _rate))
            return;
        _prepared = next;
        _engine.PrepareNext(next.Source, (float)LevelFactorFor(next));
    }

    private void OnEngineAdvanced(object? sender, string source)
    {
        if (_context is null)
            OnEngineAdvancedCore(source);
        else
            _context.Post(_ => OnEngineAdvancedCore(source), null);
    }

    private void OnEngineAdvancedCore(string source)
    {
        var expected = _prepared;
        _prepared = null;
        if (_playState is PlayState.Stopped or PlayState.Ready)
            return;
        // Something changed in the moment between the engine moving on and this call: open the right track the ordinary way.
        if (expected is null || expected.Source != source || !ReferenceEquals(CurrentPlaylist.PeekNext(), expected) || !CurrentPlaylist.MoveNext())
        {
            if (CurrentPlaylist.MoveNext())
                OpenCurrentAndPlay();
            else
                StopInternal(PlayState.Stopped);
            return;
        }

        var wasPaused = _playState == PlayState.Paused;
        SetOpenState(OpenState.MediaChanging);
        SetPlayState(PlayState.Transitioning);
        BeginItem(expected);
        SetOpenState(OpenState.MediaOpening);
        ApplyReplayGain();
        if (expected.Duration == TimeSpan.Zero)
            expected.Duration = _engine.Duration;
        CurrentMediaChanged?.Invoke(this, EventArgs.Empty);
        SetOpenState(OpenState.MediaOpen);
        ApplyMute();
        SetPlayState(wasPaused ? PlayState.Paused : PlayState.Playing);
        SetStatus(wasPaused ? "Paused" : "Playing");
    }
}
