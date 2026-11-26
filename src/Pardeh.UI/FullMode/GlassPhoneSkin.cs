using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Pardeh.Skinning.Model;
using Pardeh.Skinning.Wmp;
using static Pardeh.UI.FullMode.LunaArt;

namespace Pardeh.UI.FullMode;

/// <summary>
/// "Pardeh Glass" (key glass-phone): the phone full mode drawn after a glossy royal-blue handheld design. A rounded blue
/// window inset on a near-black screen: a title bar (Pardeh logo, title, three round glass buttons: background, full
/// screen, close), a tab band (the selected tab is a bright glossy light-blue tab with its icon), a deep blue body with
/// diagonal light sweeps, a cover tile (drawn Pardeh artwork when there is no picture), title / artist / album / stars,
/// a seek row, one wide translucent glass transport pill around a glowing play / pause orb, a Playlist card (the real
/// playlist) and a bottom bar of glass pills. Video swaps the info block for the picture and the bottom row for repeat,
/// subtitles, aspect and full screen; the visualization takes the picture's place. All art is drawn in code and original.
/// </summary>
internal static partial class PhoneSkin
{
    private const string GlassKey = "glass-phone";
    private const int GBar = 42, GTabs = 33, GFoot = 46, GCorner = 16, GHead = 28, GPillH = 68, GToggleW = 36;
    // Computed, not stored: they follow the skin colour (GlassPalette) of the build in progress.
    private static Color GDark => C("#040A1E");
    private static Color GRim => C("#6FA9F5");
    private static Color GInk => C("#FFFFFF");
    private static Color GNavy => C("#0D2B7E");
    private static Color GCardBody => C("#0A2569");

    /// <summary>
    /// Builds Pardeh Glass in the colour of <see cref="Pardeh.UI.Rendering.GlassPalette.Current"/>: every colour the art code writes with <c>C("#...")</c>
    /// and every "#RRGGBB" of the layout goes through the palette (a no-op in the default Royal blue, so that build is byte-identical to before).
    /// The flag / Pardeh logo, the album art, the gold stars (hue outside the blue band) and white / grey text keep their colours.
    /// </summary>
    private static SkinPackage BuildGlass()
    {
        _glassBuild = true;
        try
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            AddGlassArt(files);
            files["phone.wms"] = Encoding.UTF8.GetBytes(Pardeh.UI.Rendering.GlassPalette.MapText(GlassLayout()));
            files["phone.js"] = Encoding.UTF8.GetBytes(Pardeh.UI.Rendering.GlassPalette.MapText(GlassScript));
            return WmzLoader.FromFiles(files, SourcePathOf(GlassKey));
        }
        finally
        {
            _glassBuild = false;
        }
    }

    // ---- Art ---------------------------------------------------------------------------------

    /// <summary>Draws a stretchable part once at <paramref name="width"/> and cuts a left cap, a one-pixel middle and a right cap from it, so the three always match.</summary>
    private static void Slices(Dictionary<string, byte[]> files, string name, int width, int height, int cap, Action<DrawingContext> draw)
    {
        var pixels = Render(width, height, draw);
        files[$"{name}_l.png"] = Png(pixels, width * 4, 0, 0, cap, height);
        files[$"{name}_m.png"] = Png(pixels, width * 4, width / 2, 0, 1, height);
        files[$"{name}_r.png"] = Png(pixels, width * 4, width - cap, 0, cap, height);
    }

    private static IBrush PillFace => Vertical((C("#C9E0FC"), 0), (C("#9DC4F6"), 0.4), (C("#79A6EB"), 0.75), (C("#5F90E0"), 1));

    /// <summary>A dark glass pill button (bottom bar): navy face, light rim, gloss on the upper half.</summary>
    private static void GlassPlate(DrawingContext g, Rect r, double radius, bool pressed)
    {
        g.DrawRectangle(Solid(WithAlpha(C("#020B38"), 120)), null, new RoundedRect(new Rect(r.X, r.Y + 1.5, r.Width, r.Height - 1), radius));
        g.DrawRectangle(pressed ? Vertical((C("#2A62D8"), 0), (C("#1646B6"), 0.5), (C("#0F338F"), 1)) : Vertical((C("#1D4FC2"), 0), (C("#0D3396"), 0.5), (C("#082675"), 1)),
            Pen(pressed ? C("#A3CCFF") : GRim, 1.3), new RoundedRect(new Rect(r.X + 0.7, r.Y + 0.7, r.Width - 1.4, r.Height - 3), radius));
        g.DrawRectangle(Vertical((WithAlpha(Colors.White, (byte)(pressed ? 70 : 60)), 0), (WithAlpha(Colors.White, 0), 1)),
            null, new RoundedRect(new Rect(r.X + 3, r.Y + 2.5, r.Width - 6, (r.Height - 3) * 0.45), Math.Max(2, radius - 4)));
    }

    /// <summary>A light glass disc (transport round buttons): pale blue face, white rim, navy outline and glyph.</summary>
    private static void LightDisc(DrawingContext g, double size, bool pressed, bool disabled, Action<Color> glyph)
    {
        var c = new Point(size / 2, size / 2);
        var r = size / 2 - 1.2;
        g.DrawEllipse(Solid(WithAlpha(C("#031655"), 90)), null, new Point(c.X, c.Y + 1.2), r, r);
        g.DrawEllipse(Vertical((C("#E6F1FF"), 0), (C("#A9C9F6"), 0.55), (C("#7BA5E8"), 1)), Pen(C("#1B3F9A"), 1.4), c, r, r);
        var face = r - 3.4;
        g.DrawEllipse(pressed ? Vertical((C("#7FA7E8"), 0), (C("#5C88D6"), 1)) : Vertical((C("#CFE3FD"), 0), (C("#9DC0F2"), 0.5), (C("#86AEEB"), 1)), Pen(C("#4F7ED0"), 0.9), c, face, face);
        g.DrawEllipse(Vertical((WithAlpha(Colors.White, 170), 0), (WithAlpha(Colors.White, 0), 1)), null, new Point(c.X, c.Y - face * 0.42), face * 0.78, face * 0.46);
        glyph(disabled ? WithAlpha(GNavy, 100) : GNavy);
    }

    /// <summary>The glowing blue play / pause orb: bright cyan halo and ring, deep royal body, white glyph.</summary>
    private static void OrbDisc(DrawingContext g, double size, bool pressed, bool disabled, Action<Color> glyph)
    {
        var c = new Point(size / 2, size / 2);
        var r = size / 2 - 4.5;
        g.DrawEllipse(Radial(WithAlpha(C("#5CC8FF"), 190), WithAlpha(C("#5CC8FF"), 0)), null, c, size / 2 - 0.3, size / 2 - 0.3);
        g.DrawEllipse(Solid(WithAlpha(C("#020B38"), 130)), null, new Point(c.X, c.Y + 1), r + 1.5, r + 1.5);
        g.DrawEllipse(pressed ? Vertical((C("#1B52D8"), 0), (C("#0A2AA0"), 1)) : Vertical((C("#2C7BF4"), 0), (C("#0E40CC"), 0.45), (C("#06238C"), 1)), Pen(C("#62CCFF"), 2.4), c, r, r);
        g.DrawEllipse(null, Pen(WithAlpha(C("#E6F7FF"), 150), 1), c, r - 2.6, r - 2.6);
        g.DrawEllipse(Vertical((WithAlpha(Colors.White, 110), 0), (WithAlpha(Colors.White, 0), 1)), null, new Point(c.X, c.Y - r * 0.45), r * 0.7, r * 0.42);
        glyph(disabled ? WithAlpha(Colors.White, 110) : C("#F4F8FF"));
    }

    private static void TitleButton(DrawingContext g, bool pressed, Action<Color> glyph)
    {
        using (g.PushTransform(Matrix.CreateScale(26.0 / 30, 26.0 / 30)))
        {
            var c = new Point(15, 15);
            g.DrawEllipse(Solid(WithAlpha(Colors.Black, 70)), null, new Point(15, 16), 14, 14);
            g.DrawEllipse(pressed ? Vertical((C("#3A70E0"), 0), (C("#1B48B8"), 1)) : Vertical((C("#1E4EBC"), 0), (C("#0A2472"), 1)), Pen(C("#7DB7FB"), 1.8), c, 13, 13);
            g.DrawEllipse(null, Pen(WithAlpha(C("#031450"), 200), 1), c, 11.2, 11.2);
            g.DrawEllipse(Vertical((WithAlpha(Colors.White, 100), 0), (WithAlpha(Colors.White, 0), 1)), null, new Point(15, 9.5), 9, 5);
            glyph(C("#EAF4FF"));
        }
    }

    private static string StarPath(double cx, double cy, double outer, double inner)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < 10; i++)
        {
            var a = -Math.PI / 2 + i * Math.PI / 5;
            var rad = i % 2 == 0 ? outer : inner;
            sb.Append(CultureInfo.InvariantCulture, $"{(i == 0 ? "M" : "L")} {cx + rad * Math.Cos(a):0.##},{cy + rad * Math.Sin(a):0.##} ");
        }

        return sb.Append('Z').ToString();
    }

    private static void SpeakerGlyph(DrawingContext g, Color c, double cx, double cy, double scale, bool muted, bool low = false)
    {
        FillIcon(g, c, "M 3.5,8 L 6.5,8 L 10,5 L 10,15 L 6.5,12 L 3.5,12 Z", cx, cy, scale);
        StrokeIcon(g, c, muted ? "M 12.5,7.5 L 17,12.5 M 17,7.5 L 12.5,12.5" : low ? "M 12.6,7.6 Q 14.4,10 12.6,12.4" : "M 12.6,7.6 Q 14.4,10 12.6,12.4 M 14.8,5.8 Q 17.6,10 14.8,14.2", cx, cy, scale, 1.5);
    }

    /// <summary>A soft diagonal light band (stacked, ever thinner layers give it feathered edges).</summary>
    private static void LightBand(DrawingContext g, double y, double drop, double thickness, byte alpha)
    {
        const int Layers = 9;
        for (var i = 0; i < Layers; i++)
        {
            var t = thickness * (1 - i / (double)Layers) / 2;
            var geo = Geo(FormattableString.Invariant(
                $"M 520,{y - t} C 380,{y + drop * 0.32 - t} 190,{y + drop * 0.7 - t} -20,{y + drop - t} L -20,{y + drop + t} C 190,{y + drop * 0.7 + t} 380,{y + drop * 0.32 + t} 520,{y + t} Z"));
            g.DrawGeometry(Solid(WithAlpha(Colors.White, alpha)), null, geo);
        }
    }

    private static void AddGlassArt(Dictionary<string, byte[]> files)
    {
        // Title bar, tab band, body.
        files["bar.png"] = Png(1, GBar, g =>
        {
            g.FillRectangle(Vertical((C("#4F93F5"), 0), (C("#2E6BE0"), 0.46), (C("#2161D3"), 0.5), (C("#163FAE"), 1)), new Rect(0, 0, 1, GBar));
            g.FillRectangle(Solid(WithAlpha(Colors.White, 150)), new Rect(0, 1, 1, 1));
        });
        files["tabs.png"] = Png(1, GTabs, g =>
        {
            g.FillRectangle(Vertical((C("#0F3799"), 0), (C("#0B2D88"), 1)), new Rect(0, 0, 1, GTabs));
            g.FillRectangle(Solid(C("#071E66")), new Rect(0, 0, 1, 1));
            g.FillRectangle(Solid(WithAlpha(C("#8CC0FF"), 150)), new Rect(0, GTabs - 1, 1, 1));
        });
        files["body.png"] = Png(520, 1100, g =>
        {
            g.FillRectangle(Vertical((C("#1D56CB"), 0), (C("#1342B3"), 0.28), (C("#0E3599"), 0.6), (C("#0A2A82"), 1)), new Rect(0, 0, 520, 1100));
            g.DrawEllipse(Radial(WithAlpha(C("#8FC8FF"), 90), WithAlpha(C("#8FC8FF"), 0)), null, new Point(130, 110), 260, 190);
            LightBand(g, 70, 400, 150, 7);
            LightBand(g, 380, 380, 130, 5);
            LightBand(g, 700, 360, 120, 4);
        }, 1); // soft gradients gain nothing from density; 1x keeps this big picture (520 x 1100) at 2 MB instead of 20
        files["brand_hit.png"] = Png(150, GBar, g => g.FillRectangle(Solid(Color.FromArgb(2, 0, 0, 0)), new Rect(0, 0, 150, GBar)), 1); // tap target for the app menu
        var classic = AppSettings.Current.ClassicBranding;
        files["logo.png"] = Png(30, 30, g => { if (classic) ClassicFlag.Draw(g, new Rect(0, 0, 30, 30)); else PardehLogo.Draw(g, new Rect(0.5, 0.5, 29, 29)); });

        // Window corners: the near-black screen outside the rounded window, with the window's light rim along the arc.
        for (var corner = 0; corner < 4; corner++)
        {
            var (flipX, flipY) = (corner % 2 == 1, corner >= 2);
            files[$"corner{corner}.png"] = Png(GCorner, GCorner, g =>
            {
                var m = Matrix.CreateScale(flipX ? -1 : 1, flipY ? -1 : 1) * Matrix.CreateTranslation(flipX ? GCorner : 0, flipY ? GCorner : 0);
                using (g.PushTransform(m))
                {
                    g.DrawGeometry(Solid(GDark), null, Geo(FormattableString.Invariant($"M -1,-1 L {GCorner},-1 L {GCorner},0 A {GCorner},{GCorner} 0 0 0 0,{GCorner} L -1,{GCorner} Z")));
                    g.DrawGeometry(null, Pen(C("#6AA6F2"), 1.5), Geo(FormattableString.Invariant($"M {GCorner},0.75 A {GCorner - 0.75},{GCorner - 0.75} 0 0 0 0.75,{GCorner}")));
                }
            });
        }

        // Title bar buttons.
        foreach (var (suffix, down) in new[] { ("", false), ("_down", true) })
        {
            files[$"tb_min{suffix}.png"] = Png(26, 26, g => TitleButton(g, down, c => StrokeIcon(g, c, "M 5.5,10 L 14.5,10", 15 * 26.0 / 30, 15 * 26.0 / 30, 0.87, 2.5)));
            files[$"tb_max{suffix}.png"] = Png(26, 26, g => TitleButton(g, down, c => g.DrawRectangle(null, Pen(c, 2.3), new RoundedRect(new Rect(9.5, 9.5, 11, 11), 2))));
            files[$"tb_close{suffix}.png"] = Png(26, 26, g => TitleButton(g, down, c => StrokeIcon(g, c, "M 6,6 L 14,14 M 14,6 L 6,14", 15, 15, 1.0, 2.4)));
        }

        // Tabs: the selected tab's glossy plate, and a content picture (icon and label) per tab and state.
        files["tab_sel.png"] = Png(112, 32, g =>
        {
            var plate = Geo("M 1,32 L 1,10 Q 1,1.5 10,1.5 L 102,1.5 Q 111,1.5 111,10 L 111,32 Z");
            g.DrawGeometry(Vertical((C("#A2D9FF"), 0), (C("#55A5F8"), 0.45), (C("#2F7CE8"), 0.55), (C("#2468D8"), 1)), Pen(C("#D3EDFF"), 1.3), plate);
            g.DrawGeometry(Vertical((WithAlpha(Colors.White, 130), 0), (WithAlpha(Colors.White, 0), 1)), null, Geo("M 3,15 L 3,10 Q 3,3.5 10,3.5 L 102,3.5 Q 109,3.5 109,10 L 109,15 Z"));
        });
        files["sep.png"] = Png(2, 22, g =>
        {
            g.FillRectangle(Solid(WithAlpha(C("#051A5C"), 220)), new Rect(0, 0, 1, 22));
            g.FillRectangle(Solid(WithAlpha(C("#4B86E6"), 120)), new Rect(1, 0, 1, 22));
        });
        var tabs = new (string Key, string Label, string Icon)[]
        {
            ("now", "Now Playing", "M 8,15 L 8,5 L 15,3 L 15,13 M 5.5,15 a 2.5,2 0 1,0 5,0 a 2.5,2 0 1,0 -5,0 M 12.5,13 a 2.5,2 0 1,0 5,0 a 2.5,2 0 1,0 -5,0"),
            ("library", "Library", "M 10,10 m -6.5,0 a 6.5,6.5 0 1,0 13,0 a 6.5,6.5 0 1,0 -13,0 M 10,10 m -2,0 a 2,2 0 1,0 4,0 a 2,2 0 1,0 -4,0 M 5.5,7 Q 7,4.5 10,4"),
            ("radio", "Radio", NavIcons[3].Path),
            ("settings", "Settings", NavIcons[4].Path),
        };
        foreach (var (key, label, icon) in tabs)
        {
            foreach (var down in new[] { false, true })
            {
                var tabW = down ? 112 : 76; // the selected tab is as wide as its plate
                files[$"tab_{key}{(down ? "_down" : string.Empty)}.png"] = Png(tabW, GTabs, g =>
                {
                    // Buttons only react where the picture is not transparent: a barely visible fill makes the whole tab tappable.
                    g.FillRectangle(Solid(Color.FromArgb(2, 0, 0, 0)), new Rect(0, 0, tabW, GTabs));
                    var shortLabel = !down && key == "now" ? "Now" : label; // an unselected tab has less room
                    var ft = new FormattedText(shortLabel, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Pardeh.UI.Rendering.UiTheme.Typeface(false), 11, Solid(down ? Colors.White : C("#D6E6FF")));
                    var total = 17 + 3 + ft.Width;
                    var x0 = (tabW - total) / 2;
                    var cy = down ? 17.0 : 17.5;
                    if (key == "library")
                        Pardeh.UI.Rendering.PictorialIcons.Draw(g, "cd", new Rect(x0 - 0.5, cy - 9.5, 19, 19), shadow: false, glass: true); // the design's glossy silver CD (follows the skin colour)
                    else
                        StrokeIcon(g, down ? Colors.White : C("#E2EDFF"), icon, x0 + 8.5, cy, 0.85, 1.7);
                    using (g.PushTextOptions(new TextOptions { TextRenderingMode = TextRenderingMode.Antialias }))
                        g.DrawText(ft, new Point(x0 + 20, cy - ft.Height / 2));
                });
            }
        }

        // Cover tile: a blue light-swirl tile in a light glass frame; real art is laid over it.
        files["cover_note.png"] = Png(112, 112, g =>
        {
            using (g.PushTransform(Matrix.CreateScale(112.0 / 128, 112.0 / 128)))
            {
                var tile = new Rect(1, 1, 126, 126);
                using (g.PushClip(new RoundedRect(tile, 9)))
                {
                    g.FillRectangle(Vertical((C("#2A74F0"), 0), (C("#1048C8"), 0.5), (C("#0A2A96"), 1)), new Rect(0, 0, 128, 128));
                    g.DrawEllipse(Radial(WithAlpha(C("#B4E4FF"), 210), WithAlpha(C("#B4E4FF"), 0)), null, new Point(48, 46), 84, 70);
                    foreach (var (path, alpha, width) in new[]
                    {
                        ("M -10,100 C 30,40 90,16 140,26", 190, 1.8), ("M -6,124 C 40,76 100,52 150,72", 130, 1.4),
                        ("M -8,64 C 38,92 92,104 140,82", 100, 1.3), ("M 20,138 C 40,90 78,60 138,50", 90, 1.1),
                        ("M 0,30 C 40,10 90,20 130,60", 70, 1.0),
                    })
                        g.DrawGeometry(null, new Pen(Solid(WithAlpha(Colors.White, (byte)alpha)), width, lineCap: PenLineCap.Round), Geo(path));
                    g.DrawEllipse(null, Pen(WithAlpha(C("#CFEFFF"), 100), 1.2), new Point(64, 66), 52, 44);
                    g.DrawEllipse(null, Pen(WithAlpha(C("#CFEFFF"), 60), 1.0), new Point(64, 66), 60, 52);
                    g.DrawEllipse(Solid(WithAlpha(Colors.White, 230)), null, new Point(112, 22), 1.8, 1.8);
                    g.DrawEllipse(Solid(WithAlpha(Colors.White, 160)), null, new Point(20, 98), 1.3, 1.3);
                }

                g.DrawRectangle(null, Pen(C("#BBDCFF"), 1.8), new RoundedRect(tile.Deflate(0.4), 9));
            }
        });
        files["cover_rim.png"] = Png(112, 112, g => g.DrawRectangle(null, Pen(C("#BBDCFF"), 1.8), new RoundedRect(new Rect(1.3, 1.3, 109.4, 109.4), 8)));
        // One star per rating step: a gold one (lit) and a blue one (empty), each in a tap-sized cell.
        foreach (var lit in new[] { true, false })
        {
            files[lit ? "star_on.png" : "star_off.png"] = Png(18, 22, g =>
            {
                var star = Geo(StarPath(9, 11.3, 7.9, 3.9));
                g.DrawGeometry(lit ? Vertical((C("#FFE98A"), 0), (C("#FFB91F"), 1)) : Vertical((C("#8DB5F0"), 0), (C("#5D8BDA"), 1)),
                    Pen(lit ? C("#C07C0C") : C("#4A78C8"), 0.8), star);
            });
        }

        // Seek row: a pill track in three parts (caps and a tiled middle) under a transparent slider; bright filled part; silver thumb.
        Slices(files, "tr", 60, 22, 6, g => g.DrawRectangle(Vertical((C("#06205E"), 0), (C("#0D348F"), 1)), Pen(C("#5C95EA"), 1), new RoundedRect(new Rect(0.5, 6.5, 59, 8), 4)));
        // Both are 10 wide with a 4 pixel border: the slider keeps the rounded ends and stretches the middle.
        files["seek_empty.png"] = Png(10, 22, g => { });
        files["seek_fill.png"] = Png(10, 22, g => g.DrawRectangle(Vertical((C("#CDF1FF"), 0), (C("#58C0FF"), 0.45), (C("#1F88F0"), 1)), null, new RoundedRect(new Rect(0, 8, 10, 5.6), 2.8)));
        foreach (var name in new[] { "thumb.png", "thumb_hover.png" })
        {
            files[name] = Png(24, 24, g =>
            {
                var c = new Point(12, 12);
                g.DrawEllipse(Solid(WithAlpha(Colors.Black, 80)), null, new Point(12, 13), 8.6, 8.6);
                g.DrawEllipse(Vertical((C("#FFFFFF"), 0), (C("#DCE8F8"), 0.5), (C("#9DB6E2"), 1)), Pen(C("#7391D2"), 1.1), c, 8.4, 8.4);
                g.DrawEllipse(Vertical((C("#F4F8FF"), 0), (C("#B7CAEC"), 1)), Pen(WithAlpha(C("#6D88C8"), 120), 0.8), c, 4.6, 4.6);
                g.DrawEllipse(Vertical((WithAlpha(Colors.White, 230), 0), (WithAlpha(Colors.White, 0), 1)), null, new Point(12, 8), 5.6, 3.2);
            });
        }

        // Transport pill: caps and a tiled middle cut from one drawing, translucent light glass.
        Slices(files, "pill", 200, GPillH, 36, g =>
        {
            var rect = new Rect(0.8, 8.8, 198.4, 50.4);
            g.DrawRectangle(Solid(WithAlpha(C("#0C2F8E"), 200)), null, new RoundedRect(rect.Inflate(0.8), 26.8));
            g.DrawRectangle(PillFace, Pen(WithAlpha(Colors.White, 200), 1.2), new RoundedRect(rect, 26));
            g.DrawRectangle(null, Pen(C("#2A58B8"), 0.8), new RoundedRect(rect.Deflate(1.6), 24.4));
        });

        foreach (var (state, suffix) in States)
        {
            var down = state == State.Down;
            var off = state == State.Disabled;
            files[$"play{suffix}.png"] = Png(68, 68, g => OrbDisc(g, 68, down, off, c => FillIcon(g, c, "M 6.5,3.5 L 17,10 L 6.5,16.5 Z", 34.8, 34, 1.9)));
            files[$"pause{suffix}.png"] = Png(68, 68, g => OrbDisc(g, 68, down, off, c =>
            {
                g.DrawRectangle(Solid(c), null, new RoundedRect(new Rect(23.5, 22, 7.5, 24), 1.4));
                g.DrawRectangle(Solid(c), null, new RoundedRect(new Rect(37, 22, 7.5, 24), 1.4));
            }));
            files[$"prev{suffix}.png"] = Png(40, 40, g => LightDisc(g, 40, down, off, c => FillIcon(g, c, "M 3.4,4.8 L 5.4,4.8 L 5.4,15.2 L 3.4,15.2 Z M 11.4,4.8 L 5.8,10 L 11.4,15.2 Z M 17.2,4.8 L 11.6,10 L 17.2,15.2 Z", 20, 20, 1.6)));
            files[$"next{suffix}.png"] = Png(40, 40, g => LightDisc(g, 40, down, off, c => FillIcon(g, c, "M 16.6,4.8 L 14.6,4.8 L 14.6,15.2 L 16.6,15.2 Z M 8.6,4.8 L 14.2,10 L 8.6,15.2 Z M 2.8,4.8 L 8.4,10 L 2.8,15.2 Z", 20, 20, 1.6)));
            files[$"stop{suffix}.png"] = Png(30, 30, g => LightDisc(g, 30, down, off, c => g.DrawRectangle(Solid(c), null, new RoundedRect(new Rect(9, 9, 12, 12), 1.8))));
            if (off)
                continue;

            // Skip back / forward 10 s: a 32 unit light disc inside a 40 unit touch box (a barely visible fill makes the whole box tappable).
            foreach (var forward in new[] { false, true })
            {
                files[$"{(forward ? "skipf" : "skipb")}{suffix}.png"] = Png(40, 40, g =>
                {
                    g.FillRectangle(Solid(Color.FromArgb(2, 0, 0, 0)), new Rect(0, 0, 40, 40));
                    using (g.PushTransform(Matrix.CreateTranslation(4, 4)))
                        LightDisc(g, 32, down, false, c => StrokeIcon(g, c, forward ? SkipForwardPath : SkipBackPath, 16, 16, 1.1, 1.7));
                });
            }
        }

        // The speaker button opens the volume popup; its glyph shows high / low / muted (the skin shows one of the three).
        foreach (var (name, muted, low) in new[] { ("mute", false, false), ("mutelow", false, true), ("muted", true, false) })
        {
            files[$"{name}.png"] = Png(30, 30, g => LightDisc(g, 30, false, false, c => SpeakerGlyph(g, c, 15, 15, 0.95, muted, low)));
            files[$"{name}_down.png"] = Png(30, 30, g => LightDisc(g, 30, true, false, c => SpeakerGlyph(g, c, 15, 15, 0.95, muted, low)));
        }

        // Playlist card: header and bottom caps with tiled middles (cut from one drawing each).
        Slices(files, "hdr", 100, GHead, 12, g =>
        {
            using (g.PushClip(new RoundedRect(new Rect(0.7, 0.7, 98.6, 60), 10)))
            {
                g.FillRectangle(Vertical((C("#2A62D6"), 0), (C("#1B4DBE"), 0.5), (C("#143C9E"), 1)), new Rect(0, 0, 100, GHead));
                g.FillRectangle(Solid(WithAlpha(Colors.White, 40)), new Rect(0, 1, 100, 1));
                g.FillRectangle(Solid(C("#06226E")), new Rect(0, GHead - 1, 100, 1));
            }

            g.DrawRectangle(null, Pen(C("#5C95EA"), 1.4), new RoundedRect(new Rect(0.7, 0.7, 98.6, 60), 10));
        });
        Slices(files, "bot", 100, 10, 12, g =>
        {
            var rect = new Rect(0.7, -50, 98.6, 59.3);
            g.DrawRectangle(Solid(GCardBody), Pen(C("#5C95EA"), 1.4), new RoundedRect(rect, 10));
        });
        files["info_thumb.png"] = Png(62, 40, g =>
        {
            var tile = new Rect(0.7, 0.7, 60.6, 38.6);
            g.DrawRectangle(Vertical((C("#3C86F0"), 0), (C("#0F3AA8"), 1)), Pen(C("#BBDCFF"), 1.2), new RoundedRect(tile, 5));
            StrokeIcon(g, Colors.White, "M 3,5 L 13,5 L 13,15 L 3,15 Z M 13,9 L 17,6 L 17,14 L 13,11", 31, 20, 1.5, 1.4);
        });
        files["vol_icon.png"] = Png(24, 24, g => SpeakerGlyph(g, Colors.White, 12, 12, 1.1, false));
        Slices(files, "info", 100, 56, 12, g =>
        {
            var rect = new Rect(0.7, 0.7, 98.6, 54.6);
            g.DrawRectangle(Solid(GCardBody), Pen(C("#5C95EA"), 1.4), new RoundedRect(rect, 10));
            g.DrawRectangle(Vertical((WithAlpha(Colors.White, 36), 0), (WithAlpha(Colors.White, 0), 1)), null, new RoundedRect(new Rect(2, 2, 96, 22), 8));
        });
        files["list_icon.png"] = Png(28, 28, g => StrokeIcon(g, Colors.White, "M 4,6 L 4.6,6 M 8,6 L 16,6 M 4,10 L 4.6,10 M 8,10 L 16,10 M 4,14 L 4.6,14 M 8,14 L 16,14", 14, 14, 1.1, 2.2));
        foreach (var (name, y1, y2) in new[] { ("chev_down", 11.0, 18.0), ("chev_up", 18.0, 11.0) })
        {
            files[$"{name}.png"] = Png(28, 28, g =>
            {
                var pen = new Pen(Solid(Colors.White), 2.3, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                g.DrawLine(pen, new Point(8, y1), new Point(14, y2));
                g.DrawLine(pen, new Point(14, y2), new Point(20, y1));
            });
        }

        // Shuffle and repeat toggles in the Playlist card's header: a pale glyph when off, a lit glassy plate with a cyan glyph when on
        // (the "down" image is the on state: the skin's SHUFFLEBUTTON / REPEATBUTTON show it while the player's mode is on).
        foreach (var (name, path) in new[] { ("hshuf", IconShuffle), ("hrep", IconRepeat) })
        {
            foreach (var on in new[] { false, true })
            {
                files[$"{name}{(on ? "_down" : string.Empty)}.png"] = Png(GToggleW, GHead, g =>
                {
                    g.FillRectangle(Solid(Color.FromArgb(2, 0, 0, 0)), new Rect(0, 0, GToggleW, GHead)); // the whole button is a tap target
                    if (on)
                    {
                        var plate = new Rect(2.5, 3, GToggleW - 5, GHead - 6);
                        g.DrawRectangle(Vertical((WithAlpha(C("#5CC8FF"), 120), 0), (WithAlpha(C("#1B6FE0"), 170), 1)), Pen(WithAlpha(C("#9ED2FF"), 210), 1), new RoundedRect(plate, 7));
                        g.DrawRectangle(Vertical((WithAlpha(Colors.White, 70), 0), (WithAlpha(Colors.White, 0), 1)), null, new RoundedRect(new Rect(plate.X + 2, plate.Y + 1.5, plate.Width - 4, plate.Height * 0.45), 5));
                    }

                    StrokeIcon(g, on ? C("#D2F4FF") : WithAlpha(Colors.White, 215), path, GToggleW / 2.0, GHead / 2.0, 1.05, 1.7);
                });
            }
        }

        // Sort and filter in the Playlist card's header, drawn like the shuffle / repeat toggles beside them (the filter's "down" image is "a filter is on").
        foreach (var (name, path) in new[] { ("hsort", IconSort), ("hfilt", IconFilter) })
        {
            foreach (var on in new[] { false, true })
            {
                if (name == "hsort" && on)
                    continue;
                files[$"{name}{(on ? "_down" : string.Empty)}.png"] = Png(GToggleW, GHead, g =>
                {
                    g.FillRectangle(Solid(Color.FromArgb(2, 0, 0, 0)), new Rect(0, 0, GToggleW, GHead));
                    if (on)
                    {
                        var plate = new Rect(2.5, 3, GToggleW - 5, GHead - 6);
                        g.DrawRectangle(Vertical((WithAlpha(C("#5CC8FF"), 120), 0), (WithAlpha(C("#1B6FE0"), 170), 1)), Pen(WithAlpha(C("#9ED2FF"), 210), 1), new RoundedRect(plate, 7));
                        g.DrawRectangle(Vertical((WithAlpha(Colors.White, 70), 0), (WithAlpha(Colors.White, 0), 1)), null, new RoundedRect(new Rect(plate.X + 2, plate.Y + 1.5, plate.Width - 4, plate.Height * 0.45), 5));
                    }

                    StrokeIcon(g, on ? C("#D2F4FF") : WithAlpha(Colors.White, 215), path, GToggleW / 2.0, GHead / 2.0, 1.05, 1.7);
                });
            }
        }

        // The favourite heart on Now Playing: a glossy pictorial heart (silver-blue when it is not a favourite, red when it is).
        AddFavoriteArt(files, modern: false, glass: true, Colors.White, Colors.White);
        AddLyricsArt(files, modern: false, glass: true, Colors.White, Colors.White);

        // Bottom bar and its dark glass pills.
        files["foot.png"] = Png(1, GFoot, g =>
        {
            g.FillRectangle(Vertical((C("#3F82EC"), 0), (C("#2158D0"), 0.45), (C("#1648B8"), 1)), new Rect(0, 0, 1, GFoot));
            g.FillRectangle(Solid(WithAlpha(Colors.White, 160)), new Rect(0, 0, 1, 1));
            g.FillRectangle(Solid(WithAlpha(Colors.White, 40)), new Rect(0, 1, 1, 1));
        });
        var buttons = new (string Name, double Width, Action<DrawingContext, Color, double> Glyph)[]
        {
            ("addpl", 66, (g, c, w) => StrokeIcon(g, c, "M 3,6 L 12,6 M 3,10 L 12,10 M 3,14 L 8,14 M 14.2,10 L 14.2,17 M 10.7,13.5 L 17.7,13.5", w / 2, 14.5, 1.1, 1.8)),
            // The plate only: the bars on it are the live EQBARS element "eqViz" (they move with the music).
            ("viz", 150, (g, c, w) => { }),
            ("full", 66, (g, c, w) => StrokeIcon(g, c, "M 11,4 L 16,4 L 16,9 M 16,4 L 10.5,9.5 M 9,16 L 4,16 L 4,11 M 4,16 L 9.5,10.5", w / 2, 14.5, 1.1, 1.8)),
            ("repeat", 66, (g, c, w) => StrokeIcon(g, c, IconRepeat, w / 2, 14.5, 1.05, 1.8)),
            ("cc", 66, (g, c, w) =>
            {
                g.DrawRectangle(null, Pen(c, 1.7), new RoundedRect(new Rect(w / 2 - 11, 6.5, 22, 16), 4));
                var ft = new FormattedText("CC", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Pardeh.UI.Rendering.UiTheme.Typeface(true), 9, Solid(c));
                g.DrawText(ft, new Point(w / 2 - ft.Width / 2, 14.5 - ft.Height / 2));
            }),
            ("aspect", 66, (g, c, w) =>
            {
                g.DrawRectangle(null, Pen(c, 1.7), new RoundedRect(new Rect(w / 2 - 12, 7, 24, 15), 3));
                var ft = new FormattedText("4:3", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Pardeh.UI.Rendering.UiTheme.Typeface(true), 8.5, Solid(c));
                g.DrawText(ft, new Point(w / 2 - ft.Width / 2, 14.5 - ft.Height / 2));
            }),
        };
        foreach (var (name, width, glyph) in buttons)
        {
            foreach (var down in new[] { false, true })
                files[$"g_{name}{(down ? "_down" : string.Empty)}.png"] = Png((int)width, 30, g =>
                {
                    GlassPlate(g, new Rect(0, 0, width, 30), 11, down);
                    glyph(g, GInk, width);
                });
        }
    }

    // ---- Layout ----------------------------------------------------------------------------------

    private static string GlassLayout()
    {
        var f = "Segoe UI";
        var tabs = new StringBuilder();
        foreach (var (key, id) in new[] { ("now", "tabNow"), ("library", "tabLibrary"), ("radio", "tabRadio"), ("settings", "tabSettings") })
        {
            var page = key == "settings" ? "skins" : key;
            tabs.Append($$"""<BUTTON id="{{id}}" left="0" top="0" image="tab_{{key}}.png" hoverImage="tab_{{key}}.png" downImage="tab_{{key}}_down.png" onClick="ShowPane('{{page}}');"/>""").Append('\n');
        }

        var stars = new StringBuilder();
        for (var i = 1; i <= 5; i++)
        {
            var x = (i - 1) * 18;
            stars.Append($$"""                  <SUBVIEW id="sOff{{i}}" left="{{x}}" top="0" width="18" height="22" backgroundImage="star_off.png" onClick="SetStars({{i}});"/>""").Append('\n');
            stars.Append($$"""                  <SUBVIEW id="sOn{{i}}" left="{{x}}" top="0" width="18" height="22" backgroundImage="star_on.png" visible="false" onClick="SetStars({{i}});"/>""").Append('\n');
        }

        var h = H - GBar - GTabs - 8;
        var brand = AppSettings.Current.ClassicBranding ? "Windows Media Player" : "Pardeh";
        return $$"""
        <THEME id="PardehGlass" title="Pardeh Glass" author="Pardeh contributors" pardehMode="full">
          <VIEW id="vPhone" width="{{W}}" height="{{H}}" minWidth="300" minHeight="300" resizable="true" titleBar="false" backgroundColor="#040A1E" antialiasEdges="true"
                scriptFile="phone.js" onLoad="OnLoad();" onClose="OnClose();" onResize="Relayout();">
            <PLAYER openstatechange="OnOpenState(NewState);"/>

            <SUBVIEW id="svContent" left="6" top="{{4 + GBar + GTabs}}" width="{{W - 12}}" height="{{h}}" backgroundColor="#0F3A9F" backgroundImage="body.png">

              <SUBVIEW id="paneNow" left="0" top="0" width="{{W - 12}}" height="{{h}}">
                <WMPVIDEO id="vid" left="0" top="0" width="344" height="190" visible="false" backgroundColor="#000000" horizontalAlignment="stretch" verticalAlignment="stretch"/>
                <WMPEFFECTS id="vis" left="0" top="0" width="344" height="190" visible="false" palette="glass" picker="true" vizSwipe="true"/>
                <SUBVIEW id="svCover" left="18" top="14" width="112" height="112" backgroundImage="cover_note.png">
                  <SUBVIEW left="3" top="3" width="106" height="106" backgroundImage="WMPImage_AlbumArtLarge" coverFill="true" coverRadius="7" coverScale="{{ArtScale}}"/>
                  <SUBVIEW left="0" top="0" width="112" height="112" backgroundImage="cover_rim.png"/>
                </SUBVIEW>
                {{LyricsElementXml("lyr", 344, 250, "#FFFFFF", "#8FE3FF", "#FFFFFF", false, f)}}
                <TEXT id="tTitle" left="144" top="20" width="190" height="20" wordWrap="true" fontType="{{f}}" fontSize="12.5" fontStyle="bold" foregroundColor="#FFFFFF"
                    value="wmpprop:player.currentMedia ? player.currentMedia.name : 'Nothing playing'"/>
                <TEXT id="tArtist" left="144" top="46" width="190" height="18" fontType="{{f}}" fontSize="11.5" foregroundColor="#D2E4FF"
                    value="wmpprop:player.currentMedia ? player.currentMedia.getItemInfo('Author') : ''"/>
                <TEXT id="tAlbum" left="144" top="67" width="190" height="18" fontType="{{f}}" fontSize="11.5" fontStyle="italic" foregroundColor="#C2D8FF"
                    value="wmpprop:player.currentMedia ? player.currentMedia.getItemInfo('Album') : ''"/>
                <SUBVIEW id="svStars" left="144" top="93" width="90" height="22" visible="false">
        {{stars}}
                </SUBVIEW>
                <FAVBUTTON id="bFav" left="292" top="92" image="fav.png" hoverImage="fav.png" downImage="fav_down.png" disabledImage="fav_disabled.png" upToolTip="Add to favourites" downToolTip="Remove from favourites"/>

                <BUTTON id="bLyr" left="86" top="82" image="lyr.png" hoverImage="lyr.png" downImage="lyr_down.png" onClick="ToggleLyrics();" upToolTip="Lyrics" downToolTip="Hide lyrics"/>

                <TEXT id="tPos" left="12" top="142" width="42" height="16" fontType="{{f}}" fontSize="9.5" foregroundColor="#FFFFFF"
                    value="wmpprop:player.currentMedia ? player.controls.currentPositionString : '00:00'"/>
                <SUBVIEW id="trL" left="54" top="138" width="6" height="22" backgroundImage="tr_l.png"/>
                <SUBVIEW id="trM" left="60" top="138" width="200" height="22" backgroundImage="tr_m.png" backgroundTiled="true"/>
                <SUBVIEW id="trR" left="260" top="138" width="6" height="22" backgroundImage="tr_r.png"/>
                <SEEKSLIDER id="seek" left="54" top="138" width="240" height="22"
                    backgroundImage="seek_empty.png" foregroundImage="seek_fill.png" thumbImage="thumb.png" thumbHoverImage="thumb_hover.png"
                    tiled="false" slide="false" borderSize="4" direction="horizontal"/>
                <TEXT id="tRem" left="300" top="142" width="38" height="16" justification="right" fontType="{{f}}" fontSize="9.5" foregroundColor="#FFFFFF"
                    value="wmpprop:player.currentMedia ? (function(){var r=Math.max(0,Math.round(player.currentMedia.duration-player.controls.currentPosition));var m=Math.floor(r/60),s=r%60;return '-'+(m&lt;10?'0':'')+m+':'+(s&lt;10?'0':'')+s;})() : ''"/>

                <SUBVIEW id="svPill" left="15" top="167" width="318" height="{{GPillH}}">
                  <SUBVIEW id="pillL" left="0" top="0" width="36" height="{{GPillH}}" backgroundImage="pill_l.png"/>
                  <SUBVIEW id="pillM" left="36" top="0" width="246" height="{{GPillH}}" backgroundImage="pill_m.png" backgroundTiled="true"/>
                  <SUBVIEW id="pillR" left="282" top="0" width="36" height="{{GPillH}}" backgroundImage="pill_r.png"/>
                  <BUTTON id="bMute" left="24" top="19" visible="wmpprop:!player.settings.mute &amp;&amp; player.settings.volume &gt;= 50" image="mute.png" hoverImage="mute.png" downImage="mute_down.png" onClick="VolPopup();" onLongPress="ToggleMute();" upToolTip="Volume"/>
                  <BUTTON id="bMuteLow" left="24" top="19" visible="wmpprop:!player.settings.mute &amp;&amp; player.settings.volume &gt; 0 &amp;&amp; player.settings.volume &lt; 50" image="mutelow.png" hoverImage="mutelow.png" downImage="mutelow_down.png" onClick="VolPopup();" onLongPress="ToggleMute();" upToolTip="Volume"/>
                  <BUTTON id="bMuted" left="24" top="19" visible="wmpprop:player.settings.mute || player.settings.volume &lt;= 0" image="muted.png" hoverImage="muted.png" downImage="muted_down.png" onClick="VolPopup();" onLongPress="ToggleMute();" upToolTip="Volume"/>
                  <PREVBUTTON id="bPrev" left="77" top="14" image="prev.png" hoverImage="prev.png" downImage="prev_down.png" disabledImage="prev_disabled.png" upToolTip="Previous"/>
                  <PLAYBUTTON id="bPlay" left="125" top="0" image="play.png" hoverImage="play.png" downImage="play_down.png" disabledImage="play_disabled.png" upToolTip="Play"/>
                  <PAUSEBUTTON id="bPause" left="125" top="0" visible="wmpenabled:player.controls.pause" image="pause.png" hoverImage="pause.png" downImage="pause_down.png" disabledImage="pause_disabled.png" upToolTip="Pause"/>
                  <NEXTBUTTON id="bNext" left="181" top="14" image="next.png" hoverImage="next.png" downImage="next_down.png" disabledImage="next_disabled.png" upToolTip="Next"/>
                  <STOPBUTTON id="bStop" left="264" top="19" image="stop.png" hoverImage="stop.png" downImage="stop_down.png" disabledImage="stop_disabled.png" upToolTip="Stop"/>
                  <BUTTON id="bSkipB" left="46" top="14" image="skipb.png" hoverImage="skipb.png" downImage="skipb_down.png" upToolTip="Back 10 seconds" onClick="player.controls.seekBy(-10);"/>
                  <BUTTON id="bSkipF" left="232" top="14" image="skipf.png" hoverImage="skipf.png" downImage="skipf_down.png" upToolTip="Forward 10 seconds" onClick="player.controls.seekBy(10);"/>
                </SUBVIEW>

                <SUBVIEW id="svVol" left="15" top="0" width="318" height="26" visible="false">
                  <SUBVIEW left="10" top="1" width="24" height="24" backgroundImage="vol_icon.png"/>
                  <SUBVIEW id="vtrL" left="46" top="2" width="6" height="22" backgroundImage="tr_l.png"/>
                  <SUBVIEW id="vtrM" left="52" top="2" width="200" height="22" backgroundImage="tr_m.png" backgroundTiled="true"/>
                  <SUBVIEW id="vtrR" left="260" top="2" width="6" height="22" backgroundImage="tr_r.png"/>
                  <VOLUMESLIDER id="vol" left="48" top="2" width="240" height="22"
                      backgroundImage="seek_empty.png" foregroundImage="seek_fill.png" thumbImage="thumb.png" thumbHoverImage="thumb_hover.png"
                      tiled="false" slide="false" borderSize="4" direction="horizontal"/>
                </SUBVIEW>

                <SUBVIEW id="svInfo" left="8" top="0" width="332" height="56" visible="false">
                  <SUBVIEW id="infL" left="0" top="0" width="12" height="56" backgroundImage="info_l.png"/>
                  <SUBVIEW id="infM" left="12" top="0" width="308" height="56" backgroundImage="info_m.png" backgroundTiled="true"/>
                  <SUBVIEW id="infR" left="320" top="0" width="12" height="56" backgroundImage="info_r.png"/>
                  <SUBVIEW left="9" top="8" width="62" height="40" backgroundImage="info_thumb.png"/>
                  <SUBVIEW left="9" top="8" width="62" height="40" backgroundImage="WMPImage_AlbumArtLarge" coverFill="true" coverRadius="4" coverScale="{{ArtScale}}"/>
                  <TEXT id="tInfoTitle" left="82" top="8" width="240" height="20" fontType="{{f}}" fontSize="12" fontStyle="bold" foregroundColor="#FFFFFF"
                      value="wmpprop:player.currentMedia ? player.currentMedia.name : ''"/>
                  <TEXT id="tInfoSub" left="82" top="29" width="240" height="18" fontType="{{f}}" fontSize="10.5" foregroundColor="#C2D8FF"
                      value="wmpprop:player.currentMedia ? ('Video • ' + player.currentMedia.durationString) : ''"/>
                </SUBVIEW>

                <SUBVIEW id="svCard" left="8" top="241" width="332" height="260">
                  <SUBVIEW id="svPlHead" left="0" top="0" width="332" height="{{GHead}}" onClick="ToggleList();">
                    <SUBVIEW id="hdrL" left="0" top="0" width="12" height="{{GHead}}" backgroundImage="hdr_l.png"/>
                    <SUBVIEW id="hdrM" left="12" top="0" width="308" height="{{GHead}}" backgroundImage="hdr_m.png" backgroundTiled="true"/>
                    <SUBVIEW id="hdrR" left="320" top="0" width="12" height="{{GHead}}" backgroundImage="hdr_r.png"/>
                    <SUBVIEW left="10" top="0" width="28" height="28" backgroundImage="list_icon.png"/>
                    <TEXT id="tPlHead" left="44" top="5" width="190" height="20" fontType="{{f}}" fontSize="11" fontStyle="bold" foregroundColor="#FFFFFF" value="Playlist"/>
                    <QUEUESORTBUTTON id="bQSort" left="148" top="0" image="hsort.png" hoverImage="hsort.png" downImage="hsort.png" disabledImage="hsort.png" upToolTip="Sort and playlist tools"/>
                    <QUEUEFILTERBUTTON id="bQFilter" left="184" top="0" image="hfilt.png" hoverImage="hfilt.png" downImage="hfilt_down.png" upToolTip="Filter the playlist" downToolTip="Filter is on"/>
                    <SHUFFLEBUTTON id="bShuffle" left="220" top="0" image="hshuf.png" hoverImage="hshuf.png" downImage="hshuf_down.png" upToolTip="Turn Shuffle On" downToolTip="Turn Shuffle Off"/>
                    <REPEATBUTTON id="bRepeat" left="256" top="0" image="hrep.png" hoverImage="hrep.png" downImage="hrep_down.png" upToolTip="Turn Repeat On" downToolTip="Turn Repeat Off"/>
                    <SUBVIEW id="chevDown" left="294" top="0" width="28" height="28" backgroundImage="chev_down.png"/>
                    <SUBVIEW id="chevUp" left="294" top="0" width="28" height="28" backgroundImage="chev_up.png" visible="false"/>
                  </SUBVIEW>
                  <SUBVIEW id="sideL" left="0" top="{{GHead}}" width="1" height="200" backgroundColor="#4F89E6"/>
                  <SUBVIEW id="sideR" left="331" top="{{GHead}}" width="1" height="200" backgroundColor="#4F89E6"/>
                  <PLAYLIST id="ipl" left="1" top="{{GHead}}" width="330" height="200" touch="true" numbered="true" backgroundColor="#0A2569" foregroundColor="#D6E6FF"
                      itemPlayingColor="#FFFFFF" itemPlayingBackgroundColor="#2B7BF0" itemSelectedBackgroundColor="#14409E" horizontalAlignment="stretch" verticalAlignment="stretch"/>
                  <SUBVIEW id="botL" left="0" top="232" width="12" height="10" backgroundImage="bot_l.png"/>
                  <SUBVIEW id="botM" left="12" top="232" width="308" height="10" backgroundImage="bot_m.png" backgroundTiled="true"/>
                  <SUBVIEW id="botR" left="320" top="232" width="12" height="10" backgroundImage="bot_r.png"/>
                </SUBVIEW>

                <SUBVIEW id="svFoot" left="0" top="{{h - GFoot}}" width="{{W - 12}}" height="{{GFoot}}" backgroundImage="foot.png" backgroundTiled="true">
                  <BUTTON id="bAdd" left="16" top="8" image="g_addpl.png" hoverImage="g_addpl.png" downImage="g_addpl_down.png" onClick="pardeh.command('libraryaddfiles');" upToolTip="Add to playlist"/>
                  <BUTTON id="bViz" left="90" top="8" image="g_viz.png" hoverImage="g_viz.png" downImage="g_viz_down.png" onClick="ToggleViz();" upToolTip="Visualization"/>
                  <EQBARS id="eqViz" left="142.5" top="13" width="47" height="19" bars="7" barStyle="segments" segments="5" segmentHeight="3" segmentGap="1" gap="2" rest="0.2" palette="glass" colors="#35C6FF;#35C6FF;#9CEBFF"/>
                  <BUTTON id="bRepeatV" left="16" top="8" visible="false" image="g_repeat.png" hoverImage="g_repeat.png" downImage="g_repeat_down.png" onClick="player.settings.setMode('loop', !player.settings.getMode('loop'));" upToolTip="Repeat"/>
                  <BUTTON id="bCC" left="100" top="8" visible="false" image="g_cc.png" hoverImage="g_cc.png" downImage="g_cc_down.png" onClick="pardeh.command('quickpanel','subtitles');" upToolTip="Subtitles"/>
                  <BUTTON id="bAspect" left="190" top="8" visible="false" image="g_aspect.png" hoverImage="g_aspect.png" downImage="g_aspect_down.png" onClick="pardeh.command('quickpanel','video');" upToolTip="Aspect"/>
                  <BUTTON id="bFull" left="260" top="8" image="g_full.png" hoverImage="g_full.png" downImage="g_full_down.png" onClick="pardeh.command('fullscreen');" upToolTip="Full screen"/>
                </SUBVIEW>
              </SUBVIEW>

              <SUBVIEW id="paneLibrary" left="0" top="0" width="{{W - 12}}" height="{{h}}" visible="false" backgroundColor="#1648B8">
                <LIBRARY id="library" left="0" top="0" width="{{W - 12}}" height="{{h - 14}}" phone="true" libBack="#0A2468" libBar="#143FA6" libBarInk="#FFFFFF" libInk="#FFFFFF" libDim="#B9D4FF" libSel="#1A4FB8" libAccent="#6FC3FF" horizontalAlignment="stretch" verticalAlignment="stretch"/>
              </SUBVIEW>

              <SUBVIEW id="paneRadio" left="0" top="0" width="{{W - 12}}" height="{{h}}" visible="false" backgroundColor="#0A1A52">
                <LISTPANE id="lpRadio" kind="radio" glass="true" noDetails="true" left="0" top="0" width="{{W - 12}}" height="{{h - 14}}" horizontalAlignment="stretch" verticalAlignment="stretch"/>
              </SUBVIEW>

              <SUBVIEW id="paneSkins" left="0" top="0" width="{{W - 12}}" height="{{h}}" visible="false" backgroundColor="#0A2A82">
                <LISTPANE id="lpSkins" kind="settings" left="0" top="0" width="{{W - 12}}" height="{{h - 14}}" horizontalAlignment="stretch" verticalAlignment="stretch"/>
              </SUBVIEW>
            </SUBVIEW>

            <SUBVIEW id="svTabs" left="6" top="{{4 + GBar}}" width="{{W - 12}}" height="{{GTabs}}" backgroundImage="tabs.png" backgroundTiled="true">
              <SUBVIEW id="sep0" left="0" top="5" width="2" height="22" backgroundImage="sep.png"/>
              <SUBVIEW id="sep1" left="0" top="5" width="2" height="22" backgroundImage="sep.png"/>
              <SUBVIEW id="sep2" left="0" top="5" width="2" height="22" backgroundImage="sep.png"/>
              <SUBVIEW id="sep3" left="0" top="5" width="2" height="22" backgroundImage="sep.png"/>
              <SUBVIEW id="selTab" left="4" top="1" width="112" height="32" backgroundImage="tab_sel.png"/>
        {{tabs}}
            </SUBVIEW>
            <SUBVIEW id="svBar" left="6" top="4" width="{{W - 12}}" height="{{GBar}}" backgroundImage="bar.png" backgroundTiled="true">
              <SUBVIEW id="svBrand" left="0" top="0" width="200" height="{{GBar}}" >
                <SUBVIEW left="9" top="6" width="30" height="30" backgroundImage="logo.png"/>
                <TEXT id="tPage" left="46" top="10" width="160" height="24" value="{{brand}}" fontType="{{f}}" fontSize="11.5" foregroundColor="#FFFFFF"/>
                <BUTTON id="bBrand" left="0" top="0" image="brand_hit.png" hoverImage="brand_hit.png" downImage="brand_hit.png" onClick="pardeh.command('menu', 'app');" upToolTip="Menu"/>
              </SUBVIEW>
              <BUTTON id="bMin" left="{{W - 12 - 99}}" top="8" image="tb_min.png" hoverImage="tb_min.png" downImage="tb_min_down.png" onClick="pardeh.command('movetobackground');" upToolTip="Minimize"/>
              <BUTTON id="bMax" left="{{W - 12 - 67}}" top="8" image="tb_max.png" hoverImage="tb_max.png" downImage="tb_max_down.png" onClick="pardeh.command('fullscreen');" upToolTip="Full screen"/>
              <BUTTON id="bClose" left="{{W - 12 - 35}}" top="8" image="tb_close.png" hoverImage="tb_close.png" downImage="tb_close_down.png" onClick="player.controls.stop(); pardeh.command('exitapp');" upToolTip="Close"/>
            </SUBVIEW>

            <SUBVIEW id="eL" left="6" top="{{4 + GCorner}}" width="1" height="600" backgroundColor="#3F7BE0"/>
            <SUBVIEW id="eR" left="{{W - 7}}" top="{{4 + GCorner}}" width="1" height="600" backgroundColor="#3F7BE0"/>
            <SUBVIEW id="cTL" left="6" top="4" width="{{GCorner}}" height="{{GCorner}}" backgroundImage="corner0.png"/>
            <SUBVIEW id="cTR" left="{{W - 6 - GCorner}}" top="4" width="{{GCorner}}" height="{{GCorner}}" backgroundImage="corner1.png"/>
            <SUBVIEW id="cBL" left="6" top="{{H - 4 - GCorner}}" width="{{GCorner}}" height="{{GCorner}}" backgroundImage="corner2.png"/>
            <SUBVIEW id="cBR" left="{{W - 6 - GCorner}}" top="{{H - 4 - GCorner}}" width="{{GCorner}}" height="{{GCorner}}" backgroundImage="corner3.png"/>
          </VIEW>
        </THEME>
        """;
    }

    private const string GlassScript = """
        // Pardeh Glass phone full mode - original script.
        var g_pane = "", g_list = true, g_video = false, g_viz = false, g_vizPicked = false, g_lyr = false, g_vizPrev = false;
        var MX = 6, MY = 4, BAR = 42, TABSH = 33, FOOT = 46, SELW = 112, TW = 76, R = 16, HEAD = 28, PILLH = 68;
        var KEYS = ["now", "library", "radio", "skins"];
        var TABS = { now: "tabNow", library: "tabLibrary", radio: "tabRadio", skins: "tabSettings" };
        var PANES = { now: "paneNow", library: "paneLibrary", radio: "paneRadio", skins: "paneSkins" };
        var SEPS = ["sep0", "sep1", "sep2", "sep3"];

        function OnLoad() {
            var pane = theme.loadPreference("Pane");
            g_pane = pane && TABS[pane] && !IsHidden(pane) ? pane : "now";
            ShowPane(g_pane);
            OnOpenState(player.openState);
        }
        function OnClose() { theme.savePreference("Pane", g_pane); }
        function IsHidden(name) { return name != "now" && name != "skins" && ("," + pardeh.command("hiddenPages") + ",").indexOf("," + name + ",") >= 0; }
        function ApplyPages() { if (g_pane != "" && IsHidden(g_pane)) ShowPane("now"); else Relayout(); }
        function OnBack() {
            if (g_pane == "library" && library.back()) return true;
            // The visualization stage (bottom bar's middle button) is a state of Now Playing: Back returns to the cover first.
            if (g_viz && g_pane == "now") { g_viz = false; Relayout(); return true; }
            // The lyrics view is a state of Now Playing too (the Lyrics button on the cover): Back returns to the cover.
            if (g_lyr && g_pane == "now") { g_lyr = false; Relayout(); return true; }
            if (g_pane != "now") { ShowPane("now"); return true; }
            return false;
        }
        function ToggleList() { if (g_lyr) { g_lyr = false; g_list = true; } else g_list = !g_list; Relayout(); }
        // The Lyrics button: the lyrics replace the cover (portrait) or the playlist card (landscape); the visualization and the lyrics exclude each other.
        function ToggleLyrics() { g_lyr = !g_lyr; if (g_lyr) g_viz = false; Relayout(); }
        // The speaker button: a tap opens the volume popup above it (anchored at its centre top), a long press toggles mute.
        function VolPopup() {
            pardeh.command("volumepopup", svContent.left + svPill.left + bMute.left + 15, svContent.top + svPill.top + bMute.top);
        }
        function ToggleMute() { player.settings.mute = !player.settings.mute; }
        function ToggleViz() {
            // The style (Segmented bars, Mirrored bars, Circular ...) comes from the visualization picker, not from the button.
            g_viz = !g_viz;
            Relayout();
        }
        // Android: a horizontal swipe moves to the next (dir 1) or previous (dir -1) tab.
        function SwipePage(dir) {
            var shown = [];
            for (var i = 0; i < KEYS.length; i++) if (!IsHidden(KEYS[i])) shown.push(KEYS[i]);
            var at = shown.indexOf(g_pane), to = at + dir;
            if (at < 0 || to < 0 || to >= shown.length) return false;
            ShowPane(shown[to]);
            return true;
        }
        function ShowPane(name) {
            if (!TABS[name] || IsHidden(name)) name = "now";
            g_pane = name;
            for (var i = 0; i < KEYS.length; i++) {
                var key = KEYS[i];
                eval(PANES[key]).visible = key == name;
                eval(TABS[key]).down = key == name;
            }
            Relayout();
        }

        function Relayout() {
            try { LayoutAll(); } catch (e) { pardeh.command("glassdiag " + e.message); }
        }

        function LayoutAll() {
            var w = view.width, h = view.height;
            var ww = w - 2 * MX, wh = h - 2 * MY, ch = wh - BAR - TABSH;
            if (ch < 40) return;

            // The rounded window: title bar, tab band, content, corner masks and edges.
            svBar.left = MX; svBar.top = MY; svBar.width = ww;
            bClose.left = ww - 35; bMax.left = ww - 67; bMin.left = ww - 99;
            svBrand.width = ww - 105;
            svTabs.left = MX; svTabs.top = MY + BAR; svTabs.width = ww;
            svContent.left = MX; svContent.top = MY + BAR + TABSH; svContent.width = ww; svContent.height = ch;
            // Library, radio and settings are real controls laid over their pane: leave the rounded bottom corners free.
            for (var key in PANES) { var p = eval(PANES[key]); p.width = ww; p.height = ch; }
            library.width = ww; library.height = ch - 14; lpRadio.width = ww; lpRadio.height = ch - 14; lpSkins.width = ww; lpSkins.height = ch - 14;
            cTL.left = MX; cTL.top = MY; cTR.left = MX + ww - R; cTR.top = MY;
            cBL.left = MX; cBL.top = MY + wh - R; cBR.left = MX + ww - R; cBR.top = MY + wh - R;
            eL.left = MX; eL.top = MY + R; eL.height = wh - 2 * R;
            eR.left = MX + ww - 1; eR.top = MY + R; eR.height = wh - 2 * R;

            // Tabs: the selected one gets a wide glossy plate, the others share the rest and are divided by thin lines.
            var shown = [];
            for (var i = 0; i < KEYS.length; i++) {
                var vis_ = !IsHidden(KEYS[i]);
                eval(TABS[KEYS[i]]).visible = vis_;
                if (vis_) shown.push(KEYS[i]);
            }
            var avail = ww - 8, other = shown.length > 1 ? (avail - SELW) / (shown.length - 1) : avail;
            var tx = 4;
            selTab.visible = false;
            for (var j = 0; j < SEPS.length; j++) eval(SEPS[j]).visible = false;
            for (var k = 0; k < shown.length; k++) {
                var isSel = shown[k] == g_pane;
                var sw = isSel ? SELW : other;
                var tab = eval(TABS[shown[k]]);
                tab.top = 0;
                if (isSel) { tab.width = SELW; tab.left = Math.round(tx); selTab.left = Math.round(tx); selTab.visible = true; }
                else { tab.width = TW; tab.left = Math.round(tx + (sw - TW) / 2); }
                if (isSel) { }
                else if (k < SEPS.length && !(k + 1 < shown.length && shown[k + 1] == g_pane)) {
                    var sp = eval(SEPS[k]); sp.left = Math.round(tx + sw) - 1; sp.visible = true;
                }
                tx += sw;
            }

            var land = ww > ch * 1.15;
            var cw = land ? Math.round(ww * 0.5) : ww;
            var hasMedia = !!player.currentMedia;
            if (g_viz && !g_vizPrev && g_lyr) g_lyr = false;   // the bottom bar's visualization button wins over the lyrics
            g_vizPrev = g_viz;
            var viz = g_viz && !g_video;
            var lyrOn = g_lyr && !g_video && !viz;
            var lyrP = lyrOn && !land;     // portrait: the lyrics take the cover's place, like the visualization stage; landscape: the playlist card's place
            var seekC;
            var lyrTop = 4;   // the Lyrics button's row under the stage (viz / portrait lyrics)

            vid.visible = g_video;
            vis.visible = viz;
            svCover.visible = !g_video && !viz && !lyrP;
            svStars.visible = !g_video && !viz && !lyrP && hasMedia;
            bFav.visible = !g_video && !viz && !lyrP;   // the heart sits with the cover's text block (hidden over video and the visualization)
            var tw = cw - 144 - 10;
            if (g_video) {
                var vh = land ? Math.max(60, ch - FOOT - 106) :Math.min(Math.round(cw * 0.5625), ch - FOOT - 240);
                vid.left = 0; vid.top = 0; vid.width = cw; vid.height = vh;
                tTitle.visible = false; tArtist.visible = false; tAlbum.visible = false;
                seekC = vh + (land ? 16 : 20);
            } else if (viz || lyrP) {
                var zh = lyrP ? Math.max(150, Math.min(420, ch - FOOT - 198)) : land ? Math.max(60, ch - FOOT - 150) : Math.min(Math.round(cw * 0.72), ch - FOOT - 280);
                vis.left = 0; vis.top = 0; vis.width = cw; vis.height = zh;
                if (lyrP) { lyr.left = 0; lyr.top = 0; lyr.width = cw; lyr.height = zh; }
                // Under the stage: title and artist centred, the Lyrics button (44) at the right of that row (never on the artwork).
                tTitle.visible = true; tArtist.visible = true; tAlbum.visible = false;
                tTitle.left = 56; tTitle.width = cw - 112; tTitle.top = zh + 4; tTitle.justification = "center"; tTitle.height = 20;
                tArtist.left = 56; tArtist.width = cw - 112; tArtist.top = zh + 26; tArtist.justification = "center";
                seekC = zh + 60;
                lyrTop = zh + 2;
            } else {
                tTitle.visible = true; tArtist.visible = true; tAlbum.visible = true;
                var name = hasMedia ? ("" + player.currentMedia.name) : "Nothing playing";
                var perLine = Math.max(8, Math.floor(tw / 8.2));
                // Narrow views (under ~330): one title line and no album line, so the stars, the Lyrics button and the heart share the text block.
                var narrow = cw < 332;
                var lines = narrow ? 1 : Math.min(2, Math.ceil(name.length / perLine));
                var extra = (lines - 1) * 18;
                if (land && ch < 270) {
                    // A landscape phone is too short for the cover: the text block takes its place.
                    svCover.visible = false;
                    tw = cw - 36 - 52 - 48;   // the Lyrics button sits left of the heart
                    tTitle.left = 18; tTitle.width = tw; tTitle.top = 3; tTitle.height = 20; tTitle.justification = "left";
                    tArtist.left = 18; tArtist.width = tw; tArtist.top = 26; tArtist.justification = "left";
                    tAlbum.left = 18; tAlbum.width = tw; tAlbum.top = 46;
                    svStars.left = 18; svStars.top = 66;
                    bFav.left = cw - 52; bFav.top = 4;
                    seekC = 98;
                } else {
                    var t0 = land ? 6 : 14;
                    tAlbum.visible = !narrow;
                    svCover.left = 18; svCover.top = t0;
                    tTitle.left = 144; tTitle.width = tw; tTitle.top = t0 + 6; tTitle.height = 20 + extra; tTitle.justification = "left";
                    tArtist.left = 144; tArtist.width = tw; tArtist.top = t0 + 32 + extra; tArtist.justification = "left";
                    tAlbum.left = 144; tAlbum.width = tw - 52; tAlbum.top = t0 + 53 + extra;
                    svStars.left = 144; svStars.top = narrow ? t0 + 55 : t0 + 79 + extra;
                    bFav.left = cw - 50; bFav.top = t0 + 78;
                    bLyr.left = cw - 98; bLyr.top = t0 + 78;   // the text block's last row: Lyrics left of the heart, both 44 wide
                    seekC = t0 + 135;
                }
            }

            // Seek row.
            var st = seekC - 11, sx = 54, sl = cw - 54 - 54;
            tPos.left = 12; tPos.top = seekC - 8;
            trL.left = sx; trL.top = st; trM.left = sx + 6; trM.top = st; trM.width = Math.max(1, sl - 12); trR.left = sx + sl - 6; trR.top = st;
            seek.left = sx + 2; seek.top = st; seek.width = sl - 4; // inside the rounded ends of the track
            RefreshStars();
            tRem.left = cw - 13 - 38; tRem.top = seekC - 8;

            // The transport pill (translucent glass) with the glowing orb overlapping it.
            var pt = seekC + 17;
            var pw = cw - 30;
            svPill.left = 15; svPill.top = pt; svPill.width = pw;
            pillM.width = Math.max(1, pw - 72); pillR.left = pw - 36;
            // Seven buttons in the pill, evenly spaced: volume (30), back 10 s (40), previous (40), the orb (68), next (40), forward 10 s (40), stop (30).
            var pm = 10, pg = Math.max(0, (pw - 2 * pm - 288) / 6), px = pm;
            bMute.left = Math.round(px); bMuteLow.left = bMute.left; bMuted.left = bMute.left; px += 30 + pg;
            bSkipB.left = Math.round(px); px += 40 + pg;
            bPrev.left = Math.round(px); px += 40 + pg;
            bPlay.left = Math.round(px); bPause.left = bPlay.left; px += 68 + pg;
            bNext.left = Math.round(px); px += 40 + pg;
            bSkipF.left = Math.round(px); px += 40 + pg;
            bStop.left = Math.round(px);

            // Playlist card: right column in landscape, below in portrait.
            var cx = 8, cwid = cw - 16, cy = pt + 74;
            // Video in portrait: a volume row and an info card (picture, title, duration) under the transport.
            var extras = g_video && !land && ch > 560;
            svVol.visible = extras; svInfo.visible = extras;
            if (extras) {
                var vw = cw - 30 - 46;
                svVol.left = 15; svVol.top = pt + 76; svVol.width = cw - 30;
                vtrM.width = Math.max(1, vw - 12); vtrR.left = 46 + vw - 6;
                vol.left = 48; vol.width = vw - 4;
                svInfo.left = 8; svInfo.top = pt + 108; svInfo.width = cw - 16;
                infM.width = cw - 16 - 24; infR.left = cw - 16 - 12;
                tInfoTitle.width = cw - 16 - 90; tInfoSub.width = cw - 16 - 90;
                cy = pt + 108 + 56 + 8;
            }
            if (land) { cx = cw + 4; cwid = ww - cw - 12; cy = 10; }
            var bottom = ch - FOOT - 9;
            var listOn = g_list && !lyrP;   // portrait lyrics leave the playlist card at its header
            var chh = Math.max(listOn ? 70 : 38, bottom - cy);
            if (!listOn) chh = HEAD + 10;
            svCard.left = cx; svCard.top = cy; svCard.width = cwid; svCard.height = chh;
            svPlHead.width = cwid; hdrM.width = cwid - 24; hdrR.left = cwid - 12;
            tPlHead.width = cwid - 250;
            bRepeat.left = cwid - 76; bShuffle.left = cwid - 76 - 36;
            bQFilter.left = cwid - 76 - 72; bQSort.left = cwid - 76 - 108;
            chevDown.left = cwid - 34; chevUp.left = cwid - 34;
            chevDown.visible = listOn; chevUp.visible = !listOn;
            ipl.visible = listOn; sideL.visible = listOn; sideR.visible = listOn;
            svCard.visible = !(lyrOn && land);
            // Lyrics in landscape: the right column. The Lyrics button never sits on artwork: in the text block (left of the heart) or beside the centred title under the stage.
            lyr.visible = lyrOn;
            if (lyrOn && land) { lyr.left = cx; lyr.top = 10; lyr.width = cwid; lyr.height = Math.max(100, bottom - 10); }
            bLyr.visible = !g_video;
            bLyr.down = lyrOn;
            if (viz || lyrP) { bLyr.left = cw - 52; bLyr.top = lyrTop; }
            else if (land && ch < 270) { bLyr.left = cw - 100; bLyr.top = 4; }
            ipl.left = 1; ipl.top = HEAD; ipl.width = cwid - 2; ipl.height = Math.max(20, chh - HEAD - 10);
            sideL.height = chh - HEAD - 10; sideR.left = cwid - 1; sideR.height = chh - HEAD - 10;
            var by = listOn ? chh - 10 : HEAD;
            botL.top = by; botM.top = by; botR.top = by;
            botM.width = cwid - 24; botR.left = cwid - 12;

            // Bottom bar.
            svFoot.top = ch - FOOT; svFoot.width = ww;
            if (g_video) {
                bAdd.visible = false; bViz.visible = false; eqViz.visible = false;
                bRepeatV.visible = true; bCC.visible = true; bAspect.visible = true;
                var gap = (ww - 4 * 66) / 5;
                bRepeatV.left = Math.round(gap); bCC.left = Math.round(gap * 2 + 66); bAspect.left = Math.round(gap * 3 + 132); bFull.left = Math.round(gap * 4 + 198);
            } else {
                bAdd.visible = true; bViz.visible = true; eqViz.visible = true;
                bRepeatV.visible = false; bCC.visible = false; bAspect.visible = false;
                bAdd.left = 17; bViz.left = Math.round((ww - 150) / 2); bFull.left = ww - 17 - 66;
                eqViz.left = bViz.left + 52.5; // the live bars sit on the button's plate
            }
        }

        // The playing track's rating from the library: gold stars up to it; tapping a star sets it (the same star again clears it).
        function RefreshStars() {
            var r = Math.round(+pardeh.command("getrating"));
            for (var i = 1; i <= 5; i++) eval("sOn" + i).visible = r >= i;
        }
        function SetStars(n) { pardeh.command("setrating", n); RefreshStars(); }

        function OnOpenState(state) {
            if (state != osMediaOpen) return;
            g_video = !!(player.currentMedia && player.currentMedia.imageSourceWidth > 0);
            if (g_video && g_pane != "now") ShowPane("now"); else Relayout();
        }
        """;
}
