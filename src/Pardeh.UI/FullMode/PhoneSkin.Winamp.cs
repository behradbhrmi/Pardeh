using Avalonia;
using Avalonia.Media;
using static Pardeh.UI.FullMode.LunaArt;

namespace Pardeh.UI.FullMode;

/// <summary>
/// The art of "Winamp Phone" beyond its palette, after the retro mobile concept: brushed dark metal, framed cards,
/// raised round buttons (the play button with an amber ring), an amber progress bar with a round thumb. All drawn here in code; no
/// picture from the concept and no Winamp logo is used (the top bar keeps Pardeh's logo or the classic flag).
/// </summary>
internal static partial class PhoneSkin
{
    private const int TileW = 360, TileH = 240; // the brushed backdrop tile

    private static Color A(byte alpha, string hex) => WithAlpha(Color.Parse(hex), alpha);

    private static IBrush Grad(params (string Color, double At)[] stops) =>
        Vertical(stops.Select(s => (Color.Parse(s.Color), s.At)).ToArray());

    /// <summary>Horizontal brush strokes: short 1 unit lines of faint light and shade, the same on every run, wrapping at the tile's width.</summary>
    private static void Brushed(DrawingContext g, int width, int height, int seed, byte strength)
    {
        var random = new Random(seed);
        for (var y = 0; y < height; y++)
        {
            for (var i = 0; i < 5; i++)
            {
                var x = random.Next(width);
                var length = 20 + random.Next(width / 2);
                var brush = Solid(WithAlpha(random.Next(2) == 0 ? Colors.White : Colors.Black, (byte)random.Next(2, strength + 1)));
                g.FillRectangle(brush, new Rect(x, y, Math.Min(length, width - x), 1));
                if (x + length > width)
                    g.FillRectangle(brush, new Rect(0, y, x + length - width, 1));
            }
        }
    }

    /// <summary>A raised metal disc: a dark seat, a light-to-dark face, a top highlight; the play button adds the amber ring.</summary>
    private static void MetalDisc(DrawingContext g, double cx, double cy, double radius, bool down, bool ring)
    {
        var c = new Point(cx, cy);
        g.DrawEllipse(Solid(A(110, "#000000")), null, new Point(cx, cy + 2), radius + 1, radius + 1);
        g.DrawEllipse(Solid(Color.Parse("#05080E")), Pen(Color.Parse("#4A566C"), 1), c, radius, radius);
        var face = down ? Grad(("#0B1019", 0), ("#1A2232", 1)) : Grad(("#3A465C", 0), ("#1D2637", 0.4), ("#111826", 0.75), ("#20293A", 1));
        g.DrawEllipse(face, Pen(Color.Parse("#0A0E16"), 1), c, radius - 2, radius - 2);
        g.DrawEllipse(null, new Pen(Grad(("#8C9AB2", 0), ("#00000000", 0.55)), 1), c, radius - 3, radius - 3);
        if (!ring)
            return;
        g.DrawEllipse(null, Pen(A(45, "#FF9D00"), 6), c, radius - 4.5, radius - 4.5);
        g.DrawEllipse(null, new Pen(Grad(("#FFD16A", 0), ("#FF9C00", 0.5), ("#C75A00", 1)), 2.4), c, radius - 4.5, radius - 4.5);
    }

    /// <summary>A raised rounded square (the equalizer button, the selected page in the bottom bar).</summary>
    private static void MetalKey(DrawingContext g, Rect r, double corner, bool down)
    {
        g.DrawRectangle(down ? Grad(("#0A0E16", 0), ("#171F2E", 1)) : Grad(("#2E394E", 0), ("#1A2232", 0.5), ("#121927", 1)), Pen(Color.Parse("#3F4A60")), new RoundedRect(r, corner));
        g.DrawRectangle(null, new Pen(Grad(("#66758E", 0), ("#00000000", 0.4)), 1), new RoundedRect(r.Deflate(1), corner - 1));
    }

    private const string EqBarsPath = "M 4,16 L 4,9 M 10,16 L 10,4 M 16,16 L 16,11";

    private static void AddWinampArt(Dictionary<string, byte[]> files, Palette p)
    {
        // The page: brushed blue-black metal.
        files["back.png"] = Png(TileW, TileH, g =>
        {
            g.FillRectangle(Solid(Color.Parse("#0A0E16")), new Rect(0, 0, TileW, TileH));
            Brushed(g, TileW, TileH, 11, 9);
        }, 1);

        // The top bar: a brushed plate with a bevel and an amber accent cut across its left corner.
        files["bar.png"] = Png(TileW, Bar, g =>
        {
            g.FillRectangle(Grad(("#2B3548", 0), ("#19202E", 0.5), ("#0E131D", 1)), new Rect(0, 0, TileW, Bar));
            Brushed(g, TileW, Bar, 23, 16);
            g.FillRectangle(Solid(A(80, "#A9B8D0")), new Rect(0, 1, TileW, 1));
            g.FillRectangle(Solid(Color.Parse("#03050A")), new Rect(0, Bar - 3, TileW, 2));
            g.FillRectangle(Solid(Color.Parse("#4A566C")), new Rect(0, Bar - 1, TileW, 1));
        });
        files["nav.png"] = Png(TileW, Nav, g =>
        {
            g.FillRectangle(Grad(("#1A2231", 0), ("#0C1119", 1)), new Rect(0, 0, TileW, Nav));
            Brushed(g, TileW, Nav, 37, 11);
            g.FillRectangle(Solid(Color.Parse("#4A566C")), new Rect(0, 0, TileW, 1));
            g.FillRectangle(Solid(Color.Parse("#03050A")), new Rect(0, 1, TileW, 1));
        });
        foreach (var (key, _) in NavIcons)
        {
            files[$"nav_{key}_down.png"] = Png(72, 52, g =>
            {
                g.FillRectangle(Solid(Color.FromArgb(2, 0, 0, 0)), new Rect(0, 0, 72, 52));
                MetalKey(g, new Rect(5.5, 0.5, 61, 50), 9, down: false);
                NavGlyph(g, p, key, true, 36, 17);
            });
        }

        // The framed player card (nine-patches, 20 unit corners): the bezel around the stage and the plate under the controls.
        files["np_frame.png"] = Png(64, 64, g =>
        {
            g.DrawRectangle(Grad(("#58657C", 0), ("#2A3446", 0.2), ("#151C2A", 0.8), ("#313C50", 1)), Pen(Color.Parse("#06090F")), new RoundedRect(new Rect(0.5, 0.5, 63, 63), 14));
            g.DrawRectangle(null, Pen(A(120, "#9AAAC4")), new RoundedRect(new Rect(1.5, 1.5, 61, 61), 13));
            g.DrawRectangle(Solid(Color.Parse("#04070D")), Pen(Color.Parse("#0A0F18")), new RoundedRect(new Rect(5.5, 5.5, 53, 53), 9));
        });
        files["np_deck.png"] = Png(64, 64, g =>
        {
            g.DrawRectangle(Grad(("#1D2637", 0), ("#141B29", 0.5), ("#0E141F", 1)), Pen(Color.Parse("#3B465A")), new RoundedRect(new Rect(0.5, 0.5, 63, 63), 14));
            g.DrawRectangle(null, new Pen(Grad(("#5F6D86", 0), ("#00000000", 0.3)), 1), new RoundedRect(new Rect(1.5, 1.5, 61, 61), 13));
        });

        // Raised round transport buttons; skip 10 stays a bare glyph so the row does not crowd.
        foreach (var (state, suffix) in States)
        {
            var down = state == State.Down;
            Color Tint(Color c) => state == State.Disabled ? WithAlpha(c, 90) : c;
            files[$"play{suffix}.png"] = Png(64, 64, g =>
            {
                MetalDisc(g, 32, 31, 30, down, ring: true);
                FluentGlyph(g, Tint(p.Accent), "play", true, 33, 31, 30);
            });
            files[$"pause{suffix}.png"] = Png(64, 64, g =>
            {
                MetalDisc(g, 32, 31, 30, down, ring: true);
                FluentGlyph(g, Tint(p.Accent), "pause", true, 32, 31, 30);
            });
            foreach (var (name, glyph) in new[] { ("prev", "previous"), ("next", "next") })
            {
                files[$"{name}{suffix}.png"] = Png(52, 52, g =>
                {
                    MetalDisc(g, 26, 25, 24, down, ring: false);
                    FluentGlyph(g, Tint(Color.Parse("#E7EDF8")), glyph, true, 26, 25, 24);
                });
            }
        }

        foreach (var (name, glyph) in new[] { ("shuffle", "arrow_shuffle"), ("repeat", "arrow_repeat_all") })
        {
            foreach (var on in new[] { false, true })
            {
                files[name + (on ? "_down" : "") + ".png"] = Png(44, 44, g =>
                {
                    HitBox(g, 44);
                    MetalDisc(g, 22, 21, 19, down: on, ring: false);
                    FluentGlyph(g, on ? p.Accent : Color.Parse("#C5D0E4"), glyph, on, 22, 21, 21);
                });
            }
        }

        foreach (var pressed in new[] { false, true })
        {
            files[pressed ? "eqkey_down.png" : "eqkey.png"] = Png(44, 44, g =>
            {
                HitBox(g, 44);
                MetalKey(g, new Rect(3.5, 5.5, 37, 33), 7, pressed);
                StrokeIcon(g, p.Accent, EqBarsPath, 22, 22, 1.15, 2.6);
            });
        }

        // The PC look's equalizer key opens and closes a drawer, so closed it is grey, like shuffle and repeat when off.
        files["eqkey_off.png"] = Png(44, 44, g =>
        {
            HitBox(g, 44);
            MetalKey(g, new Rect(3.5, 5.5, 37, 33), 7, false);
            StrokeIcon(g, Color.Parse("#C5D0E4"), EqBarsPath, 22, 22, 1.15, 2.6);
        });

        // Progress and volume: a sunk capsule and an amber fill (end caps kept by borderSize), a round amber thumb.
        files["seek_track.png"] = Png(24, 30, g =>
        {
            g.DrawRectangle(Grad(("#02050A", 0), ("#1D2739", 1)), Pen(Color.Parse("#4E5B72")), new RoundedRect(new Rect(0.5, 10.5, 23, 9), 4.5));
        });
        files["seek_fill.png"] = Png(24, 30, g =>
        {
            g.DrawRectangle(Grad(("#FFD36A", 0), ("#FF9D00", 0.55), ("#D55E00", 1)), null, new RoundedRect(new Rect(1.5, 11.5, 21, 7), 3.5));
            g.FillRectangle(Solid(A(110, "#FFFFFF")), new Rect(4, 12.5, 16, 1));
        });
        foreach (var hover in new[] { false, true })
        {
            files[hover ? "thumb_hover.png" : "thumb.png"] = Png(30, 30, g =>
            {
                var c = new Point(15, 15);
                g.DrawEllipse(Solid(A(100, "#000000")), null, new Point(15, 16.5), 10, 10);
                g.DrawEllipse(Grad(("#FFE9AE", 0), ("#FFAE22", 0.55), ("#C96A00", 1)), Pen(Color.Parse("#6A3A00")), c, 10, 10);
                g.DrawEllipse(null, Pen(A(170, "#FFF3D0")), c, 8.5, 8.5);
            });
        }

        files["stage_frame.png"] = Png(12, 12, g => g.FillRectangle(Solid(Color.Parse("#04070D")), new Rect(0, 0, 12, 12)));
        files["cover_frame.png"] = Png(76, 76, g =>
        {
            g.DrawRectangle(Grad(("#14356E", 0), ("#071430", 1)), Pen(Color.Parse("#65748C"), 1.5), new RoundedRect(new Rect(0.75, 0.75, 74.5, 74.5), 6));
            g.DrawGeometry(null, new Pen(Solid(A(200, "#FFB52E")), 2.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
                Geo("M 32,48 L 32,26 L 48,22 L 48,44 M 28,52 m -4,0 a 4,3 0 1,0 8,0 a 4,3 0 1,0 -8,0 M 44,48 m -4,0 a 4,3 0 1,0 8,0 a 4,3 0 1,0 -8,0"));
        });
    }
}
