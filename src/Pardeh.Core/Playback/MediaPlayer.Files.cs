namespace Pardeh.Core.Playback;

/// <summary>What <see cref="MediaPlayer.ReleaseFile"/> let go of, so that it can be taken up again.</summary>
public sealed record FileRelease(Func<string, bool> Matches, MediaItem? Open, bool WasPlaying, double Position);

/// <summary>Letting go of a file while it is changed: a file that is playing can't be renamed or replaced.</summary>
public sealed partial class MediaPlayer
{
    private readonly List<Func<string, bool>> _beingChanged = [];

    /// <summary>True while <see cref="ResumeFile"/> raises its events: the track is the same one, not a new play.</summary>
    public bool IsResumingFile { get; private set; }

    private bool IsBeingChanged(string source) => _beingChanged.Any(matches => matches(source));

    /// <summary>True when the engine has the file open: it is the current track, or the one queued to follow without a gap.</summary>
    public bool IsFileInUse(Func<string, bool> matches) =>
        (CurrentMedia is { } current && _engine.IsOpen && matches(current.Source)) || (_prepared is { } next && matches(next.Source));

    /// <summary>Closes the file if the engine has it open. Playback of it stops until <see cref="ResumeFile"/>.</summary>
    public FileRelease ReleaseFile(Func<string, bool> matches)
    {
        _beingChanged.Add(matches);
        if (_prepared is { } next && matches(next.Source))
            DropPrepared();
        if (CurrentMedia is not { } current || !_engine.IsOpen || !matches(current.Source))
            return new FileRelease(matches, null, false, 0);

        var playing = _playState is PlayState.Playing or PlayState.ScanForward or PlayState.ScanReverse;
        var position = CurrentPosition;
        DropPrepared();
        StopInternal(PlayState.Stopped);
        _engine.Close();
        return new FileRelease(matches, current, playing, position);
    }

    /// <summary>
    /// Opens the released track again where it was, playing or paused as before. <paramref name="newSource"/> is where the
    /// file is now (the queue has been relinked by then); null when it is gone. Does nothing if the user has moved on.
    /// </summary>
    public void ResumeFile(FileRelease release, string? newSource)
    {
        _beingChanged.Remove(release.Matches);
        if (release.Open is not { } open || !ReferenceEquals(CurrentMedia, open) && CurrentMedia?.Source != newSource || _engine.IsOpen)
            return;

        if (newSource is null || CurrentPlaylist.Current is not { } item || item.Source != newSource)
        {
            if (newSource is null)
            {
                CurrentMedia = null;
                SetOpenState(OpenState.Undefined);
                CurrentMediaChanged?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        IsResumingFile = true;
        try
        {
            OpenCurrentAndPlay(0, release.WasPlaying, release.Position);
        }
        finally
        {
            IsResumingFile = false;
        }
    }

    /// <summary>A file in the queue was renamed or retagged: its entries get the new source and details, in place.</summary>
    public void RelinkQueue(Func<string, bool> matches, Func<MediaItem, MediaItem> replacement)
    {
        for (var i = 0; i < CurrentPlaylist.Count; i++)
        {
            var old = CurrentPlaylist.Items[i];
            if (!matches(old.Source))
                continue;
            var fresh = replacement(old);
            CurrentPlaylist.Replace(i, fresh);
            // The current item stays the current one (a released file is opened again from the queue).
            if (ReferenceEquals(CurrentMedia, old) && _engine.IsOpen)
                CurrentMedia = fresh;
        }
    }

    /// <summary>A file has left: its entries leave the queue.</summary>
    public void RemoveFromQueue(Func<string, bool> matches)
    {
        for (var i = CurrentPlaylist.Count - 1; i >= 0; i--)
            if (matches(CurrentPlaylist.Items[i].Source))
                CurrentPlaylist.RemoveAt(i);
    }
}
