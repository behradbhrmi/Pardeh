using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media;
using Pardeh.Core.LyricsKit;
using Pardeh.Skinning.Runtime;
using Pardeh.Skinning.Runtime.Elements;

namespace Pardeh.UI.Rendering;

/// <summary>Draws the LYRICS element (the lyrics view of Now Playing in the phone looks).</summary>
internal sealed partial class SkinRenderer
{
    private readonly ConditionalWeakTable<LyricsElement, LyricsPaint> _lyricsPaint = new();

    /// <summary>What the lyrics view keeps between frames: the measured lines and a few brushes, so that a frame allocates next to nothing.</summary>
    internal sealed class LyricsPaint
    {
        public FormattedText?[] Lines = [];
        public bool[] Rtl = [];
        public Lyrics? For;
        public FormattedText?[]? TabText;
        public double TabLineHeight;
        public Lyrics? TabFor;
        public FormattedText? Accent;
        public int AccentLine = -1;
        public int FillChars = -1;
        public List<Rect> FillRects = [];
        public Rect NextRect;
        public ChordLine?[] Chorded = []; // lines drawn with chords above the words (see SkinRenderer.Chords.cs)
        public readonly Dictionary<(string, double, bool, int), FormattedText> Texts = [];
    }

    /// <summary>The block direction of lyrics: the majority of their lines, else left to right.</summary>
    internal static FlowDirection FlowOf(LyricsTextDirection line, LyricsTextDirection block) =>
        (line == LyricsTextDirection.Neutral ? block : line) == LyricsTextDirection.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    private static Typeface LyricsTypeface(LyricsElement element, bool bold) =>
        new(FontFamily.Parse(element.FontFamily + ", Segoe UI, Arial"), FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal);

    private static Color Ink(LyricsElement element, double alpha = 1)
    {
        var c = ToColor(element.InkColor);
        return Color.FromArgb((byte)Math.Round(255 * Math.Clamp(alpha, 0, 1)), c.R, c.G, c.B);
    }

    private static Color Accent(LyricsElement element, double alpha = 1)
    {
        var c = ToColor(element.AccentColor);
        return Color.FromArgb((byte)Math.Round(255 * Math.Clamp(alpha, 0, 1)), c.R, c.G, c.B);
    }

    private static Color Plate(LyricsElement element, int alpha)
    {
        var c = ToColor(element.PlateColor);
        return Color.FromArgb((byte)alpha, c.R, c.G, c.B);
    }

    /// <summary>Measures every line for the element's width and text size, once per lyrics / size / width.</summary>
    internal static void LayOutLyrics(LyricsElement element, Lyrics lyrics, LyricsPaint paint)
    {
        var size = element.LineFontSize;
        var wrap = element.WrapWidth;
        var typeface = LyricsTypeface(element, bold: true);
        var block = lyrics.Direction;
        var ink = new SolidColorBrush(ToColor(element.InkColor));
        var count = lyrics.Lines.Count;
        var lines = new FormattedText?[count];
        var rtl = new bool[count];
        var heights = new double[count];
        var culture = CultureInfo.CurrentUICulture;
        var chorded = new ChordLine?[count];
        for (var i = 0; i < count; i++)
        {
            var line = lyrics.Lines[i];
            if (element.ShowsChords && line.Chords is not null)
            {
                chorded[i] = LayOutChordLine(element, lyrics, line, FlowOf(line.Direction, block));
                rtl[i] = chorded[i]!.RightToLeft;
                heights[i] = chorded[i]!.Height + size * 0.5;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line.Text))
            {
                heights[i] = size * 0.95;
                continue;
            }

            var flow = FlowOf(line.Direction, block);
            rtl[i] = flow == FlowDirection.RightToLeft;
            var text = new FormattedText(line.Text, culture, flow, typeface, size, ink)
            {
                MaxTextWidth = wrap,
                TextAlignment = element.CenterLines ? TextAlignment.Center : TextAlignment.Start,
            };
            lines[i] = text;
            heights[i] = text.Height + size * 0.5;
        }

        paint.Lines = lines;
        paint.Chorded = chorded;
        paint.Rtl = rtl;
        paint.For = lyrics;
        paint.AccentLine = -1;
        paint.FillChars = -1;
        element.SetLayout(lyrics, heights);
    }

    private FormattedText CachedText(LyricsPaint paint, string text, double size, bool bold, Color color, double maxWidth = 0, TextAlignment align = TextAlignment.Left)
    {
        var key = (text, size, bold, color.ToUInt32().GetHashCode() ^ (int)(maxWidth * 7) ^ ((int)align << 28));
        if (!paint.Texts.TryGetValue(key, out var ft))
        {
            if (paint.Texts.Count > 80)
                paint.Texts.Clear();
            ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, UiTheme.Typeface(bold), size, new SolidColorBrush(color));
            if (maxWidth > 0)
            {
                ft.MaxTextWidth = maxWidth;
                ft.TextAlignment = align;
            }

            paint.Texts[key] = ft;
        }

        return ft;
    }

    private void DrawLyrics(DrawingContext context, LyricsElement element, SkinView view)
    {
        var paint = _lyricsPaint.GetValue(element, _ => new LyricsPaint());
        var mode = element.Mode;
        var feed = element.Feed;
        var w = element.Width;
        var h = element.Height;
        if (w <= 0 || h <= 0)
            return;
        var zones = element.Zones;
        if (element.BackColor is { } back)
            context.FillRectangle(new SolidColorBrush(ToColor(back)), new Rect(0, 0, w, h));

        switch (mode)
        {
            case LyricsMode.Synced or LyricsMode.Plain when feed?.Current is { } lyrics:
                if (!ReferenceEquals(paint.For, lyrics) || element.NeedsLayout(lyrics))
                    LayOutLyrics(element, lyrics, paint);
                DrawLyricLines(context, element, lyrics, paint, mode == LyricsMode.Synced);
                if (element.HasToolbar)
                    DrawLyricsToolbar(context, element, paint, zones);
                if (zones.FirstOrDefault(z => z.Kind == LyricsZoneKind.Follow) is { Kind: LyricsZoneKind.Follow } follow)
                    DrawLyricsPill(context, element, paint, follow, element.LabelOf(LyricsZoneKind.Follow), primary: false, small: true);
                break;
            case LyricsMode.Tab when feed?.Current is { Tab: { } tab } lyrics:
                if (paint.TabText is null || !ReferenceEquals(paint.TabFor, lyrics) || element.NeedsTabLayout(lyrics))
                    LayOutTab(element, lyrics, tab, paint);
                DrawTab(context, element, paint);
                if (element.HasToolbar)
                    DrawLyricsToolbar(context, element, paint, zones);
                break;
            case LyricsMode.Loading:
                DrawLyricsSpinner(context, element, paint);
                break;
            default:
                DrawLyricsMessage(context, element, paint, mode, zones);
                break;
        }
    }

    // ---- Guitar tabs: monospaced, left to right, wrapped by system (TabNotation.Wrap) -----------------------

    private const double TabMargin = 8;
    private const double TabTop = 30; // under the source label

    private static readonly Typeface TabFace = new(FontFamily.Parse("Cascadia Mono, Consolas, DejaVu Sans Mono, Droid Sans Mono, monospace"));

    internal static void LayOutTab(LyricsElement element, Lyrics lyrics, string tab, LyricsPaint paint)
    {
        var ink = new SolidColorBrush(ToColor(element.InkColor));
        FormattedText Make(string text, double size) => new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, TabFace, size, ink);

        var size = Math.Max(8, element.LineFontSize * 0.82);
        var room = Math.Max(40, element.Width - 2 * TabMargin);
        var advance = Make("0000000000", size).Width / 10;
        // A narrow view gets smaller type before it gets very short systems.
        const int wanted = 34;
        if (room / advance < wanted)
        {
            size = Math.Max(8, size * (room / advance) / wanted);
            advance = Make("0000000000", size).Width / 10;
        }

        var lines = TabNotation.Wrap(tab, (int)(room / Math.Max(1, advance)));
        paint.TabLineHeight = Math.Ceiling(size * 1.3);
        paint.TabText = lines.Select(line => line.Length == 0 ? null : Make(line, size)).ToArray();
        paint.TabFor = lyrics;
        element.SetTabLayout(lyrics, TabTop + paint.TabText.Length * paint.TabLineHeight + 12);
    }

    private static void DrawTab(DrawingContext context, LyricsElement element, LyricsPaint paint)
    {
        var textH = element.TextHeight;
        var lineH = paint.TabLineHeight;
        var scroll = element.ScrollY;
        var lines = paint.TabText!;
        using var clip = context.PushClip(new Rect(0, 0, element.Width, textH));
        for (var i = Math.Max(0, (int)((scroll - TabTop) / lineH)); i < lines.Length; i++)
        {
            var y = TabTop + i * lineH - scroll;
            if (y > textH)
                break;
            if (lines[i] is { } line)
                context.DrawText(line, new Point(TabMargin, y));
        }
    }

    private void DrawLyricLines(DrawingContext context, LyricsElement element, Lyrics lyrics, LyricsPaint paint, bool synced)
    {
        var textH = element.TextHeight;
        var scroll = element.ScrollY;
        var tops = element.Tops;
        var heights = element.Heights;
        var first = LyricsScroll.FirstVisible(tops, heights, scroll);
        var current = synced ? element.CurrentLine : -1;
        var fade = Math.Min(64, textH * 0.3);
        var size = element.LineFontSize;
        var x0 = (element.Width - element.WrapWidth) / 2;
        var cx = element.Width / 2;

        using (context.PushClip(new Rect(0, 0, element.Width, textH)))
        {
            for (var i = first; i < tops.Count; i++)
            {
                var y = tops[i] - scroll;
                if (y > textH)
                    break;
                var text = paint.Lines[i];
                var chords = i < paint.Chorded.Length ? paint.Chorded[i] : null;
                if (text is null && chords is null)
                    continue;

                var emphasis = synced ? element.Emphasis(i) : 0;
                var lineH = chords?.Height ?? text!.Height;
                var mid = y + lineH / 2;
                var edge = LyricsScroll.EdgeFade(mid, textH, fade);
                // Timed lyrics: the current line is bright and large, the rest dim; plain lyrics are all the same.
                var alpha = (synced ? 0.42 + 0.58 * emphasis : 0.94) * edge;
                if (alpha < 0.02)
                    continue;

                var scale = 1 + (LyricsElement.HighlightScale - 1) * emphasis;
                var pivotX = element.CenterLines ? cx : paint.Rtl[i] ? x0 + element.WrapWidth : x0;
                var matrix = Matrix.CreateTranslation(-pivotX, -mid) * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(pivotX, mid);
                using (context.PushOpacity(alpha))
                using (context.PushTransform(matrix))
                {
                    if (chords is not null)
                        DrawChordLine(context, chords, new Point(x0, y));
                    else if (i == current)
                        DrawCurrentLine(context, element, lyrics, paint, i, text!, new Point(x0, y));
                    else
                        context.DrawText(text!, new Point(x0, y));
                }
            }
        }
    }

    /// <summary>The current line in the accent colour; with word timings only the part sung so far is filled (karaoke).</summary>
    private void DrawCurrentLine(DrawingContext context, LyricsElement element, Lyrics lyrics, LyricsPaint paint, int index, FormattedText text, Point origin)
    {
        if (paint.AccentLine != index || paint.Accent is null)
        {
            paint.AccentLine = index;
            paint.FillChars = -1;
            paint.Accent = new FormattedText(lyrics.Lines[index].Text, CultureInfo.CurrentUICulture, text.FlowDirection, LyricsTypeface(element, bold: true), element.LineFontSize, new SolidColorBrush(Accent(element)))
            {
                MaxTextWidth = element.WrapWidth,
                TextAlignment = element.CenterLines ? TextAlignment.Center : TextAlignment.Start,
            };
        }

        var line = lyrics.Lines[index];
        if (line.Words is not { Count: > 0 } words || element.Feed is null)
        {
            context.DrawText(paint.Accent, origin);
            return;
        }

        // Karaoke: the line dim, then the accent version clipped to the part that is already sung.
        context.DrawText(text, origin);
        var position = TimeSpan.FromSeconds(element.View.Session.Player.CurrentPosition) - TimeSpan.FromMilliseconds(lyrics.OffsetMs);
        var end = index + 1 < lyrics.Lines.Count ? lyrics.Lines[index + 1].Time : line.Time + TimeSpan.FromSeconds(4);
        var chars = LyricsScroll.FilledChars(words, end, position, line.Text.Length);
        var whole = (int)Math.Floor(chars);
        var size = element.LineFontSize;
        if (text.Height > size * 1.9)
        {
            // A wrapped line: no fill (the whole line is highlighted).
            context.DrawText(paint.Accent, origin);
            return;
        }

        // One visual line: the sung part is measured as the width of the text up to the sung character (X = width before it, Width = its own width).
        if (paint.FillChars != whole)
        {
            paint.FillChars = whole;
            var typeface = LyricsTypeface(element, bold: true);
            double Measure(int count) => count <= 0 ? 0 : new FormattedText(line.Text[..Math.Min(count, line.Text.Length)], CultureInfo.CurrentUICulture, text.FlowDirection, typeface, size, Brushes.White).Width;
            var before = Measure(whole);
            paint.NextRect = new Rect(before, 0, Math.Max(0, Measure(whole + 1) - before), 0);
        }

        var rtl = text.FlowDirection == FlowDirection.RightToLeft;
        var fill = paint.NextRect.X + paint.NextRect.Width * (chars - whole);
        var full = text.Width;
        var left = element.CenterLines ? (element.WrapWidth - full) / 2 : rtl ? element.WrapWidth - full : 0;
        var clip = rtl
            ? new Rect(left + full - fill, 0, fill + 2, text.Height + 4)
            : new Rect(left - 2, 0, fill + 2, text.Height + 4);
        using (context.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y)))
        using (context.PushClip(clip))
            context.DrawText(paint.Accent, default);
    }

    // ---- Toolbar, pills, messages -------------------------------------------------------------------------------

    private void DrawLyricsToolbar(DrawingContext context, LyricsElement element, LyricsPaint paint, IReadOnlyList<LyricsZone> zones)
    {
        var barTop = element.Height - element.ToolbarHeight;
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(46, 0, 0, 0)), new Rect(0, barTop, element.Width, element.ToolbarHeight));
        context.FillRectangle(new SolidColorBrush(Plate(element, 70)), new Rect(0, barTop, element.Width, 1));

        var tabShown = element.Mode == LyricsMode.Tab;
        var source = tabShown ? "Tab" : element.SourceText;
        if (source.Length > 0)
        {
            var label = !tabShown && element.Feed?.Current is { Synced: false } ? source + " - plain text" : source;
            var note = CachedText(paint, label, 10.5, false, Ink(element, element.HasChordBar ? 0.7 : 0.75));
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), null, new RoundedRect(new Rect(10, 6, note.Width + 12, note.Height + 4), 8));
            context.DrawText(note, new Point(16, 8));
        }

        if (element.HasChordBar)
            DrawChordBar(context, element, paint, zones, barTop - element.ChordBarHeight);

        foreach (var zone in zones)
        {
            if (zone.Kind is LyricsZoneKind.Follow or LyricsZoneKind.ChordDown or LyricsZoneKind.ChordKey or LyricsZoneKind.ChordUp or LyricsZoneKind.ChordSpelling or LyricsZoneKind.ChordToggle or LyricsZoneKind.TabToggle)
                continue;
            var pressed = element.PressedKind == zone.Kind;
            var rect = new Rect(zone.X, zone.Y, zone.W, zone.H);
            if (zone.Kind == LyricsZoneKind.OffsetLabel)
            {
                var t = CachedText(paint, element.OffsetText, 12.5, true, Ink(element, 0.92));
                context.DrawText(t, new Point(rect.X + (rect.Width - t.Width) / 2, rect.Y + (rect.Height - t.Height) / 2));
                continue;
            }

            DrawLyricsButton(context, element, paint, zone.Kind, rect, pressed);
        }
    }

    private void DrawLyricsButton(DrawingContext context, LyricsElement element, LyricsPaint paint, LyricsZoneKind kind, Rect rect, bool pressed)
    {
        var center = rect.Center;
        var ink = Ink(element, pressed ? 1 : 0.95);
        if (pressed)
            context.DrawEllipse(new SolidColorBrush(Plate(element, 60)), null, center, 17, 17);

        if (element.FluentIcons)
        {
            switch (kind)
            {
                case LyricsZoneKind.OffsetMinus: DrawFluentIcon(context, "subtract", center, 22, ink); break;
                case LyricsZoneKind.OffsetPlus: DrawFluentIcon(context, "add", center, 22, ink); break;
                case LyricsZoneKind.SizeMinus: DrawFluentIcon(context, "text_font_size", center, 17, ink); break;
                case LyricsZoneKind.SizePlus: DrawFluentIcon(context, "text_font_size", center, 25, ink); break;
                case LyricsZoneKind.More: DrawFluentIcon(context, "more_horizontal", center, 24, ink); break;
            }

            return;
        }

        // Vintage looks: a small glossy plate with a bold glyph, like the other buttons of the era.
        var plate = new Rect(center.X - 15, center.Y - 13, 30, 26);
        var top = Plate(element, pressed ? 70 : 150);
        var bottom = Plate(element, pressed ? 110 : 60);
        context.DrawRectangle(
            new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(top, 0), new GradientStop(bottom, 1) } },
            new Pen(new SolidColorBrush(Plate(element, 190)), 1), new RoundedRect(plate, 7));
        var glyphInk = Ink(element, 1);
        switch (kind)
        {
            case LyricsZoneKind.OffsetMinus: Glyph("-", 20, true); break;
            case LyricsZoneKind.OffsetPlus: Glyph("+", 20, true); break;
            case LyricsZoneKind.SizeMinus: Glyph("A", 12, true); break;
            case LyricsZoneKind.SizePlus: Glyph("A", 19, true); break;
            case LyricsZoneKind.More:
                for (var d = -1; d <= 1; d++)
                    context.DrawEllipse(new SolidColorBrush(glyphInk), null, new Point(center.X + d * 7, center.Y), 2.2, 2.2);
                break;
        }

        void Glyph(string text, double size, bool bold)
        {
            var t = CachedText(paint, text, size, bold, glyphInk);
            context.DrawText(t, new Point(center.X - t.Width / 2, center.Y - t.Height / 2));
        }
    }

    private static void DrawFluentIcon(DrawingContext context, string name, Point center, double size, Color ink)
    {
        using (context.PushTransform(Matrix.CreateScale(size / 24, size / 24) * Matrix.CreateTranslation(center.X - size / 2, center.Y - size / 2)))
            context.DrawGeometry(new SolidColorBrush(ink), null, FluentIcons.Geo(name));
    }

    private void DrawLyricsPill(DrawingContext context, LyricsElement element, LyricsPaint paint, LyricsZone zone, string label, bool primary, bool small)
    {
        var rect = new Rect(zone.X, zone.Y, zone.W, zone.H);
        var pressed = element.PressedKind == zone.Kind;
        var radius = rect.Height / 2;
        var fill = primary ? Plate(element, pressed ? 110 : 80) : Plate(element, pressed ? 90 : 55);
        var back = new SolidColorBrush(Color.FromArgb(small ? (byte)110 : (byte)70, 0, 0, 0));
        context.DrawRectangle(back, null, new RoundedRect(rect, radius));
        context.DrawRectangle(new SolidColorBrush(fill), new Pen(new SolidColorBrush(primary ? Accent(element, 0.9) : Plate(element, 120)), 1), new RoundedRect(rect, radius));
        var text = CachedText(paint, label, small ? 13 : 14.5, true, Ink(element, 1));
        context.DrawText(text, new Point(rect.X + (rect.Width - text.Width) / 2, rect.Y + (rect.Height - text.Height) / 2));
    }

    private void DrawLyricsSpinner(DrawingContext context, LyricsElement element, LyricsPaint paint)
    {
        var center = new Point(element.Width / 2, element.Height / 2 - 16);
        var angle = element.Clock() % 900 / 900.0 * 2 * Math.PI;
        const double r = 13;
        var start = new Point(center.X + r * Math.Cos(angle), center.Y + r * Math.Sin(angle));
        var end = new Point(center.X + r * Math.Cos(angle + 4.4), center.Y + r * Math.Sin(angle + 4.4));
        var arc = new StreamGeometry();
        using (var g = arc.Open())
        {
            g.BeginFigure(start, false);
            g.ArcTo(end, new Size(r, r), 0, true, SweepDirection.Clockwise);
            g.EndFigure(false);
        }

        context.DrawEllipse(null, new Pen(new SolidColorBrush(Ink(element, 0.18)), 3), center, r, r);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(Accent(element)), 3, lineCap: PenLineCap.Round), arc);
        var text = CachedText(paint, "Looking for lyrics...", 14, false, Ink(element, 0.85));
        context.DrawText(text, new Point((element.Width - text.Width) / 2, center.Y + r + 12));
    }

    private void DrawLyricsMessage(DrawingContext context, LyricsElement element, LyricsPaint paint, LyricsMode mode, IReadOnlyList<LyricsZone> zones)
    {
        var (title, note) = mode switch
        {
            LyricsMode.Off => ("Lyrics are turned off", "Turn them on to see the words here."),
            LyricsMode.NoTrack => ("Nothing is playing", "Play a song to see its lyrics."),
            _ => ("No lyrics found", element.Feed?.OnlineEnabled == true
                ? "Search online, paste them or import a .lrc file."
                : "Online search is off. Paste the lyrics or import a .lrc file."),
        };
        var top = element.MessageTop;
        var w = element.Width - 32;
        var t = CachedText(paint, title, 17, true, Ink(element, 1), w, TextAlignment.Center);
        context.DrawText(t, new Point(16, top));
        var n = CachedText(paint, note, 12.5, false, Ink(element, 0.7), w, TextAlignment.Center);
        context.DrawText(n, new Point(16, top + t.Height + 4));
        foreach (var zone in zones)
            DrawLyricsPill(context, element, paint, zone, element.LabelOf(zone.Kind), primary: zone.Kind is LyricsZoneKind.Search or LyricsZoneKind.EnableLyrics, small: false);
    }
}
