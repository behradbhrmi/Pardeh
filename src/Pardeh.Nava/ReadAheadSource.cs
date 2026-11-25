using NAudio.Wave;

namespace Pardeh.Nava;

/// <summary>
/// Reads a network source ahead on its own thread into a ring of samples, so that neither the audio thread nor the
/// interface ever waits for the network. When the ring runs dry it gives silence and reports <see cref="IsBuffering"/>
/// until enough has arrived again. Seeking is done on the reading thread too. The inner source is only ever used there.
/// </summary>
internal sealed class ReadAheadSource : IAudioSource
{
    private const int Chunk = 8192;

    private readonly IAudioSource _inner;
    private readonly Action _interrupt;
    private readonly object _gate = new();
    private readonly float[] _ring;
    private readonly int _resumeAt;
    private readonly int _rate;
    private int _start;
    private int _count;
    private long _frames; // position of the next sample Read returns, in sample frames
    private TimeSpan? _seek;
    private bool _buffering = true;
    private bool _ended;
    private bool _stop;

    /// <param name="inner">The decoder; this object disposes it.</param>
    /// <param name="seconds">How much sound to hold.</param>
    /// <param name="interrupt">Makes a read that waits on the network return.</param>
    public ReadAheadSource(IAudioSource inner, double seconds, Action interrupt)
    {
        _inner = inner;
        _interrupt = interrupt;
        _rate = inner.WaveFormat.SampleRate;
        WaveFormat = inner.WaveFormat;
        TotalTime = inner.TotalTime;
        DecoderName = inner.DecoderName;
        _ring = new float[Math.Max(Chunk * 4, (int)(_rate * 2 * Math.Clamp(seconds, 0.5, 120)))];
        _resumeAt = Math.Min(_ring.Length / 2, _rate * 2 * 2); // two seconds, or half of a small buffer
        new Thread(Fill) { IsBackground = true, Name = "Pardeh network read-ahead" }.Start();
    }

    public WaveFormat WaveFormat { get; }

    public TimeSpan TotalTime { get; }

    public string DecoderName { get; }

    public bool IsBuffering
    {
        get
        {
            lock (_gate)
                return _buffering && !_ended;
        }
    }

    public int BufferingPercent
    {
        get
        {
            lock (_gate)
                return _ended ? 100 : (int)Math.Min(99, 100L * _count / Math.Max(1, _resumeAt));
        }
    }

    /// <summary>Waits until there is enough to start playing (or the source ended). False when cancelled or too slow.</summary>
    public bool WaitUntilReady(TimeSpan timeout, CancellationToken cancel)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        lock (_gate)
        {
            while (!_ended && _count < _resumeAt)
            {
                if (cancel.IsCancellationRequested || Environment.TickCount64 > deadline)
                    return false;
                Monitor.Wait(_gate, 50);
            }

            _buffering = false;
            return true;
        }
    }

    public TimeSpan CurrentTime
    {
        get
        {
            lock (_gate)
                return _seek ?? TimeSpan.FromSeconds((double)_frames / _rate);
        }
        set
        {
            if (TotalTime <= TimeSpan.Zero)
                return; // a live stream has nowhere to go
            lock (_gate)
            {
                _seek = value < TimeSpan.Zero ? TimeSpan.Zero : value;
                _frames = (long)(_seek.Value.TotalSeconds * _rate);
                _start = _count = 0;
                _buffering = true;
                _ended = false;
                Monitor.PulseAll(_gate);
            }
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (_gate)
        {
            if (_stop)
                return 0;
            if (_buffering && (_count >= _resumeAt || _ended))
                _buffering = false;

            var take = _buffering ? 0 : Math.Min(count, _count) & ~1;
            for (var i = 0; i < take; i++)
                buffer[offset + i] = _ring[(_start + i) % _ring.Length];
            _start = (_start + take) % _ring.Length;
            _count -= take;
            _frames += take / 2;
            if (take > 0)
                Monitor.PulseAll(_gate); // there is room again

            if (take == count)
                return take;
            if (_ended && _count < 2)
                return take; // the real end: a short read, then nothing
            _buffering = true;
            for (var i = take; i < count; i++)
                buffer[offset + i] = 0f;
            return count;
        }
    }

    private void Fill()
    {
        var chunk = new float[Chunk];
        try
        {
            while (true)
            {
                TimeSpan? seek;
                lock (_gate)
                {
                    while (!_stop && _seek is null && (_ended || _ring.Length - _count < Chunk))
                        Monitor.Wait(_gate, 200);
                    if (_stop)
                        return;
                    seek = _seek;
                }

                var read = 0;
                try
                {
                    if (seek is { } target)
                        _inner.CurrentTime = target;
                    read = _inner.Read(chunk, 0, chunk.Length);
                }
                catch (Exception)
                {
                    // A connection that failed: the sound ends here and the player decides whether to connect again.
                }

                lock (_gate)
                {
                    if (_stop)
                        return;
                    if (_seek != seek)
                        continue; // another seek arrived: what was just read belongs to the old place
                    _seek = null;
                    for (var i = 0; i < read; i++)
                        _ring[(_start + _count + i) % _ring.Length] = chunk[i];
                    _count += read;
                    if (read == 0)
                        _ended = true;
                    Monitor.PulseAll(_gate);
                }
            }
        }
        finally
        {
            _inner.Dispose(); // on the only thread that uses it
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_stop)
                return;
            _stop = true;
            Monitor.PulseAll(_gate);
        }

        _interrupt();
    }
}
