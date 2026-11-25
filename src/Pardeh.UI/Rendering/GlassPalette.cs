using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Media;

namespace Pardeh.UI.Rendering;

/// <summary>
/// The colour of the Pardeh Glass look. Glass is drawn in royal blue; a <see cref="Tint"/> re-colours its chrome (title bar,
/// tabs, body, glass pills, sliders, highlights, the Library / Settings pages ...) by rotating every <i>blue</i> colour to another
/// hue (and optionally scaling its saturation and brightness). Only colours whose hue lies in the blue band and that are not grey
/// are touched, so white text, gold stars, the red record dot, the flag logo and album art keep their colours; a lightness
/// correction keeps a green or yellow body as deep as the blue one so the white text stays readable.
/// <para>
/// Everything that draws Glass colours goes through <see cref="Map(Color)"/> / <see cref="Map(string)"/> / <see cref="MapText"/>, so
/// there is exactly one place that knows how. The default tint (Royal blue) is the identity: no colour changes, no work is done.
/// </para>
/// </summary>
internal static class GlassPalette
{
    /// <summary>The hue of Glass' own blue (the body gradient); a tint's hue is where this hue ends up.</summary>
    public const double RoyalHue = 220;

    /// <summary>A target colour: the hue (degrees) the Glass blue is rotated to, and saturation and brightness scales (1 = as drawn).</summary>
    public readonly record struct Tint(double Hue, double Saturation, double Brightness)
    {
        public static readonly Tint Royal = new(RoyalHue, 1, 1);

        /// <summary>True when applying this tint changes nothing (Royal blue, no tweaks).</summary>
        public bool IsDefault => Math.Abs(Delta(Hue, RoyalHue)) < 0.5 && Math.Abs(Saturation - 1) < 0.005 && Math.Abs(Brightness - 1) < 0.005;

        /// <summary>The same tint with every value brought into its valid range.</summary>
        public Tint Normalized() => new(((Hue % 360) + 360) % 360, Math.Clamp(Saturation, 0, 1.6), Math.Clamp(Brightness, 0.5, 1.5));

        public bool Matches(Tint other) => Math.Abs(Delta(Hue, other.Hue)) < 0.5 && Math.Abs(Saturation - other.Saturation) < 0.01 && Math.Abs(Brightness - other.Brightness) < 0.01;

        /// <summary>A stable text of the tint (a cache and change key, culture independent).</summary>
        public string Key => string.Create(CultureInfo.InvariantCulture, $"{Hue:0.#}/{Saturation:0.##}/{Brightness:0.##}");
    }

    /// <summary>A named swatch of the picker.</summary>
    public sealed record Preset(string Key, string Name, Tint Tint, string Swatch);

    public static readonly Preset[] Presets =
    [
        new("royal", "Royal blue", Tint.Royal, "#2563D6"),
        new("teal", "Teal", new Tint(182, 1, 1), "#12A3A8"),
        new("emerald", "Emerald", new Tint(150, 1, 1), "#1E9E5A"),
        new("amber", "Amber", new Tint(40, 1, 1.12), "#E0951C"),
        new("orange", "Orange", new Tint(20, 1, 1), "#E5622A"),
        new("crimson", "Crimson", new Tint(352, 1, 1), "#C8283E"),
        new("rose", "Rose", new Tint(328, 0.85, 1), "#D8528F"),
        new("violet", "Violet", new Tint(268, 1, 1), "#7C4DD8"),
        new("graphite", "Graphite", new Tint(218, 0.1, 0.9), "#6B7280"),
    ];

    private static Tint _current = Tint.Royal;

    /// <summary>The tint the Glass look is drawn in right now (set by the window from the settings before it builds the look).</summary>
    public static Tint Current
    {
        get => _current;
        set => _current = value.Normalized();
    }

    /// <summary>True when Glass is not drawn in Royal blue (everything below is a no-op otherwise).</summary>
    public static bool Active => !_current.IsDefault;

    /// <summary>The tint stored in the settings (a development aid, <c>PARDEH_GLASS_TINT=amber</c> or <c>=hue,saturation,brightness</c>, overrides it for screenshots).</summary>
    public static Tint From(AppSettings settings) =>
        DevOverride ?? new Tint(settings.GlassTintHue, settings.GlassTintSaturation, settings.GlassTintBrightness).Normalized();

    private static Tint? DevOverride { get; } = ParseDev(Environment.GetEnvironmentVariable("PARDEH_GLASS_TINT"));

    private static Tint? ParseDev(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (Array.Find(Presets, p => p.Key.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase)) is { } preset)
            return preset.Tint;
        var parts = text.Split(',');
        return parts.Length == 3
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var h)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var s)
            && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var b)
            ? new Tint(h, s, b).Normalized()
            : null;
    }

    /// <summary>Takes the current tint from the settings; returns whether it changed.</summary>
    public static bool Sync(AppSettings settings)
    {
        var next = From(settings);
        var changed = next != _current;
        _current = next;
        return changed;
    }

    /// <summary>The preset a tint equals, or null for a custom colour.</summary>
    public static Preset? PresetOf(Tint tint) => Array.Find(Presets, p => p.Tint.Matches(tint));

    // ---- Mapping ----------------------------------------------------------------------------

    public static Color Map(Color color) => _current.IsDefault ? color : Transform(color, _current);

    /// <summary>A "#RRGGBB" colour through the tint (the same text when the tint is the default).</summary>
    public static string Map(string hex) => _current.IsDefault ? hex : HexOf(Transform(Color.Parse(hex), _current), hex);

    /// <summary>The colour of "#RRGGBB" through the tint.</summary>
    public static Color Parse(string hex) => Map(Color.Parse(hex));

    private static readonly Regex HexColor = new("#([0-9A-Fa-f]{6})(?![0-9A-Fa-f])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Every "#RRGGBB" in a piece of skin XML or script through the tint (unchanged text when the tint is the default).</summary>
    public static string MapText(string text) =>
        _current.IsDefault ? text : HexColor.Replace(text, m => HexOf(Transform(Color.Parse(m.Value), _current), m.Value));

    private static string HexOf(Color c, string _) => string.Create(CultureInfo.InvariantCulture, $"#{c.R:X2}{c.G:X2}{c.B:X2}");

    // ---- The transform ------------------------------------------------------------------------

    private const double BandStart = 185, BandEnd = 255, BandFeather = 22;
    private const double LightnessHold = 0.92; // how much of the original brightness (relative luminance) is kept when the hue moves

    /// <summary>
    /// <paramref name="color"/> with its blue rotated to <paramref name="tint"/> (alpha kept). Weighted by how blue and how
    /// saturated the colour is, so greys, whites, golds and reds come back unchanged.
    /// </summary>
    public static Color Transform(Color color, Tint tint)
    {
        if (tint.IsDefault)
            return color;
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        if (d < 1e-6 || max <= 0)
            return color;
        var s = d / max;
        var h = HueOf(r, g, b, max, d);

        // how much this colour is "chrome blue": inside the blue band, and clearly not grey
        var inBand = Math.Min(Smooth((h - (BandStart - BandFeather)) / BandFeather), Smooth(((BandEnd + BandFeather) - h) / BandFeather));
        var w = inBand * Smooth((s - 0.02) / 0.10);
        if (w <= 0)
            return color;

        // other blues keep their distance from Glass' blue, a little compressed so a far hue stays one colour family
        var spread = 1 - 0.4 * Math.Min(1, Math.Abs(Delta(tint.Hue, RoyalHue)) / 30);
        var target = tint.Hue + (h - RoyalHue) * spread;
        var nh = h + w * Delta(target, h);
        var ns = Math.Clamp(s * (1 + w * (tint.Saturation - 1)), 0, 1);
        var (nr, ng, nb) = FromHsv(nh, ns, max);

        // keep the relative luminance (partly): a blue body is dark, the same value of green or yellow is much brighter
        var before = Luminance(r, g, b);
        var after = Luminance(nr, ng, nb);
        if (after > 1e-6 && before > 1e-6)
        {
            var gain = Math.Pow(Math.Clamp(before / after, 0.25, 1.8), LightnessHold * w);
            if (Math.Abs(gain - 1) > 1e-4)
            {
                nr = Gamma(ToLinear(nr) * gain);
                ng = Gamma(ToLinear(ng) * gain);
                nb = Gamma(ToLinear(nb) * gain);
            }
        }

        // the brightness tweak comes last, so the luminance correction above does not undo it
        var brightness = 1 + w * (tint.Brightness - 1);
        if (Math.Abs(brightness - 1) > 1e-4)
        {
            nr = Math.Clamp(nr * brightness, 0, 1);
            ng = Math.Clamp(ng * brightness, 0, 1);
            nb = Math.Clamp(nb * brightness, 0, 1);
        }

        return Color.FromArgb(color.A, Byte(nr), Byte(ng), Byte(nb));
    }

    /// <summary>The signed shortest rotation from <paramref name="from"/> to <paramref name="to"/>, degrees in [-180, 180).</summary>
    private static double Delta(double to, double from) => ((to - from + 540) % 360 + 360) % 360 - 180;

    private static double Smooth(double t) => t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t);

    private static byte Byte(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);

    private static double Luminance(double r, double g, double b) => 0.2126 * ToLinear(r) + 0.7152 * ToLinear(g) + 0.0722 * ToLinear(b);

    private static double ToLinear(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);

    private static double Gamma(double v)
    {
        v = Math.Clamp(v, 0, 1);
        return v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
    }

    private static double HueOf(double r, double g, double b, double max, double d)
    {
        double h;
        if (max == r)
            h = (g - b) / d % 6;
        else if (max == g)
            h = (b - r) / d + 2;
        else
            h = (r - g) / d + 4;
        h *= 60;
        return h < 0 ? h + 360 : h;
    }

    private static (double R, double G, double B) FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return (r + m, g + m, b + m);
    }
}
