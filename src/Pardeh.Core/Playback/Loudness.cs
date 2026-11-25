namespace Pardeh.Core.Playback;

/// <summary>The measured loudness of a track: integrated loudness in LUFS (negative infinity for silence) and the largest sample (1 = full scale).</summary>
public sealed record LoudnessResult(double Lufs, double Peak)
{
    /// <summary>The ReplayGain 2.0 reference level.</summary>
    public const double ReferenceLufs = -18;

    /// <summary>The volume factor that brings the track to the reference level (limited by its peak, like a ReplayGain tag); 1 for silence.</summary>
    public double Factor => double.IsFinite(Lufs) ? ReplayGain.Factor(ReferenceLufs - Lufs, Peak) : 1;
}

/// <summary>
/// Integrated loudness after ITU-R BS.1770 / EBU R128: the two K-weighting filters, mean power over 400 ms blocks that overlap by
/// three quarters, an absolute gate at -70 LUFS and a relative one 10 LU under the level of what passed the first. Every channel
/// counts equally (the player works in stereo). The peak is the largest sample, not an oversampled "true peak".
/// </summary>
public sealed class LoudnessMeter
{
    private sealed class Biquad(double b0, double b1, double b2, double a1, double a2)
    {
        private double _z1, _z2;

        public double Next(double x)
        {
            var y = b0 * x + _z1;
            _z1 = b1 * x - a1 * y + _z2;
            _z2 = b2 * x - a2 * y;
            return y;
        }
    }

    private readonly int _channels;
    private readonly Biquad[] _shelf, _highPass;
    private readonly int _step;                 // frames in 100 ms
    private readonly List<double> _steps = [];  // summed channel power of each whole 100 ms
    private double _sum;
    private int _frames;
    private double _peak;

    public LoudnessMeter(int sampleRate, int channels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        _channels = channels;
        _step = Math.Max(1, sampleRate / 10);
        _shelf = [.. Enumerable.Range(0, channels).Select(_ => Shelf(sampleRate))];
        _highPass = [.. Enumerable.Range(0, channels).Select(_ => HighPass(sampleRate))];
    }

    // The two filters of BS.1770, derived for any sample rate (the standard lists them for 48 kHz).
    private static Biquad Shelf(int rate)
    {
        const double frequency = 1681.974450955533, gain = 3.999843853973347, q = 0.7071752369554196;
        var k = Math.Tan(Math.PI * frequency / rate);
        var high = Math.Pow(10, gain / 20);
        var band = Math.Pow(high, 0.4996667741545416);
        var a0 = 1 + k / q + k * k;
        return new Biquad((high + band * k / q + k * k) / a0, 2 * (k * k - high) / a0, (high - band * k / q + k * k) / a0, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0);
    }

    private static Biquad HighPass(int rate)
    {
        const double frequency = 38.13547087602444, q = 0.5003270373238773;
        var k = Math.Tan(Math.PI * frequency / rate);
        var a0 = 1 + k / q + k * k;
        return new Biquad(1, -2, 1, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0);
    }

    /// <summary>Feeds interleaved samples (whole frames).</summary>
    public void Add(ReadOnlySpan<float> samples)
    {
        for (var i = 0; i + _channels <= samples.Length; i += _channels)
        {
            for (var c = 0; c < _channels; c++)
            {
                var x = samples[i + c];
                if (!float.IsFinite(x))
                    x = 0;
                _peak = Math.Max(_peak, Math.Abs(x));
                var weighted = _highPass[c].Next(_shelf[c].Next(x));
                _sum += weighted * weighted;
            }

            if (++_frames < _step)
                continue;
            _steps.Add(_sum / _step);
            _sum = 0;
            _frames = 0;
        }
    }

    private static double Level(double power) => -0.691 + 10 * Math.Log10(power);

    private static double Power(double lufs) => Math.Pow(10, (lufs + 0.691) / 10);

    public LoudnessResult Result()
    {
        // 400 ms blocks every 100 ms; something shorter than one block is measured as a whole.
        var blocks = new List<double>();
        for (var i = 0; i + 4 <= _steps.Count; i++)
            blocks.Add((_steps[i] + _steps[i + 1] + _steps[i + 2] + _steps[i + 3]) / 4);
        if (blocks.Count == 0)
        {
            var total = _steps.Sum() * _step + _sum;
            var frames = _steps.Count * _step + _frames;
            if (frames > 0)
                blocks.Add(total / frames);
        }

        var loud = blocks.Where(p => p > Power(-70)).ToList();
        if (loud.Count == 0)
            return new LoudnessResult(double.NegativeInfinity, _peak);
        var gate = Power(Level(loud.Average()) - 10);
        var kept = loud.Where(p => p > gate).ToList();
        return new LoudnessResult(Level((kept.Count > 0 ? kept : loud).Average()), _peak);
    }
}
