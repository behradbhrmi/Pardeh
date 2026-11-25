using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Pardeh.UI.Rendering;
using static Pardeh.UI.FullMode.LunaArt;

namespace Pardeh.UI.FullMode;

/// <summary>
/// The PC versions of the retro device looks: real landscape full modes on <see cref="FullModeDesigner"/>'s layout (task
/// rail, stage, playlist, library, transport; resizable, mouse and keyboard). Each one takes its colours from the phone
/// look's palette and its art from the phone look's own pictures, which are drawn again and fitted into the desktop
/// pieces: the wallpaper behind the playlist, the bars, the transport keys, toggles and sliders. What the rail looks
/// like (a list of large words, or tabs of the device) is each look's own.
/// </summary>
internal static partial class PhoneSkin
{
    /// <summary>The PC looks: setting key, name, and the phone look each comes from.</summary>
    internal static readonly (string Key, string Name, string Phone)[] DesktopRetro =
    [
        ("zune", "Zune HD (PC)", "zune-phone"),
        ("wm6", "Windows Mobile 6 (PC)", "wm6-phone"),
        ("metro", "Windows Phone 7 (PC)", "metro-phone"),
        ("kin", "Kin Two (PC)", "kin-phone"),
        ("blades", "Xbox 360 Blades (PC)", "blades-phone"),
        ("xbox", "Xbox Classic (PC)", "xbox-phone"),
        ("walkman", "Walkman Orange (PC)", "walkman-phone"),
        ("winamp", "Winamp (PC)", "winamp-phone"),
        ("groove", "Groove (PC)", "groove-phone"),
    ];

    private static bool LightLook(Palette p) => p.Key == "blades-phone";

    private static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    internal static IReadOnlyList<FullModeDesign> DesktopRetroDesigns() => DesktopRetro.Select(DesktopDesign).ToList();

    private static FullModeDesign DesktopDesign((string Key, string Name, string Phone) entry)
    {
        // The retro looks bring their own palette and layout choices; Winamp and Groove are palettes of the shared generator.
        var look = Retro(entry.Phone);
        var p = look?.Palette ?? Pal(entry.Phone) ?? throw new InvalidOperationException("No phone look " + entry.Phone);
        var art = ArtLook(entry.Key);
        var winamp = entry.Key == "winamp";
        var light = LightLook(p);
        var ink = Color.Parse(p.ListInk);
        var ground = p.Back;
        var raised = Mix(ground, light ? Colors.Black : Colors.White, 0.08);
        var line = Mix(ground, light ? Colors.Black : Colors.White, 0.22);
        // Words-only rails are what Zune, Windows Phone and Kin looked like; the others keep tabs of their own.
        var words = entry.Key is "zune" or "metro" or "kin";
        // The toolbars and column headers need a ground that takes white type in every look (the tool icons are white).
        var bar = entry.Key switch
        {
            "blades" => Color.Parse("#4E7C1E"),
            "wm6" => Color.Parse("#14601A"),
            "zune" or "metro" => Color.Parse("#1C1C1C"),
            "kin" => Color.Parse("#232323"),
            "xbox" => Color.Parse("#17300A"),
            "winamp" => Color.Parse("#161D2A"),
            "groove" => Color.Parse("#0F3E98"),
            _ => Color.Parse("#24242A"),
        };
        var barInk = Colors.White;
        var paneInk = light ? Color.Parse("#1F2A20") : ink;
        var dim = light ? Color.Parse("#56635A") : Color.Parse(p.ArtistInk);
        var selection = light ? Color.Parse("#CBE29B") : Color.Parse(p.ListSelect);
        var playing = Color.Parse(p.ListPlaying);
        var railInk = words ? (light ? Color.Parse("#5A5A5A") : Color.Parse("#8A8A8A")) : paneInk;
        var railOn = entry.Key == "metro" ? p.Accent : light ? Color.Parse("#10200C") : Colors.White;

        return new FullModeDesign
        {
            Key = entry.Key,
            Name = entry.Name,
            Modern = true,
            Font = p.Font,
            Outline = p.BarLine == p.BarTop ? line : p.BarLine,
            BarLight = p.BarTop, BarDark = p.BarBottom, BarHighlight = p.BarTop, BarLine = p.BarLine,
            LogoLight = p.BarTop, LogoMid = p.BarTop, LogoDark = p.BarBottom,
            MenuInk = p.BarInk, MenuHover = Hex(p.Accent),
            TabLight = p.BarTop, TabDark = p.BarBottom, TabBorder = p.BarLine,
            RailLight = ground, RailMid = ground, RailDark = ground, RailEdge = line,
            CapLight = ground, CapDark = ground, CapInk = Hex(dim), RailInk = railInk,
            PillLight = raised, PillDark = raised, PillBorder = p.Accent, HoverBorder = raised,
            Silver = p.NavTop, SilverMid = Mix(p.NavTop, p.NavBottom, 0.5), SilverDark = p.NavBottom, SilverLine = p.NavLine,
            Panel = Hex(ground), PanelDark = raised, PanelDarker = raised, PanelLight = line,
            HeaderTop = bar, HeaderBottom = bar,
            PanelText = Hex(paneInk), PanelSub = Hex(dim), PanelAccent = Hex(dim), Rule = Hex(line), InfoBack = Hex(ground),
            StageGlow = p.StageGlow,
            StatusBack = Hex(bar), StatusInk = light ? "#E8FF9A" : p.ListPlaying, StatusSub = "#D0D0D0",
            TitleInk = "#FFFFFF", ArtistInk = light ? "#D8E8C0" : p.ArtistInk,
            LcdTop = ground, Lcd = ground, LcdBorder = line, LcdInk = Hex(winamp ? p.Accent : paneInk),
            Glyph = entry.Key == "blades" ? Color.Parse("#243020") : p.Dim,
            GlyphDisabled = Mix(p.NavBottom, p.Dim, 0.4),
            OrbMid = p.OrbTop, OrbDark = p.OrbBottom, OrbHoverMid = p.OrbTop, OrbHoverDark = p.OrbBottom,
            OrbRim = p.OrbRim, OrbHeroRim = p.OrbRim, OrbHoverRim = p.Accent,
            SeekTop = p.Track, SeekBottom = p.Track, SeekLine = p.Track,
            FillLight = p.Fill, FillMid = p.Fill, FillDark = p.Fill,
            VolumeLight = p.Fill, VolumeDark = p.Fill,
            EdgeLight = p.NavBottom, EdgeDark = p.NavBottom,
            Retro = new DesktopRetroStyle
            {
                PhoneKey = entry.Phone,
                Words = words,
                Lowercase = look?.Lowercase == true,
                RailFont = words ? look?.TitleFont ?? p.Font : p.Font,
                RailSize = words ? (entry.Key == "kin" ? 11 : 12) : 8,
                // The other rails put the open page on a bright tab: white reads on all the dark ones. Winamp lights it in amber.
                RailInkOn = Hex(winamp ? p.Accent : words ? railOn : light ? Color.Parse("#10200C") : Colors.White),
                RailInkOff = Hex(art ? p.NavInk : words ? railInk : light ? Color.Parse("#33422C") : paneInk),
                Framed = art,
                TiledBack = winamp,
                Equalizer = winamp,
                Dark = !light,
                PaneAttributes =
                    $"libBack=\"{Hex(ground)}\" libBar=\"{Hex(bar)}\" libBarInk=\"{Hex(barInk)}\" libInk=\"{Hex(paneInk)}\" libDim=\"{Hex(dim)}\" libSel=\"{Hex(selection)}\" libAccent=\"{Hex(playing)}\"",
                PlaylistAttributes =
                    $"itemPlayingColor=\"{Hex(playing)}\" itemSelectedBackgroundColor=\"{Hex(selection)}\" itemPlayingBackgroundColor=\"{Hex(selection)}\"",
                LyricsBack = light ? "#1E2A18" : Hex(ground),
                LyricsBar = Hex(bar),
                LyricsAccent = Hex(light ? Color.Parse("#9BE03C") : p.Accent),
                Art = files => DesktopArt(entry.Key, p, files),
            },
        };
    }

    /// <summary>The colours of the lyrics panel over a PC retro look, or null for other looks.</summary>
    internal static LyricsPanelTheme? DesktopLyricsTheme(string key) =>
        FullModeDesign.All.FirstOrDefault(d => d.Key == key)?.Retro is { } style
            ? new LyricsPanelTheme(style.LyricsBack, style.LyricsBar, "#FFFFFF", style.LyricsAccent, true, "Segoe UI")
            : null;

    // ---- Art: the phone look's pictures, fitted into the desktop pieces --------------------------------

    /// <summary>The phone look's pictures at twice the skin's resolution, so that fitting them to other sizes keeps them sharp.</summary>
    private static Dictionary<string, byte[]> PhonePictures(Palette p, int density)
    {
        var pictures = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var before = ArtScale;
        ArtScale = density;
        try
        {
            AddChrome(pictures, p);
            AddButtons(pictures, p);
            AddNavIcons(pictures, p);
            AddSliders(pictures, p);
            AddLookArt(pictures, p);
            AddDesktopExtraArt(pictures, p);
        }
        finally
        {
            ArtScale = before;
        }

        return pictures;
    }

    private static void DesktopArt(string key, Palette p, Dictionary<string, byte[]> files)
    {
        var art = ArtLook(key);
        var density = Math.Clamp(LunaArt.DefaultScale, 2, 4);
        var phone = PhonePictures(p, density);
        var loaded = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        Bitmap Phone(string name)
        {
            if (!loaded.TryGetValue(name, out var bitmap))
                loaded[name] = bitmap = new Bitmap(new MemoryStream(phone[name]));
            return bitmap;
        }

        // The soft backdrops of Winamp and Groove are drawn at one pixel per unit, whatever the density.
        int PixelsPerUnit(string name) => name == "back.png" ? 1 : density;

        // Part of a phone picture (in skin units) drawn into `to`.
        void Part(DrawingContext g, string name, Rect from, Rect to)
        {
            var per = PixelsPerUnit(name);
            using (g.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
                g.DrawImage(Phone(name), new Rect(from.X * per, from.Y * per, from.Width * per, from.Height * per), to);
        }

        Rect Whole(string name) => new(0, 0, Phone(name).PixelSize.Width / (double)PixelsPerUnit(name), Phone(name).PixelSize.Height / (double)PixelsPerUnit(name));

        // A picture that fills `to` without being squeezed: scaled to cover it, the bottom right kept (that is where the art is).
        void Cover(DrawingContext g, string name, Rect to)
        {
            var whole = Whole(name);
            var scale = Math.Max(to.Width / whole.Width, to.Height / whole.Height);
            var (w, h) = (to.Width / scale, to.Height / scale);
            Part(g, name, new Rect(whole.Width - w, whole.Height - h, w, h), to);
        }

        byte[] Draw(int width, int height, Action<DrawingContext> draw) => LunaArt.Png(width, height, draw, density);

        // The top bar and the transport are the phone's header and navigation bar. The pieces between the ends are one unit
        // wide and repeated; Winamp's brushed metal needs its whole width there (its strokes wrap), or the middle looks flat.
        var brushed = key == "winamp";
        var midWidth = brushed ? 360 : 1;
        void Bar38(DrawingContext g, string name, double width, double height)
        {
            var whole = Whole(name);
            if (!brushed)
            {
                Part(g, name, whole, new Rect(0, 0, width, height));
                return;
            }

            using (g.PushClip(new Rect(0, 0, width, height)))
                for (var x = 0.0; x < width; x += whole.Width)
                    Part(g, name, whole, new Rect(x, 0, whole.Width, height));
        }

        void TopBar(DrawingContext g, double width) => Bar38(g, "bar.png", width, 38);
        void BottomBar(DrawingContext g, double width) => Bar38(g, "nav.png", width, 78);
        files["top_left.png"] = Draw(300, 38, g =>
        {
            TopBar(g, 300);
            PardehLogo.Draw(g, new Rect(9, 7, 24, 24));
        });
        files["top_mid.png"] = Draw(midWidth, 38, g => TopBar(g, midWidth));
        files["top_right.png"] = Draw(270, 38, g => TopBar(g, 270));
        files["transport_mid.png"] = Draw(midWidth, 78, g => BottomBar(g, midWidth));
        files["transport_right.png"] = Draw(230, 78, g =>
        {
            BottomBar(g, 230);
            g.DrawRectangle(new SolidColorBrush(WithAlpha(LightLook(p) ? Colors.White : Colors.Black, 150)),
                Pen(WithAlpha(p.Accent, 110)), new RoundedRect(new Rect(14.5, 36.5, 120, 26), key is "zune" or "metro" or "kin" ? 0 : 5));
            var dots = new SolidColorBrush(WithAlpha(p.Dim, 150));
            foreach (var (dx, dy) in new[] { (0, 8), (4, 4), (4, 8), (8, 0), (8, 4), (8, 8) })
                g.FillRectangle(dots, new Rect(230 - 17 + dx, 78 - 16 + dy, 2, 2));
        });
        files["corner.png"] = Draw(140, 78, g =>
        {
            BottomBar(g, 140);
            PardehLogo.Draw(g, new Rect(14, 20, 38, 38));
        });

        // The window's own corners are the rail's and the transport's colours.
        files["edge.png"] = Draw(4, 1, g => g.FillRectangle(new SolidColorBrush(p.Back), new Rect(0, 0, 4, 1)));

        // The wallpaper stands behind the playlist (tall, as on the phone) and behind an idle stage. Winamp's brushed metal
        // is repeated at its own size: enlarged, the brush strokes turn into stripes.
        var wall = art ? "back.png" : "wall.png";
        void Backdrop(DrawingContext g, Rect to)
        {
            if (key != "winamp")
            {
                Cover(g, wall, to);
                return;
            }

            var tile = Whole(wall);
            using (g.PushClip(to))
                for (var y = to.Y; y < to.Bottom; y += tile.Height)
                    for (var x = to.X; x < to.Right; x += tile.Width)
                        Part(g, wall, tile, new Rect(x, y, tile.Width, tile.Height));
        }

        files["list_wall.png"] = Draw(260, 424, g => Backdrop(g, new Rect(0, 0, 260, 424)));
        files["stage_glow.png"] = Draw(376, 402, g =>
        {
            Backdrop(g, new Rect(0, 0, 376, 402));
            g.FillRectangle(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), new Rect(0, 0, 376, 402));
        });
        files["album_frame.png"] = Draw(84, 84, g => Part(g, "cover_frame.png", Whole("cover_frame.png"), new Rect(0, 0, 84, 84)));

        if (art)
        {
            // The framed player cards of the phone page: around the stage, and behind the cover and its details.
            files["pane_back.png"] = key == "winamp"
                ? Draw(360, 240, g => Backdrop(g, new Rect(0, 0, 360, 240)))
                : Draw(180, 360, g => Backdrop(g, new Rect(0, 0, 180, 360)));
            files["stage_card.png"] = Draw(64, 64, g => Part(g, "np_frame.png", Whole("np_frame.png"), new Rect(0, 0, 64, 64)));
            var plate = key == "winamp" ? "np_deck.png" : "np_frame.png";
            files["deck_card.png"] = Draw(64, 64, g => Part(g, plate, Whole(plate), new Rect(0, 0, 64, 64)));
        }

        if (key == "winamp")
        {
            // The task rail is brushed too.
            files["rail_tile.png"] = Draw(140, 240, g => Backdrop(g, new Rect(0, 0, 140, 240)));
            files["rail_cap.png"] = Draw(140, 26, g => Backdrop(g, new Rect(0, 0, 140, 26))); // the cap is 26 units high
            EqualizerDrawerArt(files, density);
        }

        // Transport keys: the phone's, without the empty rim of their touch targets.
        void Key(string desktop, string source, int size, double inset)
        {
            var whole = Whole(source + ".png");
            files[desktop + ".png"] = Draw(size, size, g => Part(g, source + ".png", whole.Deflate(inset), new Rect(0, 0, size, size)));
        }

        foreach (var (suffix, from) in new[] { ("", ""), ("_hover", ""), ("_down", "_down"), ("_disabled", "_disabled") })
        {
            Key("play" + suffix, "play" + from, 48, 0.5);
            Key("pause" + suffix, "pause" + from, 48, 0.5);
            Key("stop" + suffix, "stop" + from, 32, 4);
            Key("prev" + suffix, "prev" + from, 32, 4);
            Key("next" + suffix, "next" + from, 32, 4);
            var toggleState = suffix == "_down" ? "_down" : string.Empty;
            // The metal discs of Winamp and Groove's lit circle are wider than the retro looks' bare glyphs.
            var rim = art ? 2 : 5;
            Key("shuffle" + suffix, "shuffle" + toggleState, 28, rim);
            Key("repeat" + suffix, "repeat" + toggleState, 28, rim);
            Key("mute" + suffix, "mute" + toggleState, 28, rim);
            Key("muted" + suffix, "muted_down", 28, rim);
        }

        if (key == "winamp")
        {
            // The transport's last key opens the equalizer drawer.
            Key("eq", "eqkey", 28, 3);
            Key("eq_hover", "eqkey_down", 28, 3);
            Key("eq_down", "eqkey_down", 28, 3);
        }

        // Hovering a key lights it, since the phone art has no hover state of its own.
        foreach (var name in new[] { "play", "pause", "stop", "prev", "next", "shuffle", "repeat", "mute" })
        {
            var normal = new Bitmap(new MemoryStream(files[name + ".png"]));
            var side = name is "play" or "pause" ? 48 : name is "stop" or "prev" or "next" ? 32 : 28;
            files[name + "_hover.png"] = Draw(side, side, g =>
            {
                g.DrawEllipse(new SolidColorBrush(WithAlpha(p.Accent, 55)), null, new Point(side / 2.0, side / 2.0), side / 2.0 - 1, side / 2.0 - 1);
                using (g.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
                    g.DrawImage(normal, new Rect(0, 0, normal.PixelSize.Width, normal.PixelSize.Height), new Rect(0, 0, side, side));
            });
        }

        // Sliders: the middle band of the phone's 30 unit touch strips, and its thumb made smaller. The seek bar is tiled, so
        // from a capsule only the straight middle is taken.
        var middle = new Rect(art ? 9 : 0, 8, 6, 14);
        files["seek_track.png"] = Draw(6, 14, g => Part(g, "seek_track.png", middle, new Rect(0, 0, 6, 14)));
        files["seek_fill.png"] = Draw(6, 14, g => Part(g, "seek_fill.png", middle, new Rect(0, 0, 6, 14)));
        files["seek_thumb.png"] = Draw(16, 16, g => Part(g, "thumb.png", new Rect(3, 3, 24, 24), new Rect(0, 0, 16, 16)));
        files["seek_thumb_hover.png"] = Draw(16, 16, g => Part(g, "thumb_hover.png", new Rect(2, 2, 26, 26), new Rect(0, 0, 16, 16)));
        foreach (var (volume, source) in new[] { ("volume_track.png", "seek_track.png"), ("volume_fill.png", "seek_fill.png") })
        {
            files[volume] = Draw(80, 14, g =>
            {
                if (!art)
                {
                    Part(g, source, new Rect(0, 8, 6, 14), new Rect(4, 0, 72, 14));
                    return;
                }

                // A 24 unit capsule: its round ends keep their shape, the middle is stretched.
                Part(g, source, new Rect(0, 8, 9, 14), new Rect(4, 0, 9, 14));
                Part(g, source, new Rect(9, 8, 6, 14), new Rect(13, 0, 54, 14));
                Part(g, source, new Rect(15, 8, 9, 14), new Rect(67, 0, 9, 14));
            });
        }
        files["volume_thumb.png"] = Draw(14, 14, g => Part(g, "thumb.png", new Rect(3, 3, 24, 24), new Rect(0, 0, 14, 14)));
        files["volume_thumb_hover.png"] = Draw(14, 14, g => Part(g, "thumb_hover.png", new Rect(2, 2, 26, 26), new Rect(0, 0, 14, 14)));

        files["dropdown_frame.png"] = Draw(12, 12, g =>
            g.DrawRectangle(new SolidColorBrush(WithAlpha(LightLook(p) ? Colors.White : Colors.Black, 170)),
                Pen(WithAlpha(p.Accent, 120)), new RoundedRect(new Rect(0.5, 0.5, 11, 11), key is "zune" or "metro" or "kin" ? 0 : 3)));

        DesktopRail(key, p, files, density);
    }

    /// <summary>The task rail's two states. Word rails mark the open page with a small stroke or square; the others with a tab.</summary>
    private static void DesktopRail(string key, Palette p, Dictionary<string, byte[]> files, int density)
    {
        byte[] Item(Action<DrawingContext> draw) => LunaArt.Png(128, 40, draw, density);

        files["rail_item.png"] = Item(g => g.FillRectangle(new SolidColorBrush(Color.FromArgb(2, 128, 128, 128)), new Rect(0, 0, 128, 40)));
        files["rail_item_hover.png"] = Item(g => g.FillRectangle(new SolidColorBrush(Color.FromArgb(26, 128, 128, 128)), new Rect(0, 3, 128, 34)));
        files["rail_item_down.png"] = Item(g =>
        {
            g.FillRectangle(new SolidColorBrush(Color.FromArgb(2, 128, 128, 128)), new Rect(0, 0, 128, 40));
            switch (key)
            {
                case "zune": // a short stroke in the pink to orange accent
                    g.FillRectangle(Horizontal((ZunePink, 0), (ZuneOrange, 1)), new Rect(8, 33, 40, 3));
                    break;
                case "metro": // the Windows Phone accent square
                    g.FillRectangle(new SolidColorBrush(p.Accent), new Rect(0, 16, 5, 9));
                    break;
                case "kin": // a bright square of the mosaic
                    g.FillRectangle(new SolidColorBrush(WithAlpha(p.Accent, 210)), new Rect(0, 4, 128, 32));
                    break;
                case "winamp" or "groove":
                    ArtRailItem(g, key, p);
                    break;
                case "wm6": // the Start menu's highlighted row
                    Gel(g, new Rect(0.5, 3.5, 127, 33), 4, Color.Parse("#7CC862"), Color.Parse("#1F6E1C"), Color.Parse("#E9FFE0"), 90);
                    break;
                case "blades": // an open blade: white, with its coloured edge on the right
                    var blade = Geo("M 0,3 L 116,3 Q 126,20 116,37 L 0,37 Z");
                    g.DrawGeometry(Vertical((Colors.White, 0), (Color.Parse("#E3EAD8"), 1)), Pen(Color.Parse("#9AA890")), blade);
                    g.DrawGeometry(null, Pen(p.Accent, 4), Geo("M 117,4 Q 126.5,20 117,36"));
                    break;
                case "xbox": // a panel with cut corners
                    var panel = Geo("M 8,3.5 L 127,3.5 L 127,28 L 118,36.5 L 0.5,36.5 L 0.5,11 Z");
                    g.DrawGeometry(Vertical((Color.Parse("#7BC70E"), 0), (Color.Parse("#2D5503"), 1)), Pen(p.Accent, 1.2), panel);
                    break;
                default: // walkman: a glossy orange key
                    Gel(g, new Rect(0.5, 3.5, 127, 33), 8, Color.Parse("#FF9A2E"), Color.Parse("#C4500A"), Color.Parse("#FFD0A0"), 110);
                    break;
            }
        });
    }
}

/// <summary>What a PC retro look changes in <see cref="FullModeDesigner"/>'s shared layout and drawing.</summary>
internal sealed record DesktopRetroStyle
{
    public required string PhoneKey { get; init; }

    /// <summary>The rail is a list of large words (Zune, Windows Phone, Kin), without a "Tasks" caption.</summary>
    public bool Words { get; init; }

    public bool Lowercase { get; init; }

    public string RailFont { get; init; } = "Segoe UI";

    public int RailSize { get; init; } = 8;

    public string RailInkOn { get; init; } = "#FFFFFF";

    public string RailInkOff { get; init; } = "#FFFFFF";

    public bool Dark { get; init; } = true;

    /// <summary>The stage and the cover sit in framed cards on the look's backdrop (Winamp, Groove).</summary>
    public bool Framed { get; init; }

    /// <summary>The backdrop behind the framed stage is a tile (brushed metal) rather than one stretched picture.</summary>
    public bool TiledBack { get; init; }

    /// <summary>The transport's last key opens a ten band equalizer drawer over the stage (Winamp).</summary>
    public bool Equalizer { get; init; }

    /// <summary>The colour attributes of the Library and the list panes (opt-in in the renderer).</summary>
    public string PaneAttributes { get; init; } = string.Empty;

    public string PlaylistAttributes { get; init; } = string.Empty;

    public string LyricsBack { get; init; } = "#101010";

    public string LyricsBar { get; init; } = "#202020";

    public string LyricsAccent { get; init; } = "#60CDFF";

    /// <summary>Replaces the designer's pictures with the look's own.</summary>
    public required Action<Dictionary<string, byte[]>> Art { get; init; }
}
