using System.Globalization;
using Avalonia;
using Avalonia.Media;
using static Pardeh.UI.FullMode.LunaArt;

namespace Pardeh.UI.FullMode;

/// <summary>
/// "Luna Phone Plus" (key luna-plus-phone): the Luna palette closer to Luna's full mode. A glossy band, a dark info strip
/// (title, artist, time), the green Luna seek bar, a silver deck with the glossy play orb, stop, a joined previous / next
/// pill and the volume, and the real playlist on the Now Playing page as a collapsible sheet (tap its header to expand
/// it over the stage). The bottom navigation uses glossy pills like Luna's task rail. All art is drawn in code.
/// </summary>
internal static partial class PhoneSkin
{
    private const int DeckHeight = 124, SheetHeight = 32, StripHeight = 46;

    private static void AddPlusArt(Dictionary<string, byte[]> files, Palette p)
    {
        files["deck.png"] = Png(1, DeckHeight, g =>
        {
            g.FillRectangle(Vertical((C("#FBFCFE"), 0), (C("#E3EBF8"), 0.55), (C("#BFD2EA"), 1)), new Rect(0, 0, 1, DeckHeight));
            g.FillRectangle(Solid(C("#9DB2E4")), new Rect(0, 0, 1, 1));
            g.FillRectangle(Solid(WithAlpha(Colors.White, 200)), new Rect(0, 1, 1, 1));
        });
        files["sheet.png"] = Png(1, SheetHeight, g =>
        {
            g.FillRectangle(Vertical((C("#7787D0"), 0), (C("#5465AE"), 1)), new Rect(0, 0, 1, SheetHeight));
            g.FillRectangle(Solid(WithAlpha(Colors.White, 120)), new Rect(0, 1, 1, 1));
            g.FillRectangle(Solid(C("#2E375D")), new Rect(0, SheetHeight - 1, 1, 1));
        });
        foreach (var up in new[] { true, false })
        {
            files[up ? "chev_up.png" : "chev_down.png"] = Png(32, 32, g =>
            {
                var pen = new Pen(Solid(Colors.White), 2.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
                g.DrawLine(pen, new Point(9, up ? 20 : 12), new Point(16, up ? 12 : 20));
                g.DrawLine(pen, new Point(16, up ? 12 : 20), new Point(23, up ? 20 : 12));
            });
        }

        // Transport: stop orb, joined previous / next pill, small orbs for mute / shuffle / repeat.
        foreach (var (state, suffix) in States)
        {
            files[$"stop{suffix}.png"] = Png(40, 40, g => Orb(g, p, 40, state, false, c => g.FillRectangle(Solid(c), new Rect(14, 14, 12, 12))));
            files[$"skipb{suffix}.png"] = Png(40, 40, g => { HitBox(g, 40); Orb(g, p, 40, state, false, c => SkipGlyph(g, p, c, false, 20, 20, 24, 1.25)); });
            files[$"skipf{suffix}.png"] = Png(40, 40, g => { HitBox(g, 40); Orb(g, p, 40, state, false, c => SkipGlyph(g, p, c, true, 20, 20, 24, 1.25)); });
            files[$"prev{suffix}.png"] = Png(46, 40, g => PillHalf(g, p, state, true, c => FillIcon(g, c, PrevPath, 25, 20, 1.2)));
            files[$"next{suffix}.png"] = Png(46, 40, g => PillHalf(g, p, state, false, c => FillIcon(g, c, NextPath, 21, 20, 1.2)));
        }

        foreach (var (name, path) in new[] { ("shuffle", IconShuffle), ("repeat", IconRepeat), ("mute", IconMute), ("muted", IconMuted) })
        {
            var toggled = name == "muted";
            files[$"{name}.png"] = Png(36, 36, g => Orb(g, p, 36, State.Normal, toggled, c => StrokeIcon(g, c, path, 18, 18, 1.0, 1.7)));
            files[$"{name}_down.png"] = Png(36, 36, g => Orb(g, p, 36, State.Normal, true, c => StrokeIcon(g, c, path, 18, 18, 1.0, 1.9)));
        }

        // Seek bar: Luna's silver groove with the green fill and a green capsule thumb.
        files["seek_track.png"] = Png(6, 30, g =>
        {
            g.FillRectangle(Vertical((C("#AEB5CE"), 0), (C("#D9DEEC"), 1)), new Rect(0, 11, 6, 8));
            g.FillRectangle(Solid(C("#8E97B4")), new Rect(0, 11, 6, 1));
            g.FillRectangle(Solid(C("#8E97B4")), new Rect(0, 18, 6, 1));
        });
        files["seek_fill.png"] = Png(6, 30, g =>
        {
            g.FillRectangle(Vertical((C("#C8E9C6"), 0), (C("#94C692"), 0.5), (C("#6FAE6C"), 1)), new Rect(0, 11, 6, 8));
            g.FillRectangle(Solid(C("#5E9A5B")), new Rect(0, 11, 6, 1));
            g.FillRectangle(Solid(C("#5E9A5B")), new Rect(0, 18, 6, 1));
        });
        foreach (var name in new[] { "thumb.png", "thumb_hover.png" })
        {
            var hover = name.Contains("hover", StringComparison.Ordinal);
            files[name] = Png(20, 30, g =>
            {
                var r = new RoundedRect(new Rect(2.5, 2.5, 15, 25), 7.5);
                g.DrawRectangle(Vertical((C("#7CE07A"), 0), (C("#1AB919"), 0.5), (C("#0E7C10"), 1)), Pen(hover ? C("#06570A") : C("#0B6B0E"), 1.2), r);
                g.DrawRectangle(Vertical((WithAlpha(Colors.White, 190), 0), (WithAlpha(Colors.White, 0), 1)), null, new RoundedRect(new Rect(5, 4, 10, 10), 4));
            });
        }

        // Navigation: Luna task-rail style items, the label drawn into the image so the whole item is one touch target.
        var labels = new Dictionary<string, string> { ["now"] = "Now Playing", ["library"] = "Library", ["queue"] = "Playlist", ["radio"] = "Radio", ["settings"] = "Settings" };
        foreach (var (key, path) in NavIcons)
        {
            files[$"nav_{key}.png"] = Png(72, 50, g =>
            {
                g.FillRectangle(Solid(WithAlpha(Colors.Black, 2)), new Rect(0, 0, 72, 50)); // the whole slot is tappable
                NavGlyph(g, p, key, false, 36, 15);
                Label(g, labels[key], p.NavInk, 33);
            });
            files[$"nav_{key}_down.png"] = Png(72, 50, g =>
            {
                var rect = new RoundedRect(new Rect(3.5, 1.5, 65, 46), 14);
                g.DrawRectangle(Vertical((C("#FFFFFF"), 0), (C("#E8EEFA"), 0.5), (C("#CBD8F2"), 1)), Pen(C("#9FB4E4"), 1.2), rect);
                g.DrawRectangle(Vertical((WithAlpha(Colors.White, 230), 0), (WithAlpha(Colors.White, 0), 1)), null, new RoundedRect(new Rect(7, 3, 58, 20), 10));
                NavGlyph(g, p, key, true, 36, 15);
                Label(g, labels[key], p.NavSel, 33);
            });
        }
    }

    private static void Label(DrawingContext g, string text, Color color, double y)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Pardeh.UI.Rendering.UiTheme.Typeface(true), 10, Solid(color));
        using (g.PushTextOptions(new TextOptions { TextRenderingMode = TextRenderingMode.Antialias }))
            g.DrawText(ft, new Point((72 - ft.Width) / 2, y));
    }

    /// <summary>A small glossy orb (stop, mute, shuffle, repeat); <paramref name="on"/> gives it a green rim.</summary>
    private static void Orb(DrawingContext g, Palette p, double size, State state, bool on, Action<Color> glyph)
    {
        var c = new Point(size / 2, size / 2);
        var r = size / 2 - 1.5;
        var down = state == State.Down;
        var brush = state == State.Disabled ? Vertical((C("#F7F9FC"), 0), (C("#DCE2EE"), 1))
            : down ? Vertical((p.OrbBottom, 0), (p.OrbTop, 1)) : Vertical((p.OrbTop, 0), (p.OrbBottom, 1));
        g.DrawEllipse(brush, Pen(on ? C("#0E7C10") : p.OrbRim, on ? 2 : 1.4), c, r, r);
        g.DrawEllipse(Vertical((WithAlpha(Colors.White, 220), 0), (WithAlpha(Colors.White, 0), 1)), null, new Point(c.X, c.Y - r * 0.38), r * 0.7, r * 0.45);
        glyph(state == State.Disabled ? WithAlpha(p.Glyph, 90) : p.Glyph);
    }

    /// <summary>One half of the joined previous / next pill (rounded on the outer side, square where they meet).</summary>
    private static void PillHalf(DrawingContext g, Palette p, State state, bool left, Action<Color> glyph)
    {
        var shape = Geo(left ? "M 46,0.8 L 20.5,0.8 A 19.2,19.2 0 0 0 20.5,39.2 L 46,39.2 Z" : "M 0,0.8 L 25.5,0.8 A 19.2,19.2 0 0 1 25.5,39.2 L 0,39.2 Z");
        var brush = state == State.Disabled ? Vertical((C("#F7F9FC"), 0), (C("#DCE2EE"), 1))
            : state == State.Down ? Vertical((p.OrbBottom, 0), (p.OrbTop, 1)) : Vertical((p.OrbTop, 0), (p.OrbBottom, 1));
        g.DrawGeometry(brush, Pen(p.OrbRim, 1.4), shape);
        g.DrawRectangle(Vertical((WithAlpha(Colors.White, 220), 0), (WithAlpha(Colors.White, 0), 1)), null, new Rect(left ? 12 : 0, 2.5, 34, 15));
        glyph(state == State.Disabled ? WithAlpha(p.Glyph, 90) : p.Glyph);
    }

    private static string PlusNowPane(Palette p)
    {
        var f = p.Font;
        var h = H - Bar - Nav;
        return $$"""
              <SUBVIEW id="paneNow" left="0" top="0" width="{{W}}" height="{{h}}" backgroundColor="{{Hex(p.Back)}}">
                <SUBVIEW id="svStage" left="8" top="8" width="344" height="170" backgroundImage="stage_frame.png" backgroundColor="{{Hex(p.StageGlow)}}">
                  <WMPVIDEO id="vid" left="0" top="0" width="344" height="170" visible="false" backgroundColor="#000000" horizontalAlignment="stretch" verticalAlignment="stretch"/>
                  <WMPEFFECTS id="vis" left="0" top="0" width="344" height="170" visible="true" picker="true" horizontalAlignment="stretch" verticalAlignment="stretch"/>
                  <SUBVIEW id="cover" left="10" top="10" width="76" height="76" backgroundImage="cover_frame.png">
                    <SUBVIEW left="2" top="2" width="72" height="72" backgroundImage="WMPImage_AlbumArtLarge" coverScale="{{ArtScale}}"/>
                  </SUBVIEW>
                  {{LyricsElementXml("lyr", 344, 170, "#FFFFFF", "#7FFF00", "#FFFFFF", false, f, Hex(p.StageGlow))}}
                </SUBVIEW>

                <!-- Luna's info strip: title, artist, and the time in green. -->
                <SUBVIEW id="svStrip" left="8" top="186" width="344" height="{{StripHeight}}" backgroundColor="#0B0E1C">
                  <TEXT id="tTitle" left="10" top="5" width="230" height="20" fontType="{{f}}" fontSize="10" fontStyle="bold" foregroundColor="#FFFFFF"
                      value="wmpprop:player.currentMedia ? player.currentMedia.name : 'Nothing playing'"/>
                  <TEXT id="tArtist" left="10" top="25" width="230" height="16" fontType="{{f}}" fontSize="8" foregroundColor="#8F9CC9"
                      value="wmpprop:player.currentMedia ? player.currentMedia.getItemInfo('Author') : 'Ready'"/>
                  <TEXT id="tPos" left="244" top="5" width="90" height="20" justification="right" fontType="{{f}}" fontSize="11" fontStyle="bold" foregroundColor="#7FFF00"
                      value="wmpprop:player.currentMedia ? player.controls.currentPositionString : '00:00'"/>
                  <TEXT id="tDur" left="244" top="25" width="90" height="16" justification="right" fontType="{{f}}" fontSize="8" foregroundColor="#8F9CC9"
                      value="wmpprop:player.currentMedia ? player.currentMedia.durationString : ''"/>
                  <FAVBUTTON id="bFav" left="238" top="1" image="fav.png" hoverImage="fav.png" downImage="fav_down.png" disabledImage="fav_disabled.png" upToolTip="Add to favourites" downToolTip="Remove from favourites"/>
                  <BUTTON id="bLyr" left="194" top="1" image="lyr.png" hoverImage="lyr.png" downImage="lyr_down.png" onClick="ToggleLyrics();" upToolTip="Lyrics" downToolTip="Hide lyrics"/>
                </SUBVIEW>

                <SEEKSLIDER id="seek" left="8" top="236" width="344" height="30"
                    backgroundImage="seek_track.png" foregroundImage="seek_fill.png" thumbImage="thumb.png" thumbHoverImage="thumb_hover.png"
                    tiled="true" slide="false" borderSize="0" direction="horizontal"/>

                <SUBVIEW id="svDeck" left="0" top="268" width="{{W}}" height="{{DeckHeight}}" backgroundImage="deck.png" backgroundTiled="true">
                  <PLAYBUTTON id="bPlay" left="148" top="8" image="play.png" hoverImage="play.png" downImage="play_down.png" disabledImage="play_disabled.png" upToolTip="Play"/>
                  <PAUSEBUTTON id="bPause" left="148" top="8" visible="wmpenabled:player.controls.pause" image="pause.png" hoverImage="pause.png" downImage="pause_down.png" disabledImage="pause_disabled.png" upToolTip="Pause"/>
                  <STOPBUTTON id="bStop" left="222" top="20" image="stop.png" hoverImage="stop.png" downImage="stop_down.png" disabledImage="stop_disabled.png" upToolTip="Stop"/>
                  <PREVBUTTON id="bPrev" left="272" top="20" image="prev.png" hoverImage="prev.png" downImage="prev_down.png" disabledImage="prev_disabled.png" upToolTip="Previous"/>
                  <NEXTBUTTON id="bNext" left="318" top="20" image="next.png" hoverImage="next.png" downImage="next_down.png" disabledImage="next_disabled.png" upToolTip="Next"/>
                  <BUTTON id="bSkipB" left="28" top="20" image="skipb.png" hoverImage="skipb.png" downImage="skipb_down.png" upToolTip="Back 10 seconds" onClick="player.controls.seekBy(-10);"/>
                  <BUTTON id="bSkipF" left="292" top="20" image="skipf.png" hoverImage="skipf.png" downImage="skipf_down.png" upToolTip="Forward 10 seconds" onClick="player.controls.seekBy(10);"/>
                  <MUTEBUTTON id="bMute" left="12" top="80" image="mute.png" hoverImage="mute.png" downImage="muted_down.png" upToolTip="Mute" downToolTip="Sound"/>
                  <VOLUMESLIDER id="vol" left="56" top="83" width="200" height="30"
                      backgroundImage="seek_track.png" foregroundImage="seek_fill.png" thumbImage="thumb.png" thumbHoverImage="thumb_hover.png"
                      tiled="true" slide="false" borderSize="0" direction="horizontal"/>
                  <SHUFFLEBUTTON id="bShuffle" left="272" top="80" image="shuffle.png" hoverImage="shuffle.png" downImage="shuffle_down.png" upToolTip="Turn Shuffle On" downToolTip="Turn Shuffle Off"/>
                  <REPEATBUTTON id="bRepeat" left="312" top="80" image="repeat.png" hoverImage="repeat.png" downImage="repeat_down.png" upToolTip="Turn Repeat On" downToolTip="Turn Repeat Off"/>
                </SUBVIEW>

                <!-- The playlist sheet: tap the header to expand it over the stage. -->
                <SUBVIEW id="svSheet" left="0" top="396" width="{{W}}" height="{{SheetHeight}}" backgroundImage="sheet.png" backgroundTiled="true" onClick="ToggleSheet();">
                  <TEXT id="tSheet" left="12" top="8" width="260" height="18" fontType="{{f}}" fontSize="9" fontStyle="bold" foregroundColor="#FFFFFF"
                      value="wmpprop:'Playlist  -  ' + player.currentPlaylist.count + ' items'"/>
                  <SUBVIEW id="chevUp" left="320" top="0" width="32" height="32" backgroundImage="chev_up.png"/>
                  <SUBVIEW id="chevDown" left="320" top="0" width="32" height="32" backgroundImage="chev_down.png" visible="false"/>
                  <QUEUEFILTERBUTTON id="bQFilterS" left="246" top="0" image="sqfilter.png" hoverImage="sqfilter.png" downImage="sqfilter_down.png" upToolTip="Filter the playlist" downToolTip="Filter is on"/>
                  <QUEUESORTBUTTON id="bQSortS" left="284" top="0" image="sqsort.png" hoverImage="sqsort.png" downImage="sqsort_down.png" disabledImage="sqsort_disabled.png" upToolTip="Sort and playlist tools"/>
                </SUBVIEW>
                <PLAYLIST id="ipl" left="0" top="428" width="{{W}}" height="180" touch="true" backgroundColor="#2E375D" foregroundColor="#FFFFFF"
                    itemPlayingColor="#7FFF00" itemPlayingBackgroundColor="#1B2244" itemSelectedBackgroundColor="#5465AE" horizontalAlignment="stretch" verticalAlignment="stretch"/>
              </SUBVIEW>
        """;
    }

    private const string PlusNowScript = """

        var g_exp = false;

        function ToggleSheet() { g_exp = !g_exp; Relayout(); }

        function BackNow() {
            if (g_pane == "now" && g_exp) { g_exp = false; Relayout(); return true; }
            return false;
        }

        function LayoutNow(w, h, ch) {
            var land = w > h * 1.15;
            if (g_lyr && g_exp && !land) g_exp = false;   // the Lyrics button is in the info strip, which stays when the playlist is expanded: opening the lyrics collapses it
            var STRIP = 46, SEEK = 30, DECK = 124, HEAD = 32;
            var lw = land ? Math.round(w * 0.46) : w;      // left column
            var sw = lw - 16;
            var sh = 0, y = 0;

            // Stage and strip (left column in landscape).
            if (land) {
                sh = ch - 8 - 6 - STRIP - 8;
                if (sh < 40) sh = 40;
                svStage.visible = true;
                svStage.left = 8; svStage.top = 8; svStage.width = sw; svStage.height = sh;
                svStrip.left = 8; svStrip.top = 8 + sh + 6; svStrip.width = sw;
            } else {
                sh = Math.round(ch * 0.26);
                if (sh < 110) sh = 110;
                if (sh > 190) sh = 190;
                // The lyrics view takes the room of the collapsed playlist (the sheet header, controls and strip keep theirs).
                if (g_lyr && !g_exp) sh = Math.max(sh, ch - 264);
                svStage.visible = !g_exp;
                svStage.left = 8; svStage.top = 8; svStage.width = sw; svStage.height = sh;
                y = g_exp ? 6 : 8 + sh + 8;
                svStrip.left = 8; svStrip.top = y; svStrip.width = sw;
                y += STRIP + 4;
            }
            // The info strip, left to right: title and artist, Lyrics (44), the heart (44), the time. Nothing sits on the stage's artwork.
            tTitle.width = sw - 174; tArtist.width = sw - 174;
            bLyr.left = sw - 160; bLyr.top = 1;
            bFav.left = sw - 112; bFav.top = 1;
            tPos.width = 56; tDur.width = 56;
            tPos.left = sw - 64; tDur.left = sw - 64;

            // Seek, deck, sheet header, playlist (right column in landscape).
            var cx = 0, cw = w;
            if (land) { cx = lw; cw = w - lw; y = 4; }
            var showControls = !(land && g_exp);
            seek.visible = showControls; svDeck.visible = showControls;
            if (showControls) {
                seek.left = cx + 8; seek.top = y; seek.width = cw - 16;
                y += SEEK + 2;
                svDeck.left = cx; svDeck.top = y; svDeck.width = cw;
                y += DECK + 4;
                var x0 = Math.round((cw - 216) / 2);
                bPlay.left = x0; bPause.left = x0;
                bStop.left = x0 + 74; bPrev.left = x0 + 124; bNext.left = x0 + 170;
                bSkipB.left = Math.max(2, x0 - 44); bSkipF.left = Math.min(cw - 42, x0 + 220);
                bMute.left = 12;
                bShuffle.left = cw - 88; bRepeat.left = cw - 48;
                vol.left = 56; vol.width = cw - 152;
            }
            svSheet.left = cx; svSheet.top = y; svSheet.width = cw;
            tSheet.width = cw - 130;
            chevUp.left = cw - 36; chevDown.left = cw - 36;
            bQSortS.left = cw - 74; bQFilterS.left = cw - 108;
            chevUp.visible = !g_exp; chevDown.visible = g_exp;
            y += HEAD;
            // A landscape view leaves no room under the collapsed header: no list then (a sliver of its first row showed above the bar).
            var room = ch - y;
            ipl.visible = room >= 40;
            ipl.left = cx; ipl.top = y; ipl.width = cw; ipl.height = Math.max(44, room);
        }
        """;
}
