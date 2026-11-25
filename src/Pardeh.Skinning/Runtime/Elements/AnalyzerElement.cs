using Pardeh.Skinning.Model;

namespace Pardeh.Skinning.Runtime.Elements;

/// <summary>
/// &lt;ANALYZER&gt;: the small spectrum bars of the Winamp classic main window, coloured from the skin's viscolor.txt.
/// An opt-in extension element: WMP skins never use it. Attribute <c>colors</c> is the 24 viscolor entries as
/// "#RRGGBB;#RRGGBB;…" (0 background, 1 dots, 2–17 the bar from top to bottom, 23 peaks).
/// </summary>
public sealed class AnalyzerElement(SkinView view, SkinNode node, SkinElement? parent) : SkinElement(view, node, parent), IPlayerSynced
{
    public const int Bars = 19;       // 19 bars of 3 px with a 1 px gap fill the 76 px area
    public const int BarWidth = 3;
    public const int Pitch = 4;
    public const int Rows = 16;

    private readonly float[] _bins = new float[Bars];
    private int[]? _colors;
    private string? _colorsSource;

    /// <summary>Current bar heights in pixels (0 – 16).</summary>
    public int[] Heights { get; } = new int[Bars];

    /// <summary>Peak marker positions in pixels from the bottom (0 – 16; 0 = none).</summary>
    public int[] Peaks { get; } = new int[Bars];

    private readonly float[] _levels = new float[Bars];
    private readonly float[] _peakLevels = new float[Bars];

    public override bool HitTest(double x, double y) => false;

    /// <summary>The 24 viscolor.txt entries as 0xRRGGBB (the classic defaults when the skin gives none).</summary>
    public int[] Colors
    {
        get
        {
            var source = GetString("colors");
            if (_colors is null || source != _colorsSource)
            {
                _colorsSource = source;
                _colors = Parse(source);
            }

            return _colors;
        }
    }

    public static int[] Parse(string? source)
    {
        var colors = (int[])DefaultColors.Clone();
        if (string.IsNullOrWhiteSpace(source))
            return colors;
        var parts = source.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < Math.Min(parts.Length, colors.Length); i++)
        {
            if (SkinValue.ParseColor(parts[i]) is { } rgb)
                colors[i] = rgb;
        }

        return colors;
    }

    /// <summary>Winamp 2's default viscolor.txt.</summary>
    public static readonly int[] DefaultColors =
    [
        0x000000, 0x182129, 0xEF3110, 0xCE2910, 0xD65A00, 0xD66600, 0xD67300, 0xC67B08,
        0xDEA518, 0xD6B521, 0xBDDE29, 0x94DE21, 0x29CE10, 0x32BE10, 0x39B510, 0x319C08,
        0x299400, 0x188408, 0xFFFFFF, 0xD6D6DE, 0xB5BDBD, 0xA0AAAF, 0x949CA5, 0x969696,
    ];

    public void SyncWithPlayer()
    {
        var player = View.Session.Player;
        var playing = player.GetSpectrum(_bins);
        for (var i = 0; i < Bars; i++)
        {
            var target = playing ? Math.Clamp(_bins[i], 0f, 1f) : 0f;
            _levels[i] = target > _levels[i] ? target : Math.Max(0, _levels[i] - 0.06f);
            _peakLevels[i] = _levels[i] > _peakLevels[i] ? _levels[i] : Math.Max(0, _peakLevels[i] - 0.012f);
            Heights[i] = (int)Math.Round(_levels[i] * Rows);
            Peaks[i] = (int)Math.Round(_peakLevels[i] * Rows);
        }
    }
}
