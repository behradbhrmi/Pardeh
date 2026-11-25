using NAudio.Wave;

namespace Pardeh.Nava;

/// <summary>
/// The audio of the playing track with, optionally, the next one waiting behind it. When the current reader runs out, the same
/// <see cref="Read"/> call carries on with the next one, so the output never sees an end: no silence and no click between the tracks.
/// Position and length are those of the reader being played. Not thread-safe: the engine serialises every member.
/// </summary>
internal sealed class GaplessSource(IAudioSource first) : IAudioSource
{
    private IAudioSource _current = first;
    private (IAudioSource Source, float Gain, Action Started)? _next;
    private bool _disposed;

    /// <summary>
    /// A factor on top of the output's loudness factor: 1, except between a switch and the moment the output takes over the new
    /// track's ReplayGain (it then calls <see cref="ResetGain"/>).
    /// </summary>
    private float _gain = 1f;

    public WaveFormat WaveFormat { get; } = first.WaveFormat;

    public TimeSpan TotalTime => _current.TotalTime;

    public string DecoderName => _current.DecoderName;

    public TimeSpan CurrentTime
    {
        get => _current.CurrentTime;
        set => _current.CurrentTime = value;
    }

    public bool HasNext => _next is not null;

    /// <summary>True when <paramref name="source"/> can follow without a gap: the same sample rate and channel count.</summary>
    public bool CanFollow(IAudioSource source) =>
        source.WaveFormat.SampleRate == WaveFormat.SampleRate && source.WaveFormat.Channels == WaveFormat.Channels && source.WaveFormat.Encoding == WaveFormat.Encoding;

    /// <summary>
    /// Queues the next reader (replacing one queued before). <paramref name="relativeGain"/> scales its samples; <paramref name="started"/>
    /// runs inside <see cref="Read"/> at the moment it takes over. Returns false (and leaves the reader to the caller) when it cannot follow.
    /// </summary>
    public bool SetNext(IAudioSource source, float relativeGain, Action started)
    {
        if (_disposed || !CanFollow(source))
            return false;
        DropNext();
        _next = (source, relativeGain, started);
        return true;
    }

    public void DropNext()
    {
        if (_next is { } waiting)
            waiting.Source.Dispose();
        _next = null;
    }

    public void ResetGain() => _gain = 1f;

    public int Read(float[] buffer, int offset, int count)
    {
        var filled = 0;
        while (filled < count && !_disposed)
        {
            var read = _current.Read(buffer, offset + filled, count - filled);
            if (read > 0)
            {
                if (_gain != 1f)
                {
                    for (var i = 0; i < read; i++)
                        buffer[offset + filled + i] *= _gain;
                }

                filled += read;
                continue;
            }

            if (_next is not { } following)
                break;
            var finished = _current;
            _current = following.Source;
            _gain = following.Gain;
            _next = null;
            finished.Dispose();
            following.Started();
        }

        return filled;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        DropNext();
        _current.Dispose();
    }
}
