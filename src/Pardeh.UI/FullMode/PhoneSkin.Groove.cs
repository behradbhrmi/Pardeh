using Avalonia;
using Avalonia.Media;
using static Pardeh.UI.FullMode.LunaArt;

namespace Pardeh.UI.FullMode;

/// <summary>
/// The art of "Groove Phone" beyond its palette, after the Windows 11 Groove Music look: a deep blue page with
/// soft light, an acrylic card around the stage, a pale blue round play button, thin sliders with white thumbs. All drawn in code.
/// </summary>
internal static partial class PhoneSkin
{
    private static IBrush Glow(double x, double y, double rx, double ry, byte alpha, string hex) => new RadialGradientBrush
    {
        Center = new RelativePoint(x, y, RelativeUnit.Relative), GradientOrigin = new RelativePoint(x, y, RelativeUnit.Relative),
        RadiusX = new RelativeScalar(rx, RelativeUnit.Relative), RadiusY = new RelativeScalar(ry, RelativeUnit.Relative),
        GradientStops = { new GradientStop(A(alpha, hex), 0), new GradientStop(A(0, hex), 1) },
    };

    private static void AddGrooveArt(Dictionary<string, byte[]> files, Palette p)
    {
        // The page (stretched to the pane): blue with a light bloom at the top and two soft sweeps. Smooth, so 1x is enough.
        files["back.png"] = Png(180, 360, g =>
        {
            var all = new Rect(0, 0, 180, 360);
            g.FillRectangle(Grad(("#1552B4", 0), ("#0E3C94", 0.4), ("#0B2E78", 0.72), ("#08225C", 1)), all);
            g.FillRectangle(Glow(0.2, 0.08, 0.85, 0.42, 120, "#5FB4FF"), all);
            g.FillRectangle(Glow(0.95, 0.62, 0.7, 0.3, 70, "#3C86E8"), all);
            g.DrawGeometry(Solid(A(14, "#FFFFFF")), null, Geo("M 0,196 C 50,168 110,236 180,190 L 180,226 C 118,270 54,204 0,238 Z"));
            g.DrawGeometry(Solid(A(9, "#FFFFFF")), null, Geo("M 0,252 C 62,224 108,290 180,250 L 180,262 C 112,304 58,238 0,268 Z"));
        }, 1);

        // The acrylic card around the stage (nine-patch, 20 unit corners).
        files["np_frame.png"] = Png(64, 64, g =>
        {
            g.DrawRectangle(Solid(A(34, "#9CCBFF")), null, new RoundedRect(new Rect(0.5, 0.5, 63, 63), 16));
            g.DrawRectangle(null, new Pen(Grad(("#78CFE6FF", 0), ("#28CFE6FF", 1)), 1), new RoundedRect(new Rect(0.5, 0.5, 63, 63), 16));
        });

        // Bottom navigation: a darker translucent shelf; the selected page is only lit (no pill).
        files["nav.png"] = Png(1, Nav, g =>
        {
            g.FillRectangle(Solid(Color.Parse("#082257")), new Rect(0, 0, 1, Nav));
            g.FillRectangle(Solid(A(60, "#9CCBFF")), new Rect(0, 0, 1, 1));
        });
        files["bar.png"] = Png(1, Bar, g => g.FillRectangle(Solid(Color.Parse("#1552B4")), new Rect(0, 0, 1, Bar)));
        foreach (var (key, _) in NavIcons)
        {
            files[$"nav_{key}_down.png"] = Png(72, 52, g =>
            {
                g.FillRectangle(Solid(Color.FromArgb(2, 0, 0, 0)), new Rect(0, 0, 72, 52));
                NavGlyph(g, p, key, true, 36, 17);
            });
        }

        // The pale blue round play button with a soft halo.
        foreach (var (state, suffix) in States)
        {
            var face = state switch { State.Down => "#A9D4FF", State.Disabled => "#6C8CC0", _ => "#C6E6FF" };
            var ink = state == State.Disabled ? A(110, "#0B2E78") : Color.Parse("#0B2E78");
            foreach (var name in new[] { "play", "pause" })
            {
                files[$"{name}{suffix}.png"] = Png(64, 64, g =>
                {
                    if (state != State.Disabled)
                        g.DrawEllipse(Glow(0.5, 0.5, 0.5, 0.5, 150, "#9CD6FF"), null, new Point(32, 32), 32, 32);
                    g.DrawEllipse(Solid(Color.Parse(face)), null, new Point(32, 32), 29, 29);
                    FluentGlyph(g, ink, name, true, name == "play" ? 33 : 32, 32, 30);
                });
            }
        }

        // Thin pill tracks (end caps kept by borderSize) and white thumbs.
        files["seek_track.png"] = Png(24, 30, g => g.DrawRectangle(Solid(A(90, "#CFE6FF")), null, new RoundedRect(new Rect(0, 13, 24, 4), 2)));
        files["seek_fill.png"] = Png(24, 30, g => g.DrawRectangle(Solid(Color.Parse("#A8E0FF")), null, new RoundedRect(new Rect(0, 13, 24, 4), 2)));
        foreach (var hover in new[] { false, true })
        {
            files[hover ? "thumb_hover.png" : "thumb.png"] = Png(30, 30, g =>
            {
                g.DrawEllipse(Solid(A(70, "#02102E")), null, new Point(15, 16), 8.5, 8.5);
                g.DrawEllipse(Solid(Colors.White), null, new Point(15, 15), 8, 8);
            });
        }

        files["stage_frame.png"] = Png(12, 12, g => g.FillRectangle(Solid(Color.Parse("#04102E")), new Rect(0, 0, 12, 12)));
        files["cover_frame.png"] = Png(76, 76, g =>
        {
            g.DrawRectangle(Grad(("#4AA0F2", 0), ("#1C55BE", 1)), Pen(A(120, "#D5EBFF")), new RoundedRect(new Rect(0.5, 0.5, 75, 75), 12));
            g.DrawGeometry(null, new Pen(Solid(A(220, "#FFFFFF")), 2.4, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
                Geo("M 32,48 L 32,26 L 48,22 L 48,44 M 28,52 m -4,0 a 4,3 0 1,0 8,0 a 4,3 0 1,0 -8,0 M 44,48 m -4,0 a 4,3 0 1,0 8,0 a 4,3 0 1,0 -8,0"));
        });
    }

    // ---- Layout extras shared by the two art looks -------------------------------------------------

    private static bool HasArt(Palette p) => p.Key is "winamp-phone" or "groove-phone";

    /// <summary>
    /// The art looks' additions to the standard layout (text replacements, so the other looks' layout stays byte for byte as it was):
    /// the backdrop and cards behind Now Playing, pill-shaped sliders, themed queue rows, and per look the title's side and the EQ button.
    /// </summary>
    private static string ApplyArtLayout(Palette p, string layout)
    {
        var winamp = p.Key == "winamp-phone";
        var back = winamp ? "backgroundTiled=\"true\"" : "sliceBorder=\"0\"";
        var cards = $"""<SUBVIEW id="svBackdrop" left="0" top="0" width="{W}" height="{H - Bar - Nav}" backgroundImage="back.png" {back}/>"""
            + """<SUBVIEW id="npFrame" left="8" top="4" width="344" height="384" backgroundImage="np_frame.png" sliceBorder="20"/>""";
        if (winamp)
            cards += """<SUBVIEW id="npDeck" left="8" top="392" width="344" height="216" backgroundImage="np_deck.png" sliceBorder="20"/>""";
        layout = Replace(layout, "<SUBVIEW id=\"svStage\"", cards + "\n                <SUBVIEW id=\"svStage\"");
        layout = Replace(layout, "tiled=\"true\" slide=\"false\" borderSize=\"0\" direction=\"horizontal\"", "tiled=\"false\" slide=\"false\" borderSize=\"8\" direction=\"horizontal\"");
        layout = Replace(layout, "touch=\"true\" headerHeight=\"40\"", $"touch=\"true\" rowStyle=\"{(winamp ? "amber" : "tiles")}\" headerHeight=\"40\"");
        if (winamp)
        {
            layout = Replace(layout, "<REPEATBUTTON id=\"bRepeat\"",
                """<BUTTON id="bEq" left="260" top="558" image="eqkey.png" hoverImage="eqkey.png" downImage="eqkey_down.png" upToolTip="Equalizer" onClick="pardeh.command('openEqualizer');"/>""" + "\n                <REPEATBUTTON id=\"bRepeat\"");
        }
        else
        {
            // Groove: the cover's corners rounded like its frame.
            layout = Replace(layout, "backgroundImage=\"WMPImage_AlbumArtLarge\"", "backgroundImage=\"WMPImage_AlbumArtLarge\" coverFill=\"true\" coverRadius=\"10\"");
            // Groove: the title and artist on the left, as in the concept.
            layout = Replace(layout, "justification=\"center\" fontType=\"Segoe UI\" fontSize=\"13\"", "justification=\"left\" fontType=\"Segoe UI\" fontSize=\"15\"");
            layout = Replace(layout, "justification=\"center\" fontType=\"Segoe UI\" fontSize=\"9\"", "justification=\"left\" fontType=\"Segoe UI\" fontSize=\"10\"");
        }

        return layout;
    }

    private static string Replace(string text, string old, string value) =>
        text.Contains(old, StringComparison.Ordinal) ? text.Replace(old, value, StringComparison.Ordinal) : throw new InvalidOperationException("Phone layout changed: " + old);

    /// <summary>Runs after the standard Now Playing layout: places the cards and insets the stage inside its frame.</summary>
    private static string ArtScript(Palette p) => """

        var baseLayoutNow = LayoutNow;
        LayoutNow = function (w, h, ch) {
            baseLayoutNow(w, h, ch);
            var pane = ch;
            svBackdrop.width = w; svBackdrop.height = pane;
            var fx = svStage.left - 6, fy = svStage.top - 6, fw = svStage.width + 12, fh = svStage.height + 12;
            npFrame.left = fx; npFrame.top = fy; npFrame.width = fw; npFrame.height = fh;
            svStage.left = fx + 6; svStage.top = fy + 6; svStage.width = fw - 12; svStage.height = fh - 12;

            // A short landscape view: the bottom row moves up so its buttons are not cut off by the navigation bar.
            var over = bShuffle.top + 45 - ch;
            if (over > 0) { bShuffle.top = bShuffle.top - over; bMute.top = bMute.top - over; bRepeat.top = bRepeat.top - over; vol.top = vol.top - over; }
        """ + (p.Key == "winamp-phone" ? """

            var deckTop = Math.max(2, tTitle.top - 6), colX = seek.left - 14, colW = seek.width + 28;
            npDeck.left = colX + 6; npDeck.top = deckTop; npDeck.width = colW - 12; npDeck.height = Math.max(40, Math.min(pane - deckTop - 2, bShuffle.top + 52 - deckTop));
            bShuffle.left = colX + 12; bMute.left = colX + 58;
            bRepeat.left = colX + colW - 56; bEq.left = colX + colW - 102; bEq.top = bShuffle.top;
            vol.left = colX + 104; vol.width = colW - 104 - 108;
        };
        """ : """

            var colX2 = seek.left - 14, colW2 = seek.width + 28;
            tTitle.left = colX2 + 18; tTitle.width = colW2 - 18 - 108; tTitle.height = 28; tTitle.top = tTitle.top - 2;
            tArtist.left = colX2 + 18; tArtist.width = colW2 - 18 - 108; tArtist.top = tArtist.top + 2;
            bLyr.left = colX2 + colW2 - 14 - 88;
        };
        """);
}
