using NAudio.Wave;
using Pardeh.Core.Playback;
using Pardeh.Ffmpeg;

namespace Pardeh.Nava;

/// <summary>
/// Nava: Pardeh's audio. Decode (FFmpeg, Media Foundation on Windows) → play speed → 10-band EQ → volume/balance →
/// an <see cref="IAudioOutput"/> (WASAPI on Windows, AudioTrack on Android), with a tap for the visualization spectrum;
/// gapless and crossfaded changes of track, and the other audio tracks a file offers.
/// </summary>
internal sealed class NavaPlayer : IDisposable
{
    /// <summary>Audio leaves the decoder this much before it's heard.</summary>
    private static readonly TimeSpan OutputLatency = TimeSpan.FromMilliseconds(150);

    private static readonly HashSet<string> AudioOnlyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wma", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".mid", ".midi", ".aif", ".aiff", ".ac3",
    };

    private readonly Lock _sync = new();
    private IAudioSource? _reader;
    private TimeStretcher? _stretcher;
    private NavaPipeline? _pipeline;
    private IAudioOutput? _output;
    private ReadAheadSource? _readAhead;
    private bool _stopRequested;
    private double _rate = 1;
    private float _volume = 0.5f;
    private float _balance;
    private bool _muted;

    /// <summary>A file of a kind that never carries a picture.</summary>
    public static bool IsAudioFile(string path) => AudioOnlyExtensions.Contains(Path.GetExtension(path));

    public EqualizerSettings Equalizer { get; } = new();

    /// <summary>Raised when the sound ran out (or the output failed) without being asked to stop.</summary>
    public event EventHandler? Ended;

    /// <summary>Raised (on the audio thread) when audio output stops because of an error.</summary>
    public event EventHandler<Exception>? Failed;

    /// <summary>A seek has been carried out (raised on the seek worker).</summary>
    public event Action? Seeked;

    /// <summary>There is sound to play.</summary>
    public bool IsOpen => _reader is not null;

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

    public string? DecoderName => _reader?.DecoderName;

    public bool IsBuffering => _readAhead?.IsBuffering ?? false;

    public int BufferingPercent => _readAhead?.BufferingPercent ?? 100;

    /// <summary>Play speed (pitch kept).</summary>
    public double Rate
    {
        set
        {
            _rate = value;
            lock (_sync)
            {
                if (_stretcher is not null)
                    _stretcher.Rate = _rate;
            }
        }
    }

    private static TimeSpan Max0(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t;

    public TimeSpan Position
    {
        get
        {
            if (Volatile.Read(ref _pendingSeek) is >= 0 and var seeking)
                return TimeSpan.FromSeconds(seeking); // a seek is in progress: show where it is going
            lock (_sync)
                return Max0((_reader?.CurrentTime ?? TimeSpan.Zero) - TimeSpan.FromSeconds(_stretcher?.PendingSourceSeconds ?? 0));
        }
        set
        {
            // The seek itself can take a while (a part of the file not read before comes off the disk, a stream needs its index): it runs
            // on a worker so the interface never waits for it, and the position reads as the target meanwhile. Newer targets replace older ones.
            if (_reader is not { } reader)
                return;
            var clamped = value < TimeSpan.Zero ? TimeSpan.Zero : value > reader.TotalTime ? reader.TotalTime : value;
            Volatile.Write(ref _pendingSeek, clamped.TotalSeconds);
            StartSeekWorker();
        }
    }

    /// <summary>The place in the sound that is being heard now: behind <see cref="Position"/> by the output's delay. What a picture is timed to.</summary>
    public TimeSpan HeardPosition => Max0(Position - TimeSpan.FromSeconds(OutputLatency.TotalSeconds * _rate)); // the delay is in seconds of sound heard, the position in seconds of the file

    private double _pendingSeek = -1; // seconds; negative: none
    private int _seekWorker;

    private void StartSeekWorker()
    {
        if (Interlocked.Exchange(ref _seekWorker, 1) == 0)
            Task.Run(SeekWorker);
    }

    private void SeekWorker()
    {
        try
        {
            while (true)
            {
                var target = Volatile.Read(ref _pendingSeek);
                if (target < 0)
                    break;
                try
                {
                    lock (_sync)
                    {
                        if (_reader is { } reader)
                        {
                            reader.CurrentTime = TimeSpan.FromSeconds(target);
                            _stretcher?.Reset();
                        }
                    }

                    Seeked?.Invoke();
                }
                catch (Exception)
                {
                    // A reader closed or failing mid-seek: the next open or seek starts clean.
                }

                if (Interlocked.CompareExchange(ref _pendingSeek, -1, target) == target)
                    break;
            }
        }
        finally
        {
            Volatile.Write(ref _seekWorker, 0);
            if (Volatile.Read(ref _pendingSeek) >= 0)
                StartSeekWorker(); // a seek arrived as this one finished
        }
    }

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_pipeline is not null)
                _pipeline.Volume = _volume;
        }
    }

    public float Balance
    {
        get => _balance;
        set
        {
            _balance = Math.Clamp(value, -1f, 1f);
            if (_pipeline is not null)
                _pipeline.Balance = _balance;
        }
    }

    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            if (_pipeline is not null)
                _pipeline.Muted = value;
        }
    }

    private float _levelFactor = 1f;

    public void SetLevelFactor(float factor)
    {
        _levelFactor = Math.Clamp(factor, 0.05f, 4f);
        lock (_sync)
        {
            (_reader as GaplessSource)?.ResetGain(); // the output now carries the new track's loudness itself
            if (_pipeline is not null)
                _pipeline.Level = _levelFactor;
        }
    }

    /// <summary>FFmpeg first (every format), Media Foundation as the fallback. Null, with the reason, when there is no sound to play.</summary>
    public static IAudioSource? OpenSource(string source, out Exception? error)
    {
        error = null;
        if (FfmpegLibrary.IsAvailable)
        {
            try
            {
                return new FfmpegAudioSource(source);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        }

        try
        {
            return PlatformAudio.OpenFallbackAudio(source);
        }
        catch (Exception ex)
        {
            error ??= ex;
            return null;
        }
    }

    /// <summary>reader → play speed → effects → output. Takes the reader over; when it throws, the reader is still the caller's.</summary>
    public void Start(IAudioSource opened)
    {
        var reader = new GaplessSource(opened); // so that a following track can be joined on without a gap
        var stretcher = new TimeStretcher(reader) { Rate = _rate };
        var pipeline = new NavaPipeline(stretcher, Equalizer, _sync)
        {
            Volume = _volume,
            Balance = _balance,
            Muted = _muted,
            Level = _levelFactor,
        };

        var output = PlatformAudio.CreateOutput();
        output.Init(pipeline);
        output.PlaybackStopped += OnPlaybackStopped;

        lock (_sync)
        {
            _reader = reader;
            _stretcher = stretcher;
            _pipeline = pipeline;
        }

        _readAhead = opened as ReadAheadSource;
        _openedStream = (opened as FfmpegAudioSource)?.StreamIndex;
        _output = output;
    }

    /// <summary>Stops and lets go of the output; the first half of <see cref="Close"/>, so that the sound is silent before anything else is closed.</summary>
    public void CloseOutput()
    {
        Interlocked.Increment(ref _prepareGeneration); // a track being prepared belongs to what is closing
        Volatile.Write(ref _pendingSeek, -1);

        if (_output is not null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            _stopRequested = true;
            _output.Stop();
            _output.Dispose();
            _output = null;
        }
    }

    public void Close()
    {
        CloseOutput();
        lock (_sync)
        {
            _reader?.Dispose();
            _stretcher = null;
            _reader = null;
            _readAhead = null;
            _pipeline = null;
        }
    }

    public void Play()
    {
        if (_output is null)
            return;
        _stopRequested = false;
        _output.Play();
    }

    public void Pause() => _output?.Pause();

    public void Stop()
    {
        if (_output is null)
            return;
        _stopRequested = true;
        _output.Stop();
    }

    public bool GetWaveform(Span<float> samples)
    {
        var pipeline = _pipeline;
        if (pipeline is null || !IsPlaying)
        {
            samples.Clear();
            return false;
        }

        pipeline.Spectrum.CopyWaveform(samples);
        return true;
    }

    public bool GetSpectrum(Span<float> bins)
    {
        var pipeline = _pipeline;
        if (pipeline is null || !IsPlaying)
        {
            bins.Clear();
            return false;
        }

        pipeline.Spectrum.Compute(bins);
        return true;
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // NAudio posts this event later: a previous track's output, or a stop followed by a quick restart
        // (Next / Previous), must not count as "the media ended" — that skipped on to the last track.
        if (!ReferenceEquals(sender, _output) || IsPlaying)
            return;

        if (_stopRequested)
        {
            _stopRequested = false;
            return;
        }

        if (e.Exception is not null)
            Failed?.Invoke(this, e.Exception);

        // The source ran out (or the output failed): treat as the end of the media.
        Ended?.Invoke(this, EventArgs.Empty);
    }

    // ---- Audio tracks and chapters --------------------------------------------------------------------------------

    private readonly List<AudioTrack> _tracks = [];
    private int _currentTrack = -1;
    private IReadOnlyList<Chapter> _chapters = [];

    public IReadOnlyList<AudioTrack> Tracks => _tracks;

    public int CurrentTrack => _currentTrack;

    public IReadOnlyList<Chapter> Chapters => _chapters;

    public void ClearTracks()
    {
        _tracks.Clear();
        _currentTrack = -1;
        _chapters = [];
    }

    /// <summary>What the file being played offers: its chapters, its own audio tracks (dubbed / original languages), then separate audio files next to it.</summary>
    public void ListTracks(string source)
    {
        if (FfmpegLibrary.IsAvailable)
        {
            _chapters = FfmpegAudioSource.ListChapters(source);
            foreach (var (stream, label) in FfmpegAudioSource.ListTracks(source))
                _tracks.Add(new AudioTrack(source, stream, label));
            foreach (var file in SidecarAudio(source))
                _tracks.Add(new AudioTrack(file, -1, "File: " + Path.GetFileName(file)));
        }

        _currentTrack = _openedStream is { } playing ? _tracks.FindIndex(t => t.Source == source && t.Stream == playing) : -1;
    }

    /// <summary>The file's stream number of the sound <see cref="Start"/> was given, when FFmpeg decodes it.</summary>
    private int? _openedStream;

    /// <summary>Plays another audio track (a dub, the original language …) from the same position.</summary>
    public void SelectTrack(int index)
    {
        if (index < 0 || index >= _tracks.Count || index == _currentTrack || !FfmpegLibrary.IsAvailable)
            return;
        var track = _tracks[index];
        var position = Position;
        var playing = IsPlaying;
        var reader = new FfmpegAudioSource(track.Source, track.Stream) { CurrentTime = position };

        // Replace the audio chain; a picture keeps running and follows the new audio clock.
        if (_output is { } old)
        {
            old.PlaybackStopped -= OnPlaybackStopped;
            old.Stop();
            old.Dispose();
        }

        lock (_sync)
        {
            _reader?.Dispose();
            _reader = null;
        }

        Start(reader);
        _currentTrack = index;
        if (playing)
            _output?.Play();
    }

    /// <summary>Adds a separate audio file (e.g. a dub downloaded on its own) and switches to it.</summary>
    public bool AddFile(string path)
    {
        if (!FfmpegLibrary.IsAvailable || _reader is null)
            return false;
        try
        {
            FfmpegAudioSource.ListTracks(path).First(); // has audio?
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        var existing = _tracks.FindIndex(t => string.Equals(t.Source, path, StringComparison.OrdinalIgnoreCase));
        if (existing < 0)
        {
            _tracks.Add(new AudioTrack(path, -1, "File: " + Path.GetFileName(path)));
            existing = _tracks.Count - 1;
        }

        SelectTrack(existing);
        return true;
    }

    private static readonly string[] AudioFileExtensions = [".mka", ".mp3", ".aac", ".ac3", ".eac3", ".dts", ".m4a", ".flac", ".ogg", ".opus", ".wav", ".wma"];

    /// <summary>Audio files named like the video (movie.fa.mka, movie.dub.ac3 …): dubs shipped separately.</summary>
    private static IEnumerable<string> SidecarAudio(string video)
    {
        if (Path.GetDirectoryName(video) is not { Length: > 0 } folder || IsAudioFile(video))
            return [];
        try
        {
            var name = Path.GetFileNameWithoutExtension(video);
            return Directory.EnumerateFiles(folder, name + "*")
                .Where(f => AudioFileExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // ---- Gapless playback ----------------------------------------------------------------------------------------

    private int _prepareGeneration;

    /// <summary>Playback went on into the track given to <see cref="PrepareNext"/>.</summary>
    public event EventHandler<string>? AdvancedToNext;

    public void PrepareNext(string source, float levelFactor)
    {
        if (_reader is not GaplessSource chain || !IsAudioFile(source) || NetworkMedia.IsNetwork(source))
            return;
        var generation = Interlocked.Increment(ref _prepareGeneration);
        var factor = Math.Clamp(levelFactor, 0.05f, 4f);
        Task.Run(() =>
        {
            var reader = OpenSource(source, out _);
            if (reader is null)
                return;
            IReadOnlyList<Chapter> chapters = [];
            var tracks = new List<AudioTrack>();
            try
            {
                if (reader is FfmpegAudioSource)
                {
                    chapters = FfmpegAudioSource.ListChapters(source);
                    foreach (var (stream, label) in FfmpegAudioSource.ListTracks(source))
                        tracks.Add(new AudioTrack(source, stream, label));
                }
            }
            catch (Exception)
            {
                // The track still plays; it just offers no chapters or other audio tracks.
            }

            var accepted = false;
            lock (_sync)
            {
                if (generation == Volatile.Read(ref _prepareGeneration) && ReferenceEquals(_reader, chain))
                    accepted = chain.SetNext(reader, factor / _levelFactor, () => OnAdvanced(source, reader, chapters, tracks));
            }

            if (!accepted)
                reader.Dispose();
        });
    }

    public void DropNext()
    {
        Interlocked.Increment(ref _prepareGeneration);
        lock (_sync)
            (_reader as GaplessSource)?.DropNext();
    }

    // Runs on the audio thread, inside the read that crossed from one track to the next (under _sync).
    private void OnAdvanced(string source, IAudioSource reader, IReadOnlyList<Chapter> chapters, List<AudioTrack> tracks)
    {
        Volatile.Write(ref _pendingSeek, -1); // a seek still waiting belonged to the track that ended
        _chapters = chapters;
        _tracks.Clear();
        _tracks.AddRange(tracks);
        _currentTrack = reader is FfmpegAudioSource ffmpeg ? _tracks.FindIndex(t => t.Source == source && t.Stream == ffmpeg.StreamIndex) : -1;
        Task.Run(() => AdvancedToNext?.Invoke(this, source)); // never call out from the audio thread
    }

    // ---- Crossfading ---------------------------------------------------------------------------------------------

    private readonly List<(IAudioOutput Output, IAudioSource Reader)> _fading = [];

    /// <summary>Something is playing that could fade into a following track.</summary>
    public bool CanCrossfade => _output is not null && _reader is not null && _pipeline is not null;

    /// <summary>
    /// Sets the playing track aside to fade out over <paramref name="seconds"/> in its own output, then to be closed. The
    /// player is free for the next track at once, which <see cref="FadeIn"/> brings in.
    /// </summary>
    public void FadeOut(double seconds)
    {
        if (!CanCrossfade)
            return;
        DropNext();
        var (output, reader, pipeline) = (_output!, _reader!, _pipeline!);
        output.PlaybackStopped -= OnPlaybackStopped;
        pipeline.Effects.FadeTo(0, seconds);
        lock (_fading)
            _fading.Add((output, reader));
        _output = null;
        _reader = null;
        _pipeline = null;
        _stretcher = null;
        _ = Task.Delay(TimeSpan.FromSeconds(seconds + 0.3)).ContinueWith(_ => EndFade(output, reader));
    }

    public void FadeIn(double seconds)
    {
        if (_pipeline is { } fresh)
        {
            fresh.Effects.FadeTo(0, 0);
            fresh.Effects.FadeTo(1, seconds);
        }
    }

    private void EndFade(IAudioOutput output, IAudioSource reader)
    {
        lock (_fading)
        {
            if (_fading.RemoveAll(f => ReferenceEquals(f.Output, output)) == 0)
                return;
        }

        output.Stop();
        output.Dispose();
        lock (_sync)
            reader.Dispose();
    }

    public void Dispose()
    {
        Close();
        List<(IAudioOutput Output, IAudioSource Reader)> fading;
        lock (_fading)
            fading = _fading.ToList();
        foreach (var (output, reader) in fading)
            EndFade(output, reader);
    }
}
