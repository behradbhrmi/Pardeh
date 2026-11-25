using Avalonia;
using Avalonia.Media;

namespace Pardeh.UI.Rendering;

/// <summary>
/// The Pardeh Glass icon families, redrawn from the Collage / Showcase references (vector, 100 x 100 sheet):
/// "t_*" glass tiles (a glossy blue rounded square with a pale glossy glyph: the Library rows), "b_*" the large Browse icons,
/// "s_*" the pale-blue glossy Settings icons, "g_*" the loose icons (iridescent CD, yellow folder, gold star, pale gear, blue i / ? balls).
/// </summary>
internal static partial class PictorialIcons
{
    private static readonly Pal PaleBlue = new("#F6FBFF", "#C6E0FB", "#8DB9F0", "#FFFFFF");
    private static readonly Pal BrowseBlue = new("#D2EAFF", "#79B9F8", "#3D86E4", "#2A62C4");
    private static readonly Pal BallBlue = new("#8CCBFF", "#2F82F2", "#0B47C0", "#082F8C");
    private static readonly Pal GearPale = new("#F1F7FF", "#BBD0F0", "#86A6DC", "#5F82C4");

    private static readonly Dictionary<string, Action<Sheet>> GlassIcons = new()
    {
        ["t_note"] = s => Tile(s, g => PaleAt(g, NoteShape(), 0.2, 20, 14)),
        ["t_person"] = s => Tile(s, g =>
        {
            g.Pale(Geo("M 22,80 Q 22,58 50,58 Q 78,58 78,80 Z"));
            g.Pale(Ell(50, 38, 13.5, 15));
        }),
        ["t_cd"] = s => Tile(s, GlyphCd),
        ["t_tag"] = s => Tile(s, GlyphTag),
        ["t_list"] = s => Tile(s, GlyphBars),
        ["t_star"] = s => Tile(s, g => { using (g.Placed(0.6, 20, 18)) Star(g); }),
        ["t_heart"] = s => Tile(s, g => { using (g.Placed(0.6, 20, 20)) Heart(g); }),
        ["t_clock"] = s => Tile(s, GlyphClock),
        ["t_play"] = s => Tile(s, g => g.Pale(Geo("M 36,27 L 74,50 L 36,73 Z"), 7)),
        ["t_radio"] = s => Tile(s, GlyphRadio),
        ["t_folder"] = s => Tile(s, g => { using (g.Placed(0.62, 19, 21)) FolderYellow(g); }),
        ["t_addfolder"] = s => Tile(s, g =>
        {
            using (g.Placed(0.56, 15, 17))
                FolderYellow(g);
            g.Obj(Ell(70, 70, 17, 17), Green, 3, halo: false);
            g.Line("M 70,61 L 70,79 M 61,70 L 79,70", "#FFFFFF", 6);
        }),
        ["t_phone"] = s => Tile(s, g => { using (g.Placed(0.62, 19, 19)) PhoneMusic(g); }),
        ["t_trophy"] = s => Tile(s, g => { using (g.Placed(0.6, 20, 19)) Trophy(g); }),
        ["t_newnote"] = s => Tile(s, g =>
        {
            PaleAt(g, NoteShape(), 0.17, 17, 18);
            g.Obj(Ell(70, 30, 15, 15), Green, 3, halo: false);
            g.Line("M 70,22 L 70,38 M 62,30 L 78,30", "#FFFFFF", 5);
        }),

        ["b_music"] = s => { using (s.Placed(0.26, 12, 4)) s.Obj(NoteShape(), BrowseBlue, 9, halo: false); },
        ["b_video"] = BrowseVideo,
        ["b_pictures"] = BrowsePictures,
        ["b_radio"] = BrowseRadio,
        ["b_folder"] = s => FolderYellow(s),
        ["b_playlist"] = BrowsePlaylist,
        ["b_tv"] = BrowseTv,
        ["t_tv"] = s => Tile(s, GlyphTv),

        ["s_player"] = s => s.Pale(Geo("M 24,14 L 84,50 L 24,86 Z"), 9),
        ["s_film"] = SettingsVideo,
        ["s_speaker"] = SettingsSpeaker,
        ["s_palette"] = SettingsWheel,
        ["s_info"] = s => InfoBall(s, pale: true),
        ["s_brush"] = SettingsBrush,
        ["s_pages"] = SettingsPages,
        ["s_library"] = s => s.Pale(Geo("M 8,24 Q 8,19 13,19 L 37,19 Q 41,19 44,23 L 48,29 L 87,29 Q 92,29 92,34 L 92,80 Q 92,85 87,85 L 13,85 Q 8,85 8,80 Z"), 3),
        ["s_equalizer"] = SettingsEqualizer,

        ["g_cd"] = CdIridescent,
        ["g_folder"] = FolderYellow,
        ["g_addfolder"] = s =>
        {
            using (s.Placed(0.88, 0, 4))
                FolderYellow(s);
            s.Obj(Ell(73, 73, 23, 23), Green, 4);
            s.Line("M 73,62 L 73,84 M 62,73 L 84,73", "#FFFFFF", 8);
        },
        ["g_star"] = StarGold,
        ["g_gear"] = s => s.Obj(WithHoles(GearShape(), Ell(50, 50, 15, 15)), GearPale, 3.4),
        ["g_info"] = s => InfoBall(s, pale: false),
        ["g_help"] = HelpBall,
    };

    /// <summary>The icon key to use on Pardeh Glass (a glossy family), or the key itself.</summary>
    public static string ForGlass(string key, bool settings = false)
    {
        if (settings && GlassIcons.ContainsKey("s_" + key))
            return "s_" + key;
        return GlassIcons.ContainsKey("g_" + key) ? "g_" + key : key;
    }

    /// <summary>The Library row tile of a key ("t_note" ...), the note tile when the concept has none.</summary>
    public static string TileOf(string glyphOrKey)
    {
        var key = Resolve(glyphOrKey);
        if (key == "clock")
            return "t_play"; // the "Recently played" glyph
        return GlassIcons.ContainsKey("t_" + key) ? "t_" + key : key switch { "film" => "t_play", "library" => "t_folder", "picture" => "t_folder", _ => "t_note" };
    }

    // ---- shared pieces ------------------------------------------------------------------------

    /// <summary>The note of the references (beam, two stems, two tilted heads) in a 300 x 350 box.</summary>
    private static Geometry NoteShape() => Group(
        Geo("M 103,92 L 297,2 L 297,240 L 270,240 L 270,76 L 130,140 L 130,300 L 103,300 Z"),
        Ell(70, 305, 68, 44, -18),
        Ell(240, 262, 60, 42, -18));

    /// <summary>The note shape for a large control (Library backdrop): returns the geometry, 300 x 350.</summary>
    public static Geometry BigNote() => NoteShape();

    private static void PaleAt(Sheet s, Geometry geo, double scale, double x, double y)
    {
        using (s.Placed(scale, x, y))
            s.Pale(geo, 0);
    }

    private static readonly IBrush TileShadow = new SolidColorBrush(Color.FromArgb(90, 2, 14, 70));

    private static void Tile(Sheet s, Action<Sheet> glyph)
    {
        var tile = RR(7, 7, 86, 86, 19);
        using (s.G.PushTransform(Matrix.CreateTranslation(0, 3)))
            s.G.DrawGeometry(_tint ? new SolidColorBrush(K(90, 2, 14, 70)) : TileShadow, null, tile);
        s.G.DrawGeometry(null, PenOf("#0B2F8E", 2.6), tile);
        var b = tile.Bounds;
        s.G.DrawGeometry(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(b.Center.X, b.Top, RelativeUnit.Absolute), EndPoint = new RelativePoint(b.Center.X, b.Bottom, RelativeUnit.Absolute),
            GradientStops = { new GradientStop(K("#62AEFF"), 0), new GradientStop(K("#3380EE"), 0.5), new GradientStop(K("#2058CE"), 1) },
        }, null, tile);
        using (s.G.PushGeometryClip(tile))
        {
            // a soft lit corner at the top left and a gentle bevel
            s.G.DrawEllipse(new RadialGradientBrush
            {
                Center = new RelativePoint(0.2, 0.15, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.2, 0.15, RelativeUnit.Relative), RadiusX = RelativeScalar.Parse("80%"), RadiusY = RelativeScalar.Parse("80%"),
                GradientStops = { new GradientStop(Color.FromArgb(120, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1) },
            }, null, new Point(20, 20), 70, 70);
            s.G.DrawRectangle(Solid("#FFFFFF", 28), null, new Rect(7, 7, 86, 30));
        }

        s.G.DrawGeometry(null, PenOf("#CDE6FF", 1.7), RR(8.6, 8.6, 82.8, 82.8, 17.5));
        glyph(s);
    }

    private static void GlyphCd(Sheet s)
    {
        s.Pale(Ell(50, 50, 31, 31), 0);
        s.G.DrawEllipse(null, PenOf("#FFFFFF", 1.8, 150), new Point(50, 50), 23, 23);
        s.G.DrawEllipse(Solid("#4C86DA"), null, new Point(50, 50), 12, 12);
        s.G.DrawEllipse(Solid("#A9CBF6"), null, new Point(50, 50), 8, 8);
        s.G.DrawEllipse(Solid("#2B58B8"), null, new Point(50, 50), 4, 4);
    }

    private static void GlyphTag(Sheet s)
    {
        using (s.Rotated(-40))
        {
            s.Pale(WithHoles(Geo("M 38,22 L 62,22 Q 67,22 67,27 L 67,70 Q 67,76 62,76 L 38,76 Q 33,76 33,70 L 33,27 Q 33,22 38,22 Z"), Ell(50,33,4.5,4.5)), 0);
            s.Line("M 40,50 L 60,50 M 40,60 L 60,60", "#4C86DA", 3.4);
        }
    }

    private static void GlyphBars(Sheet s)
    {
        s.Pale(RR(24, 27, 54, 11, 5.5), 0);
        s.Pale(RR(24, 45, 54, 11, 5.5), 0);
        s.Pale(RR(24, 63, 44, 11, 5.5), 0);
        s.Pale(Ell(26, 32.5, 8, 7), 0);
        s.Pale(Ell(26, 68.5, 8, 7), 0);
    }

    private static void GlyphClock(Sheet s)
    {
        s.Pale(Ell(50, 50, 31, 31), 0);
        s.G.DrawEllipse(Solid("#3F7BDB"), null, new Point(50, 50), 23.5, 23.5);
        s.G.DrawEllipse(Solid("#E9F4FF"), null, new Point(50, 50), 21, 21);
        s.Line("M 50,50 L 50,34 M 50,50 L 62,57", "#2E64C8", 4.4);
    }

    private static void GlyphRadio(Sheet s)
    {
        s.Pale(Ell(50, 50, 7, 7), 0);
        s.G.DrawGeometry(null, PenOf("#EAF5FF", 6), Geo("M 33,34 Q 22,50 33,66 M 67,34 Q 78,50 67,66"));
        s.G.DrawGeometry(null, PenOf("#CFE6FC", 5), Geo("M 20,24 Q 4,50 20,76 M 80,24 Q 96,50 80,76"));
    }

    // ---- pale glossy objects ---------------------------------------------------------------------

    // ---- loose icons ---------------------------------------------------------------------------------

    private static void FolderYellow(Sheet s)
    {
        s.Obj(Geo("M 8,24 Q 8,19 13,19 L 37,19 Q 41,19 44,23 L 48,29 L 87,29 Q 92,29 92,34 L 92,80 Q 92,85 87,85 L 13,85 Q 8,85 8,80 Z"), new Pal("#F7D272", "#E8B243", "#C98A1E", "#9A6612"), 3.2, gloss: false, halo: false);
        s.Obj(Geo("M 7,47 Q 7,42 13,42 L 91,42 Q 97,42 96,48 L 91,80 Q 90,86 84,86 L 13,86 Q 7,86 8,80 Z"), new Pal("#FFEFB4", "#FBD477", "#EDB442", "#B27C1C"), 3.2, halo: false);
    }

    private static void StarGold(Sheet s)
    {
        var points = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            var a = -Math.PI / 2 + i * Math.PI / 5;
            var r = i % 2 == 0 ? 46 : 21;
            points.Add(FormattableString.Invariant($"{(i == 0 ? "M" : "L")} {50 + r * Math.Cos(a)},{54 + r * Math.Sin(a)}"));
        }

        var star = Geo(string.Join(' ', points) + " Z");
        s.Obj(star, new Pal("#FFF4A6", "#FFC72C", "#E08A12", "#B26A0A"), 4.2, halo: false);
        s.Fill(Geo("M 50,20 L 55,38 L 50,48 L 45,38 Z"), "#FFFFFF", 110);
    }

    private static void CdIridescent(Sheet s)
    {
        var disc = Ell(50, 50, 44, 44);
        s.Obj(disc, new Pal("#FFFFFF", "#EAF0FC", "#B8C9EC", "#6C86C4"), 3.2, gloss: false, halo: false);
        using (s.G.PushGeometryClip(disc))
        {
            s.Fill(Geo("M 50,50 L 94,28 L 78,6 Z"), "#B9A2F0", 140);
            s.Fill(Geo("M 50,50 L 96,60 L 90,86 Z"), "#8FC9FF", 120);
            s.Fill(Geo("M 50,50 L 6,72 L 22,94 Z"), "#C6A8F4", 130);
            s.Fill(Geo("M 50,50 L 4,36 L 12,12 Z"), "#9ED6FF", 110);
            s.Fill(Geo("M 50,50 L 40,4 L 62,4 Z"), "#FFD2EC", 90);
        }

        s.G.DrawEllipse(null, PenOf("#FFFFFF", 2, 190), new Point(50, 50), 31, 31);
        s.Obj(Ell(50, 50, 19, 19), new Pal("#B8D4F8", "#6C9AE0", "#3E6FC4", "#2C58A8"), 2.6, gloss: false, halo: false);
        s.Fill(Ell(50, 50, 7, 7), "#2D58B4");
    }

    private static Geometry GearShape()
    {
        var points = new List<string>();
        for (var k = 0; k < 8; k++)
        {
            foreach (var (deg, r) in new[] { (-14.0, 33.0), (-9.0, 45.0), (9.0, 45.0), (14.0, 33.0) })
            {
                var a = (k * 45 + deg - 90) * Math.PI / 180;
                points.Add(FormattableString.Invariant($"{(points.Count == 0 ? "M" : "L")} {50 + r * Math.Cos(a)},{50 + r * Math.Sin(a)}"));
            }
        }

        return Geo(string.Join(' ', points) + " Z");
    }

    private static void InfoBall(Sheet s, bool pale)
    {
        if (pale)
        {
            s.Pale(Ell(50, 50, 42, 42), 0);
            s.Fill(Ell(50, 28, 7, 7), "#1D3F94");
            s.Fill(Geo("M 40,42 L 57,42 L 57,70 L 63,70 L 63,78 L 37,78 L 37,70 L 43,70 L 43,50 L 40,50 Z"), "#1D3F94");
            return;
        }

        s.Obj(Ell(50, 50, 43, 43), BallBlue, 3.4, halo: false);
        s.Fill(Ell(50, 27, 7.5, 7.5), "#FFFFFF");
        s.Fill(Geo("M 40,41 L 57,41 L 57,69 L 63,69 L 63,78 L 37,78 L 37,69 L 43,69 L 43,50 L 40,50 Z"), "#FFFFFF");
    }

    private static void HelpBall(Sheet s)
    {
        s.Obj(Ell(50, 50, 43, 43), BallBlue, 3.4, halo: false);
        s.Line("M 35,36 Q 35,21 50,21 Q 65,21 65,35 Q 65,45 52,52 Q 49,54 49,60", "#FFFFFF", 9.5);
        s.Fill(Ell(49, 75, 6, 6), "#FFFFFF");
    }

    // ---- browse (large) icons ------------------------------------------------------------------------

    private static void BrowseVideo(Sheet s)
    {
        s.Obj(RR(8, 10, 84, 80, 8), new Pal("#1E55C4", "#1646AE", "#0D3190", "#58C0FF"), 3.2, gloss: false, halo: false);
        for (var i = 0; i < 6; i++)
        {
            s.Fill(RR(13, 17 + i * 12.5, 7, 7, 2), "#CFE8FF");
            s.Fill(RR(80, 17 + i * 12.5, 7, 7, 2), "#CFE8FF");
        }

        var frame = new Pal("#E4F4FF", "#9ED2FB", "#5AA6F0", "#CBE6FF");
        s.Obj(RR(26, 17, 48, 29, 3), frame, 2.4, halo: false);
        s.Obj(RR(26, 54, 48, 29, 3), frame, 2.4, halo: false);
    }

    private static void BrowsePictures(Sheet s)
    {
        s.Obj(RR(6, 12, 88, 76, 5), new Pal("#F4F8FF", "#D6E2F6", "#A9BEE4", "#7C97CC"), 2.6, gloss: false, halo: false);
        var inner = RR(14, 20, 72, 60, 2);
        s.FillV(inner, "#6FD0FF", "#2E8BEA");
        using (s.G.PushGeometryClip(inner))
        {
            s.Fill(Geo("M 20,34 Q 26,26 34,30 Q 40,24 48,32 L 20,36 Z"), "#FFFFFF", 235);
            s.Fill(Geo("M 62,34 L 69,27 L 76,34 L 69,41 Z"), "#FFFFFF", 240);
            s.FillV(Geo("M 12,82 L 12,66 L 32,44 L 52,68 L 62,58 L 88,82 Z"), "#8FD84C", "#2F9330");
            s.FillV(Geo("M 40,82 L 64,56 L 88,82 Z"), "#7ACB3E", "#3A9E30");
        }
    }

    private static void BrowseRadio(Sheet s)
    {
        s.G.DrawGeometry(null, PenOf("#BFDDFB", 3.4), Geo("M 28,26 L 68,12"));
        s.Obj(RR(7, 28, 86, 60, 8), new Pal("#E4F1FF", "#A9CEF6", "#6EA2E6", "#4A7EC8"), 3, halo: false);
        var grille = RR(15, 38, 30, 40, 3);
        s.FillV(grille, "#7FA7DE", "#5D88CE");
        s.G.DrawGeometry(null, PenOf("#4C78BC", 1.2), Geo("M 15,46 L 45,46 M 15,54 L 45,54 M 15,62 L 45,62 M 15,70 L 45,70 M 24,38 L 24,78 M 33,38 L 33,78"));
        s.G.DrawGeometry(null, PenOf("#4C78BC", 2), grille);
        s.Obj(RR(54, 38, 28, 8, 4), new Pal("#F2F8FF", "#CFE3FA", "#9EC0EE", "#5A86C6"), 2, gloss: false, halo: false);
        s.Obj(Ell(68, 63, 11, 11), new Pal("#F2F8FF", "#BFD8F7", "#7FA9E4", "#4A74B8"), 2.4, halo: false);
        s.Fill(Geo("M 65,58 L 74,63 L 65,68 Z"), "#4A74B8");
    }

    private static void BrowsePlaylist(Sheet s)
    {
        s.Obj(RR(14, 8, 72, 84, 8), new Pal("#F2F8FF", "#C6DDF8", "#8FB6EC", "#5A86C6"), 3, halo: false);
        s.Fill(RR(26, 24, 48, 9, 4.5), "#3C7BDD");
        s.Fill(RR(26, 42, 48, 9, 4.5), "#3C7BDD");
        s.Fill(RR(26, 60, 30, 9, 4.5), "#3C7BDD");
        s.Obj(Geo("M 60,58 L 84,72 L 60,86 Z"), new Pal("#9BE06A", "#43B53A", "#1F8226", "#14601A"), 3, halo: false);
    }

    /// <summary>Recorded TV: a glossy blue TV set (aerial, glass screen, knobs, feet) with a red record dot on the screen.</summary>
    private static void BrowseTv(Sheet s)
    {
        s.G.DrawGeometry(null, PenOf("#BFDDFB", 3.4), Geo("M 36,22 L 26,8 M 64,22 L 74,8"));
        s.Obj(RR(20, 80, 14, 9, 3), new Pal("#E4F1FF", "#A9CEF6", "#6EA2E6", "#4A7EC8"), 2, gloss: false, halo: false);
        s.Obj(RR(66, 80, 14, 9, 3), new Pal("#E4F1FF", "#A9CEF6", "#6EA2E6", "#4A7EC8"), 2, gloss: false, halo: false);
        s.Obj(RR(6, 20, 88, 64, 10), new Pal("#E4F1FF", "#A9CEF6", "#6EA2E6", "#4A7EC8"), 3, halo: false);
        var screen = RR(14, 28, 56, 48, 6);
        s.FillV(screen, "#3F8BEA", "#0E3A9E");
        using (s.G.PushGeometryClip(screen))
            s.Fill(Geo("M 14,28 L 52,28 L 14,62 Z"), "#FFFFFF", 70);
        s.G.DrawGeometry(null, PenOf("#2F5EB8", 2.2), screen);
        s.Obj(Ell(78, 40, 6, 6), new Pal("#F2F8FF", "#CFE3FA", "#9EC0EE", "#5A86C6"), 1.8, gloss: false, halo: false);
        s.Obj(Ell(78, 56, 6, 6), new Pal("#F2F8FF", "#CFE3FA", "#9EC0EE", "#5A86C6"), 1.8, gloss: false, halo: false);
        s.G.DrawGeometry(null, PenOf("#4C78BC", 1.8), Geo("M 74,67 L 82,67 M 74,72 L 82,72"));
        s.Obj(Ell(42, 52, 13, 13), Red, 2.6, halo: false);
    }

    private static void GlyphTv(Sheet s)
    {
        s.G.DrawGeometry(null, PenOf("#EAF5FF", 4), Geo("M 38,26 L 30,16 M 62,26 L 70,16"));
        s.Pale(WithHoles(RR(14, 28, 72, 52, 9), RR(22, 36, 42, 36, 3)), 0);
        s.Pale(Ell(75, 45, 4.5, 4.5), 0);
        s.Pale(Ell(75, 61, 4.5, 4.5), 0);
        s.Obj(Ell(43, 54, 10, 10), Red, 2.2, halo: false);
    }

    // ---- settings icons ----------------------------------------------------------------------------------

    private static void SettingsVideo(Sheet s)
    {
        s.Pale(WithHoles(RR(6, 20, 88, 62, 10), RR(14, 28, 58, 46, 3)), 0);
        s.Pale(RR(78, 29, 9, 8, 2), 0);
        s.Pale(RR(78, 46, 9, 8, 2), 0);
        s.Pale(RR(78, 63, 9, 8, 2), 0);
        s.Pale(RR(21, 60, 18, 7, 2), 0);
    }

    private static void SettingsSpeaker(Sheet s)
    {
        s.Pale(Geo("M 6,38 L 26,38 L 50,14 L 50,86 L 26,62 L 6,62 Z"), 3);
        s.G.DrawGeometry(null, PenOf("#0A2F8A", 8, 90), Geo("M 64,40 Q 72,50 64,60"));
        s.G.DrawGeometry(null, PenOf("#EAF5FF", 6.5), Geo("M 64,38 Q 74,50 64,62"));
        s.G.DrawGeometry(null, PenOf("#CFE6FC", 6), Geo("M 74,26 Q 92,50 74,74"));
    }

    private static void SettingsWheel(Sheet s)
    {
        s.Pale(Ell(50, 50, 42, 42), 0);
        s.G.DrawEllipse(null, PenOf("#5D8FDC", 3), new Point(50, 50), 31, 31);
        s.Line("M 27,27 L 73,73 M 73,27 L 27,73 M 50,18 L 50,82", "#1E3F91", 4.4);
        s.G.DrawEllipse(Solid("#1E3F91"), null, new Point(50, 50), 7, 7);
        s.G.DrawEllipse(Solid("#CFE6FC"), null, new Point(50, 50), 3, 3);
    }

    private static void SettingsBrush(Sheet s)
    {
        using (s.Rotated(40))
        {
            s.Pale(RR(44, 2, 12, 50, 6), 0);
            s.Pale(RR(40, 47, 20, 17, 3), 0);
            s.Pale(Geo("M 41,64 L 59,64 Q 67,84 50,99 Q 33,84 41,64 Z"), 0);
        }
    }

    private static void SettingsPages(Sheet s)
    {
        s.Pale(WithHoles(RR(6, 10, 62, 52, 6), RR(12, 21, 50, 35, 2)), 0);
        s.Pale(WithHoles(RR(32, 38, 62, 52, 6), RR(38, 49, 50, 35, 2)), 0);
    }

    private static void SettingsEqualizer(Sheet s)
    {
        double[] knobs = [26, 50, 36];
        for (var i = 0; i < 3; i++)
        {
            var x = 22 + i * 28;
            s.Pale(RR(x - 4, 8, 8, 84, 4), 0);
            s.Pale(RR(x - 13, knobs[i], 26, 15, 6), 0);
        }
    }
}
