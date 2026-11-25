using NAudio.Dsp;
using NAudio.Wave;
using Pardeh.Core.Playback;

namespace Pardeh.Nava;

/// <summary>Stereo float DSP chain: equalizer → enhancements → spectrum tap → balance/volume/mute.</summary>
internal sealed class NavaPipeline : ISampleProvider
{
    private const float BandQ = 1.2f;

    private readonly ISampleProvider _source;
    private readonly EqualizerSettings _equalizer;
    private readonly Lock _sync;
    private readonly BiQuadFilter[,] _filters;
    private volatile bool _filtersDirty = true;
    private bool _bypass = true;

    public NavaPipeline(ISampleProvider source, EqualizerSettings equalizer, Lock sync)
    {
        _source = source;
        _equalizer = equalizer;
        _sync = sync;
        _filters = new BiQuadFilter[source.WaveFormat.Channels, EqualizerSettings.BandCount];
        _equalizer.Changed += (_, _) => _filtersDirty = true;
        Spectrum = new SpectrumAnalyzer(source.WaveFormat.SampleRate);
        Effects = new AudioEffects(equalizer, source.WaveFormat.Channels, source.WaveFormat.SampleRate);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public SpectrumAnalyzer Spectrum { get; }

    /// <summary>Bass / width, Quiet Mode, Auto Volume Leveling and the crossfade gain.</summary>
    public AudioEffects Effects { get; }

    public float Volume { get; set; } = 0.5f;

    /// <summary>ReplayGain factor of the open track (1 = unchanged).</summary>
    public float Level { get; set; } = 1f;

    public float Balance { get; set; }

    public bool Muted { get; set; }

    public int Read(float[] buffer, int offset, int count)
    {
        int read;
        lock (_sync)
            read = _source.Read(buffer, offset, count);

        if (read == 0)
            return 0;

        if (_filtersDirty)
            RebuildFilters();

        var channels = WaveFormat.Channels;
        if (!_bypass)
        {
            for (var i = 0; i < read; i++)
            {
                var channel = i % channels;
                var sample = buffer[offset + i];
                for (var band = 0; band < EqualizerSettings.BandCount; band++)
                    sample = _filters[channel, band].Transform(sample);
                buffer[offset + i] = sample;
            }
        }

        Effects.Process(buffer, offset, read);
        Spectrum.Write(buffer.AsSpan(offset, read), channels);

        // Equal-power-ish balance: attenuate the opposite side only.
        var gain = Muted ? 0f : Volume * Volume * Level; // perceptual curve, then the track loudness
        var left = gain * (Balance > 0 ? 1f - Balance : 1f);
        var right = gain * (Balance < 0 ? 1f + Balance : 1f);
        for (var i = 0; i < read; i += channels)
        {
            buffer[offset + i] = Math.Clamp(buffer[offset + i] * left, -1f, 1f);
            if (channels > 1)
                buffer[offset + i + 1] = Math.Clamp(buffer[offset + i + 1] * right, -1f, 1f);
        }

        return read;
    }

    private void RebuildFilters()
    {
        _filtersDirty = false;
        _bypass = _equalizer.Bypass;
        var sampleRate = WaveFormat.SampleRate;
        for (var channel = 0; channel < _filters.GetLength(0); channel++)
        {
            for (var band = 0; band < EqualizerSettings.BandCount; band++)
            {
                var frequency = (float)Math.Min(EqualizerSettings.BandFrequencies[band], sampleRate * 0.45);
                var gain = (float)_equalizer.GetGain(band);
                if (_filters[channel, band] is null)
                    _filters[channel, band] = BiQuadFilter.PeakingEQ(sampleRate, frequency, BandQ, gain);
                else
                    _filters[channel, band].SetPeakingEq(sampleRate, frequency, BandQ, gain);
            }
        }
    }
}
