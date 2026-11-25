using Avalonia;
using Avalonia.Media;
using static Pardeh.UI.FullMode.LunaArt;

namespace Pardeh.UI.FullMode;

/// <summary>
/// The art of the favourite heart (Now Playing) and the queue tools (sort, filter) of the phone looks. Vintage looks (Glass, Luna,
/// Luna Plus, Pardeh Ten) draw the glossy pictorial objects of <see cref="Pardeh.UI.Rendering.PictorialIcons"/>; the modern looks
/// (Fluent, Ember, Horizon) the Fluent UI System Icons (heart, arrow sort, filter). Nothing here is an outline glyph in a coloured disc.
/// </summary>
internal static partial class PhoneSkin
{
    /// <summary>Touch box of the heart button (skin units; the screen is ~360 wide, so 44 is a 44 dp target).</summary>
    private const int FavSize = 44;

    private const string IconSort = "M 3,5 L 10,5 M 3,10 L 8,10 M 3,15 L 6,15 M 14,4 L 14,16 M 11,13 L 14,16.5 L 17,13";
    private const string IconFilter = "M 3,4.5 L 17,4.5 L 11.5,10.5 L 11.5,16 L 8.5,14.5 L 8.5,10.5 Z";

    private static readonly Color HeartRed = Color.Parse("#FF5C7A");

    /// <summary>How the tool glyphs are drawn: glossy pictures, Fluent icons or plain white strokes (a coloured header band).</summary>
    private enum ToolStyle { Pictorial, Fluent, Stroke }

    /// <summary>
    /// fav.png (not a favourite), fav_down.png (a favourite: the "down" image of a FAVBUTTON), fav_disabled.png (nothing playing).
    /// <paramref name="glass"/>: Pardeh Glass (its pictures follow the skin colour).
    /// </summary>
    private static void AddFavoriteArt(Dictionary<string, byte[]> files, bool modern, bool glass, Color off, Color dim)
    {
        void Heart(DrawingContext g, bool on, bool disabled)
        {
            HitBox(g, FavSize); // the whole box is a tap target
            if (modern)
            {
                var color = disabled ? WithAlpha(dim, 90) : on ? HeartRed : off;
                if (on)
                    g.DrawEllipse(Solid(WithAlpha(HeartRed, 40)), null, new Point(FavSize / 2.0, FavSize / 2.0), 19, 19);
                FluentGlyph(g, color, "heart", on, FavSize / 2.0, FavSize / 2.0, 28);
                return;
            }

            using (g.PushOpacity(disabled ? 0.4 : 1))
                Pardeh.UI.Rendering.PictorialIcons.Draw(g, on ? "heart" : "heartoff", new Rect(7, 6, 30, 30), shadow: true, glass: false);
        }

        files["fav.png"] = Png(FavSize, FavSize, g => Heart(g, false, false));
        files["fav_down.png"] = Png(FavSize, FavSize, g => Heart(g, true, false));
        files["fav_disabled.png"] = Png(FavSize, FavSize, g => Heart(g, false, true));
    }

    /// <summary>
    /// qsort.png / qsort_down.png (pressed) and qfilter.png / qfilter_down.png (a filter is on) / disabled variants: square
    /// <paramref name="size"/> buttons; <paramref name="ink"/> is the glyph colour, <paramref name="on"/> the lit one.
    /// </summary>
    private static void AddQueueToolArt(Dictionary<string, byte[]> files, int size, ToolStyle style, Color ink, Color on, bool glass = false)
    {
        void Tool(DrawingContext g, bool filter, bool lit, bool pressed, bool disabled)
        {
            HitBox(g, size);
            var c = size / 2.0;
            if (lit || pressed)
                g.DrawEllipse(Solid(WithAlpha(lit ? on : ink, (byte)(lit ? 60 : 40))), null, new Point(c, c), size / 2.0 - 2, size / 2.0 - 2);
            switch (style)
            {
                case ToolStyle.Fluent:
                    FluentGlyph(g, disabled ? WithAlpha(ink, 90) : lit ? on : ink, filter ? "filter" : "arrow_sort", lit && filter, c, c, size * 0.64);
                    break;
                case ToolStyle.Stroke:
                    StrokeIcon(g, disabled ? WithAlpha(ink, 100) : lit ? on : ink, filter ? IconFilter : IconSort, c, c, size / 20.0 * 0.78, 1.8);
                    break;
                default:
                    using (g.PushOpacity(disabled ? 0.4 : 1))
                    {
                        var box = size * 0.7;
                        Pardeh.UI.Rendering.PictorialIcons.Draw(g, filter ? "filter" : "sort", new Rect(c - box / 2, c - box / 2, box, box), shadow: false, glass: glass);
                    }

                    break;
            }
        }

        files["qsort.png"] = Png(size, size, g => Tool(g, false, false, false, false));
        files["qsort_down.png"] = Png(size, size, g => Tool(g, false, false, true, false));
        files["qsort_disabled.png"] = Png(size, size, g => Tool(g, false, false, false, true));
        files["qfilter.png"] = Png(size, size, g => Tool(g, true, false, false, false));
        files["qfilter_down.png"] = Png(size, size, g => Tool(g, true, true, false, false));
    }
}
