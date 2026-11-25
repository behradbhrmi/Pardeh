using System.Diagnostics;
using Pardeh.Skinning.Imaging;
using Pardeh.Skinning.Model;

namespace Pardeh.Skinning.Runtime.Elements;

/// <summary>
/// &lt;GLYPHTEXT&gt;: a line of text drawn from a bitmap font sheet, as the Winamp classic skins do (text.bmp for the
/// song title, numbers.bmp for the clock). An opt-in extension element: WMP skins never use it.
/// Attributes: <c>value</c>, <c>glyphImage</c> (the sheet), <c>glyphSet</c> ("text" or "digits": which character sits in
/// which cell), <c>glyphWidth</c> / <c>glyphHeight</c> (cell size), <c>glyphSpacing</c> (extra pixels between glyphs),
/// <c>justification</c> and <c>scroll</c> (a title longer than the element scrolls, like Winamp's marquee).
/// </summary>
public sealed class GlyphTextElement(SkinView view, SkinNode node, SkinElement? parent) : SkinElement(view, node, parent), IPlayerSynced
{
    private const double ScrollPause = 1.5;   // seconds before a long title starts moving
    private const double ScrollSpeed = 12;    // pixels per second
    private const int ScrollGapCells = 5;     // blank cells between the end of the title and its repeat

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private string _shownText = string.Empty;

    public string Text => GetString("value") ?? string.Empty;

    public SkinBitmap? Sheet => Image("glyphImage");

    public int CellWidth => Math.Max(1, (int)GetDouble("glyphWidth", 5));

    public int CellHeight => Math.Max(1, (int)GetDouble("glyphHeight", 6));

    public int Spacing => (int)GetDouble("glyphSpacing");

    public string Justification => GetString("justification") ?? "left";

    public bool Scrolls => GetBool("scroll");

    private string GlyphSet => GetString("glyphSet") ?? "text";

    protected override IEnumerable<string> KnownProperties =>
        ["value", "glyphImage", "glyphSet", "glyphWidth", "glyphHeight", "glyphSpacing", "justification", "scroll"];

    public override bool HitTest(double x, double y) => false; // clicks fall through (the window can be dragged by it)

    /// <summary>Where the sheet's cell for <paramref name="c"/> is (cell column and row), or null when the font has no such glyph.</summary>
    public static (int Column, int Row)? FindCell(string glyphSet, char c)
    {
        if (glyphSet.Equals("digits", StringComparison.OrdinalIgnoreCase))
        {
            return c switch
            {
                >= '0' and <= '9' => (c - '0', 0),
                '-' => (11, 0), // only in the extended nums_ex.bmp
                _ => null,
            };
        }

        // Winamp's text.bmp: 31 cells per row, three rows.
        c = char.ToUpperInvariant(c);
        var index = TextRow0.IndexOf(c);
        if (index >= 0)
            return (index, 0);
        index = TextRow1.IndexOf(c);
        if (index >= 0)
            return (index, 1);
        index = TextRow2.IndexOf(c);
        return index >= 0 ? (index, 2) : null;
    }

    // Spaces are left out on purpose: they draw nothing (the display behind shows through).
    private const string TextRow0 = "ABCDEFGHIJKLMNOPQRSTUVWXYZ\"@";
    private const string TextRow1 = "0123456789….:()-'!_+\\/[]^&%,=$#";
    private const string TextRow2 = "ÅÖÄ?*";

    /// <summary>The pixel width of <paramref name="text"/> in this font.</summary>
    public double MeasureText(string text) => text.Length == 0 ? 0 : text.Length * (CellWidth + Spacing) - Spacing;

    /// <summary>
    /// The glyphs to draw now: source cell origin in the sheet and destination x in the element. Only glyphs whose
    /// cell lies inside the sheet are returned; spaces and unknown characters just advance.
    /// </summary>
    public IEnumerable<(int SourceX, int SourceY, double X)> Layout()
    {
        var sheet = Sheet;
        if (sheet is null)
            yield break;

        var text = Text;
        var set = GlyphSet;
        var advance = CellWidth + Spacing;
        var textWidth = MeasureText(text);
        var origin = 0.0;
        var scrolling = Scrolls && Width > 0 && textWidth > Width;
        if (scrolling)
        {
            var period = textWidth + ScrollGapCells * advance;
            var moving = Math.Max(0, _clock.Elapsed.TotalSeconds - ScrollPause) * ScrollSpeed;
            origin = -(moving % period);
            // The title, then (after a gap) the title again so the loop has no jump.
            foreach (var glyph in Glyphs(text, set, sheet, advance, origin))
                yield return glyph;
            foreach (var glyph in Glyphs(text, set, sheet, advance, origin + period))
                yield return glyph;
            yield break;
        }

        origin = Justification.ToLowerInvariant() switch
        {
            "right" when Width > 0 => Width - textWidth,
            "center" when Width > 0 => Math.Floor((Width - textWidth) / 2),
            _ => 0,
        };
        foreach (var glyph in Glyphs(text, set, sheet, advance, origin))
            yield return glyph;
    }

    private IEnumerable<(int SourceX, int SourceY, double X)> Glyphs(string text, string set, SkinBitmap sheet, int advance, double origin)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (FindCell(set, text[i]) is not { } cell)
                continue;
            var sx = cell.Column * CellWidth;
            var sy = cell.Row * CellHeight;
            if (sx + CellWidth > sheet.LogicalWidth || sy + CellHeight > sheet.LogicalHeight)
                continue;
            yield return (sx, sy, origin + i * advance);
        }
    }

    /// <summary>Restarts the marquee when the title changes; keeps the window redrawing while it moves.</summary>
    public void SyncWithPlayer()
    {
        var text = Text;
        if (text != _shownText)
        {
            _shownText = text;
            _clock.Restart();
        }

        if (Scrolls && Width > 0 && MeasureText(text) > Width)
            View.Invalidate();
    }
}
