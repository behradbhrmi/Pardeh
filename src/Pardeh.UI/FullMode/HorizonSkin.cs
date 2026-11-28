using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Pardeh.Skinning.Model;
using Pardeh.Skinning.Wmp;
using static Pardeh.UI.FullMode.LunaArt;

namespace Pardeh.UI.FullMode;

/// <summary>
/// "Horizon": a full mode that follows the layout of the community WMP 9 web clone
/// (github.com/rmellis/Windows-Media-Player-9-WebApp-Clone, its own screenshot social-share.png):
/// <list type="bullet">
/// <item>a pale top bar with a two-tier blue logo block (round logo and white menus; a darker strip with a sizer
/// knob below it over the rail) and a rounded "Now Playing" tab on the right;</item>
/// <item>a full-height pale-blue task rail with two-line bold labels, a white pill and a caret for the open task,
/// a chevron and the emblem at its foot;</item>
/// <item>a centre column: the stage with a small caption and a big bold title over it, a dark status strip with green
/// text and time, and its own seek row with pill end buttons;</item>
/// <item>a full-height right column: "Now Playing" header, "Find Album Info", the cover centred over its title, the
/// list, a footer with buttons and total time, and a green stereo meter;</item>
/// <item>a silver transport under the centre and right columns: a big play orb, stop, joined previous / next, a
/// volume capsule, and shuffle / repeat with two round buttons at the far right;</item>
/// <item>a blue frame around everything.</item>
/// </list>
/// All art is drawn here (no code, CSS or images from the clone, which is GPL-2 and hot-links images of unclear
/// origin).
/// </summary>
internal static class HorizonSkin
{
    public static string SourcePath => FullModeDesign.Horizon.SourcePath;

    // Design size and metrics (skin pixels).
    private const int W = 800, H = 540;
    private const int F = 4;                         // blue frame
    private const int Top = 32;                      // top bar
    private const int Rail = 112;                    // rail column (with the left frame)
    private const int RightW = 214;                  // right column (with the right frame)
    private const int Cap = 22;                      // dark strip under the logo block
    private const int Foot = 96;                     // rail foot (chevron, emblem)
    private const int Transport = 46;                // transport (with the bottom frame)
    private const int SeekRow = 16, StatusRow = 18;
    private const int CenterW = W - Rail - RightW;
    private const int StageH = H - Top - Transport - SeekRow - StatusRow;
    private const int ColumnH = H - Top - Transport; // right column height
    private const int TopLeftW = 240, TopRightW = 250, TransportRightW = 240;

    // Palette (measured by eye from the clone's screenshot; values are Pardeh's own).
    private static readonly Color Outline = Color.Parse("#1B3170");
    private static readonly Color FrameLight = Color.Parse("#7A9AD6");
    private static readonly Color FrameDark = Color.Parse("#4365AE");
    private static readonly Color BarLight = Color.Parse("#F6F9FE");
    private static readonly Color BarDark = Color.Parse("#DCE6F6");
    private static readonly Color LogoLight = Color.Parse("#7898D6");
    private static readonly Color LogoMid = Color.Parse("#5476C0");
    private static readonly Color LogoDark = Color.Parse("#3F5FAA");
    private static readonly Color CapLight = Color.Parse("#5072BA");
    private static readonly Color CapDark = Color.Parse("#3A5799");
    private static readonly Color RailLight = Color.Parse("#E6EEFA");
    private static readonly Color RailDark = Color.Parse("#B3CAEC");
    private static readonly Color RailInk = Color.Parse("#16306E");
    private static readonly Color Silver = Color.Parse("#F1F5FC");
    private static readonly Color SilverDark = Color.Parse("#C4D3EC");
    private static readonly Color SeekBand = Color.Parse("#DDE7F6");
    private static readonly Color Panel = Color.Parse("#4A5C9A");
    private static readonly Color PanelDark = Color.Parse("#2F4377");
    private static readonly Color Ink = Color.Parse("#1D2F68");

    public static SkinPackage Build()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        AddChrome(files);
        AddButtons(files);
        files["horizon.wms"] = Encoding.UTF8.GetBytes(Layout);
        files["horizon.js"] = Encoding.UTF8.GetBytes(Script);
        return WmzLoader.FromFiles(files, SourcePath);
    }

    private static IBrush Solid(Color c) => new SolidColorBrush(c);

    private static IBrush Solid(string c) => new SolidColorBrush(Color.Parse(c));

    // ---- Chrome ---------------------------------------------------------------------------------

    private static void AddChrome(Dictionary<string, byte[]> files)
    {
        files["top_left.png"] = Png(TopLeftW, Top, TopLeft);
        files["top_mid.png"] = Png(1, Top, g => TopBand(g, 1));
        files["top_right.png"] = Png(TopRightW, Top, TopRight);
        files["rail_cap.png"] = Png(Rail, Cap, RailCap);
        files["rail_tile.png"] = Png(Rail, 1, g => RailBody(g, 1));
        files["rail_foot.png"] = Png(Rail, Foot, RailFoot);
        files["frame_right.png"] = Png(F, 1, g =>
        {
            g.FillRectangle(Horizontal((FrameLight, 0), (FrameDark, 1)), new Rect(0, 0, F, 1));
            g.FillRectangle(Solid(Outline), new Rect(F - 1, 0, 1, 1));
        });
        files["seek_band.png"] = Png(1, SeekRow, g =>
        {
            g.FillRectangle(Vertical((Color.Parse("#EAF0FA"), 0), (SeekBand, 1)), new Rect(0, 0, 1, SeekRow));
            g.FillRectangle(Solid("#9DB3DA"), new Rect(0, SeekRow - 1, 1, 1));
        });
        files["transport_mid.png"] = Png(1, Transport, g => TransportBand(g, 1));
        files["transport_right.png"] = Png(TransportRightW, Transport, TransportRight);
        files["column_header.png"] = Png(RightW - F, 22, g =>
        {
            g.FillRectangle(Vertical((Color.Parse("#5B6FB0"), 0), (Color.Parse("#40549A"), 1)), new Rect(0, 0, RightW - F, 22));
            g.FillRectangle(Solid(Outline), new Rect(0, 21, RightW - F, 1));
        });
        files["column_footer.png"] = Png(RightW - F, 26, g =>
        {
            g.FillRectangle(Vertical((Color.Parse("#3A4D86"), 0), (PanelDark, 1)), new Rect(0, 0, RightW - F, 26));
            g.FillRectangle(Solid("#6276AD"), new Rect(0, 0, RightW - F, 1));
        });
        files["column_edge.png"] = Png(2, 1, g =>
        {
            g.FillRectangle(Solid(Outline), new Rect(0, 0, 1, 1));
            g.FillRectangle(Solid("#7084BE"), new Rect(1, 0, 1, 1));
        });
        // The idle stage: a deep blue glow fading to black.
        files["stage_glow.png"] = Png(CenterW, StageH, g =>
            g.FillRectangle(new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.6, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.6, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#22398A"), 0), new GradientStop(Color.Parse("#0B1430"), 0.6), new GradientStop(Colors.Black, 1) },
            }, new Rect(0, 0, CenterW, StageH)));
        files["status_play.png"] = Png(9, 9, g => g.DrawGeometry(Solid("#00D000"), null, Geo("M 1,0.5 L 8.5,4.5 L 1,8.5 Z")));
        // The stereo meter strip: the column's corner rounds off into the silver transport below it.
        files["meter.png"] = Png(RightW - F - 2, 32, g =>
        {
            g.FillRectangle(Vertical((Silver, 0), (SilverDark, 1)), new Rect(0, 0, RightW - F - 2, 32));
            g.DrawRectangle(Vertical((Color.Parse("#43579A"), 0), (Color.Parse("#34478A"), 1)), null, new RoundedRect(new Rect(0, -10, RightW - F - 2, 42), 0, 0, 0, 18));
            g.DrawGeometry(null, Pen(Outline), Geo(FormattableString.Invariant($"M 0.5,0 L 0.5,{13.5} Q 0.5,31.5 18.5,31.5 L {RightW - F},31.5")));
        });
        files["album_frame.png"] = Png(82, 82, g =>
        {
            g.DrawRectangle(Solid(Color.FromArgb(80, 0, 0, 0)), null, new Rect(2, 2, 80, 80));
            g.DrawRectangle(Vertical((Color.Parse("#7891CB"), 0), (Color.Parse("#34488A"), 1)), Pen(Color.Parse("#1E2D5C")), new Rect(0.5, 0.5, 79, 79));
            var ink = Solid(Color.FromArgb(200, 255, 255, 255));
            g.DrawGeometry(ink, null, Geo("M 32,23 L 53,18 L 53,22 L 32,27 Z"));
            g.FillRectangle(ink, new Rect(32, 25, 2.5, 25));
            g.FillRectangle(ink, new Rect(50.5, 20, 2.5, 25));
            g.DrawEllipse(ink, null, new Point(28.5, 51), 6, 4.6);
            g.DrawEllipse(ink, null, new Point(47, 46), 6, 4.6);
        });
    }

    private static void FrameTop(DrawingContext g, double w)
    {
        g.FillRectangle(Solid(Outline), new Rect(0, 0, w, 1));
        g.FillRectangle(Solid(Color.FromArgb(220, 255, 255, 255)), new Rect(0, 1, w, 1));
    }

    private static void TopBand(DrawingContext g, double w)
    {
        g.FillRectangle(Vertical((BarLight, 0), (BarDark, 1)), new Rect(0, 0, w, Top));
        FrameTop(g, w);
        g.FillRectangle(Solid("#A9BEE2"), new Rect(0, Top - 1, w, 1));
    }

    private static void TopLeft(DrawingContext g)
    {
        using (g.PushClip(new RoundedRect(new Rect(0, 0, TopLeftW, Top + 10), 8, 0, 0, 0)))
        {
            TopBand(g, TopLeftW);
            // The blue logo block with its rounded lower-right corner; it continues into the strip over the rail.
            var block = Geo("M 0,0 L 206,0 L 206,14 Q 206,32 188,32 L 0,32 Z");
            g.DrawGeometry(Vertical((LogoLight, 0), (LogoMid, 0.55), (LogoDark, 1)), null, block);
            g.DrawGeometry(null, Pen(Color.FromArgb(120, 255, 255, 255)), Geo("M 1,1.5 L 205,1.5"));
            g.DrawGeometry(null, Pen(Outline), Geo("M 206.5,0 L 206.5,14 Q 206.5,32.5 188,32.5"));
            g.DrawGeometry(null, Pen(Outline), Geo("M 0.5,40 L 0.5,8.5 Q 0.5,0.5 8.5,0.5 L 240,0.5"));
        }

        // The round logo.
        PardehLogo.Draw(g, new Rect(5, 2, 28, 28));
    }

    private static void TopRight(DrawingContext g)
    {
        using (g.PushClip(new RoundedRect(new Rect(-10, 0, TopRightW + 10, Top + 10), 0, 8, 0, 0)))
        {
            TopBand(g, TopRightW);
            g.DrawGeometry(null, Pen(Outline), Geo(FormattableString.Invariant($"M 0,0.5 L {TopRightW - 8.5},0.5 Q {TopRightW - 0.5},0.5 {TopRightW - 0.5},8.5 L {TopRightW - 0.5},{Top}")));
        }

        // The "Now Playing" tab.
        var tab = new RoundedRect(new Rect(8.5, 4.5, 170, 24), 6);
        g.DrawRectangle(Vertical((Colors.White, 0), (Color.Parse("#E4ECF8"), 1)), Pen(Color.Parse("#9DB2DC")), tab);
        g.DrawRectangle(null, Pen(Color.FromArgb(230, 255, 255, 255)), new RoundedRect(new Rect(9.5, 5.5, 168, 22), 5));
    }

    private static void RailBody(DrawingContext g, double h)
    {
        // Left frame, then the pale rail.
        g.FillRectangle(Horizontal((FrameDark, 0), (FrameLight, 1)), new Rect(0, 0, F, h));
        g.FillRectangle(Solid(Outline), new Rect(0, 0, 1, h));
        g.FillRectangle(Horizontal((RailLight, 0), (Color.Parse("#D0DEF4"), 0.6), (RailDark, 1)), new Rect(F, 0, Rail - F, h));
        g.FillRectangle(Solid(Colors.White), new Rect(F, 0, 1, h));
        g.FillRectangle(Solid("#7F9ACB"), new Rect(Rail - 1, 0, 1, h));
    }

    private static void RailCap(DrawingContext g)
    {
        RailBody(g, Cap);
        // The darker strip under the logo block, with the round sizer knob.
        g.FillRectangle(Vertical((CapLight, 0), (CapDark, 1)), new Rect(F, 0, Rail - F, Cap));
        g.FillRectangle(Solid(Outline), new Rect(F, Cap - 1, Rail - F, 1));
        g.DrawEllipse(Radial(Color.Parse("#E7EEFA"), Color.Parse("#8EA6D6")), Pen(Outline), new Point(F + 14, Cap / 2.0), 7, 7);
        g.DrawEllipse(Solid(Color.Parse("#3A5799")), null, new Point(F + 14, Cap / 2.0), 2.2, 2.2);
    }

    private static void RailFoot(DrawingContext g)
    {
        using (g.PushClip(new RoundedRect(new Rect(0, -10, Rail + 10, Foot + 10), 0, 0, 0, 8)))
        {
            // The blue foot, then the pale rail ending in a curve that falls to the right (as in the clone).
            g.FillRectangle(Vertical((Color.Parse("#6F8FD0"), 0), (FrameDark, 1)), new Rect(0, 0, Rail, Foot));
            var curve = FormattableString.Invariant($"M 0,0 L {Rail},0 L {Rail},{Foot - 30} Q {Rail * 0.55},{Foot - 58} 0,{Foot - 52} Z");
            using (g.PushGeometryClip(Geo(curve)))
                RailBody(g, Foot);
            g.DrawGeometry(null, Pen(Color.Parse("#9DB4E4"), 1.5), Geo(FormattableString.Invariant($"M 0,{Foot - 52} Q {Rail * 0.55},{Foot - 58} {Rail},{Foot - 30}")));
            g.FillRectangle(Solid(Outline), new Rect(0, 0, 1, Foot));
            g.DrawGeometry(null, Pen(Outline), Geo(FormattableString.Invariant($"M 0.5,0 L 0.5,{Foot - 8.5} Q 0.5,{Foot - 0.5} 8.5,{Foot - 0.5} L {Rail},{Foot - 0.5}")));
        }

        // A double chevron (more tasks) and the logo in the blue foot.
        var chevron = new Pen(Solid(Ink), 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var cx = Rail / 2.0 + 2;
        g.DrawGeometry(null, chevron, Geo(FormattableString.Invariant($"M {cx - 4},6 L {cx},10 L {cx + 4},6 M {cx - 4},11 L {cx},15 L {cx + 4},11")));
        PardehLogo.Draw(g, new Rect(Rail / 2.0 - 15, Foot - 40, 32, 32));
    }

    private static void TransportBand(DrawingContext g, double w)
    {
        g.FillRectangle(Vertical((Silver, 0), (Color.Parse("#E2EAF7"), 0.4), (SilverDark, 1)), new Rect(0, 0, w, Transport));
        g.FillRectangle(Solid("#A3B8DE"), new Rect(0, 0, w, 1));
        g.FillRectangle(Solid(Colors.White), new Rect(0, 1, w, 1));
        g.FillRectangle(Vertical((FrameLight, 0), (FrameDark, 1)), new Rect(0, Transport - F, w, F));
        g.FillRectangle(Solid(Outline), new Rect(0, Transport - 1, w, 1));
    }

    private static void TransportRight(DrawingContext g)
    {
        using (g.PushClip(new RoundedRect(new Rect(-10, -10, TransportRightW + 10, Transport + 10), 0, 0, 8, 0)))
        {
            TransportBand(g, TransportRightW);
            g.FillRectangle(Horizontal((FrameLight, 0), (FrameDark, 1)), new Rect(TransportRightW - F, 0, F, Transport));
            g.DrawGeometry(null, Pen(Outline), Geo(FormattableString.Invariant(
                $"M {TransportRightW - 0.5},0 L {TransportRightW - 0.5},{Transport - 8.5} Q {TransportRightW - 0.5},{Transport - 0.5} {TransportRightW - 8.5},{Transport - 0.5} L 0,{Transport - 0.5}")));
        }

        // Resize grip.
        var dots = Solid("#6F86C2");
        foreach (var (dx, dy) in new[] { (0, 8), (4, 4), (4, 8), (8, 0), (8, 4), (8, 8) })
            g.FillRectangle(dots, new Rect(TransportRightW - 17 + dx, Transport - 18 + dy, 2, 2));
    }

    private static void DrawEmblem(DrawingContext g, Rect r)
    {
        var colors = new[] { Color.Parse("#E8483B"), Color.Parse("#F2B530"), Color.Parse("#3C9B4A"), Color.Parse("#2E6FD8") };
        var fold = r.Width / 4;
        for (var i = 0; i < 4; i++)
        {
            var x = r.X + i * fold;
            var geometry = Geo(FormattableString.Invariant(
                $"M {x},{r.Y + 2} Q {x + fold / 2},{r.Y - 2} {x + fold},{r.Y + 2} L {x + fold - 1},{r.Bottom} Q {x + fold / 2},{r.Bottom - 4} {x + 1},{r.Bottom} Z"));
            var dark = Color.FromRgb((byte)(colors[i].R * 0.7), (byte)(colors[i].G * 0.7), (byte)(colors[i].B * 0.7));
            g.DrawGeometry(Vertical((Colors.White, 0), (colors[i], 0.35), (dark, 1)), Pen(Color.FromArgb(70, 0, 0, 0), 0.6), geometry);
        }
    }

    // ---- Buttons --------------------------------------------------------------------------------

    private enum State { Normal, Hover, Down, Disabled }

    private static readonly (State State, string Suffix)[] States =
        [(State.Normal, ""), (State.Hover, "_hover"), (State.Down, "_down"), (State.Disabled, "_disabled")];

    private static Color InkFor(State s) => s == State.Disabled ? Color.Parse("#A3ADC6") : Ink;

    private static IBrush Face(State s, bool vertical = true) => s switch
    {
        State.Hover => Vertical((Colors.White, 0), (Color.Parse("#D8EBFF"), 0.5), (Color.Parse("#A4CDF7"), 1)),
        State.Down => Vertical((Color.Parse("#B7C7E6"), 0), (Color.Parse("#E0E8F6"), 1)),
        State.Disabled => Vertical((Color.Parse("#F7F9FC"), 0), (Color.Parse("#DDE3EE"), 1)),
        _ => Vertical((Colors.White, 0), (Color.Parse("#E6EDF8"), 0.5), (Color.Parse("#BACAE6"), 1)),
    };

    private static void AddButtons(Dictionary<string, byte[]> files)
    {
        foreach (var (state, suffix) in States)
        {
            files[$"play{suffix}.png"] = Png(40, 40, g => Orb(g, 40, state, hero: true, c => Play(g, c, 20, 20, 8)));
            files[$"pause{suffix}.png"] = Png(40, 40, g => Orb(g, 40, state, hero: true, c => Pause(g, c, 20, 20, 7.5)));
            files[$"stop{suffix}.png"] = Png(28, 28, g => Orb(g, 28, state, hero: false, c => g.DrawRectangle(Solid(c), null, new RoundedRect(new Rect(9.5, 9.5, 9, 9), 1))));
            files[$"prev{suffix}.png"] = Png(26, 26, g => Half(g, state, left: true));
            files[$"next{suffix}.png"] = Png(26, 26, g => Half(g, state, left: false));
            files[$"mute{suffix}.png"] = Png(22, 22, g => Orb(g, 22, state, hero: false, c => Speaker(g, c, crossed: false)));
            files[$"muted{suffix}.png"] = Png(22, 22, g => Orb(g, 22, state, hero: false, c => Speaker(g, c, crossed: true)));
            files[$"round{suffix}.png"] = Png(24, 24, g => Orb(g, 24, state, hero: false, _ => { }));
            files[$"skinmode{suffix}.png"] = Png(24, 24, g => Orb(g, 24, state, hero: false, c => Stroke(g, c, "M 7.5,7.5 L 16.5,7.5 L 16.5,16.5 L 7.5,16.5 Z M 10.5,10.5 L 13.5,10.5 L 13.5,13.5 L 10.5,13.5 Z")));
            files[$"fullscreen{suffix}.png"] = Png(24, 24, g => Orb(g, 24, state, hero: false, c => Stroke(g, c, "M 7,10 L 7,7 L 10,7 M 14,7 L 17,7 L 17,10 M 17,14 L 17,17 L 14,17 M 10,17 L 7,17 L 7,14")));
            files[$"shuffle{suffix}.png"] = Png(20, 18, g => FlatGlyph(g, state, "M 3,5 L 6,5 L 13,13 L 16,13 M 3,13 L 6,13 L 13,5 L 16,5 M 14,3 L 16,5 L 14,7 M 14,11 L 16,13 L 14,15"));
            files[$"repeat{suffix}.png"] = Png(20, 18, g => FlatGlyph(g, state, "M 4,10 L 4,6 L 15,6 M 16,8 L 16,12 L 5,12 M 13,4 L 15,6 L 13,8 M 7,10 L 5,12 L 7,14"));
            files[$"seek_back{suffix}.png"] = Png(24, 14, g => EndPill(g, state, back: true));
            files[$"seek_fwd{suffix}.png"] = Png(24, 14, g => EndPill(g, state, back: false));
            files[$"viz_prev{suffix}.png"] = Png(12, 12, g => Tiny(g, state, "M 8,2.5 L 4,6 L 8,9.5 Z"));
            files[$"viz_next{suffix}.png"] = Png(12, 12, g => Tiny(g, state, "M 4,2.5 L 8,6 L 4,9.5 Z"));
            files[$"min{suffix}.png"] = Png(18, 16, g => WindowButton(g, state, "M 5,11.5 L 13,11.5"));
            files[$"max{suffix}.png"] = Png(18, 16, g => WindowButton(g, state, "M 5,4.5 L 13,4.5 L 13,11.5 L 5,11.5 Z M 5,5.5 L 13,5.5"));
            files[$"close{suffix}.png"] = Png(18, 16, g => WindowButton(g, state, "M 5.5,4.5 L 12.5,11.5 M 12.5,4.5 L 5.5,11.5", close: true));
            files[$"list_add{suffix}.png"] = Png(18, 18, g => SquareButton(g, state, "M 9,5 L 9,13 M 5,9 L 13,9"));
            files[$"list_clear{suffix}.png"] = Png(18, 18, g => SquareButton(g, state, "M 5.5,5.5 L 12.5,12.5 M 12.5,5.5 L 5.5,12.5"));
        }

        // The volume capsule (holds the mute button and the slider).
        files["volume_capsule.png"] = Png(116, 26, g =>
        {
            g.DrawRectangle(Vertical((Color.Parse("#C7D5EE"), 0), (Colors.White, 1)), Pen(Color.Parse("#8EA4D2")), new RoundedRect(new Rect(0.5, 0.5, 115, 25), 12.5));
            g.DrawRectangle(null, Pen(Color.FromArgb(160, 255, 255, 255)), new RoundedRect(new Rect(1.5, 1.5, 113, 23), 11.5));
        });

        files["rail_item.png"] = Png(Rail - F - 6, 44, g => g.FillRectangle(Solid(Color.FromArgb(2, 255, 255, 255)), new Rect(0, 0, Rail - F - 6, 44)));
        files["rail_item_hover.png"] = Png(Rail - F - 6, 44, g => RailPill(g, active: false));
        files["rail_item_down.png"] = Png(Rail - F - 6, 44, g => RailPill(g, active: true));

        files["seek_track.png"] = Png(6, 10, g =>
        {
            g.FillRectangle(Vertical((Color.Parse("#C3D3EA"), 0), (Color.Parse("#F3F7FD"), 1)), new Rect(0, 1, 6, 8));
            g.FillRectangle(Solid("#8DA2CB"), new Rect(0, 1, 6, 1));
            g.FillRectangle(Solid(Colors.White), new Rect(0, 9, 6, 1));
        });
        files["seek_fill.png"] = Png(6, 10, g =>
            g.FillRectangle(Vertical((Color.Parse("#DCF2CC"), 0), (Color.Parse("#9ED07A"), 0.6), (Color.Parse("#77AE4E"), 1)), new Rect(0, 2, 6, 7)));
        files["seek_thumb.png"] = Png(26, 12, g => Capsule(g, new Rect(0.5, 0.5, 25, 11), "#5EE256", "#1BA811", "#0B6E06"));
        files["seek_thumb_hover.png"] = Png(26, 12, g => Capsule(g, new Rect(0.5, 0.5, 25, 11), "#FFD27A", "#F08A1C", "#B85600"));
        files["volume_track.png"] = Png(70, 12, g =>
            g.DrawRectangle(Vertical((Color.Parse("#8BA1C6"), 0), (Color.Parse("#C9D6EC"), 1)), Pen(Color.Parse("#7088BA")), new RoundedRect(new Rect(3.5, 4.5, 63, 4), 2)));
        files["volume_fill.png"] = Png(70, 12, g =>
            g.DrawRectangle(Vertical((Color.Parse("#B8FFB0"), 0), (Color.Parse("#2FCF2A"), 1)), null, new RoundedRect(new Rect(4, 5, 62, 3), 1.5)));
        files["volume_thumb.png"] = Png(10, 16, g => Capsule(g, new Rect(0.5, 0.5, 9, 15), "#5EE256", "#1BA811", "#0B6E06"));
        files["volume_thumb_hover.png"] = Png(10, 16, g => Capsule(g, new Rect(0.5, 0.5, 9, 15), "#FFD27A", "#F08A1C", "#B85600"));
    }

    private static void Orb(DrawingContext g, int size, State state, bool hero, Action<Color> glyph)
    {
        var c = new Point(size / 2.0, size / 2.0);
        var r = size / 2.0 - 1;
        g.DrawEllipse(Solid(Color.FromArgb(55, 28, 46, 102)), null, new Point(c.X, c.Y + 1), r, r);
        var rim = state == State.Hover ? Color.Parse("#2F7FE0") : hero ? Color.Parse("#2C4790") : Color.Parse("#6F87C2");
        g.DrawEllipse(Face(state), Pen(rim, hero ? 2 : 1), c, r - (hero ? 1 : 0.5), r - (hero ? 1 : 0.5));
        g.DrawEllipse(Vertical((Color.FromArgb(230, 255, 255, 255), 0), (Color.FromArgb(0, 255, 255, 255), 1)), null, new Point(c.X, c.Y - r * 0.4), r * 0.7, r * 0.48);
        glyph(InkFor(state));
    }

    /// <summary>One half of the joined previous / next pill.</summary>
    private static void Half(DrawingContext g, State state, bool left)
    {
        var rect = left ? new RoundedRect(new Rect(0.5, 0.5, 25.5, 25), 12.5, 0, 0, 12.5) : new RoundedRect(new Rect(0, 0.5, 25.5, 25), 0, 12.5, 12.5, 0);
        g.DrawRectangle(Face(state), Pen(state == State.Hover ? Color.Parse("#2F7FE0") : Color.Parse("#6F87C2")), rect);
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
        g.DrawGeometry(Solid(c), null, Geo("M 5,9 L 8,9 L 11,6 L 11,16 L 8,13 L 5,13 Z"));
        if (crossed)
            g.DrawGeometry(null, new Pen(Solid("#C0392B"), 1.4, lineCap: PenLineCap.Round), Geo("M 13,8.5 L 17,13.5 M 17,8.5 L 13,13.5"));
        else
            g.DrawGeometry(null, new Pen(Solid(c), 1.1, lineCap: PenLineCap.Round), Geo("M 13,8.5 Q 15,11 13,13.5 M 15,7 Q 18.5,11 15,15"));
    }

    private static void Stroke(DrawingContext g, Color c, string data) =>
        g.DrawGeometry(null, new Pen(Solid(c), 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Geo(data));

    private static void FlatGlyph(DrawingContext g, State state, string data)
    {
        if (state is State.Hover or State.Down)
            g.DrawRectangle(Solid(Color.FromArgb((byte)(state == State.Down ? 90 : 150), 255, 255, 255)), Pen(Color.Parse("#9DB2DC")), new RoundedRect(new Rect(0.5, 0.5, 19, 17), 3));
        Stroke(g, state == State.Down ? Color.Parse("#2F7FE0") : InkFor(state), data);
    }

    private static void EndPill(DrawingContext g, State state, bool back)
    {
        g.DrawRectangle(Face(state), Pen(state == State.Hover ? Color.Parse("#2F7FE0") : Color.Parse("#7F95C8")), new RoundedRect(new Rect(0.5, 0.5, 23, 13), 6.5));
        var brush = Solid(InkFor(state));
        g.DrawGeometry(brush, null, Geo(back ? "M 11,3.5 L 6,7 L 11,10.5 Z M 17,3.5 L 12,7 L 17,10.5 Z" : "M 7,3.5 L 12,7 L 7,10.5 Z M 13,3.5 L 18,7 L 13,10.5 Z"));
    }

    private static void Tiny(DrawingContext g, State state, string data) =>
        g.DrawGeometry(Solid(state == State.Hover ? Color.Parse("#FFFFFF") : Color.Parse("#00D000")), null, Geo(data));

    private static void WindowButton(DrawingContext g, State state, string data, bool close = false)
    {
        if (state is State.Hover or State.Down)
        {
            var back = close ? Vertical((Color.Parse("#F29A86"), 0), (Color.Parse("#D2442A"), 1)) : Vertical((Colors.White, 0), (Color.Parse("#D3E2F7"), 1));
            g.DrawRectangle(back, Pen(close ? Color.Parse("#9E2A16") : Color.Parse("#8FA6D6")), new RoundedRect(new Rect(0.5, 0.5, 17, 15), 3));
        }

        var ink = close && state is State.Hover or State.Down ? Colors.White : Ink;
        g.DrawGeometry(null, new Pen(Solid(ink), 1.7, lineCap: PenLineCap.Round), Geo(data));
    }

    private static void SquareButton(DrawingContext g, State state, string data)
    {
        var face = state == State.Hover
            ? Vertical((Color.Parse("#B9CCF5"), 0), (Color.Parse("#6D8AD2"), 1))
            : Vertical((Color.Parse("#8EA6DF"), 0), (Color.Parse("#4A6BBD"), 1));
        g.DrawRectangle(face, Pen(Color.Parse("#22356E")), new RoundedRect(new Rect(0.5, 0.5, 17, 17), 2));
        g.DrawGeometry(null, new Pen(Solid(Colors.White), 1.6, lineCap: PenLineCap.Round), Geo(data));
    }

    private static void RailPill(DrawingContext g, bool active)
    {
        var w = Rail - F - 6;
        var rect = new RoundedRect(new Rect(0.5, 0.5, w - 12, 43), 4);
        if (active)
        {
            g.DrawRectangle(Vertical((Colors.White, 0), (Color.Parse("#DCE8F9"), 1)), Pen(Color.Parse("#7C97CF")), rect);
            g.DrawRectangle(null, Pen(Color.FromArgb(220, 255, 255, 255)), new RoundedRect(new Rect(1.5, 1.5, w - 14, 41), 3));
            // The caret beside the open task, outside the pill.
            g.DrawGeometry(Solid(RailInk), null, Geo(FormattableString.Invariant($"M {w - 8},17 L {w - 3},22 L {w - 8},27 Z")));
        }
        else
        {
            g.DrawRectangle(Solid(Color.FromArgb(120, 255, 255, 255)), Pen(Color.FromArgb(170, 143, 169, 216)), rect);
        }
    }

    private static void Capsule(DrawingContext g, Rect r, string light, string mid, string dark)
    {
        var radius = Math.Min(r.Width, r.Height) / 2;
        g.DrawRectangle(Vertical((Color.Parse(light), 0), (Color.Parse(mid), 0.55), (Color.Parse(dark), 1)), Pen(Color.Parse(dark)), new RoundedRect(r, radius));
        g.DrawRectangle(null, Pen(Color.FromArgb(140, 255, 255, 255)), new RoundedRect(r.Deflate(1.5), Math.Max(1, radius - 1.5)));
    }

    // ---- Layout & script ------------------------------------------------------------------------

    private static string RailItem(string id, string pane, string label, int index) => $$"""
                <BUTTON id="{{id}}" left="{{F + 4}}" top="{{6 + index * 48}}" width="{{Rail - F - 6}}" height="44"
                    image="rail_item.png" hoverImage="rail_item_hover.png" downImage="rail_item_down.png"
                    onClick="ShowPane('{{pane}}');" upToolTip="{{label.Replace("&#10;", " ")}}"/>
                <TEXT id="{{id}}Label" left="{{F + 12}}" top="{{6 + index * 48 + (label.Contains("&#10;") ? 8 : 15)}}" width="{{Rail - F - 30}}" height="30" value="{{label}}"
                    fontType="Tahoma" fontSize="8" fontStyle="bold" foregroundColor="#16306E"/>
        """;

    private static string Menu(string id, int left, int width, string label, string menu) =>
        $"""<TEXT id="{id}" left="{left}" top="9" width="{width}" value="{label}" fontType="Tahoma" fontSize="8" foregroundColor="#FFFFFF" hoverForegroundColor="#FFE38A" onClick="pardeh.command('menu', '{menu}');"/>""";

    private static string Layout => $$"""
        <THEME id="PardehHorizon" title="Pardeh Horizon" author="Pardeh contributors" pardehMode="full">
          <VIEW id="vFull" width="{{W}}" height="{{H}}" minWidth="700" minHeight="460" resizable="true"
                titleBar="false" backgroundColor="none" scriptFile="horizon.js"
                onLoad="OnLoad();" onClose="OnClose();">

            <PLAYER openstatechange="OnOpenState(NewState);"/>

            <!-- Top bar: logo block with the menus (hidden while Pardeh's own menu bar is shown), the Now Playing tab. -->
            <SUBVIEW id="svTop" left="0" top="0" width="{{W}}" height="{{Top}}" horizontalAlignment="stretch">
              <SUBVIEW left="0" top="0" width="{{TopLeftW}}" height="{{Top}}" backgroundImage="top_left.png">
                <SUBVIEW left="0" top="0" width="{{TopLeftW}}" height="{{Top}}" visible="wmpprop:!app.titleBarVisible">
                  {{Menu("mFile", 42, 26, "File", "file")}}
                  {{Menu("mView", 72, 30, "View", "view")}}
                  {{Menu("mPlay", 106, 26, "Play", "play")}}
                  {{Menu("mTools", 136, 32, "Tools", "tools")}}
                  {{Menu("mHelp", 172, 28, "Help", "help")}}
                </SUBVIEW>
                <TEXT left="42" top="9" width="150" value="Pardeh" visible="wmpprop:app.titleBarVisible" fontType="Tahoma" fontSize="8" fontStyle="bold" foregroundColor="#FFFFFF"/>
              </SUBVIEW>
              <SUBVIEW left="{{TopLeftW}}" top="0" width="{{W - TopLeftW - TopRightW}}" height="{{Top}}" backgroundImage="top_mid.png" backgroundTiled="true" horizontalAlignment="stretch"/>
              <SUBVIEW left="{{W - TopRightW}}" top="0" width="{{TopRightW}}" height="{{Top}}" backgroundImage="top_right.png" horizontalAlignment="right">
                <DROPDOWNPLAYLIST left="14" top="8" width="158" height="18"/>
                <BUTTON left="{{TopRightW - 70}}" top="8" image="min.png" hoverImage="min_hover.png" downImage="min_down.png" upToolTip="Minimize" onClick="view.minimize();"/>
                <BUTTON left="{{TopRightW - 50}}" top="8" image="max.png" hoverImage="max_hover.png" downImage="max_down.png" upToolTip="Maximize" onClick="pardeh.command('maximize');"/>
                <BUTTON left="{{TopRightW - 30}}" top="8" image="close.png" hoverImage="close_hover.png" downImage="close_down.png" upToolTip="Close" onClick="view.close();"/>
              </SUBVIEW>
            </SUBVIEW>

            <!-- Rail: the dark strip, the tasks, the foot with the emblem; full height. -->
            <SUBVIEW id="svRail" left="0" top="{{Top}}" width="{{Rail}}" height="{{H - Top}}" backgroundImage="rail_tile.png" backgroundTiled="true" verticalAlignment="stretch">
              <SUBVIEW left="0" top="0" width="{{Rail}}" height="{{Cap}}" backgroundImage="rail_cap.png">
                <TEXT left="{{F + 26}}" top="4" width="{{Rail - F - 30}}" value="pardeh" fontType="Tahoma" fontSize="8" foregroundColor="#B9C9EB"/>
              </SUBVIEW>
              <SUBVIEW id="svRailItems" left="0" top="{{Cap + 4}}" width="{{Rail}}" height="{{7 * 48 + 8}}">
        {{RailItem("railNow", "now", "Now&#10;Playing", 0)}}
        {{RailItem("railLibrary", "library", "Media&#10;Library", 1)}}
        {{RailItem("railRip", "cdrip", "Copy from&#10;CD", 2)}}
        {{RailItem("railRadio", "radio", "Radio&#10;Tuner", 3)}}
        {{RailItem("railBurn", "burn", "Copy to CD&#10;or Device", 4)}}
        {{RailItem("railInfo", "info", "Info&#10;Center", 5)}}
        {{RailItem("railSkins", "skins", "Settings", 6)}}
              </SUBVIEW>
              <SUBVIEW left="0" top="{{H - Top - Foot}}" width="{{Rail}}" height="{{Foot}}" backgroundImage="rail_foot.png" verticalAlignment="bottom"/>
            </SUBVIEW>

            <!-- Centre column: one pane at a time; Now Playing has the stage, the status strip and the seek row. -->
            <SUBVIEW id="svContent" left="{{Rail}}" top="{{Top}}" width="{{W - Rail - F}}" height="{{ColumnH}}" backgroundColor="#000000"
                horizontalAlignment="stretch" verticalAlignment="stretch">

              <SUBVIEW id="paneNow" left="0" top="0" width="{{W - Rail - F}}" height="{{ColumnH}}" horizontalAlignment="stretch" verticalAlignment="stretch">
                <SUBVIEW id="svStage" left="0" top="0" width="{{CenterW}}" height="{{StageH}}" backgroundImage="stage_glow.png" resizeBackgroundImage="true" backgroundColor="#000000" horizontalAlignment="stretch" verticalAlignment="stretch">
                  <WMPVIDEO id="vid" left="0" top="0" width="{{CenterW}}" height="{{StageH}}" visible="false" backgroundColor="#000000" horizontalAlignment="stretch" verticalAlignment="stretch"/>
                  <WMPEFFECTS id="vis" left="0" top="0" width="{{CenterW}}" height="{{StageH}}" visible="true" horizontalAlignment="stretch" verticalAlignment="stretch"/>
                  <TEXT id="tCaption" left="10" top="6" width="{{CenterW - 20}}" fontType="Tahoma" fontSize="8" foregroundColor="#E8EEF8" horizontalAlignment="stretch"
                      value="wmpprop:player.currentMedia &amp;&amp; player.currentMedia.getItemInfo('Author') ? player.currentMedia.getItemInfo('Author') : player.status"/>
                  <TEXT id="tTitle" left="10" top="19" width="{{CenterW - 20}}" fontType="Tahoma" fontSize="12" fontStyle="bold" foregroundColor="#FFFFFF" horizontalAlignment="stretch"
                      value="wmpprop:player.currentMedia ? player.currentMedia.name : 'Pardeh'"/>
                </SUBVIEW>
                <SUBVIEW id="svStatus" left="0" top="{{StageH}}" width="{{CenterW}}" height="{{StatusRow}}" backgroundColor="#252B2E" horizontalAlignment="stretch" verticalAlignment="bottom">
                  <SUBVIEW left="8" top="5" width="9" height="9" backgroundImage="status_play.png"/>
                  <TEXT left="21" top="3" width="{{CenterW - 133}}" fontType="Tahoma" fontSize="8" fontStyle="bold" foregroundColor="#00E000" horizontalAlignment="stretch"
                      value="wmpprop:player.currentMedia ? player.status + ': ' + player.currentMedia.name : player.status"/>
                  <TEXT left="{{CenterW - 104}}" top="3" width="60" justification="right" horizontalAlignment="right" fontType="Tahoma" fontSize="8" fontStyle="bold" foregroundColor="#00E000"
                      value="wmpprop:player.controls.currentPositionString"/>
                  <BUTTON left="{{CenterW - 38}}" top="3" image="viz_prev.png" hoverImage="viz_prev_hover.png" horizontalAlignment="right" upToolTip="Previous Visualization" onClick="vis.previous();"/>
                  <BUTTON left="{{CenterW - 22}}" top="3" image="viz_next.png" hoverImage="viz_next_hover.png" horizontalAlignment="right" upToolTip="Next Visualization" onClick="vis.next();"/>
                </SUBVIEW>
                <SUBVIEW id="svSeek" left="0" top="{{StageH + StatusRow}}" width="{{CenterW}}" height="{{SeekRow}}" backgroundImage="seek_band.png" backgroundTiled="true"
                    horizontalAlignment="stretch" verticalAlignment="bottom">
                  <BUTTON left="6" top="1" image="seek_back.png" hoverImage="seek_back_hover.png" downImage="seek_back_down.png" upToolTip="Rewind 10 seconds"
                      onClick="player.controls.currentPosition = Math.max(0, player.controls.currentPosition - 10);"/>
                  <SEEKSLIDER id="seek" left="36" top="3" width="{{CenterW - 72}}" height="10" horizontalAlignment="stretch"
                      backgroundImage="seek_track.png" foregroundImage="seek_fill.png" thumbImage="seek_thumb.png" thumbHoverImage="seek_thumb_hover.png"
                      tiled="true" slide="false" borderSize="0" direction="horizontal"/>
                  <BUTTON left="{{CenterW - 30}}" top="1" image="seek_fwd.png" hoverImage="seek_fwd_hover.png" downImage="seek_fwd_down.png" horizontalAlignment="right" upToolTip="Forward 10 seconds"
                      onClick="player.controls.currentPosition = player.controls.currentPosition + 10;"/>
                </SUBVIEW>

                <!-- Right column. -->
                <SUBVIEW id="svList" left="{{CenterW}}" top="0" width="{{RightW - F}}" height="{{ColumnH}}" backgroundColor="#4A5C9A" horizontalAlignment="right" verticalAlignment="stretch">
                  <SUBVIEW left="0" top="0" width="2" height="{{ColumnH}}" backgroundImage="column_edge.png" backgroundTiled="true" verticalAlignment="stretch"/>
                  <SUBVIEW left="2" top="0" width="{{RightW - F - 2}}" height="22" backgroundImage="column_header.png" resizeBackgroundImage="true" horizontalAlignment="stretch">
                    <TEXT left="{{RightW - F - 104}}" top="4" width="96" justification="right" horizontalAlignment="right" fontType="Tahoma" fontSize="8" fontStyle="bold" foregroundColor="#E8EEFA" value="▸ Now Playing"/>
                  </SUBVIEW>
                  <TEXT left="10" top="28" width="{{RightW - F - 20}}" justification="right" horizontalAlignment="stretch" fontType="Tahoma" fontSize="8" foregroundColor="#A4C1F4"
                      hoverForegroundColor="#FFFFFF" fontStyle="underline" value="Find Album Info" onClick="ShowPane('info');"/>
                  <SUBVIEW left="{{(RightW - F - 82) / 2}}" top="46" width="82" height="82" backgroundImage="album_frame.png" horizontalAlignment="center">
                    <SUBVIEW left="0" top="0" width="80" height="80" backgroundImage="WMPImage_AlbumArtLarge"/>
                  </SUBVIEW>
                  <TEXT left="8" top="134" width="{{RightW - F - 16}}" justification="center" horizontalAlignment="stretch" fontType="Tahoma" fontSize="8" foregroundColor="#FFFFFF"
                      value="wmpprop:player.currentMedia ? player.currentMedia.name : 'Nothing playing'"/>
                  <SUBVIEW left="4" top="152" width="{{RightW - F - 6}}" height="1" backgroundColor="#6276AD" horizontalAlignment="stretch"/>
                  <PLAYLIST id="ipl" left="4" top="156" width="{{RightW - F - 6}}" height="{{ColumnH - 156 - 62}}" backgroundColor="#4A5C9A" foregroundColor="#FFFFFF" horizontalAlignment="stretch" verticalAlignment="stretch"/>
                  <SUBVIEW left="2" top="{{ColumnH - 58}}" width="{{RightW - F - 2}}" height="26" backgroundImage="column_footer.png" resizeBackgroundImage="true" horizontalAlignment="stretch" verticalAlignment="bottom">
                    <BUTTON left="6" top="4" image="list_add.png" hoverImage="list_add_hover.png" upToolTip="Add to Now Playing" onClick="pardeh.command('menu', 'file');"/>
                    <BUTTON left="28" top="4" image="list_clear.png" hoverImage="list_clear_hover.png" upToolTip="Clear Now Playing" onClick="player.currentPlaylist.clear();"/>
                    <TEXT left="54" top="6" width="{{RightW - F - 64}}" justification="right" horizontalAlignment="stretch" fontType="Tahoma" fontSize="8" foregroundColor="#FFFFFF"
                        value="wmpprop:player.currentMedia ? 'Total Time: ' + player.currentMedia.durationString : ''"/>
                  </SUBVIEW>
                  <!-- Stereo meter. -->
                  <SUBVIEW left="2" top="{{ColumnH - 32}}" width="{{RightW - F - 2}}" height="32" backgroundImage="meter.png" resizeBackgroundImage="true" horizontalAlignment="stretch" verticalAlignment="bottom">
                    <TEXT left="10" top="10" width="54" fontType="Tahoma" fontSize="7" fontStyle="bold" foregroundColor="#00E676" value="STEREO"/>
                    <WMPEFFECTS id="meter" left="62" top="5" width="{{RightW - F - 74}}" height="22" visible="true"/>
                  </SUBVIEW>
                </SUBVIEW>
              </SUBVIEW>

              <SUBVIEW id="paneLibrary" left="0" top="0" width="{{W - Rail - F}}" height="{{ColumnH}}" visible="false" horizontalAlignment="stretch" verticalAlignment="stretch">
                <LIBRARY id="library" left="0" top="0" width="{{W - Rail - F}}" height="{{ColumnH}}" horizontalAlignment="stretch" verticalAlignment="stretch"/>
              </SUBVIEW>
              <SUBVIEW id="paneRadio" left="0" top="0" width="{{W - Rail - F}}" height="{{ColumnH}}" visible="false" horizontalAlignment="stretch" verticalAlignment="stretch">
                <LISTPANE kind="radio" left="0" top="0" width="{{W - Rail - F}}" height="{{ColumnH}}" horizontalAlignment="stretch" verticalAlignment="stretch"/>
              </SUBVIEW>
              <SUBVIEW id="paneBurn" left="0" top="0" width="{{W - Rail - F}}" height="{{ColumnH}}" visible="false" horizontalAlignment="stretch" verticalAlignment="stretch">
                <LISTPANE kind="device" left="0" top="0" width="{{W - Rail - F}}" height="{{ColumnH}}" horizontalAlignment="stretch" verticalAlignment="stretch"/>
              </SUBVIEW>
              <SUBVIEW id="paneSkins" left="0" top="0" width="{{W - Rail - F}}" height="{{ColumnH}}" visible="false" horizontalAlignment="stretch" verticalAlignment="stretch">
                <LISTPANE kind="settings" left="0" top="0" width="{{W - Rail - F}}" height="{{ColumnH}}" horizontalAlignment="stretch" verticalAlignment="stretch"/>
              </SUBVIEW>
              <SUBVIEW id="paneInfo" left="0" top="0" width="{{W - Rail - F}}" height="{{ColumnH}}" visible="false" backgroundColor="#1F2A5C" horizontalAlignment="stretch" verticalAlignment="stretch">
                <TEXT left="24" top="18" width="400" value="Info Center" fontType="Tahoma" fontSize="14" fontStyle="bold" foregroundColor="#FFFFFF"/>
                <SUBVIEW left="24" top="56" width="82" height="82" backgroundImage="album_frame.png">
                  <SUBVIEW left="0" top="0" width="80" height="80" backgroundImage="WMPImage_AlbumArtLarge"/>
                </SUBVIEW>
                <TEXT left="122" top="58" width="420" fontType="Tahoma" fontSize="11" fontStyle="bold" foregroundColor="#FFFFFF"
                    value="wmpprop:player.currentMedia ? player.currentMedia.name : 'Nothing is playing.'"/>
                <TEXT left="122" top="82" width="420" fontType="Tahoma" fontSize="9" foregroundColor="#D7E0F4"
                    value="wmpprop:player.currentMedia &amp;&amp; player.currentMedia.getItemInfo('Author') ? 'Artist: ' + player.currentMedia.getItemInfo('Author') : ''"/>
                <TEXT left="122" top="100" width="420" fontType="Tahoma" fontSize="9" foregroundColor="#D7E0F4"
                    value="wmpprop:player.currentMedia &amp;&amp; player.currentMedia.getItemInfo('Album') ? 'Album: ' + player.currentMedia.getItemInfo('Album') : ''"/>
                <TEXT left="122" top="118" width="420" fontType="Tahoma" fontSize="9" foregroundColor="#D7E0F4"
                    value="wmpprop:player.currentMedia &amp;&amp; player.currentMedia.getItemInfo('Genre') ? 'Genre: ' + player.currentMedia.getItemInfo('Genre') : ''"/>
                <TEXT left="122" top="136" width="420" fontType="Tahoma" fontSize="9" foregroundColor="#A4C1F4"
                    value="wmpprop:player.currentMedia ? 'Length: ' + player.currentMedia.durationString : ''"/>
              </SUBVIEW>
              <SUBVIEW id="paneSoon" left="0" top="0" width="{{W - Rail - F}}" height="{{ColumnH}}" visible="false" backgroundColor="#1F2A5C" horizontalAlignment="stretch" verticalAlignment="stretch">
                <TEXT id="soonTitle" left="24" top="18" width="400" value="" fontType="Tahoma" fontSize="14" fontStyle="bold" foregroundColor="#FFFFFF"/>
                <TEXT id="soonText" left="24" top="50" width="520" value="" fontType="Tahoma" fontSize="8" foregroundColor="#C9D3F0"/>
              </SUBVIEW>
            </SUBVIEW>

            <!-- Right frame (beside the right column). -->
            <SUBVIEW left="{{W - F}}" top="{{Top}}" width="{{F}}" height="{{ColumnH}}" backgroundImage="frame_right.png" backgroundTiled="true" horizontalAlignment="right" verticalAlignment="stretch"/>

            <!-- Transport under the centre and right columns. -->
            <SUBVIEW id="svTransport" left="{{Rail}}" top="{{H - Transport}}" width="{{W - Rail}}" height="{{Transport}}" horizontalAlignment="stretch" verticalAlignment="bottom">
              <SUBVIEW left="0" top="0" width="{{W - Rail - TransportRightW}}" height="{{Transport}}" backgroundImage="transport_mid.png" backgroundTiled="true" horizontalAlignment="stretch"/>
              <SUBVIEW left="{{W - Rail - TransportRightW}}" top="0" width="{{TransportRightW}}" height="{{Transport}}" backgroundImage="transport_right.png" horizontalAlignment="right">
                <SHUFFLEBUTTON left="{{TransportRightW - 180}}" top="13" image="shuffle.png" hoverImage="shuffle_hover.png" downImage="shuffle_down.png" upToolTip="Turn Shuffle On" downToolTip="Turn Shuffle Off"/>
                <REPEATBUTTON left="{{TransportRightW - 156}}" top="13" image="repeat.png" hoverImage="repeat_hover.png" downImage="repeat_down.png" upToolTip="Turn Repeat On" downToolTip="Turn Repeat Off"/>
                <BUTTON left="{{TransportRightW - 82}}" top="10" image="skinmode.png" hoverImage="skinmode_hover.png" downImage="skinmode_down.png" upToolTip="Switch to Skin Mode" onClick="pardeh.command('skinMode');"/>
                <BUTTON left="{{TransportRightW - 54}}" top="10" image="fullscreen.png" hoverImage="fullscreen_hover.png" downImage="fullscreen_down.png" upToolTip="Full Screen" onClick="pardeh.command('fullScreen');"/>
                <BUTTON left="{{TransportRightW - 20}}" top="{{Transport - 20}}" width="16" height="16" cursor="sizenwse" upToolTip="" onmousedown="view.size('bottomright');"/>
              </SUBVIEW>

              <PLAYBUTTON left="12" top="2" image="play.png" hoverImage="play_hover.png" downImage="play_down.png" disabledImage="play_disabled.png" upToolTip="Play"/>
              <PAUSEBUTTON left="12" top="2" visible="wmpenabled:player.controls.pause" image="pause.png" hoverImage="pause_hover.png" downImage="pause_down.png" disabledImage="pause_disabled.png" upToolTip="Pause"/>
              <STOPBUTTON left="58" top="8" image="stop.png" hoverImage="stop_hover.png" downImage="stop_down.png" disabledImage="stop_disabled.png" upToolTip="Stop"/>
              <PREVBUTTON left="96" top="9" image="prev.png" hoverImage="prev_hover.png" downImage="prev_down.png" disabledImage="prev_disabled.png" upToolTip="Previous"/>
              <NEXTBUTTON left="122" top="9" image="next.png" hoverImage="next_hover.png" downImage="next_down.png" disabledImage="next_disabled.png" upToolTip="Next"/>
              <SUBVIEW left="160" top="9" width="116" height="26" backgroundImage="volume_capsule.png">
                <MUTEBUTTON id="bMute" left="3" top="2" image="mute.png" hoverImage="mute_hover.png" downImage="muted.png" upToolTip="Mute" downToolTip="Sound"/>
                <VOLUMESLIDER left="34" top="7" width="70" height="12" backgroundImage="volume_track.png" foregroundImage="volume_fill.png"
                    thumbImage="volume_thumb.png" thumbHoverImage="volume_thumb_hover.png" borderSize="3" direction="horizontal"/>
              </SUBVIEW>
            </SUBVIEW>
          </VIEW>
        </THEME>
        """;

    private const string Script = """
        // Pardeh Horizon full mode — original script.
        var g_pane = "";
        var RAIL = { now: "railNow", library: "railLibrary", cdrip: "railRip", radio: "railRadio", burn: "railBurn", info: "railInfo", skins: "railSkins" };
        var SOON = { cdrip: "Copy from CD" };

        function OnLoad() {
            var pane = theme.loadPreference("Pane");
            ApplyPages();
            ShowPane(pane && RAIL[pane] && !IsHidden(pane) ? pane : "now");
            OnOpenState(player.openState);
        }

        function OnClose() {
            theme.savePreference("Pane", g_pane);
        }

        // The right column's width (the host's drag handle on its left edge): the stage and its two rows take what is left.
        function SetListWidth(width) {
            var change = width - svList.width;
            if (change == 0) return;
            svList.width = width;
            svStage.width = svStage.width - change;
            svStatus.width = svStatus.width - change;
            svSeek.width = svSeek.width - change;
        }

        // Pages (Settings › Pages): hide unticked tasks from the rail and close the gaps.
        var g_railTops = null;

        function IsHidden(name) {
            return ("," + pardeh.command("hiddenPages") + ",").indexOf("," + name + ",") >= 0;
        }

        function ApplyPages() {
            var keys = [];
            for (var key in RAIL) keys.push(key);
            if (g_railTops == null) {
                g_railTops = {};
                for (var i = 0; i < keys.length; i++)
                    g_railTops[keys[i]] = [eval(RAIL[keys[i]]).top, eval(RAIL[keys[i]] + "Label").top];
            }
            var first = g_railTops[keys[0]][0];
            var step = g_railTops[keys[1]][0] - first;
            var slot = 0;
            for (var j = 0; j < keys.length; j++) {
                var button = eval(RAIL[keys[j]]);
                var label = eval(RAIL[keys[j]] + "Label");
                var shown = !IsHidden(keys[j]);
                button.visible = shown;
                label.visible = shown;
                if (shown) {
                    button.top = first + slot * step;
                    label.top = button.top + (g_railTops[keys[j]][1] - g_railTops[keys[j]][0]);
                    slot++;
                }
            }
            if (g_pane != "" && IsHidden(g_pane)) ShowPane("now");
        }

        function ShowPane(name) {
            g_pane = name;
            paneNow.visible = name == "now";
            paneLibrary.visible = name == "library";
            paneRadio.visible = name == "radio";
            paneBurn.visible = name == "burn";
            paneSkins.visible = name == "skins";
            paneInfo.visible = name == "info";
            paneSoon.visible = SOON[name] != undefined;
            if (SOON[name] != undefined) {
                soonTitle.value = SOON[name];
                soonText.value = SOON[name] + " is coming in a later version of Pardeh.";
            }
            for (var key in RAIL)
                eval(RAIL[key]).down = (key == name);
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
