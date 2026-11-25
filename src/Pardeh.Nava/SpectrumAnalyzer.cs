using NAudio.Dsp;

namespace Pardeh.Nava;

/// <summary>Keeps the most recent mono samples and turns them into log-spaced spectrum bands on demand.</summary>
public sealed class SpectrumAnalyzer
{
    private const int FftExponent = 11;               // 2048-point FFT
    private const int FftSize = 1 << FftExponent;

    private readonly float[] _ring = new float[FftSize];
    private readonly Complex[] _fft = new Complex[FftSize];
    private readonly Lock _lock = new();
    private readonly int _sampleRate;
    private int _writePosition;

    public SpectrumAnalyzer(int sampleRate) => _sampleRate = sampleRate;

    /// <summary>Called from the audio thread with interleaved samples.</summary>
    public void Write(ReadOnlySpan<float> interleaved, int channels)
    {
        lock (_lock)
        {
            for (var i = 0; i + channels - 1 < interleaved.Length; i += channels)
            {
                var mono = 0f;
                for (var c = 0; c < channels; c++)
                    mono += interleaved[i + c];
                _ring[_writePosition] = mono / channels;
                _writePosition = (_writePosition + 1) % FftSize;
            }
        }
    }

    /// <summary>The most recent mono samples (oldest first), for oscilloscope-style visualizations.</summary>
    public void CopyWaveform(Span<float> samples)
    {
        var count = Math.Min(samples.Length, FftSize);
        lock (_lock)
        {
            for (var i = 0; i < count; i++)
                samples[i] = _ring[(_writePosition - count + i + FftSize) % FftSize];
        }
    }

    /// <summary>Fills <paramref name="bands"/> with values in 0 – 1, log-spaced from 40 Hz to 16 kHz.</summary>
    public void Compute(Span<float> bands)
    {
        lock (_lock)
        {
            for (var i = 0; i < FftSize; i++)
            {
                var sample = _ring[(_writePosition + i) % FftSize];
                _fft[i].X = (float)(sample * FastFourierTransform.HannWindow(i, FftSize));
                _fft[i].Y = 0;
            }
        }

        FastFourierTransform.FFT(true, FftExponent, _fft);

        var count = bands.Length;
        if (count == 0)
            return;

        const double minFreq = 40, maxFreq = 16000;
        var binWidth = (double)_sampleRate / FftSize;
        for (var b = 0; b < count; b++)
        {
            var f0 = minFreq * Math.Pow(maxFreq / minFreq, (double)b / count);
            var f1 = minFreq * Math.Pow(maxFreq / minFreq, (double)(b + 1) / count);
            var i0 = Math.Clamp((int)(f0 / binWidth), 1, FftSize / 2 - 1);
            var i1 = Math.Clamp((int)Math.Ceiling(f1 / binWidth), i0 + 1, FftSize / 2);

            double peak = 0;
            for (var i = i0; i < i1; i++)
            {
                var magnitude = Math.Sqrt(_fft[i].X * _fft[i].X + _fft[i].Y * _fft[i].Y);
                peak = Math.Max(peak, magnitude);
            }

            // Map -70 dB … 0 dB to 0 … 1.
            var db = 20 * Math.Log10(peak + 1e-9);
            bands[b] = (float)Math.Clamp((db + 70) / 70, 0, 1);
        }
    }
}
