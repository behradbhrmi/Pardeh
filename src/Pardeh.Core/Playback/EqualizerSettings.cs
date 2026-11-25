namespace Pardeh.Core.Playback;

/// <summary>10-band graphic equalizer settings (31 Hz … 16 kHz), gains in dB from -14 to +14.</summary>
public sealed class EqualizerSettings
{
    public const int BandCount = 10;
    public const double MinGain = -14;
    public const double MaxGain = 14;

    public static readonly double[] BandFrequencies = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    /// <summary>Built-in presets (original Pardeh curves).</summary>
    public static readonly IReadOnlyList<(string Name, double[] Gains)> Presets =
    [
        ("Default", [0, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
        ("Rock", [5, 4, 2, -1, -2, -1, 2, 4, 5, 5]),
        ("Rap", [6, 5, 2, 0, -1, -1, 1, 0, 2, 3]),
        ("Grunge", [3, 2, 0, -1, 2, 3, 2, 1, 2, 2]),
        ("Metal", [4, 3, 3, 0, -2, 0, 3, 4, 5, 4]),
        ("Dance", [7, 6, 3, 0, 0, -2, -1, 0, 4, 5]),
        ("Country", [2, 1, 0, 1, 2, 3, 2, 2, 2, 1]),
        ("Jazz", [3, 2, 1, 2, -1, -1, 0, 1, 2, 3]),
        ("Speech", [-4, -3, -1, 2, 4, 4, 3, 1, -1, -3]),
        ("Classical", [4, 3, 2, 1, -1, -1, 0, 2, 3, 4]),
        ("Blues", [2, 2, 1, 0, -1, 0, 1, 2, 2, 1]),
        ("Oldies", [-2, 0, 2, 3, 3, 2, 1, 0, -1, -2]),
        ("Reggae", [0, 0, -1, -3, 0, 3, 4, 0, 0, 0]),
        ("Opera", [-2, -1, 0, 2, 4, 4, 3, 1, 0, -1]),
        ("Swing", [-1, 0, 1, 3, 3, 2, 1, 1, 2, 2]),
        ("Acoustic", [3, 3, 2, 0, 1, 1, 2, 3, 2, 1]),
        ("New Age", [3, 2, 0, -1, 0, 1, 2, 2, 3, 4]),
    ];

    private readonly double[] _gains = new double[BandCount];
    private bool _bypass = true;
    private int _currentPreset;

    public event EventHandler? Changed;

    /// <summary>True when the equalizer is off.</summary>
    public bool Bypass
    {
        get => _bypass;
        set
        {
            if (_bypass == value)
                return;
            _bypass = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public int CurrentPreset
    {
        get => _currentPreset;
        set
        {
            if (value < 0 || value >= Presets.Count)
                return;
            _currentPreset = value;
            Presets[value].Gains.CopyTo(_gains, 0);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Gain of a band (0-based) in dB.</summary>
    public double GetGain(int band) => _gains[band];

    public void SetGain(int band, double gain)
    {
        gain = Math.Clamp(gain, MinGain, MaxGain);
        if (Math.Abs(_gains[band] - gain) < 0.001)
            return;
        _gains[band] = gain;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reset() => CurrentPreset = 0;

    // ---- The other audio enhancements (WMP 9's panes) ---------------------------------------------

    private bool _crossFade, _autoVolume, _enhanced;
    private double _crossFadeSeconds = 5, _wow = 50, _truBass = 50;
    private int _speakerSize = 1, _quietMode;

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Crossfading: the end of a track fades into the start of the next.</summary>
    public bool CrossFade { get => _crossFade; set => Set(ref _crossFade, value); }

    /// <summary>Crossfade overlap, 0 – 10 seconds.</summary>
    public double CrossFadeSeconds { get => _crossFadeSeconds; set => Set(ref _crossFadeSeconds, Math.Clamp(value, 0, 10)); }

    /// <summary>Auto Volume Leveling: quiet and loud tracks play at a similar loudness.</summary>
    public bool AutoVolume { get => _autoVolume; set => Set(ref _autoVolume, value); }

    /// <summary>Quiet Mode (dynamic range control): 0 off, 1 medium difference, 2 little difference between soft and loud.</summary>
    public int QuietMode { get => _quietMode; set => Set(ref _quietMode, Math.Clamp(value, 0, 2)); }

    /// <summary>The bass and width enhancement (the classic player's SRS WOW pane).</summary>
    public bool Enhanced { get => _enhanced; set => Set(ref _enhanced, value); }

    /// <summary>Stereo widening, 0 – 100.</summary>
    public double WowLevel { get => _wow; set => Set(ref _wow, Math.Clamp(value, 0, 100)); }

    /// <summary>Bass enhancement, 0 – 100.</summary>
    public double TruBassLevel { get => _truBass; set => Set(ref _truBass, Math.Clamp(value, 0, 100)); }

    /// <summary>0 small (computer speakers), 1 medium, 2 large speakers or headphones; sets the bass frequency.</summary>
    public int SpeakerSize { get => _speakerSize; set => Set(ref _speakerSize, Math.Clamp(value, 0, 2)); }

    public static readonly string[] SpeakerNames = ["Small Speakers", "Medium Speakers", "Large Speakers"];
}

/// <summary>Picture adjustments (Video Settings): brightness, contrast, hue and saturation, each -127 – 127.</summary>
public sealed class VideoSettings
{
    private double _brightness, _contrast, _hue, _saturation;

    public event EventHandler? Changed;

    public double Brightness { get => _brightness; set => Set(ref _brightness, value); }

    public double Contrast { get => _contrast; set => Set(ref _contrast, value); }

    public double Hue { get => _hue; set => Set(ref _hue, value); }

    public double Saturation { get => _saturation; set => Set(ref _saturation, value); }

    public bool IsDefault => _brightness == 0 && _contrast == 0 && _hue == 0 && _saturation == 0;

    /// <summary>The shapes the picture can be shown in (View > Aspect Ratio): the video's own, a fixed ratio, stretched to the pane, or cropped to fill it.</summary>
    public static readonly (string Key, string Name, double Ratio)[] Aspects =
    [
        ("auto", "Original", 0),
        ("16:9", "16:9", 16.0 / 9),
        ("4:3", "4:3", 4.0 / 3),
        ("21:9", "21:9 (cinema)", 21.0 / 9),
        ("16:10", "16:10", 16.0 / 10),
        ("1:1", "1:1 (square)", 1),
        ("fill", "Stretch to fill", -1),
        ("crop", "Crop to fill", -2),
    ];

    private string _aspect = "auto";

    /// <summary>One of the keys of <see cref="Aspects"/>; "auto" shows the picture in its own shape.</summary>
    public string Aspect
    {
        get => _aspect;
        set
        {
            var key = Aspects.Any(a => a.Key == value) ? value : "auto";
            if (key == _aspect)
                return;
            _aspect = key;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Next / previous entry of <see cref="Aspects"/> (a key press cycles through them).</summary>
    public void StepAspect(int direction)
    {
        var index = Array.FindIndex(Aspects, a => a.Key == _aspect);
        Aspect = Aspects[((index + direction) % Aspects.Length + Aspects.Length) % Aspects.Length].Key;
    }

    public void Reset()
    {
        _brightness = _contrast = _hue = _saturation = 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Set(ref double field, double value)
    {
        value = Math.Clamp(Math.Round(value), -127, 127);
        if (field == value)
            return;
        field = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
