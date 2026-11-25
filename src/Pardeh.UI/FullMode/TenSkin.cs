using System.Text;
using Avalonia;
using Avalonia.Media;
using Pardeh.Skinning.Model;
using Pardeh.Skinning.Wmp;
using static Pardeh.UI.FullMode.LunaArt;

namespace Pardeh.UI.FullMode;

/// <summary>
/// "Pardeh Ten" (look key "aero10"): a full mode in the spirit of the 2004 player on Windows XP's Luna theme:
/// a glossy light-blue frame, a title row, a darker blue task band with raised tabs and a curved corner at the right,
/// a blue toolbar row, a body (the stage or the library on the left, a medium-blue list pane on the right), a status
/// row, and a pale glossy transport bar with a big round play button. All art is drawn here (original drawing, Pardeh's
/// own name and logo).
/// </summary>
internal static class TenSkin
{
    public const string SourcePath = "pardeh://full-mode/aero10";

    // Design size and metrics (skin pixels).
    private const int W = 800, H = 540;
    private const int F = 7;                          // side frame
    private const int TitleH = 28, BandH = 34, Top = TitleH + BandH;
    private const int ToolH = 28, BodyTop = Top + ToolH;
    private const int StatusH = 28, Transport = 78;
    private const int BodyH = H - BodyTop - StatusH - Transport;
    private const int BodyW = W - 2 * F, ListW = 232, LeftW = BodyW - ListW;
    private const int TopLeftW = 16, TopRightW = 290;
    private const int StatusTop = BodyTop + BodyH;

    // Palette (own values, eyeballed from a reference capture).
    private static readonly Color Outline = Color.Parse("#1E3B99");
    private static readonly Color FrameLight = Color.Parse("#7B9BE8");
    private static readonly Color FrameDark = Color.Parse("#3A5FCD");
    private static readonly Color Ink = Color.Parse("#1B3A8C");

    private static Color C(string hex) => Color.Parse(hex);

    private static IBrush Solid(Color c) => new SolidColorBrush(c);

    private static IBrush Solid(string c) => new SolidColorBrush(Color.Parse(c));

    public static SkinPackage Build()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        AddChrome(files);
        AddTabs(files);
        AddButtons(files);
        AddIcons(files);
        files["aero10.wms"] = Encoding.UTF8.GetBytes(Layout);
        files["aero10.js"] = Encoding.UTF8.GetBytes(Script);
        return WmzLoader.FromFiles(files, SourcePath);
    }

    // ---- Chrome ---------------------------------------------------------------------------------

    private static void TopBand(DrawingContext g, double w)
    {
        // Title row (pale blue) and task band (medium blue).
        g.FillRectangle(Vertical((C("#C2D6F8"), 0), (C("#9DBBF0"), 0.55), (C("#86A9EA"), 1)), new Rect(0, 0, w, TitleH));
        g.FillRectangle(Vertical((C("#6C93E4"), 0), (C("#4B76D8"), 0.5), (C("#3B64CC"), 1)), new Rect(0, TitleH, w, BandH));
        g.FillRectangle(Solid(Color.FromArgb(200, 255, 255, 255)), new Rect(0, 1, w, 1));
        g.FillRectangle(Solid("#2F52B8"), new Rect(0, TitleH, w, 1));
        g.FillRectangle(Solid(Color.FromArgb(70, 255, 255, 255)), new Rect(0, TitleH + 1, w, 1));
        g.FillRectangle(Solid(Outline), new Rect(0, 0, w, 1));
    }

    private static void TopLeft(DrawingContext g)
    {
        using (g.PushClip(new RoundedRect(new Rect(0, 0, TopLeftW, Top + 10), 9, 0, 0, 0)))
        {
            TopBand(g, TopLeftW);
            g.FillRectangle(Solid(Outline), new Rect(0, 0, 1, Top));
            g.DrawGeometry(null, Pen(Outline), Geo("M 0.5,40 L 0.5,9.5 Q 0.5,0.5 9.5,0.5 L 16,0.5"));
        }
    }

    private static void TopRight(DrawingContext g)
    {
        using (g.PushClip(new RoundedRect(new Rect(-10, 0, TopRightW + 10, Top + 10), 0, 9, 0, 0)))
        {
            TopBand(g, TopRightW);
            g.DrawGeometry(null, Pen(Outline), Geo(FormattableString.Invariant($"M 0,0.5 L {TopRightW - 9.5},0.5 Q {TopRightW - 0.5},0.5 {TopRightW - 0.5},9.5 L {TopRightW - 0.5},{Top}")));
        }

        // The glossy curved region in the task band: a pale swoosh rising to the right, holding the logo.
        var swoosh = Geo(FormattableString.Invariant($"M 70,{Top} Q 100,{TitleH + 8} 150,{TitleH + 6} L {TopRightW - 1},{TitleH + 6} L {TopRightW - 1},{Top} Z"));
        g.DrawGeometry(Vertical((C("#9EBCF2"), 0), (C("#6F97E6"), 0.5), (C("#4F7ADB"), 1)), null, swoosh);
        g.DrawGeometry(null, Pen(Color.FromArgb(190, 255, 255, 255)), Geo(FormattableString.Invariant($"M 70,{Top} Q 100,{TitleH + 8} 150,{TitleH + 6} L {TopRightW - 1},{TitleH + 6}")));
        g.DrawGeometry(null, Pen(C("#2F52B8")), Geo(FormattableString.Invariant($"M 70,{Top + 0.5} Q 100,{TitleH + 8.5} 150,{TitleH + 6.5}")));
        PardehLogo.Draw(g, new Rect(TopRightW - 66, TitleH + 3, 28, 28));
        // The small "more" arrow beside it.
        g.DrawGeometry(Solid(Colors.White), null, Geo(FormattableString.Invariant($"M {TopRightW - 28},{TitleH + 15} L {TopRightW - 20},{TitleH + 15} L {TopRightW - 24},{TitleH + 20} Z")));
        g.DrawGeometry(null, new Pen(Solid(Colors.White), 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
            Geo(FormattableString.Invariant($"M {TopRightW - 14},{TitleH + 13} L {TopRightW - 10},{TitleH + 17.5} L {TopRightW - 14},{TitleH + 22} M {TopRightW - 9},{TitleH + 13} L {TopRightW - 5},{TitleH + 17.5} L {TopRightW - 9},{TitleH + 22}")));
    }

    private static void AddChrome(Dictionary<string, byte[]> files)
    {
        files["top_left.png"] = Png(TopLeftW, Top, TopLeft);
        files["top_mid.png"] = Png(1, Top, g => TopBand(g, 1));
        files["top_right.png"] = Png(TopRightW, Top, TopRight);
        files["frame_l.png"] = Png(F, 1, g =>
        {
            g.FillRectangle(Horizontal((FrameDark, 0), (FrameLight, 1)), new Rect(0, 0, F, 1));
            g.FillRectangle(Solid(Outline), new Rect(0, 0, 1, 1));
        });
        files["frame_r.png"] = Png(F, 1, g =>
        {
            g.FillRectangle(Horizontal((FrameLight, 0), (FrameDark, 1)), new Rect(0, 0, F, 1));
            g.FillRectangle(Solid(Outline), new Rect(F - 1, 0, 1, 1));
        });
        files["toolbar.png"] = Png(1, ToolH, g =>
        {
            g.FillRectangle(Vertical((C("#4A78DC"), 0), (C("#3566CF"), 0.5), (C("#2D5BC3"), 1)), new Rect(0, 0, 1, ToolH));
            g.FillRectangle(Solid(Color.FromArgb(90, 255, 255, 255)), new Rect(0, 0, 1, 1));
            g.FillRectangle(Solid("#24479F"), new Rect(0, ToolH - 1, 1, 1));
        });
        files["status.png"] = Png(1, StatusH, g =>
        {
            g.FillRectangle(Vertical((C("#3E6ED2"), 0), (C("#2F5CC5"), 1)), new Rect(0, 0, 1, StatusH));
            g.FillRectangle(Solid("#1F409A"), new Rect(0, 0, 1, 1));
            g.FillRectangle(Solid(Color.FromArgb(70, 255, 255, 255)), new Rect(0, 1, 1, 1));
        });
        files["search_box.png"] = Png(118, 20, g =>
        {
            g.DrawRectangle(Vertical((C("#DCE6F8"), 0), (Colors.White, 0.35), (Colors.White, 1)), Pen(C("#16328A")), new RoundedRect(new Rect(0.5, 0.5, 117, 19), 2));
        });
        files["list_bg.png"] = Png(ListW, BodyH, g =>
        {
            g.FillRectangle(Vertical((C("#6B94E0"), 0), (C("#5B86D8"), 0.3), (C("#3D6BC9"), 1)), new Rect(0, 0, ListW, BodyH));
            g.FillRectangle(Solid("#1F409A"), new Rect(0, 0, 1, BodyH));
            g.FillRectangle(Solid(Color.FromArgb(90, 255, 255, 255)), new Rect(1, 0, 1, BodyH));
        });
        files["stage_glow.png"] = Png(LeftW, BodyH, g =>
            g.FillRectangle(new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.6, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.6, RelativeUnit.Relative),
                GradientStops = { new GradientStop(C("#2B4FA8"), 0), new GradientStop(C("#0F1E52"), 0.65), new GradientStop(C("#050B24"), 1) },
            }, new Rect(0, 0, LeftW, BodyH)));

        // Transport: a pale glossy bar with the blue frame below; round corners at its ends.
        files["transport_mid.png"] = Png(1, Transport, g => TransportBand(g, 1));
        files["transport_l.png"] = Png(16, Transport, g =>
        {
            using (g.PushClip(new RoundedRect(new Rect(0, -10, 16, Transport + 10), 0, 0, 0, 10)))
            {
                TransportBand(g, 16);
                g.FillRectangle(Horizontal((FrameDark, 0), (FrameLight, 1)), new Rect(0, 0, F, Transport));
                g.DrawGeometry(null, Pen(Outline), Geo(FormattableString.Invariant($"M 0.5,0 L 0.5,{Transport - 10.5} Q 0.5,{Transport - 0.5} 10.5,{Transport - 0.5} L 16,{Transport - 0.5}")));
            }
        });
        files["transport_r.png"] = Png(16, Transport, g =>
        {
            using (g.PushClip(new RoundedRect(new Rect(0, -10, 16, Transport + 10), 0, 0, 10, 0)))
            {
                TransportBand(g, 16);
                g.FillRectangle(Horizontal((FrameLight, 0), (FrameDark, 1)), new Rect(16 - F, 0, F, Transport));
                g.DrawGeometry(null, Pen(Outline), Geo(FormattableString.Invariant($"M 15.5,0 L 15.5,{Transport - 10.5} Q 15.5,{Transport - 0.5} 5.5,{Transport - 0.5} L 0,{Transport - 0.5}")));
            }
        });

        // The recessed display.
        const int DispH = 38;
        files["display_mid.png"] = Png(1, DispH, g => Recess(g, 1, DispH, 0, 0, 0));
        files["display_l.png"] = Png(10, DispH, g => Recess(g, 10, DispH, 0, 9, 0));
        files["display_r.png"] = Png(10, DispH, g => Recess(g, 10, DispH, 1, 9, 0));

        files["seek_track.png"] = Png(6, 10, g =>
        {
            g.FillRectangle(Vertical((C("#9FB8E4"), 0), (C("#E4EDFB"), 1)), new Rect(0, 2, 6, 6));
            g.FillRectangle(Solid("#6F8ECB"), new Rect(0, 2, 6, 1));
            g.FillRectangle(Solid(Colors.White), new Rect(0, 8, 6, 1));
        });
        files["seek_fill.png"] = Png(6, 10, g =>
            g.FillRectangle(Vertical((C("#BFD6FB"), 0), (C("#6E9AEA"), 0.5), (C("#3E6BD3"), 1)), new Rect(0, 3, 6, 5)));
        files["seek_thumb.png"] = Png(24, 14, g => Capsule(g, new Rect(0.5, 0.5, 23, 13), "#E5EEFF", "#8FB0F0", "#3F69C9"));
        files["seek_thumb_hover.png"] = Png(24, 14, g => Capsule(g, new Rect(0.5, 0.5, 23, 13), "#FFFFFF", "#B5D0FA", "#5E8AE0"));
        files["volume_track.png"] = Png(70, 12, g =>
            g.DrawRectangle(Vertical((C("#9FB8E4"), 0), (C("#E4EDFB"), 1)), Pen(C("#6F8ECB")), new RoundedRect(new Rect(3.5, 4.5, 63, 4), 2)));
        files["volume_fill.png"] = Png(70, 12, g =>
            g.DrawRectangle(Vertical((C("#BFD6FB"), 0), (C("#3E6BD3"), 1)), null, new RoundedRect(new Rect(4, 5, 62, 3), 1.5)));
        files["volume_thumb.png"] = Png(10, 16, g => Capsule(g, new Rect(0.5, 0.5, 9, 15), "#E5EEFF", "#8FB0F0", "#3F69C9"));
        files["volume_thumb_hover.png"] = Png(10, 16, g => Capsule(g, new Rect(0.5, 0.5, 9, 15), "#FFFFFF", "#B5D0FA", "#5E8AE0"));
    }

    private static void TransportBand(DrawingContext g, double w)
    {
        g.FillRectangle(Vertical((C("#F2F7FF"), 0), (C("#D7E5FB"), 0.45), (C("#BDD2F5"), 0.62), (C("#CFE0FA"), 1)), new Rect(0, 0, w, Transport - 7));
        g.FillRectangle(Solid("#7F9FE0"), new Rect(0, 0, w, 1));
        g.FillRectangle(Solid(Colors.White), new Rect(0, 1, w, 1));
        g.FillRectangle(Vertical((FrameLight, 0), (FrameDark, 1)), new Rect(0, Transport - 7, w, 7));
        g.FillRectangle(Solid(Outline), new Rect(0, Transport - 1, w, 1));
    }

    private static void Recess(DrawingContext g, int w, int h, int side, double radius, int unused)
    {
        var rect = side == 0 && radius == 0 ? new Rect(0, 0.5, w, h - 1) : side == 0 ? new Rect(0.5, 0.5, w + 10, h - 1) : new Rect(-10.5, 0.5, w + 10, h - 1);
        var round = radius == 0 ? 0 : radius;
        g.DrawRectangle(Vertical((C("#9DB6E6"), 0), (C("#CFE0FA"), 0.2), (C("#E4EEFD"), 1)), null, new RoundedRect(rect, round));
        if (radius == 0)
        {
            g.FillRectangle(Solid("#5373C4"), new Rect(0, 0, w, 1));
            g.FillRectangle(Solid("#5373C4"), new Rect(0, h - 1, w, 1));
        }
        else
        {
            g.DrawRectangle(null, Pen(C("#5373C4")), new RoundedRect(rect, round));
        }

        g.FillRectangle(Solid(Color.FromArgb(110, 40, 70, 150)), new Rect(radius == 0 ? 0 : (side == 0 ? 4 : 0), 1, radius == 0 ? w : w - 4, 2));
    }

    private static void Capsule(DrawingContext g, Rect r, string light, string mid, string dark)
    {
        var radius = Math.Min(r.Width, r.Height) / 2;
        g.DrawRectangle(Vertical((C(light), 0), (C(mid), 0.55), (C(dark), 1)), Pen(C("#27479F")), new RoundedRect(r, radius));
        g.DrawRectangle(null, Pen(Color.FromArgb(140, 255, 255, 255)), new RoundedRect(r.Deflate(1.5), Math.Max(1, radius - 1.5)));
    }

    // ---- Tabs -----------------------------------------------------------------------------------

    private static readonly (string Key, string Id, string Label, int Width)[] Tabs =
    [
        ("now", "tabNow", "Now Playing", 112),
        ("library", "tabLibrary", "Library", 84),
        ("cdrip", "tabRip", "Rip", 66),
        ("burnsoon", "tabBurn", "Burn", 70),
        ("burn", "tabSync", "Sync", 70),
        ("radio", "tabGuide", "Guide", 76),
    ];

    private const int TabsLeft = 14, TabH = BandH - 2;

    private static void AddTabs(Dictionary<string, byte[]> files)
    {
        foreach (var width in Tabs.Select(t => t.Width).Distinct())
        {
            var arrow = width == 112;
            files[$"tab_{width}.png"] = Png(width, TabH, g => TabArt(g, width, 0, arrow));
            files[$"tab_{width}_hover.png"] = Png(width, TabH, g => TabArt(g, width, 1, arrow));
            files[$"tab_{width}_down.png"] = Png(width, TabH, g => TabArt(g, width, 2, arrow));
        }
    }

    private static void TabArt(DrawingContext g, int w, int state, bool arrow)
    {
        if (state == 2)
        {
            var shape = new RoundedRect(new Rect(0.5, 0.5, w - 1, TabH + 8), 9, 9, 0, 0);
            g.DrawRectangle(Vertical((C("#F6F9FF"), 0), (C("#DDE8FC"), 0.5), (C("#C4D6F6"), 1)), Pen(C("#2F52B8")), shape);
            g.DrawRectangle(null, Pen(Color.FromArgb(230, 255, 255, 255)), new RoundedRect(new Rect(1.5, 1.5, w - 3, TabH + 8), 8, 8, 0, 0));
            g.FillRectangle(Vertical((Color.FromArgb(0, 255, 255, 255), 0), (Color.FromArgb(0, 255, 255, 255), 1)), new Rect(0, 0, 1, 1));
        }
        else
        {
            g.FillRectangle(Solid(Color.FromArgb(2, 255, 255, 255)), new Rect(0, 0, w, TabH));
            if (state == 1)
            {
                g.DrawRectangle(Vertical((Color.FromArgb(120, 255, 255, 255), 0), (Color.FromArgb(25, 255, 255, 255), 1)), null, new RoundedRect(new Rect(1, 2, w - 2, TabH + 6), 8, 8, 0, 0));
            }
            else
            {
                g.FillRectangle(Solid(Color.FromArgb(90, 24, 52, 150)), new Rect(w - 1, 8, 1, TabH - 14));
                g.FillRectangle(Solid(Color.FromArgb(70, 255, 255, 255)), new Rect(w - 2, 8, 1, TabH - 14));
            }
        }

        if (arrow)
            g.DrawGeometry(Solid(state == 2 ? Ink : Colors.White), null, Geo(FormattableString.Invariant($"M {w - 15},{TabH / 2 - 2} L {w - 7},{TabH / 2 - 2} L {w - 11},{TabH / 2 + 3} Z")));
    }

    // ---- Buttons --------------------------------------------------------------------------------

    private enum State { Normal, Hover, Down, Disabled }

    private static readonly (State State, string Suffix)[] States =
        [(State.Normal, ""), (State.Hover, "_hover"), (State.Down, "_down"), (State.Disabled, "_disabled")];

    private static Color InkFor(State s) => s == State.Disabled ? C("#9DAAC8") : Ink;

    private static IBrush Face(State s) => s switch
    {
        State.Hover => Vertical((Colors.White, 0), (C("#DCEBFF"), 0.5), (C("#A9CCFA"), 1)),
        State.Down => Vertical((C("#A9BFE8"), 0), (C("#DDE8FA"), 1)),
        State.Disabled => Vertical((C("#F4F7FC"), 0), (C("#D9E2F2"), 1)),
        _ => Vertical((Colors.White, 0), (C("#DDE9FB"), 0.5), (C("#A9C3F0"), 1)),
    };

    private static void AddButtons(Dictionary<string, byte[]> files)
    {
        foreach (var (state, suffix) in States)
        {
            files[$"play{suffix}.png"] = Png(46, 46, g => Orb(g, 46, state, hero: true, c => Play(g, c, 23, 23, 9)));
            files[$"pause{suffix}.png"] = Png(46, 46, g => Orb(g, 46, state, hero: true, c => Pause(g, c, 23, 23, 8)));
            files[$"stop{suffix}.png"] = Png(28, 28, g => Orb(g, 28, state, hero: false, c => g.DrawRectangle(Solid(c), null, new RoundedRect(new Rect(9.5, 9.5, 9, 9), 1))));
            files[$"prev{suffix}.png"] = Png(26, 26, g => Half(g, state, left: true));
            files[$"next{suffix}.png"] = Png(26, 26, g => Half(g, state, left: false));
            files[$"mute{suffix}.png"] = Png(24, 24, g => Orb(g, 24, state, hero: false, c => Speaker(g, c, crossed: false)));
            files[$"muted{suffix}.png"] = Png(24, 24, g => Orb(g, 24, state, hero: false, c => Speaker(g, c, crossed: true)));
            files[$"skinmode{suffix}.png"] = Png(24, 24, g => Orb(g, 24, state, hero: false, c => Stroke(g, c, "M 7.5,7.5 L 16.5,7.5 L 16.5,16.5 L 7.5,16.5 Z M 10.5,10.5 L 13.5,10.5 L 13.5,13.5 L 10.5,13.5 Z")));
            files[$"fullscreen{suffix}.png"] = Png(24, 24, g => Orb(g, 24, state, hero: false, c => Stroke(g, c, "M 7,10 L 7,7 L 10,7 M 14,7 L 17,7 L 17,10 M 17,14 L 17,17 L 14,17 M 10,17 L 7,17 L 7,14")));
            files[$"min{suffix}.png"] = Png(21, 21, g => WindowButton(g, state, "M 6,15 L 12,15"));
            files[$"max{suffix}.png"] = Png(21, 21, g => WindowButton(g, state, "M 5.5,5.5 L 15.5,5.5 L 15.5,15.5 L 5.5,15.5 Z M 5.5,6.5 L 15.5,6.5"));
            files[$"close{suffix}.png"] = Png(21, 21, g => WindowButton(g, state, "M 6,6 L 15,15 M 15,6 L 6,15", close: true));
            files[$"dd{suffix}.png"] = Png(14, 14, g =>
            {
                if (state is State.Hover or State.Down)
                    g.DrawRectangle(Solid(Color.FromArgb(110, 255, 255, 255)), null, new RoundedRect(new Rect(0, 0, 14, 14), 2));
                g.DrawGeometry(Solid(Ink), null, Geo("M 3,5 L 11,5 L 7,10 Z"));
            });
        }
    }

    private static void Orb(DrawingContext g, int size, State state, bool hero, Action<Color> glyph)
    {
        var c = new Point(size / 2.0, size / 2.0);
        var r = size / 2.0 - 1;
        g.DrawEllipse(Solid(Color.FromArgb(60, 28, 46, 120)), null, new Point(c.X, c.Y + 1), r, r);
        var rim = state == State.Hover ? C("#2F7FE0") : hero ? C("#2F52B8") : C("#6F87C2");
        g.DrawEllipse(Face(state), Pen(rim, hero ? 2 : 1), c, r - (hero ? 1 : 0.5), r - (hero ? 1 : 0.5));
        g.DrawEllipse(Vertical((Color.FromArgb(230, 255, 255, 255), 0), (Color.FromArgb(0, 255, 255, 255), 1)), null, new Point(c.X, c.Y - r * 0.4), r * 0.7, r * 0.48);
        glyph(InkFor(state));
    }

    private static void Half(DrawingContext g, State state, bool left)
    {
        var rect = left ? new RoundedRect(new Rect(0.5, 0.5, 25.5, 25), 12.5, 0, 0, 12.5) : new RoundedRect(new Rect(0, 0.5, 25.5, 25), 0, 12.5, 12.5, 0);
        g.DrawRectangle(Face(state), Pen(state == State.Hover ? C("#2F7FE0") : C("#6F87C2")), rect);
        g.DrawRectangle(Vertical((Color.FromArgb(200, 255, 255, 255), 0), (Color.FromArgb(0, 255, 255, 255), 1)), null, new Rect(left ? 4 : 0, 2, 21, 10));
        var brush = Solid(InkFor(state));
        if (left)
        {
            g.FillRectangle(brush, new Rect(8, 8, 2, 10));
            g.DrawGeometry(brush, null, Geo("M 18,8 L 11,13 L 18,18 Z"));
        }
        else
        {
            g.FillRectangle(brush, new Rect(16, 8, 2, 10));
            g.DrawGeometry(brush, null, Geo("M 8,8 L 15,13 L 8,18 Z"));
        }
    }

    private static void Play(DrawingContext g, Color c, double cx, double cy, double half) =>
        g.DrawGeometry(Solid(c), null, Geo(FormattableString.Invariant($"M {cx - half * 0.55},{cy - half} L {cx + half * 0.95},{cy} L {cx - half * 0.55},{cy + half} Z")));

    private static void Pause(DrawingContext g, Color c, double cx, double cy, double half)
    {
        g.DrawRectangle(Solid(c), null, new RoundedRect(new Rect(cx - half * 0.8, cy - half, half * 0.6, half * 2), 1));
        g.DrawRectangle(Solid(c), null, new RoundedRect(new Rect(cx + half * 0.2, cy - half, half * 0.6, half * 2), 1));
    }

    private static void Speaker(DrawingContext g, Color c, bool crossed)
    {
        g.DrawGeometry(Solid(c), null, Geo("M 6,10 L 9,10 L 12,7 L 12,17 L 9,14 L 6,14 Z"));
        if (crossed)
            g.DrawGeometry(null, new Pen(Solid("#C0392B"), 1.4, lineCap: PenLineCap.Round), Geo("M 14,9.5 L 18,14.5 M 18,9.5 L 14,14.5"));
        else
            g.DrawGeometry(null, new Pen(Solid(c), 1.1, lineCap: PenLineCap.Round), Geo("M 14,9.5 Q 16,12 14,14.5 M 16,8 Q 19.5,12 16,16"));
    }

    private static void Stroke(DrawingContext g, Color c, string data) =>
        g.DrawGeometry(null, new Pen(Solid(c), 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Geo(data));

    private static void WindowButton(DrawingContext g, State state, string data, bool close = false)
    {
        var top = close ? C("#E9967A") : C("#8FB2F6");
        var bottom = close ? C("#C8401F") : C("#3F69D0");
        if (state == State.Hover) { top = close ? C("#F5B49E") : C("#B7D0FB"); bottom = close ? C("#DB5A38") : C("#5C86E2"); }
        if (state == State.Down) { top = close ? C("#B8391B") : C("#2F52B8"); bottom = close ? C("#E07B5C") : C("#6E92E8"); }
        g.DrawRectangle(Vertical((top, 0), (bottom, 1)), Pen(Colors.White), new RoundedRect(new Rect(1.5, 1.5, 18, 18), 3));
        g.DrawRectangle(null, Pen(Color.FromArgb(70, 20, 40, 120)), new RoundedRect(new Rect(0.5, 0.5, 20, 20), 4));
        g.DrawGeometry(null, new Pen(Solid(Colors.White), 2, lineCap: PenLineCap.Flat), Geo(data));
    }

    // ---- Small icons (toolbar and status row) ---------------------------------------------------

    private static void AddIcons(Dictionary<string, byte[]> files)
    {
        var white = new Pen(Solid(Colors.White), 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        files["icon_logo.png"] = Png(22, 22, g => PardehLogo.Draw(g, new Rect(0, 0, 22, 22)));
        files["icon_search.png"] = Png(18, 18, g =>
        {
            g.DrawEllipse(Vertical((Colors.White, 0), (C("#BFD6FB"), 1)), new Pen(Solid("#F5C542"), 1.8), new Point(10.5, 7.5), 4.7, 4.7);
            g.DrawGeometry(null, new Pen(Solid("#F5C542"), 2.4, lineCap: PenLineCap.Round), Geo("M 7,11 L 2.5,15.5"));
        });
        files["icon_info.png"] = Png(18, 18, g =>
        {
            g.DrawRectangle(Vertical((C("#8FB2F6"), 0), (C("#2F52B8"), 1)), Pen(Colors.White), new RoundedRect(new Rect(1.5, 1.5, 15, 15), 2));
            g.DrawGeometry(null, white, Geo("M 9,8 L 9,13"));
            g.DrawEllipse(Solid(Colors.White), null, new Point(9, 5.4), 1.1, 1.1);
        });
        files["icon_options.png"] = Png(18, 18, g =>
        {
            g.DrawRectangle(Solid(Colors.White), Pen(C("#16328A")), new Rect(2.5, 1.5, 11, 14));
            g.DrawGeometry(null, new Pen(Solid("#D33A2C"), 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Geo("M 5,9 L 7.5,12 L 13,4"));
        });
        files["icon_list.png"] = Png(18, 18, g =>
        {
            g.DrawRectangle(Solid(Colors.White), Pen(C("#16328A")), new Rect(2.5, 1.5, 11, 14));
            g.DrawGeometry(null, Pen(C("#6F87C2")), Geo("M 5,5.5 L 11,5.5 M 5,8.5 L 11,8.5 M 5,11.5 L 9,11.5"));
            g.DrawGeometry(null, new Pen(Solid("#2FB34A"), 2.2, lineCap: PenLineCap.Flat), Geo("M 14,9 L 14,16 M 10.5,12.5 L 17.5,12.5"));
        });
        files["icon_add.png"] = Png(18, 18, g =>
            g.DrawGeometry(null, new Pen(Solid("#3FD35B"), 3.6, lineCap: PenLineCap.Flat), Geo("M 9,2.5 L 9,15.5 M 2.5,9 L 15.5,9")));
        files["icon_sync.png"] = Png(18, 18, g =>
        {
            g.DrawEllipse(Solid("#2FB34A"), Pen(Colors.White), new Point(9, 9), 7.6, 7.6);
            g.DrawGeometry(null, new Pen(Solid(Colors.White), 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Geo("M 5,8 Q 6,4.5 10,5 M 13,10 Q 12,13.5 8,13 M 9,3.2 L 11,5 L 9,6.8 M 9,11.2 L 7,13 L 9,14.8"));
        });
        files["icon_burn.png"] = Png(18, 18, g =>
        {
            g.DrawEllipse(Vertical((Colors.White, 0), (C("#AFC0DE"), 1)), Pen(C("#4B5F94")), new Point(9, 9), 7.6, 7.6);
            g.DrawEllipse(Solid("#E8503A"), null, new Point(9, 9), 2.6, 2.6);
        });
    }

    // ---- Layout & script ------------------------------------------------------------------------

    private static string Menu(string id, int left, int width, string label, string menu) =>
        $"""<TEXT id="{id}" left="{left}" top="8" width="{width}" value="{label}" fontType="Tahoma" fontSize="8" foregroundColor="#1B3A8C" hoverForegroundColor="#FFFFFF" onClick="pardeh.command('menu', '{menu}');"/>""";

    private static string TabXml()
    {
        var sb = new StringBuilder();
        var x = TabsLeft;
        foreach (var (key, id, label, width) in Tabs)
        {
            var labelWidth = width == 112 ? width - 16 : width;
            sb.AppendLine(CultureFormat($$"""
                <BUTTON id="{{id}}" left="{{x}}" top="{{Top - TabH}}" image="tab_{{width}}.png" hoverImage="tab_{{width}}_hover.png" downImage="tab_{{width}}_down.png"
                    onClick="ShowPane('{{key}}');" upToolTip="{{label}}"/>
                <TEXT id="{{id}}L" left="{{x}}" top="{{Top - TabH + 10}}" width="{{labelWidth}}" justification="center" value="{{label}}" fontType="Tahoma" fontSize="8" fontStyle="bold" foregroundColor="#FFFFFF"/>
                <TEXT id="{{id}}S" left="{{x}}" top="{{Top - TabH + 10}}" width="{{labelWidth}}" justification="center" value="{{label}}" fontType="Tahoma" fontSize="8" fontStyle="bold" foregroundColor="#1B3A8C" visible="false"/>
        """));
            x += width;
        }

        return sb.ToString();
    }

    private static string CultureFormat(string s) => s;

    private static string Tool(string id, int iconLeft, int textLeft, int width, string icon, string label, string onClick) => $$"""
              <SUBVIEW id="{{id}}Icon" left="{{iconLeft}}" top="5" width="18" height="18" backgroundImage="{{icon}}"/>
              <TEXT id="{{id}}" left="{{textLeft}}" top="7" width="{{width}}" value="{{label}}" fontType="Tahoma" fontSize="8" foregroundColor="#FFFFFF" hoverForegroundColor="#FFE38A" onClick="{{onClick}}"/>
        """;

    private static string Layout => $$"""
        <THEME id="PardehTen" title="Pardeh Ten" author="Pardeh contributors" pardehMode="full">
          <VIEW id="vFull" width="{{W}}" height="{{H}}" minWidth="760" minHeight="480" resizable="true"
                titleBar="false" backgroundColor="none" scriptFile="aero10.js"
                onLoad="OnLoad();" onClose="OnClose();">

            <PLAYER openstatechange="OnOpenState(NewState);"/>

            <!-- Title row and task band. -->
            <SUBVIEW id="svTop" left="0" top="0" width="{{W}}" height="{{Top}}" horizontalAlignment="stretch">
              <SUBVIEW left="0" top="0" width="{{TopLeftW}}" height="{{Top}}" backgroundImage="top_left.png"/>
              <SUBVIEW left="{{TopLeftW}}" top="0" width="{{W - TopLeftW - TopRightW}}" height="{{Top}}" backgroundImage="top_mid.png" backgroundTiled="true" horizontalAlignment="stretch"/>
              <SUBVIEW left="{{W - TopRightW}}" top="0" width="{{TopRightW}}" height="{{Top}}" backgroundImage="top_right.png" horizontalAlignment="right">
                <BUTTON left="{{TopRightW - 118}}" top="7" image="dd.png" hoverImage="dd_hover.png" downImage="dd_down.png" upToolTip="View" onClick="pardeh.command('menu', 'view');"/>
                <BUTTON left="{{TopRightW - 98}}" top="4" image="min.png" hoverImage="min_hover.png" downImage="min_down.png" upToolTip="Minimize" onClick="view.minimize();"/>
                <BUTTON left="{{TopRightW - 74}}" top="4" image="max.png" hoverImage="max_hover.png" downImage="max_down.png" upToolTip="Maximize" onClick="pardeh.command('maximize');"/>
                <BUTTON left="{{TopRightW - 50}}" top="4" image="close.png" hoverImage="close_hover.png" downImage="close_down.png" upToolTip="Close" onClick="view.close();"/>
              </SUBVIEW>
              <SUBVIEW left="10" top="4" width="22" height="22" backgroundImage="icon_logo.png"/>
              <TEXT left="36" top="7" width="80" value="Pardeh" fontType="Tahoma" fontSize="9" foregroundColor="#1B3A8C"/>
              <SUBVIEW left="110" top="0" width="260" height="{{TitleH}}" visible="wmpprop:!app.titleBarVisible">
                {{Menu("mFile", 0, 28, "File", "file")}}
                {{Menu("mView", 34, 30, "View", "view")}}
                {{Menu("mPlay", 70, 28, "Play", "play")}}
                {{Menu("mTools", 104, 34, "Tools", "tools")}}
                {{Menu("mHelp", 144, 30, "Help", "help")}}
              </SUBVIEW>
        {{TabXml()}}
            </SUBVIEW>

            <!-- Frame sides. -->
            <SUBVIEW left="0" top="{{Top}}" width="{{F}}" height="{{H - Top - Transport}}" backgroundImage="frame_l.png" backgroundTiled="true" verticalAlignment="stretch"/>
            <SUBVIEW left="{{W - F}}" top="{{Top}}" width="{{F}}" height="{{H - Top - Transport}}" backgroundImage="frame_r.png" backgroundTiled="true" horizontalAlignment="right" verticalAlignment="stretch"/>

            <!-- Toolbar row. -->
            <SUBVIEW id="svTool" left="{{F}}" top="{{Top}}" width="{{BodyW}}" height="{{ToolH}}" backgroundImage="toolbar.png" backgroundTiled="true" horizontalAlignment="stretch">
              <SUBVIEW left="6" top="4" width="118" height="20" backgroundImage="search_box.png"/>
              <TEXT id="tSearchHint" left="12" top="7" width="100" value="Search library" fontType="Tahoma" fontSize="8" foregroundColor="#8190B8" onClick="DoSearch();"/>
        {{Tool("tbSearch", 132, 152, 50, "icon_search.png", "Search", "DoSearch();")}}
        {{Tool("tbInfo", 210, 230, 62, "icon_info.png", "More Info", "ShowPane('info');")}}
        {{Tool("tbOptions", 296, 316, 100, "icon_options.png", "Library Options ▾", "pardeh.command('menu', 'tools');")}}
              <SUBVIEW left="{{BodyW - 170}}" top="0" width="170" height="{{ToolH}}" horizontalAlignment="right">
        {{Tool("tbList", 6, 28, 120, "icon_list.png", "Now Playing List ▾", "pardeh.command('menu', 'play');")}}
              </SUBVIEW>
            </SUBVIEW>

            <!-- Body: one pane at a time on the left (the list pane stays on the right for Now Playing and Library). -->
            <SUBVIEW id="svContent" left="{{F}}" top="{{BodyTop}}" width="{{BodyW}}" height="{{BodyH}}" backgroundColor="#EDF3FD"
                horizontalAlignment="stretch" verticalAlignment="stretch">

              <SUBVIEW id="paneNow" left="0" top="0" width="{{LeftW}}" height="{{BodyH}}" horizontalAlignment="stretch" verticalAlignment="stretch">
                <SUBVIEW id="svStage" left="0" top="0" width="{{LeftW}}" height="{{BodyH}}" backgroundImage="stage_glow.png" resizeBackgroundImage="true" backgroundColor="#050B24" horizontalAlignment="stretch" verticalAlignment="stretch">
                  <WMPVIDEO id="vid" left="0" top="0" width="{{LeftW}}" height="{{BodyH}}" visible="false" backgroundColor="#000000" horizontalAlignment="stretch" verticalAlignment="stretch"/>
                  <WMPEFFECTS id="vis" left="0" top="0" width="{{LeftW}}" height="{{BodyH}}" visible="false" horizontalAlignment="stretch" verticalAlignment="stretch"/>
                  <TEXT id="tCaption" left="12" top="8" width="{{LeftW - 24}}" fontType="Tahoma" fontSize="8" foregroundColor="#C9D8F6" horizontalAlignment="stretch"
                      value="wmpprop:player.currentMedia &amp;&amp; player.currentMedia.getItemInfo('Author') ? player.currentMedia.getItemInfo('Author') : player.status"/>
                  <TEXT id="tTitle" left="12" top="22" width="{{LeftW - 24}}" fontType="Tahoma" fontSize="12" fontStyle="bold" foregroundColor="#FFFFFF" horizontalAlignment="stretch"
                      value="wmpprop:player.currentMedia ? player.currentMedia.name : 'Pardeh'"/>
                </SUBVIEW>
              </SUBVIEW>
              <SUBVIEW id="paneLibrary" left="0" top="0" width="{{LeftW}}" height="{{BodyH}}" visible="false" horizontalAlignment="stretch" verticalAlignment="stretch">
                <LIBRARY id="library" left="0" top="0" width="{{LeftW}}" height="{{BodyH}}" light="true" toolbar="false" footer="false" horizontalAlignment="stretch" verticalAlignment="stretch"/>
              </SUBVIEW>
              <SUBVIEW id="paneRadio" left="0" top="0" width="{{BodyW}}" height="{{BodyH}}" visible="false" horizontalAlignment="stretch" verticalAlignment="stretch">
                <LISTPANE kind="radio" light="true" left="0" top="0" width="{{BodyW}}" height="{{BodyH}}" horizontalAlignment="stretch" verticalAlignment="stretch"/>
              </SUBVIEW>
              <SUBVIEW id="paneSync" left="0" top="0" width="{{BodyW}}" height="{{BodyH}}" visible="false" horizontalAlignment="stretch" verticalAlignment="stretch">
                <LISTPANE kind="device" light="true" left="0" top="0" width="{{BodyW}}" height="{{BodyH}}" horizontalAlignment="stretch" verticalAlignment="stretch"/>
              </SUBVIEW>
              <SUBVIEW id="paneSkins" left="0" top="0" width="{{BodyW}}" height="{{BodyH}}" visible="false" horizontalAlignment="stretch" verticalAlignment="stretch">
                <LISTPANE kind="settings" left="0" top="0" width="{{BodyW}}" height="{{BodyH}}" horizontalAlignment="stretch" verticalAlignment="stretch"/>
              </SUBVIEW>
              <SUBVIEW id="paneInfo" left="0" top="0" width="{{BodyW}}" height="{{BodyH}}" visible="false" backgroundColor="#EDF3FD" horizontalAlignment="stretch" verticalAlignment="stretch">
                <TEXT left="24" top="18" width="400" value="More Info" fontType="Tahoma" fontSize="14" fontStyle="bold" foregroundColor="#1B3A8C"/>
                <SUBVIEW left="24" top="56" width="82" height="82" backgroundColor="#3D6BC9">
                  <SUBVIEW left="1" top="1" width="80" height="80" backgroundImage="WMPImage_AlbumArtLarge"/>
                </SUBVIEW>
                <TEXT left="122" top="58" width="420" fontType="Tahoma" fontSize="11" fontStyle="bold" foregroundColor="#1B3A8C"
                    value="wmpprop:player.currentMedia ? player.currentMedia.name : 'Nothing is playing.'"/>
                <TEXT left="122" top="82" width="420" fontType="Tahoma" fontSize="9" foregroundColor="#2B3F78"
                    value="wmpprop:player.currentMedia &amp;&amp; player.currentMedia.getItemInfo('Author') ? 'Artist: ' + player.currentMedia.getItemInfo('Author') : ''"/>
                <TEXT left="122" top="100" width="420" fontType="Tahoma" fontSize="9" foregroundColor="#2B3F78"
                    value="wmpprop:player.currentMedia &amp;&amp; player.currentMedia.getItemInfo('Album') ? 'Album: ' + player.currentMedia.getItemInfo('Album') : ''"/>
                <TEXT left="122" top="118" width="420" fontType="Tahoma" fontSize="9" foregroundColor="#2B3F78"
                    value="wmpprop:player.currentMedia &amp;&amp; player.currentMedia.getItemInfo('Genre') ? 'Genre: ' + player.currentMedia.getItemInfo('Genre') : ''"/>
                <TEXT left="122" top="136" width="420" fontType="Tahoma" fontSize="9" foregroundColor="#3D6BC9"
                    value="wmpprop:player.currentMedia ? 'Length: ' + player.currentMedia.durationString : ''"/>
              </SUBVIEW>
              <SUBVIEW id="paneSoon" left="0" top="0" width="{{BodyW}}" height="{{BodyH}}" visible="false" backgroundColor="#EDF3FD" horizontalAlignment="stretch" verticalAlignment="stretch">
                <TEXT id="soonTitle" left="24" top="18" width="400" value="" fontType="Tahoma" fontSize="14" fontStyle="bold" foregroundColor="#1B3A8C"/>
                <TEXT id="soonText" left="24" top="50" width="520" value="" fontType="Tahoma" fontSize="8" foregroundColor="#2B3F78"/>
              </SUBVIEW>

              <!-- The list pane. -->
              <SUBVIEW id="svList" left="{{LeftW}}" top="0" width="{{ListW}}" height="{{BodyH}}" backgroundImage="list_bg.png" resizeBackgroundImage="true" horizontalAlignment="right" verticalAlignment="stretch">
                <PLAYLIST id="ipl" left="4" top="4" width="{{ListW - 8}}" height="{{BodyH - 30}}" transparentBackground="true" foregroundColor="#FFFFFF" itemPlayingColor="#FFE38A" itemSelectedBackgroundColor="#2F5CC5" verticalAlignment="stretch"/>
                <TEXT id="listHint" left="14" top="22" width="{{ListW - 28}}" justification="center" fontType="Tahoma" fontSize="8" foregroundColor="#FFFFFF"
                    visible="wmpprop:player.currentPlaylist.count == 0" value="Drag items here to build"/>
                <TEXT id="listHint2" left="14" top="36" width="{{ListW - 28}}" justification="center" fontType="Tahoma" fontSize="8" foregroundColor="#FFFFFF"
                    visible="wmpprop:player.currentPlaylist.count == 0" value="a list of items to play."/>
                <TEXT id="listTotal" left="8" top="{{BodyH - 20}}" width="{{ListW - 16}}" justification="right" fontType="Tahoma" fontSize="8" foregroundColor="#FFFFFF" verticalAlignment="bottom"
                    value="wmpprop:'Total Time: ' + PlaylistTotal()"/>
              </SUBVIEW>
            </SUBVIEW>

            <!-- Status row. -->
            <SUBVIEW id="svStatus" left="{{F}}" top="{{StatusTop}}" width="{{BodyW}}" height="{{StatusH}}" backgroundImage="status.png" backgroundTiled="true" horizontalAlignment="stretch" verticalAlignment="bottom">
              <SUBVIEW left="8" top="5" width="18" height="18" backgroundImage="icon_add.png"/>
              <TEXT left="30" top="8" width="120" value="Add to Library ▾" fontType="Tahoma" fontSize="8" fontStyle="bold" foregroundColor="#FFFFFF" hoverForegroundColor="#FFE38A" onClick="pardeh.command('menu', 'file');"/>
              <TEXT left="170" top="8" width="100" fontType="Tahoma" fontSize="8" foregroundColor="#FFFFFF" value="wmpprop:(g_pane == 'library' ? library.itemCount : player.currentPlaylist.count) + ' Item(s)'"/>
              <TEXT left="300" top="8" width="200" fontType="Tahoma" fontSize="8" foregroundColor="#FFFFFF"
                  value="wmpprop:'Total Time: ' + (g_pane == 'library' ? library.totalTime : PlaylistTotal())"/>
              <SUBVIEW id="svBurnLinks" left="{{BodyW - 200}}" top="0" width="200" height="{{StatusH}}" horizontalAlignment="right">
                <SUBVIEW left="6" top="5" width="18" height="18" backgroundImage="icon_sync.png"/>
                <TEXT left="28" top="8" width="70" value="Start Sync" fontType="Tahoma" fontSize="8" fontStyle="bold" foregroundColor="#FFFFFF" hoverForegroundColor="#FFE38A" onClick="ShowPane('burn');"/>
                <SUBVIEW left="104" top="5" width="18" height="18" backgroundImage="icon_burn.png"/>
                <TEXT left="126" top="8" width="70" value="Start Burn" fontType="Tahoma" fontSize="8" fontStyle="bold" foregroundColor="#FFFFFF" hoverForegroundColor="#FFE38A" onClick="ShowPane('burnsoon');"/>
              </SUBVIEW>
            </SUBVIEW>

            <!-- Transport. -->
            <SUBVIEW id="svTransport" left="0" top="{{H - Transport}}" width="{{W}}" height="{{Transport}}" horizontalAlignment="stretch" verticalAlignment="bottom">
              <SUBVIEW left="0" top="0" width="16" height="{{Transport}}" backgroundImage="transport_l.png"/>
              <SUBVIEW left="16" top="0" width="{{W - 32}}" height="{{Transport}}" backgroundImage="transport_mid.png" backgroundTiled="true" horizontalAlignment="stretch"/>
              <SUBVIEW left="{{W - 16}}" top="0" width="16" height="{{Transport}}" backgroundImage="transport_r.png" horizontalAlignment="right"/>

              <SEEKSLIDER id="seek" left="20" top="7" width="{{W - 40}}" height="14" horizontalAlignment="stretch"
                  backgroundImage="seek_track.png" foregroundImage="seek_fill.png" thumbImage="seek_thumb.png" thumbHoverImage="seek_thumb_hover.png"
                  tiled="true" slide="false" borderSize="0" direction="horizontal"/>

              <PLAYBUTTON left="16" top="22" image="play.png" hoverImage="play_hover.png" downImage="play_down.png" disabledImage="play_disabled.png" upToolTip="Play"/>
              <PAUSEBUTTON left="16" top="22" visible="wmpenabled:player.controls.pause" image="pause.png" hoverImage="pause_hover.png" downImage="pause_down.png" disabledImage="pause_disabled.png" upToolTip="Pause"/>
              <STOPBUTTON left="68" top="31" image="stop.png" hoverImage="stop_hover.png" downImage="stop_down.png" disabledImage="stop_disabled.png" upToolTip="Stop"/>
              <PREVBUTTON left="102" top="31" image="prev.png" hoverImage="prev_hover.png" downImage="prev_down.png" disabledImage="prev_disabled.png" upToolTip="Previous"/>
              <NEXTBUTTON left="128" top="31" image="next.png" hoverImage="next_hover.png" downImage="next_down.png" disabledImage="next_disabled.png" upToolTip="Next"/>
              <MUTEBUTTON id="bMute" left="166" top="31" image="mute.png" hoverImage="mute_hover.png" downImage="muted.png" upToolTip="Mute" downToolTip="Sound"/>
              <VOLUMESLIDER left="192" top="33" width="70" height="12" backgroundImage="volume_track.png" foregroundImage="volume_fill.png"
                  thumbImage="volume_thumb.png" thumbHoverImage="volume_thumb_hover.png" borderSize="3" direction="horizontal"/>

              <!-- The recessed display. -->
              <SUBVIEW id="svDisplay" left="286" top="26" width="{{W - 286 - 84}}" height="38" horizontalAlignment="stretch">
                <SUBVIEW left="0" top="0" width="10" height="38" backgroundImage="display_l.png"/>
                <SUBVIEW left="10" top="0" width="{{W - 286 - 84 - 20}}" height="38" backgroundImage="display_mid.png" backgroundTiled="true" horizontalAlignment="stretch"/>
                <SUBVIEW left="{{W - 286 - 84 - 10}}" top="0" width="10" height="38" backgroundImage="display_r.png" horizontalAlignment="right"/>
                <TEXT id="dTitle" left="14" top="5" width="{{W - 286 - 84 - 100}}" fontType="Tahoma" fontSize="9" fontStyle="bold" foregroundColor="#1B3A8C" horizontalAlignment="stretch"
                    value="wmpprop:player.currentMedia ? player.currentMedia.name : 'Pardeh'"/>
                <TEXT id="dStatus" left="14" top="20" width="{{W - 286 - 84 - 100}}" fontType="Tahoma" fontSize="8" foregroundColor="#3D5CA8" horizontalAlignment="stretch"
                    value="wmpprop:player.status"/>
                <TEXT id="dTime" left="{{W - 286 - 84 - 80}}" top="10" width="68" justification="right" fontType="Tahoma" fontSize="11" fontStyle="bold" foregroundColor="#1B3A8C" horizontalAlignment="right"
                    value="wmpprop:player.controls.currentPositionString"/>
              </SUBVIEW>

              <BUTTON left="{{W - 74}}" top="30" image="skinmode.png" hoverImage="skinmode_hover.png" downImage="skinmode_down.png" horizontalAlignment="right" upToolTip="Switch to Skin Mode" onClick="pardeh.command('skinMode');"/>
              <BUTTON left="{{W - 46}}" top="30" image="fullscreen.png" hoverImage="fullscreen_hover.png" downImage="fullscreen_down.png" horizontalAlignment="right" upToolTip="Full Screen" onClick="pardeh.command('fullScreen');"/>
              <BUTTON left="{{W - 22}}" top="{{Transport - 20}}" width="16" height="16" horizontalAlignment="right" cursor="sizenwse" upToolTip="" onmousedown="view.size('bottomright');"/>
            </SUBVIEW>
          </VIEW>
        </THEME>
        """;

    private const string Script = """
        // Pardeh Ten full mode — original script.
        var g_pane = "";
        var TABS = { now: "tabNow", library: "tabLibrary", cdrip: "tabRip", burnsoon: "tabBurn", burn: "tabSync", radio: "tabGuide" };
        var ORDER = ["now", "library", "cdrip", "burnsoon", "burn", "radio"];
        var WIDTH = { now: 112, library: 84, cdrip: 66, burnsoon: 70, burn: 70, radio: 76 };
        var SOON = { cdrip: "Rip", burnsoon: "Burn" };
        // Which Settings › Pages key hides each tab: Rip is "Copy from CD"; Burn and Sync both belong to "Copy to CD or Device".
        var PAGEKEY = { library: "library", cdrip: "cdrip", burnsoon: "burn", burn: "burn", radio: "radio", info: "info" };

        function OnLoad() {
            var pane = theme.loadPreference("Pane");
            ApplyPages();
            ShowPane(pane && (TABS[pane] || pane == "info") && !IsHidden(pane) ? pane : "now");
            OnOpenState(player.openState);
        }

        function OnClose() {
            theme.savePreference("Pane", g_pane == "skins" ? "now" : g_pane);
        }

        // Pages (Settings › Pages): hide unticked tabs and close the gaps.
        function IsHidden(name) {
            var page = PAGEKEY[name];
            if (page == undefined) return false;
            return ("," + pardeh.command("hiddenPages") + ",").indexOf("," + page + ",") >= 0;
        }

        // The search box: Pardeh's skins have no text input, so this asks for the text and applies it to the library.
        function DoSearch() {
            if (IsHidden("library")) return;
            ShowPane("library");
            library.searchPrompt();
        }

        function PlaylistTotal() {
            var total = 0;
            var list = player.currentPlaylist;
            for (var i = 0; i < list.count; i++) total += list.item(i).duration;
            total = Math.floor(total);
            var s = total % 60, m = Math.floor(total / 60) % 60, h = Math.floor(total / 3600);
            var text = (m < 10 && h > 0 ? "0" : "") + m + ":" + (s < 10 ? "0" : "") + s;
            return h > 0 ? h + ":" + text : text;
        }

        function ApplyPages() {
            var x = 14;
            for (var i = 0; i < ORDER.length; i++) {
                var key = ORDER[i];
                var shown = !IsHidden(key);
                var id = TABS[key];
                eval(id).visible = shown;
                eval(id + "L").visible = shown && key != g_pane;
                eval(id + "S").visible = shown && key == g_pane;
                if (shown) {
                    eval(id).left = x;
                    eval(id + "L").left = x;
                    eval(id + "S").left = x;
                    x += WIDTH[key];
                }
            }
            var info = !IsHidden("info");
            tbInfo.visible = info;
            tbInfoIcon.visible = info;
            svBurnLinks.visible = !IsHidden("burn");
            if (g_pane != "" && IsHidden(g_pane)) ShowPane("now");
        }

        function ShowPane(name) {
            if (IsHidden(name)) name = "now";
            g_pane = name;
            paneNow.visible = name == "now";
            paneLibrary.visible = name == "library";
            paneRadio.visible = name == "radio";
            paneSync.visible = name == "burn";
            paneSkins.visible = name == "skins";
            paneInfo.visible = name == "info";
            paneSoon.visible = SOON[name] != undefined;
            svList.visible = name == "now" || name == "library";
            if (SOON[name] != undefined) {
                soonTitle.value = SOON[name];
                soonText.value = SOON[name] + " is coming in a later version of Pardeh.";
            }
            for (var key in TABS) {
                var id = TABS[key];
                var sel = key == name;
                eval(id).down = sel;
                if (!IsHidden(key)) {
                    eval(id + "L").visible = !sel;
                    eval(id + "S").visible = sel;
                }
            }
        }

        function OnOpenState(state) {
            if (state != osMediaOpen) return;
            var video = player.currentMedia && player.currentMedia.imageSourceWidth > 0;
            vid.visible = video;
            vis.visible = !video;
            tTitle.visible = !video;
            tCaption.visible = !video;
            if (video && g_pane != "now") ShowPane("now");
        }
        """;
}
