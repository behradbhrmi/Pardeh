using Avalonia;
using Avalonia.Media;
using Pardeh.UI.Rendering;
using static Pardeh.UI.FullMode.LunaArt;

namespace Pardeh.UI.FullMode;

/// <summary>Windows Mobile 6 (the green "Today" screen and its soft-key bar) and Windows Phone 7 (black, type, one accent).</summary>
internal static partial class PhoneSkin
{
    // ---- Windows Mobile 6 -----------------------------------------------------------------------

    private static RetroLook WindowsMobileLook() => new()
    {
        Palette = new Palette
        {
            Key = "wm6-phone", Name = "Windows Mobile 6",
            BarTop = Hx("#2C8A2A"), BarBottom = Hx("#0A4F10"), BarLine = Hx("#8FD27A"), BarInk = "#FFFFFF", BarIcon = Hx("#FFFFFF"),
            Back = Hx("#0B5214"),
            NavTop = Hx("#3F9A32"), NavBottom = Hx("#0C4A10"), NavLine = Hx("#A5E08E"), NavInk = Hx("#E4F6DC"), NavSel = Hx("#FFFFFF"),
            NavPill = Hx("#7CC862"), NavPillRim = Hx("#E9FFE0"), LabelInk = "#E4F6DC", LabelSel = "#FFFFFF",
            ArtistInk = "#CDEBC2", TimeInk = "#E4F6DC", StageGlow = Hx("#083D0E"),
            Track = Hx("#EAF5E6"), Fill = Hx("#6CC63C"), ThumbLight = Hx("#FFFFFF"), ThumbDark = Hx("#BFCABD"),
            OrbTop = Hx("#FFFFFF"), OrbBottom = Hx("#B9C2BA"), OrbRim = Hx("#5F6B62"), Glyph = Hx("#17501A"), Dim = Hx("#E4F6DC"), Accent = Hx("#C9F56A"),
            ListInk = "#FFFFFF", ListPlaying = "#E8FF8C", ListSelect = "#2C8F2B",
        },
        Settings = new MobileTheme(false, Hx("#EFF6EC"), Hx("#FCFEFB"), Hx("#2E7D1F"), Hx("#3F9A32"), Hx("#14601A"), Colors.White),
        Art = WindowsMobileArt,
        Title = (11, 16, true),
        // The status icons of the Start bar (signal, sound, battery), on the right.
        BarDecor = """<SUBVIEW id="svStatus" left="262" top="17" width="88" height="18" backgroundImage="wm6_status.png"/>""",
        // The thin rules that separate the rows of the Today screen, above and below the title.
        NowDecor = """
            <SUBVIEW id="ruleTop" left="0" top="384" width="360" height="1" backgroundColor="#6DB95C"/>
            <SUBVIEW id="ruleBottom" left="0" top="434" width="360" height="1" backgroundColor="#6DB95C"/>
            """,
        NowScript = """
                svStatus.left = w - 96;
                var land = w > h * 1.15;
                var x = land ? svStage.left + svStage.width + 6 : 0;
                ruleTop.left = x; ruleBottom.left = x; ruleTop.width = w - x; ruleBottom.width = w - x;
                ruleTop.top = tTitle.top - 4; ruleBottom.top = tTitle.top + 45;
            """,
    };

    private static void WindowsMobileArt(Dictionary<string, byte[]> files, Palette p)
    {
        Wallpaper(files, (g, w, h) =>
        {
            g.FillRectangle(Vertical((Hx("#0A5416"), 0), (Hx("#0D6118"), 0.45), (Hx("#07400F"), 1)), new Rect(0, 0, w, h));
            Glow(g, new Point(w * 0.78, h * 0.72), w * 0.95, h * 0.42, Color.FromArgb(230, 104, 190, 40));
            Glow(g, new Point(w * 0.05, h * 0.98), w * 0.7, h * 0.22, Color.FromArgb(120, 40, 140, 30));
            // The swoosh: two long soft arcs sweeping up from the lower left.
            var wide = Path($"M {-20},{h * 0.93} C {w * 0.35},{h * 0.60} {w * 0.52},{h * 0.52} {w + 20},{h * 0.47} L {w + 20},{h * 0.53} C {w * 0.58},{h * 0.60} {w * 0.42},{h * 0.70} {-20},{h * 0.99} Z");
            g.DrawGeometry(Horizontal((Color.FromArgb(0, 255, 255, 255), 0), (Color.FromArgb(46, 220, 255, 190), 0.6), (Color.FromArgb(12, 255, 255, 255), 1)), null, wide);
            g.DrawGeometry(null, Pen(Color.FromArgb(52, 230, 255, 200), 1.4), Path($"M {w * 0.62},{h + 20} C {w * 0.55},{h * 0.72} {w * 0.72},{h * 0.56} {w + 20},{h * 0.52}"));
            g.DrawGeometry(null, Pen(Color.FromArgb(30, 230, 255, 200), 1), Path($"M {w * 0.74},{h + 20} C {w * 0.68},{h * 0.80} {w * 0.82},{h * 0.64} {w + 20},{h * 0.60}"));
        });

        BarArt(files, (g, r) =>
        {
            g.FillRectangle(Vertical((Hx("#3C9A38"), 0), (Hx("#1D7222"), 0.48), (Hx("#0E5A15"), 0.52), (Hx("#0A4A10"), 1)), r);
            g.FillRectangle(Solid(Color.FromArgb(110, 255, 255, 255)), new Rect(0, 1, 1, 1));
            g.FillRectangle(Solid(Hx("#7BC568")), new Rect(0, Bar - 1, 1, 1));
        });

        files["wm6_status.png"] = Png(88, 18, g =>
        {
            var white = Solid(Colors.White);
            for (var i = 0; i < 4; i++) // signal
                g.FillRectangle(white, new Rect(2 + i * 4.5, 13 - i * 3.2, 3, 2 + i * 3.2));
            g.DrawGeometry(white, null, Geo("M 29,6.5 L 32.5,6.5 L 37,2.5 L 37,15.5 L 32.5,11.5 L 29,11.5 Z"));
            var wave = new Pen(white, 1.4, lineCap: PenLineCap.Round);
            g.DrawGeometry(null, wave, Geo("M 40,6 Q 42.5,9 40,12 M 43,4 Q 47.5,9 43,14"));
            g.DrawRectangle(null, new Pen(white, 1.2), new Rect(57.5, 4.5, 24, 10));
            g.FillRectangle(white, new Rect(82, 7, 2.5, 5));
            for (var i = 0; i < 3; i++)
                g.FillRectangle(Solid(Hx("#D8FF9A")), new Rect(60 + i * 6.8, 7, 5.4, 5));
        });

        // The soft-key bar: glossy green, with a split highlight like the "Phone | Contacts" bar.
        NavArt(files, (g, r) =>
        {
            g.FillRectangle(Vertical((Hx("#8FD27A"), 0), (Hx("#3F9A32"), 0.42), (Hx("#1C741C"), 0.5), (Hx("#0B4A0F"), 1)), r);
            g.FillRectangle(Solid(Hx("#C9F0B8")), new Rect(0, 0, 1, 1));
        });
        NavButtons(files, (g, key, _, selected) =>
        {
            if (selected)
                Gel(g, new Rect(4.5, 1.5, 63, 48), 5, Color.FromArgb(150, 200, 245, 150), Color.FromArgb(170, 40, 130, 30), Hx("#E9FFE0"), 90);
            PictorialIcons.Draw(g, VintageNav[key], new Rect(23, 5, 26, 26), shadow: false);
        });

        // The silver hardware keys under the screen: rounded squares like a d-pad cluster.
        TransportArt(files, false, (g, size, hero, _, state) =>
        {
            var r = new Rect(2, 2, size - 4, size - 4);
            if (!hero && size == SkipSize)
                r = r.Deflate(3);
            var radius = hero ? 15.0 : 11;
            g.DrawRectangle(Solid(Color.FromArgb(80, 0, 25, 0)), null, new RoundedRect(r.Translate(new Vector(0, 1.5)), radius));
            var (top, bottom) = state switch
            {
                State.Down => (Hx("#A9B5AA"), Hx("#E2E8E2")),
                State.Disabled => (Hx("#C3CCC3"), Hx("#9FAAA0")),
                _ => (Hx("#FFFFFF"), Hx("#AEB9AF")),
            };
            Gel(g, r, radius, top, bottom, Hx("#4F5D52"), 150, 1.2);
            if (hero)
                g.DrawRectangle(null, Pen(Hx("#7FBF4A"), 1.6), new RoundedRect(r.Deflate(5), 10));
        }, (_, _, _) => p.Glyph, heroGlyph: 28, sideGlyph: 22, skipGlyph: 25);

        ToggleArt(files, false, Hx("#E4F6DC"), Hx("#F1FFC4"), (g, down) =>
        {
            if (down)
                Gel(g, new Rect(4, 6, 36, 32), 5, Color.FromArgb(180, 170, 230, 110), Color.FromArgb(200, 40, 130, 30), Hx("#E9FFE0"), 80);
        });

        SliderArt(files, 8,
            (g, b) =>
            {
                g.FillRectangle(Vertical((Hx("#BCCBB8"), 0), (Hx("#FFFFFF"), 1)), b);
                g.FillRectangle(Solid(Hx("#052F09")), new Rect(0, b.Y - 1, 6, 1));
                g.FillRectangle(Solid(Hx("#052F09")), new Rect(0, b.Bottom, 6, 1));
            },
            (g, b) => g.FillRectangle(Vertical((Hx("#C8F56E"), 0), (Hx("#6CC63C"), 0.5), (Hx("#2F9A1E"), 1)), b),
            (g, hover) => Gel(g, new Rect(8.5, 3.5, 13, 23), 4, hover ? Hx("#F4FFE8") : Hx("#FFFFFF"), Hx("#A5B1A6"), Hx("#34452F"), 140));
    }

    // ---- Windows Phone 7 ------------------------------------------------------------------------

    private static readonly Color MetroAccent = Color.Parse("#1BA1E2");

    private static RetroLook MetroLook() => new()
    {
        Palette = new Palette
        {
            Key = "metro-phone", Name = "Windows Phone 7", Font = "Segoe UI", Flat = true, Fluent = true, Dark = true,
            BarTop = Hx("#000000"), BarBottom = Hx("#000000"), BarLine = Hx("#000000"), BarInk = "#FFFFFF", BarIcon = Hx("#FFFFFF"),
            Back = Hx("#000000"),
            NavTop = Hx("#1F1F1F"), NavBottom = Hx("#1F1F1F"), NavLine = Hx("#1F1F1F"), NavInk = Hx("#E6E6E6"), NavSel = MetroAccent,
            NavPill = Hx("#1F1F1F"), NavPillRim = Hx("#1F1F1F"), LabelInk = "#C8C8C8", LabelSel = "#1BA1E2",
            ArtistInk = "#999999", TimeInk = "#999999", StageGlow = Hx("#101010"),
            Track = Hx("#3A3A3A"), Fill = MetroAccent, ThumbLight = Hx("#FFFFFF"), ThumbDark = Hx("#FFFFFF"),
            OrbTop = Hx("#000000"), OrbBottom = Hx("#000000"), OrbRim = Hx("#FFFFFF"), Glyph = Hx("#FFFFFF"), Dim = Hx("#FFFFFF"), Accent = MetroAccent,
            ListInk = "#FFFFFF", ListPlaying = "#1BA1E2", ListSelect = "#161616",
        },
        Settings = new MobileTheme(true, Hx("#000000"), Hx("#1F1F1F"), MetroAccent, Art: "metro"),
        Art = MetroArt,
        Lowercase = true,
        Title = (24, 5, false),
        TitleFont = "Segoe UI Light",
        NavText = (7, 37),
    };

    private static void MetroArt(Dictionary<string, byte[]> files, Palette p)
    {
        Wallpaper(files, (g, w, h) => g.FillRectangle(Solid(Colors.Black), new Rect(0, 0, w, h)));
        BarArt(files, (g, r) => g.FillRectangle(Solid(Colors.Black), r));

        // The application bar: round outlined buttons; the open page's circle is filled with the accent.
        NavArt(files, (g, r) => g.FillRectangle(Solid(p.NavTop), r));
        NavButtons(files, (g, key, _, selected) =>
        {
            var centre = new Point(36, 18);
            g.DrawEllipse(selected ? Solid(MetroAccent) : null, new Pen(Solid(selected ? MetroAccent : Colors.White), 2), centre, 15, 15);
            FluentGlyph(g, Colors.White, FluentNav[key], selected, 36, 18, 19);
        });

        TransportArt(files, true, (g, size, hero, _, state) =>
        {
            var centre = new Point(size / 2, size / 2);
            var radius = hero ? 29.0 : size == 52 ? 21 : 18;
            var ring = state == State.Disabled ? Color.FromArgb(90, 255, 255, 255) : Colors.White;
            g.DrawEllipse(state == State.Down ? Solid(MetroAccent) : null, new Pen(Solid(ring), hero ? 3 : 2.2), centre, radius, radius);
        }, (_, _, _) => Colors.White, heroGlyph: 30, sideGlyph: 22, skipGlyph: 22);

        ToggleArt(files, true, Hx("#8A8A8A"), MetroAccent);

        SliderArt(files, 4,
            (g, b) => g.FillRectangle(Solid(p.Track), b),
            (g, b) => g.FillRectangle(Solid(MetroAccent), b),
            (g, hover) => g.FillRectangle(Solid(hover ? MetroAccent : Colors.White), new Rect(11, 5, 8, 20)));
    }
}
