using System.Diagnostics;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Pardeh.Core.Playback;
using Pardeh.Nava.Ffmpeg;

namespace Pardeh.Nava;

/// <summary>
/// Nava: Pardeh's media engine. Audio: decode (FFmpeg, Media Foundation on Windows) → 10-band EQ → volume/balance →
/// an <see cref="IAudioOutput"/> (WASAPI on Windows, AudioTrack on Android), with a tap for the visualization spectrum.
/// Video: frames timed to the audio clock (or to a timer for video without sound).
/// </summary>
public sealed partial class NavaEngine : IPlaybackEngine
{
    /// <summary>Audio leaves the decoder this much before it's heard; video is shown that much later.</summary>
    private static readonly TimeSpan OutputLatency = TimeSpan.FromMilliseconds(150);

    private static readonly HashSet<string> AudioOnlyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wma", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".mid", ".midi", ".aif", ".aiff", ".ac3",
    };

    private readonly Lock _sync = new();
    private IAudioSource? _reader;
    private TimeStretcher? _stretcher;
    private double _rate = 1;

    /// <summary>Play speed, 0.25 – 4 (pitch kept). Video follows because it is timed off the audio position.</summary>
    public double Rate
    {
        get => _rate;
        set
        {
            _rate = Math.Clamp(value, 0.25, 4);
            lock (_sync)
            {
                if (_stretcher is not null)
                    _stretcher.Rate = _rate;
            }

            _silentClock?.SetRate(_rate);
            ResetVideoClock();
            if (_video is { } rateVideo)
                rateVideo.PlaybackRate = (float)_rate;
        }
    }
    private NavaPipeline? _pipeline;
    private IAudioOutput? _output;
    private TimedVideoDecoder? _video;
    private SilentClock? _silentClock;
    private Timer? _endWatcher;
    private bool _stopRequested;
    private float _volume = 0.5f;
    private float _balance;
    private bool _muted;

    public EqualizerSettings Equalizer { get; } = new();

    public event EventHandler? PlaybackEnded;

    public bool IsOpen => _reader is not null || _silentClock is not null;

    public TimeSpan Duration => _reader?.TotalTime ?? _silentClock?.Duration ?? TimeSpan.Zero;

    public (int Width, int Height) VideoSize => _video is { } v ? (v.Width, v.Height) : (0, 0);

    public VideoFrame? CurrentVideoFrame => _video?.Current;

    private static TimeSpan Max0(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t;

    public TimeSpan Position
    {
        get
        {
            if (_silentClock is { } clock)
                return clock.Position;
            if (Volatile.Read(ref _pendingSeek) is >= 0 and var seeking)
                return TimeSpan.FromSeconds(seeking); // a seek is in progress: show where it is going
            lock (_sync)
                return Max0((_reader?.CurrentTime ?? TimeSpan.Zero) - TimeSpan.FromSeconds(_stretcher?.PendingSourceSeconds ?? 0));
        }
        set
        {
            if (_silentClock is { } clock)
            {
                clock.Position = value < TimeSpan.Zero ? TimeSpan.Zero : value > clock.Duration ? clock.Duration : value;
                _video?.RequestSeek();
                return;
            }

            // The seek itself can take a while (a part of the file not read before comes off the disk, a stream needs its index): it runs
            // on a worker so the interface never waits for it, and the position reads as the target meanwhile. Newer targets replace older ones.
            if (_reader is not { } reader)
                return;
            var clamped = value < TimeSpan.Zero ? TimeSpan.Zero : value > reader.TotalTime ? reader.TotalTime : value;
            Volatile.Write(ref _pendingSeek, clamped.TotalSeconds);
            StartSeekWorker();
        }
    }

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
                            ResetVideoClock();
                        }
                    }

                    _video?.RequestSeek();
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

    public void Open(string source)
    {
        Close();

        var reader = OpenAudio(source, out var audioError);

        // Internet radio: audio only (a video probe would open a second connection to the stream).
        var isStream = source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        if (!isStream && !AudioOnlyExtensions.Contains(Path.GetExtension(source)))
        {
            _video = FfmpegLibrary.IsAvailable ? FfmpegVideoDecoder.TryOpen(source, VideoClock) : null;
            _video ??= PlatformAudio.TryOpenFallbackVideo(source, VideoClock);
            if (_video is { } openedVideo)
                openedVideo.PlaybackRate = (float)_rate;
        }

        // What the file before offered is gone (also for a video without sound, which leaves early below).
        _source = source;
        _tracks.Clear();
        _currentTrack = -1;
        _chapters = [];
        _subtitleTracks = [];
        if (_video is not null && FfmpegLibrary.IsAvailable && !isStream)
        {
            // The subtitle tracks are looked for in the background (a second probe of the file) so opening is not slowed by it.
            var probed = source;
            Task.Run(() =>
            {
                try
                {
                    var found = FfmpegSubtitleReader.List(probed);
                    if (_source == probed)
                        _subtitleTracks = found;
                }
                catch (Exception)
                {
                    // A file FFmpeg cannot probe again simply has no subtitle tracks to offer.
                }
            });
        }

        if (reader is null)
        {
            if (_video is null)
                throw audioError ?? new InvalidDataException("No playable audio or video stream.");

            // Video without sound: a timer drives the clock.
            _silentClock = new SilentClock(_video.Duration);
            _silentClock.SetRate(_rate);
            _endWatcher = new Timer(_ => CheckSilentEnd(), null, 200, 200);
            return;
        }

        try
        {
            BuildAudio(reader);
        }
        catch
        {
            reader.Dispose();
            _video?.Dispose();
            _video = null;
            throw;
        }

        // Audio tracks: the file's own (dubbed / original languages), then separate audio files next to it.
        if (FfmpegLibrary.IsAvailable && !isStream)
        {
            _chapters = FfmpegAudioSource.ListChapters(source);
            foreach (var (stream, label) in FfmpegAudioSource.ListTracks(source))
                _tracks.Add(new AudioTrack(source, stream, label));
            foreach (var file in SidecarAudio(source))
                _tracks.Add(new AudioTrack(file, -1, "File: " + Path.GetFileName(file)));
        }

        _currentTrack = reader is FfmpegAudioSource ffmpegReader ? _tracks.FindIndex(t => t.Source == source && t.Stream == ffmpegReader.StreamIndex) : -1;
    }

    private string? _source;
    private readonly List<AudioTrack> _tracks = [];
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

    // ---- Gapless playback ----------------------------------------------------------------------------------------

    private int _prepareGeneration;

    public event EventHandler<string>? AdvancedToNext;

    public void PrepareNext(string source, float levelFactor)
    {
        if (_reader is not GaplessSource chain || _video is not null || !AudioOnlyExtensions.Contains(Path.GetExtension(source))
            || NetworkMedia.IsNetwork(source))
            return;
        var generation = Interlocked.Increment(ref _prepareGeneration);
        var factor = Math.Clamp(levelFactor, 0.05f, 4f);
        Task.Run(() =>
        {
            var reader = OpenAudio(source, out _);
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
        _source = source;
        _chapters = chapters;
        _tracks.Clear();
        _tracks.AddRange(tracks);
        _currentTrack = reader is FfmpegAudioSource ffmpeg ? _tracks.FindIndex(t => t.Source == source && t.Stream == ffmpeg.StreamIndex) : -1;
        _subtitleTracks = [];
        Task.Run(() => AdvancedToNext?.Invoke(this, source)); // never call out from the audio thread
    }
    private int _currentTrack = -1;

    public IReadOnlyList<AudioTrack> AudioTracks => _tracks;

    private IReadOnlyList<Chapter> _chapters = [];

    public IReadOnlyList<Chapter> Chapters => _chapters;

    public int CurrentAudioTrack => _currentTrack;

    private volatile IReadOnlyList<SubtitleTrack> _subtitleTracks = [];

    public IReadOnlyList<SubtitleTrack> SubtitleTracks => _subtitleTracks;

    public IReadOnlyList<SubtitleCue>? ReadSubtitleTrack(SubtitleTrack track, CancellationToken cancel) =>
        FfmpegLibrary.IsAvailable ? FfmpegSubtitleReader.Extract(track.Source, track.Stream, cancel) : null;

    /// <summary>Plays another audio track (a dub, the original language …) from the same position.</summary>
    public void SelectAudioTrack(int index)
    {
        if (index < 0 || index >= _tracks.Count || index == _currentTrack || !FfmpegLibrary.IsAvailable)
            return;
        var track = _tracks[index];
        var position = Position;
        var playing = _output?.PlaybackState == PlaybackState.Playing;
        var reader = new FfmpegAudioSource(track.Source, track.Stream) { CurrentTime = position };

        // Replace the audio chain; the video keeps running and follows the new audio clock.
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

        BuildAudio(reader);
        _currentTrack = index;
        if (playing)
            _output?.Play();
    }

    /// <summary>Adds a separate audio file (e.g. a dub downloaded on its own) and switches to it.</summary>
    public bool AddAudioFile(string path)
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

        SelectAudioTrack(existing);
        return true;
    }

    private static readonly string[] AudioFileExtensions = [".mka", ".mp3", ".aac", ".ac3", ".eac3", ".dts", ".m4a", ".flac", ".ogg", ".opus", ".wav", ".wma"];

    /// <summary>Audio files named like the video (movie.fa.mka, movie.dub.ac3 …): dubs shipped separately.</summary>
    private static IEnumerable<string> SidecarAudio(string video)
    {
        if (Path.GetDirectoryName(video) is not { Length: > 0 } folder || AudioOnlyExtensions.Contains(Path.GetExtension(video)))
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

    /// <summary>reader → play speed → effects → WASAPI output.</summary>
    private void BuildAudio(IAudioSource opened)
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

        _output = output;
    }

    /// <summary>"FFmpeg" / "Media Foundation" for the open audio and video streams (diagnostics).</summary>
    public string DecoderInfo =>
        $"audio: {_reader?.DecoderName ?? "none"}, video: {_video?.DecoderName ?? "none"}, ffmpeg: {FfmpegLibrary.Version ?? FfmpegLibrary.LoadError ?? "not loaded"}";

    /// <summary>FFmpeg first (every format), Media Foundation as the fallback.</summary>
    private static IAudioSource? OpenAudio(string source, out Exception? error)
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


    private readonly List<(IAudioOutput Output, IAudioSource Reader)> _fading = [];

    /// <summary>
    /// Crossfading: the playing track keeps going while it fades out over <paramref name="seconds"/>, and
    /// <paramref name="source"/> opens in its own output, fading in. Falls back to a plain open for video.
    /// </summary>
    public void OpenCrossfade(string source, double seconds)
    {
        if (_output is null || _reader is null || _pipeline is null || _video is not null || seconds <= 0)
        {
            Open(source);
            return;
        }

        DropNext();
        var (output, reader, pipeline) = (_output, _reader, _pipeline);
        output.PlaybackStopped -= OnPlaybackStopped;
        pipeline.Effects.FadeTo(0, seconds);
        lock (_fading)
            _fading.Add((output, reader));
        _output = null;
        _reader = null;
        _pipeline = null;
        _stretcher = null;
        _ = Task.Delay(TimeSpan.FromSeconds(seconds + 0.3)).ContinueWith(_ => EndFade(output, reader));

        Open(source);
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
    public void Close()
    {
        Interlocked.Increment(ref _prepareGeneration); // a track being prepared belongs to what is closing
        _endWatcher?.Dispose();
        _endWatcher = null;
        _silentClock = null;
        Volatile.Write(ref _pendingSeek, -1);

        if (_output is not null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            _stopRequested = true;
            _output.Stop();
            _output.Dispose();
            _output = null;
        }

        _video?.Dispose();
        _video = null;

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
        _silentClock?.Start();
        if (_output is null)
            return;
        _stopRequested = false;
        _output.Play();
    }

    public void Pause()
    {
        _silentClock?.Pause();
        _output?.Pause();
    }

    public void Stop()
    {
        _silentClock?.Pause();
        if (_output is null)
            return;
        _stopRequested = true;
        _output.Stop();
    }

    public bool GetWaveform(Span<float> samples)
    {
        var pipeline = _pipeline;
        if (pipeline is null || _output?.PlaybackState != PlaybackState.Playing)
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
        if (pipeline is null || _output?.PlaybackState != PlaybackState.Playing)
        {
            bins.Clear();
            return false;
        }

        pipeline.Spectrum.Compute(bins);
        return true;
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

    // ---- Smooth video clock ----------------------------------------------------------------------------
    // The audio position only moves when the output asks for more samples (every 40-100 ms with shared-mode WASAPI), so a video
    // timed straight off it shows frames in bursts. The picture is timed off a clock that runs smoothly between those steps (the wall
    // clock at the play speed) and is nudged towards the audio position; a seek, a speed change or a large error re-aligns it at once.
    private readonly System.Diagnostics.Stopwatch _wall = System.Diagnostics.Stopwatch.StartNew();
    private volatile bool _clockStale = true;
    private double _clockBaseSeconds, _clockBaseWall;
    private double _lastRawWall = -1, _lastRawSeconds;

    private void ResetVideoClock() => _clockStale = true;

    private TimeSpan VideoClock()
    {
        if (_silentClock is { } clock)
            return clock.Position;

        var now = _wall.Elapsed.TotalSeconds;
        if (_output?.PlaybackState != PlaybackState.Playing)
        {
            _clockStale = true;
            return RawVideoClock();
        }

        // Read the audio position at most every 20 ms (it takes the audio lock).
        if (_lastRawWall < 0 || now - _lastRawWall >= 0.02 || _clockStale)
        {
            _lastRawSeconds = RawVideoClock().TotalSeconds;
            _lastRawWall = now;
        }

        var predicted = _clockBaseSeconds + (now - _clockBaseWall) * _rate;
        var error = _lastRawSeconds - predicted;
        if (_clockStale || Math.Abs(error) > 0.25)
        {
            _clockStale = false;
            _clockBaseSeconds = _lastRawSeconds;
            _clockBaseWall = now;
            return TimeSpan.FromSeconds(Math.Max(0, _lastRawSeconds));
        }

        // Follow the audio gently (the raw value steps, its average is the true position).
        _clockBaseSeconds += error * 0.04;
        return TimeSpan.FromSeconds(Math.Max(0, predicted + error * 0.04));
    }

    private TimeSpan RawVideoClock()
    {
        var position = Position - TimeSpan.FromSeconds(OutputLatency.TotalSeconds * _rate); // the delay is in seconds of sound heard, the position in seconds of the file
        return position < TimeSpan.Zero ? TimeSpan.Zero : position;
    }

    private void CheckSilentEnd()
    {
        if (_silentClock is { IsRunning: true } clock && clock.Position >= clock.Duration && clock.Duration > TimeSpan.Zero)
        {
            clock.Pause();
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // NAudio posts this event later: a previous track's output, or a stop followed by a quick restart
        // (Next / Previous), must not count as "the media ended" — that skipped on to the last track.
        if (!ReferenceEquals(sender, _output) || _output?.PlaybackState == PlaybackState.Playing)
            return;

        if (_stopRequested)
        {
            _stopRequested = false;
            return;
        }

        if (e.Exception is not null)
            PlaybackFailed?.Invoke(this, e.Exception);

        // The source ran out (or the output failed): treat as the end of the media.
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised (on the audio thread) when audio output stops because of an error.</summary>
    public event EventHandler<Exception>? PlaybackFailed;

    /// <summary>Playback clock for media without an audio stream.</summary>
    private sealed class SilentClock(TimeSpan duration)
    {
        private readonly Stopwatch _watch = new();
        private TimeSpan _offset;
        private double _rate = 1;

        public TimeSpan Duration { get; } = duration;

        public bool IsRunning => _watch.IsRunning;

        public TimeSpan Position
        {
            get => _offset + _watch.Elapsed * _rate;
            set
            {
                _offset = value;
                if (_watch.IsRunning)
                    _watch.Restart();
                else
                    _watch.Reset();
            }
        }

        public void Start() => _watch.Start();

        /// <summary>Play speed: time already played is kept, the rest runs at the new rate.</summary>
        public void SetRate(double rate)
        {
            Position = Position;
            _rate = rate;
        }

        public void Pause() => _watch.Stop();
    }
}
