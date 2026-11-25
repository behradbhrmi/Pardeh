using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Pardeh.Core.LyricsKit;
using Pardeh.Skinning.Runtime.Elements;

namespace Pardeh.UI.Rendering;

/// <summary>Lyric lines with chords above the words: each chord stands over the syllable it belongs to, long lines wrap between words.</summary>
internal sealed partial class SkinRenderer
{
    /// <summary>One measured piece: a chord (or none) over a bit of the words, at a place inside the line.</summary>
    internal sealed record ChordPiece(FormattedText? Chord, FormattedText? Words, double X, double Y, double Width, double ChordHeight);

    internal sealed record ChordLine(IReadOnlyList<ChordPiece> Pieces, double Height, bool RightToLeft);

    /// <summary>
    /// The pieces of a line. The text is cut into words (with their trailing space) and again where a chord starts; a piece is as wide
    /// as the wider of its words and its chord. A word's pieces stay on one row. Rows are centred or start-aligned like plain lines;
    /// right-to-left lines run from the right.
    /// </summary>
    internal static ChordLine LayOutChordLine(LyricsElement element, Lyrics lyrics, LyricLine line, FlowDirection flow)
    {
        var size = element.LineFontSize;
        var chordSize = Math.Max(10, size * 0.82);
        var wrap = element.WrapWidth;
        var words = LyricsTypeface(element, bold: true);
        var spelling = element.Feed?.ChordSpelling ?? ChordSpelling.Auto;
        var ink = new SolidColorBrush(ToColor(element.InkColor));
        var accent = new SolidColorBrush(Accent(element));
        var culture = CultureInfo.CurrentUICulture;
        var rtl = flow == FlowDirection.RightToLeft;

        // Word groups: [(chord, text)] where a group ends after a space.
        var chords = line.Chords!.OrderBy(c => c.At).ToList();
        var groups = new List<List<(string? Chord, string Text)>>();
        var group = new List<(string? Chord, string Text)>();
        var text = line.Text;
        var at = 0;
        var next = 0;
        while (at < text.Length)
        {
            var names = new List<string>();
            while (next < chords.Count && chords[next].At <= at)
                names.Add(ChordNotation.Display(chords[next++].Name, lyrics.Transpose, spelling, lyrics.ChordsUseFlats));
            var end = at + 1;
            var stop = next < chords.Count ? Math.Min(chords[next].At, text.Length) : text.Length;
            while (end < stop && !(char.IsWhiteSpace(text[end - 1]) && !char.IsWhiteSpace(text[end])))
                end++;
            group.Add((names.Count > 0 ? string.Join(" ", names) : null, text[at..end]));
            if (char.IsWhiteSpace(text[end - 1]) && (end >= text.Length || !char.IsWhiteSpace(text[end])))
            {
                groups.Add(group);
                group = [];
            }

            at = end;
        }

        if (group.Count > 0)
            groups.Add(group);
        // Chords after the last word (or a line of chords alone): each stands by itself.
        while (next < chords.Count)
            groups.Add([(ChordNotation.Display(chords[next++].Name, lyrics.Transpose, spelling, lyrics.ChordsUseFlats), string.Empty)]);

        FormattedText Make(string value, double fontSize, IBrush brush) => new(value, culture, flow, words, fontSize, brush);
        var wordHeight = Make("Ag", size, ink).Height;
        var chordHeight = Make("Am", chordSize, accent).Height;

        var rows = new List<List<(FormattedText? Chord, FormattedText? Words, double Width)>> { new() };
        var rowWidth = 0.0;
        foreach (var pieces in groups)
        {
            var made = pieces.Select(p =>
            {
                var chord = p.Chord is null ? null : Make(p.Chord, chordSize, accent);
                var under = p.Text.Length == 0 ? null : Make(p.Text, size, ink);
                var width = Math.Max(under?.WidthIncludingTrailingWhitespace ?? 0, chord is null ? 0 : chord.Width + size * 0.3);
                return (chord, under, width);
            }).ToList();
            var groupWidth = made.Sum(m => m.width);
            if (rowWidth > 0 && rowWidth + groupWidth > wrap)
            {
                rows.Add([]);
                rowWidth = 0;
            }

            rows[^1].AddRange(made);
            rowWidth += groupWidth;
        }

        var result = new List<ChordPiece>();
        var y = 0.0;
        foreach (var row in rows)
        {
            var width = row.Sum(p => p.Width);
            var hasChord = row.Any(p => p.Chord is not null);
            var hasWords = row.Any(p => p.Words is not null);
            var x = element.CenterLines ? Math.Max(0, (wrap - width) / 2) : rtl ? Math.Max(0, wrap - width) : 0;
            var run = 0.0;
            var above = hasChord ? chordHeight : 0;
            foreach (var (chord, under, pieceWidth) in row)
            {
                var left = rtl ? x + width - run - pieceWidth : x + run;
                result.Add(new ChordPiece(chord, under, left, y, pieceWidth, above));
                run += pieceWidth;
            }

            y += above + (hasWords ? wordHeight : 0);
        }

        return new ChordLine(result, y, rtl);
    }

    private static void DrawChordLine(DrawingContext context, ChordLine line, Point origin)
    {
        foreach (var piece in line.Pieces)
        {
            // FormattedText draws right-to-left text leftwards of its origin only inside a set width, so give each piece its box.
            if (piece.Chord is { } chord)
                context.DrawText(chord, new Point(origin.X + piece.X + (line.RightToLeft ? piece.Width - chord.Width : 0), origin.Y + piece.Y));
            if (piece.Words is { } words)
                context.DrawText(words, new Point(origin.X + piece.X + (line.RightToLeft ? piece.Width - words.WidthIncludingTrailingWhitespace : 0), origin.Y + piece.Y + piece.ChordHeight));
        }
    }

    /// <summary>The bar above the toolbar: transpose down / up (tap the key to go back to 0), spelling, hide or show.</summary>
    private void DrawChordBar(DrawingContext context, LyricsElement element, LyricsPaint paint, IReadOnlyList<LyricsZone> zones, double top)
    {
        context.FillRectangle(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), new Rect(0, top, element.Width, element.ChordBarHeight));
        context.FillRectangle(new SolidColorBrush(Plate(element, 50)), new Rect(0, top, element.Width, 1));
        foreach (var zone in zones)
        {
            if (zone.Kind is not (LyricsZoneKind.ChordDown or LyricsZoneKind.ChordKey or LyricsZoneKind.ChordUp or LyricsZoneKind.ChordSpelling or LyricsZoneKind.ChordToggle or LyricsZoneKind.TabToggle))
                continue;
            var rect = new Rect(zone.X, zone.Y, zone.W, zone.H).Deflate(new Thickness(2.5, 4));
            var pressed = element.PressedKind == zone.Kind;
            context.DrawRectangle(new SolidColorBrush(Plate(element, pressed ? 100 : 40)), new Pen(new SolidColorBrush(Plate(element, 110)), 1), new RoundedRect(rect, 9));
            var step = zone.Kind is LyricsZoneKind.ChordDown or LyricsZoneKind.ChordUp;
            var label = CachedText(paint, element.LabelOf(zone.Kind), step ? 18 : 12.5, true, Ink(element, 1));
            context.DrawText(label, new Point(rect.X + (rect.Width - label.Width) / 2, rect.Y + (rect.Height - label.Height) / 2));
        }
    }
}
