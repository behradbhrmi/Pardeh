using Avalonia;
using Avalonia.Media;
using Pardeh.UI.Rendering;
using static Pardeh.UI.FullMode.LunaArt;

namespace Pardeh.UI.FullMode;

/// <summary>
/// The Sony Ericsson Walkman phones of the late 2000s: glossy black, orange light that swirls across the screen, a chrome
/// ringed orange play key over dark keys, and an icon bar whose open item lights up.
/// </summary>
internal static partial class PhoneSkin
{
    private static readonly Color WalkOrange = Color.Parse("#FF7A00");

    private static RetroLook WalkmanLook() => new()
    {
        Palette = new Palette
        {
            Key = "walkman-phone", Name = "Walkman Orange", Font = "Arial", Flat = true, Fluent = true, Dark = true,
            BarTop = Hx("#3A3A3E"), BarBottom = Hx("#050506"), BarLine = Hx("#FF7A00"), BarInk = "#FFFFFF", BarIcon = Hx("#FFFFFF"),
            Back = Hx("#08070A"),
            NavTop = Hx("#303034"), NavBottom = Hx("#040405"), NavLine = Hx("#55555A"), NavInk = Hx("#B9B9C0"), NavSel = Hx("#FF9A2E"),
            NavPill = Hx("#4A2406"), NavPillRim = WalkOrange, LabelInk = "#A9A9B0", LabelSel = "#FF9A2E",
            ArtistInk = "#FFA552", TimeInk = "#DADADF", StageGlow = Hx("#101013"),
            Track = Hx("#3A3A40"), Fill = WalkOrange, ThumbLight = Hx("#FFFFFF"), ThumbDark = WalkOrange,
            OrbTop = Hx("#FFB347"), OrbBottom = Hx("#E14E00"), OrbRim = Hx("#D8D8DE"), Glyph = Hx("#FFFFFF"), Dim = Hx("#DADADF"), Accent = Hx("#FF8C1A"),
            ListInk = "#FFFFFF", ListPlaying = "#FF9A2E", ListSelect = "#3A1C04",
        },
        Settings = new MobileTheme(true, Hx("#0B0A0D"), Hx("#1D1C21"), WalkOrange),
        Art = WalkmanArt,
        Title = (12, 15, true),
    };

    private static void WalkmanArt(Dictionary<string, byte[]> files, Palette p)
    {
        Wallpaper(files, (g, w, h) =>
        {
            g.FillRectangle(Vertical((Hx("#0A090C"), 0), (Hx("#0B080A"), 0.55), (Hx("#1A0804"), 1)), new Rect(0, 0, w, h));
            Glow(g, new Point(w * 0.92, h * 0.80), w * 0.8, h * 0.30, Color.FromArgb(165, 226, 60, 0));
            Glow(g, new Point(w * 0.16, h * 1.0), w * 0.7, h * 0.22, Color.FromArgb(130, 170, 10, 20));

            // The swirl: ribbons of light, each one a little lower, thinner and redder than the last.
            for (var i = 0; i < 9; i++)
            {
                var drop = i * 9.0;
                var ribbon = Path($"M {-30},{h * 0.99 - drop * 0.3} C {w * 0.30},{h * 0.66 + drop} {w * 0.62},{h * 0.98 - drop * 1.6} {w + 30},{h * 0.58 + drop * 1.4}");
                var colour = Color.FromRgb(255, (byte)(165 - i * 15), 0);
                g.DrawGeometry(null, Pen(WithAlpha(colour, (byte)(34 - i * 2)), 7), ribbon);
                g.DrawGeometry(null, Pen(WithAlpha(colour, (byte)(150 - i * 13)), i == 0 ? 2 : 1), ribbon);
            }
        });

        BarArt(files, (g, r) =>
        {
            g.FillRectangle(Vertical((Hx("#55555B"), 0), (Hx("#27272B"), 0.49), (Hx("#0C0C0E"), 0.51), (Hx("#040405"), 1)), r);
            g.FillRectangle(Solid(Color.FromArgb(90, 255, 122, 0)), new Rect(0, Bar - 3, 1, 1));
            g.FillRectangle(Solid(WalkOrange), new Rect(0, Bar - 2, 1, 2));
        });

        NavArt(files, (g, r) =>
        {
            g.FillRectangle(Vertical((Hx("#3C3C42"), 0), (Hx("#1A1A1D"), 0.45), (Hx("#070708"), 0.5), (Hx("#030304"), 1)), r);
            g.FillRectangle(Solid(p.NavLine), new Rect(0, 0, 1, 1));
        });
        NavButtons(files, (g, key, _, selected) =>
        {
            if (selected)
            {
                using (g.PushClip(new Rect(0, 0, 72, 52)))
                    Glow(g, new Point(36, 2), 38, 30, Color.FromArgb(200, 255, 122, 0));
                g.FillRectangle(Horizontal((Color.FromArgb(0, 255, 170, 60), 0), (Hx("#FFC27A"), 0.5), (Color.FromArgb(0, 255, 170, 60), 1)), new Rect(8, 0, 56, 2));
            }

            FluentGlyph(g, selected ? Colors.White : p.NavInk, FluentNav[key], selected, 36, 19, 24);
        });

        TransportArt(files, true, (g, size, hero, _, state) =>
        {
            var centre = new Point(size / 2, size / 2);
            if (hero)
            {
                // A chrome ring around a glossy orange key.
                if (state != State.Disabled)
                    Glow(g, centre, 32, 32, Color.FromArgb((byte)(state == State.Down ? 230 : 120), 255, 122, 0));
                g.DrawEllipse(Vertical((Hx("#FFFFFF"), 0), (Hx("#8E8E96"), 0.5), (Hx("#D9D9E0"), 1)), Pen(Hx("#35353A"), 1), centre, 29, 29);
                var face = state switch
                {
                    State.Down => Vertical((Hx("#C84400"), 0), (Hx("#FF9A2E"), 1)),
                    State.Disabled => Vertical((Hx("#8A6A52"), 0), (Hx("#5A3F2E"), 1)),
                    _ => Vertical((Hx("#FFB347"), 0), (Hx("#FF7A00"), 0.5), (Hx("#D94800"), 1)),
                };
                g.DrawEllipse(face, Pen(Hx("#7A2E00"), 1), centre, 24, 24);
                g.DrawEllipse(Vertical((Color.FromArgb(150, 255, 255, 255), 0), (Color.FromArgb(10, 255, 255, 255), 1)), null, new Point(32, 21), 17, 10);
                return;
            }

            var r = new Rect(0, 0, size, size).Deflate(size == 52 ? 5 : 4);
            var down = state == State.Down;
            Gel(g, r, r.Height / 2, down ? Hx("#7A3600") : Hx("#4A4A52"), down ? Hx("#2A1200") : Hx("#0C0C0F"), down ? WalkOrange : Hx("#8C8C96"), 60, 1.2);
        }, (_, _, _) => Colors.White, heroGlyph: 28, sideGlyph: 22, skipGlyph: 23);

        ToggleArt(files, true, Hx("#C4C4CC"), Hx("#FF9A2E"), (g, down) =>
        {
            if (down)
                Glow(g, new Point(22, 22), 21, 21, Color.FromArgb(140, 255, 122, 0));
        });

        SliderArt(files, 6,
            (g, b) =>
            {
                g.FillRectangle(Solid(p.Track), b);
                g.FillRectangle(Solid(Color.FromArgb(150, 0, 0, 0)), new Rect(0, b.Y, 6, 1));
            },
            (g, b) => g.FillRectangle(Vertical((Hx("#FFC774"), 0), (WalkOrange, 0.5), (Hx("#C23E00"), 1)), b),
            (g, hover) =>
            {
                var centre = new Point(15, 15);
                if (hover)
                    Glow(g, centre, 15, 15, Color.FromArgb(200, 255, 140, 20));
                g.DrawEllipse(Vertical((Hx("#FFFFFF"), 0), (Hx("#92929A"), 1)), Pen(Hx("#2E2E33"), 1), centre, 10.5, 10.5);
                g.DrawEllipse(Solid(WalkOrange), null, centre, 4.5, 4.5);
            });
    }
}
