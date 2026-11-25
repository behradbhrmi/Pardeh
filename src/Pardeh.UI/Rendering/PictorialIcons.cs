using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Pardeh.UI.Rendering;

/// <summary>
/// The pictorial icon set of the XP-era looks (Glass and Royal/Luna phone themes): small glossy objects drawn in code on a
/// 100 x 100 sheet (a gradient body, a rim, a light halo so they stand on dark and on pale cards, a soft gloss and a
/// shadow), in several colours: a yellow folder, a gold star, a silver-blue CD, a blue note, a film strip ... They are
/// vectors, so they are drawn at the device's scale and stay crisp. All path data is built with the invariant culture.
/// </summary>
internal static partial class PictorialIcons
{
    private readonly record struct Pal(string Top, string Mid, string Bottom, string Rim);

    private static readonly Pal Blue = new("#A9DBFF", "#3F93F2", "#1649B8", "#0B2F86");
    private static readonly Pal Silver = new("#FFFFFF", "#D9E7FB", "#93B4E6", "#3F66AD");
    private static readonly Pal Ice = new("#F2FAFF", "#A9D0F8", "#5C93E0", "#1E4FA6");
    private static readonly Pal Gold = new("#FFF6AE", "#FFC62E", "#DA8712", "#A9590A");
    private static readonly Pal Navy = new("#5C8BE0", "#2352B8", "#0B2670", "#06194F");
    private static readonly Pal Green = new("#BDF396", "#4FB93D", "#1F7F22", "#14601A");
    private static readonly Pal Red = new("#FFB9AA", "#EE4B3A", "#A8160F", "#7A0D08");
    private static readonly Pal FolderBack = new("#F2C94E", "#D9A21E", "#B87A0C", "#8A5A08");
    private static readonly Pal FolderFront = new("#FFF3AE", "#F9CF4D", "#E0A41E", "#A8730C");
    private static readonly Pal Wood = new("#FFF3CF", "#F3D58C", "#D6A94C", "#8F6419");
    private static readonly Pal Pencil = new("#FFF09A", "#FFD23A", "#E0A010", "#9A6A06");
    private static readonly Pal Pink = new("#FFD3DC", "#F58FA6", "#D0506E", "#8F2A44");
    private static readonly Pal Steel = new("#F1F5FB", "#B5C4DC", "#6F87B0", "#34507F");
    private static readonly Pal Phone = new("#7C9AD2", "#2C4A86", "#0B1B45", "#061236");

    /// <summary>What the existing line-glyph path data of a row stood for (rows still pass their glyph; a pictorial theme swaps it).</summary>
    private static readonly Dictionary<string, string> GlyphKeys = new()
    {
        ["M 8,15 L 8,5 L 15,3 L 15,13 M 5.5,15 a 2.5,2 0 1,0 5,0 a 2.5,2 0 1,0 -5,0 M 12.5,13 a 2.5,2 0 1,0 5,0 a 2.5,2 0 1,0 -5,0"] = "note",
        ["M 3,17 L 3,13 L 13,3 L 17,7 L 7,17 Z M 11,5 L 15,9"] = "brush",
        ["M 4,16 L 4,11 M 8,16 L 8,6 M 12,16 L 12,9 M 16,16 L 16,3"] = "equalizer", // Settings > Visualization
        ["M 3,3 L 9,3 L 9,9 L 3,9 Z M 11,3 L 17,3 L 17,9 L 11,9 Z M 3,11 L 9,11 L 9,17 L 3,17 Z M 11,11 L 17,11 L 17,17 L 11,17 Z"] = "pages",
        ["M 4,4 L 16,4 L 16,16 L 4,16 Z M 8.5,7.5 L 12.5,10 L 8.5,12.5 Z"] = "player",
        ["M 10,3 A 7,7 0 1 0 10,17 Q 12,17 12,15 Q 12,13 14,13 L 15,13 A 7,7 0 0 0 10,3 Z"] = "palette",
        ["M 3.5,8 L 6.5,8 L 10,5 L 10,15 L 6.5,12 L 3.5,12 Z M 13,8 Q 14.5,10 13,12 M 15,6.5 Q 17.5,10 15,13.5"] = "speaker",
        ["M 3,5 L 13,5 L 13,15 L 3,15 Z M 13,9 L 17,6 L 17,14 L 13,11"] = "film",
        ["M 3,5 L 8,5 L 9.5,7 L 17,7 L 17,15 L 3,15 Z"] = "library",
        ["M 10,3 A 7,7 0 1 0 10.01,3 Z M 10,9 L 10,14 M 10,6 L 10,6.4"] = "info",
        ["M 3,4 L 17,4 L 17,16 L 3,16 Z M 3,13 L 8,9 L 12,13 L 14,11 L 17,14"] = "picture",
        ["M 10,7 m -3,0 a 3,3 0 1,0 6,0 a 3,3 0 1,0 -6,0 M 4,17 Q 4,12 10,12 Q 16,12 16,17"] = "person",
        ["M 10,10 m -7,0 a 7,7 0 1,0 14,0 a 7,7 0 1,0 -14,0 M 10,10 m -2,0 a 2,2 0 1,0 4,0 a 2,2 0 1,0 -4,0"] = "cd",
        ["M 4,6 L 16,6 M 4,10 L 16,10 M 4,14 L 11,14"] = "tag",
        ["M 3,6 L 13,6 M 3,10 L 13,10 M 3,14 L 9,14 M 12,12 L 17,14.5 L 12,17 Z"] = "list",
        ["M 3,6 L 13,6 M 3,10 L 13,10 M 3,14 L 9,14"] = "note",
        ["M 3,5 L 8,5 L 9.5,7 L 17,7 L 17,15 L 3,15 Z M 10,9 L 10,13 M 8,11 L 12,11"] = "addfolder",
        ["M 10,3 L 12,8 L 17,8 L 13,11.5 L 14.5,17 L 10,13.5 L 5.5,17 L 7,11.5 L 3,8 L 8,8 Z"] = "star",
        ["M 10,4 L 10,10 L 14,12 M 10,10 m -7,0 a 7,7 0 1,0 14,0 a 7,7 0 1,0 -14,0"] = "newnote",
        ["M 7,4 L 17,10 L 7,16 Z"] = "clock",
        ["M 10,10 m -1.2,0 a 1.2,1.2 0 1,0 2.4,0 a 1.2,1.2 0 1,0 -2.4,0 M 6.5,6.5 Q 3.5,10 6.5,13.5 M 13.5,6.5 Q 16.5,10 13.5,13.5"] = "radio",
    };

    private static readonly Dictionary<string, Action<Sheet>> Icons = new()
    {
        ["note"] = s => Note(s, Blue),
        ["person"] = Person,
        ["cd"] = Cd,
        ["tag"] = Tag,
        ["list"] = s => ListPaper(s),
        ["star"] = Star,
        ["clock"] = Clock,
        ["film"] = Film,
        ["picture"] = Picture,
        ["folder"] = s => Folder(s),
        ["addfolder"] = AddFolder,
        ["library"] = LibraryFolder,
        ["phone"] = PhoneMusic,
        ["radio"] = Radio,
        ["brush"] = Brush,
        ["palette"] = Palette,
        ["pages"] = Pages,
        ["player"] = PlayDisc,
        ["speaker"] = Speaker,
        ["equalizer"] = Equalizer,
        ["info"] = Info,
        ["gear"] = Gear,
        ["search"] = Magnifier,
        ["trophy"] = Trophy,
        ["subtitle"] = Subtitle,
        ["aspect"] = Aspect,
        ["remove"] = Remove,
        ["pencil"] = PencilIcon,
        ["playnext"] = s => ListWithBadge(s, play: true),
        ["queue"] = s => ListWithBadge(s, play: false),
        ["newnote"] = NewNote,
        ["heart"] = Heart,
        ["heartoff"] = HeartOff,
        ["sort"] = Sort,
        ["filter"] = Funnel,
        ["lyrics"] = Lyrics,    };

    /// <summary>The icon keys of the set (used by the icon contact sheet).</summary>
    public static IReadOnlyCollection<string> Keys => [.. Icons.Keys, .. GlassIcons.Keys];

    /// <summary>The key of an icon given a key ("@note"), or the line glyph a row used before (an unknown glyph is a note).</summary>
    public static string Resolve(string glyphOrKey)
    {
        if (Icons.ContainsKey(glyphOrKey) || GlassIcons.ContainsKey(glyphOrKey))
            return glyphOrKey;
        if (glyphOrKey.StartsWith('@'))
            return Icons.ContainsKey(glyphOrKey[1..]) || GlassIcons.ContainsKey(glyphOrKey[1..]) ? glyphOrKey[1..] : "note";
        return GlyphKeys.GetValueOrDefault(glyphOrKey, "note");
    }

    /// <summary>The icon for a row: a control of the given logical size (vector, so it is drawn at the device scale).</summary>
    public static Control Create(string glyphOrKey, double size, bool glass = false) => new PictorialIcon(Resolve(glyphOrKey), glass)
    {
        Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
    };

    /// <summary>Draws icon <paramref name="key"/> into <paramref name="box"/>.</summary>
    public static void Draw(DrawingContext g, string key, Rect box, bool shadow = true, bool glass = false)
    {
        if (!Icons.TryGetValue(key, out var draw) && !GlassIcons.TryGetValue(key, out draw))
            draw = Icons["note"];
        // The Glass families (t_ b_ s_ g_) follow the Glass skin colour (GlassPalette); the shared XP-era icons never do.
        var previous = _tint;
        _tint = GlassPalette.Active && (glass || IsGlassFamily(key)); // glass: a shared icon drawn by the Glass look itself (the tab's CD)
        if (_tint && _cacheTint != GlassPalette.Current.Key)
        {
            SolidCache.Clear();
            PenCache.Clear();
            _cacheTint = GlassPalette.Current.Key;
        }

        try
        {
            using (g.PushTransform(Matrix.CreateScale(box.Width / 100, box.Height / 100) * Matrix.CreateTranslation(box.X, box.Y)))
            {
                var sheet = new Sheet(g);
                if (shadow)
                    g.DrawEllipse(_tint ? new RadialGradientBrush { GradientStops = { new GradientStop(K(110, 0, 8, 40), 0), new GradientStop(K(0, 0, 8, 40), 1) } } : ShadowBrush, null, new Point(50, 95), 34, 4.5);
                draw(sheet);
            }
        }
        finally
        {
            _tint = previous;
        }
    }

    private static bool IsGlassFamily(string key) => key.Length > 2 && key[1] == '_' && key[0] is 't' or 'b' or 's' or 'g';

    /// <summary>True while a Glass-family icon is drawn in a non-default skin colour: every colour below goes through <see cref="GlassPalette"/>.</summary>
    [ThreadStatic]
    private static bool _tint;

    private static string? _cacheTint;

    private static Color K(string hex) => _tint ? GlassPalette.Parse(hex) : Color.Parse(hex);

    private static Color K(byte a, byte r, byte g, byte b) => _tint ? GlassPalette.Map(Color.FromArgb(a, r, g, b)) : Color.FromArgb(a, r, g, b);

    // ---- Drawing helpers --------------------------------------------------------------------

    private static readonly Dictionary<string, Geometry> GeoCache = [];
    private static readonly Dictionary<string, IBrush> SolidCache = [];
    private static readonly Dictionary<(string, double), IPen> PenCache = [];

    private static readonly IBrush ShadowBrush = new RadialGradientBrush
    {
        GradientStops = { new GradientStop(Color.FromArgb(110, 0, 8, 40), 0), new GradientStop(Color.FromArgb(0, 0, 8, 40), 1) },
    };

    private static readonly IBrush GlossBrush = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.FromArgb(200, 255, 255, 255), 0), new GradientStop(Color.FromArgb(50, 255, 255, 255), 1) },
    };

    private static readonly IPen HaloPen = new Pen(new SolidColorBrush(Color.FromArgb(175, 232, 245, 255)), 8.4, lineJoin: PenLineJoin.Round);

    private static Geometry Geo(string data)
    {
        if (!GeoCache.TryGetValue(data, out var geometry))
            GeoCache[data] = geometry = Geometry.Parse(data);
        return geometry;
    }

    private static IBrush Solid(string color, byte alpha = 255)
    {
        var key = alpha == 255 ? color : color + alpha.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (_tint)
            key = "~" + key; // tinted entries are cleared whenever the skin colour changes (see Draw)
        if (!SolidCache.TryGetValue(key, out var brush))
        {
            var c = K(color);
            SolidCache[key] = brush = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
        }

        return brush;
    }

    private static IPen PenOf(string color, double width)
    {
        var key = (_tint ? "~" + color : color, width);
        if (!PenCache.TryGetValue(key, out var pen))
            PenCache[key] = pen = new Pen(Solid(color), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        return pen;
    }

    private static IPen PenOf(string color, double width, byte alpha) => new Pen(Solid(color, alpha), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

    private static string F(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A rounded rectangle.</summary>
    private static Geometry RR(double x, double y, double w, double h, double r) => Geo(FormattableString.Invariant(
        $"M {x + r},{y} L {x + w - r},{y} A {r},{r} 0 0 1 {x + w},{y + r} L {x + w},{y + h - r} A {r},{r} 0 0 1 {x + w - r},{y + h} L {x + r},{y + h} A {r},{r} 0 0 1 {x},{y + h - r} L {x},{y + r} A {r},{r} 0 0 1 {x + r},{y} Z"));

    /// <summary>An ellipse, optionally tilted by <paramref name="rot"/> degrees.</summary>
    private static Geometry Ell(double cx, double cy, double rx, double ry, double rot = 0)
    {
        var a = rot * Math.PI / 180;
        double dx = Math.Cos(a) * rx, dy = Math.Sin(a) * rx;
        return Geo(FormattableString.Invariant($"M {cx - dx},{cy - dy} A {rx},{ry} {rot} 1 1 {cx + dx},{cy + dy} A {rx},{ry} {rot} 1 1 {cx - dx},{cy - dy} Z"));
    }

    private static Geometry Group(params Geometry[] parts) => new GeometryGroup { FillRule = FillRule.NonZero, Children = [.. parts] };

    /// <summary>The first shape with the others cut out of it (holes).</summary>
    private static Geometry WithHoles(params Geometry[] parts) => new GeometryGroup { FillRule = FillRule.EvenOdd, Children = [.. parts] };

    private sealed class Sheet(DrawingContext g)
    {
        public DrawingContext G { get; } = g;

        /// <summary>A glossy object: a light halo, the rim, a vertical body gradient (one gradient over all of its pieces, so they join) and a gloss on the upper half.</summary>
        public void Obj(Geometry geo, Pal p, double rim = 4.4, bool gloss = true, bool halo = true)
        {
            var b = geo.Bounds;
            if (halo)
                G.DrawGeometry(null, _tint ? new Pen(new SolidColorBrush(K(175, 232, 245, 255)), 8.4, lineJoin: PenLineJoin.Round) : HaloPen, geo);
            G.DrawGeometry(null, new Pen(Solid(p.Rim), rim, lineJoin: PenLineJoin.Round), geo);
            G.DrawGeometry(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(b.Center.X, b.Top, RelativeUnit.Absolute), EndPoint = new RelativePoint(b.Center.X, b.Bottom, RelativeUnit.Absolute),
                GradientStops = { new GradientStop(K(p.Top), 0), new GradientStop(K(p.Mid), 0.5), new GradientStop(K(p.Bottom), 1) },
            }, null, geo);
            if (gloss)
                Shine(geo);
        }

        public void Shine(Geometry geo)
        {
            var b = geo.Bounds;
            using (G.PushGeometryClip(geo))
                G.DrawRectangle(GlossBrush, null, new Rect(b.X - 1, b.Y - 1, b.Width + 2, b.Height * 0.46 + 1));
        }

        /// <summary>A pale glossy glyph (the Glass families): a soft blue shadow, a pale vertical gradient, a light rim and a gloss; <paramref name="round"/> thickens and rounds it.</summary>
        public void Pale(Geometry geo, double round = 0)
        {
            var b = geo.Bounds;
            var fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(b.Center.X, b.Top, RelativeUnit.Absolute), EndPoint = new RelativePoint(b.Center.X, b.Bottom, RelativeUnit.Absolute),
                GradientStops = { new GradientStop(K("#F6FBFF"), 0), new GradientStop(K("#C6E0FB"), 0.55), new GradientStop(K("#8DB9F0"), 1) },
            };
            var shadow = Solid("#0A2F8A", 120);
            using (G.PushTransform(Matrix.CreateTranslation(0, 2.6)))
                G.DrawGeometry(shadow, round > 0 ? new Pen(shadow, round, lineJoin: PenLineJoin.Round) : null, geo);
            G.DrawGeometry(fill, round > 0 ? new Pen(fill, round, lineJoin: PenLineJoin.Round) : null, geo);
            G.DrawGeometry(null, PenOf("#FFFFFF", 1.2, 170), geo);
            using (G.PushGeometryClip(geo))
                G.DrawRectangle(Solid("#FFFFFF", 70), null, new Rect(b.X - 1, b.Y - 1, b.Width + 2, b.Height * 0.4 + 1));
        }

        public void Fill(Geometry geo, string color, byte alpha = 255) => G.DrawGeometry(Solid(color, alpha), null, geo);

        public void FillV(Geometry geo, string top, string bottom)
        {
            var b = geo.Bounds;
            G.DrawGeometry(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(b.Center.X, b.Top, RelativeUnit.Absolute), EndPoint = new RelativePoint(b.Center.X, b.Bottom, RelativeUnit.Absolute),
                GradientStops = { new GradientStop(K(top), 0), new GradientStop(K(bottom), 1) },
            }, null, geo);
        }

        public void Line(string data, string color, double width) => G.DrawGeometry(null, PenOf(color, width), Geo(data));

        /// <summary>A stroke with a pale halo under it (wave arcs, a handle).</summary>
        public void Halo(string data, string color, double width)
        {
            G.DrawGeometry(null, PenOf("#E8F5FF", width + 5), Geo(data));
            G.DrawGeometry(null, PenOf(color, width), Geo(data));
        }

        public IDisposable Rotated(double degrees) => G.PushTransform(Matrix.CreateTranslation(-50, -50) * Matrix.CreateRotation(degrees * Math.PI / 180) * Matrix.CreateTranslation(50, 50));

        public IDisposable Placed(double scale, double x, double y) => G.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(x, y));
    }

    // ---- The icons --------------------------------------------------------------------------

    private static void Note(Sheet s, Pal p) => s.Obj(Group(Geo("M 38,76 L 38,24 L 90,10 L 90,66 L 80,66 L 80,31 L 48,39 L 48,76 Z"), Ell(33, 77, 15, 11, -20), Ell(75, 69, 15, 11, -20)), p);

    private static void Person(Sheet s)
    {
        s.Obj(Geo("M 14,92 Q 14,56 50,56 Q 86,56 86,92 Z"), Silver);
        s.Obj(Ell(50, 30, 18, 19), Silver);
    }

    private static void Cd(Sheet s)
    {
        var disc = Ell(50, 50, 43, 43);
        s.Obj(disc, Ice);
        s.G.DrawEllipse(null, PenOf("#FFFFFF", 2.4), new Point(50, 50), 32, 32);
        using (s.G.PushGeometryClip(disc))
        {
            s.Fill(Geo("M 50,50 L 4,26 L 28,2 Z"), "#FFFFFF", 120);
            s.Fill(Geo("M 50,50 L 96,74 L 72,98 Z"), "#FFFFFF", 80);
        }

        s.Obj(Ell(50, 50, 15, 15), Navy, 3, gloss: false, halo: false);
        s.Fill(Ell(50, 50, 6.5, 6.5), "#EAF4FF");
    }

    private static void Tag(Sheet s)
    {
        s.Obj(WithHoles(Geo("M 8,50 L 34,22 Q 36,20 40,20 L 84,20 Q 91,20 91,27 L 91,73 Q 91,80 84,80 L 40,80 Q 36,80 34,78 Z"), Ell(30, 50, 7.5, 7.5)), Ice);
        s.Line("M 46,38 L 80,38 M 46,50 L 80,50 M 46,62 L 68,62", "#2D63C8", 5);
    }

    private static void ListPaper(Sheet s, double y = 16)
    {
        s.Obj(RR(12, y, 76, 68, 9), Silver);
        for (var i = 0; i < 3; i++)
        {
            var row = y + 18 + i * 17;
            s.G.DrawEllipse(Solid("#2F6FD8"), null, new Point(28, row), 5, 5);
            s.Fill(RR(40, row - 3.5, 38, 7, 3.5), "#2F6FD8");
        }
    }

    private static void Star(Sheet s)
    {
        var points = new List<string>();
        for (var i = 0; i < 10; i++)
        {
            var a = -Math.PI / 2 + i * Math.PI / 5;
            var r = i % 2 == 0 ? 46 : 20;
            points.Add(FormattableString.Invariant($"{(i == 0 ? "M" : "L")} {50 + r * Math.Cos(a)},{53 + r * Math.Sin(a)}"));
        }

        s.Obj(Geo(string.Join(' ', points) + " Z"), Gold, 4.4);
    }

    private static void Clock(Sheet s)
    {
        s.Obj(Ell(50, 50, 43, 43), Gold);
        s.Obj(Ell(50, 50, 32, 32), new Pal("#FFFFFF", "#F2F7FF", "#C4D6F2", "#B8760C"), 2.6, gloss: false, halo: false);
        s.Line("M 50,18 L 50,24 M 50,76 L 50,82 M 18,50 L 24,50 M 76,50 L 82,50", "#6E86B8", 4);
        s.Line("M 50,50 L 50,28 M 50,50 L 66,60", "#1B3E8F", 6.5);
        s.G.DrawEllipse(Solid("#1B3E8F"), null, new Point(50, 50), 5, 5);
    }

    private static void Film(Sheet s)
    {
        s.Obj(RR(12, 8, 76, 84, 8), Navy);
        for (var i = 0; i < 5; i++)
        {
            s.Fill(RR(17, 15 + i * 15.5, 9, 8, 2), "#DCEBFF");
            s.Fill(RR(74, 15 + i * 15.5, 9, 8, 2), "#DCEBFF");
        }

        var frame = new Pal("#E6F4FF", "#8FC8FF", "#3C8AE6", "#BFE0FF");
        s.Obj(RR(32, 15, 36, 33, 3), frame, 2.6, halo: false);
        s.Obj(RR(32, 52, 36, 33, 3), frame, 2.6, halo: false);
    }

    private static void Picture(Sheet s)
    {
        s.Obj(RR(8, 15, 84, 70, 7), Silver);
        var inner = RR(16, 23, 68, 54, 2);
        s.FillV(inner, "#8EDCFF", "#3F9BF0");
        using (s.G.PushGeometryClip(inner))
        {
            s.FillV(Geo("M 14,78 L 14,60 L 34,40 L 52,62 L 64,50 L 86,70 L 86,78 Z"), "#92D851", "#2E8E2C");
            s.G.DrawEllipse(Solid("#FFFFFF"), null, new Point(68, 36), 7, 7);
        }
    }

    private static void Folder(Sheet s, double lift = 0)
    {
        s.Obj(Geo("M 8,22 Q 8,18 12,18 L 36,18 Q 40,18 43,22 L 47,28 L 86,28 Q 91,28 91,33 L 91,80 Q 91,85 86,85 L 12,85 Q 8,85 8,80 Z"), FolderBack, 4.4);
        s.Obj(Geo("M 6,47 Q 6,41 12,41 L 92,41 Q 98,41 97,47 L 91,82 Q 90,88 84,88 L 12,88 Q 6,88 7,82 Z"), FolderFront, 4.4, halo: false);
    }

    private static void AddFolder(Sheet s)
    {
        using (s.Placed(0.88, 0, 4))
            Folder(s);
        s.Obj(Ell(73, 73, 23, 23), Green, 4);
        s.Line("M 73,62 L 73,84 M 62,73 L 84,73", "#FFFFFF", 8);
    }

    private static void LibraryFolder(Sheet s)
    {
        Folder(s);
        using (s.Placed(0.5, 28, 40))
            Note(s, Blue);
    }

    private static void PhoneMusic(Sheet s)
    {
        s.Obj(RR(27, 3, 46, 94, 9), Phone, 4.4);
        var screen = RR(32, 13, 36, 66, 2);
        s.FillV(screen, "#CFE8FF", "#4E96E8");
        using (s.Placed(0.28, 36, 34))
            Note(s, new Pal("#FFFFFF", "#F4F9FF", "#C7DBF7", "#2D5FB8"));
        s.G.DrawEllipse(Solid("#C9D6EE"), null, new Point(50, 88), 4.5, 4.5);
    }

    private static void Radio(Sheet s)
    {
        s.Halo("M 36,20 Q 28,30 36,40 M 64,20 Q 72,30 64,40", "#3E8DF0", 5);
        s.Halo("M 25,11 Q 12,30 25,49 M 75,11 Q 88,30 75,49", "#3E8DF0", 5);
        s.Obj(Geo("M 46,38 L 54,38 L 68,93 L 58,93 L 50,66 L 42,93 L 32,93 Z"), Blue, 3.6);
        s.Line("M 44,74 L 56,74 M 40,84 L 60,84", "#E8F4FF", 3.6);
        s.Obj(Ell(50, 31, 7, 7), Blue, 3, halo: false);
    }

    private static void Brush(Sheet s)
    {
        using (s.Rotated(40))
        {
            s.Obj(RR(44, 2, 12, 50, 6), Wood, 4);
            s.Obj(RR(40, 47, 20, 17, 3), Silver, 3.6);
            s.Obj(Geo("M 41,64 L 59,64 Q 67,84 50,99 Q 33,84 41,64 Z"), Blue, 3.6);
        }
    }

    private static void Palette(Sheet s)
    {
        var shape = WithHoles(Geo("M 50,9 C 20,9 6,32 8,52 C 10,76 32,92 52,90 C 63,89 63,78 57,72 C 51,64 58,56 68,58 C 87,61 95,50 92,40 C 88,20 72,9 50,9 Z"), Ell(30, 70, 7.5, 6.5));
        s.Obj(shape, Wood, 4.4);
        s.G.DrawEllipse(Solid("#E8452F"), PenOf("#8F2A18", 1.6), new Point(28, 40), 7.5, 7.5);
        s.G.DrawEllipse(Solid("#FFD21E"), PenOf("#9A6A06", 1.6), new Point(48, 25), 7.5, 7.5);
        s.G.DrawEllipse(Solid("#46B03A"), PenOf("#1E6A1A", 1.6), new Point(70, 29), 7.5, 7.5);
        s.G.DrawEllipse(Solid("#2F78E8"), PenOf("#123F94", 1.6), new Point(76, 50), 7.5, 7.5);
    }

    private static void Pages(Sheet s)
    {
        s.Obj(RR(7, 10, 62, 52, 5), Silver, 4);
        s.Fill(RR(11, 14, 54, 10, 2.5), "#3A7BE6");
        s.Obj(RR(31, 36, 62, 54, 5), Silver, 4);
        s.Fill(RR(35, 40, 54, 11, 2.5), "#2F6FD8");
        s.Fill(RR(37, 58, 40, 5, 2.5), "#8FB0E6");
        s.Fill(RR(37, 68, 30, 5, 2.5), "#8FB0E6");
    }

    private static void PlayDisc(Sheet s)
    {
        s.Obj(Ell(50, 50, 43, 43), Blue);
        s.G.DrawGeometry(Solid("#FFFFFF"), PenOf("#FFFFFF", 6), Geo("M 41,33 L 70,50 L 41,67 Z"));
    }

    private static void Speaker(Sheet s)
    {
        s.Halo("M 63,36 Q 73,50 63,64 M 73,24 Q 93,50 73,76", "#2F7DEB", 6.5);
        s.Obj(Geo("M 6,38 L 26,38 L 50,17 L 50,83 L 26,62 L 6,62 Z"), Silver, 4.4);
    }

    private static void Equalizer(Sheet s)
    {
        double[] knobs = [26, 50, 36];
        for (var i = 0; i < 3; i++)
        {
            var x = 22 + i * 28;
            s.Obj(RR(x - 5, 8, 10, 84, 5), Ice, 3.6, halo: i == 0);
            s.Obj(RR(x - 14, knobs[i], 28, 16, 6), Silver, 3.6);
        }
    }

    private static void Info(Sheet s)
    {
        s.Obj(Ell(50, 50, 43, 43), Blue);
        s.Fill(Ell(50, 28, 7.5, 7.5), "#FFFFFF");
        s.Fill(Geo("M 41,42 L 57,42 L 57,70 L 62,70 L 62,78 L 38,78 L 38,70 L 43,70 L 43,50 L 41,50 Z"), "#FFFFFF");
    }

    private static void Gear(Sheet s)
    {
        var points = new List<string>();
        for (var k = 0; k < 8; k++)
        {
            foreach (var (deg, r) in new[] { (-15.0, 34.0), (-8.5, 45.0), (8.5, 45.0), (15.0, 34.0) })
            {
                var a = (k * 45 + deg - 90) * Math.PI / 180;
                points.Add(FormattableString.Invariant($"{(points.Count == 0 ? "M" : "L")} {50 + r * Math.Cos(a)},{50 + r * Math.Sin(a)}"));
            }
        }

        s.Obj(WithHoles(Geo(string.Join(' ', points) + " Z"), Ell(50, 50, 15, 15)), Steel, 4);
    }

    private static void Magnifier(Sheet s)
    {
        s.Halo("M 62,62 L 88,88", "#1A47A6", 13);
        s.G.DrawEllipse(Solid("#CFE8FF", 150), PenOf("#E8F5FF", 17), new Point(40, 40), 26, 26);
        s.G.DrawEllipse(Solid("#CFE8FF", 130), PenOf("#2F7DEB", 9), new Point(40, 40), 26, 26);
        s.Line("M 27,34 Q 31,26 40,25", "#FFFFFF", 4);
    }

    private static void Trophy(Sheet s) => s.Obj(Group(
        Geo("M 24,8 L 76,8 L 73,38 Q 70,64 50,66 Q 30,64 27,38 Z"),
        Geo("M 27,14 Q 5,14 9,34 Q 13,50 31,52 L 30,43 Q 21,41 19,33 Q 17,24 29,23 Z"),
        Geo("M 73,14 Q 95,14 91,34 Q 87,50 69,52 L 70,43 Q 79,41 81,33 Q 83,24 71,23 Z"),
        RR(43, 64, 14, 16, 2),
        Geo("M 28,93 L 33,78 L 67,78 L 72,93 Z")), Gold, 4.4);

    private static void Subtitle(Sheet s)
    {
        s.Obj(Geo("M 8,16 Q 8,9 15,9 L 85,9 Q 92,9 92,16 L 92,60 Q 92,67 85,67 L 48,67 L 28,90 L 30,67 L 15,67 Q 8,67 8,60 Z"), Silver, 4.4);
        s.Fill(RR(20, 24, 36, 7, 3.5), "#2F6FD8");
        s.Fill(RR(62, 24, 18, 7, 3.5), "#2F6FD8");
        s.Fill(RR(20, 40, 18, 7, 3.5), "#2F6FD8");
        s.Fill(RR(44, 40, 36, 7, 3.5), "#2F6FD8");
    }

    private static void Aspect(Sheet s)
    {
        s.Obj(RR(6, 20, 88, 60, 7), Silver, 4.4);
        s.FillV(RR(15, 29, 70, 42, 2), "#5AA6F6", "#1F5FC8");
        s.Line("M 23,45 L 23,37 L 31,37 M 77,45 L 77,37 L 69,37 M 23,55 L 23,63 L 31,63 M 77,55 L 77,63 L 69,63", "#FFFFFF", 4);
    }

    private static void Remove(Sheet s)
    {
        s.Obj(Ell(50, 50, 43, 43), Red);
        s.Line("M 34,34 L 66,66 M 66,34 L 34,66", "#FFFFFF", 9);
    }

    private static void PencilIcon(Sheet s)
    {
        using (s.Rotated(40))
        {
            s.Obj(RR(41, 2, 18, 14, 5), Pink, 3.6);
            s.Obj(RR(41, 14, 18, 58, 2), Pencil, 3.6);
            s.Obj(Geo("M 41,72 L 59,72 L 50,97 Z"), Wood, 3.6, gloss: false);
            s.Fill(Geo("M 46.5,86 L 53.5,86 L 50,97 Z"), "#33405E");
        }
    }

    private static void ListWithBadge(Sheet s, bool play)
    {
        using (s.Placed(0.84, 0, 4))
            ListPaper(s, 12);
        s.Obj(Ell(75, 74, 22, 22), play ? Blue : Green, 4);
        if (play)
            s.G.DrawGeometry(Solid("#FFFFFF"), PenOf("#FFFFFF", 3), Geo("M 68,63 L 87,74 L 68,85 Z"));
        else
            s.Line("M 75,63 L 75,85 M 64,74 L 86,74", "#FFFFFF", 8);
    }

    private static void NewNote(Sheet s)
    {
        using (s.Placed(0.84, 6, 12))
            Note(s, Blue);
        s.Obj(Ell(25, 25, 21, 21), Green, 4);
        s.Line("M 25,14 L 25,36 M 14,25 L 36,25", "#FFFFFF", 8);
    }
}

/// <summary>A pictorial icon as a control (see <see cref="PictorialIcons"/>).</summary>
internal sealed class PictorialIcon(string key, bool glass = false) : Control
{
    // glass: an icon of the Pardeh Glass theme: its blues follow the skin colour (GlassPalette), whichever family the key is from.
    public override void Render(DrawingContext context) => PictorialIcons.Draw(context, key, new Rect(Bounds.Size), glass: glass);
}
