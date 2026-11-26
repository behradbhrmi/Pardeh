using System.Diagnostics;
using Pardeh.Core.Playback;
using Pardeh.Ffmpeg;

namespace Pardeh.Nama;

/// <summary>
/// Nama: Pardeh's video. It holds the picture of the open media (FFmpeg, Media Foundation on Windows), shows each frame at
/// its time, and reads the subtitles inside a video file. The time comes from outside: the sound being heard, which the
/// owner gives as two functions, or its own timer for a video without sound (<see cref="StartWithoutSound"/>).
/// </summary>
/// <param name="soundPosition">The place in the sound being heard now.</param>
/// <param name="soundPlaying">Whether the sound is running (a stopped one does not move the picture on).</param>
internal sealed class NamaPlayer(Func<TimeSpan> soundPosition, Func<bool> soundPlaying) : IDisposable
{
    private TimedVideoDecoder? _video;
    private SilentClock? _silentClock;
    private Timer? _endWatcher;
    private double _rate = 1;

    /// <summary>A video without sound reached its end.</summary>
    public event EventHandler? Ended;

    public bool HasPicture => _video is not null;

    /// <summary>True when Nama keeps the time itself: a video without sound is open.</summary>
    public bool KeepsTime => _silentClock is not null;

    public (int Width, int Height) Size => _video is { } v ? (v.Width, v.Height) : (0, 0);

    public VideoFrame? CurrentFrame => _video?.Current;

    public string? DecoderName => _video?.DecoderName;

    /// <summary>Play speed. The picture follows the sound's clock; without sound, its own runs at this speed.</summary>
    public double Rate
    {
        set
        {
            _rate = value;
            _silentClock?.SetRate(_rate);
            ResetClock();
            if (_video is { } video)
                video.PlaybackRate = (float)_rate;
        }
    }

    /// <summary>The picture of a file, or null when it has none. Not shown until <see cref="Show"/>.</summary>
    public TimedVideoDecoder? OpenFile(string source)
    {
        var video = FfmpegLibrary.IsAvailable ? FfmpegVideoDecoder.TryOpen(source, Clock) : null;
        return video ?? PlatformVideo.TryOpenFallback(source, Clock);
    }

    /// <summary>The picture at a network address (its own connection), or null. Not shown until <see cref="Show"/>.</summary>
    public TimedVideoDecoder? OpenNetwork(string address, NetworkInput network) => FfmpegVideoDecoder.TryOpen(address, Clock, network);

    /// <summary>Makes an opened picture (or none) the current one. The one before must have been closed.</summary>
    public void Show(TimedVideoDecoder? video)
    {
        _video = video;
        if (video is not null)
            video.PlaybackRate = (float)_rate;
    }

    /// <summary>A video without sound: a timer drives the clock, started and stopped with <see cref="Play"/> and <see cref="Pause"/>.</summary>
    public void StartWithoutSound()
    {
        _silentClock = new SilentClock(_video!.Duration);
        _silentClock.SetRate(_rate);
        _endWatcher = new Timer(_ => CheckSilentEnd(), null, 200, 200);
    }

    /// <summary>The length by Nama's own timer (zero while the sound keeps the time).</summary>
    public TimeSpan Duration => _silentClock?.Duration ?? TimeSpan.Zero;

    /// <summary>The place by Nama's own timer; setting it seeks the picture. Only while <see cref="KeepsTime"/>.</summary>
    public TimeSpan Position
    {
        get => _silentClock?.Position ?? TimeSpan.Zero;
        set
        {
            if (_silentClock is not { } clock)
                return;
            clock.Position = value < TimeSpan.Zero ? TimeSpan.Zero : value > clock.Duration ? clock.Duration : value;
            _video?.RequestSeek();
        }
    }

    public void Play() => _silentClock?.Start();

    public void Pause() => _silentClock?.Pause();

    /// <summary>The sound moved to another place: the clock is lined up again and the picture there is found now.</summary>
    public void Resync()
    {
        ResetClock();
        _video?.RequestSeek();
    }

    public void Close()
    {
        _endWatcher?.Dispose();
        _endWatcher = null;
        _silentClock = null;
        _video?.Dispose();
        _video = null;
        _subtitleTracks = [];
        _source = null;
    }

    public void Dispose() => Close();

    // ---- Subtitles inside the file --------------------------------------------------------------------------------

    private volatile IReadOnlyList<SubtitleTrack> _subtitleTracks = [];
    private volatile string? _source;

    public IReadOnlyList<SubtitleTrack> SubtitleTracks => _subtitleTracks;

    /// <summary>The subtitle tracks are looked for in the background (a second probe of the file) so opening is not slowed by it.</summary>
    public void FindSubtitleTracks(string source)
    {
        _source = source;
        Task.Run(() =>
        {
            try
            {
                var found = FfmpegSubtitleReader.List(source);
                if (_source == source)
                    _subtitleTracks = found;
            }
            catch (Exception)
            {
                // A file FFmpeg cannot probe again simply has no subtitle tracks to offer.
            }
        });
    }

    public static IReadOnlyList<SubtitleCue>? ReadSubtitleTrack(SubtitleTrack track, CancellationToken cancel) =>
        FfmpegLibrary.IsAvailable ? FfmpegSubtitleReader.Extract(track.Source, track.Stream, cancel) : null;

    // ---- Smooth video clock ----------------------------------------------------------------------------
    // The audio position only moves when the output asks for more samples (every 40-100 ms with shared-mode WASAPI), so a video
    // timed straight off it shows frames in bursts. The picture is timed off a clock that runs smoothly between those steps (the wall
    // clock at the play speed) and is nudged towards the audio position; a seek, a speed change or a large error re-aligns it at once.
    private readonly Stopwatch _wall = Stopwatch.StartNew();
    private volatile bool _clockStale = true;
    private double _clockBaseSeconds, _clockBaseWall;
    private double _lastRawWall = -1, _lastRawSeconds;

    private void ResetClock() => _clockStale = true;

    private TimeSpan Clock()
    {
        if (_silentClock is { } clock)
            return clock.Position;

        var now = _wall.Elapsed.TotalSeconds;
        if (!soundPlaying())
        {
            _clockStale = true;
            return soundPosition();
        }

        // Read the audio position at most every 20 ms (it takes the audio lock).
        if (_lastRawWall < 0 || now - _lastRawWall >= 0.02 || _clockStale)
        {
            _lastRawSeconds = soundPosition().TotalSeconds;
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

    private void CheckSilentEnd()
    {
        if (_silentClock is { IsRunning: true } clock && clock.Position >= clock.Duration && clock.Duration > TimeSpan.Zero)
        {
            clock.Pause();
            Ended?.Invoke(this, EventArgs.Empty);
        }
    }

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
