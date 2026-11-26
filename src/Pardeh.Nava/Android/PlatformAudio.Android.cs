using Android.Media;
using NAudio.Wave;

namespace Pardeh.Nava;

/// <summary>Android choices: AudioTrack output. No platform decoder fallback yet (see the TODO below).</summary>
internal static partial class PlatformAudio
{
    public static IAudioOutput CreateOutput() => new AndroidAudioOutput();

    // TODO(android): fall back to MediaExtractor + MediaCodec when the FFmpeg libraries are missing.
    // Until then, without libav*.so in the APK (see docs/android-build.md) nothing can be decoded.
    public static IAudioSource? OpenFallbackAudio(string source) =>
        throw new PlatformNotSupportedException(
            "No decoder: the FFmpeg libraries (libav*.so) are not packaged in this build. " +
            "Place them in src/Pardeh.Android/lib/<abi>/ (see docs/android-build.md).");
}

/// <summary>
/// Streams float PCM into an <see cref="AudioTrack"/> from a background write loop. The blocking write is the
/// audio clock: the loop reads the next chunk from the pipeline only when the track has room.
/// </summary>
internal sealed class AndroidAudioOutput : IAudioOutput
{
    private readonly Lock _gate = new();
    private readonly ManualResetEventSlim _running = new(false);
    private AudioTrack? _track;
    private ISampleProvider? _source;
    private Thread? _thread;
    private volatile bool _cancel;
    private volatile PlaybackState _state = PlaybackState.Stopped;
    private double _bufferSeconds;

    public event EventHandler<StoppedEventArgs>? PlaybackStopped;

    public PlaybackState PlaybackState => _state;

    public void Init(ISampleProvider source)
    {
        var format = source.WaveFormat;
        var mask = format.Channels == 1 ? ChannelOut.Mono : ChannelOut.Stereo;
        var minBytes = AudioTrack.GetMinBufferSize(format.SampleRate, mask, Encoding.PcmFloat);
        var bytes = Math.Max(minBytes, format.SampleRate * format.Channels * sizeof(float) / 5); // ~200 ms
        _bufferSeconds = (double)bytes / (format.SampleRate * format.Channels * sizeof(float));

        var attributes = new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.Media)!
            .SetContentType(AudioContentType.Music)!
            .Build()!;
        var audioFormat = new AudioFormat.Builder()
            .SetEncoding(Encoding.PcmFloat)!
            .SetSampleRate(format.SampleRate)!
            .SetChannelMask(mask)!
            .Build()!;
        _track = new AudioTrack.Builder()
            .SetAudioAttributes(attributes)!
            .SetAudioFormat(audioFormat)!
            .SetBufferSizeInBytes(bytes)!
            .SetTransferMode(AudioTrackMode.Stream)!
            .Build();
        _source = source;
    }

    public void Play()
    {
        lock (_gate)
        {
            if (_track is null || _source is null || _state == PlaybackState.Playing)
                return;
            _track.Play();
            _state = PlaybackState.Playing;
            _running.Set();
            if (_thread is null)
            {
                _cancel = false;
                _thread = new Thread(WriteLoop) { IsBackground = true, Name = "Pardeh audio (AudioTrack)" };
                _thread.Start();
            }
        }
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (_state != PlaybackState.Playing)
                return;
            _state = PlaybackState.Paused;
            _running.Reset();
            _track?.Pause();
        }
    }

    public void Stop()
    {
        Thread? thread;
        lock (_gate)
        {
            if (_thread is null && _state == PlaybackState.Stopped)
                return;
            _cancel = true;
            _state = PlaybackState.Stopped;
            _running.Set(); // wake a paused loop so it can leave
            _track?.Pause();
            _track?.Flush();
            thread = _thread;
        }

        if (thread is not null && thread != Thread.CurrentThread)
            thread.Join();
    }

    private void WriteLoop()
    {
        var buffer = new float[4096];
        Exception? error = null;
        var ended = false;
        try
        {
            while (!_cancel)
            {
                _running.Wait();
                if (_cancel)
                    break;
                var count = _source!.Read(buffer, 0, buffer.Length);
                if (count <= 0)
                {
                    ended = true;
                    break;
                }

                // Blocking write: returns when the data is queued; a short write only happens on stop/release.
                var written = 0;
                while (written < count && !_cancel)
                {
                    var n = _track!.Write(buffer, written, count - written, WriteMode.Blocking);
                    if (n < 0)
                        throw new InvalidOperationException("AudioTrack write failed: " + n);
                    written += n;
                }
            }

            if (ended && !_cancel)
            {
                _track?.Stop(); // plays out what is queued
                Thread.Sleep(TimeSpan.FromSeconds(_bufferSeconds));
            }
        }
        catch (Exception ex)
        {
            error = ex;
        }

        lock (_gate)
        {
            _state = PlaybackState.Stopped;
            _thread = null;
        }

        PlaybackStopped?.Invoke(this, new StoppedEventArgs(error));
    }

    public void Dispose()
    {
        Stop();
        _track?.Release();
        _track?.Dispose();
        _track = null;
        _running.Dispose();
    }
}
