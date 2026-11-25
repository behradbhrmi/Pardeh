using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Pardeh.UI.Rendering;
using static Pardeh.UI.FullMode.LunaArt;

namespace Pardeh.UI.FullMode;

/// <summary>
/// Zune HD (black, huge lower-case type, magenta into orange) and Kin Two (a mosaic of bright squares, a tile behind the open
/// page, and a play button made of dots after the Kin "Spot").
/// </summary>
internal static partial class PhoneSkin
{
    // ---- Zune HD --------------------------------------------------------------------------------

    private static readonly Color ZunePink = Color.Parse("#F0187C");
    private static readonly Color ZuneOrange = Color.Parse("#F7681F");

    private static RetroLook ZuneLook() => new()
    {
        Palette = new Palette
        {
            Key = "zune-phone", Name = "Zune HD", Font = "Segoe UI", Flat = true, Fluent = true, Dark = true,
            BarTop = Hx("#000000"), BarBottom = Hx("#000000"), BarLine = Hx("#000000"), BarInk = "#FFFFFF", BarIcon = Hx("#FFFFFF"),
            Back = Hx("#000000"),
            NavTop = Hx("#000000"), NavBottom = Hx("#000000"), NavLine = Hx("#000000"), NavInk = Hx("#777777"), NavSel = Hx("#FFFFFF"),
            NavPill = Hx("#000000"), NavPillRim = Hx("#000000"), LabelInk = "#6E6E6E", LabelSel = "#FFFFFF",
            ArtistInk = "#F0568F", TimeInk = "#8C8C8C", StageGlow = Hx("#0C0C0C"),
            Track = Hx("#333333"), Fill = ZunePink, ThumbLight = Hx("#FFFFFF"), ThumbDark = ZunePink,
            OrbTop = Hx("#000000"), OrbBottom = Hx("#000000"), OrbRim = Hx("#FFFFFF"), Glyph = Hx("#FFFFFF"), Dim = Hx("#E8E8E8"), Accent = Hx("#F0457A"),
            ListInk = "#FFFFFF", ListPlaying = "#F85C8C", ListSelect = "#1C0A14",
        },
        Settings = new MobileTheme(true, Hx("#000000"), Hx("#171717"), Hx("#F0457A"), Art: "zune"),
        Art = ZuneArt,
        Lowercase = true,
        Title = (28, 2, false),
        TitleFont = "Segoe UI Light",
        NavText = (8, 19), // "now playing" must fit a 64 unit slot (a 320 wide view)
    };

    /// <summary>Giant faint type that runs off the edge of the screen, the signature of the Zune interface.</summary>
    private static void GiantType(DrawingContext g, string text, double x, double y, double size, Color color)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Parse("Segoe UI, Arial"), FontStyle.Normal, FontWeight.Bold), size, new SolidColorBrush(color));
        using (g.PushTextOptions(new TextOptions { TextRenderingMode = TextRenderingMode.Antialias }))
            g.DrawText(ft, new Point(x, y));
    }

    private static void ZuneArt(Dictionary<string, byte[]> files, Palette p)
    {
        Wallpaper(files, (g, w, h) =>
        {
            g.FillRectangle(Solid(Colors.Black), new Rect(0, 0, w, h));
            Glow(g, new Point(w * 1.02, h * 0.98), w * 0.8, h * 0.34, Color.FromArgb(120, 240, 90, 30));
            Glow(g, new Point(w * -0.05, h * 0.80), w * 0.75, h * 0.28, Color.FromArgb(95, 240, 24, 124));
            GiantType(g, "music", -14, h - 168, 158, Color.FromArgb(19, 255, 255, 255));
        });
        BarArt(files, (g, r) => g.FillRectangle(Solid(Colors.Black), r));

        // The pivot of words: no icons; a short gradient stroke marks the open page.
        NavArt(files, (g, r) => g.FillRectangle(Solid(Colors.Black), r));
        NavButtons(files, (g, _, _, selected) =>
        {
            if (selected)
                g.FillRectangle(Horizontal((ZunePink, 0), (ZuneOrange, 1)), new Rect(20, 6, 32, 3));
        });

        TransportArt(files, true, (g, size, hero, _, state) =>
        {
            var centre = new Point(size / 2, size / 2);
            var radius = hero ? 29.5 : size == 52 ? 20 : 17.5;
            var fill = state == State.Down ? Horizontal((ZunePink, 0), (ZuneOrange, 1)) : null;
            IBrush ring = state == State.Disabled ? Solid(Color.FromArgb(70, 255, 255, 255))
                : hero ? Horizontal((ZunePink, 0), (ZuneOrange, 1)) : Solid(Color.FromArgb(150, 255, 255, 255));
            g.DrawEllipse(fill, new Pen(ring, hero ? 2.6 : 1.4), centre, radius, radius);
        }, (_, _, _) => Colors.White, heroGlyph: 30, sideGlyph: 22, skipGlyph: 22);

        ToggleArt(files, true, Hx("#7C7C7C"), p.Accent);

        SliderArt(files, 3,
            (g, b) => g.FillRectangle(Solid(p.Track), b),
            (g, b) => g.FillRectangle(Solid(ZunePink), b),
            (g, hover) =>
            {
                g.DrawEllipse(Solid(Colors.Black), new Pen(Horizontal((ZunePink, 0), (ZuneOrange, 1)), 3), new Point(15, 15), 7.5, 7.5);
                g.DrawEllipse(Solid(Colors.White), null, new Point(15, 15), hover ? 4 : 2.5, hover ? 4 : 2.5);
            });
    }

    // ---- Kin Two --------------------------------------------------------------------------------

    private static readonly Color KinGreen = Color.Parse("#8DD01F");

    // One bright colour for each page, in navigation order.
    private static readonly string[] KinTiles = ["#7AB800", "#E6177E", "#F58220", "#0095DA", "#7B3FA0"];

    private static RetroLook KinLook() => new()
    {
        Palette = new Palette
        {
            Key = "kin-phone", Name = "Kin Two", Font = "Segoe UI", Flat = true, Fluent = true, Dark = true,
            BarTop = Hx("#0B0B0B"), BarBottom = Hx("#0B0B0B"), BarLine = Hx("#262626"), BarInk = "#FFFFFF", BarIcon = Hx("#FFFFFF"),
            Back = Hx("#121212"),
            NavTop = Hx("#0B0B0B"), NavBottom = Hx("#0B0B0B"), NavLine = Hx("#262626"), NavInk = Hx("#B4B4B4"), NavSel = Hx("#FFFFFF"),
            NavPill = Hx("#7AB800"), NavPillRim = Hx("#7AB800"), LabelInk = "#A8A8A8", LabelSel = "#FFFFFF",
            ArtistInk = "#A6E22E", TimeInk = "#C4C4C4", StageGlow = Hx("#181818"),
            Track = Hx("#383838"), Fill = KinGreen, ThumbLight = Hx("#FFFFFF"), ThumbDark = KinGreen,
            OrbTop = KinGreen, OrbBottom = KinGreen, OrbRim = KinGreen, Glyph = Hx("#101A02"), Dim = Hx("#EDEDED"), Accent = KinGreen,
            ListInk = "#FFFFFF", ListPlaying = "#A6E22E", ListSelect = "#22300A",
        },
        Settings = new MobileTheme(true, Hx("#121212"), Hx("#222222"), KinGreen),
        Art = KinArt,
        Lowercase = true,
        Title = (18, 11, true),
        // A row of the mosaic's squares at the right of the header.
        BarDecor = """<SUBVIEW id="svChips" left="282" top="20" width="68" height="12" backgroundImage="kin_chips.png"/>""",
        NowScript = "    svChips.left = w - 78;",
    };

    private static void KinArt(Dictionary<string, byte[]> files, Palette p)
    {
        Wallpaper(files, (g, w, h) =>
        {
            g.FillRectangle(Solid(p.Back), new Rect(0, 0, w, h));
            // The Loop: a mosaic of squares and double squares that thickens toward the bottom; dim, so text stays readable.
            const double Cell = 45;
            var random = new Random(2010); // fixed seed: the same picture every time
            var rows = (int)Math.Ceiling(h / Cell);
            var taken = new bool[8, rows];
            for (var y = 0; y < rows; y++)
            {
                for (var x = 0; x < 8; x++)
                {
                    var depth = (y + 1.0) / rows;
                    var big = x < 7 && y < rows - 1 && random.NextDouble() < 0.16;
                    var show = random.NextDouble() < depth * depth * 1.1;
                    var colour = Hx(KinTiles[random.Next(KinTiles.Length)]);
                    var alpha = (byte)(18 + random.Next(26) + 34 * depth);
                    if (taken[x, y] || !show)
                        continue;
                    var span = big && !taken[x + 1, y] ? 2 : 1;
                    for (var dx = 0; dx < span; dx++)
                        for (var dy = 0; dy < span; dy++)
                            taken[x + dx, y + dy] = true;
                    g.FillRectangle(Solid(WithAlpha(colour, alpha)), new Rect(x * Cell + 1, y * Cell + 1, span * Cell - 2, span * Cell - 2));
                }
            }
        });
        BarArt(files, (g, r) =>
        {
            g.FillRectangle(Solid(p.BarTop), r);
            g.FillRectangle(Solid(p.BarLine), new Rect(0, Bar - 1, 1, 1));
        });
        files["kin_chips.png"] = Png(68, 12, g =>
        {
            for (var i = 0; i < KinTiles.Length; i++)
                g.FillRectangle(Solid(Hx(KinTiles[i])), new Rect(i * 14, 0, 11, 11));
        });

        NavArt(files, (g, r) =>
        {
            g.FillRectangle(Solid(p.NavTop), r);
            g.FillRectangle(Solid(p.NavLine), new Rect(0, 0, 1, 1));
        });
        NavButtons(files, (g, key, index, selected) =>
        {
            if (selected)
                g.FillRectangle(Solid(Hx(KinTiles[index])), new Rect(4, 0, 64, 52));
            else
                g.FillRectangle(Solid(Hx(KinTiles[index])), new Rect(28, 1, 16, 3));
            FluentGlyph(g, selected ? Colors.White : p.NavInk, FluentNav[key], selected, 36, 20, 23);
        });

        TransportArt(files, true, (g, size, hero, glyph, state) =>
        {
            if (hero)
            {
                // The Spot: a ring of dots around a solid disc.
                var centre = new Point(32, 32);
                for (var i = 0; i < 20; i++)
                {
                    var angle = i * Math.PI / 10;
                    g.DrawEllipse(Solid(WithAlpha(Colors.White, (byte)(state == State.Down ? 255 : 150))), null,
                        new Point(32 + 29.5 * Math.Cos(angle), 32 + 29.5 * Math.Sin(angle)), 1.7, 1.7);
                }

                var disc = state switch { State.Down => Hx("#C3F25A"), State.Disabled => Hx("#4C6420"), _ => KinGreen };
                g.DrawEllipse(Solid(disc), null, centre, 24.5, 24.5);
                return;
            }

            var tile = Hx(glyph switch { "previous" => KinTiles[1], "next" => KinTiles[3], "back10" => KinTiles[2], _ => KinTiles[4] });
            var inset = size == 52 ? 6.0 : 6;
            var colour = state switch { State.Down => Colors.White, State.Disabled => WithAlpha(tile, 90), _ => tile };
            g.FillRectangle(Solid(colour), new Rect(inset, inset, size - 2 * inset, size - 2 * inset));
        }, (hero, glyph, state) => hero ? p.Glyph : state == State.Down ? Hx("#181818") : Colors.White, heroGlyph: 26, sideGlyph: 23, skipGlyph: 24);

        ToggleArt(files, true, Hx("#CFCFCF"), KinGreen);

        SliderArt(files, 6,
            (g, b) => g.FillRectangle(Solid(p.Track), b),
            (g, b) => g.FillRectangle(Solid(KinGreen), b),
            (g, hover) =>
            {
                g.DrawEllipse(Solid(Colors.White), null, new Point(15, 15), hover ? 11.5 : 10.5, hover ? 11.5 : 10.5);
                g.DrawEllipse(Solid(KinGreen), null, new Point(15, 15), 5, 5);
            });
    }
}
