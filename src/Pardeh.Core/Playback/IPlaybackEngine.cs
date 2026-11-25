namespace Pardeh.Core.Playback;

/// <summary>A low-level engine that decodes and outputs one media source at a time.</summary>
public interface IPlaybackEngine : IDisposable
{
    /// <summary>Opens a source. Throws if the source can't be opened.</summary>
    void Open(string source);

    void Close();

    /// <summary>True when <paramref name="source"/> is opened in two steps: a slow one off the interface thread, then <see cref="OpenPrepared"/>.</summary>
    bool OpensInBackground(string source) => false;

    /// <summary>
    /// The slow part of opening (connecting, reading the start). Safe to call on any thread; it must give up soon after
    /// <paramref name="cancel"/> is cancelled. Throws when the source can't be opened. The result is disposed if it is not used.
    /// </summary>
    IDisposable PrepareOpen(string source, NetworkOptions options, CancellationToken cancel) => throw new NotSupportedException();

    /// <summary>Makes what <see cref="PrepareOpen"/> returned the open media.</summary>
    void OpenPrepared(IDisposable prepared) => throw new NotSupportedException();

    /// <summary>True while network sound is waited for.</summary>
    bool IsBuffering => false;

    /// <summary>How full the buffer is while <see cref="IsBuffering"/>, 0 - 100.</summary>
    int BufferingPercent => 0;

    /// <summary>
    /// Gapless playback: opens <paramref name="source"/> in the background so it can follow the playing one without a gap, with its
    /// loudness factor. When it cannot (video, a stream, another sample rate) nothing happens and the current one simply ends.
    /// </summary>
    void PrepareNext(string source, float levelFactor)
    {
    }

    /// <summary>Forgets what <see cref="PrepareNext"/> prepared.</summary>
    void DropNext()
    {
    }

    /// <summary>Raised (on any thread) when a prepared source has taken over from the one before; the argument is its source.</summary>
    event EventHandler<string>? AdvancedToNext
    {
        add { }
        remove { }
    }

    /// <summary>Opens the next source while the current one fades out (crossfading). Default: a plain open.</summary>
    void OpenCrossfade(string source, double seconds) => Open(source);

    void Play();

    void Pause();

    void Stop();

    bool IsOpen { get; }

    TimeSpan Position { get; set; }

    TimeSpan Duration { get; }

    /// <summary>0.0 – 1.0.</summary>
    float Volume { get; set; }

    /// <summary>-1.0 (left) – 1.0 (right).</summary>
    float Balance { get; set; }

    bool Muted { get; set; }

    /// <summary>Play speed (1 = normal; 0.25 – 4), pitch kept. Engines that can't change speed ignore it.</summary>
    double Rate
    {
        get => 1;
        set { }
    }

    /// <summary>Equalizer the engine applies to its output.</summary>
    EqualizerSettings Equalizer { get; }

    /// <summary>Raised (on any thread) when the source plays to its end.</summary>
    event EventHandler? PlaybackEnded;

    /// <summary>Fills <paramref name="bins"/> with the current spectrum (0 – 1). Returns false if nothing is playing.</summary>
    bool GetSpectrum(Span<float> bins);

    /// <summary>Fills <paramref name="samples"/> with the latest mono waveform (-1 … 1). Returns false if nothing is playing.</summary>
    /// <summary>The audio tracks of the open media (inside the file, plus separate audio files added).</summary>
    IReadOnlyList<AudioTrack> AudioTracks => [];

    /// <summary>Index into <see cref="AudioTracks"/> of the track playing, or -1.</summary>
    int CurrentAudioTrack => -1;

    /// <summary>The chapters of the open media (empty if it has none or the engine can't read them).</summary>
    IReadOnlyList<Chapter> Chapters => [];

    /// <summary>Switches to another audio track at the same position.</summary>
    void SelectAudioTrack(int index)
    {
    }

    /// <summary>A loudness factor (ReplayGain) applied to the sound of the open media; 1 = unchanged.</summary>
    void SetLevelFactor(float factor)
    {
    }

    /// <summary>Adds a separate audio file (a dub) as a track and switches to it. False if it can't be played.</summary>
    bool AddAudioFile(string path) => false;

    /// <summary>The text subtitle tracks inside the open media (found in the background, so it may fill in a moment after opening).</summary>
    IReadOnlyList<SubtitleTrack> SubtitleTracks => [];

    /// <summary>Reads every cue of an embedded subtitle track (a pass over the file; call it off the interface thread). Null when it can't be read.</summary>
    IReadOnlyList<SubtitleCue>? ReadSubtitleTrack(SubtitleTrack track, CancellationToken cancel) => null;

    bool GetWaveform(Span<float> samples)
    {
        samples.Clear();
        return false;
    }

    /// <summary>Size of the open video, or (0, 0) for audio-only media.</summary>
    (int Width, int Height) VideoSize { get; }

    /// <summary>The frame to show now, or null.</summary>
    VideoFrame? CurrentVideoFrame { get; }
}

/// <summary>Reads tags and technical info for a media source.</summary>
public interface IMediaInfoReader
{
    MediaItem Read(string source);

    /// <summary>The cover picture embedded in the file (JPEG / PNG bytes), or null.</summary>
    byte[]? ReadPicture(string source) => null;
}

/// <summary>A chapter of a media file: a title and the time range it covers.</summary>
public sealed record Chapter(string Title, TimeSpan Start, TimeSpan End);

/// <summary>A text subtitle stream inside a media file.</summary>
public sealed record SubtitleTrack(string Source, int Stream, string Label);

/// <summary>An audio track: a stream inside the media file (Stream ≥ 0) or a separate audio file (a dub).</summary>
public sealed record AudioTrack(string Source, int Stream, string Label);
