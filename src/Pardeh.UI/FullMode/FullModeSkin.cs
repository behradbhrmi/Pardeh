using System.Text;
using Avalonia;
using Avalonia.Media;
using Pardeh.Skinning.Model;
using Pardeh.Skinning.Wmp;
using static Pardeh.UI.FullMode.LunaArt;

namespace Pardeh.UI.FullMode;

/// <summary>
/// Builds Pardeh's full mode as a skin package: generated Luna-style art (original) plus a WMS layout and script.
/// Files in an optional asset folder (e.g. private/fullmode) override generated images with the same name.
/// </summary>
internal static class FullModeSkin
{
    // Design size and chrome metrics (skin pixels).
    private const int W = 690, H = 480;
    private const int Top = 54;          // top band
    private const int Rail = 98;         // left taskbar rail
    private const int Right = 8;         // right frame
    private const int Bottom = 92;       // status line (16) + seek row (16) + transport (60)
    private const int TopPiece = 64, BottomPiece = 100;
    private const int TopLeftW = 320, TopRightW = 250, BottomLeftW = 420, BottomRightW = 130;
    private const int ContentW = W - Rail - Right, ContentH = H - Top - Bottom;

    public const string SourcePath = "pardeh://full-mode";

    public static SkinPackage Build(string? overrideFolder)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        AddChrome(files);
        AddButtons(files);
        files["fullmode.wms"] = Encoding.UTF8.GetBytes(Layout);
        files["fullmode.js"] = Encoding.UTF8.GetBytes(Script);

        if (overrideFolder is not null && Directory.Exists(overrideFolder))
        {
            foreach (var file in Directory.EnumerateFiles(overrideFolder))
                files[Path.GetFileName(file)] = File.ReadAllBytes(file);
        }

        return WmzLoader.FromFiles(files, SourcePath);
    }

    // ---- Chrome ---------------------------------------------------------------------------------

    private static void AddChrome(Dictionary<string, byte[]> files)
    {
        var pixels = Render(W, H, DrawChrome);
        var stride = W * 4;
        files["top_left.png"] = Png(pixels, stride, 0, 0, TopLeftW, TopPiece);
        files["top_mid.png"] = Png(pixels, stride, TopLeftW, 0, 1, TopPiece);
        files["top_right.png"] = Png(pixels, stride, W - TopRightW, 0, TopRightW, TopPiece);
        files["left.png"] = Png(pixels, stride, 0, 200, Rail, 1);
        files["right.png"] = Png(pixels, stride, W - Right, 200, Right, 1);
        files["bottom_left.png"] = Png(pixels, stride, 0, H - BottomPiece, BottomLeftW, BottomPiece);
        files["bottom_mid.png"] = Png(pixels, stride, BottomLeftW, H - BottomPiece, 1, BottomPiece);
        files["bottom_right.png"] = Png(pixels, stride, W - BottomRightW, H - BottomPiece, BottomRightW, BottomPiece);
    }

    private static void DrawChrome(DrawingContext g)
    {
        // Window body.
        g.DrawRectangle(new SolidColorBrush(Frame), Pen(FrameDark), new RoundedRect(new Rect(0.5, 0.5, W - 1, H - 1), 12));

        // Top band.
        using (g.PushClip(new RoundedRect(new Rect(3, 3, W - 6, Top - 3), 10, 10, 0, 0)))
        {
            g.FillRectangle(Vertical((BandLight, 0), (BandMid, 0.45), (BandDark, 1)), new Rect(3, 3, W - 6, Top - 3));
            g.FillRectangle(new SolidColorBrush(WithAlpha(Colors.White, 150)), new Rect(3, 3, W - 6, 1));
        }

        // Raised tab holding the playlist selector and window buttons.
        var tab = new RoundedRect(new Rect(W - 238, 6, 228, 30), 9);
        g.DrawRectangle(Vertical((TabLight, 0), (TabFace, 1)), Pen(Color.Parse("#9EB0E0")), tab);
        g.DrawRectangle(null, Pen(WithAlpha(Colors.White, 200)), new RoundedRect(new Rect(W - 237, 7, 226, 28), 8));

        // Content well (panes draw over it).
        g.FillRectangle(new SolidColorBrush(Navy), new Rect(Rail, Top, ContentW, ContentH));
        g.DrawRectangle(null, Pen(Color.Parse("#0E1430")), new Rect(Rail - 0.5, Top - 0.5, ContentW + 1, ContentH + 1));

        // Taskbar rail.
        var rail = new RoundedRect(new Rect(5, Top + 2, Rail - 10, H - Top - 64), 9, 9, 0, 0);
        g.DrawRectangle(Horizontal((RailLight, 0), (RailMid, 0.65), (RailDark, 1)), Pen(Color.Parse("#9DB2E4")), rail);

        // Status line and seek row.
        g.FillRectangle(Brushes.Black, new Rect(Rail, H - Bottom, ContentW, 16));
        g.FillRectangle(new SolidColorBrush(SeekBand), new Rect(Rail, H - Bottom + 16, ContentW + Right - 3, 16));

        // Transport band with rounded bottom corners.
        var band = new RoundedRect(new Rect(3, H - 60, W - 6, 57), 0, 0, 11, 11);
        g.DrawRectangle(Vertical((TransportTop, 0), (Color.Parse("#E1E7F7"), 0.5), (TransportBottom, 1)), null, band);
        g.FillRectangle(Brushes.White, new Rect(3, H - 60, W - 6, 1));
        g.FillRectangle(new SolidColorBrush(Color.Parse("#A5B5E0")), new Rect(3, H - 61, W - 6, 1));

        // Pardeh emblem at the foot of the rail: three curtain folds (پرده = curtain / screen).
        DrawEmblem(g, new Rect(30, H - 98, 36, 34));

        // Resize grip.
        var dots = new SolidColorBrush(Color.Parse("#7A8CC4"));
        foreach (var (dx, dy) in new[] { (0, 8), (4, 4), (4, 8), (8, 0), (8, 4), (8, 8) })
            g.FillRectangle(dots, new Rect(W - 18 + dx, H - 16 + dy, 2, 2));
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
            g.DrawGeometry(Vertical((Colors.White, 0), (colors[i], 0.35), (Color.FromRgb((byte)(colors[i].R * 0.7), (byte)(colors[i].G * 0.7), (byte)(colors[i].B * 0.7)), 1)), Pen(WithAlpha(Colors.Black, 60), 0.6), geometry);
        }
    }

    // ---- Buttons --------------------------------------------------------------------------------

    private enum State { Normal, Hover, Down, Disabled }

    private static readonly (State State, string Suffix)[] States =
        [(State.Normal, ""), (State.Hover, "_hover"), (State.Down, "_down"), (State.Disabled, "_disabled")];

    private static void AddButtons(Dictionary<string, byte[]> files)
    {
        foreach (var (state, suffix) in States)
        {
            files[$"play{suffix}.png"] = Png(46, 46, g => RoundButton(g, 46, state, (gg, c) => Triangle(gg, c, 9)));
            files[$"pause{suffix}.png"] = Png(46, 46, g => RoundButton(g, 46, state, (gg, c) => Bars(gg, c, 9)));
            files[$"stop{suffix}.png"] = Png(30, 30, g => RoundButton(g, 30, state, (gg, c) => gg.FillRectangle(new SolidColorBrush(c), new Rect(11, 11, 8, 8))));
            files[$"prev{suffix}.png"] = Png(28, 30, g => PillHalf(g, state, left: true));
            files[$"next{suffix}.png"] = Png(28, 30, g => PillHalf(g, state, left: false));
            files[$"mute{suffix}.png"] = Png(30, 30, g => RoundButton(g, 30, state, (gg, c) => Speaker(gg, c, crossed: false)));
            files[$"muted{suffix}.png"] = Png(30, 30, g => RoundButton(g, 30, state, (gg, c) => Speaker(gg, c, crossed: true)));
            files[$"shuffle{suffix}.png"] = Png(24, 24, g => RoundButton(g, 24, state, (gg, c) => Glyph(gg, c, "M 6,8 L 9,8 L 15,16 L 18,16 M 6,16 L 9,16 L 15,8 L 18,8")));
            files[$"repeat{suffix}.png"] = Png(24, 24, g => RoundButton(g, 24, state, (gg, c) => Glyph(gg, c, "M 7,13 L 7,9 L 16,9 M 17,11 L 17,15 L 8,15 M 14,7 L 16,9 L 14,11 M 10,13 L 8,15 L 10,17")));
            files[$"skinmode{suffix}.png"] = Png(24, 24, g => RoundButton(g, 24, state, (gg, c) => Glyph(gg, c, "M 7,7 L 17,7 L 17,17 L 7,17 Z M 10,10 L 14,10 L 14,14 L 10,14 Z")));
            files[$"viz_prev{suffix}.png"] = Png(14, 14, g => SmallRound(g, state, left: true));
            files[$"viz_next{suffix}.png"] = Png(14, 14, g => SmallRound(g, state, left: false));
            files[$"min{suffix}.png"] = Png(18, 16, g => WindowGlyph(g, state, "M 5,11 L 12,11"));
            files[$"max{suffix}.png"] = Png(18, 16, g => WindowGlyph(g, state, "M 5,4 L 13,4 L 13,11 L 5,11 Z M 5,5 L 13,5"));
            files[$"close{suffix}.png"] = Png(18, 16, g => WindowGlyph(g, state, "M 5,4 L 12,11 M 12,4 L 5,11"));
        }

        files["rail_item.png"] = Png(88, 40, g => g.FillRectangle(new SolidColorBrush(Color.FromArgb(2, 255, 255, 255)), new Rect(0, 0, 88, 40)));
        files["rail_item_hover.png"] = Png(88, 40, g => RailTile(g, selected: false));
        files["rail_item_down.png"] = Png(88, 40, g => RailTile(g, selected: true));

        files["seek_track.png"] = Png(4, 10, g =>
        {
            g.FillRectangle(new SolidColorBrush(Track), new Rect(0, 1, 4, 8));
            g.FillRectangle(new SolidColorBrush(Color.Parse("#8E97B4")), new Rect(0, 1, 4, 1));
            g.FillRectangle(Brushes.White, new Rect(0, 9, 4, 1));
        });
        files["seek_fill.png"] = Png(4, 10, g =>
        {
            g.FillRectangle(Vertical((Color.Parse("#B9E0B6"), 0), (TrackFill, 0.5), (Color.Parse("#6FAE6C"), 1)), new Rect(0, 1, 4, 8));
        });
        files["seek_thumb.png"] = Png(28, 12, g => Capsule(g, new Rect(0.5, 0.5, 27, 11), hover: false));
        files["seek_thumb_hover.png"] = Png(28, 12, g => Capsule(g, new Rect(0.5, 0.5, 27, 11), hover: true));
        files["volume_track.png"] = Png(90, 22, g =>
        {
            var wedge = Geo("M 4,15 L 86,6 L 86,15 Z");
            g.DrawGeometry(Vertical((Color.Parse("#D7DEF0"), 0), (Color.Parse("#AEB9DA"), 1)), Pen(Color.Parse("#8C9CC8"), 0.8), wedge);
        });
        files["volume_fill.png"] = Png(90, 22, g =>
            g.DrawGeometry(Vertical((Color.Parse("#B9E0B6"), 0), (Color.Parse("#5FA85C"), 1)), null, Geo("M 4,15 L 86,6 L 86,15 Z")));
        files["volume_thumb.png"] = Png(12, 20, g => Capsule(g, new Rect(0.5, 0.5, 11, 19), hover: false));
        files["volume_thumb_hover.png"] = Png(12, 20, g => Capsule(g, new Rect(0.5, 0.5, 11, 19), hover: true));
        files["menu_mark.png"] = Png(18, 18, g =>
        {
            g.DrawEllipse(Radial(Colors.White, Color.Parse("#AFC2EC")), Pen(Color.Parse("#6F86C9")), new Point(9, 9), 8, 8);
            DrawEmblem(g, new Rect(4.5, 5, 9, 8));
        });
    }

    private static Color IconColor(State state) => state == State.Disabled ? Color.Parse("#A3ABC4") : Ink;

    private static void RoundButton(DrawingContext g, int size, State state, Action<DrawingContext, Color> icon)
    {
        var center = new Point(size / 2.0, size / 2.0);
        var r = size / 2.0 - 1;
        var face = state switch
        {
            State.Hover => Radial(Colors.White, Color.Parse("#C9E3FF")),
            State.Down => Radial(Color.Parse("#D5DDF0"), Color.Parse("#A9B8DE")),
            State.Disabled => Radial(Color.Parse("#F6F7FB"), Color.Parse("#D9DEEC")),
            _ => Radial(Colors.White, Color.Parse("#D0D9F0")),
        };
        var rim = state == State.Hover ? Color.Parse("#4F9BE8") : Color.Parse("#7F93CC");
        g.DrawEllipse(new SolidColorBrush(WithAlpha(Color.Parse("#30407A"), 50)), null, new Point(center.X, center.Y + 1), r, r);
        g.DrawEllipse(face, Pen(rim, state == State.Hover ? 1.5 : 1), center, r - 0.5, r - 0.5);
        g.DrawEllipse(null, Pen(WithAlpha(Colors.White, 220)), center, r - 2, r - 2);
        icon(g, IconColor(state));
    }

    private static void Triangle(DrawingContext g, Color color, double half)
    {
        var c = 23.0;
        g.DrawGeometry(new SolidColorBrush(color), null, Geo(FormattableString.Invariant($"M {c - half * 0.6},{c - half} L {c + half * 0.9},{c} L {c - half * 0.6},{c + half} Z")));
    }

    private static void Bars(DrawingContext g, Color color, double half)
    {
        var brush = new SolidColorBrush(color);
        g.FillRectangle(brush, new Rect(23 - half * 0.75, 23 - half, half * 0.55, half * 2));
        g.FillRectangle(brush, new Rect(23 + half * 0.2, 23 - half, half * 0.55, half * 2));
    }

    private static void Speaker(DrawingContext g, Color color, bool crossed)
    {
        g.DrawGeometry(new SolidColorBrush(color), null, Geo("M 8,12 L 11,12 L 15,8 L 15,22 L 11,18 L 8,18 Z"));
        if (crossed)
            g.DrawGeometry(null, Pen(Color.Parse("#C0392B"), 1.6), Geo("M 18,11 L 23,19 M 23,11 L 18,19"));
        else
            g.DrawGeometry(null, Pen(color, 1.2), Geo("M 18,11 Q 21,15 18,19 M 20,9 Q 25,15 20,21"));
    }

    private static void Glyph(DrawingContext g, Color color, string data) =>
        g.DrawGeometry(null, new Pen(new SolidColorBrush(color), 1.6, lineCap: PenLineCap.Round), Geo(data));

    private static void PillHalf(DrawingContext g, State state, bool left)
    {
        var rect = left ? new RoundedRect(new Rect(1, 1, 27, 28), 14, 0, 0, 14) : new RoundedRect(new Rect(0, 1, 27, 28), 0, 14, 14, 0);
        var face = state switch
        {
            State.Hover => Vertical((Colors.White, 0), (Color.Parse("#C9E3FF"), 1)),
            State.Down => Vertical((Color.Parse("#C5D0EC"), 0), (Color.Parse("#E4EAF7"), 1)),
            State.Disabled => Vertical((Color.Parse("#F6F7FB"), 0), (Color.Parse("#DDE2EE"), 1)),
            _ => Vertical((Colors.White, 0), (Color.Parse("#CBD5EE"), 1)),
        };
        g.DrawRectangle(face, Pen(state == State.Hover ? Color.Parse("#4F9BE8") : Color.Parse("#7F93CC")), rect);
        var c = IconColor(state);
        var brush = new SolidColorBrush(c);
        if (left)
        {
            g.FillRectangle(brush, new Rect(9, 10, 2, 10));
            g.DrawGeometry(brush, null, Geo("M 19,10 L 12,15 L 19,20 Z"));
        }
        else
        {
            g.FillRectangle(brush, new Rect(17, 10, 2, 10));
            g.DrawGeometry(brush, null, Geo("M 9,10 L 16,15 L 9,20 Z"));
        }
    }

    private static void SmallRound(DrawingContext g, State state, bool left)
    {
        var face = state == State.Hover ? Radial(Colors.White, Color.Parse("#9FB7E6")) : Radial(Color.Parse("#DDE4F4"), Color.Parse("#7E8DB8"));
        g.DrawEllipse(face, Pen(Color.Parse("#1A2040")), new Point(7, 7), 6, 6);
        g.DrawGeometry(new SolidColorBrush(Color.Parse("#1A2040")), null, Geo(left ? "M 8.5,4 L 4.5,7 L 8.5,10 Z" : "M 5.5,4 L 9.5,7 L 5.5,10 Z"));
    }

    private static void WindowGlyph(DrawingContext g, State state, string data)
    {
        if (state is State.Hover or State.Down)
            g.DrawRectangle(new SolidColorBrush(WithAlpha(Colors.White, (byte)(state == State.Down ? 110 : 170))), Pen(Color.Parse("#8FA4DC")), new RoundedRect(new Rect(0.5, 0.5, 17, 15), 3));
        g.DrawGeometry(null, new Pen(new SolidColorBrush(Ink), 2, lineCap: PenLineCap.Square), Geo(data));
    }

    private static void RailTile(DrawingContext g, bool selected)
    {
        var rect = new RoundedRect(new Rect(0.5, 0.5, 87, 39), 4);
        if (selected)
        {
            g.DrawRectangle(Vertical((Colors.White, 0), (Color.Parse("#E4EBF8"), 1)), Pen(Color.Parse("#A9BCE6")), rect);
            g.FillRectangle(new SolidColorBrush(Color.Parse("#7F99D9")), new Rect(1, 2, 2, 36));
        }
        else
        {
            g.DrawRectangle(new SolidColorBrush(WithAlpha(Colors.White, 150)), Pen(WithAlpha(Color.Parse("#A9BCE6"), 160)), rect);
        }
    }

    private static void Capsule(DrawingContext g, Rect r, bool hover)
    {
        var radius = Math.Min(r.Width, r.Height) / 2;
        g.DrawRectangle(Vertical((Color.Parse(hover ? "#9BF59A" : "#7CE07A"), 0), (Thumb, 0.55), (Color.Parse("#0E7C10"), 1)),
            Pen(Color.Parse("#0B5E0C")), new RoundedRect(r, radius));
        g.DrawRectangle(null, Pen(WithAlpha(Colors.White, 140)), new RoundedRect(r.Deflate(1.5), Math.Max(1, radius - 1.5)));
    }

    // ---- Layout & script ------------------------------------------------------------------------

    private static string Rail_Item(string id, string pane, string label, int index) => $$"""
                <BUTTON id="{{id}}" left="6" top="{{8 + index * 42}}" width="88" height="40"
                    image="rail_item.png" hoverImage="rail_item_hover.png" downImage="rail_item_down.png"
                    onClick="ShowPane('{{pane}}');" upToolTip="{{label.Replace("&#10;", " ")}}"/>
                <TEXT id="{{id}}Label" left="14" top="{{8 + index * 42 + 6}}" width="76" height="30" value="{{label}}"
                    fontType="Tahoma" fontSize="8" fontStyle="bold" foregroundColor="#212741"/>
        """;

    private static string Layout => $$"""
        <THEME id="PardehFullMode" title="Pardeh" author="Pardeh contributors" pardehMode="full">
          <VIEW id="vFull" width="{{W}}" height="{{H}}" minWidth="600" minHeight="420" resizable="true"
                titleBar="false" backgroundColor="none" scriptFile="fullmode.js"
                onLoad="OnLoad();" onClose="OnClose();">

            <PLAYER openstatechange="OnOpenState(NewState);"/>

            <!-- Chrome: nine-grid pieces (stretch with the window). -->
            <SUBVIEW id="svTop" left="0" top="0" width="{{W}}" height="{{TopPiece}}" horizontalAlignment="stretch">
              <SUBVIEW left="0" top="0" width="{{TopLeftW}}" height="{{TopPiece}}" backgroundImage="top_left.png"/>
              <SUBVIEW left="{{TopLeftW}}" top="0" width="{{W - TopLeftW - TopRightW}}" height="{{TopPiece}}" backgroundImage="top_mid.png" backgroundTiled="true" horizontalAlignment="stretch"/>
              <SUBVIEW id="svTopRight" left="{{W - TopRightW}}" top="0" width="{{TopRightW}}" height="{{TopPiece}}" backgroundImage="top_right.png" horizontalAlignment="right">
                <DROPDOWNPLAYLIST left="22" top="11" width="150" height="20"/>
                <BUTTON left="{{TopRightW - 68}}" top="13" image="min.png" hoverImage="min_hover.png" downImage="min_down.png" upToolTip="Minimize" onClick="view.minimize();"/>
                <BUTTON left="{{TopRightW - 50}}" top="13" image="max.png" hoverImage="max_hover.png" downImage="max_down.png" upToolTip="Maximize" onClick="pardeh.command('maximize');"/>
                <BUTTON left="{{TopRightW - 32}}" top="13" image="close.png" hoverImage="close_hover.png" downImage="close_down.png" upToolTip="Close" onClick="view.close();"/>
              </SUBVIEW>
              <BUTTON left="12" top="9" image="menu_mark.png" upToolTip="Pardeh" onClick="pardeh.command('menu', 'help');"/>
              <TEXT id="mFile" visible="wmpprop:!app.titleBarVisible" left="38" top="11" width="28" value="File" fontType="Tahoma" fontSize="8" foregroundColor="#102060" hoverForegroundColor="#FFFFFF" onClick="pardeh.command('menu', 'file');"/>
              <TEXT id="mView" visible="wmpprop:!app.titleBarVisible" left="68" top="11" width="32" value="View" fontType="Tahoma" fontSize="8" foregroundColor="#102060" hoverForegroundColor="#FFFFFF" onClick="pardeh.command('menu', 'view');"/>
              <TEXT id="mPlay" visible="wmpprop:!app.titleBarVisible" left="102" top="11" width="28" value="Play" fontType="Tahoma" fontSize="8" foregroundColor="#102060" hoverForegroundColor="#FFFFFF" onClick="pardeh.command('menu', 'play');"/>
              <TEXT id="mTools" visible="wmpprop:!app.titleBarVisible" left="132" top="11" width="34" value="Tools" fontType="Tahoma" fontSize="8" foregroundColor="#102060" hoverForegroundColor="#FFFFFF" onClick="pardeh.command('menu', 'tools');"/>
              <TEXT id="mHelp" visible="wmpprop:!app.titleBarVisible" left="168" top="11" width="30" value="Help" fontType="Tahoma" fontSize="8" foregroundColor="#102060" hoverForegroundColor="#FFFFFF" onClick="pardeh.command('menu', 'help');"/>
            </SUBVIEW>

            <SUBVIEW id="svRail" left="0" top="{{TopPiece}}" width="{{Rail}}" height="{{H - TopPiece - BottomPiece}}" backgroundImage="left.png" backgroundTiled="true" verticalAlignment="stretch"/>
            <SUBVIEW left="{{W - Right}}" top="{{TopPiece}}" width="{{Right}}" height="{{H - TopPiece - BottomPiece}}" backgroundImage="right.png" backgroundTiled="true" horizontalAlignment="right" verticalAlignment="stretch"/>

            <SUBVIEW id="svBottom" left="0" top="{{H - BottomPiece}}" width="{{W}}" height="{{BottomPiece}}" horizontalAlignment="stretch" verticalAlignment="bottom">
              <SUBVIEW left="0" top="0" width="{{BottomLeftW}}" height="{{BottomPiece}}" backgroundImage="bottom_left.png"/>
              <SUBVIEW left="{{BottomLeftW}}" top="0" width="{{W - BottomLeftW - BottomRightW}}" height="{{BottomPiece}}" backgroundImage="bottom_mid.png" backgroundTiled="true" horizontalAlignment="stretch"/>
              <SUBVIEW left="{{W - BottomRightW}}" top="0" width="{{BottomRightW}}" height="{{BottomPiece}}" backgroundImage="bottom_right.png" horizontalAlignment="right">
                <BUTTON left="{{BottomRightW - 50}}" top="{{BottomPiece - 36}}" image="skinmode.png" hoverImage="skinmode_hover.png" downImage="skinmode_down.png" upToolTip="Switch to Skin Mode" onClick="pardeh.command('skinMode');"/>
                <BUTTON left="{{BottomRightW - 20}}" top="{{BottomPiece - 20}}" width="16" height="16" cursor="sizenwse" upToolTip="" onmousedown="view.size('bottomright');"/>
              </SUBVIEW>

              <!-- Status line, seek bar and transport. -->
              <TEXT id="status" left="{{Rail + 24}}" top="{{BottomPiece - Bottom + 1}}" width="300" value="wmpprop:player.status" fontType="Tahoma" fontSize="8" foregroundColor="#7FFF00"/>
              <TEXT id="time" left="{{W - Right - 80}}" top="{{BottomPiece - Bottom + 1}}" width="74" value="wmpprop:player.controls.currentPositionString" justification="right" horizontalAlignment="right" fontType="Tahoma" fontSize="8" foregroundColor="#7FFF00"/>
              <SEEKSLIDER id="seek" left="{{Rail + 4}}" top="{{BottomPiece - Bottom + 19}}" width="{{ContentW - 8}}" height="12" horizontalAlignment="stretch"
                  backgroundImage="seek_track.png" foregroundImage="seek_fill.png" thumbImage="seek_thumb.png" thumbHoverImage="seek_thumb_hover.png"
                  tiled="true" slide="false" borderSize="0" direction="horizontal"/>

              <PLAYBUTTON left="108" top="{{BottomPiece - 54}}" image="play.png" hoverImage="play_hover.png" downImage="play_down.png" disabledImage="play_disabled.png" upToolTip="Play"/>
              <PAUSEBUTTON left="108" top="{{BottomPiece - 54}}" visible="wmpenabled:player.controls.pause" image="pause.png" hoverImage="pause_hover.png" downImage="pause_down.png" disabledImage="pause_disabled.png" upToolTip="Pause"/>
              <STOPBUTTON left="160" top="{{BottomPiece - 46}}" image="stop.png" hoverImage="stop_hover.png" downImage="stop_down.png" disabledImage="stop_disabled.png" upToolTip="Stop"/>
              <PREVBUTTON left="200" top="{{BottomPiece - 46}}" image="prev.png" hoverImage="prev_hover.png" downImage="prev_down.png" disabledImage="prev_disabled.png" upToolTip="Previous"/>
              <NEXTBUTTON left="228" top="{{BottomPiece - 46}}" image="next.png" hoverImage="next_hover.png" downImage="next_down.png" disabledImage="next_disabled.png" upToolTip="Next"/>
              <MUTEBUTTON id="bMute" left="268" top="{{BottomPiece - 46}}" image="mute.png" hoverImage="mute_hover.png" downImage="muted.png" upToolTip="Mute" downToolTip="Sound"/>
              <VOLUMESLIDER left="300" top="{{BottomPiece - 42}}" width="90" height="22" backgroundImage="volume_track.png" foregroundImage="volume_fill.png"
                  thumbImage="volume_thumb.png" thumbHoverImage="volume_thumb_hover.png" borderSize="4" direction="horizontal"/>
              <SHUFFLEBUTTON left="400" top="{{BottomPiece - 43}}" image="shuffle.png" hoverImage="shuffle_hover.png" downImage="shuffle_down.png" upToolTip="Turn Shuffle On" downToolTip="Turn Shuffle Off"/>
              <REPEATBUTTON left="428" top="{{BottomPiece - 43}}" image="repeat.png" hoverImage="repeat_hover.png" downImage="repeat_down.png" upToolTip="Turn Repeat On" downToolTip="Turn Repeat Off"/>
            </SUBVIEW>

            <!-- Taskbar rail items (drawn over the rail). -->
            <SUBVIEW id="svRailItems" left="0" top="{{Top}}" width="{{Rail}}" height="270">
        {{Rail_Item("railNow", "now", "Now&#10;Playing", 0)}}
        {{Rail_Item("railLibrary", "library", "Media&#10;Library", 1)}}
        {{Rail_Item("railRip", "cdrip", "Copy from&#10;CD", 2)}}
        {{Rail_Item("railBurn", "burn", "Copy to CD&#10;or Device", 3)}}
        {{Rail_Item("railRadio", "radio", "Radio&#10;Tuner", 4)}}
        {{Rail_Item("railSkins", "skins", "Settings", 5)}}
            </SUBVIEW>

            <!-- Content area: one pane at a time. -->
            <SUBVIEW id="svContent" left="{{Rail}}" top="{{Top}}" width="{{ContentW}}" height="{{ContentH}}" backgroundColor="#212741"
                horizontalAlignment="stretch" verticalAlignment="stretch">

              <SUBVIEW id="paneNow" left="0" top="0" width="{{ContentW}}" height="{{ContentH}}" horizontalAlignment="stretch" verticalAlignment="stretch">
                <SUBVIEW id="svVideo" left="0" top="0" width="{{ContentW - 200}}" height="{{ContentH - 22}}" backgroundColor="#000000" horizontalAlignment="stretch" verticalAlignment="stretch">
                  <WMPVIDEO id="vid" left="0" top="0" width="{{ContentW - 200}}" height="{{ContentH - 22}}" visible="false" backgroundColor="#000000"/>
                  <WMPEFFECTS id="vis" left="0" top="0" width="{{ContentW - 200}}" height="{{ContentH - 22}}" visible="true"/>
                </SUBVIEW>
                <SUBVIEW id="svList" left="{{ContentW - 200}}" top="0" width="200" height="{{ContentH - 22}}" backgroundColor="#394472" horizontalAlignment="right" verticalAlignment="stretch">
                  <SUBVIEW left="0" top="0" width="200" height="22" backgroundColor="#6071BF" horizontalAlignment="stretch"/>
                  <TEXT left="8" top="4" width="186" height="16" foregroundColor="#FFFFFF" fontType="Tahoma" fontSize="8" fontStyle="bold" horizontalAlignment="stretch"
                      value="wmpprop:player.currentMedia ? player.currentMedia.name : 'Now Playing'"/>
                  <PLAYLIST id="ipl" left="2" top="26" width="196" height="{{ContentH - 22 - 30}}" backgroundColor="#394472" foregroundColor="#FFFFFF" horizontalAlignment="stretch" verticalAlignment="stretch"/>
                </SUBVIEW>
                <SUBVIEW id="svBar" left="0" top="{{ContentH - 22}}" width="{{ContentW}}" height="22" backgroundColor="#282F4A" horizontalAlignment="stretch" verticalAlignment="bottom">
                  <BUTTON left="6" top="4" image="viz_prev.png" hoverImage="viz_prev_hover.png" upToolTip="Previous Visualization" onClick="vis.previous();"/>
                  <BUTTON left="22" top="4" image="viz_next.png" hoverImage="viz_next_hover.png" upToolTip="Next Visualization" onClick="vis.next();"/>
                  <TEXT left="42" top="4" width="260" value="wmpprop:vid.visible ? '' : vis.currentEffectTitle" fontType="Tahoma" fontSize="8" foregroundColor="#FFFFFF"/>
                  <TEXT left="{{ContentW - 200}}" top="4" width="194" justification="right" horizontalAlignment="right" fontType="Tahoma" fontSize="8" foregroundColor="#FFFFFF"
                      value="wmpprop:player.currentMedia ? 'Total Time: ' + player.currentMedia.durationString : ''"/>
                </SUBVIEW>
              </SUBVIEW>

              <SUBVIEW id="paneLibrary" left="0" top="0" width="{{ContentW}}" height="{{ContentH}}" visible="false" horizontalAlignment="stretch" verticalAlignment="stretch">
                <LIBRARY id="library" left="0" top="0" width="{{ContentW}}" height="{{ContentH}}" horizontalAlignment="stretch" verticalAlignment="stretch"/>
              </SUBVIEW>

              <!-- Settings: skins (and Pardeh's built-in full modes) above, all settings below. -->
              <SUBVIEW id="paneSkins" left="0" top="0" width="{{ContentW}}" height="{{ContentH}}" visible="false" horizontalAlignment="stretch" verticalAlignment="stretch">
                <LISTPANE kind="settings" left="0" top="0" width="{{ContentW}}" height="{{ContentH}}" horizontalAlignment="stretch" verticalAlignment="stretch"/>
              </SUBVIEW>

              <SUBVIEW id="paneSoon" left="0" top="0" width="{{ContentW}}" height="{{ContentH}}" visible="false" horizontalAlignment="stretch" verticalAlignment="stretch">
                <TEXT id="soonTitle" left="20" top="18" width="400" value="" fontType="Tahoma" fontSize="14" fontStyle="bold" foregroundColor="#FFFFFF"/>
                <TEXT id="soonText" left="20" top="48" width="520" value="" fontType="Tahoma" fontSize="8" foregroundColor="#C9D3F0"/>
              </SUBVIEW>
            </SUBVIEW>
          </VIEW>
        </THEME>
        """;

    private const string Script = """
        // Pardeh full mode — original script.
        var g_pane = "";
        var RAIL = { now: "railNow", library: "railLibrary", cdrip: "railRip", burn: "railBurn", radio: "railRadio", skins: "railSkins" };
        var SOON = { cdrip: "Copy from CD", burn: "Copy to CD or Device", radio: "Radio Tuner" };

        function OnLoad() {
            var pane = theme.loadPreference("Pane");
            ApplyPages();
            ShowPane(pane && RAIL[pane] && !IsHidden(pane) ? pane : "now");
            OnOpenState(player.openState);
        }

        function OnClose() {
            theme.savePreference("Pane", g_pane);
        }

        // The playlist panel's width (the host's drag handle on its left edge): the picture takes what is left. The panel
        // keeps its right edge by itself, being right-aligned.
        function SetListWidth(width) {
            var change = width - svList.width;
            if (change == 0) return;
            svList.width = width;
            svVideo.width = svVideo.width - change;
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
            paneSkins.visible = name == "skins";
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
            if (video && g_pane != "now") ShowPane("now");
        }
        """;
}
