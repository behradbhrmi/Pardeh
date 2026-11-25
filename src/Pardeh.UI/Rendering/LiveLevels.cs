using System.Diagnostics;
using System.Runtime.CompilerServices;
using Pardeh.Core.Playback;

namespace Pardeh.UI.Rendering;

/// <summary>
/// Smooths spectrum levels in time: a quick attack, a slower decay and falling peak markers, independent of how often it is updated.
/// While nothing plays the targets are zero, the levels sink and the smoother settles; once <see cref="Settled"/> nothing changes
/// any more, so the bars freeze (and callers can skip redrawing).
/// </summary>
internal sealed class LevelSmoother(int count, double attackSeconds = 0.045, double decaySeconds = 0.24, double peakHoldSeconds = 0.3, double peakFallPerSecond = 0.7)
{
    private readonly double[] _hold = new double[count];

    public int Count => count;

    public float[] Levels { get; } = new float[count];

    public float[] Peaks { get; } = new float[count];

    /// <summary>True when every level and peak is at rest (zero): a frozen display.</summary>
    public bool Settled { get; private set; } = true;

    /// <summary>
    /// Moves the levels towards <paramref name="target"/> (0 - 1 per band) over <paramref name="dt"/> seconds. With <paramref name="active"/> false
    /// (paused, stopped) the targets are zero.
    /// </summary>
    public void Update(ReadOnlySpan<float> target, bool active, double dt)
    {
        dt = Math.Clamp(dt, 0, 0.25);
        var attack = 1 - Math.Exp(-dt / attackSeconds);
        var decay = 1 - Math.Exp(-dt / decaySeconds);
        var settled = true;
        for (var i = 0; i < Levels.Length; i++)
        {
            var t = active && i < target.Length ? Math.Clamp(target[i], 0f, 1f) : 0f;
            var level = Levels[i];
            level += (float)((t - level) * (t > level ? attack : decay));
            if (!active && level < 0.004f)
                level = 0;
            Levels[i] = level;

            if (level >= Peaks[i])
            {
                Peaks[i] = level;
                _hold[i] = peakHoldSeconds;
            }
            else if (_hold[i] > 0)
            {
                _hold[i] -= dt;
            }
            else
            {
                Peaks[i] = Math.Max(level, (float)(Peaks[i] - peakFallPerSecond * dt));
            }

            if (!active && Peaks[i] < 0.004f)
                Peaks[i] = 0;
            settled &= Levels[i] <= 0 && Peaks[i] <= 0;
        }

        Settled = settled;
    }

    public void Reset()
    {
        Array.Clear(Levels);
        Array.Clear(Peaks);
        Array.Clear(_hold);
        Settled = true;
    }
}

/// <summary>
/// The live audio levels every visual of the phone looks shares (the playing row's bars, the bottom bar's glyph, the Glass visualizer): one
/// spectrum read per player at most ~30 times a second (an FFT each), smoothed with <see cref="LevelSmoother"/>. Used on the UI thread from
/// the render pass only, so nothing runs while nothing is drawn (screen off, app in the background, element hidden).
/// </summary>
internal sealed class SpectrumFeed
{
    public const int Bands = 48;
    public const int WaveLength = 512;

    /// <summary>The fastest update rate (about 30 per second).</summary>
    public const double MinInterval = 1.0 / 30 - 0.002;

    private static readonly ConditionalWeakTable<MediaPlayer, SpectrumFeed> Feeds = new();
    private static readonly long Origin = Stopwatch.GetTimestamp();

    private readonly ISpectrumTap? _tap;
    private readonly float[] _raw = new float[Bands];
    private double _last = double.NegativeInfinity;

    private SpectrumFeed(ISpectrumTap? tap) => _tap = tap;

    public static SpectrumFeed For(MediaPlayer player) => Feeds.GetValue(player, p => new SpectrumFeed(new PlayerTap(p)));

    /// <summary>A feed over any audio source (the picker's synthetic thumbnails, tests).</summary>
    public static SpectrumFeed Over(ISpectrumTap tap) => new(tap);

    /// <summary>A feed fed by hand through <see cref="Apply"/> (tests).</summary>
    public static SpectrumFeed Manual() => new(null);

    /// <summary>Seconds on a clock shared by every consumer.</summary>
    public static double Now => (Stopwatch.GetTimestamp() - Origin) / (double)Stopwatch.Frequency;

    public LevelSmoother Smoother { get; } = new(Bands);

    public float[] Wave { get; } = new float[WaveLength];

    /// <summary>The engine reported audio playing on the last update.</summary>
    public bool Playing { get; private set; }

    /// <summary>Not playing and every level has settled: a display can stop redrawing.</summary>
    public bool Frozen => !Playing && Smoother.Settled;

    public float[] Levels => Smoother.Levels;

    public float[] Peaks => Smoother.Peaks;

    /// <summary>Updates counted since the feed was made (for diagnostics and tests).</summary>
    public long Updates { get; private set; }

    /// <summary>
    /// Reads and smooths the audio again if at least <see cref="MinInterval"/> has passed since the last time; cheap otherwise. Returns true
    /// when it updated. <paramref name="wantWave"/> also reads the waveform (only the oscilloscope-like styles need it).
    /// </summary>
    public bool Update(double now, bool wantWave = false)
    {
        if (_tap is null || now - _last < MinInterval)
            return false;
        var dt = double.IsNegativeInfinity(_last) ? 1.0 / 30 : now - _last;
        _last = now;
        var playing = _tap.GetSpectrum(_raw);
        if (playing && wantWave)
            _tap.GetWaveform(Wave);
        else if (!playing)
            Array.Clear(Wave);
        Apply(_raw, playing, dt);
        return true;
    }

    /// <summary>Applies one reading: the raw bands (0 - 1), whether audio plays, and the time since the last reading.</summary>
    public void Apply(ReadOnlySpan<float> bands, bool playing, double dt)
    {
        Playing = playing;
        Smoother.Update(bands, playing, dt);
        Updates++;
    }

    /// <summary>
    /// Folds the bands into <paramref name="bars"/> groups (the loudest band of each, with a contrast curve that makes quiet passages
    /// still move). Group 0 holds the bass.
    /// </summary>
    public void Group(Span<float> bars, bool peaks = false)
    {
        var source = peaks ? Peaks : Levels;
        var n = bars.Length;
        // The bands are log-spaced from 40 Hz to 16 kHz; the lowest and the topmost few carry little for music.
        const int first = 1, last = 44;
        for (var k = 0; k < n; k++)
        {
            int b0 = first + k * (last - first) / n, b1 = Math.Max(b0 + 1, first + (k + 1) * (last - first) / n);
            var peak = 0f;
            for (var b = b0; b < b1 && b < Bands; b++)
                peak = Math.Max(peak, source[b]);
            bars[k] = Contrast(peak);
        }
    }

    /// <summary>Spreads the useful part of a level (0.15 - 0.85 of the analyser's range) over 0 - 1.</summary>
    public static float Contrast(float level) => Math.Clamp((level - 0.15f) / 0.7f, 0f, 1f);

    /// <summary>The bass (lowest eight bands), mid and treble averages, 0 - 1.</summary>
    public (float Bass, float Mid, float Treble, float Energy) Bands3()
    {
        float bass = 0, mid = 0, treble = 0;
        for (var i = 0; i < 8; i++)
            bass += Levels[i];
        for (var i = 8; i < 28; i++)
            mid += Levels[i];
        for (var i = 28; i < Bands; i++)
            treble += Levels[i];
        bass = Contrast(bass / 8);
        mid = Contrast(mid / 20);
        treble = Contrast(treble / 20);
        return (bass, mid, treble, (bass + mid + treble) / 3);
    }
}

/// <summary>Where visualizations read their audio from: the player, or a synthetic source (the picker's thumbnails, tests).</summary>
internal interface ISpectrumTap
{
    /// <summary>Fills the bands (0 - 1); false when nothing plays.</summary>
    bool GetSpectrum(Span<float> bands);

    /// <summary>Fills the latest mono samples (-1 - 1); false when nothing plays.</summary>
    bool GetWaveform(Span<float> samples);
}

/// <summary>The real audio: the player's engine.</summary>
internal sealed class PlayerTap(MediaPlayer player) : ISpectrumTap
{
    public bool GetSpectrum(Span<float> bands) => player.GetSpectrum(bands);

    public bool GetWaveform(Span<float> samples) => player.GetWaveform(samples);
}

/// <summary>
/// Made-up music: a tilted spectrum with a moving ripple and a bass beat about twice a second, and a two-tone waveform. It gives the
/// picker's thumbnails and the tests something lively to draw without a playing track.
/// </summary>
internal sealed class DemoTap : ISpectrumTap
{
    private readonly Func<double> _clock;

    public DemoTap(Func<double>? clock = null) => _clock = clock ?? (() => SpectrumFeed.Now);

    /// <summary>False pauses the demo audio (the feed then settles, like a paused track).</summary>
    public bool Playing { get; set; } = true;

    public bool GetSpectrum(Span<float> bands)
    {
        if (!Playing)
        {
            bands.Clear();
            return false;
        }

        var t = _clock();
        var beat = Math.Pow(Math.Max(0, Math.Sin(t * 2 * Math.PI * 1.9)), 6);
        for (var i = 0; i < bands.Length; i++)
        {
            var u = i / (double)Math.Max(1, bands.Length - 1);
            var v = 0.62 - 0.32 * u + 0.2 * Math.Sin(t * 3.1 + i * 0.45) * Math.Sin(t * 1.3 + i * 0.11) + beat * 0.35 * (1 - u) * (1 - u);
            bands[i] = (float)Math.Clamp(v, 0, 1);
        }

        return true;
    }

    public bool GetWaveform(Span<float> samples)
    {
        if (!Playing)
        {
            samples.Clear();
            return false;
        }

        var t = _clock();
        for (var i = 0; i < samples.Length; i++)
        {
            var u = i / (double)Math.Max(1, samples.Length - 1);
            samples[i] = (float)(0.45 * Math.Sin(2 * Math.PI * (u * 3 + t * 1.6)) + 0.25 * Math.Sin(2 * Math.PI * (u * 8 - t * 2.4)));
        }

        return true;
    }
}
