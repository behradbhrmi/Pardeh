using NAudio.Dsp;
using Pardeh.Core.Playback;

namespace Pardeh.Nava;

/// <summary>
/// The classic player's other audio enhancements, applied after the equalizer:
/// bass and stereo width (its SRS WOW pane), Quiet Mode (a compressor), Auto Volume Leveling (a slow loudness
/// leveller) and the crossfade gain. Pardeh's own DSP; settings come from <see cref="EqualizerSettings"/>.
/// </summary>
internal sealed class AudioEffects
{
    private static readonly double[] BassFrequency = [150, 100, 60]; // small, medium, large speakers

    private readonly EqualizerSettings _settings;
    private readonly int _channels;
    private readonly float _sampleRate;
    private BiQuadFilter[] _bass = [];
    private BiQuadFilter[] _presence = [];
    private volatile bool _dirty = true;

    // Quiet Mode envelope and Auto Volume Leveling state.
    private double _envelope;
    private double _meanSquare = 0.01;
    private double _levelGain = 1;

    // Crossfade: the gain moves towards the target at the given rate (per sample).
    private double _fade = 1, _fadeTarget = 1, _fadeStep;

    public AudioEffects(EqualizerSettings settings, int channels, int sampleRate)
    {
        _settings = settings;
        _channels = channels;
        _sampleRate = sampleRate;
        settings.Changed += (_, _) => _dirty = true;
    }

    /// <summary>Fades the output to <paramref name="target"/> (0 – 1) over <paramref name="seconds"/>.</summary>
    public void FadeTo(double target, double seconds)
    {
        _fadeTarget = Math.Clamp(target, 0, 1);
        var samples = Math.Max(1, seconds * _sampleRate);
        _fadeStep = Math.Abs(_fadeTarget - _fade) / samples;
        if (seconds <= 0)
            _fade = _fadeTarget;
    }

    /// <summary>Current crossfade gain.</summary>
    public double Fade => _fade;

    public void Process(float[] buffer, int offset, int count)
    {
        if (_dirty)
            Rebuild();

        var s = _settings;
        var enhanced = s.Enhanced;
        var width = 1 + s.WowLevel / 100 * 1.2;
        var quiet = s.QuietMode;
        var (threshold, ratio, makeup) = quiet == 2 ? (-30.0, 6.0, 10.0) : (-24.0, 3.0, 6.0);
        var attack = Math.Exp(-1 / (0.010 * _sampleRate));
        var release = Math.Exp(-1 / (0.250 * _sampleRate));
        var autoVolume = s.AutoVolume;
        var levelSmoothing = 1 / (3.0 * _sampleRate);
        var gainSmoothing = 1 / (0.5 * _sampleRate);
        const double targetMeanSquare = 0.0158; // -18 dBFS RMS

        for (var i = offset; i + _channels - 1 < offset + count; i += _channels)
        {
            // Bass and width (stereo only for the width).
            if (enhanced)
            {
                for (var c = 0; c < _channels; c++)
                    buffer[i + c] = _presence[c].Transform(_bass[c].Transform(buffer[i + c]));
                if (_channels == 2)
                {
                    float l = buffer[i], r = buffer[i + 1];
                    var mid = (l + r) * 0.5f;
                    var side = (l - r) * 0.5f * (float)width;
                    buffer[i] = mid + side;
                    buffer[i + 1] = mid - side;
                }
            }

            double gain = 1;

            // Quiet Mode: soften loud passages and lift the overall level back up.
            if (quiet > 0)
            {
                double peak = 0;
                for (var c = 0; c < _channels; c++)
                    peak = Math.Max(peak, Math.Abs(buffer[i + c]));
                var coefficient = peak > _envelope ? attack : release;
                _envelope = coefficient * _envelope + (1 - coefficient) * peak;
                var db = 20 * Math.Log10(_envelope + 1e-9);
                var reduction = db > threshold ? (db - threshold) * (1 - 1 / ratio) : 0;
                gain *= Math.Pow(10, (makeup - reduction) / 20);
            }

            // Auto Volume Leveling: follow the loudness of the last few seconds towards -18 dBFS.
            if (autoVolume)
            {
                double square = 0;
                for (var c = 0; c < _channels; c++)
                    square += buffer[i + c] * buffer[i + c];
                _meanSquare += (square / _channels - _meanSquare) * levelSmoothing;
                if (_meanSquare > 1e-5) // not during silence
                {
                    var wanted = Math.Clamp(Math.Sqrt(targetMeanSquare / _meanSquare), 0.25, 4);
                    _levelGain += (wanted - _levelGain) * gainSmoothing;
                }

                gain *= _levelGain;
            }

            // Crossfade.
            if (_fade != _fadeTarget)
                _fade = _fade < _fadeTarget ? Math.Min(_fadeTarget, _fade + _fadeStep) : Math.Max(_fadeTarget, _fade - _fadeStep);
            gain *= _fade;

            if (gain != 1)
            {
                for (var c = 0; c < _channels; c++)
                    buffer[i + c] = (float)(buffer[i + c] * gain);
            }
        }
    }

    private void Rebuild()
    {
        _dirty = false;
        var s = _settings;
        var frequency = BassFrequency[s.SpeakerSize];
        var bassDb = (float)(s.TruBassLevel / 100 * 10);
        var presenceDb = (float)(s.WowLevel / 100 * 3);
        _bass = Enumerable.Range(0, _channels).Select(_ => BiQuadFilter.LowShelf(_sampleRate, (float)frequency, 1, bassDb)).ToArray();
        _presence = Enumerable.Range(0, _channels).Select(_ => BiQuadFilter.HighShelf(_sampleRate, 8000, 1, presenceDb)).ToArray();
    }
}
