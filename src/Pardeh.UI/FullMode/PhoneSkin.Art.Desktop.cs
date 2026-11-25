using Avalonia;
using Avalonia.Media;
using static Pardeh.UI.FullMode.LunaArt;

namespace Pardeh.UI.FullMode;

/// <summary>
/// What the PC versions of "Winamp Phone" and "Groove Phone" need beyond the phone pictures: a stop key (no phone layout has
/// one), Winamp's mute key as a metal disc like its other toggles, the rail tabs, and the pictures of Winamp's equalizer
/// drawer. The phone looks never pass through here.
/// </summary>
internal static partial class PhoneSkin
{
    private static bool ArtLook(string key) => key is "winamp" or "groove";

    private static void AddDesktopExtraArt(Dictionary<string, byte[]> files, Palette p)
    {
        if (!HasArt(p))
            return;
        var winamp = p.Key == "winamp-phone";
        foreach (var (state, suffix) in States)
        {
            files[$"stop{suffix}.png"] = Png(52, 52, g =>
            {
                HitBox(g, 52);
                void Square(Color color) => g.DrawRectangle(Solid(color), null, new RoundedRect(new Rect(19, winamp ? 18 : 19, 14, 14), 2));
                if (!winamp)
                {
                    Side(g, p, state, Square);
                    return;
                }

                MetalDisc(g, 26, 25, 24, state == State.Down, ring: false);
                Square(state == State.Disabled ? WithAlpha(Color.Parse("#E7EDF8"), 90) : Color.Parse("#E7EDF8"));
            });
        }

        if (!winamp)
            return;
        foreach (var (name, icon) in new[] { ("mute", "speaker_2"), ("muted", "speaker_mute") })
        {
            foreach (var down in new[] { false, true })
            {
                var lit = down || name == "muted";
                files[name + (down ? "_down" : "") + ".png"] = Png(44, 44, g =>
                {
                    HitBox(g, 44);
                    MetalDisc(g, 22, 21, 19, lit, ring: false);
                    FluentGlyph(g, lit ? p.Accent : Color.Parse("#C5D0E4"), icon, lit, 22, 21, 21);
                });
            }
        }
    }

    /// <summary>The open page on the rail: Winamp's raised metal key with an amber lamp; Groove's lit acrylic row with a short blue bar.</summary>
    private static void ArtRailItem(DrawingContext g, string key, Palette p)
    {
        if (key == "winamp")
        {
            MetalKey(g, new Rect(0.5, 3.5, 127, 33), 7, down: false);
            g.DrawRectangle(Solid(A(60, "#FF9D00")), null, new RoundedRect(new Rect(2.5, 9, 7, 22), 3));
            g.DrawRectangle(Solid(p.Accent), null, new RoundedRect(new Rect(4.5, 11, 3, 18), 1.5));
            return;
        }

        g.DrawRectangle(Solid(A(42, "#CFE6FF")), Pen(A(60, "#CFE6FF")), new RoundedRect(new Rect(0.5, 3.5, 127, 33), 6));
        g.DrawRectangle(Solid(p.Accent), null, new RoundedRect(new Rect(1.5, 12, 3, 16), 1.5));
    }

    /// <summary>Winamp's equalizer drawer: its plate, the sunk slider slot with the amber fill, and the amber-edged fader cap.</summary>
    private static void EqualizerDrawerArt(Dictionary<string, byte[]> files, int density)
    {
        byte[] Draw(int w, int h, Action<DrawingContext> draw) => LunaArt.Png(w, h, draw, density);

        files["eqd_track.png"] = Draw(14, 4, g =>
        {
            g.FillRectangle(Solid(Color.Parse("#4E5B72")), new Rect(4, 0, 6, 4));
            g.FillRectangle(Solid(Color.Parse("#02050A")), new Rect(5, 0, 4, 4));
        });
        files["eqd_fill.png"] = Draw(14, 4, g =>
        {
            g.FillRectangle(Solid(A(70, "#FF9D00")), new Rect(4, 0, 6, 4));
            g.FillRectangle(Solid(Color.Parse("#FF9D00")), new Rect(5, 0, 4, 4));
        });
        foreach (var hover in new[] { false, true })
        {
            files[hover ? "eqd_thumb_hover.png" : "eqd_thumb.png"] = Draw(14, 10, g =>
            {
                g.DrawRectangle(Grad((hover ? "#5A6880" : "#46536B", 0), ("#1A2232", 1)), Pen(Color.Parse("#05080E")), new RoundedRect(new Rect(0.5, 0.5, 13, 9), 2));
                g.FillRectangle(Solid(Color.Parse(hover ? "#FFD36A" : "#FF9D00")), new Rect(2, 4, 10, 2));
            });
        }
    }
}
