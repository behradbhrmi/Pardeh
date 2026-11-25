using Pardeh.Core.Playback;

namespace Pardeh.Nava;

/// <summary>
/// Shared presentation loop for video decoders: decodes on its own thread and publishes each frame when the
/// playback clock reaches its timestamp. Late frames are dropped unconverted; clock jumps trigger a seek.
/// Subclasses only decode (all their calls happen on the decoder thread).
/// </summary>
internal abstract class TimedVideoDecoder : IDisposable
{
    private static readonly TimeSpan LateThreshold = TimeSpan.FromMilliseconds(80);

    private readonly Func<TimeSpan> _clock;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private volatile bool _stop;
    private volatile VideoFrame? _current;
    private Exception? _openError;
    private long _sequence;
    private volatile bool _seekRequested;
    private bool _catchingUp;

    // A few frame buffers reused in turn (a new 4 MB array per frame made the garbage collector stutter).
    // The UI copies the newest frame within a tick, long before its buffer comes round again.
    private readonly byte[][] _buffers = new byte[4][];
    private int _nextBuffer;

    /// <summary>A reusable buffer for the next converted frame.</summary>
    protected byte[] RentFrameBuffer(int length)
    {
        var index = _nextBuffer;
        _nextBuffer = (_nextBuffer + 1) % _buffers.Length;
        if (_buffers[index]?.Length != length)
            _buffers[index] = new byte[length];
        return _buffers[index];
    }

    /// <summary>The playback position was set: find the new picture now rather than waiting to notice the jump.</summary>
    public void RequestSeek() => _seekRequested = true;

    /// <summary>The play speed (1 = normal); the loop shows no more than about 40 pictures per real second, so a fast speed does not flood the screen.</summary>
    public volatile float PlaybackRate = 1;

    private static readonly TimeSpan CatchUpSeek = TimeSpan.FromSeconds(4);

    /// <summary>Called when decoding falls behind the clock (true) or catches up (false); decoders may skip work.</summary>
    protected virtual void SetCatchUp(bool behind)
    {
    }

    protected TimedVideoDecoder(Func<TimeSpan> clock, string threadName)
    {
        _clock = clock;
        _thread = new Thread(Run) { IsBackground = true, Name = threadName };
    }

    public int Width { get; protected set; }

    public int Height { get; protected set; }

    public TimeSpan Duration { get; protected set; }

    public VideoFrame? Current => _current;

    /// <summary>True once the last frame has been shown.</summary>
    public bool Ended { get; private set; }

    public abstract string DecoderName { get; }

    /// <summary>Starts the thread and waits for the stream to open. False if there's no usable video.</summary>
    /// <param name="wait">How long opening may take (null: 10 seconds, enough for a file).</param>
    protected bool Start(TimeSpan? wait = null)
    {
        _thread.Start();
        return _ready.Wait(wait ?? TimeSpan.FromSeconds(10)) && _openError is null && Width > 0 && Height > 0;
    }

    /// <summary>Makes a call that waits (on the network) return, so that the thread can stop.</summary>
    protected virtual void Interrupt()
    {
    }

    public void Dispose()
    {
        _stop = true;
        Interrupt();
        if (_thread.IsAlive && Thread.CurrentThread != _thread)
            _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    /// <summary>Opens the video stream; throws if there is none.</summary>
    protected abstract void OpenStream();

    /// <summary>Decodes the next frame (kept pending). Returns false at end of stream.</summary>
    protected abstract bool DecodeNext(out TimeSpan time);

    /// <summary>Converts the pending frame to top-down BGRA (Width × Height).</summary>
    protected abstract byte[] ConvertPending();

    protected abstract void DropPending();

    protected abstract void SeekTo(TimeSpan position);

    protected abstract void CloseStream();

    private void Run()
    {
        try
        {
            OpenStream();
            _ready.Set();
            Loop();
        }
        catch (Exception ex)
        {
            _openError = ex;
            _ready.Set();
        }
        finally
        {
            try
            {
                CloseStream();
            }
            catch (Exception)
            {
                // Best effort.
            }
        }
    }

    private void Loop()
    {
        TimeSpan? pending = null;
        var lastShown = TimeSpan.MinValue;
        var endOfStream = false;

        while (!_stop)
        {
            var clock = _clock();

            // Seek detection: the clock jumped backwards, or far ahead of what we're decoding.
            var reference = pending ?? lastShown;
            if (_seekRequested || (lastShown != TimeSpan.MinValue &&
                (clock < lastShown - TimeSpan.FromMilliseconds(500) || clock > reference + CatchUpSeek)))
            {
                if (pending is not null)
                    DropPending();
                _seekRequested = false;
                pending = null;
                SeekTo(clock);
                lastShown = TimeSpan.MinValue;
                endOfStream = false;
                Ended = false;
                continue;
            }

            if (pending is null)
            {
                if (endOfStream)
                {
                    Ended = clock >= lastShown;
                    Thread.Sleep(15);
                    continue;
                }

                if (!DecodeNext(out var time))
                {
                    endOfStream = true;
                    continue;
                }

                var late = time + LateThreshold < clock;
                if (late != _catchingUp)
                {
                    _catchingUp = late;
                    SetCatchUp(late);
                }

                // Late (e.g. just after a seek): skip without converting. The first frame always shows.
                if (late && lastShown != TimeSpan.MinValue)
                {
                    DropPending();
                    lastShown = time;
                    continue;
                }

                pending = time;
            }

            var wait = pending.Value - clock;
            if (wait > TimeSpan.Zero && lastShown != TimeSpan.MinValue)
            {
                Thread.Sleep(wait > TimeSpan.FromMilliseconds(10) ? 10 : Math.Max(1, (int)wait.TotalMilliseconds));
                continue;
            }

            // Faster than the screen needs (a fast play speed): skip the picture without converting it.
            var rate = Math.Max(1f, PlaybackRate);
            if (rate > 1.01f && lastShown != TimeSpan.MinValue && pending.Value - lastShown < TimeSpan.FromSeconds(0.025 * rate) && pending.Value + LateThreshold >= clock)
            {
                DropPending();
                pending = null;
                continue;
            }

            var pixels = ConvertPending();
            DropPending();
            _current = new VideoFrame(Width, Height, pixels, Interlocked.Increment(ref _sequence)) { Time = pending.Value };
            lastShown = pending.Value;
            pending = null;
        }

        if (pending is not null)
            DropPending();
    }
}
