using System.Globalization;
using System.Text;

namespace Pardeh.Core.LyricsKit;

/// <summary>Reading direction of a lyric line, from its first strong character.</summary>
public enum LyricsTextDirection
{
    /// <summary>No strong character (a blank line, digits, punctuation): follow the surrounding lines.</summary>
    Neutral,

    LeftToRight,

    /// <summary>Arabic, Persian, Hebrew and the other right-to-left scripts.</summary>
    RightToLeft,
}

/// <summary>Limits that keep a damaged or hostile lyrics file from costing memory or time.</summary>
public static class LyricsLimits
{
    /// <summary>Largest lyrics file or tag we read (bytes).</summary>
    public const int MaxBytes = 1024 * 1024;

    /// <summary>Most lines kept from one lyrics text (the rest is dropped).</summary>
    public const int MaxLines = 5000;

    /// <summary>Longest single line kept (characters).</summary>
    public const int MaxLineLength = 1000;
}

/// <summary>Where a <see cref="Lyrics"/> came from (the value of <see cref="Lyrics.Source"/>).</summary>
public static class LyricsSources
{
    public const string Override = "override";
    public const string Embedded = "embedded";
    public const string Sidecar = "sidecar";
    public const string OnlineCache = "online-cache";
    public const string Online = "online";

    /// <summary>A chord sheet next to the track (.cho, .chordpro, .crd, .txt with [Am] marks).</summary>
    public const string ChordFile = "chord-file";

    /// <summary>The user's own chords text for the track.</summary>
    public const string OwnChords = "own-chords";

    /// <summary>A tab only: no words were found, just a guitar tab.</summary>
    public const string TabFile = "tab-file";
}

/// <summary>One word of an enhanced LRC line (<c>&lt;00:12.30&gt;word</c>). The text keeps the spacing of the source.</summary>
public sealed record LyricWord(TimeSpan Time, string Text);

/// <summary>One line of lyrics. <see cref="Time"/> is <see cref="TimeSpan.Zero"/> for unsynchronised text.</summary>
public sealed class LyricLine
{
    public LyricLine(TimeSpan time, string text, IReadOnlyList<LyricWord>? words = null, IReadOnlyList<LyricChord>? chords = null)
    {
        Time = time;
        Text = text;
        Words = words is { Count: > 0 } ? words : null;
        Chords = chords is { Count: > 0 } ? chords : null;
        Direction = DetectDirection(text);
    }

    /// <summary>The chords over the line, in text order, else null.</summary>
    public IReadOnlyList<LyricChord>? Chords { get; }

    public TimeSpan Time { get; }

    /// <summary>The text of the line; empty for a gap or instrumental break.</summary>
    public string Text { get; }

    /// <summary>Word timing of an enhanced LRC line, else null. Renderers may ignore it.</summary>
    public IReadOnlyList<LyricWord>? Words { get; }

    public LyricsTextDirection Direction { get; }

    /// <summary>A gap: no words and no chords (a line of chords alone, an intro say, is not blank).</summary>
    public bool IsBlank => string.IsNullOrWhiteSpace(Text) && Chords is null;

    /// <summary>The direction of the first strong (letter) character: Arabic, Hebrew and related scripts are right to left.</summary>
    public static LyricsTextDirection DetectDirection(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.Value;
            if (value == 0x200F || value == 0x061C)
                return LyricsTextDirection.RightToLeft;
            if (value == 0x200E)
                return LyricsTextDirection.LeftToRight;
            if (!Rune.IsLetter(rune))
                continue;
            return IsRightToLeftCode(value) ? LyricsTextDirection.RightToLeft : LyricsTextDirection.LeftToRight;
        }

        return LyricsTextDirection.Neutral;
    }

    private static bool IsRightToLeftCode(int value) =>
        value is >= 0x0590 and <= 0x08FF       // Hebrew, Arabic, Syriac, Thaana, NKo, Samaritan, Mandaic, Arabic Extended-A
            or >= 0xFB1D and <= 0xFDFF         // presentation forms A
            or >= 0xFE70 and <= 0xFEFF         // presentation forms B
            or >= 0x10800 and <= 0x10FFF
            or >= 0x1E800 and <= 0x1EFFF;

    public override string ToString() => Text;
}

/// <summary>
/// The lyrics of one track: timed lines (<see cref="Synced"/>) or plain text lines (all at time zero).
/// The lines never change after creation; <see cref="OffsetMs"/> is the user's timing correction.
/// </summary>
public sealed class Lyrics
{
    private readonly LyricLine[] _lines;

    public Lyrics(IEnumerable<LyricLine> lines, bool synced)
    {
        _lines = lines.ToArray();
        Synced = synced && _lines.Length > 0;
    }

    public IReadOnlyList<LyricLine> Lines => _lines;

    /// <summary>True when the lines carry time stamps (sorted by time) and can follow playback.</summary>
    public bool Synced { get; }

    public string? Title { get; init; }

    public string? Artist { get; init; }

    public string? Album { get; init; }

    /// <summary>The LRC "by" tag: who made the lyrics file.</summary>
    public string? Author { get; init; }

    /// <summary>The track length from an LRC <c>[length:]</c> tag, if any.</summary>
    public TimeSpan? Length { get; init; }

    /// <summary>Where the lyrics came from, see <see cref="LyricsSources"/>.</summary>
    public string? Source { get; set; }

    /// <summary>
    /// The user's timing correction in milliseconds: a line shows when the position reaches <c>line.Time + OffsetMs</c>, so a
    /// positive value delays the lyrics and a negative one shows them sooner. A file's own <c>[offset:]</c> is already applied to
    /// the line times and is not repeated here.
    /// </summary>
    public int OffsetMs { get; set; }

    /// <summary>True when there is at least one line with text.</summary>
    public bool HasText => _lines.Any(l => !l.IsBlank);

    /// <summary>True when any line carries chords.</summary>
    public bool HasChords => _lines.Any(l => l.Chords is not null);

    /// <summary>Whether the sheet is written with flats (for the automatic spelling).</summary>
    public bool ChordsUseFlats => _usesFlats ??= ChordNotation.UsesFlats(this);

    private bool? _usesFlats;

    /// <summary>The user's transposition of the chords, in semitones (-11 to +11).</summary>
    public int Transpose { get; set; }

    /// <summary>The track's guitar tab as monospaced text (see <see cref="TabNotation"/>), else null. Tab-only tracks have no lines.</summary>
    public string? Tab { get; set; }

    /// <summary>Where the chords came from when that differs from <see cref="Source"/> (see <see cref="LyricsSources"/>), else null.</summary>
    public string? ChordSource { get; set; }

    /// <summary>The majority direction of the lines that have one, for laying out the whole block.</summary>
    public LyricsTextDirection Direction
    {
        get
        {
            var rtl = _lines.Count(l => l.Direction == LyricsTextDirection.RightToLeft);
            var ltr = _lines.Count(l => l.Direction == LyricsTextDirection.LeftToRight);
            return rtl == 0 && ltr == 0 ? LyricsTextDirection.Neutral : rtl > ltr ? LyricsTextDirection.RightToLeft : LyricsTextDirection.LeftToRight;
        }
    }

    /// <summary>The same lines with other descriptive fields (Source and OffsetMs are copied).</summary>
    public Lyrics WithInfo(string? title, string? artist, string? album) =>
        new(_lines, Synced) { Title = title, Artist = artist, Album = album, Author = Author, Length = Length, Source = Source, OffsetMs = OffsetMs, Transpose = Transpose, ChordSource = ChordSource, Tab = Tab };

    /// <summary>The time a line is shown at, with the user's offset (never negative).</summary>
    public TimeSpan TimeOf(int index)
    {
        var shown = _lines[index].Time + TimeSpan.FromMilliseconds(OffsetMs);
        return shown < TimeSpan.Zero ? TimeSpan.Zero : shown;
    }

    /// <summary>
    /// The index of the line to show at <paramref name="position"/>: the last line whose time has been reached (with
    /// <see cref="OffsetMs"/>), or -1 before the first line and for unsynchronised lyrics. Lines sharing one time (a text and its
    /// translation) give the first of the group.
    /// </summary>
    public int IndexAt(TimeSpan position)
    {
        if (!Synced)
            return -1;
        var target = position - TimeSpan.FromMilliseconds(OffsetMs);
        int lo = 0, hi = _lines.Length - 1, found = -1;
        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (_lines[mid].Time <= target)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        while (found > 0 && _lines[found - 1].Time == _lines[found].Time)
            found--;
        return found;
    }

    /// <summary>Plain, unsynchronised lyrics from text (one line per text line; blank lines are kept as gaps).</summary>
    public static Lyrics FromPlainText(string text, string? source = null)
    {
        var lines = new List<LyricLine>();
        foreach (var raw in SplitLines(text))
        {
            if (lines.Count >= LyricsLimits.MaxLines)
                break;
            lines.Add(new LyricLine(TimeSpan.Zero, Clean(raw)));
        }

        // Leading and trailing gaps carry nothing.
        while (lines.Count > 0 && lines[^1].IsBlank)
            lines.RemoveAt(lines.Count - 1);
        var skip = 0;
        while (skip < lines.Count && lines[skip].IsBlank)
            skip++;
        return new Lyrics(lines.Skip(skip), synced: false) { Source = source };
    }

    /// <summary>The lyrics as LRC text (header tags, then one <c>[mm:ss.xx]</c> line each); unsynchronised lyrics as header tags and text. The user's offset is written into the times.</summary>
    public string ToLrc(bool applyOffset = true)
    {
        var text = new StringBuilder();
        AppendTag(text, "ti", Title);
        AppendTag(text, "ar", Artist);
        AppendTag(text, "al", Album);
        AppendTag(text, "by", Author);
        if (Length is { } length)
            AppendTag(text, "length", FormatStamp(length, brackets: false, forceMillis: false));
        for (var i = 0; i < _lines.Length; i++)
        {
            var line = _lines[i];
            if (!Synced)
            {
                text.Append(ChordNotation.Inline(line.Text, line.Chords)).Append('\n');
                continue;
            }

            var time = applyOffset ? TimeOf(i) : line.Time;
            text.Append(FormatStamp(time, brackets: true, forceMillis: false));
            if (line.Words is { } words)
            {
                var shift = time - line.Time;
                foreach (var word in words)
                    text.Append('<').Append(FormatStamp(word.Time + shift, brackets: false, forceMillis: false)).Append('>').Append(word.Text);
            }
            else
            {
                text.Append(ChordNotation.Inline(line.Text, line.Chords));
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    /// <summary>The text of all lines, one per line, without time stamps.</summary>
    public string ToPlainText() => string.Join("\n", _lines.Select(l => ChordNotation.Inline(l.Text, l.Chords)));

    /// <summary>Time as <c>mm:ss.xx</c> (centiseconds), or <c>mm:ss.xxx</c> when the time is not a whole centisecond.</summary>
    internal static string FormatStamp(TimeSpan time, bool brackets, bool forceMillis)
    {
        var totalMs = (long)Math.Round(Math.Max(0, time.TotalMilliseconds));
        var minutes = totalMs / 60000;
        var seconds = totalMs / 1000 % 60;
        var ms = totalMs % 1000;
        var fraction = forceMillis || ms % 10 != 0
            ? ms.ToString("000", CultureInfo.InvariantCulture)
            : (ms / 10).ToString("00", CultureInfo.InvariantCulture);
        var stamp = string.Create(CultureInfo.InvariantCulture, $"{minutes:00}:{seconds:00}.{fraction}");
        return brackets ? "[" + stamp + "]" : stamp;
    }

    private static void AppendTag(StringBuilder text, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            text.Append('[').Append(key).Append(':').Append(value.Replace('\r', ' ').Replace('\n', ' ').Trim()).Append("]\n");
    }

    internal static IEnumerable<string> SplitLines(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n'))
                continue;
            yield return text[start..i];
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                i++;
            start = i + 1;
        }

        if (start <= text.Length && (start < text.Length || text.Length == 0))
            yield return text[start..];
    }

    /// <summary>Trims a line and drops control characters and over-long text.</summary>
    internal static string Clean(string line)
    {
        var text = line.Trim().Trim('﻿', '\u0000');
        if (text.Length > LyricsLimits.MaxLineLength)
            text = text[..LyricsLimits.MaxLineLength];
        if (text.Any(char.IsControl))
            text = new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return text;
    }
}
