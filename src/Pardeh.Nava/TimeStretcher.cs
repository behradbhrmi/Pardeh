using NAudio.Wave;

namespace Pardeh.Nava;

/// <summary>
/// Play speed without changing pitch (0.25× – 4×): the audio is cut into ~80 ms pieces that are joined where they
/// match best and cross-faded (overlap-add with a similarity search, the method SoundTouch uses). At 1× it passes
/// samples straight through.
/// </summary>
internal sealed class TimeStretcher : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly int _sequence;   // frames per piece
    private readonly int _seek;       // frames searched for the best join
    private readonly int _overlap;    // frames cross-faded
    private readonly float[] _readBuffer;

    private float[] _input = new float[1 << 16];
    private int _inputFrames;
    private float[] _output = new float[1 << 16];
    private int _outputStart, _outputFrames;
    private readonly float[] _tail;
    private bool _haveTail;
    private double _skipFraction;
    private int _skipDebt; // input still to skip (fast speeds skip more than is buffered)
    private double _rate = 1;
    private bool _ended;

    public TimeStretcher(ISampleProvider source)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        var rate = source.WaveFormat.SampleRate;
        _sequence = rate * 82 / 1000;
        _seek = rate * 20 / 1000;
        _overlap = rate * 12 / 1000;
        _tail = new float[_overlap * _channels];
        _readBuffer = new float[4096 * _channels];
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>Playback speed (1 = normal). Changing it starts the next piece at the new speed.</summary>
    public double Rate
    {
        get => _rate;
        set
        {
            var rate = Math.Clamp(value, 0.25, 4);
            if (Math.Abs(rate - _rate) < 1e-6)
                return;
            _rate = rate;
            if (Math.Abs(rate - 1) < 1e-6)
                Reset(); // back to pass-through; the few buffered milliseconds are dropped
        }
    }

    /// <summary>
    /// Seconds of the source that were read from it but have not been heard yet (still in the stretcher's buffers). The reader's own
    /// position is this far ahead of the sound; 0 at normal speed.
    /// </summary>
    public double PendingSourceSeconds =>
        Math.Abs(_rate - 1) < 1e-6 && _inputFrames == 0 && _outputFrames == 0
            ? 0
            : Math.Max(0, _inputFrames + _outputFrames * _rate) / WaveFormat.SampleRate;

    /// <summary>Drops buffered audio (after a seek).</summary>
    public void Reset()
    {
        _inputFrames = 0;
        _outputStart = _outputFrames = 0;
        _haveTail = false;
        _skipFraction = 0;
        _skipDebt = 0;
        _ended = false;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (Math.Abs(_rate - 1) < 1e-6 && _outputFrames == 0 && _inputFrames == 0)
            return _source.Read(buffer, offset, count);

        var wanted = count / _channels;
        while (_outputFrames < wanted && !_ended)
        {
            var read = _source.Read(_readBuffer, 0, _readBuffer.Length) / _channels;
            if (read == 0)
            {
                _ended = true;
                Flush();
                break;
            }

            Append(ref _input, ref _inputFrames, _readBuffer, 0, read);
            Process();
        }

        var frames = Math.Min(wanted, _outputFrames);
        // Element copy: NAudio may hand over a byte[] aliased as float[], which Array.Copy rejects (the audio thread died on it).
        var from = _outputStart * _channels;
        var total = frames * _channels;
        for (var i = 0; i < total; i++)
            buffer[offset + i] = _output[from + i];
        _outputStart += frames;
        _outputFrames -= frames;
        if (_outputFrames == 0)
            _outputStart = 0;
        return frames * _channels;
    }

    private void Process()
    {
        while (true)
        {
            if (_skipDebt > 0)
            {
                var n = Math.Min(_skipDebt, _inputFrames);
                Consume(n);
                _skipDebt -= n;
                if (_skipDebt > 0)
                    return;
            }

            if (_inputFrames < _sequence + _seek)
                return;

            var start = 0;
            if (!_haveTail)
            {
                // First piece: its own start is the "previous tail", so the cross-fade is seamless.
                Array.Copy(_input, 0, _tail, 0, _overlap * _channels);
                _haveTail = true;
            }
            else
            {
                start = BestJoin();
            }

            // Cross-fade the previous piece's tail into this piece's start.
            EnsureOutput(_sequence);
            var o = (_outputStart + _outputFrames) * _channels;
            for (var f = 0; f < _overlap; f++)
            {
                var fadeIn = (float)f / _overlap;
                for (var c = 0; c < _channels; c++)
                {
                    var i = f * _channels + c;
                    _output[o + i] = _tail[i] * (1 - fadeIn) + _input[(start + f) * _channels + c] * fadeIn;
                }
            }

            _outputFrames += _overlap;

            // The middle of the piece unchanged; its end becomes the next tail.
            var middle = _sequence - 2 * _overlap;
            Append(ref _output, ref _outputFrames, _input, start + _overlap, middle, _outputStart);
            Array.Copy(_input, (start + _sequence - _overlap) * _channels, _tail, 0, _overlap * _channels);

            // Move through the input at the play speed.
            _skipFraction += _rate * (_sequence - _overlap);
            var skip = (int)_skipFraction;
            _skipFraction -= skip;
            _skipDebt += skip;
        }
    }

    /// <summary>The offset (0 … seek) where the input best continues the previous tail (normalised correlation).</summary>
    private int BestJoin()
    {
        var best = 0;
        var bestScore = double.MinValue;
        for (var offset = 0; offset < _seek; offset += 2)
        {
            double dot = 0, energy = 1e-9;
            for (var f = 0; f < _overlap; f += 2)
            {
                float a = 0, b = 0;
                for (var c = 0; c < _channels; c++)
                {
                    a += _tail[f * _channels + c];
                    b += _input[(offset + f) * _channels + c];
                }

                dot += a * b;
                energy += b * b;
            }

            var score = dot / Math.Sqrt(energy);
            if (score > bestScore)
            {
                bestScore = score;
                best = offset;
            }
        }

        return best;
    }

    /// <summary>End of the source: play out what is left.</summary>
    private void Flush()
    {
        Append(ref _output, ref _outputFrames, _input, 0, _inputFrames, _outputStart);
        _inputFrames = 0;
    }

    private void Consume(int frames)
    {
        var remaining = _inputFrames - frames;
        Array.Copy(_input, frames * _channels, _input, 0, remaining * _channels);
        _inputFrames = remaining;
    }

    private void EnsureOutput(int extraFrames)
    {
        if (_outputStart > 0)
        {
            Array.Copy(_output, _outputStart * _channels, _output, 0, _outputFrames * _channels);
            _outputStart = 0;
        }

        Grow(ref _output, (_outputFrames + extraFrames) * _channels);
    }

    private void Append(ref float[] target, ref int targetFrames, float[] source, int sourceFrame, int frames, int targetStart = 0)
    {
        if (frames <= 0)
            return;
        if (ReferenceEquals(target, _output))
            EnsureOutput(frames);
        else
            Grow(ref target, (targetStart + targetFrames + frames) * _channels);
        var start = ReferenceEquals(target, _output) ? _outputStart : targetStart;
        Array.Copy(source, sourceFrame * _channels, target, (start + targetFrames) * _channels, frames * _channels);
        targetFrames += frames;
    }

    private static void Grow(ref float[] array, int length)
    {
        if (array.Length >= length)
            return;
        Array.Resize(ref array, Math.Max(length, array.Length * 2));
    }
}
