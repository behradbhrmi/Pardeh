namespace Pardeh.Core.Playback;

/// <summary>Volume levelling for files without ReplayGain tags: their loudness is measured in the background.</summary>
public sealed partial class MediaPlayer
{
    private LoudnessService? _loudness;

    /// <summary>Measures untagged tracks while volume levelling is on (null: only the tags are used). The head sets it.</summary>
    public LoudnessService? Loudness
    {
        get => _loudness;
        set
        {
            if (_loudness is not null)
                _loudness.Measured -= OnLoudnessMeasured;
            _loudness = value;
            if (value is not null)
                value.Measured += OnLoudnessMeasured;
        }
    }

    private static bool HasGainTag(MediaItem item, ReplayGainMode mode) =>
        !double.IsNaN(Playback.ReplayGain.ParseDb(item.GetAttribute(Playback.ReplayGain.TrackGain)))
        || (mode == ReplayGainMode.Album && !double.IsNaN(Playback.ReplayGain.ParseDb(item.GetAttribute(Playback.ReplayGain.AlbumGain))));

    // Streams have no whole to measure, and reading a film through just for its sound would cost too much.
    private static bool CanMeasure(MediaItem item) =>
        !Library.MediaLibrary.VideoExtensions.Contains(Path.GetExtension(item.Source))
        && (Library.MediaSources.IsContent(item.Source) || !item.Source.Contains("://", StringComparison.Ordinal));

    /// <summary>
    /// The volume factor of an item under the current levelling: from its tags, else from its measured loudness (the track's own,
    /// also in album mode). 1 while it is still to be measured.
    /// </summary>
    public double LevelFactorFor(MediaItem? item)
    {
        if (item is null || _replayGain == ReplayGainMode.Off)
            return 1;
        if (HasGainTag(item, _replayGain) || _loudness is null || !CanMeasure(item))
            return Playback.ReplayGain.FactorFor(item, _replayGain);
        return _loudness.Find(item.Source)?.Factor ?? 1;
    }

    /// <summary>Queues the playing track, and the one after it, for measuring when they need it.</summary>
    private void RequestLoudness()
    {
        if (_loudness is null || _replayGain == ReplayGainMode.Off)
            return;
        if (CurrentMedia is { } current && !HasGainTag(current, _replayGain) && CanMeasure(current))
            _loudness.Request(current.Source, first: true);
        if (CurrentPlaylist.PeekNext() is { } next && !HasGainTag(next, _replayGain) && CanMeasure(next))
            _loudness.Request(next.Source);
    }

    private void OnLoudnessMeasured(string source)
    {
        void Apply()
        {
            if (CurrentMedia?.Source == source)
                ApplyReplayGain();
            if (_prepared?.Source == source)
                DropPrepared(); // prepared before its loudness was known; it is prepared again on the next tick
        }

        if (_context is null)
            Apply();
        else
            _context.Post(_ => Apply(), null);
    }
}
