using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Pardeh.Skinning.Model;
using Pardeh.Skinning.Wmp;
using static Pardeh.UI.FullMode.LunaArt;

namespace Pardeh.UI.FullMode;

/// <summary>
/// Builds Pardeh's designed full modes (Horizon, Luna, Fluent) as skin packages, drawn entirely in code from a
/// <see cref="FullModeDesign"/>. Shared layout: a top bar holding the emblem and menus plus a tab with the playlist
/// chooser and window buttons; a task rail (Now Playing, Media Library, Copy from CD, Copy to CD or Device, Radio Tuner,
/// Info Center, Skin Chooser); a stage (video or visualization, title over it, status line); a playlist panel (cover,
/// details, list, total time); and a transport (seek bar, play orb, stop / previous / next, mute and volume, shuffle /
/// repeat, enhancements, an LCD time capsule, skin mode). Resizable; works without any Windows XP files.
/// All art is original.
/// </summary>
internal sealed class FullModeDesigner
{
    // Design size and metrics (skin pixels).
    private const int W = 780, H = 540;
    private const int Top = 38;                 // top bar
    private const int Rail = 140;               // task rail
    private const int Transport = 78;           // seek row + controls
    private const int Edge = 4;                 // right frame
    private const int ListW = 260;              // playlist panel
    private const int Cap = 26;                 // rail cap
    private const int Status = 22;              // stage status line
    private const int TopLeftW = 300, TopRightW = 270, TransportRightW = 230;
    private const int ContentW = W - Rail - Edge, ContentH = H - Top - Transport;
    private const int StageW = ContentW - ListW;

    private readonly FullModeDesign _d;

    private FullModeDesigner(FullModeDesign design) => _d = design;

    public static SkinPackage Build(FullModeDesign design)
    {
        var designer = new FullModeDesigner(design);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        designer.AddChrome(files);
        designer.AddButtons(files);
        design.Retro?.Art(files);
        files["fullmode.wms"] = Encoding.UTF8.GetBytes(designer.Layout());
        files["fullmode.js"] = Encoding.UTF8.GetBytes(design.Retro is { } retro
            ? $"var RAIL_ON = '{retro.RailInkOn}', RAIL_OFF = '{retro.RailInkOff}';\n" + Script + (retro.Equalizer ? EqualizerScript : string.Empty)
            : Script);
        return WmzLoader.FromFiles(files, design.SourcePath);
    }

    private bool Modern => _d.Modern;

    private static IBrush Solid(Color color) => new SolidColorBrush(color);

    // ---- Chrome ---------------------------------------------------------------------------------

    private void AddChrome(Dictionary<string, byte[]> files)
    {
        files["top_left.png"] = Png(TopLeftW, Top, TopLeft);
        files["top_mid.png"] = Png(1, Top, g => TopBand(g, 1));
        files["top_right.png"] = Png(TopRightW, Top, TopRight);
        files["rail_cap.png"] = Png(Rail, Cap, RailCap);
        files["rail_tile.png"] = Png(Rail, 1, g => RailBody(g, 1));
        files["corner.png"] = Png(Rail, Transport, Corner);
        files["edge.png"] = Png(Edge, 1, g =>
        {
            g.FillRectangle(Horizontal((_d.EdgeLight, 0), (_d.EdgeDark, 1)), new Rect(0, 0, Edge, 1));
            g.FillRectangle(Solid(_d.Outline), new Rect(Edge - 1, 0, 1, 1));
        });
        files["transport_mid.png"] = Png(1, Transport, g => TransportBand(g, 1));
        files["transport_right.png"] = Png(TransportRightW, Transport, TransportRight);
        files["list_header.png"] = Png(ListW, 24, g =>
        {
            g.FillRectangle(Vertical((_d.HeaderTop, 0), (Color.Parse(_d.Panel), 0.5), (_d.HeaderBottom, 1)), new Rect(0, 0, ListW, 24));
            if (!Modern)
                g.FillRectangle(Solid(WithAlpha(Colors.White, 90)), new Rect(0, 0, ListW, 1));
            g.FillRectangle(Solid(Modern ? _d.PanelLight : _d.Outline), new Rect(0, 23, ListW, 1));
        });
        files["list_footer.png"] = Png(ListW, 26, g =>
        {
            g.FillRectangle(Vertical((_d.PanelDark, 0), (_d.PanelDarker, 1)), new Rect(0, 0, ListW, 26));
            g.FillRectangle(Solid(Modern ? _d.PanelLight : _d.Outline), new Rect(0, 0, ListW, 1));
        });
        files["album_frame.png"] = Png(84, 84, AlbumPlaceholder);
        if (Modern)
        {
            // Windows 11 combo box: a dark rounded field (stretched as a nine-slice).
            files["dropdown_frame.png"] = Png(12, 12, g =>
                g.DrawRectangle(Solid(Color.Parse("#2D2D2D")), Pen(Color.Parse("#3A3A3A")), new RoundedRect(new Rect(0.5, 0.5, 11, 11), 3)));
        }
        files["stage_glow.png"] = Png(StageW, ContentH - Status, g =>
            g.FillRectangle(new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0.55, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.5, 0.55, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.65, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.65, RelativeUnit.Relative),
                GradientStops = { new GradientStop(_d.StageGlow, 0), new GradientStop(Colors.Black, 1) },
            }, new Rect(0, 0, StageW, ContentH - Status)));
    }

    private void TopBand(DrawingContext g, double w)
    {
        g.FillRectangle(Vertical((_d.BarLight, 0), (_d.BarDark, 1)), new Rect(0, 0, w, Top));
        g.FillRectangle(Solid(_d.Outline), new Rect(0, 0, w, 1));
        g.FillRectangle(Solid(_d.BarHighlight), new Rect(0, 1, w, 1));
        g.FillRectangle(Solid(_d.BarLine), new Rect(0, Top - 1, w, 1));
    }

    private double Radius => Modern ? 8 : 9;

    private void TopLeft(DrawingContext g)
    {
        using (g.PushClip(new RoundedRect(new Rect(0, 0, TopLeftW, Top + 10), Radius, 0, 0, 0)))
        {
            TopBand(g, TopLeftW);

            if (_d.LogoBlock)
            {
                // The blue block that holds the emblem and the menus, with its sweeping lower-right corner.
                var block = Geo("M 0,0 L 278,0 L 278,8 Q 278,34 252,34 L 0,34 Z");
                g.DrawGeometry(Vertical((_d.LogoLight, 0), (_d.LogoMid, 0.5), (_d.LogoDark, 1)), null, block);
                g.DrawGeometry(null, Pen(WithAlpha(Colors.White, 110)), Geo("M 1,1.5 L 277,1.5"));
                g.DrawGeometry(null, Pen(_d.Outline), Geo("M 278,0 L 278,8 Q 278,34.5 252,34.5 L 0,34.5"));
            }
            else if (!Modern)
            {
                // A soft gloss across the upper half of the band.
                g.FillRectangle(Vertical((WithAlpha(Colors.White, 120), 0), (WithAlpha(Colors.White, 0), 1)), new Rect(0, 2, TopLeftW, Top / 2.0));
            }

            g.DrawGeometry(null, Pen(_d.Outline), Geo(FormattableString.Invariant($"M 0.5,40 L 0.5,{Radius + 0.5} Q 0.5,0.5 {Radius + 0.5},0.5 L 300,0.5")));
        }

        PardehLogo.Draw(g, new Rect(9, 5, 26, 26));
    }

    private void TopRight(DrawingContext g)
    {
        using (g.PushClip(new RoundedRect(new Rect(-10, 0, TopRightW + 10, Top + 10), 0, Radius, 0, 0)))
        {
            TopBand(g, TopRightW);
            if (!Modern && !_d.LogoBlock)
                g.FillRectangle(Vertical((WithAlpha(Colors.White, 120), 0), (WithAlpha(Colors.White, 0), 1)), new Rect(0, 2, TopRightW, Top / 2.0));
            g.DrawGeometry(null, Pen(_d.Outline), Geo(FormattableString.Invariant(
                $"M 0,0.5 L {TopRightW - Radius - 0.5},0.5 Q {TopRightW - 0.5},0.5 {TopRightW - 0.5},{Radius + 0.5} L {TopRightW - 0.5},{Top}")));
        }

        if (Modern)
            return; // Windows 11: the chooser and window buttons sit directly on the bar.

        // Raised tab holding the playlist chooser and the window buttons.
        var tab = new RoundedRect(new Rect(12.5, 6.5, TopRightW - 22, 25), 8);
        g.DrawRectangle(Vertical((_d.TabLight, 0), (_d.TabDark, 1)), Pen(_d.TabBorder), tab);
        g.DrawRectangle(null, Pen(WithAlpha(Colors.White, 220)), new RoundedRect(new Rect(13.5, 7.5, TopRightW - 24, 23), 7));
    }

    private void RailBody(DrawingContext g, double h)
    {
        g.FillRectangle(Horizontal((_d.RailLight, 0), (_d.RailMid, 0.6), (_d.RailDark, 1)), new Rect(0, 0, Rail, h));
        g.FillRectangle(Solid(_d.Outline), new Rect(0, 0, 1, h));
        if (!Modern)
            g.FillRectangle(Solid(Colors.White), new Rect(1, 0, 1, h));
        g.FillRectangle(Solid(_d.RailEdge), new Rect(Rail - 1, 0, 1, h));
    }

    private void RailCap(DrawingContext g)
    {
        RailBody(g, Cap);
        if (Modern)
            return; // Windows 11: just the "Tasks" caption.
        var cap = new RoundedRect(new Rect(5, 3, Rail - 10, Cap - 6), 9);
        g.DrawRectangle(Vertical((_d.CapLight, 0), (_d.CapDark, 1)), Pen(_d.Outline), cap);
        g.DrawRectangle(null, Pen(WithAlpha(Colors.White, 70)), new RoundedRect(new Rect(6, 4, Rail - 12, Cap - 8), 8));
        // The round "sizer" knob on the cap.
        g.DrawEllipse(Radial(Colors.White, _d.OrbDark), Pen(_d.Outline), new Point(Rail - 18, Cap / 2.0), 6.5, 6.5);
        g.DrawGeometry(Solid(_d.Glyph), null, Geo(FormattableString.Invariant($"M {Rail - 21},{Cap / 2.0 - 1} L {Rail - 15},{Cap / 2.0 - 1} L {Rail - 18},{Cap / 2.0 + 2.5} Z")));
    }

    private void Corner(DrawingContext g)
    {
        using (g.PushClip(new RoundedRect(new Rect(0, -10, Rail + 10, Transport + 10), 0, 0, 0, Radius)))
        {
            RailBody(g, Transport);
            g.FillRectangle(Solid(_d.Outline), new Rect(0, Transport - 1, Rail, 1));
            g.DrawGeometry(null, Pen(_d.Outline), Geo(FormattableString.Invariant(
                $"M 0.5,0 L 0.5,{Transport - Radius - 0.5} Q 0.5,{Transport - 0.5} {Radius + 0.5},{Transport - 0.5} L {Rail},{Transport - 0.5}")));
        }

        // Pardeh's mark and name (پرده: curtain, screen).
        PardehLogo.Draw(g, new Rect(14, 18, 38, 38));
        var face = Modern
            ? new Typeface("Segoe UI", FontStyle.Normal, FontWeight.SemiBold)
            : new Typeface("Trebuchet MS", FontStyle.Italic, FontWeight.Bold);
        FormattedText Name(Color color) => new("Pardeh", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, Modern ? 16 : 17, Solid(color));
        if (!Modern)
            g.DrawText(Name(WithAlpha(Colors.White, 200)), new Point(59, 27));
        g.DrawText(Name(_d.RailInk), new Point(58, 26));
    }

    private void TransportBand(DrawingContext g, double w)
    {
        g.FillRectangle(Vertical((_d.Silver, 0), (_d.SilverMid, 0.35), (_d.SilverDark, 1)), new Rect(0, 0, w, Transport));
        g.FillRectangle(Solid(_d.Outline), new Rect(0, 0, w, 1));
        if (!Modern)
        {
            g.FillRectangle(Solid(Colors.White), new Rect(0, 1, w, 1));
            g.FillRectangle(Solid(_d.SilverLine), new Rect(0, 24, w, 1));
            g.FillRectangle(Solid(WithAlpha(Colors.White, 200)), new Rect(0, 25, w, 1));
        }

        g.FillRectangle(Solid(_d.Outline), new Rect(0, Transport - 1, w, 1));
    }

    private void TransportRight(DrawingContext g)
    {
        using (g.PushClip(new RoundedRect(new Rect(-10, -10, TransportRightW + 10, Transport + 10), 0, 0, Radius, 0)))
        {
            TransportBand(g, TransportRightW);
            g.DrawGeometry(null, Pen(_d.Outline), Geo(FormattableString.Invariant(
                $"M {TransportRightW - 0.5},0 L {TransportRightW - 0.5},{Transport - Radius - 0.5} Q {TransportRightW - 0.5},{Transport - 0.5} {TransportRightW - Radius - 0.5},{Transport - 0.5} L 0,{Transport - 0.5}")));
        }

        // Time display (the text is drawn over it by the layout).
        if (Modern)
        {
            g.DrawRectangle(Solid(_d.Lcd), null, new RoundedRect(new Rect(14, 36, 121, 27), 6));
        }
        else
        {
            var lcd = new RoundedRect(new Rect(14.5, 36.5, 120, 26), 13);
            g.DrawRectangle(Vertical((Color.Parse("#AFC0DD"), 0), (Colors.White, 1)), null, new RoundedRect(new Rect(13, 35, 123, 29), 14.5));
            g.DrawRectangle(Vertical((_d.LcdTop, 0), (_d.Lcd, 1)), Pen(_d.LcdBorder), lcd);
            g.DrawRectangle(null, Pen(WithAlpha(Color.Parse(_d.LcdInk), 30)), new RoundedRect(new Rect(16, 38, 117, 23), 11.5));
        }

        // Resize grip.
        var dots = Solid(Modern ? Color.Parse("#6E6E6E") : Color.Parse("#7A8CC4"));
        foreach (var (dx, dy) in new[] { (0, 8), (4, 4), (4, 8), (8, 0), (8, 4), (8, 8) })
            g.FillRectangle(dots, new Rect(TransportRightW - 17 + dx, Transport - 16 + dy, 2, 2));
    }

    private void AlbumPlaceholder(DrawingContext g)
    {
        if (Modern)
        {
            g.DrawRectangle(Solid(Color.Parse("#333333")), null, new RoundedRect(new Rect(0, 0, 82, 82), 6));
        }
        else
        {
            g.DrawRectangle(Solid(WithAlpha(Colors.Black, 70)), null, new Rect(2, 3, 82, 81));
            g.DrawRectangle(Vertical((Color.Parse("#7891CB"), 0), (_d.PanelDarker, 1)), Pen(_d.Outline), new Rect(0.5, 0.5, 81, 81));
            g.DrawRectangle(null, Pen(WithAlpha(Colors.White, 60)), new Rect(1.5, 1.5, 79, 79));
        }

        // A pair of beamed notes.
        var ink = Solid(WithAlpha(Colors.White, (byte)(Modern ? 150 : 200)));
        g.DrawGeometry(ink, null, Geo("M 33,24 L 55,19 L 55,23 L 33,28 Z"));
        g.FillRectangle(ink, new Rect(33, 26, 2.5, 26));
        g.FillRectangle(ink, new Rect(52.5, 21, 2.5, 26));
        g.DrawEllipse(ink, null, new Point(29.5, 53), 6, 4.6);
        g.DrawEllipse(ink, null, new Point(49, 48), 6, 4.6);
    }

    private static void DrawEmblem(DrawingContext g, Rect r)
    {
        // Pardeh's mark: four curtain folds.
        var colors = new[] { Color.Parse("#E8483B"), Color.Parse("#F2B530"), Color.Parse("#3C9B4A"), Color.Parse("#2E6FD8") };
        var fold = r.Width / 4;
        for (var i = 0; i < 4; i++)
        {
            var x = r.X + i * fold;
            var geometry = Geo(FormattableString.Invariant(
                $"M {x},{r.Y + 2} Q {x + fold / 2},{r.Y - 2} {x + fold},{r.Y + 2} L {x + fold - 1},{r.Bottom} Q {x + fold / 2},{r.Bottom - 4} {x + 1},{r.Bottom} Z"));
            var dark = Color.FromRgb((byte)(colors[i].R * 0.7), (byte)(colors[i].G * 0.7), (byte)(colors[i].B * 0.7));
            g.DrawGeometry(Vertical((Colors.White, 0), (colors[i], 0.35), (dark, 1)), Pen(WithAlpha(Colors.Black, 70), 0.6), geometry);
        }
    }

    // ---- Buttons --------------------------------------------------------------------------------

    private enum State { Normal, Hover, Down, Disabled }

    private static readonly (State State, string Suffix)[] States =
        [(State.Normal, ""), (State.Hover, "_hover"), (State.Down, "_down"), (State.Disabled, "_disabled")];

    private void AddButtons(Dictionary<string, byte[]> files)
    {
        foreach (var (state, suffix) in States)
        {
            files[$"play{suffix}.png"] = Png(48, 48, g => Orb(g, 48, state, true, c => PlayGlyph(g, c, 24, 24, 10)));
            files[$"pause{suffix}.png"] = Png(48, 48, g => Orb(g, 48, state, true, c => PauseGlyph(g, c, 24, 24, 9)));
            files[$"stop{suffix}.png"] = Png(30, 30, g => Orb(g, 30, state, false, c => g.DrawRectangle(Solid(c), null, new RoundedRect(new Rect(10.5, 10.5, 9, 9), 1.5))));
            files[$"prev{suffix}.png"] = Png(30, 30, g => Orb(g, 30, state, false, c => SkipGlyph(g, c, forward: false)));
            files[$"next{suffix}.png"] = Png(30, 30, g => Orb(g, 30, state, false, c => SkipGlyph(g, c, forward: true)));
            files[$"mute{suffix}.png"] = Png(28, 28, g => Orb(g, 28, state, false, c => Speaker(g, c, crossed: false)));
            files[$"muted{suffix}.png"] = Png(28, 28, g => Orb(g, 28, state, false, c => Speaker(g, c, crossed: true)));
            files[$"shuffle{suffix}.png"] = Png(26, 26, g => Orb(g, 26, state, false, c => Stroke(g, c, "M 7,9 L 10,9 L 16,17 L 19,17 M 7,17 L 10,17 L 16,9 L 19,9 M 17,7 L 19,9 L 17,11 M 17,15 L 19,17 L 17,19")));
            files[$"repeat{suffix}.png"] = Png(26, 26, g => Orb(g, 26, state, false, c => Stroke(g, c, "M 8,14 L 8,10 L 17,10 M 18,12 L 18,16 L 9,16 M 15,8 L 17,10 L 15,12 M 11,14 L 9,16 L 11,18")));
            files[$"eq{suffix}.png"] = Png(26, 26, g => Orb(g, 26, state, false, c => Stroke(g, c, "M 9,18 L 9,13 M 13,18 L 13,8 M 17,18 L 17,11")));
            files[$"skinmode{suffix}.png"] = Png(26, 26, g => Orb(g, 26, state, false, c => Stroke(g, c, "M 8,8 L 18,8 L 18,18 L 8,18 Z M 11,11 L 15,11 L 15,15 L 11,15 Z")));
            files[$"viz_prev{suffix}.png"] = Png(16, 16, g => Mini(g, state, left: true));
            files[$"viz_next{suffix}.png"] = Png(16, 16, g => Mini(g, state, left: false));
            files[$"min{suffix}.png"] = Png(20, 18, g => WindowButton(g, state, "M 6,12.5 L 14,12.5"));
            files[$"max{suffix}.png"] = Png(20, 18, g => WindowButton(g, state, "M 6,5 L 14,5 L 14,13 L 6,13 Z M 6,6 L 14,6"));
            files[$"close{suffix}.png"] = Png(20, 18, g => WindowButton(g, state, "M 6.5,5.5 L 13.5,12.5 M 13.5,5.5 L 6.5,12.5", close: true));
        }

        files["rail_item.png"] = Png(128, 40, g => g.FillRectangle(Solid(Color.FromArgb(2, 255, 255, 255)), new Rect(0, 0, 128, 40)));
        files["rail_item_hover.png"] = Png(128, 40, g => RailPill(g, active: false));
        files["rail_item_down.png"] = Png(128, 40, g => RailPill(g, active: true));

        if (Modern)
        {
            // Windows 11 sliders: a thin track, accent fill, a round thumb with an accent centre.
            files["seek_track.png"] = Png(6, 14, g => g.FillRectangle(Solid(_d.SeekTop), new Rect(0, 5, 6, 4)));
            files["seek_fill.png"] = Png(6, 14, g => g.FillRectangle(Solid(_d.FillMid), new Rect(0, 5, 6, 4)));
            files["seek_thumb.png"] = Png(14, 14, g => ModernThumb(g, 14, hover: false));
            files["seek_thumb_hover.png"] = Png(14, 14, g => ModernThumb(g, 14, hover: true));
            files["volume_track.png"] = Png(80, 14, g => g.DrawRectangle(Solid(_d.SeekTop), null, new RoundedRect(new Rect(4, 5, 72, 4), 2)));
            files["volume_fill.png"] = Png(80, 14, g => g.DrawRectangle(Solid(_d.VolumeDark), null, new RoundedRect(new Rect(4, 5, 72, 4), 2)));
            files["volume_thumb.png"] = Png(14, 14, g => ModernThumb(g, 14, hover: false));
            files["volume_thumb_hover.png"] = Png(14, 14, g => ModernThumb(g, 14, hover: true));
            return;
        }

        files["seek_track.png"] = Png(6, 12, g =>
        {
            g.DrawRectangle(Vertical((_d.SeekTop, 0), (_d.SeekBottom, 1)), null, new Rect(0, 1, 6, 10));
            g.FillRectangle(Solid(_d.SeekLine), new Rect(0, 1, 6, 1));
            g.FillRectangle(Brushes.White, new Rect(0, 11, 6, 1));
        });
        files["seek_fill.png"] = Png(6, 12, g =>
            g.FillRectangle(Vertical((_d.FillLight, 0), (_d.FillMid, 0.5), (_d.FillDark, 1)), new Rect(0, 2, 6, 9)));
        files["seek_thumb.png"] = Png(30, 14, g => Capsule(g, new Rect(0.5, 0.5, 29, 13), _d.ThumbLight, _d.ThumbMid, _d.ThumbDark));
        files["seek_thumb_hover.png"] = Png(30, 14, g => Capsule(g, new Rect(0.5, 0.5, 29, 13), _d.ThumbHoverLight, _d.ThumbHoverMid, _d.ThumbHoverDark));
        files["volume_track.png"] = Png(80, 14, g =>
            g.DrawRectangle(Vertical((_d.SeekLine, 0), (_d.SeekTop, 1)), Pen(_d.OrbRim), new RoundedRect(new Rect(4.5, 4.5, 71, 5), 2.5)));
        files["volume_fill.png"] = Png(80, 14, g =>
            g.DrawRectangle(Vertical((_d.VolumeLight, 0), (_d.VolumeDark, 1)), null, new RoundedRect(new Rect(5, 5, 70, 4), 2)));
        files["volume_thumb.png"] = Png(12, 16, g => Capsule(g, new Rect(0.5, 0.5, 11, 15), _d.ThumbLight, _d.ThumbMid, _d.ThumbDark));
        files["volume_thumb_hover.png"] = Png(12, 16, g => Capsule(g, new Rect(0.5, 0.5, 11, 15), _d.ThumbHoverLight, _d.ThumbHoverMid, _d.ThumbHoverDark));
    }

    private Color InkFor(State state) => state == State.Disabled ? _d.GlyphDisabled : _d.Glyph;

    /// <summary>
    /// A transport button. XP designs: a glossy orb with a blue rim (the play orb gets a stronger ring).
    /// Fluent: the play button is a white disc with a dark glyph; the rest are flat glyphs with a soft hover circle.
    /// </summary>
    private void Orb(DrawingContext g, int size, State state, bool hero, Action<Color> glyph)
    {
        var c = new Point(size / 2.0, size / 2.0);
        var r = size / 2.0 - 1;
        if (Modern)
        {
            if (hero)
            {
                var disc = state switch
                {
                    State.Hover => Color.Parse("#E8E8E8"),
                    State.Down => Color.Parse("#CFCFCF"),
                    State.Disabled => Color.Parse("#5A5A5A"),
                    _ => Colors.White,
                };
                g.DrawEllipse(Solid(disc), null, c, r - 3, r - 3);
                glyph(state == State.Disabled ? Color.Parse("#2B2B2B") : Color.Parse("#1C1C1C"));
            }
            else
            {
                if (state is State.Hover or State.Down)
                    g.DrawEllipse(Solid(WithAlpha(Colors.White, (byte)(state == State.Down ? 14 : 22))), null, c, r, r);
                glyph(state == State.Down ? Color.Parse("#C5C5C5") : InkFor(state));
            }

            return;
        }

        g.DrawEllipse(Solid(WithAlpha(Color.Parse("#1C2E66"), 55)), null, new Point(c.X, c.Y + 1), r, r);
        var face = state switch
        {
            State.Hover => Vertical((Colors.White, 0), (_d.OrbHoverMid, 0.5), (_d.OrbHoverDark, 1)),
            State.Down => Vertical((_d.OrbDark, 0), (_d.OrbMid, 1)),
            State.Disabled => Vertical((Color.Parse("#F7F9FC"), 0), (Color.Parse("#DCE2EE"), 1)),
            _ => Vertical((Colors.White, 0), (_d.OrbMid, 0.5), (_d.OrbDark, 1)),
        };
        var rim = state == State.Hover ? _d.OrbHoverRim : hero ? _d.OrbHeroRim : _d.OrbRim;
        g.DrawEllipse(face, Pen(rim, hero ? 1.6 : 1), c, r - 0.5, r - 0.5);
        // Gloss across the top half.
        g.DrawEllipse(Vertical((WithAlpha(Colors.White, 230), 0), (WithAlpha(Colors.White, 0), 1)), null,
            new Point(c.X, c.Y - r * 0.38), r * 0.72, r * 0.5);
        if (hero)
            g.DrawEllipse(null, Pen(WithAlpha(Colors.White, 200)), c, r - 3, r - 3);
        glyph(InkFor(state));
    }

    private static void PlayGlyph(DrawingContext g, Color color, double cx, double cy, double half) =>
        g.DrawGeometry(Solid(color), null, Geo(FormattableString.Invariant(
            $"M {cx - half * 0.55},{cy - half} L {cx + half * 0.95},{cy} L {cx - half * 0.55},{cy + half} Z")));

    private static void PauseGlyph(DrawingContext g, Color color, double cx, double cy, double half)
    {
        var brush = Solid(color);
        g.DrawRectangle(brush, null, new RoundedRect(new Rect(cx - half * 0.8, cy - half, half * 0.55, half * 2), 1));
        g.DrawRectangle(brush, null, new RoundedRect(new Rect(cx + half * 0.25, cy - half, half * 0.55, half * 2), 1));
    }

    private static void SkipGlyph(DrawingContext g, Color color, bool forward)
    {
        var brush = Solid(color);
        if (forward)
        {
            g.DrawGeometry(brush, null, Geo("M 10,9.5 L 18,15 L 10,20.5 Z"));
            g.FillRectangle(brush, new Rect(18.5, 9.5, 2.2, 11));
        }
        else
        {
            g.DrawGeometry(brush, null, Geo("M 20,9.5 L 12,15 L 20,20.5 Z"));
            g.FillRectangle(brush, new Rect(9.3, 9.5, 2.2, 11));
        }
    }

    private static void Speaker(DrawingContext g, Color color, bool crossed)
    {
        g.DrawGeometry(Solid(color), null, Geo("M 7,11.5 L 10,11.5 L 14,8 L 14,20 L 10,16.5 L 7,16.5 Z"));
        if (crossed)
            g.DrawGeometry(null, new Pen(Solid(Color.Parse("#E0503F")), 1.6, lineCap: PenLineCap.Round), Geo("M 16.5,11 L 21,17 M 21,11 L 16.5,17"));
        else
            g.DrawGeometry(null, new Pen(Solid(color), 1.2, lineCap: PenLineCap.Round), Geo("M 16.5,11 Q 19,14 16.5,17 M 18.5,9 Q 23,14 18.5,19"));
    }

    private static void Stroke(DrawingContext g, Color color, string data) =>
        g.DrawGeometry(null, new Pen(Solid(color), 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), Geo(data));

    private void Mini(DrawingContext g, State state, bool left)
    {
        var arrow = Geo(left ? "M 9.8,4.5 L 5.2,8 L 9.8,11.5 Z" : "M 6.2,4.5 L 10.8,8 L 6.2,11.5 Z");
        if (Modern)
        {
            if (state == State.Hover)
                g.DrawEllipse(Solid(WithAlpha(Colors.White, 26)), null, new Point(8, 8), 8, 8);
            g.DrawGeometry(Solid(Color.Parse("#C5C5C5")), null, arrow);
            return;
        }

        var face = state == State.Hover ? Radial(Colors.White, Color.Parse("#9FC3F5")) : Radial(Color.Parse("#E3EAF6"), Color.Parse("#8496C4"));
        g.DrawEllipse(face, Pen(Color.Parse("#16234A")), new Point(8, 8), 7, 7);
        g.DrawGeometry(Solid(Color.Parse("#16234A")), null, arrow);
    }

    private void WindowButton(DrawingContext g, State state, string data, bool close = false)
    {
        if (state is State.Hover or State.Down)
        {
            IBrush back;
            if (Modern)
                back = close ? Solid(Color.Parse(state == State.Down ? "#C83C31" : "#C42B1C")) : Solid(WithAlpha(Colors.White, (byte)(state == State.Down ? 14 : 22)));
            else
                back = close
                    ? Vertical((Color.Parse("#F29A86"), 0), (Color.Parse("#D2442A"), 1))
                    : Vertical((Colors.White, 0), (Color.Parse(state == State.Down ? "#B9CBEA" : "#D7E5F8"), 1));
            var border = Modern ? null : Pen(close ? Color.Parse("#9E2A16") : Color.Parse("#7F99D0"));
            g.DrawRectangle(back, border, new RoundedRect(new Rect(0.5, 0.5, 19, 17), 4));
        }

        var ink = close && state is State.Hover or State.Down ? Colors.White : _d.Glyph;
        g.DrawGeometry(null, new Pen(Solid(ink), Modern ? 1.1 : 1.8, lineCap: PenLineCap.Round), Geo(data));
    }

    private void RailPill(DrawingContext g, bool active)
    {
        if (Modern)
        {
            // Windows 11 navigation: a soft rounded fill; the selected item gets an accent bar on its left.
            g.DrawRectangle(Solid(WithAlpha(Colors.White, (byte)(active ? 15 : 9))), null, new RoundedRect(new Rect(0, 2, 128, 36), 4));
            if (active)
                g.DrawRectangle(Solid(_d.PillBorder), null, new RoundedRect(new Rect(1, 12, 3, 16), 1.5));
            return;
        }

        var rect = new RoundedRect(new Rect(0.5, 0.5, 127, 39), 7);
        if (active)
        {
            g.DrawRectangle(Vertical((_d.PillLight, 0), (_d.PillDark, 1)), Pen(_d.PillBorder), rect);
            g.DrawRectangle(null, Pen(WithAlpha(Colors.White, 220)), new RoundedRect(new Rect(1.5, 1.5, 125, 37), 6));
            // The caret pointing at the open pane.
            g.DrawGeometry(Solid(_d.RailInk), null, Geo("M 114,15 L 119,20 L 114,25 Z"));
        }
        else
        {
            g.DrawRectangle(Solid(WithAlpha(Colors.White, 120)), Pen(WithAlpha(_d.HoverBorder, 170)), rect);
        }
    }

    private static void Capsule(DrawingContext g, Rect r, string light, string mid, string dark)
    {
        var radius = Math.Min(r.Width, r.Height) / 2;
        g.DrawRectangle(Vertical((Color.Parse(light), 0), (Color.Parse(mid), 0.55), (Color.Parse(dark), 1)), Pen(Color.Parse(dark)), new RoundedRect(r, radius));
        g.DrawRectangle(null, Pen(WithAlpha(Colors.White, 140)), new RoundedRect(r.Deflate(1.5), Math.Max(1, radius - 1.5)));
    }

    private void ModernThumb(DrawingContext g, int size, bool hover)
    {
        var c = new Point(size / 2.0, size / 2.0);
        g.DrawEllipse(Solid(Color.Parse("#454545")), Pen(Color.Parse("#555555")), c, size / 2.0 - 0.5, size / 2.0 - 0.5);
        g.DrawEllipse(Solid(_d.FillMid), null, c, hover ? 4.5 : 3.5, hover ? 4.5 : 3.5);
    }

    // ---- Layout & script ------------------------------------------------------------------------

    private string RailItem(string id, string pane, string label, int index)
    {
        var retro = _d.Retro;
        var tip = label.Replace("&#10;", " ");
        var shown = retro is { Lowercase: true } ? label.ToLowerInvariant().Replace("cd", "CD") : label;
        var two = label.Contains("&#10;");
        var words = retro is { Words: true };
        var size = retro?.RailSize ?? 8;
        var textTop = words ? two ? 1 : 40 / 2 - size - 1 : two ? 6 : 13;
        var left = words ? 18 : Modern ? 14 : 16;
        var font = retro?.RailFont ?? _d.Font;
        var ink = retro?.RailInkOff ?? Hex(_d.RailInk);
        return $$"""
                <BUTTON id="{{id}}" left="6" top="{{6 + index * 43}}" width="128" height="40"
                    image="rail_item.png" hoverImage="rail_item_hover.png" downImage="rail_item_down.png"
                    onClick="ShowPane('{{pane}}');" upToolTip="{{tip}}"/>
                <TEXT id="{{id}}Label" left="{{left}}" top="{{6 + index * 43 + textTop}}" width="{{(words ? 118 : 96)}}" height="{{(words ? 40 : 30)}}" value="{{shown}}"
                    fontType="{{font}}" fontSize="{{(words && two ? size - 1 : size)}}" fontStyle="{{(Modern ? "normal" : "bold")}}" foregroundColor="{{ink}}"/>
        """;
    }

    /// <summary>The width of the frame around the stage of a framed PC look.</summary>
    internal const int StageInset = 6;

    private const int EqualizerH = 152;

    /// <summary>A framed look's backdrop and the card the stage sits in.</summary>
    private string StageCard(int inset)
    {
        if (inset == 0)
            return string.Empty;
        var back = _d.Retro is { TiledBack: true } ? "backgroundTiled=\"true\"" : "resizeBackgroundImage=\"true\"";
        return $"""
                <SUBVIEW left="0" top="0" width="{StageW}" height="{ContentH - Status}" backgroundImage="pane_back.png" {back} horizontalAlignment="stretch" verticalAlignment="stretch"/>
                <SUBVIEW left="0" top="0" width="{StageW}" height="{ContentH - Status}" backgroundImage="stage_card.png" sliceBorder="20" horizontalAlignment="stretch" verticalAlignment="stretch"/>
        """;
    }

    /// <summary>
    /// The ten band equalizer over the lower part of the stage (hidden until the transport's equalizer key): on / off, the
    /// preset stepper and one fader per band. The faders follow the player's equalizer, whoever changes it; taking hold of
    /// one, or choosing a preset, switches the equalizer on (it starts off, and moving faders would otherwise do nothing).
    /// </summary>
    private string EqualizerDrawer(int inset)
    {
        if (_d.Retro is not { Equalizer: true } retro)
            return string.Empty;
        var f = _d.Font;
        var amber = retro.RailInkOn;
        var dim = retro.RailInkOff;
        var width = StageW - 2 * inset;
        const int Step = 25;
        string[] bands = ["31", "62", "125", "250", "500", "1K", "2K", "4K", "8K", "16K"];
        var faders = new StringBuilder();
        for (var i = 0; i < bands.Length; i++)
        {
            var n = i + 1;
            faders.AppendLine($"""
                      <SLIDER id="eqBand{n}" left="{i * Step + 5}" top="0" width="14" height="86" direction="vertical" min="-14" max="14" tiled="true"
                          backgroundImage="eqd_track.png" foregroundImage="eqd_fill.png" thumbImage="eqd_thumb.png" thumbHoverImage="eqd_thumb_hover.png"
                          value="wmpprop:eq.gainLevel{n}" value_onchange="eq.gainLevel{n} = eqBand{n}.value;" ondragbegin="eq.bypass = false;" upToolTip="{bands[i]} Hz"/>
                      <TEXT left="{i * Step}" top="90" width="{Step - 1}" justification="center" value="{bands[i]}" fontType="{f}" fontSize="7" foregroundColor="{dim}"/>
            """);
        }

        return $"""
                <SUBVIEW id="svEq" left="{inset}" top="{ContentH - Status - inset - EqualizerH}" width="{width}" height="{EqualizerH}" visible="false"
                    backgroundImage="deck_card.png" sliceBorder="20" horizontalAlignment="stretch" verticalAlignment="bottom">
                  <EQUALIZERSETTINGS id="eq"/>
                  <TEXT left="14" top="9" width="80" value="EQUALIZER" fontType="{f}" fontSize="8" fontStyle="bold" foregroundColor="{amber}"/>
                  <TEXT id="eqOnOff" left="96" top="9" width="34" value="wmpprop:eq.bypass ? 'OFF' : 'ON'" fontType="{f}" fontSize="8" fontStyle="bold" foregroundColor="{dim}"
                      hoverForegroundColor="{amber}" onClick="eq.bypass = !eq.bypass;"/>
                  <SUBVIEW left="{width - 150}" top="7" width="140" height="18" horizontalAlignment="right">
                    <TEXT id="eqPrev" left="0" top="0" width="16" justification="center" value="&lt;" fontType="{f}" fontSize="9" fontStyle="bold" foregroundColor="{dim}"
                        hoverForegroundColor="{amber}" onClick="eq.bypass = false; eq.previousPreset();"/>
                    <TEXT left="18" top="2" width="104" justification="center" value="wmpprop:eq.currentPresetTitle" fontType="{f}" fontSize="8" foregroundColor="#FFFFFF"/>
                    <TEXT id="eqNext" left="124" top="0" width="16" justification="center" value="&gt;" fontType="{f}" fontSize="9" fontStyle="bold" foregroundColor="{dim}"
                        hoverForegroundColor="{amber}" onClick="eq.bypass = false; eq.nextPreset();"/>
                  </SUBVIEW>
                  <SUBVIEW left="{(width - 10 * Step) / 2}" top="36" width="{10 * Step}" height="104" horizontalAlignment="center">
        {faders}
                  </SUBVIEW>
                </SUBVIEW>
        """;
    }

    private const string EqualizerScript = """

        function ToggleEqualizer() {
            if (g_pane != "now") { ShowPane("now"); svEq.visible = true; }
            else svEq.visible = !svEq.visible;
            theme.savePreference("Equalizer", svEq.visible ? "open" : "closed");
            LightEqualizer();
        }

        // The equalizer button stays lit while the drawer is on screen.
        function LightEqualizer() {
            bEq.down = svEq.visible && g_pane == "now";
        }

        // The drawer is as it was left.
        function RestoreEqualizer() {
            if (theme.loadPreference("Equalizer") == "open") svEq.visible = true;
            LightEqualizer();
        }
        """;

    private string Menu(string id, int left, int width, string label, string menu) =>
        $"""<TEXT id="{id}" left="{left}" top="11" width="{width}" value="{label}" fontType="{_d.Font}" fontSize="8" fontStyle="{(Modern ? "normal" : "bold")}" foregroundColor="{_d.MenuInk}" hoverForegroundColor="{_d.MenuHover}" onClick="pardeh.command('menu', '{menu}');"/>""";

    private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private string Layout()
    {
        var d = _d;
        var f = d.Font;
        // PC retro looks: the wallpaper behind the playlist and stage, and the look's colours on the Library and list panes.
        var retro = d.Retro;
        var stageExtra = retro is null ? string.Empty : " resizeBackgroundImage=\"true\"";
        var listExtra = retro is null ? string.Empty : " backgroundImage=\"list_wall.png\" resizeBackgroundImage=\"true\"";
        var playlistExtra = retro is null ? string.Empty : $" transparentBackground=\"true\" {retro.PlaylistAttributes}";
        var paneExtra = retro?.PaneAttributes ?? string.Empty;
        var dropDownExtra = !Modern ? string.Empty : $" dropDownBackgroundImage=\"dropdown_frame.png\" foregroundColor=\"{(retro is { Dark: false } ? d.PanelText : "#FFFFFF")}\"";
        var tasks = retro is { Words: true } ? string.Empty : retro is { Lowercase: true } ? "tasks" : "Tasks";
        var inset = retro is { Framed: true } ? StageInset : 0;
        return $$"""
        <THEME id="Pardeh{{d.Name}}" title="Pardeh {{d.Name}}" author="Pardeh contributors" pardehMode="full">
          <VIEW id="vFull" width="{{W}}" height="{{H}}" minWidth="680" minHeight="460" resizable="true"
                titleBar="false" backgroundColor="none" scriptFile="fullmode.js"
                onLoad="OnLoad();" onClose="OnClose();">

            <PLAYER openstatechange="OnOpenState(NewState);"/>

            <!-- Top bar: emblem and menus (hidden while Pardeh's own menu bar is shown), the playlist chooser and window buttons. -->
            <SUBVIEW id="svTop" left="0" top="0" width="{{W}}" height="{{Top}}" horizontalAlignment="stretch">
              <SUBVIEW left="0" top="0" width="{{TopLeftW}}" height="{{Top}}" backgroundImage="top_left.png">
                <SUBVIEW id="svMenus" left="0" top="0" width="{{TopLeftW}}" height="{{Top}}" visible="wmpprop:!app.titleBarVisible">
                  {{Menu("mFile", 42, 30, "File", "file")}}
                  {{Menu("mView", 78, 34, "View", "view")}}
                  {{Menu("mPlay", 118, 30, "Play", "play")}}
                  {{Menu("mTools", 154, 36, "Tools", "tools")}}
                  {{Menu("mHelp", 196, 32, "Help", "help")}}
                </SUBVIEW>
                <TEXT left="42" top="11" width="220" value="Pardeh" visible="wmpprop:app.titleBarVisible" fontType="{{f}}" fontSize="9" fontStyle="bold" foregroundColor="{{d.MenuInk}}"/>
              </SUBVIEW>
              <SUBVIEW left="{{TopLeftW}}" top="0" width="{{W - TopLeftW - TopRightW}}" height="{{Top}}" backgroundImage="top_mid.png" backgroundTiled="true" horizontalAlignment="stretch"/>
              <SUBVIEW left="{{W - TopRightW}}" top="0" width="{{TopRightW}}" height="{{Top}}" backgroundImage="top_right.png" horizontalAlignment="right">
                <DROPDOWNPLAYLIST left="22" top="{{(Modern ? 7 : 10)}}" width="160" height="{{(Modern ? 24 : 19)}}"{{dropDownExtra}}/>
                <BUTTON left="{{TopRightW - 80}}" top="10" image="min.png" hoverImage="min_hover.png" downImage="min_down.png" upToolTip="Minimize" onClick="view.minimize();"/>
                <BUTTON left="{{TopRightW - 58}}" top="10" image="max.png" hoverImage="max_hover.png" downImage="max_down.png" upToolTip="Maximize" onClick="pardeh.command('maximize');"/>
                <BUTTON left="{{TopRightW - 36}}" top="10" image="close.png" hoverImage="close_hover.png" downImage="close_down.png" upToolTip="Close" onClick="view.close();"/>
              </SUBVIEW>
            </SUBVIEW>

            <!-- Task rail. -->
            <SUBVIEW id="svRail" left="0" top="{{Top}}" width="{{Rail}}" height="{{ContentH}}" backgroundImage="rail_tile.png" backgroundTiled="true" verticalAlignment="stretch">
              <SUBVIEW left="0" top="0" width="{{Rail}}" height="{{Cap}}" backgroundImage="rail_cap.png">
                <TEXT left="14" top="6" width="96" value="{{tasks}}" fontType="{{f}}" fontSize="8" fontStyle="bold" foregroundColor="{{d.CapInk}}"/>
              </SUBVIEW>
              <SUBVIEW id="svRailItems" left="0" top="{{Cap}}" width="{{Rail}}" height="{{8 * 43 + 8}}">
        {{RailItem("railNow", "now", "Now Playing", 0)}}
        {{RailItem("railLibrary", "library", "Media Library", 1)}}
        {{RailItem("railRip", "cdrip", "Copy from CD", 2)}}
        {{RailItem("railBurn", "burn", "Copy to CD&#10;or Device", 3)}}
        {{RailItem("railRadio", "radio", "Radio Tuner", 4)}}
        {{RailItem("railInfo", "info", "Info Center", 5)}}
        {{RailItem("railSkins", "skins", "Settings", 6)}}
              </SUBVIEW>
            </SUBVIEW>

            <!-- Right frame. -->
            <SUBVIEW left="{{W - Edge}}" top="{{Top}}" width="{{Edge}}" height="{{ContentH}}" backgroundImage="edge.png" backgroundTiled="true" horizontalAlignment="right" verticalAlignment="stretch"/>

            <!-- Content: one pane at a time. -->
            <SUBVIEW id="svContent" left="{{Rail}}" top="{{Top}}" width="{{ContentW}}" height="{{ContentH}}" backgroundColor="#000000"
                horizontalAlignment="stretch" verticalAlignment="stretch">

              <SUBVIEW id="paneNow" left="0" top="0" width="{{ContentW}}" height="{{ContentH}}" horizontalAlignment="stretch" verticalAlignment="stretch">
                <!-- Stage: video or visualization, with the title over it and the status line below. -->
        {{StageCard(inset)}}
                <SUBVIEW id="svStage" left="{{inset}}" top="{{inset}}" width="{{StageW - 2 * inset}}" height="{{ContentH - Status - 2 * inset}}" backgroundImage="stage_glow.png" backgroundColor="#000000"{{stageExtra}}
                    horizontalAlignment="stretch" verticalAlignment="stretch">
                  <WMPVIDEO id="vid" left="0" top="0" width="{{StageW - 2 * inset}}" height="{{ContentH - Status - 2 * inset}}" visible="false" backgroundColor="#000000"
                      horizontalAlignment="stretch" verticalAlignment="stretch"/>
                  <WMPEFFECTS id="vis" left="0" top="0" width="{{StageW - 2 * inset}}" height="{{ContentH - Status - 2 * inset}}" visible="true"
                      horizontalAlignment="stretch" verticalAlignment="stretch"/>
                  <TEXT id="tArtist" left="12" top="8" width="{{StageW - 24}}" fontType="{{f}}" fontSize="8" foregroundColor="{{d.ArtistInk}}" horizontalAlignment="stretch"
                      value="wmpprop:player.currentMedia ? player.currentMedia.getItemInfo('Author') : ''"/>
                  <TEXT id="tTitle" left="12" top="22" width="{{StageW - 24}}" fontType="{{f}}" fontSize="12" fontStyle="bold" foregroundColor="{{d.TitleInk}}" horizontalAlignment="stretch"
                      value="wmpprop:player.currentMedia ? player.currentMedia.name : 'Pardeh'"/>
                </SUBVIEW>
        {{EqualizerDrawer(inset)}}
                <SUBVIEW id="svStatus" left="0" top="{{ContentH - Status}}" width="{{StageW}}" height="{{Status}}" backgroundColor="{{d.StatusBack}}"
                    horizontalAlignment="stretch" verticalAlignment="bottom">
                  <BUTTON left="6" top="3" image="viz_prev.png" hoverImage="viz_prev_hover.png" upToolTip="Previous Visualization" onClick="vis.previous();"/>
                  <BUTTON left="24" top="3" image="viz_next.png" hoverImage="viz_next_hover.png" upToolTip="Next Visualization" onClick="vis.next();"/>
                  <TEXT left="46" top="4" width="{{StageW - 150}}" fontType="{{f}}" fontSize="8" fontStyle="bold" foregroundColor="{{d.StatusInk}}" horizontalAlignment="stretch"
                      value="wmpprop:player.status"/>
                  <TEXT left="{{StageW - 100}}" top="4" width="94" justification="right" horizontalAlignment="right" fontType="{{f}}" fontSize="8" foregroundColor="{{d.StatusSub}}"
                      value="wmpprop:vid.visible ? '' : vis.currentEffectTitle"/>
                </SUBVIEW>

                <!-- Playlist panel: cover and details, the list, total time. -->
                <SUBVIEW id="svList" left="{{StageW}}" top="0" width="{{ListW}}" height="{{ContentH}}" backgroundColor="{{d.Panel}}"{{listExtra}} horizontalAlignment="right" verticalAlignment="stretch">
                  <SUBVIEW left="0" top="0" width="{{ListW}}" height="24" backgroundImage="list_header.png" resizeBackgroundImage="true" horizontalAlignment="stretch">
                    <TEXT left="10" top="5" width="{{ListW - 20}}" horizontalAlignment="stretch" fontType="{{f}}" fontSize="8" fontStyle="bold" foregroundColor="{{d.PanelText}}" value="Now Playing"/>
                  </SUBVIEW>
        {{(retro is { Framed: true } ? $"""<SUBVIEW left="4" top="28" width="{ListW - 8}" height="96" backgroundImage="deck_card.png" sliceBorder="20" horizontalAlignment="stretch"/>""" : string.Empty)}}
                  <SUBVIEW left="10" top="34" width="84" height="84" backgroundImage="album_frame.png">
                    <SUBVIEW left="1" top="1" width="80" height="80" backgroundImage="WMPImage_AlbumArtLarge"/>
                  </SUBVIEW>
                  <TEXT left="104" top="36" width="{{ListW - 112}}" horizontalAlignment="stretch" height="30" fontType="{{f}}" fontSize="9" fontStyle="bold" foregroundColor="{{d.PanelText}}" wordWrap="true"
                      value="wmpprop:player.currentMedia ? player.currentMedia.name : 'Nothing playing'"/>
                  <TEXT left="104" top="70" width="{{ListW - 112}}" horizontalAlignment="stretch" fontType="{{f}}" fontSize="8" foregroundColor="{{d.PanelSub}}"
                      value="wmpprop:player.currentMedia ? player.currentMedia.getItemInfo('Author') : ''"/>
                  <TEXT left="104" top="86" width="{{ListW - 112}}" horizontalAlignment="stretch" fontType="{{f}}" fontSize="8" foregroundColor="{{d.PanelAccent}}"
                      value="wmpprop:player.currentMedia ? player.currentMedia.getItemInfo('Album') : ''"/>
                  <TEXT left="104" top="102" width="{{ListW - 112}}" horizontalAlignment="stretch" fontType="{{f}}" fontSize="8" foregroundColor="{{d.PanelAccent}}"
                      value="wmpprop:player.currentMedia ? player.currentMedia.getItemInfo('Genre') : ''"/>
                  <SUBVIEW left="8" top="127" width="{{ListW - 16}}" height="1" backgroundColor="{{d.Rule}}" horizontalAlignment="stretch"/>
                  <PLAYLIST id="ipl" left="4" top="132" width="{{ListW - 8}}" height="{{ContentH - 132 - 30}}" backgroundColor="{{d.Panel}}" foregroundColor="{{d.PanelText}}"{{playlistExtra}}
                      horizontalAlignment="stretch" verticalAlignment="stretch"/>
                  <SUBVIEW left="0" top="{{ContentH - 26}}" width="{{ListW}}" height="26" backgroundImage="list_footer.png" resizeBackgroundImage="true" horizontalAlignment="stretch" verticalAlignment="bottom">
                    <TEXT left="10" top="6" width="{{ListW - 20}}" horizontalAlignment="stretch" justification="right" fontType="{{f}}" fontSize="8" foregroundColor="{{d.PanelAccent}}"
                        value="wmpprop:player.currentMedia ? 'Total time: ' + player.currentMedia.durationString : ''"/>
                  </SUBVIEW>
                </SUBVIEW>
              </SUBVIEW>

              <SUBVIEW id="paneLibrary" left="0" top="0" width="{{ContentW}}" height="{{ContentH}}" visible="false" horizontalAlignment="stretch" verticalAlignment="stretch">
                <LIBRARY id="library" left="0" top="0" width="{{ContentW}}" height="{{ContentH}}" {{paneExtra}} horizontalAlignment="stretch" verticalAlignment="stretch"/>
              </SUBVIEW>

              <SUBVIEW id="paneRadio" left="0" top="0" width="{{ContentW}}" height="{{ContentH}}" visible="false" horizontalAlignment="stretch" verticalAlignment="stretch">
                <LISTPANE kind="radio" left="0" top="0" width="{{ContentW}}" height="{{ContentH}}" {{paneExtra}} horizontalAlignment="stretch" verticalAlignment="stretch"/>
              </SUBVIEW>

              <SUBVIEW id="paneBurn" left="0" top="0" width="{{ContentW}}" height="{{ContentH}}" visible="false" horizontalAlignment="stretch" verticalAlignment="stretch">
                <LISTPANE kind="device" left="0" top="0" width="{{ContentW}}" height="{{ContentH}}" {{paneExtra}} horizontalAlignment="stretch" verticalAlignment="stretch"/>
              </SUBVIEW>

              <SUBVIEW id="paneSkins" left="0" top="0" width="{{ContentW}}" height="{{ContentH}}" visible="false" horizontalAlignment="stretch" verticalAlignment="stretch">
                <LISTPANE kind="settings" left="0" top="0" width="{{ContentW}}" height="{{ContentH}}" horizontalAlignment="stretch" verticalAlignment="stretch"/>
              </SUBVIEW>

              <!-- Info Center: the current item's details and cover, large. -->
              <SUBVIEW id="paneInfo" left="0" top="0" width="{{ContentW}}" height="{{ContentH}}" visible="false" backgroundColor="{{d.InfoBack}}" horizontalAlignment="stretch" verticalAlignment="stretch">
                <TEXT left="24" top="18" width="400" value="Info Center" fontType="{{f}}" fontSize="14" fontStyle="bold" foregroundColor="{{d.PanelText}}"/>
                <SUBVIEW left="24" top="56" width="84" height="84" backgroundImage="album_frame.png">
                  <SUBVIEW left="1" top="1" width="80" height="80" backgroundImage="WMPImage_AlbumArtLarge"/>
                </SUBVIEW>
                <TEXT left="124" top="58" width="420" fontType="{{f}}" fontSize="11" fontStyle="bold" foregroundColor="{{d.PanelText}}"
                    value="wmpprop:player.currentMedia ? player.currentMedia.name : 'Nothing is playing.'"/>
                <TEXT left="124" top="82" width="420" fontType="{{f}}" fontSize="9" foregroundColor="{{d.PanelSub}}"
                    value="wmpprop:player.currentMedia &amp;&amp; player.currentMedia.getItemInfo('Author') ? 'Artist: ' + player.currentMedia.getItemInfo('Author') : ''"/>
                <TEXT left="124" top="100" width="420" fontType="{{f}}" fontSize="9" foregroundColor="{{d.PanelSub}}"
                    value="wmpprop:player.currentMedia &amp;&amp; player.currentMedia.getItemInfo('Album') ? 'Album: ' + player.currentMedia.getItemInfo('Album') : ''"/>
                <TEXT left="124" top="118" width="420" fontType="{{f}}" fontSize="9" foregroundColor="{{d.PanelSub}}"
                    value="wmpprop:player.currentMedia &amp;&amp; player.currentMedia.getItemInfo('Genre') ? 'Genre: ' + player.currentMedia.getItemInfo('Genre') : ''"/>
                <TEXT left="124" top="136" width="420" fontType="{{f}}" fontSize="9" foregroundColor="{{d.PanelAccent}}"
                    value="wmpprop:player.currentMedia ? 'Length: ' + player.currentMedia.durationString : ''"/>
              </SUBVIEW>

              <SUBVIEW id="paneSoon" left="0" top="0" width="{{ContentW}}" height="{{ContentH}}" visible="false" backgroundColor="{{d.InfoBack}}" horizontalAlignment="stretch" verticalAlignment="stretch">
                <TEXT id="soonTitle" left="24" top="18" width="400" value="" fontType="{{f}}" fontSize="14" fontStyle="bold" foregroundColor="{{d.PanelText}}"/>
                <TEXT id="soonText" left="24" top="50" width="520" value="" fontType="{{f}}" fontSize="8" foregroundColor="{{d.PanelSub}}"/>
              </SUBVIEW>
            </SUBVIEW>

            <!-- Bottom: the rail's corner and the transport. -->
            <SUBVIEW left="0" top="{{H - Transport}}" width="{{Rail}}" height="{{Transport}}" backgroundImage="corner.png" verticalAlignment="bottom"/>
            <SUBVIEW id="svTransport" left="{{Rail}}" top="{{H - Transport}}" width="{{W - Rail}}" height="{{Transport}}" horizontalAlignment="stretch" verticalAlignment="bottom">
              <SUBVIEW left="0" top="0" width="{{W - Rail - TransportRightW}}" height="{{Transport}}" backgroundImage="transport_mid.png" backgroundTiled="true" horizontalAlignment="stretch"/>
              <SUBVIEW left="{{W - Rail - TransportRightW}}" top="0" width="{{TransportRightW}}" height="{{Transport}}" backgroundImage="transport_right.png" horizontalAlignment="right">
                <TEXT id="time" left="16" top="42" width="117" justification="center" fontType="{{f}}" fontSize="9" fontStyle="bold" foregroundColor="{{d.LcdInk}}"
                    value="wmpprop:player.currentMedia ? player.controls.currentPositionString + ' / ' + player.currentMedia.durationString : '00:00'"/>
                <BUTTON left="{{TransportRightW - 86}}" top="38" image="skinmode.png" hoverImage="skinmode_hover.png" downImage="skinmode_down.png" upToolTip="Switch to Skin Mode" onClick="pardeh.command('skinMode');"/>
                <BUTTON left="{{TransportRightW - 20}}" top="{{Transport - 20}}" width="16" height="16" cursor="sizenwse" upToolTip="" onmousedown="view.size('bottomright');"/>
              </SUBVIEW>

              <SEEKSLIDER id="seek" left="12" top="6" width="{{W - Rail - 24}}" height="14" horizontalAlignment="stretch"
                  backgroundImage="seek_track.png" foregroundImage="seek_fill.png" thumbImage="seek_thumb.png" thumbHoverImage="seek_thumb_hover.png"
                  tiled="true" slide="false" borderSize="0" direction="horizontal"/>

              <PLAYBUTTON left="14" top="27" image="play.png" hoverImage="play_hover.png" downImage="play_down.png" disabledImage="play_disabled.png" upToolTip="Play"/>
              <PAUSEBUTTON left="14" top="27" visible="wmpenabled:player.controls.pause" image="pause.png" hoverImage="pause_hover.png" downImage="pause_down.png" disabledImage="pause_disabled.png" upToolTip="Pause"/>
              <STOPBUTTON left="68" top="36" image="stop.png" hoverImage="stop_hover.png" downImage="stop_down.png" disabledImage="stop_disabled.png" upToolTip="Stop"/>
              <PREVBUTTON left="102" top="36" image="prev.png" hoverImage="prev_hover.png" downImage="prev_down.png" disabledImage="prev_disabled.png" upToolTip="Previous"/>
              <NEXTBUTTON left="134" top="36" image="next.png" hoverImage="next_hover.png" downImage="next_down.png" disabledImage="next_disabled.png" upToolTip="Next"/>
              <MUTEBUTTON id="bMute" left="178" top="37" image="mute.png" hoverImage="mute_hover.png" downImage="muted.png" upToolTip="Mute" downToolTip="Sound"/>
              <VOLUMESLIDER left="208" top="44" width="80" height="14" backgroundImage="volume_track.png" foregroundImage="volume_fill.png"
                  thumbImage="volume_thumb.png" thumbHoverImage="volume_thumb_hover.png" borderSize="4" direction="horizontal"/>
              <SHUFFLEBUTTON left="300" top="38" image="shuffle.png" hoverImage="shuffle_hover.png" downImage="shuffle_down.png" upToolTip="Turn Shuffle On" downToolTip="Turn Shuffle Off"/>
              <REPEATBUTTON left="330" top="38" image="repeat.png" hoverImage="repeat_hover.png" downImage="repeat_down.png" upToolTip="Turn Repeat On" downToolTip="Turn Repeat Off"/>
              <BUTTON id="bEq" left="360" top="38" image="eq.png" hoverImage="eq_hover.png" downImage="eq_down.png" upToolTip="{{(retro is { Equalizer: true } ? "Equalizer" : "Enhancements")}}" onClick="{{(retro is { Equalizer: true } ? "ToggleEqualizer();" : "pardeh.command('menu', 'view');")}}"/>
            </SUBVIEW>
          </VIEW>
        </THEME>
        """;
    }

    private const string Script = """
        // Pardeh designed full mode — original script.
        var g_pane = "";
        var RAIL = { now: "railNow", library: "railLibrary", cdrip: "railRip", burn: "railBurn", radio: "railRadio", info: "railInfo", skins: "railSkins" };
        var SOON = { cdrip: "Copy from CD" };

        function OnLoad() {
            var pane = theme.loadPreference("Pane");
            ApplyPages();
            ShowPane(pane && RAIL[pane] && !IsHidden(pane) ? pane : "now");
            if (typeof RestoreEqualizer != "undefined") RestoreEqualizer();
            OnOpenState(player.openState);
        }

        function OnClose() {
            theme.savePreference("Pane", g_pane);
        }

        // The playlist panel's width (the host's drag handle on its left edge): the stage and what is under it take what is left.
        function SetListWidth(width) {
            var change = width - svList.width;
            if (change == 0) return;
            svList.width = width;
            svStage.width = svStage.width - change;
            svStatus.width = svStatus.width - change;
            if (typeof svEq != "undefined") svEq.width = svEq.width - change;
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
            for (var key in RAIL) {
                eval(RAIL[key]).down = (key == name);
                if (typeof RAIL_ON != "undefined") eval(RAIL[key] + "Label").foregroundColor = key == name ? RAIL_ON : RAIL_OFF;
            }
            if (typeof LightEqualizer == "function") LightEqualizer();
        }

        function OnOpenState(state) {
            if (state != osMediaOpen) return;
            var video = player.currentMedia && player.currentMedia.imageSourceWidth > 0;
            vid.visible = video;
            vis.visible = !video;
            tTitle.visible = !video;
            tArtist.visible = !video;
            if (video && g_pane != "now") ShowPane("now");
        }
        """;
}
