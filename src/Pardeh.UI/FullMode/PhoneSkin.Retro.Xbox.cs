using Avalonia;
using Avalonia.Media;
using Pardeh.UI.Rendering;
using static Pardeh.UI.FullMode.LunaArt;

namespace Pardeh.UI.FullMode;

/// <summary>
/// The two Xbox dashboards: the 2005 "blades" of the Xbox 360 (silver, one colour for each blade, a chrome orb with a green
/// ring) and the 2001 original (black and acid green, a glowing core, cut-corner panels and segmented meters).
/// </summary>
internal static partial class PhoneSkin
{
    // ---- Xbox 360 blades ------------------------------------------------------------------------

    private static readonly Color BladeGreen = Color.Parse("#5FA01C");

    private static readonly (string Light, string Dark)[] BladeColours =
    [
        ("#A6D645", "#5B981A"), // now playing: green
        ("#FFB43C", "#E0720A"), // library: orange
        ("#5DB2F0", "#1E6FBE"), // playlist: blue
        ("#B48AE0", "#7247A8"), // radio: violet
        ("#8F9B95", "#4E5A55"), // settings: slate
    ];

    private static RetroLook BladesLook() => new()
    {
        Palette = new Palette
        {
            Key = "blades-phone", Name = "Xbox 360 Blades", Font = "Segoe UI", Flat = true, Fluent = true,
            BarTop = Hx("#9CCB3B"), BarBottom = Hx("#56921A"), BarLine = Hx("#E9F5D8"), BarInk = "#FFFFFF", BarIcon = Hx("#1F2A20"),
            Back = Hx("#D5DAD6"),
            NavTop = Hx("#F1F3F1"), NavBottom = Hx("#C4CBC6"), NavLine = Hx("#FFFFFF"), NavInk = Hx("#4A544D"), NavSel = Hx("#FFFFFF"),
            NavPill = Hx("#5B981A"), NavPillRim = Hx("#FFFFFF"), LabelInk = "#3E483F", LabelSel = "#FFFFFF",
            TitleInk = "#1F2A20", ArtistInk = "#4A5A4C", TimeInk = "#37443A", StageGlow = Hx("#E7ECE8"),
            Track = Hx("#99A39C"), Fill = BladeGreen, ThumbLight = Hx("#FFFFFF"), ThumbDark = BladeGreen,
            OrbTop = Hx("#FFFFFF"), OrbBottom = Hx("#AAB3AD"), OrbRim = Hx("#6B766F"), Glyph = Hx("#2F5A08"), Dim = Hx("#3E483F"), Accent = Hx("#3F7F08"),
            ListInk = "#1F2A20", ListPlaying = "#2F6E00", ListSelect = "#CBE29B",
        },
        Settings = new MobileTheme(false, Hx("#E3E7E4"), Hx("#FAFBFA"), BladeGreen, Hx("#9CCB3B"), Hx("#56921A"), Colors.White),
        Art = BladesArt,
        Lowercase = true,
        Title = (17, 12, true),
    };

    private static void BladesArt(Dictionary<string, byte[]> files, Palette p)
    {
        Wallpaper(files, (g, w, h) =>
        {
            g.FillRectangle(Vertical((Hx("#EEF1EE"), 0), (Hx("#D9DEDA"), 0.6), (Hx("#C2CAC4"), 1)), new Rect(0, 0, w, h));
            // The big green curve of the open blade.
            g.DrawGeometry(Vertical((Color.FromArgb(0, 150, 200, 60), 0), (Color.FromArgb(130, 140, 195, 45), 1)), null,
                Path($"M {-10},{h * 0.86} C {w * 0.30},{h * 0.64} {w * 0.72},{h * 0.66} {w + 10},{h * 0.50} L {w + 10},{h + 10} L {-10},{h + 10} Z"));
            g.DrawGeometry(null, Pen(Color.FromArgb(200, 255, 255, 255), 2), Path($"M {-10},{h * 0.90} C {w * 0.34},{h * 0.69} {w * 0.74},{h * 0.71} {w + 10},{h * 0.56}"));

            // The edges of the other blades, waiting at both sides: one on the left, two on the right.
            void Edge(double x, double width, bool left, (string Light, string Dark) colour)
            {
                var bulge = left ? 4.0 : -4.0;
                var inner = left ? x + width : x;
                var outer = left ? x - 4 : x + width + 4;
                var shape = Path($"M {outer},{-4} L {inner},{-4} C {inner + bulge},{h * 0.3} {inner + bulge},{h * 0.7} {inner},{h + 4} L {outer},{h + 4} Z");
                g.DrawGeometry(Horizontal((Hx(colour.Light), 0), (Hx(colour.Dark), 1)), Pen(Color.FromArgb(210, 255, 255, 255), 1.2), shape);
            }

            Edge(0, 5, true, BladeColours[1]);
            Edge(w - 9, 9, false, BladeColours[2]);
            Edge(w - 4, 4, false, BladeColours[3]);
        });

        BarArt(files, (g, r) =>
        {
            g.FillRectangle(Vertical((Hx("#B7DE58"), 0), (Hx("#86BC2A"), 0.5), (Hx("#6AA81C"), 0.52), (Hx("#4F8A16"), 1)), r);
            g.FillRectangle(Solid(Color.FromArgb(150, 255, 255, 255)), new Rect(0, 1, 1, 1));
            g.FillRectangle(Solid(p.BarLine), new Rect(0, Bar - 2, 1, 2));
        });

        NavArt(files, (g, r) =>
        {
            g.FillRectangle(Vertical((Hx("#FBFCFB"), 0), (Hx("#D3D9D4"), 0.5), (Hx("#B4BDB7"), 1)), r);
            g.FillRectangle(Solid(Hx("#8E9991")), new Rect(0, 0, 1, 1));
        });
        NavButtons(files, (g, key, index, selected) =>
        {
            var (light, dark) = BladeColours[index];
            if (selected)
            {
                // The open blade: a tall tab with a curved shoulder.
                var blade = Geo("M 4,52 L 7,10 Q 8,0.8 18,0.8 L 54,0.8 Q 64,0.8 65,10 L 68,52 Z");
                g.DrawGeometry(Vertical((Hx(light), 0), (Hx(dark), 1)), Pen(Colors.White, 1.2), blade);
                using (g.PushGeometryClip(blade))
                    g.FillRectangle(Solid(Color.FromArgb(55, 255, 255, 255)), new Rect(4, 0, 64, 17));
            }
            else
            {
                g.DrawRectangle(Horizontal((Hx(light), 0), (Hx(dark), 1)), null, new RoundedRect(new Rect(17, 1.5, 38, 4), 2));
            }

            FluentGlyph(g, selected ? Colors.White : p.NavInk, FluentNav[key], selected, 36, 21, 23);
        });

        // Chrome spheres; the big one carries the glowing green ring of the guide button (without its logo).
        TransportArt(files, true, (g, size, hero, _, state) =>
        {
            var centre = new Point(size / 2, size / 2);
            var radius = hero ? 29.0 : size == 52 ? 22 : 18.5;
            g.DrawEllipse(Solid(Color.FromArgb(55, 20, 30, 20)), null, new Point(centre.X, centre.Y + 2), radius, radius);
            var face = state == State.Down ? Vertical((Hx("#A3ADA6"), 0), (Hx("#F4F6F4"), 1))
                : Vertical((Hx("#FFFFFF"), 0), (Hx("#E1E6E2"), 0.5), (Hx("#A7B1AA"), 1));
            g.DrawEllipse(face, Pen(Hx("#6F7A73"), 1.2), centre, radius, radius);
            if (hero)
            {
                var ring = state == State.Disabled ? Hx("#9AA79B") : state == State.Down ? Hx("#B6F04A") : Hx("#6DBE1E");
                if (state != State.Disabled)
                    g.DrawEllipse(null, Pen(Color.FromArgb(90, 150, 230, 40), 7), centre, radius - 6.5, radius - 6.5);
                g.DrawEllipse(null, Pen(ring, 3), centre, radius - 6.5, radius - 6.5);
            }

            g.DrawEllipse(Vertical((Color.FromArgb(215, 255, 255, 255), 0), (Color.FromArgb(0, 255, 255, 255), 1)), null,
                new Point(centre.X, centre.Y - radius * 0.46), radius * 0.62, radius * 0.38);
        }, (hero, _, _) => hero ? p.Glyph : Hx("#333D36"), heroGlyph: 24, sideGlyph: 22, skipGlyph: 24);

        ToggleArt(files, true, Hx("#4F5A52"), Hx("#3F7F08"), (g, down) =>
        {
            if (down)
                Gel(g, new Rect(4, 6, 36, 32), 15, Hx("#F4FBE8"), Hx("#C3E08C"), BladeGreen, 100, 1.2);
        });

        SliderArt(files, 8,
            (g, b) =>
            {
                g.FillRectangle(Vertical((Hx("#7F8A82"), 0), (Hx("#BCC4BE"), 1)), b);
                g.FillRectangle(Solid(Color.FromArgb(200, 255, 255, 255)), new Rect(0, b.Bottom, 6, 1));
            },
            (g, b) => g.FillRectangle(Vertical((Hx("#C6EC6C"), 0), (Hx("#74B424"), 0.5), (Hx("#4E8C12"), 1)), b),
            (g, hover) =>
            {
                var centre = new Point(15, 15);
                g.DrawEllipse(Solid(Color.FromArgb(70, 20, 30, 20)), null, new Point(15, 16.5), 11, 11);
                g.DrawEllipse(Vertical((Hx("#FFFFFF"), 0), (Hx("#AEB8B1"), 1)), Pen(Hx("#66716A"), 1.1), centre, 11, 11);
                g.DrawEllipse(null, Pen(hover ? Hx("#9BE82C") : Hx("#6DBE1E"), 2.4), centre, 5.5, 5.5);
            });
    }

    // ---- The original Xbox ----------------------------------------------------------------------

    private static readonly Color AcidGreen = Color.Parse("#9BE80C");

    private static RetroLook XboxLook() => new()
    {
        Palette = new Palette
        {
            Key = "xbox-phone", Name = "Xbox Classic", Font = "Verdana", Flat = true, Fluent = true, Dark = true,
            BarTop = Hx("#122806"), BarBottom = Hx("#061002"), BarLine = Hx("#4F8A0C"), BarInk = "#C4FF3C", BarIcon = Hx("#C4FF3C"),
            Back = Hx("#030901"),
            NavTop = Hx("#0C1C04"), NavBottom = Hx("#030901"), NavLine = Hx("#4F8A0C"), NavInk = Hx("#6FAF22"), NavSel = Hx("#0A1700"),
            NavPill = Hx("#9BE80C"), NavPillRim = Hx("#E2FF9A"), LabelInk = "#78B82A", LabelSel = "#E4FF9C",
            TitleInk = "#D6FF6A", ArtistInk = "#7FC22C", TimeInk = "#B4E85A", StageGlow = Hx("#071203"),
            Track = Hx("#1F3A0A"), Fill = AcidGreen, ThumbLight = Hx("#E8FFAC"), ThumbDark = Hx("#5E9E08"),
            OrbTop = Hx("#C8FF4A"), OrbBottom = Hx("#2E6004"), OrbRim = Hx("#B8F83C"), Glyph = Hx("#102400"), Dim = Hx("#B4E85A"), Accent = AcidGreen,
            ListInk = "#C9F27E", ListPlaying = "#F0FFC0", ListSelect = "#1E4006",
        },
        Settings = new MobileTheme(true, Hx("#050D02"), Hx("#112405"), AcidGreen),
        Art = XboxArt,
        Title = (12, 15, true),
        NavText = (7, 37),
    };

    /// <summary>A rectangle with its top-left and bottom-right corners cut off.</summary>
    private static Geometry Cut(Rect r, double c) =>
        Path($"M {r.X + c},{r.Y} L {r.Right},{r.Y} L {r.Right},{r.Bottom - c} L {r.Right - c},{r.Bottom} L {r.X},{r.Bottom} L {r.X},{r.Y + c} Z");

    private static void XboxArt(Dictionary<string, byte[]> files, Palette p)
    {
        Wallpaper(files, (g, w, h) =>
        {
            g.FillRectangle(Solid(p.Back), new Rect(0, 0, w, h));
            var core = new Point(w * 0.5, h * 0.83);
            Glow(g, core, w * 0.85, h * 0.36, Color.FromArgb(170, 70, 150, 0));
            Glow(g, core, w * 0.34, h * 0.13, Color.FromArgb(190, 150, 235, 20));
            // The wire cage around the core: tilted rings and struts.
            for (var i = 1; i <= 6; i++)
                g.DrawEllipse(null, Pen(Color.FromArgb((byte)(80 - i * 10), 155, 232, 12), 1), core, i * 42, i * 17);
            foreach (var tilt in new[] { -62.0, -24, 24, 62 })
                using (g.PushTransform(Matrix.CreateTranslation(-core.X, -core.Y) * Matrix.CreateRotation(tilt * Math.PI / 180) * Matrix.CreateTranslation(core.X, core.Y)))
                    g.DrawEllipse(null, Pen(Color.FromArgb(34, 180, 255, 60), 1), core, 190, 46);
            for (var y = 0.0; y < h; y += 4) // scan lines
                g.FillRectangle(Solid(Color.FromArgb(26, 0, 0, 0)), new Rect(0, y, w, 1));
        });

        BarArt(files, (g, r) =>
        {
            g.FillRectangle(Vertical((p.BarTop, 0), (p.BarBottom, 1)), r);
            g.FillRectangle(Solid(Color.FromArgb(60, 155, 232, 12)), new Rect(0, Bar - 3, 1, 3));
            g.FillRectangle(Solid(p.BarLine), new Rect(0, Bar - 1, 1, 1));
        });

        NavArt(files, (g, r) =>
        {
            g.FillRectangle(Vertical((p.NavTop, 0), (p.NavBottom, 1)), r);
            g.FillRectangle(Solid(p.NavLine), new Rect(0, 0, 1, 1));
        });
        NavButtons(files, (g, key, _, selected) =>
        {
            var panel = Cut(new Rect(7.5, 3.5, 57, 29), 9);
            g.DrawGeometry(selected ? Vertical((Hx("#D2FF5A"), 0), (Hx("#6CB000"), 1)) : Solid(Color.FromArgb(110, 22, 46, 6)),
                Pen(selected ? p.NavPillRim : Color.FromArgb(120, 111, 175, 34), 1), panel);
            FluentGlyph(g, selected ? p.NavSel : p.NavInk, FluentNav[key], selected, 36, 18, 22);
        });

        TransportArt(files, true, (g, size, hero, _, state) =>
        {
            var centre = new Point(size / 2, size / 2);
            if (hero)
            {
                // The green core: a glowing sphere with a lit rim.
                if (state != State.Disabled)
                    Glow(g, centre, 33, 33, Color.FromArgb((byte)(state == State.Down ? 255 : 190), 160, 245, 20));
                var sphere = state == State.Disabled ? Radial(Hx("#3F5C1A"), Hx("#152406"))
                    : state == State.Down ? Radial(Hx("#F8FFD8"), Hx("#7CC800")) : Radial(Hx("#E6FF9C"), Hx("#4E8C00"));
                g.DrawEllipse(sphere, Pen(p.OrbRim, 1.4), centre, 26, 26);
                g.DrawEllipse(null, Pen(Color.FromArgb(80, 240, 255, 200), 1), centre, 21.5, 21.5);
                return;
            }

            var r = new Rect(0, 0, size, size).Deflate(size == 52 ? 6 : 5);
            var panel = Cut(r, 10);
            var fill = state == State.Down ? Vertical((Hx("#B9F52E"), 0), (Hx("#4E8C00"), 1)) : Vertical((Hx("#1E3D08"), 0), (Hx("#0A1802"), 1));
            g.DrawGeometry(fill, Pen(state == State.Disabled ? Color.FromArgb(70, 155, 232, 12) : Hx("#79BE12"), 1.3), panel);
        }, (hero, _, state) => hero ? (state == State.Disabled ? Hx("#8FBF4A") : p.Glyph) : state == State.Down ? p.Glyph : Hx("#D2FF6A"), heroGlyph: 26, sideGlyph: 21, skipGlyph: 23);

        ToggleArt(files, true, Hx("#5F9A1E"), Hx("#D6FF6A"), (g, down) =>
        {
            if (down)
                g.DrawGeometry(Solid(Color.FromArgb(80, 155, 232, 12)), Pen(Hx("#79BE12"), 1), Cut(new Rect(4.5, 7.5, 35, 29), 8));
        });

        // Segmented meters: each 6 unit tile is a lit (or unlit) block and a gap.
        SliderArt(files, 9,
            (g, b) => g.FillRectangle(Solid(p.Track), new Rect(0, b.Y, 4, b.Height)),
            (g, b) => g.FillRectangle(Vertical((Hx("#E2FF86"), 0), (AcidGreen, 0.45), (Hx("#4E8C00"), 1)), new Rect(0, b.Y, 4, b.Height)),
            (g, hover) =>
            {
                Glow(g, new Point(15, 15), 14, 14, Color.FromArgb((byte)(hover ? 230 : 150), 170, 250, 30));
                g.DrawGeometry(Vertical((p.ThumbLight, 0), (p.ThumbDark, 1)), Pen(Hx("#F0FFC8"), 1), Geo("M 15,4 L 22,15 L 15,26 L 8,15 Z"));
            });
    }
}
