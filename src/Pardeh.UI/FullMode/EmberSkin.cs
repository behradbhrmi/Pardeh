using System.Text;
using Avalonia;
using Avalonia.Media;
using Pardeh.Skinning.Model;
using Pardeh.Skinning.Wmp;
using static Pardeh.UI.FullMode.LunaArt;

namespace Pardeh.UI.FullMode;

/// <summary>
/// "Ember": a dark modern full mode whose design is adapted from the VLC Redesign by Juxtopposed
/// (https://www.juxtopposed.com/, Figma community file "VLC Redesign", licensed CC BY 4.0), as implemented in the
/// "VLC Juxtopposed" beta. Adapted: the near-black surfaces, the slim icon rail, the floating side panel, the rounded
/// stage, and the bottom bar (orange play button, dark seek track with an orange thumb, green-to-yellow volume wedge,
/// icon buttons). Pardeh's own drawing and logo; no VLC code, artwork or trademark (the cone) is used.
/// </summary>
internal static class EmberSkin
{
    public const string SourcePath = "pardeh://full-mode/ember";

    private const int W = 860, H = 540;
    private const int Top = 40, Rail = 50, Bar = 62, Gap = 14;
    private const int StageX = Rail + 6, StageW = W - StageX - Gap, StageH = H - Top - Bar;
    private const int SideW = 300;
    private const int RightW = 300; // bottom bar's right piece (volume and icons)

    private static readonly Color Surface = Color.Parse("#141414");
    private static readonly Color Panel = Color.Parse("#181818");
    private static readonly Color Line = Color.Parse("#2A2A2A");
    private static readonly Color Text = Color.Parse("#D6D6D6");
    private static readonly Color Dim = Color.Parse("#8C8C8C");
    private static readonly Color Accent = Color.Parse("#E5801F");
    private static readonly Color AccentDark = Color.Parse("#3A2108");

    public static SkinPackage Build()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        AddArt(files);
        files["ember.wms"] = Encoding.UTF8.GetBytes(Layout);
        files["ember.js"] = Encoding.UTF8.GetBytes(Script);
        return WmzLoader.FromFiles(files, SourcePath);
    }

    private static IBrush Solid(Color c) => new SolidColorBrush(c);

    private static IPen Stroke(Color c, double width = 1.6) =>
        new Pen(Solid(c), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);

    // ---- Art ------------------------------------------------------------------------------------

    private enum State { Normal, Hover, Down }

    private static readonly (State State, string Suffix)[] States = [(State.Normal, ""), (State.Hover, "_hover"), (State.Down, "_down")];

    /// <summary>Line icons (20×20 boxes), drawn like the redesign's thin outline glyphs.</summary>
    private static readonly Dictionary<string, string> Icons = new()
    {
        ["now"] = "M 4,4 L 16,4 L 16,16 L 4,16 Z M 8.5,7.5 L 12.5,10 L 8.5,12.5 Z",
        ["home"] = "M 3.5,9.5 L 10,4 L 16.5,9.5 M 5.5,8 L 5.5,16 L 14.5,16 L 14.5,8 M 8.5,16 L 8.5,12 L 11.5,12 L 11.5,16",
        ["video"] = "M 4,5 L 16,5 L 16,15 L 4,15 Z M 8.5,7.5 L 12.5,10 L 8.5,12.5 Z",
        ["music"] = "M 4,4.5 L 16,4.5 L 16,15.5 L 4,15.5 Z M 11,7 L 11,12.5 M 11,7 L 13,6.5 M 11,12.5 m -1.6,0 a 1.6,1.3 0 1,0 3.2,0 a 1.6,1.3 0 1,0 -3.2,0",
        ["playlists"] = "M 3.5,6 L 13,6 M 3.5,10 L 13,10 M 3.5,14 L 9,14 M 12,12 L 16.5,14.5 L 12,17 Z",
        ["history"] = "M 4.5,10 a 5.5,5.5 0 1,0 1.6,-3.9 M 4,4.5 L 4,7.5 L 7,7.5 M 10,7 L 10,10.5 L 12.5,12",
        ["library"] = "M 4,5 L 9,5 L 10.5,7 L 16,7 L 16,15 L 4,15 Z M 11.5,9 L 11.5,13 M 11.5,9 L 13.5,8.5",
        ["radio"] = "M 10,10 m -1.2,0 a 1.2,1.2 0 1,0 2.4,0 a 1.2,1.2 0 1,0 -2.4,0 M 6.5,6.5 Q 3.5,10 6.5,13.5 M 13.5,6.5 Q 16.5,10 13.5,13.5 M 4.2,4.2 Q -0.5,10 4.2,15.8 M 15.8,4.2 Q 20.5,10 15.8,15.8",
        ["burn"] = "M 4,6 L 13,6 M 4,10 L 11,10 M 4,14 L 9,14 M 15,8 L 15,15 M 12.5,12.5 L 15,15 L 17.5,12.5",
        ["info"] = "M 10,10 m -6.5,0 a 6.5,6.5 0 1,0 13,0 a 6.5,6.5 0 1,0 -13,0 M 10,9 L 10,14 M 10,6.3 L 10,6.5",
        ["skins"] = "M 10,10 m -2.4,0 a 2.4,2.4 0 1,0 4.8,0 a 2.4,2.4 0 1,0 -4.8,0 M 10,3 L 10,5 M 10,15 L 10,17 M 3,10 L 5,10 M 15,10 L 17,10 M 5,5 L 6.4,6.4 M 13.6,13.6 L 15,15 M 15,5 L 13.6,6.4 M 6.4,13.6 L 5,15",
        ["cdrip"] = "M 10,10 m -6.5,0 a 6.5,6.5 0 1,0 13,0 a 6.5,6.5 0 1,0 -13,0 M 10,10 m -1.6,0 a 1.6,1.6 0 1,0 3.2,0 a 1.6,1.6 0 1,0 -3.2,0",
        ["more"] = "M 5,10 L 5.2,10 M 10,10 L 10.2,10 M 15,10 L 15.2,10",
        ["side"] = "M 3.5,4.5 L 16.5,4.5 L 16.5,15.5 L 3.5,15.5 Z M 7.5,4.5 L 7.5,15.5",
        ["folder"] = "M 3.5,5.5 L 8,5.5 L 9.5,7.5 L 16.5,7.5 L 16.5,15 L 3.5,15 Z",
        ["min"] = "M 5,10 L 15,10",
        ["max"] = "M 5.5,5.5 L 14.5,5.5 L 14.5,14.5 L 5.5,14.5 Z",
        ["full"] = "M 4,8 L 4,4 L 8,4 M 12,4 L 16,4 L 16,8 M 16,12 L 16,16 L 12,16 M 8,16 L 4,16 L 4,12",
        ["close"] = "M 5.5,5.5 L 14.5,14.5 M 14.5,5.5 L 5.5,14.5",
        ["prev"] = "M 6,5 L 6,15 M 15,5 L 8,10 L 15,15 Z",
        ["next"] = "M 14,5 L 14,15 M 5,5 L 12,10 L 5,15 Z",
        ["mute"] = "M 3.5,8 L 6.5,8 L 10,5 L 10,15 L 6.5,12 L 3.5,12 Z M 13,8 Q 14.5,10 13,12 M 15,6.5 Q 17.5,10 15,13.5",
        ["muted"] = "M 3.5,8 L 6.5,8 L 10,5 L 10,15 L 6.5,12 L 3.5,12 Z M 13,8 L 17,12 M 17,8 L 13,12",
        ["viz"] = "M 4,9 L 4,11 M 7,6 L 7,14 M 10,4 L 10,16 M 13,7 L 13,13 M 16,9 L 16,11",
        ["captions"] = "M 3.5,5.5 L 16.5,5.5 L 16.5,14.5 L 3.5,14.5 Z M 6,10.5 L 9,10.5 M 11,10.5 L 14,10.5 M 6,12.5 L 12,12.5",
        ["queue"] = "M 4,6 L 16,6 M 4,10 L 16,10 M 4,14 L 11,14",
        ["shuffle"] = "M 3.5,6 L 6.5,6 L 13,14 L 16.5,14 M 3.5,14 L 6.5,14 L 13,6 L 16.5,6 M 14.5,4 L 16.5,6 L 14.5,8 M 14.5,12 L 16.5,14 L 14.5,16",
        ["repeat"] = "M 4,11 L 4,7 L 15,7 M 16,9 L 16,13 L 5,13 M 13,5 L 15,7 L 13,9 M 7,11 L 5,13 L 7,15",
        ["add"] = "M 10,4.5 L 10,15.5 M 4.5,10 L 15.5,10",
    };

    private static void IconButton(DrawingContext g, int size, string icon, State state, Color? ink = null)
    {
        if (state != State.Normal)
            g.DrawRectangle(Solid(state == State.Down ? Color.Parse("#2C2C2C") : Color.Parse("#262626")), null, new RoundedRect(new Rect(0, 0, size, size), 6));
        var offset = (size - 20) / 2.0;
        using (g.PushTransform(Matrix.CreateTranslation(offset, offset)))
            g.DrawGeometry(null, Stroke(ink ?? (state == State.Normal ? Dim : Text)), Geo(Icons[icon]));
    }

    private static void AddArt(Dictionary<string, byte[]> files)
    {
        foreach (var (state, suffix) in States)
        {
            foreach (var icon in Icons.Keys)
                files[$"i_{icon}{suffix}.png"] = Png(32, 32, g => IconButton(g, 32, icon, state));

            // The orange play / pause button.
            var face = state switch { State.Hover => Color.Parse("#F08F33"), State.Down => Color.Parse("#C96D16"), _ => Accent };
            files[$"play{suffix}.png"] = Png(54, 28, g =>
            {
                g.DrawRectangle(Solid(face), null, new RoundedRect(new Rect(0, 0, 54, 28), 6));
                g.DrawGeometry(Solid(Color.Parse("#1A1206")), null, Geo("M 23,8 L 32,14 L 23,20 Z"));
            });
            files[$"pause{suffix}.png"] = Png(54, 28, g =>
            {
                g.DrawRectangle(Solid(face), null, new RoundedRect(new Rect(0, 0, 54, 28), 6));
                g.FillRectangle(Solid(Color.Parse("#1A1206")), new Rect(22, 8, 3, 12));
                g.FillRectangle(Solid(Color.Parse("#1A1206")), new Rect(29, 8, 3, 12));
            });
        }

        // The rail's selected icon: orange.
        foreach (var icon in new[] { "home", "video", "music", "radio", "playlists", "history" })
            files[$"i_{icon}_active.png"] = Png(32, 32, g => IconButton(g, 32, icon, State.Normal, Accent));

        // Chrome.
        files["frame.png"] = Png(8, 8, g => g.FillRectangle(Solid(Surface), new Rect(0, 0, 8, 8)));
        files["stage_corner_tl.png"] = Png(10, 10, g => Corner(g, 0));
        files["stage_corner_tr.png"] = Png(10, 10, g => Corner(g, 1));
        files["stage_corner_bl.png"] = Png(10, 10, g => Corner(g, 2));
        files["stage_corner_br.png"] = Png(10, 10, g => Corner(g, 3));
        files["chip.png"] = Png(150, 28, g => g.DrawRectangle(Solid(Color.Parse("#1F1F1F")), null, new RoundedRect(new Rect(0, 0, 150, 28), 6)));
        files["note.png"] = Png(72, 64, g =>
        {
            var p = Stroke(Accent, 4);
            g.DrawGeometry(null, p, Geo("M 4,6 L 26,6 L 32,14 L 68,14 L 68,60 L 4,60 Z"));
            g.DrawGeometry(null, Stroke(Accent, 4.5), Geo("M 42,24 L 42,46 M 42,24 L 54,21"));
            g.DrawEllipse(Solid(Accent), null, new Point(36, 47), 7, 5.5);
        });

        // Seek: dark track, dark-orange fill, orange thumb.
        // Tiled pieces: flat (no ends), so the track reads as one long bar.
        files["seek_track.png"] = Png(2, 26, g =>
        {
            g.FillRectangle(Solid(Color.Parse("#0C0C0C")), new Rect(0, 0, 2, 26));
            g.FillRectangle(Solid(Color.Parse("#242424")), new Rect(0, 0, 2, 1));
            g.FillRectangle(Solid(Color.Parse("#242424")), new Rect(0, 25, 2, 1));
        });
        files["seek_fill.png"] = Png(2, 26, g => g.FillRectangle(Solid(AccentDark), new Rect(0, 1, 2, 24)));
        files["seek_thumb.png"] = Png(8, 22, g => g.DrawRectangle(Solid(Accent), null, new RoundedRect(new Rect(0, 0, 8, 22), 3)));
        files["seek_thumb_hover.png"] = Png(8, 22, g => g.DrawRectangle(Solid(Color.Parse("#F59A45")), null, new RoundedRect(new Rect(0, 0, 8, 22), 3)));

        // Volume: a wedge that fills green to yellow.
        const string wedge = "M 2,14 L 86,3 L 86,19 L 2,19 Z";
        files["vol_track.png"] = Png(88, 22, g => g.DrawGeometry(Solid(Color.Parse("#0E0E0E")), null, Geo(wedge)));
        files["vol_fill.png"] = Png(88, 22, g => g.DrawGeometry(
            Horizontal((Color.Parse("#5C8F14"), 0), (Color.Parse("#B9C81C"), 0.6), (Color.Parse("#E8B61A"), 1)), null, Geo(wedge)));
        files["vol_thumb.png"] = Png(4, 22, g => g.FillRectangle(Solid(Color.FromArgb(1, 0, 0, 0)), new Rect(0, 0, 4, 22)));
        files["vol_thumb_hover.png"] = Png(4, 22, g => g.FillRectangle(Solid(Color.FromArgb(120, 255, 255, 255)), new Rect(1, 3, 2, 17)));

        files["logo.png"] = Png(24, 24, g => PardehLogo.Draw(g, new Rect(1, 1, 22, 22)));
        files["logo_big.png"] = Png(40, 40, g => PardehLogo.Draw(g, new Rect(2, 2, 36, 36)));

        // The idle stage's orange "Open file" button.
        foreach (var (state, suffix) in States)
        {
            var face = state switch { State.Hover => Color.Parse("#F08F33"), State.Down => Color.Parse("#C96D16"), _ => Accent };
            files[$"open_file{suffix}.png"] = Png(104, 34, g =>
            {
                g.DrawRectangle(Solid(face), null, new RoundedRect(new Rect(0, 0, 104, 34), 5));
                using (g.PushTransform(Matrix.CreateTranslation(10, 7)))
                    g.DrawGeometry(null, Stroke(Color.Parse("#1A1206"), 1.4), Geo(Icons["folder"]));
                var label = new FormattedText("Open file", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), 13, Solid(Color.Parse("#1A1206")));
                g.DrawText(label, new Point(36, (34 - label.Height) / 2));
            });
        }
    }

    /// <summary>A stage corner: surface colour outside a 10-pixel radius, transparent inside.</summary>
    private static void Corner(DrawingContext g, int which)
    {
        var x = which is 1 or 3 ? 0 : 10;
        var y = which is 2 or 3 ? 0 : 10;
        var outside = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, 10, 10)),
            new EllipseGeometry(new Rect(x - 10, y - 10, 20, 20)));
        g.DrawGeometry(Solid(Surface), null, outside);
    }

    // ---- Layout & script ------------------------------------------------------------------------

    private static string Btn(string id, int left, int top, string icon, string tip, string onClick, string extra = "") =>
        $"""<BUTTON id="{id}" left="{left}" top="{top}" image="i_{icon}.png" hoverImage="i_{icon}_hover.png" downImage="i_{icon}_down.png" upToolTip="{tip}" onClick="{onClick}" {extra}/>""";

    private static string RailItem(string id, string pane, string icon, string tip, int index) =>
        Btn(id, 9, 10 + index * 44, icon, tip, $"Side('{pane}');") + "\n" +
        $"""<TEXT id="{id}Label" left="0" top="{10 + index * 44}" width="1" height="1" value=""/>""";

    private static string Pane(string id, string inner, string extra = "") =>
        $"""<SUBVIEW id="{id}" left="0" top="0" width="{StageW}" height="{StageH}" visible="false" {extra} horizontalAlignment="stretch" verticalAlignment="stretch">{inner}</SUBVIEW>""";

    private const string F = "Inter, Segoe UI";

    private static string Layout => $$"""
        <THEME id="PardehEmber" title="Pardeh Ember" author="Pardeh contributors; design adapted from the VLC Redesign by Juxtopposed (CC BY 4.0)" pardehMode="full">
          <VIEW id="vFull" width="{{W}}" height="{{H}}" minWidth="640" minHeight="400" resizable="true" titleBar="false" backgroundColor="#141414"
                scriptFile="ember.js" onLoad="OnLoad();" onClose="OnClose();">

            <PLAYER openstatechange="OnOpenState(NewState);"/>

            <!-- Top bar. -->
            <SUBVIEW id="svTop" left="0" top="0" width="{{W}}" height="{{Top}}" backgroundColor="#141414" horizontalAlignment="stretch">
              <SUBVIEW left="17" top="8" width="24" height="24" backgroundImage="logo.png"/>
              {{Btn("bMore", 52, 4, "more", "Menu", "pardeh.command('menu', 'app');")}}
              {{Btn("bSide", 90, 4, "side", "Show or hide the side panel", "ToggleSide();")}}
              <SUBVIEW left="128" top="10" width="1" height="20" backgroundColor="#2A2A2A"/>
              <SUBVIEW left="138" top="6" width="150" height="28" backgroundImage="chip.png">
                <SUBVIEW left="0" top="-2" width="32" height="32" backgroundImage="i_folder.png"/>
                <TEXT left="30" top="6" width="114" fontType="{{F}}" fontSize="9" foregroundColor="#C8C8C8" value="wmpprop:player.currentPlaylist.name ? player.currentPlaylist.name : 'Now Playing'"
                    onClick="ToggleSide();"/>
              </SUBVIEW>
              <TEXT left="300" top="12" width="{{W - 600}}" justification="center" horizontalAlignment="stretch" fontType="{{F}}" fontSize="9" foregroundColor="#B4B4B4"
                  value="wmpprop:player.currentMedia ? player.currentMedia.name : 'Pardeh'"/>
              <SUBVIEW left="{{W - 150}}" top="4" width="150" height="32" horizontalAlignment="right">
                {{Btn("bMin", 2, 0, "min", "Minimize", "view.minimize();")}}
                {{Btn("bMax", 38, 0, "max", "Maximize", "pardeh.command('maximize');")}}
                {{Btn("bFull", 74, 0, "full", "Full Screen", "pardeh.command('fullScreen');")}}
                {{Btn("bClose", 110, 0, "close", "Close", "view.close();")}}
              </SUBVIEW>
            </SUBVIEW>

            <!-- Icon rail. -->
            <SUBVIEW id="svRail" left="0" top="{{Top}}" width="{{Rail}}" height="{{StageH}}" backgroundColor="#141414" verticalAlignment="stretch">
              {{RailItem("railHome", "home", "home", "Home", 0)}}
              {{RailItem("railVideos", "videos", "video", "Videos", 1)}}
              {{RailItem("railMusic", "music", "music", "Music", 2)}}
              {{RailItem("railNetwork", "network", "radio", "Network streams", 3)}}
              {{RailItem("railPlaylists", "playlists", "playlists", "Playlists", 4)}}
              {{RailItem("railRecent", "recent", "history", "Recent files", 5)}}
              {{Btn("bAdd", 9, 10 + 6 * 44, "add", "Open files", "pardeh.command('openFile');")}}
            </SUBVIEW>

            <!-- Stage (rounded) with the pages. -->
            <SUBVIEW id="svStage" left="{{StageX}}" top="{{Top}}" width="{{StageW}}" height="{{StageH}}" backgroundColor="#000000" horizontalAlignment="stretch" verticalAlignment="stretch">
              <SUBVIEW id="paneNow" left="0" top="0" width="{{StageW}}" height="{{StageH}}" horizontalAlignment="stretch" verticalAlignment="stretch">
                <WMPVIDEO id="vid" left="0" top="0" width="{{StageW}}" height="{{StageH}}" visible="false" backgroundColor="#000000" horizontalAlignment="stretch" verticalAlignment="stretch"/>
                <WMPEFFECTS id="vis" left="0" top="0" width="{{StageW}}" height="{{StageH}}" visible="false" horizontalAlignment="stretch" verticalAlignment="stretch"/>
                <SUBVIEW id="svIdle" left="0" top="0" width="{{StageW}}" height="{{StageH}}" horizontalAlignment="stretch" verticalAlignment="stretch">
                  <!-- Something open: the music glyph and its name. -->
                  <SUBVIEW left="0" top="0" width="{{StageW}}" height="{{StageH}}" visible="wmpprop:player.currentMedia ? true : false" horizontalAlignment="stretch" verticalAlignment="stretch">
                    <SUBVIEW id="svNote" left="{{StageW / 2 - 36}}" top="{{StageH / 2 - 56}}" width="72" height="64" backgroundImage="note.png" horizontalAlignment="center" verticalAlignment="center"/>
                    <TEXT left="20" top="{{StageH / 2 + 18}}" width="{{StageW - 40}}" justification="center" horizontalAlignment="stretch" verticalAlignment="center"
                        fontType="{{F}}" fontSize="13" foregroundColor="#E6E6E6" value="wmpprop:player.currentMedia ? player.currentMedia.name : ''"/>
                  </SUBVIEW>
                  <!-- Nothing open: drop a file, or Open file. -->
                  <SUBVIEW left="0" top="0" width="{{StageW}}" height="{{StageH}}" visible="wmpprop:player.currentMedia ? false : true" horizontalAlignment="stretch" verticalAlignment="stretch">
                    <SUBVIEW left="{{StageW / 2 - 20}}" top="{{StageH / 2 - 58}}" width="40" height="40" backgroundImage="logo_big.png" horizontalAlignment="center" verticalAlignment="center"/>
                    <TEXT left="20" top="{{StageH / 2 - 8}}" width="{{StageW - 40}}" justification="center" horizontalAlignment="stretch" verticalAlignment="center"
                        fontType="{{F}}" fontSize="12" foregroundColor="#D6D6D6" value="Drop a file to start playing"/>
                    <BUTTON left="{{StageW / 2 - 52}}" top="{{StageH / 2 + 20}}" image="open_file.png" hoverImage="open_file_hover.png" downImage="open_file_down.png"
                        horizontalAlignment="center" verticalAlignment="center" upToolTip="Open file" onClick="pardeh.command('openFile');"/>
                  </SUBVIEW>
                </SUBVIEW>

                <!-- Floating queue panel. -->
                <SUBVIEW id="svSide" left="0" top="0" width="{{SideW}}" height="{{StageH}}" visible="false" backgroundColor="#141414" verticalAlignment="stretch">
                  <TEXT left="22" top="18" width="{{SideW - 44}}" fontType="{{F}}" fontSize="11" foregroundColor="#E6E6E6" value="Queue"/>
                  <TEXT left="22" top="44" width="{{SideW - 44}}" fontType="{{F}}" fontSize="9" foregroundColor="#8C8C8C"
                      value="wmpprop:player.currentPlaylist.count == 1 ? '1 item' : player.currentPlaylist.count + ' items'"/>
                  <SUBVIEW left="20" top="70" width="{{SideW - 40}}" height="1" backgroundColor="#2A2A2A"/>
                  <BUTTON left="14" top="80" image="i_add.png" hoverImage="i_add_hover.png" downImage="i_add_down.png" upToolTip="Add files" onClick="pardeh.command('openFile');"/>
                  <TEXT left="52" top="88" width="200" fontType="{{F}}" fontSize="10" foregroundColor="#B4B4B4" hoverForegroundColor="#FFFFFF" value="Add" onClick="pardeh.command('openFile');"/>
                  <PLAYLIST id="ipl" left="16" top="118" width="{{SideW - 32}}" height="{{StageH - 130}}" backgroundColor="#141414" foregroundColor="#D0D0D0" verticalAlignment="stretch"/>
                </SUBVIEW>
              </SUBVIEW>

              {{Pane("paneLibrary", $"""<LIBRARY id="library" left="0" top="0" width="{StageW}" height="{StageH}" horizontalAlignment="stretch" verticalAlignment="stretch"/>""")}}
              {{Pane("paneRadio", $"""<LISTPANE kind="radio" left="0" top="0" width="{StageW}" height="{StageH}" horizontalAlignment="stretch" verticalAlignment="stretch"/>""")}}
              {{Pane("paneBurn", $"""<LISTPANE kind="device" left="0" top="0" width="{StageW}" height="{StageH}" horizontalAlignment="stretch" verticalAlignment="stretch"/>""")}}
              {{Pane("paneSkins", $"""<LISTPANE kind="settings" left="0" top="0" width="{StageW}" height="{StageH}" horizontalAlignment="stretch" verticalAlignment="stretch"/>""")}}
              {{Pane("paneInfo", $"""
                <TEXT left="28" top="24" width="500" value="Info Center" fontType="{F}" fontSize="15" foregroundColor="#E6E6E6"/>
                <TEXT left="28" top="66" width="600" fontType="{F}" fontSize="12" foregroundColor="#FFFFFF" value="wmpprop:player.currentMedia ? player.currentMedia.name : 'Nothing is playing.'"/>
                <TEXT left="28" top="94" width="600" fontType="{F}" fontSize="9" foregroundColor="#B4B4B4" value="wmpprop:player.currentMedia &amp;&amp; player.currentMedia.getItemInfo('Author') ? 'Artist: ' + player.currentMedia.getItemInfo('Author') : ''"/>
                <TEXT left="28" top="114" width="600" fontType="{F}" fontSize="9" foregroundColor="#B4B4B4" value="wmpprop:player.currentMedia &amp;&amp; player.currentMedia.getItemInfo('Album') ? 'Album: ' + player.currentMedia.getItemInfo('Album') : ''"/>
                <TEXT left="28" top="134" width="600" fontType="{F}" fontSize="9" foregroundColor="#8C8C8C" value="wmpprop:player.currentMedia ? 'Length: ' + player.currentMedia.durationString : ''"/>
                <TEXT left="28" top="{StageH - 30}" width="700" verticalAlignment="bottom" fontType="{F}" fontSize="7" foregroundColor="#6E6E6E"
                    value="Ember's design is adapted from the VLC Redesign by Juxtopposed (juxtopposed.com), licensed CC BY 4.0."/>
                """, """backgroundColor="#101010" """)}}
              {{Pane("paneSoon", $"""
                <TEXT id="soonTitle" left="28" top="24" width="500" value="" fontType="{F}" fontSize="15" foregroundColor="#E6E6E6"/>
                <TEXT id="soonText" left="28" top="60" width="600" value="" fontType="{F}" fontSize="9" foregroundColor="#8C8C8C"/>
                """, """backgroundColor="#101010" """)}}

              <!-- Rounded corners. -->
              <SUBVIEW left="0" top="0" width="10" height="10" backgroundImage="stage_corner_tl.png"/>
              <SUBVIEW left="{{StageW - 10}}" top="0" width="10" height="10" backgroundImage="stage_corner_tr.png" horizontalAlignment="right"/>
              <SUBVIEW left="0" top="{{StageH - 10}}" width="10" height="10" backgroundImage="stage_corner_bl.png" verticalAlignment="bottom"/>
              <SUBVIEW left="{{StageW - 10}}" top="{{StageH - 10}}" width="10" height="10" backgroundImage="stage_corner_br.png" horizontalAlignment="right" verticalAlignment="bottom"/>
            </SUBVIEW>

            <!-- Bottom bar. -->
            <SUBVIEW id="svBar" left="0" top="{{H - Bar}}" width="{{W}}" height="{{Bar}}" backgroundColor="#141414" horizontalAlignment="stretch" verticalAlignment="bottom">
              <PREVBUTTON left="22" top="15" image="i_prev.png" hoverImage="i_prev_hover.png" downImage="i_prev_down.png" disabledImage="i_prev.png" upToolTip="Previous"/>
              <PLAYBUTTON left="60" top="17" image="play.png" hoverImage="play_hover.png" downImage="play_down.png" disabledImage="play.png" upToolTip="Play"/>
              <PAUSEBUTTON left="60" top="17" visible="wmpenabled:player.controls.pause" image="pause.png" hoverImage="pause_hover.png" downImage="pause_down.png" upToolTip="Pause"/>
              <NEXTBUTTON left="120" top="15" image="i_next.png" hoverImage="i_next_hover.png" downImage="i_next_down.png" disabledImage="i_next.png" upToolTip="Next"/>
              <TEXT left="166" top="23" width="66" fontType="{{F}}" fontSize="9" foregroundColor="#8C8C8C" value="wmpprop:player.controls.currentPositionString"/>
              <SEEKSLIDER id="seek" left="236" top="18" width="{{W - 236 - RightW - 74}}" height="26" horizontalAlignment="stretch"
                  backgroundImage="seek_track.png" foregroundImage="seek_fill.png" thumbImage="seek_thumb.png" thumbHoverImage="seek_thumb_hover.png"
                  tiled="true" slide="false" borderSize="1" direction="horizontal"/>
              <SUBVIEW left="{{W - RightW - 66}}" top="0" width="{{RightW + 66}}" height="{{Bar}}" horizontalAlignment="right">
                <TEXT left="4" top="23" width="60" fontType="{{F}}" fontSize="9" foregroundColor="#8C8C8C"
                    value="wmpprop:player.currentMedia ? player.currentMedia.durationString : '00:00'"/>
                <MUTEBUTTON id="bMute" left="66" top="15" image="i_mute.png" hoverImage="i_mute_hover.png" downImage="i_muted.png" upToolTip="Mute" downToolTip="Sound"/>
                <VOLUMESLIDER left="100" top="20" width="88" height="22" backgroundImage="vol_track.png" foregroundImage="vol_fill.png"
                    thumbImage="vol_thumb.png" thumbHoverImage="vol_thumb_hover.png" borderSize="2" direction="horizontal"/>
                {{Btn("bViz", 196, 15, "viz", "Audio", "pardeh.command('quickPanel', 'audio');")}}
                {{Btn("bCaptions", 232, 15, "captions", "Subtitles", "pardeh.command('quickPanel', 'subtitles');")}}
                {{Btn("bSettings", 268, 15, "skins", "Playback settings", "pardeh.command('quickPanel', 'playback');")}}
                {{Btn("bQueue", 304, 15, "queue", "Queue, bookmarks and chapters", "pardeh.command('queuePanel');")}}
                <BUTTON left="{{RightW + 50}}" top="{{Bar - 16}}" width="14" height="14" cursor="sizenwse" upToolTip="" onmousedown="view.size('bottomright');"/>
              </SUBVIEW>
            </SUBVIEW>
          </VIEW>
        </THEME>
        """;

    private const string Script = """
        // Pardeh Ember full mode — original script.
        var g_pane = "";
        var g_side = "";
        var g_viz = false;
        // The rail opens the library side panel (as in the VLC redesign): home, videos, music, network, playlists, recent.
        var RAIL = { home: "railHome", videos: "railVideos", music: "railMusic", network: "railNetwork", playlists: "railPlaylists", recent: "railRecent" };
        var ICON = { home: "home", videos: "video", music: "music", network: "radio", playlists: "playlists", recent: "history" };
        var HIDE = { videos: "library", music: "library", network: "radio" }; // Settings › Pages
        var PANES = { now: 1, library: 1, radio: 1, burn: 1, skins: 1, info: 1, cdrip: 1 };
        var SOON = { cdrip: "Copy from CD" };

        function OnLoad() {
            g_viz = theme.loadPreference("Viz") == "true";
            pardeh.command("emberViz", g_viz);
            var pane = theme.loadPreference("Pane");
            ApplyPages();
            ShowPane(pane && PANES[pane] && !PaneHidden(pane) ? pane : "now");
            OnOpenState(player.openState);
        }

        function OnClose() {
            theme.savePreference("Pane", g_pane);
            theme.savePreference("Viz", g_viz ? "true" : "false");
        }

        // Stage pages (Now Playing, and the full pages the menus and panels open: library, radio, settings …).
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
        }

        // A rail page: opens (or closes) the side panel on it; the rail lights the open page.
        function Side(page) {
            var open = pardeh.command("libraryPanel", page);
            g_side = open ? page : "";
            if (open && g_pane != "now") ShowPane("now");
            LightRail();
        }

        // The top bar's panel button: the side panel on its last page.
        function ToggleSide() {
            var open = pardeh.command("libraryPanel", "");
            g_side = open ? (g_side != "" ? g_side : "home") : "";
            LightRail();
        }

        // The host reports the side panel's width (0: closed): the stage moves aside for it, as in the redesign.
        function OnSidePanel(width) {
            var left = width > 0 ? 56 + width + 6 : 56;
            svStage.left = left;
            svStage.width = view.width - left - 14;
            if (width == 0 && g_side != "") {
                g_side = "";
                LightRail();
            }
        }

        function LightRail() {
            for (var key in RAIL) {
                var button = eval(RAIL[key]);
                var icon = "i_" + ICON[key] + (key == g_side ? "_active" : "");
                button.image = icon + ".png";
                button.hoverImage = (key == g_side ? icon : "i_" + ICON[key] + "_hover") + ".png";
            }
        }

        // The quick panel's Audio Visualizer switch (the host keeps the state for it).
        function SetViz(on) {
            g_viz = on;
            OnOpenState(osMediaOpen);
        }

        function OnOpenState(state) {
            var video = player.currentMedia && player.currentMedia.imageSourceWidth > 0;
            vid.visible = video;
            var playing = player.currentMedia ? true : false;
            vis.visible = !video && g_viz && playing;
            svIdle.visible = !video && (!g_viz || !playing); // nothing open: always the drop / Open file area
            if (state == osMediaOpen && video && g_pane != "now") ShowPane("now");
        }

        // Pages (Settings › Pages): hide those rail items and close the gaps.
        var g_railTops = null;

        function PaneHidden(name) {
            return ("," + pardeh.command("hiddenPages") + ",").indexOf("," + name + ",") >= 0;
        }

        function IsHidden(key) {
            return HIDE[key] != undefined && PaneHidden(HIDE[key]);
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
                    label.top = button.top;
                    slot++;
                }
            }
            bAdd.top = first + slot * step;
            if (g_pane != "" && PaneHidden(g_pane)) ShowPane("now");
        }
        """;
}
