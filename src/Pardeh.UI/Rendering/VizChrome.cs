using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Pardeh.Skinning.Runtime.Elements;

namespace Pardeh.UI.Rendering;

/// <summary>What the phone looks draw over a visualization: the small style chip (it opens the picker) and the style's name after a change.</summary>
internal static class VizChrome
{
    /// <summary>The chip: a dark translucent disc with four dots (styles), bottom right of <paramref name="bounds"/>.</summary>
    public static void DrawChip(DrawingContext context, Rect bounds, bool glass)
    {
        var (cx, cy, size) = EffectsElement.ChipRect(bounds.Width, bounds.Height);
        var centre = new Point(bounds.X + cx + size / 2, bounds.Y + cy + size / 2);
        var fill = glass ? GlassPalette.Map(Color.FromArgb(120, 3, 14, 60)) : Color.FromArgb(110, 0, 0, 0);
        var ring = glass ? GlassPalette.Parse("#9ED2FF") : Colors.White;
        context.DrawEllipse(LiveBars.Fill(fill), new Pen(LiveBars.Fill(Color.FromArgb(120, ring.R, ring.G, ring.B)), 1), centre, size / 2, size / 2);
        var dot = LiveBars.Fill(Color.FromArgb(225, 255, 255, 255));
        foreach (var (dx, dy) in new[] { (-5.0, -5.0), (5.0, -5.0), (-5.0, 5.0), (5.0, 5.0) })
            context.DrawEllipse(dot, null, new Point(centre.X + dx, centre.Y + dy), 2.5, 2.5);
    }

    /// <summary>The name of the style just chosen, in a pill near the bottom, fading out (nothing is drawn once it is over).</summary>
    public static void DrawFlash(DrawingContext context, Rect bounds, bool glass)
    {
        var remaining = VizDirector.FlashUntil - SpectrumFeed.Now;
        if (remaining <= 0 || VizDirector.FlashText.Length == 0)
            return;
        var alpha = Math.Clamp(remaining / 0.5, 0, 1);
        var text = new FormattedText(VizDirector.FlashText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, UiTheme.Typeface(true), 13,
            LiveBars.Fill(Color.FromArgb((byte)(255 * alpha), 255, 255, 255)))
        {
            MaxTextWidth = Math.Max(20, bounds.Width - 100),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        var pill = new Rect(bounds.X + (bounds.Width - text.Width) / 2 - 14, bounds.Bottom - 40, text.Width + 28, text.Height + 12);
        var back = glass ? GlassPalette.Map(Color.FromArgb((byte)(170 * alpha), 3, 14, 60)) : Color.FromArgb((byte)(150 * alpha), 0, 0, 0);
        context.DrawRectangle(LiveBars.Fill(back), null, new RoundedRect(pill, pill.Height / 2));
        context.DrawText(text, new Point(pill.X + 14, pill.Y + 6));
    }
}
