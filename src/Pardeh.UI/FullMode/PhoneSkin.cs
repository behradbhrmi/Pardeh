using System.Text;
using Avalonia;
using Avalonia.Media;
using Pardeh.Skinning.Model;
using Pardeh.Skinning.Wmp;
using static Pardeh.UI.FullMode.LunaArt;

namespace Pardeh.UI.FullMode;

/// <summary>
/// The phone full modes: "Luna Phone", "Fluent Phone", "Ember Phone", "Horizon Phone" and "Pardeh Ten Phone"
/// (keys luna-phone, fluent-phone, ember-phone, horizon-phone, aero10-phone). Portrait-first layouts of the same art
/// language as the desktop full modes, drawn in code: a top bar (logo, page title, a three-dot menu), a large stage,
/// title and artist, a seek bar with times, big transport buttons, shuffle / mute / volume / repeat, and a bottom
/// navigation bar (Now Playing, Library, Playlist, Radio, Settings) instead of the task rail. Every touch target is
/// at least 44 skin pixels. The layout is computed by the skin's script from the view size, so it follows the screen
/// (Android) or the window (Windows) and flips to a side-by-side arrangement in landscape.
/// All art is original.
/// </summary>
internal static partial class PhoneSkin
{
    private const int W = 360, H = 720;           // design size (skin pixels); the host resizes the view
    private const int Bar = 52, Nav = 56;

    // "luna-plus-phone" (Luna Phone Plus) is the Luna palette with a Now Playing page that includes the playlist (PhoneSkinPlus.cs).
    public static readonly string[] Keys = ["luna-phone", "fluent-phone", "ember-phone", "horizon-phone", "aero10-phone", "luna-plus-phone", "glass-phone", "winamp-phone", "groove-phone",
        "wm6-phone", "metro-phone", "zune-phone", "kin-phone", "blades-phone", "xbox-phone", "walkman-phone"]; // the last line: PhoneSkin.Retro*.cs

    public static bool IsPhoneKey(string? key) => key is not null && Keys.Contains(key);

    /// <summary>
    /// Image pixels per skin unit for the art of the next <see cref="Build"/> (1 = the old, skin-resolution art). The host sets it
    /// to the screen's whole-number density before building a phone look, so every bitmap is drawn at screen resolution but still
    /// laid out in the same skin units (the skin engine reads the density from a marker chunk in each PNG, see PngDensity).
    /// </summary>
    public static int ArtScale { get; set; } = 1;

    // All art below is written in skin units; these shadow the LunaArt helpers (a class member hides a using-static method)
    // and draw at ArtScale. A scale argument overrides it (soft backdrops that gain nothing from density stay at 1).
    private static byte[] Png(int w, int h, Action<DrawingContext> draw) => LunaArt.Png(w, h, draw, ArtScale);

    private static byte[] Png(int w, int h, Action<DrawingContext> draw, int scale) => LunaArt.Png(w, h, draw, scale);

    private static byte[] Render(int w, int h, Action<DrawingContext> draw) => LunaArt.Render(w, h, draw, ArtScale);

    /// <summary>Encodes a region of a <see cref="Render"/> result; position, size and stride are in skin units.</summary>
    private static byte[] Png(byte[] pixels, int stride, int x, int y, int w, int h) =>
        LunaArt.Png(pixels, stride * ArtScale, x * ArtScale, y * ArtScale, w * ArtScale, h * ArtScale, ArtScale);

    public static string SourcePathOf(string key) => "pardeh://full-mode/" + key;

    public static bool IsPhoneSource(string? source) => KeyOf(source) is not null;

    /// <summary>The phone look key of a skin source path, or null.</summary>
    public static string? KeyOf(string? source) =>
        source is not null && source.StartsWith("pardeh://full-mode/", StringComparison.Ordinal) && source["pardeh://full-mode/".Length..] is var key && IsPhoneKey(key) ? key : null;

    /// <summary>Colours behind the system bars: status bar, the gap, and the gesture bar. Neutral dark for other skins.</summary>
    public static (Color Top, Color Middle, Color Bottom) Backdrop(string? key)
    {
        if (key == "glass-phone")
        {
            var screen = Pardeh.UI.Rendering.GlassPalette.Parse("#040A1E"); // the skin colour tints the near-black screen a little
            return (screen, screen, screen);
        }

        if (key is null || Pal(key) is not { } p)
            return (Color.Parse("#101010"), Color.Parse("#101010"), Color.Parse("#101010"));
        return (p.BarTop, p.Back, p.NavBottom);
    }

    // ---- Palettes -------------------------------------------------------------------------------

    private sealed record Palette
    {
        public required string Key { get; init; }
        public required string Name { get; init; }
        public string Font { get; init; } = "Tahoma";
        public bool Flat { get; init; }
        public bool Block { get; init; }                 // Horizon's blue logo block
        public bool Square { get; init; }                // Ember's rounded-square play button
        public bool Fluent { get; init; }              // retro looks that use the Fluent icons
        public bool Dark { get; init; }                // retro looks with dark library lists
        public bool Modern => Fluent || Key is "fluent-phone" or "ember-phone" or "horizon-phone" or "winamp-phone" or "groove-phone";   // Fluent icons; the others use the vintage pictorial set
        public bool Plus { get; init; }                // Luna Phone Plus: Now Playing with the playlist, Luna-style transport
        public Color BarTop { get; init; }
        public Color BarBottom { get; init; }
        public Color BarLine { get; init; }
        public string BarInk { get; init; } = "#FFFFFF";   // page title
        public Color BarIcon { get; init; }
        public Color Back { get; init; }
        public Color NavTop { get; init; }
        public Color NavBottom { get; init; }
        public Color NavLine { get; init; }
        public Color NavInk { get; init; }
        public Color NavSel { get; init; }
        public Color NavPill { get; init; }
        public Color NavPillRim { get; init; }
        public string LabelInk { get; init; } = "#000000";
        public string LabelSel { get; init; } = "#000000";
        public string TitleInk { get; init; } = "#FFFFFF";
        public string ArtistInk { get; init; } = "#C8D2E8";
        public string TimeInk { get; init; } = "#C8D2E8";
        public Color StageGlow { get; init; }
        public Color Track { get; init; }
        public Color Fill { get; init; }
        public Color ThumbLight { get; init; }
        public Color ThumbDark { get; init; }
        public Color OrbTop { get; init; }
        public Color OrbBottom { get; init; }
        public Color OrbRim { get; init; }
        public Color Glyph { get; init; }
        public Color Dim { get; init; }                  // secondary icon colour on the page
        public Color Accent { get; init; }
        public string ListInk { get; init; } = "#FFFFFF";
        public string ListPlaying { get; init; } = "#00EE00";
        public string ListSelect { get; init; } = "#2B3B6B";
    }

    /// <summary>True while Pardeh Glass is being built: its colours then go through <see cref="Pardeh.UI.Rendering.GlassPalette"/> (the skin colour setting).</summary>
    [ThreadStatic]
    private static bool _glassBuild;

    private static Color C(string hex) => _glassBuild ? Pardeh.UI.Rendering.GlassPalette.Parse(hex) : Color.Parse(hex);

    private static Palette? Pal(string key) => key switch
    {
        "luna-phone" => new Palette
        {
            Key = key, Name = "Luna Phone",
            BarTop = C("#CCD9F6"), BarBottom = C("#8199D6"), BarLine = C("#5A6FB6"), BarInk = "#13235E", BarIcon = C("#13235E"),
            Back = C("#212741"),
            NavTop = C("#FBFCFE"), NavBottom = C("#BFD2EA"), NavLine = C("#9DB2E4"), NavInk = C("#5A6BA6"), NavSel = C("#1F2F6E"),
            NavPill = C("#DCE5F7"), NavPillRim = C("#7F93CC"), LabelInk = "#4A5A98", LabelSel = "#1F2F6E",
            ArtistInk = "#B8C3E6", TimeInk = "#A9B8EC", StageGlow = C("#1D2550"),
            Track = C("#AEB5CE"), Fill = C("#94C692"), ThumbLight = C("#7CE07A"), ThumbDark = C("#0E7C10"),
            OrbTop = C("#FFFFFF"), OrbBottom = C("#C3CEEC"), OrbRim = C("#5666AB"), Glyph = C("#1F2F6E"), Dim = C("#C3CEEC"), Accent = C("#7CE07A"),
            ListInk = "#FFFFFF", ListPlaying = "#7FFF00", ListSelect = "#5465AE",
        },
        "fluent-phone" => new Palette
        {
            Key = key, Name = "Fluent Phone", Font = "Segoe UI", Flat = true,
            BarTop = C("#202020"), BarBottom = C("#202020"), BarLine = C("#2E2E2E"), BarInk = "#FFFFFF", BarIcon = C("#FFFFFF"),
            Back = C("#1C1C1C"),
            NavTop = C("#202020"), NavBottom = C("#202020"), NavLine = C("#2E2E2E"), NavInk = C("#9D9D9D"), NavSel = C("#60CDFF"),
            NavPill = C("#2D2D2D"), NavPillRim = C("#2D2D2D"), LabelInk = "#9D9D9D", LabelSel = "#60CDFF",
            ArtistInk = "#C5C5C5", TimeInk = "#C5C5C5", StageGlow = C("#14181E"),
            Track = C("#4A4A4A"), Fill = C("#60CDFF"), ThumbLight = C("#FFFFFF"), ThumbDark = C("#60CDFF"),
            OrbTop = C("#FFFFFF"), OrbBottom = C("#FFFFFF"), OrbRim = C("#FFFFFF"), Glyph = C("#1C1C1C"), Dim = C("#C5C5C5"), Accent = C("#60CDFF"),
            ListInk = "#FFFFFF", ListPlaying = "#60CDFF", ListSelect = "#3A3A3A",
        },
        "ember-phone" => new Palette
        {
            Key = key, Name = "Ember Phone", Font = "Segoe UI", Flat = true, Square = true,
            BarTop = C("#141414"), BarBottom = C("#141414"), BarLine = C("#2A2A2A"), BarInk = "#D6D6D6", BarIcon = C("#D6D6D6"),
            Back = C("#141414"),
            NavTop = C("#181818"), NavBottom = C("#181818"), NavLine = C("#2A2A2A"), NavInk = C("#8C8C8C"), NavSel = C("#E5801F"),
            NavPill = C("#3A2108"), NavPillRim = C("#3A2108"), LabelInk = "#8C8C8C", LabelSel = "#E5801F",
            TitleInk = "#D6D6D6", ArtistInk = "#8C8C8C", TimeInk = "#8C8C8C", StageGlow = C("#1B1B1B"),
            Track = C("#333333"), Fill = C("#E5801F"), ThumbLight = C("#F08F33"), ThumbDark = C("#E5801F"),
            OrbTop = C("#E5801F"), OrbBottom = C("#E5801F"), OrbRim = C("#E5801F"), Glyph = C("#1A1206"), Dim = C("#D6D6D6"), Accent = C("#E5801F"),
            ListInk = "#D6D6D6", ListPlaying = "#E5801F", ListSelect = "#3A2108",
        },
        "horizon-phone" => new Palette
        {
            Key = key, Name = "Horizon Phone", Block = true,
            BarTop = C("#EFF4FB"), BarBottom = C("#D6E3F4"), BarLine = C("#AABFE0"), BarInk = "#FFFFFF", BarIcon = C("#22356E"),
            Back = C("#22356F"),
            NavTop = C("#E2ECF8"), NavBottom = C("#A4C0E4"), NavLine = C("#7F9ACB"), NavInk = C("#3B5996"), NavSel = C("#1A326C"),
            NavPill = C("#EAF2FC"), NavPillRim = C("#6F8CC6"), LabelInk = "#3B5996", LabelSel = "#1A326C",
            ArtistInk = "#C8D2E8", TimeInk = "#C8D2E8", StageGlow = C("#22356F"),
            Track = C("#B8C9E0"), Fill = C("#A9D883"), ThumbLight = C("#5EE256"), ThumbDark = C("#0B6E06"),
            OrbTop = C("#FFFFFF"), OrbBottom = C("#B9C9E6"), OrbRim = C("#3F5FA8"), Glyph = C("#22356E"), Dim = C("#C9D7EE"), Accent = C("#A9D883"),
            ListInk = "#FFFFFF", ListPlaying = "#B8FFB0", ListSelect = "#4F639F",
        },
        "aero10-phone" => new Palette
        {
            Key = key, Name = "Pardeh Ten Phone",
            BarTop = C("#6C93E4"), BarBottom = C("#3B64CC"), BarLine = C("#2F52B8"), BarInk = "#FFFFFF", BarIcon = C("#FFFFFF"),
            Back = C("#1E3B99"),
            NavTop = C("#F4F7FD"), NavBottom = C("#C3CDEA"), NavLine = C("#7B9BE8"), NavInk = C("#3A5FCD"), NavSel = C("#1B3A8C"),
            NavPill = C("#FFFFFF"), NavPillRim = C("#3A5FCD"), LabelInk = "#3A5FCD", LabelSel = "#1B3A8C",
            ArtistInk = "#C2D6F8", TimeInk = "#C2D6F8", StageGlow = C("#15286B"),
            Track = C("#7B93CF"), Fill = C("#5EE256"), ThumbLight = C("#7CE07A"), ThumbDark = C("#0E7C10"),
            OrbTop = C("#FFFFFF"), OrbBottom = C("#9DBBF0"), OrbRim = C("#1E3B99"), Glyph = C("#1B3A8C"), Dim = C("#DCE6F8"), Accent = C("#7CE07A"),
            ListInk = "#FFFFFF", ListPlaying = "#7FFF00", ListSelect = "#3A5FCD",
        },
        // Winamp Phone: charcoal blue-black, amber accent, raised dark controls with an amber ring.
        "winamp-phone" => new Palette
        {
            Key = key, Name = "Winamp Phone", Font = "Segoe UI", Flat = true,
            BarTop = C("#161D2A"), BarBottom = C("#0E131D"), BarLine = C("#2B3548"), BarInk = "#E7EDF8", BarIcon = C("#FF9D00"),
            Back = C("#0A0E16"),
            NavTop = C("#141B28"), NavBottom = C("#0C1119"), NavLine = C("#2B3548"), NavInk = C("#9AA8BF"), NavSel = C("#FF9D00"),
            NavPill = C("#2A2112"), NavPillRim = C("#7A5412"), LabelInk = "#9AA8BF", LabelSel = "#FF9D00",
            TitleInk = "#FFA81A", ArtistInk = "#9AA8BF", TimeInk = "#E7EDF8", StageGlow = C("#111824"),
            Track = C("#2D384D"), Fill = C("#FF9D00"), ThumbLight = C("#FFD36A"), ThumbDark = C("#FF9D00"),
            OrbTop = C("#202B3D"), OrbBottom = C("#0F1520"), OrbRim = C("#FF9D00"), Glyph = C("#FF9D00"), Dim = C("#9AA8BF"), Accent = C("#FF9D00"),
            ListInk = "#E7EDF8", ListPlaying = "#FF9D00", ListSelect = "#3A2A0C",
        },
        // Groove Phone: the Windows 11 Groove Music look, a deep blue with light-blue accents.
        "groove-phone" => new Palette
        {
            Key = key, Name = "Groove Phone", Font = "Segoe UI", Flat = true,
            BarTop = C("#0F3E98"), BarBottom = C("#0C3488"), BarLine = C("#2858BC"), BarInk = "#FFFFFF", BarIcon = C("#FFFFFF"),
            Back = C("#0B2E78"),
            NavTop = C("#0D3490"), NavBottom = C("#0A2A74"), NavLine = C("#2858BC"), NavInk = C("#A9C4F0"), NavSel = C("#7FD4FF"),
            NavPill = C("#1B4FB0"), NavPillRim = C("#1B4FB0"), LabelInk = "#A9C4F0", LabelSel = "#7FD4FF",
            ArtistInk = "#B7CCF2", TimeInk = "#DCE8FF", StageGlow = C("#123C92"),
            Track = C("#5479C2"), Fill = C("#7FD4FF"), ThumbLight = C("#FFFFFF"), ThumbDark = C("#7FD4FF"),
            OrbTop = C("#DCEBFF"), OrbBottom = C("#DCEBFF"), OrbRim = C("#DCEBFF"), Glyph = C("#0B2E78"), Dim = C("#D5E4FF"), Accent = C("#7FD4FF"),
            ListInk = "#FFFFFF", ListPlaying = "#7FD4FF", ListSelect = "#1F57C2",
        },
        "luna-plus-phone" => Pal("luna-phone") is { } luna ? luna with { Key = key, Name = "Luna Phone Plus", Plus = true } : null,
        _ => Retro(key)?.Palette,
    };

    // ---- Build ----------------------------------------------------------------------------------

    public static SkinPackage Build(string key)
    {
        if (key == GlassKey)
            return BuildGlass();
        var p = Pal(key) ?? throw new ArgumentException("Unknown phone look: " + key);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        AddChrome(files, p);
        AddButtons(files, p);
        AddNavIcons(files, p);
        AddSliders(files, p);
        // The favourite heart on Now Playing and the queue tools (sort, filter): Fluent icons on the modern looks, glossy pictures on the vintage ones.
        AddFavoriteArt(files, p.Modern, false, p.Dim, p.Dim);
        AddLyricsArt(files, p.Modern, false, p.Dim, p.Modern ? p.Accent : Colors.White);
        AddQueueToolArt(files, 40, p.Modern ? ToolStyle.Fluent : ToolStyle.Pictorial, p.Dim, p.Accent);
        if (p.Plus)
        {
            AddPlusArt(files, p);
            AddQueueToolArt(files, 32, ToolStyle.Stroke, Colors.White, C("#CFF6C8"), glass: false); // the Playlist sheet's header band: white strokes, like its chevron (qsort / qfilter of the Now Playing page)
            foreach (var name in new[] { "qsort.png", "qsort_down.png", "qsort_disabled.png", "qfilter.png", "qfilter_down.png" })
                files["s" + name] = files[name];
            AddQueueToolArt(files, 40, p.Modern ? ToolStyle.Fluent : ToolStyle.Pictorial, p.Dim, p.Accent); // the Playlist page's own 40 unit buttons
        }

        AddLookArt(files, p);

        var layout = Layout(p);
        if (HasArt(p))
            layout = ApplyArtLayout(p, layout);
        if (p.Plus && layout.IndexOf("<SUBVIEW id=\"paneNow\"", StringComparison.Ordinal) is var from and >= 0
            && layout.IndexOf("<SUBVIEW id=\"paneLibrary\"", StringComparison.Ordinal) is var to and > 0)
            layout = layout[..from] + PlusNowPane(p) + "\n" + layout[to..];
        files["phone.wms"] = Encoding.UTF8.GetBytes(RetroLayout(p, layout));
        var script = RetroScript(p, ScriptHead + (p.Plus ? PlusNowScript : DefaultNowScript) + (HasArt(p) ? ArtScript(p) : string.Empty) + ScriptTail);
        files["phone.js"] = Encoding.UTF8.GetBytes(script.Replace("__INK__", p.LabelInk).Replace("__SEL__", p.LabelSel));
        return WmzLoader.FromFiles(files, SourcePathOf(key));
    }

    /// <summary>The pictures a look draws over the shared ones (the PC versions draw the same).</summary>
    private static void AddLookArt(Dictionary<string, byte[]> files, Palette p)
    {
        if (p.Key == "winamp-phone")
            AddWinampArt(files, p);
        else if (p.Key == "groove-phone")
            AddGrooveArt(files, p);
        AddRetroArt(files, p); // the retro looks draw their own pictures over the shared ones
    }

    /// <summary>The LIBRARY element's opt-in palette attributes for a phone look: dark for Fluent / Ember, light for the others.</summary>
    private static string LibAttrs(Palette p)
    {
        var dark = p.Dark || p.Key is "fluent-phone" or "ember-phone" or "winamp-phone" or "groove-phone";
        return dark
            ? $"libBack=\"{Hex(p.Back)}\" libBar=\"{Hex(p.NavTop)}\" libBarInk=\"{p.ListInk}\" libInk=\"{p.ListInk}\" libDim=\"{p.ArtistInk}\" libSel=\"{p.ListSelect}\" libAccent=\"{p.ListPlaying}\""
            : $"libBack=\"#F4F7FD\" libBar=\"{Hex(p.NavBottom)}\" libBarInk=\"{Hex(p.BarIcon)}\" libInk=\"#1B2A55\" libDim=\"{Hex(p.NavInk)}\" libSel=\"#CFDDF6\" libAccent=\"#1B7A1B\"";
    }

    private static IBrush Solid(Color c) => new SolidColorBrush(c);

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    // ---- Chrome ---------------------------------------------------------------------------------

    private static void AddChrome(Dictionary<string, byte[]> files, Palette p)
    {
        files["bar.png"] = Png(1, Bar, g =>
        {
            g.FillRectangle(Vertical((p.BarTop, 0), (p.BarBottom, 1)), new Rect(0, 0, 1, Bar));
            if (!p.Flat)
                g.FillRectangle(Solid(WithAlpha(Colors.White, 110)), new Rect(0, 1, 1, 1));
            g.FillRectangle(Solid(p.BarLine), new Rect(0, Bar - 1, 1, 1));
        });
        files["nav.png"] = Png(1, Nav, g =>
        {
            g.FillRectangle(Vertical((p.NavTop, 0), (p.NavBottom, 1)), new Rect(0, 0, 1, Nav));
            g.FillRectangle(Solid(p.NavLine), new Rect(0, 0, 1, 1));
            if (!p.Flat)
                g.FillRectangle(Solid(WithAlpha(Colors.White, 130)), new Rect(0, 1, 1, 1));
        });
        var classic = AppSettings.Current.ClassicBranding; // the flag logo of the design instead of Pardeh's own
        void Logo(DrawingContext g, Rect r)
        {
            if (classic)
                ClassicFlag.Draw(g, r.Inflate(r.Width * 0.18));
            else
                PardehLogo.Draw(g, r);
        }

        files["logo.png"] = Png(32, 32, g => Logo(g, new Rect(3, 3, 26, 26)));
        if (p.Block)
        {
            // Horizon: the blue block behind the logo and title, with its sweeping lower-right corner.
            files["bar_block.png"] = Png(230, Bar, g =>
            {
                g.FillRectangle(Vertical((p.BarTop, 0), (p.BarBottom, 1)), new Rect(0, 0, 230, Bar));
                var block = Geo("M 0,0 L 220,0 L 220,12 Q 220,46 190,46 L 0,46 Z");
                g.DrawGeometry(Vertical((C("#6A8CC2"), 0), (C("#5577B7"), 0.5), (C("#4566AA"), 1)), null, block);
                g.DrawGeometry(null, Pen(WithAlpha(Colors.White, 110)), Geo("M 1,1.5 L 219,1.5"));
                g.DrawGeometry(null, Pen(C("#142A5C")), Geo("M 220,0 L 220,12 Q 220,46.5 190,46.5 L 0,46.5"));
                g.FillRectangle(Solid(p.BarLine), new Rect(0, Bar - 1, 230, 1));
                Logo(g, new Rect(11, 9, 28, 28));
            });
        }

        files["stage_frame.png"] = Png(12, 12, g =>
            g.DrawRectangle(Solid(p.StageGlow), Pen(p.Flat ? p.BarLine : p.NavLine), new RoundedRect(new Rect(0.5, 0.5, 11, 11), p.Flat ? 5 : 3)));
        files["cover_frame.png"] = Png(76, 76, g =>
        {
            g.DrawRectangle(Vertical((C("#33406E"), 0), (C("#1C2448"), 1)), Pen(WithAlpha(Colors.White, 130)), new RoundedRect(new Rect(0.5, 0.5, 75, 75), p.Flat ? 8 : 4));
            g.DrawGeometry(null, new Pen(Solid(WithAlpha(Colors.White, 170)), 2.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
                Geo("M 32,48 L 32,26 L 48,22 L 48,44 M 28,52 m -4,0 a 4,3 0 1,0 8,0 a 4,3 0 1,0 -8,0 M 44,48 m -4,0 a 4,3 0 1,0 8,0 a 4,3 0 1,0 -8,0"));
        });
    }

    // ---- Icons ----------------------------------------------------------------------------------

    // 20x20 line icons, drawn like the Ember set.
    private const string IconShuffle = "M 3.5,6 L 6.5,6 L 13,14 L 16.5,14 M 3.5,14 L 6.5,14 L 13,6 L 16.5,6 M 14.5,4 L 16.5,6 L 14.5,8 M 14.5,12 L 16.5,14 L 14.5,16";
    private const string IconRepeat = "M 4,11 L 4,7 L 15,7 M 16,9 L 16,13 L 5,13 M 13,5 L 15,7 L 13,9 M 7,11 L 5,13 L 7,15";
    private const string IconMute = "M 3.5,8 L 6.5,8 L 10,5 L 10,15 L 6.5,12 L 3.5,12 Z M 13,8 Q 14.5,10 13,12 M 15,6.5 Q 17.5,10 15,13.5";
    private const string IconMuted = "M 3.5,8 L 6.5,8 L 10,5 L 10,15 L 6.5,12 L 3.5,12 Z M 13,8 L 17,12 M 17,8 L 13,12";

    private static readonly (string Key, string Path)[] NavIcons =
    [
        ("now", "M 4,4 L 16,4 L 16,16 L 4,16 Z M 8.5,7.5 L 12.5,10 L 8.5,12.5 Z"),
        ("library", "M 4,5 L 9,5 L 10.5,7 L 16,7 L 16,15 L 4,15 Z M 11.5,9 L 11.5,13 M 11.5,9 L 13.5,8.5"),
        ("queue", "M 3.5,6 L 13,6 M 3.5,10 L 13,10 M 3.5,14 L 9,14 M 12,12 L 16.5,14.5 L 12,17 Z"),
        ("radio", "M 10,10 m -1.2,0 a 1.2,1.2 0 1,0 2.4,0 a 1.2,1.2 0 1,0 -2.4,0 M 6.5,6.5 Q 3.5,10 6.5,13.5 M 13.5,6.5 Q 16.5,10 13.5,13.5 M 4.2,4.2 Q -0.5,10 4.2,15.8 M 15.8,4.2 Q 20.5,10 15.8,15.8"),
        ("settings", "M 10,10 m -2.4,0 a 2.4,2.4 0 1,0 4.8,0 a 2.4,2.4 0 1,0 -4.8,0 M 10,3 L 10,5 M 10,15 L 10,17 M 3,10 L 5,10 M 15,10 L 17,10 M 5,5 L 6.4,6.4 M 13.6,13.6 L 15,15 M 15,5 L 13.6,6.4 M 6.4,13.6 L 5,15"),
    ];

    private static void StrokeIcon(DrawingContext g, Color color, string data, double cx, double cy, double scale, double width = 1.7)
    {
        using (g.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(cx - 10 * scale, cy - 10 * scale)))
            g.DrawGeometry(null, new Pen(Solid(color), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Geo(data));
    }

    /// <summary>A Fluent UI System Icon (24 px grid) centred at (cx, cy), <paramref name="size"/> px, regular or filled.</summary>
    private static void FluentGlyph(DrawingContext g, Color color, string name, bool filled, double cx, double cy, double size)
    {
        var s = size / 24;
        using (g.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation(cx - 12 * s, cy - 12 * s)))
            g.DrawGeometry(Solid(color), null, Pardeh.UI.Rendering.FluentIcons.Geo(name, filled));
    }

    private static readonly Dictionary<string, string> FluentNav = new() { ["now"] = "play_circle", ["library"] = "library", ["queue"] = "text_bullet_list_ltr", ["radio"] = "live", ["settings"] = "settings" };

    // Vintage (XP / WMP9 era) looks use the glossy pictorial set (no extracted WMP9 task-rail icons exist on disk).
    private static readonly Dictionary<string, string> VintageNav = new() { ["now"] = "player", ["library"] = "library", ["queue"] = "list", ["radio"] = "radio", ["settings"] = "gear" };

    /// <summary>The bottom-navigation glyph: Fluent (regular, filled + accent when selected) on the modern looks, a glossy XP-style picture on the vintage ones.</summary>
    private static void NavGlyph(DrawingContext g, Palette p, string key, bool selected, double cx, double cy)
    {
        if (p.Modern)
            FluentGlyph(g, selected ? p.NavSel : p.NavInk, FluentNav[key], selected, cx, cy, 26);
        else
            Pardeh.UI.Rendering.PictorialIcons.Draw(g, VintageNav[key], new Rect(cx - 14, cy - 14, 28, 28), shadow: false);
    }

    private static void FillIcon(DrawingContext g, Color color, string data, double cx, double cy, double scale)
    {
        using (g.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(cx - 10 * scale, cy - 10 * scale)))
            g.DrawGeometry(Solid(color), null, Geo(data));
    }

    private static void AddNavIcons(Dictionary<string, byte[]> files, Palette p)
    {
        foreach (var (key, path) in NavIcons)
        {
            // A button only reacts where its picture is not transparent: a barely visible fill makes the whole 72x52 slot tappable.
            files[$"nav_{key}.png"] = Png(72, 52, g =>
            {
                g.FillRectangle(Solid(Color.FromArgb(2, 0, 0, 0)), new Rect(0, 0, 72, 52));
                NavGlyph(g, p, key, false, 36, 17);
            });
            files[$"nav_{key}_down.png"] = Png(72, 52, g =>
            {
                g.FillRectangle(Solid(Color.FromArgb(2, 0, 0, 0)), new Rect(0, 0, 72, 52));
                g.DrawRectangle(p.Flat ? Solid(p.NavPill) : Vertical((p.NavPill, 0), (WithAlpha(p.NavPill, 200), 1)), p.Flat ? null : Pen(p.NavPillRim), new RoundedRect(new Rect(4.5, 0.5, 63, 33), 17));
                NavGlyph(g, p, key, true, 36, 17);
            });
        }

        // The top bar's logo + title area is the app-menu button (no three dots): a nearly invisible hit area, with the logo
        // drawn into it unless the look has its own logo block (Horizon).
        var classic = AppSettings.Current.ClassicBranding;
        files["menu_hit.png"] = Png(MenuHitWidth, Bar, g =>
        {
            g.FillRectangle(Solid(Color.FromArgb(2, 0, 0, 0)), new Rect(0, 0, MenuHitWidth, Bar));
            if (p.Block)
                return;
            if (classic)
                ClassicFlag.Draw(g, new Rect(3, 3, 26, 26).Inflate(26 * 0.18).Translate(new Vector(10, 10)));
            else
                PardehLogo.Draw(g, new Rect(13, 13, 26, 26));
        });
    }

    private const int MenuHitWidth = 250;

    // ---- Buttons --------------------------------------------------------------------------------

    private enum State { Normal, Down, Disabled }

    private static readonly (State State, string Suffix)[] States = [(State.Normal, ""), (State.Down, "_down"), (State.Disabled, "_disabled")];

    private const string PlayPath = "M 7,4 L 16.5,10 L 7,16 Z";
    private const string PausePath = "M 6.5,4.5 L 9,4.5 L 9,15.5 L 6.5,15.5 Z M 11,4.5 L 13.5,4.5 L 13.5,15.5 L 11,15.5 Z";
    private const string PrevPath = "M 5,4.5 L 7,4.5 L 7,15.5 L 5,15.5 Z M 15.5,4.5 L 8.5,10 L 15.5,15.5 Z";
    private const string NextPath = "M 13,4.5 L 15,4.5 L 15,15.5 L 13,15.5 Z M 4.5,4.5 L 11.5,10 L 4.5,15.5 Z";

    // Skip back / forward 10 seconds, 20x20 line icons for the vintage looks: a circular arrow (open at the top, the arrow head's corner
    // on the side it travels towards) with a "10" in it. The modern looks use the Fluent "Skip Back 10" / "Skip Forward 10" icons.
    private const string SkipDigits = "M 6.6,10.6 L 8.3,9.3 L 8.3,14 M 11.6,9 A 1.75,2.6 0 1 0 11.6,14.2 A 1.75,2.6 0 1 0 11.6,9 Z";
    private const string SkipBackPath = "M 5.2,3.4 L 5.2,6.4 L 8.4,6.4 M 5.2,6.4 A 6.8,6.8 0 1 1 3.3,12.4 " + SkipDigits;
    private const string SkipForwardPath = "M 14.8,3.4 L 14.8,6.4 L 11.6,6.4 M 14.8,6.4 A 6.8,6.8 0 1 0 16.7,12.4 " + SkipDigits;

    /// <summary>The skip glyph centred at (cx, cy): Fluent on the modern looks, a drawn "10" arrow on the vintage ones.</summary>
    private static void SkipGlyph(DrawingContext g, Palette p, Color color, bool forward, double cx, double cy, double fluentSize, double scale)
    {
        if (p.Modern)
            FluentGlyph(g, color, forward ? "skip_forward_10" : "skip_back_10", false, cx, cy, fluentSize);
        else
            StrokeIcon(g, color, forward ? SkipForwardPath : SkipBackPath, cx, cy, scale, 1.55);
    }

    private static void HitBox(DrawingContext g, double size) => g.FillRectangle(Solid(Color.FromArgb(2, 0, 0, 0)), new Rect(0, 0, size, size));

    private static void AddButtons(Dictionary<string, byte[]> files, Palette p)
    {
        foreach (var (state, suffix) in States)
        {
            // A barely visible fill under the picture makes the whole box tappable: the flat looks draw a thin glyph with holes in it.
            files[$"skipb{suffix}.png"] = Png(SkipSize, SkipSize, g => { HitBox(g, SkipSize); Side(g, p, state, c => SkipGlyph(g, p, c, false, SkipSize / 2.0, SkipSize / 2.0, 30, 1.4), SkipSize); });
            files[$"skipf{suffix}.png"] = Png(SkipSize, SkipSize, g => { HitBox(g, SkipSize); Side(g, p, state, c => SkipGlyph(g, p, c, true, SkipSize / 2.0, SkipSize / 2.0, 30, 1.4), SkipSize); });
            files[$"play{suffix}.png"] = Png(64, 64, g => Hero(g, p, state, c => { if (p.Modern) FluentGlyph(g, c, "play", true, 33, 32, 32); else FillIcon(g, c, PlayPath, 33, 32, 1.9); }));
            files[$"pause{suffix}.png"] = Png(64, 64, g => Hero(g, p, state, c => { if (p.Modern) FluentGlyph(g, c, "pause", true, 32, 32, 32); else FillIcon(g, c, PausePath, 32, 32, 1.9); }));
            files[$"prev{suffix}.png"] = Png(52, 52, g => Side(g, p, state, c => { if (p.Modern) FluentGlyph(g, c, "previous", true, 26, 26, 28); else FillIcon(g, c, PrevPath, 26, 26, 1.5); }));
            files[$"next{suffix}.png"] = Png(52, 52, g => Side(g, p, state, c => { if (p.Modern) FluentGlyph(g, c, "next", true, 26, 26, 28); else FillIcon(g, c, NextPath, 26, 26, 1.5); }));
        }

        // Toggles: shuffle / repeat (down = on), mute (down = muted).
        foreach (var (name, path) in new[] { ("shuffle", IconShuffle), ("repeat", IconRepeat), ("mute", IconMute), ("muted", IconMuted) })
        {
            var on = name is "muted";
            var fluent = name switch { "shuffle" => "arrow_shuffle", "repeat" => "arrow_repeat_all", "mute" => "speaker_2", _ => "speaker_mute" };
            void Glyph(DrawingContext g, Color c, bool filled)
            {
                if (p.Modern)
                    FluentGlyph(g, c, fluent, filled, 22, 22, 26);
                else
                    StrokeIcon(g, c, path, 22, 22, 1.25, filled ? 1.9 : 1.7);
            }

            files[$"{name}.png"] = Png(44, 44, g => Glyph(g, on ? p.Accent : p.Dim, false));
            files[$"{name}_down.png"] = Png(44, 44, g =>
            {
                g.DrawEllipse(Solid(WithAlpha(p.Accent, 55)), null, new Point(22, 22), 20, 20);
                Glyph(g, p.Accent, true);
            });
        }
    }

    private static Color Ink(Palette p, State state) => state == State.Disabled ? WithAlpha(p.Glyph, 90) : p.Glyph;

    /// <summary>The big play / pause button: a glossy orb, a flat disc (Fluent) or a rounded square (Ember).</summary>
    private static void Hero(DrawingContext g, Palette p, State state, Action<Color> glyph)
    {
        const double size = 64;
        var c = new Point(size / 2, size / 2);
        var down = state == State.Down;
        if (p.Square)
        {
            var face = state == State.Disabled ? C("#6B4A22") : down ? C("#C96D16") : p.OrbTop;
            g.DrawRectangle(Solid(face), null, new RoundedRect(new Rect(2, 2, 60, 60), 18));
            glyph(Ink(p, state));
            return;
        }

        if (p.Flat)
        {
            var disc = state == State.Disabled ? C("#5A5A5A") : down ? C("#CFCFCF") : p.OrbTop;
            g.DrawEllipse(Solid(disc), null, c, 29, 29);
            glyph(Ink(p, state));
            return;
        }

        g.DrawEllipse(Solid(WithAlpha(C("#1C2E66"), 60)), null, new Point(c.X, c.Y + 1.5), 30, 30);
        var brush = state switch
        {
            State.Down => Vertical((p.OrbBottom, 0), (p.OrbTop, 1)),
            State.Disabled => Vertical((C("#F7F9FC"), 0), (C("#DCE2EE"), 1)),
            _ => Vertical((p.OrbTop, 0), (Color.FromRgb((byte)((p.OrbTop.R + p.OrbBottom.R) / 2), (byte)((p.OrbTop.G + p.OrbBottom.G) / 2), (byte)((p.OrbTop.B + p.OrbBottom.B) / 2)), 0.5), (p.OrbBottom, 1)),
        };
        g.DrawEllipse(brush, Pen(p.OrbRim, 2), c, 29, 29);
        g.DrawEllipse(Vertical((WithAlpha(Colors.White, 230), 0), (WithAlpha(Colors.White, 0), 1)), null, new Point(c.X, c.Y - 11), 22, 15);
        g.DrawEllipse(null, Pen(WithAlpha(Colors.White, 200)), c, 25.5, 25.5);
        glyph(Ink(p, state));
    }

    /// <summary>Previous / next: a glossy small orb on the XP-era looks, a bare glyph on the flat ones.</summary>
    private static void Side(DrawingContext g, Palette p, State state, Action<Color> glyph, int size = 52)
    {
        var half = size / 2.0;
        var radius = half - 2;
        var c = new Point(half, half);
        if (p.Flat)
        {
            if (state == State.Down)
                g.DrawEllipse(Solid(WithAlpha(Colors.White, 24)), null, c, radius, radius);
            glyph(state == State.Disabled ? WithAlpha(p.Dim, 90) : p.Dim);
            return;
        }

        var brush = state switch
        {
            State.Down => Vertical((p.OrbBottom, 0), (p.OrbTop, 1)),
            State.Disabled => Vertical((C("#F7F9FC"), 0), (C("#DCE2EE"), 1)),
            _ => Vertical((p.OrbTop, 0), (p.OrbBottom, 1)),
        };
        var k = size / 52.0; // 1 for the 52 unit previous / next buttons: their art is unchanged
        g.DrawEllipse(brush, Pen(p.OrbRim, 1.4), c, radius, radius);
        g.DrawEllipse(Vertical((WithAlpha(Colors.White, 220), 0), (WithAlpha(Colors.White, 0), 1)), null, new Point(half, half - 9 * k), 17 * k, 11 * k);
        glyph(Ink(p, state));
    }

    /// <summary>Touch size (skin units) of the skip 10 s buttons: ~50 dp on a 1080 px phone, a little smaller than previous / next.</summary>
    private const int SkipSize = 44;

    // ---- Sliders --------------------------------------------------------------------------------

    private static void AddSliders(Dictionary<string, byte[]> files, Palette p)
    {
        // Tiled tracks, 30 high so the touch target is large; the bar itself is a few pixels.
        var round = p.Flat;
        files["seek_track.png"] = Png(6, 30, g =>
        {
            g.FillRectangle(Solid(p.Track), new Rect(0, 12, 6, 6));
            if (!p.Flat)
                g.FillRectangle(Solid(WithAlpha(Colors.Black, 60)), new Rect(0, 12, 6, 1));
        });
        files["seek_fill.png"] = Png(6, 30, g =>
        {
            g.FillRectangle(Solid(p.Fill), new Rect(0, 12, 6, 6));
            if (!p.Flat)
                g.FillRectangle(Solid(WithAlpha(Colors.White, 130)), new Rect(0, 12, 6, 2));
        });
        foreach (var hover in new[] { false, true })
        {
            files[hover ? "thumb_hover.png" : "thumb.png"] = Png(30, 30, g =>
            {
                var c = new Point(15, 15);
                g.DrawEllipse(Solid(WithAlpha(Colors.Black, 60)), null, new Point(15, 16), 11, 11);
                if (round)
                {
                    g.DrawEllipse(Solid(p.ThumbLight), null, c, 11, 11);
                    if (p.Key != "ember-phone")
                        g.DrawEllipse(Solid(p.ThumbDark), null, c, 6, 6);
                }
                else
                {
                    g.DrawEllipse(Vertical((p.ThumbLight, 0), (p.ThumbDark, 1)), Pen(p.ThumbDark), c, 11, 11);
                    g.DrawEllipse(null, Pen(WithAlpha(Colors.White, 160)), c, 9, 9);
                }
            });
        }
    }

    // ---- Layout ---------------------------------------------------------------------------------

    private static string Layout(Palette p)
    {
        var f = p.Font;
        var navItems = new StringBuilder();
        var nav = new (string Key, string Id, string Label)[]
        {
            ("now", "navNow", "Now Playing"), ("library", "navLibrary", "Library"), ("queue", "navQueue", "Playlist"),
            ("radio", "navRadio", "Radio"), ("settings", "navSettings", "Settings"),
        };
        foreach (var (key, id, label) in nav)
        {
            var page = key == "settings" ? "skins" : key;
            navItems.Append($$"""
                <BUTTON id="{{id}}" left="0" top="3" image="nav_{{key}}.png" hoverImage="nav_{{key}}.png" downImage="nav_{{key}}_down.png" onClick="ShowPane('{{page}}');" upToolTip="{{label}}"/>
                <TEXT id="{{id}}Label" left="0" top="38" width="72" height="14" justification="center" value="{{(p.Plus ? string.Empty : label)}}" fontType="{{f}}" fontSize="7" fontStyle="bold" foregroundColor="{{p.LabelInk}}"/>

""");
        }

        var barBack = p.Block
            ? $"""<SUBVIEW left="0" top="0" width="230" height="{Bar}" backgroundImage="bar_block.png"/>"""
            : string.Empty; // the logo is drawn into the menu button's image
        var titleLeft = p.Block ? 50 : 50;

        return $$"""
        <THEME id="Pardeh{{p.Name.Replace(" ", "")}}" title="Pardeh {{p.Name}}" author="Pardeh contributors" pardehMode="full">
          <VIEW id="vPhone" width="{{W}}" height="{{H}}" minWidth="300" minHeight="300" resizable="true" antialiasEdges="true"
                titleBar="false" backgroundColor="{{Hex(p.Back)}}" scriptFile="phone.js"
                onLoad="OnLoad();" onClose="OnClose();" onResize="Relayout();">

            <PLAYER openstatechange="OnOpenState(NewState);"/>

            <!-- Content: one pane at a time, between the top bar and the navigation bar. -->
            <SUBVIEW id="svContent" left="0" top="{{Bar}}" width="{{W}}" height="{{H - Bar - Nav}}" backgroundColor="{{Hex(p.Back)}}">

              <SUBVIEW id="paneNow" left="0" top="0" width="{{W}}" height="{{H - Bar - Nav}}">
                <SUBVIEW id="svStage" left="14" top="10" width="332" height="372" backgroundImage="stage_frame.png" backgroundColor="{{Hex(p.StageGlow)}}">
                  <WMPVIDEO id="vid" left="0" top="0" width="332" height="372" visible="false" backgroundColor="#000000" horizontalAlignment="stretch" verticalAlignment="stretch"/>
                  <WMPEFFECTS id="vis" left="0" top="0" width="332" height="372" visible="true" picker="true" horizontalAlignment="stretch" verticalAlignment="stretch"/>
                  <SUBVIEW id="cover" left="10" top="10" width="76" height="76" backgroundImage="cover_frame.png">
                    <SUBVIEW left="2" top="2" width="72" height="72" backgroundImage="WMPImage_AlbumArtLarge" coverScale="{{ArtScale}}"/>
                  </SUBVIEW>
                  {{LyricsElementXml("lyr", 332, 372, p.TitleInk, Hex(p.Accent), "#FFFFFF", p.Modern, f, Hex(p.StageGlow))}}
                </SUBVIEW>
                <TEXT id="tTitle" left="14" top="390" width="332" height="22" justification="center" fontType="{{f}}" fontSize="13" fontStyle="bold" foregroundColor="{{p.TitleInk}}"
                    value="wmpprop:player.currentMedia ? player.currentMedia.name : 'Nothing playing'"/>
                <TEXT id="tArtist" left="14" top="414" width="332" height="18" justification="center" fontType="{{f}}" fontSize="9" foregroundColor="{{p.ArtistInk}}"
                    value="wmpprop:player.currentMedia ? player.currentMedia.getItemInfo('Author') : ''"/>
                <FAVBUTTON id="bFav" left="302" top="389" image="fav.png" hoverImage="fav.png" downImage="fav_down.png" disabledImage="fav_disabled.png" upToolTip="Add to favourites" downToolTip="Remove from favourites"/>
                <BUTTON id="bLyr" left="14" top="389" image="lyr.png" hoverImage="lyr.png" downImage="lyr_down.png" onClick="ToggleLyrics();" upToolTip="Lyrics" downToolTip="Hide lyrics"/>
                <SEEKSLIDER id="seek" left="14" top="438" width="332" height="30"
                    backgroundImage="seek_track.png" foregroundImage="seek_fill.png" thumbImage="thumb.png" thumbHoverImage="thumb_hover.png"
                    tiled="true" slide="false" borderSize="0" direction="horizontal"/>
                <TEXT id="tPos" left="14" top="470" width="90" height="14" fontType="{{f}}" fontSize="8" foregroundColor="{{p.TimeInk}}"
                    value="wmpprop:player.currentMedia ? player.controls.currentPositionString : '00:00'"/>
                <TEXT id="tDur" left="256" top="470" width="90" height="14" justification="right" fontType="{{f}}" fontSize="8" foregroundColor="{{p.TimeInk}}"
                    value="wmpprop:player.currentMedia ? player.currentMedia.durationString : ''"/>
                <PLAYBUTTON id="bPlay" left="148" top="488" image="play.png" hoverImage="play.png" downImage="play_down.png" disabledImage="play_disabled.png" upToolTip="Play"/>
                <PAUSEBUTTON id="bPause" left="148" top="488" visible="wmpenabled:player.controls.pause" image="pause.png" hoverImage="pause.png" downImage="pause_down.png" disabledImage="pause_disabled.png" upToolTip="Pause"/>
                <PREVBUTTON id="bPrev" left="78" top="494" image="prev.png" hoverImage="prev.png" downImage="prev_down.png" disabledImage="prev_disabled.png" upToolTip="Previous"/>
                <NEXTBUTTON id="bNext" left="230" top="494" image="next.png" hoverImage="next.png" downImage="next_down.png" disabledImage="next_disabled.png" upToolTip="Next"/>
                <BUTTON id="bSkipB" left="28" top="496" image="skipb.png" hoverImage="skipb.png" downImage="skipb_down.png" upToolTip="Back 10 seconds" onClick="player.controls.seekBy(-10);"/>
                <BUTTON id="bSkipF" left="288" top="496" image="skipf.png" hoverImage="skipf.png" downImage="skipf_down.png" upToolTip="Forward 10 seconds" onClick="player.controls.seekBy(10);"/>
                <SHUFFLEBUTTON id="bShuffle" left="8" top="558" image="shuffle.png" hoverImage="shuffle.png" downImage="shuffle_down.png" upToolTip="Turn Shuffle On" downToolTip="Turn Shuffle Off"/>
                <MUTEBUTTON id="bMute" left="54" top="558" image="mute.png" hoverImage="mute.png" downImage="muted_down.png" upToolTip="Mute" downToolTip="Sound"/>
                <VOLUMESLIDER id="vol" left="104" top="565" width="150" height="30"
                    backgroundImage="seek_track.png" foregroundImage="seek_fill.png" thumbImage="thumb.png" thumbHoverImage="thumb_hover.png"
                    tiled="true" slide="false" borderSize="0" direction="horizontal"/>
                <REPEATBUTTON id="bRepeat" left="308" top="558" image="repeat.png" hoverImage="repeat.png" downImage="repeat_down.png" upToolTip="Turn Repeat On" downToolTip="Turn Repeat Off"/>
              </SUBVIEW>

              <SUBVIEW id="paneLibrary" left="0" top="0" width="{{W}}" height="{{H - Bar - Nav}}" visible="false">
                <LIBRARY id="library" left="0" top="0" width="{{W}}" height="{{H - Bar - Nav}}" phone="true" {{LibAttrs(p)}} horizontalAlignment="stretch" verticalAlignment="stretch"/>
              </SUBVIEW>

              <SUBVIEW id="paneQueue" left="0" top="0" width="{{W}}" height="{{H - Bar - Nav}}" visible="false" backgroundColor="{{Hex(p.Back)}}">
                <PLAYLIST id="ipl" left="0" top="0" width="{{W}}" height="{{H - Bar - Nav}}" touch="true" headerHeight="40" favIcons="{{(p.Modern ? "fluent" : "pictorial")}}" backgroundColor="{{Hex(p.Back)}}" foregroundColor="{{p.ListInk}}"
                    itemPlayingColor="{{p.ListPlaying}}" itemSelectedBackgroundColor="{{p.ListSelect}}" itemPlayingBackgroundColor="{{p.ListSelect}}" horizontalAlignment="stretch" verticalAlignment="stretch"/>
                <QUEUEFILTERBUTTON id="bQFilter" left="{{W - 90}}" top="0" image="qfilter.png" hoverImage="qfilter.png" downImage="qfilter_down.png" upToolTip="Filter the playlist" downToolTip="Filter is on"/>
                <QUEUESORTBUTTON id="bQSort" left="{{W - 46}}" top="0" image="qsort.png" hoverImage="qsort.png" downImage="qsort_down.png" disabledImage="qsort_disabled.png" upToolTip="Sort and playlist tools"/>
              </SUBVIEW>

              <SUBVIEW id="paneRadio" left="0" top="0" width="{{W}}" height="{{H - Bar - Nav}}" visible="false">
                <LISTPANE kind="radio" left="0" top="0" width="{{W}}" height="{{H - Bar - Nav}}" horizontalAlignment="stretch" verticalAlignment="stretch"/>
              </SUBVIEW>

              <SUBVIEW id="paneSkins" left="0" top="0" width="{{W}}" height="{{H - Bar - Nav}}" visible="false">
                <LISTPANE kind="settings" left="0" top="0" width="{{W}}" height="{{H - Bar - Nav}}" horizontalAlignment="stretch" verticalAlignment="stretch"/>
              </SUBVIEW>
            </SUBVIEW>

            <!-- Top bar: logo, page title, the three-dot menu (sits below the status bar: the host pads the view). -->
            <SUBVIEW id="svBar" left="0" top="0" width="{{W}}" height="{{Bar}}" backgroundImage="bar.png" backgroundTiled="true">
              {{barBack}}
              <TEXT id="tPage" left="{{titleLeft}}" top="14" width="200" height="24" value="Now Playing" fontType="{{f}}" fontSize="12" fontStyle="bold" foregroundColor="{{p.BarInk}}"/>
              <BUTTON id="bMenu" left="0" top="0" image="menu_hit.png" hoverImage="menu_hit.png" downImage="menu_hit.png" upToolTip="Menu" onClick="pardeh.command('menu', 'app');"/>
            </SUBVIEW>

            <!-- Bottom navigation: replaces the task rail. -->
            <SUBVIEW id="svNav" left="0" top="{{H - Nav}}" width="{{W}}" height="{{Nav}}" backgroundImage="nav.png" backgroundTiled="true">
        {{navItems}}
            </SUBVIEW>
          </VIEW>
        </THEME>
        """;
    }

    private const string ScriptHead = """
        // Pardeh phone full mode - original script. The layout is computed here from the view size.
        var g_pane = "";
        var g_w = 0, g_ch = 0;
        var BAR = 52, NAV = 56, BTN = 64;
        var KEYS = ["now", "library", "queue", "radio", "skins"];
        var NAVBTN = { now: "navNow", library: "navLibrary", queue: "navQueue", radio: "navRadio", skins: "navSettings" };
        var PANES = { now: "paneNow", library: "paneLibrary", queue: "paneQueue", radio: "paneRadio", skins: "paneSkins" };
        var TITLES = { now: "Now Playing", library: "Library", queue: "Playlist", radio: "Radio", skins: "Settings" };
        var INK = "__INK__";
        var SEL = "__SEL__";
        var g_lyr = false;

        // The Lyrics button: the stage shows the lyrics instead of the visualization and the cover (not over video).
        function ToggleLyrics() { g_lyr = !g_lyr; ApplyLyrics(); }
        function ApplyLyrics() {
            var video = !!(player.currentMedia && player.currentMedia.imageSourceWidth > 0);
            if (video) g_lyr = false;
            lyr.visible = g_lyr;
            if (!video) { vis.visible = !g_lyr; cover.visible = !g_lyr; }
            bLyr.down = g_lyr;
            bLyr.visible = !video;
            Relayout();
        }

        function OnLoad() {
            var pane = theme.loadPreference("Pane");
            g_pane = pane && NAVBTN[pane] && !IsHidden(pane) ? pane : "now";
            ShowPane(g_pane);
            OnOpenState(player.openState);
        }

        function OnClose() {
            theme.savePreference("Pane", g_pane);
        }

        function IsHidden(name) {
            return name != "now" && ("," + pardeh.command("hiddenPages") + ",").indexOf("," + name + ",") >= 0;
        }

        // Settings > Pages: hide unticked pages from the navigation bar and spread the rest.
        function ApplyPages() {
            if (g_pane != "" && IsHidden(g_pane)) ShowPane("now");
            else Relayout();
        }

        // Android Back: the library's list returns to its tree, any other page returns to Now Playing.
        function OnBack() {
            if (g_pane == "library" && library.back()) return true;
            if (typeof BackNow == "function" && BackNow()) return true;
            // The lyrics view is a state of Now Playing: Back returns to the stage first.
            if (g_lyr && g_pane == "now") { g_lyr = false; ApplyLyrics(); return true; }
            if (g_pane != "now") { ShowPane("now"); return true; }
            return false;
        }

        // Android: a horizontal swipe moves to the next (dir 1) or previous (dir -1) page of the navigation bar.
        function SwipePage(dir) {
            var shown = [];
            for (var i = 0; i < KEYS.length; i++) if (!IsHidden(KEYS[i])) shown.push(KEYS[i]);
            var at = shown.indexOf(g_pane), to = at + dir;
            if (at < 0 || to < 0 || to >= shown.length) return false;
            ShowPane(shown[to]);
            return true;
        }

        function ShowPane(name) {
            if (!NAVBTN[name] || IsHidden(name)) name = "now";
            g_pane = name;
            for (var i = 0; i < KEYS.length; i++) {
                var key = KEYS[i];
                eval(PANES[key]).visible = key == name;
                eval(NAVBTN[key]).down = key == name;
                eval(NAVBTN[key] + "Label").foregroundColor = key == name ? SEL : INK;
            }
            tPage.value = TITLES[name];
            Relayout();
        }

        function Relayout() {
            var w = view.width, h = view.height;
            var cw = w, ch = h - BAR - NAV;
            if (ch < 40) return;

            svBar.width = w;
            svContent.width = cw;
            svContent.height = ch;
            for (var key in PANES) {
                var pane = eval(PANES[key]);
                pane.width = cw;
                pane.height = ch;
            }
            bQFilter.left = cw - 90; bQSort.left = cw - 46;
            svNav.top = h - NAV;
            svNav.width = w;
            g_w = w; g_ch = ch;

            // Navigation: the visible pages share the width.
            var shown = [];
            for (var i = 0; i < KEYS.length; i++) {
                var visible = !IsHidden(KEYS[i]);
                eval(NAVBTN[KEYS[i]]).visible = visible;
                eval(NAVBTN[KEYS[i]] + "Label").visible = visible;
                if (visible) shown.push(KEYS[i]);
            }
            var slot = w / shown.length;
            for (var j = 0; j < shown.length; j++) {
                var button = eval(NAVBTN[shown[j]]);
                var label = eval(NAVBTN[shown[j]] + "Label");
                button.left = Math.round(slot * j + (slot - (button.width > 0 ? button.width : 72)) / 2);
                label.left = Math.round(slot * j);
                label.width = Math.round(slot);
            }

            LayoutNow(w, h, ch);
        }
        """;

    private const string DefaultNowScript = """

        function LayoutNow(w, h, ch) {
            // Now Playing: stage on top and controls below in portrait, side by side in landscape.
            var land = w > h * 1.15;
            var m = 14;
            var sx, sy, sw, sh, x0, colW, y0;
            if (land) {
                sx = 10; sy = 8; sh = ch - 16;
                sw = Math.round(Math.min(w * 0.5, sh * 1.5));
                x0 = sx + sw + 6; colW = w - x0 - 4;
                y0 = Math.max(2, Math.round((ch - 214) / 2));
                m = 10;
            } else {
                sx = m; sy = 10; sw = w - 2 * m;
                y0 = ch - 224;
                sh = y0 - 10 - 12;
                x0 = 0; colW = w;
            }
            if (sh < 40) sh = 40;
            svStage.left = sx; svStage.top = sy; svStage.width = sw; svStage.height = sh;

            // The heart (44 units) sits at the right of title and artist; both stay centred, with the same room kept on the left.
            tTitle.left = x0 + m + 46; tTitle.top = y0; tTitle.width = colW - 2 * m - 92;
            tArtist.left = x0 + m + 46; tArtist.top = y0 + 24; tArtist.width = colW - 2 * m - 92;
            bFav.left = x0 + colW - m - 44; bFav.top = y0 - 1;
            bLyr.left = x0 + m; bLyr.top = y0 - 1;   // the mirror of the heart: the title stays centred between them
            seek.left = x0 + m; seek.top = y0 + 48; seek.width = colW - 2 * m;
            tPos.left = x0 + m; tPos.top = y0 + 80;
            tDur.left = x0 + colW - m - 90; tDur.top = y0 + 80;
            var cx = x0 + colW / 2;
            bPlay.left = Math.round(cx - 32); bPlay.top = y0 + 98;
            bPause.left = Math.round(cx - 32); bPause.top = y0 + 98;
            bPrev.left = Math.round(cx - 32 - 18 - 52); bPrev.top = y0 + 104;
            bNext.left = Math.round(cx + 32 + 18); bNext.top = y0 + 104;
            // Skip 10 s: outside previous / next (44 units, 6 apart), kept inside the column.
            bSkipB.left = Math.max(x0 + 2, Math.round(cx - 32 - 18 - 52 - 6 - 44)); bSkipB.top = y0 + 108;
            bSkipF.left = Math.min(x0 + colW - 46, Math.round(cx + 32 + 18 + 52 + 6)); bSkipF.top = y0 + 108;
            var ry = y0 + 170;
            bShuffle.left = x0 + 6; bShuffle.top = ry;
            bMute.left = x0 + 52; bMute.top = ry;
            vol.left = x0 + 100; vol.top = ry + 7; vol.width = colW - 100 - 56;
            bRepeat.left = x0 + colW - 50; bRepeat.top = ry;
        }
        """;

    private const string ScriptTail = """

        function OnOpenState(state) {
            if (state != osMediaOpen) return;
            var video = player.currentMedia && player.currentMedia.imageSourceWidth > 0;
            vid.visible = video;
            vis.visible = !video;
            cover.visible = !video;
            if (video && g_pane != "now") ShowPane("now");
            ApplyLyrics();
        }
        """;
}
