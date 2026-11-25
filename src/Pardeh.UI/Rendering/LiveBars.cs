using Avalonia;
using Avalonia.Media;
using Pardeh.Skinning.Runtime;
using Pardeh.Skinning.Runtime.Elements;

namespace Pardeh.UI.Rendering;

/// <summary>How a <see cref="LiveBars"/> glyph looks: solid bars, or stacks of small segments with a bright top one.</summary>
internal readonly record struct LiveBarsLook(Color Low, Color High, Color Tip, bool Segmented = false, int Segments = 5, double SegmentHeight = 3, double SegmentGap = 1, double Gap = 1.5, double Rest = 0.18)
{
    /// <summary>The 3-bar "now playing" indicator of the Glass playlist: solid cyan bars 4.5 wide on a 6 pitch, up to 14 high.</summary>
    public static LiveBarsLook PlayingRow => new(GlassPalette.Parse("#8FE3FF"), GlassPalette.Parse("#8FE3FF"), GlassPalette.Parse("#8FE3FF"), Gap: 1.5, Rest: 0.3);

    /// <summary>The look an EQBARS element asks for; <c>palette="glass"</c> runs its colours through the skin colour.</summary>
    public static LiveBarsLook Of(EqBarsElement element)
    {
        var parts = (element.GetString("colors") ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var glass = string.Equals(element.GetString("palette"), "glass", StringComparison.OrdinalIgnoreCase);
        Color Pick(int i, Color fallback)
        {
            if (i >= parts.Length && parts.Length > 0)
                i = parts.Length - 1;
            if (i < parts.Length && SkinValue.ParseColor(parts[i]) is { } rgb)
            {
                var color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
                return glass ? GlassPalette.Map(color) : color;
            }

            return fallback;
        }

        var low = Pick(0, Colors.White);
        return new LiveBarsLook(low, Pick(1, low), Pick(2, Pick(1, low)), element.Segmented, element.Segments,
            element.GetDouble("segmentHeight", 3), element.GetDouble("segmentGap", 1), element.GetDouble("gap", 1.5), element.Rest);
    }
}

/// <summary>Vector drawing of the small live equaliser glyphs (the playing row's bars, the bottom bar's visualization button).</summary>
internal static class LiveBars
{
    private static readonly Dictionary<uint, SolidColorBrush> Brushes = [];

    internal static IBrush Fill(Color c)
    {
        var key = (uint)(c.A << 24 | c.R << 16 | c.G << 8 | c.B);
        lock (Brushes)
        {
            if (Brushes.Count > 200)
                Brushes.Clear();
            if (!Brushes.TryGetValue(key, out var brush))
                Brushes[key] = brush = new SolidColorBrush(c);
            return brush;
        }
    }

    private static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>
    /// Draws <c>levels.Length</c> bars growing up from the bottom of <paramref name="area"/>. Each level (0 - 1) is lifted to at least
    /// <see cref="LiveBarsLook.Rest"/>, so a paused glyph is a low, flat row rather than nothing.
    /// </summary>
    public static void Draw(DrawingContext context, Rect area, ReadOnlySpan<float> levels, in LiveBarsLook look)
    {
        var n = levels.Length;
        if (n == 0 || area.Width <= 0 || area.Height <= 0)
            return;
        var barW = (area.Width - look.Gap * (n - 1)) / n;
        if (barW <= 0)
            return;
        for (var i = 0; i < n; i++)
        {
            var level = Math.Max(look.Rest, Math.Clamp(levels[i], 0f, 1f));
            var x = area.X + i * (barW + look.Gap);
            if (!look.Segmented)
            {
                var h = Math.Max(2, area.Height * level);
                context.FillRectangle(Fill(Mix(look.Low, look.High, level)), new Rect(x, area.Bottom - h, barW, h));
                continue;
            }

            var pitch = look.SegmentHeight + look.SegmentGap;
            var lit = Math.Clamp((int)Math.Round(level * look.Segments), 1, look.Segments);
            for (var s = 0; s < lit; s++)
            {
                var color = s == lit - 1 ? look.Tip : Mix(look.Low, look.High, look.Segments <= 1 ? 0 : (double)s / (look.Segments - 1));
                context.FillRectangle(Fill(color), new Rect(x, area.Bottom - (s + 1) * pitch + look.SegmentGap, barW, look.SegmentHeight));
            }
        }
    }
}
