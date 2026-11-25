using Avalonia;
using Avalonia.Media;
using static Pardeh.UI.FullMode.LunaArt;

namespace Pardeh.UI.FullMode;

/// <summary>
/// The Lyrics button of Now Playing (all phone looks) and the attributes of the LYRICS element. Vintage looks (Glass, Luna, Luna Plus,
/// Pardeh Ten) draw the glossy pictorial karaoke microphone with a gold note of <see cref="Pardeh.UI.Rendering.PictorialIcons"/>; the modern looks
/// (Fluent, Ember, Horizon) Microsoft's Fluent UI System Icon "Mic" (filled when the lyrics are open). No outline glyph in a coloured disc.
/// </summary>
internal static partial class PhoneSkin
{
    /// <summary>Touch box of the lyrics button (skin units, ~44 dp on a phone).</summary>
    internal const int LyricsSize = 44;

    /// <summary>lyr.png (hidden), lyr_down.png (the lyrics view is showing).</summary>
    private static void AddLyricsArt(Dictionary<string, byte[]> files, bool modern, bool glass, Color off, Color on)
    {
        void Icon(DrawingContext g, bool lit)
        {
            HitBox(g, LyricsSize); // the whole box is a tap target
            var c = LyricsSize / 2.0;
            if (modern)
            {
                // Lit: the filled Mic in the accent colour (no disc behind it).
                FluentGlyph(g, lit ? on : off, "mic", lit, c, c, 28);
                return;
            }

            if (lit)
                g.DrawEllipse(Solid(WithAlpha(Colors.White, 58)), Pen(WithAlpha(Colors.White, 130), 1.2), new Point(c, c), 20, 20);
            Pardeh.UI.Rendering.PictorialIcons.Draw(g, "lyrics", new Rect(c - 16, c - 17, 32, 32), shadow: !lit, glass: glass);
        }

        files["lyr.png"] = Png(LyricsSize, LyricsSize, g => Icon(g, false));
        files["lyr_down.png"] = Png(LyricsSize, LyricsSize, g => Icon(g, true));
    }

    /// <summary>The LYRICS element of a phone look (attributes: ink, accent, plate colour, icon family, the skin's font).</summary>
    private static string LyricsElementXml(string id, int width, int height, string ink, string accent, string plate, bool modern, string font, string? back = null) =>
        $"""<LYRICS id="{id}" left="0" top="0" width="{width}" height="{height}" visible="false" textColor="{ink}" accentColor="{accent}" plateColor="{plate}"{(back is null ? string.Empty : $" backColor=\"{back}\"")} icons="{(modern ? "fluent" : "pictorial")}" fontType="{font}" horizontalAlignment="stretch" verticalAlignment="stretch"/>""";
}
