using System.Text;
using System.Text.RegularExpressions;

namespace Pardeh.Core.LyricsKit;

/// <summary>A chord over a lyric line: <see cref="At"/> is the character of the line's text it stands over (the text's length = after it).</summary>
public sealed record LyricChord(int At, string Name);

/// <summary>How transposed chords are spelled.</summary>
public enum ChordSpelling
{
    /// <summary>Flats when the sheet itself uses flats, else sharps; untransposed chords stay as written.</summary>
    Auto,

    Sharps,

    Flats,
}

/// <summary>
/// Chords written inside lyrics as <c>[Am]</c> marks (ChordPro and the common <c>.crd</c> / <c>.txt</c> sheets): recognising them, taking
/// them out of a line, reading the few ChordPro <c>{directives}</c> that matter, transposing, and attaching a chord sheet to timed lyrics.
/// </summary>
public static partial class ChordNotation
{
    /// <summary>The file extensions that hold chord sheets, in order of preference (a <c>.txt</c> counts only when it has chords).</summary>
    public static readonly string[] Extensions = [".cho", ".chordpro", ".crd", ".txt"];

    private static readonly string[] SharpNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    private static readonly string[] FlatNames = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"];

    // Root, accidental, quality, extensions such as 7, maj7, sus4, b5 or (add9), an optional bass note or figure.
    [GeneratedRegex(@"^(N\.?C\.?|[A-G](#|b|♯|♭)?(maj|min|mi|m|M|dim|aug|sus|add|\+|°|o|ø|Δ)?\d{0,2}((maj|sus|add|dim|aug|no|b|#|♭|♯|\+|-)\d{0,2})*(\([^()\[\]]{1,10}\))?(/([A-G](#|b|♯|♭)?|\d{1,2}))?)$", RegexOptions.CultureInvariant)]
    private static partial Regex ChordPattern();

    [GeneratedRegex(@"\[([^\[\]\r\n]{1,16})\]", RegexOptions.CultureInvariant)]
    private static partial Regex Bracketed();

    [GeneratedRegex(@"^\s*\{\s*([A-Za-z_]+)\s*(?:[:\s]\s*(.*?))?\s*\}\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Directive();

    /// <summary>True for a chord name such as <c>Am</c>, <c>F#m7b5</c>, <c>Bbmaj7</c>, <c>G/B</c> or <c>N.C.</c>.</summary>
    public static bool IsChord(string text) => text.Length is > 0 and <= 16 && ChordPattern().IsMatch(text);

    /// <summary>
    /// True when a lyrics text carries chords: at least two <c>[chord]</c> marks, or one together with a ChordPro directive. A lone
    /// "[A]" in ordinary lyrics is not enough.
    /// </summary>
    public static bool LooksLikeChords(string text)
    {
        var chords = 0;
        foreach (Match match in Bracketed().Matches(text))
        {
            if (IsChord(match.Groups[1].Value.Trim()) && ++chords >= 2)
                return true;
        }

        return chords == 1 && Lyrics.SplitLines(text).Any(line => Directive().IsMatch(line));
    }

    /// <summary>Takes the chord marks out of a line. Other bracketed text ("[Chorus]", "[x2]") stays. No chords: the line as it was.</summary>
    public static (string Text, IReadOnlyList<LyricChord>? Chords) Extract(string line)
    {
        if (!line.Contains('['))
            return (line, null);
        var text = new StringBuilder();
        var chords = new List<LyricChord>();
        var from = 0;
        foreach (Match match in Bracketed().Matches(line))
        {
            var name = match.Groups[1].Value.Trim();
            if (!IsChord(name))
                continue;
            text.Append(line, from, match.Index - from);
            chords.Add(new LyricChord(text.Length, name));
            from = match.Index + match.Length;
        }

        if (chords.Count == 0)
            return (line, null);
        text.Append(line, from, line.Length - from);
        var clean = text.ToString();
        var lead = clean.Length - clean.TrimStart().Length;
        clean = clean.Trim();
        return (clean, chords.Select(c => new LyricChord(Math.Clamp(c.At - lead, 0, clean.Length), c.Name)).ToList());
    }

    /// <summary>The line with its chords written back as <c>[Am]</c> marks.</summary>
    public static string Inline(string text, IReadOnlyList<LyricChord>? chords)
    {
        if (chords is null || chords.Count == 0)
            return text;
        var result = new StringBuilder();
        var from = 0;
        foreach (var chord in chords.OrderBy(c => c.At))
        {
            var at = Math.Clamp(chord.At, from, text.Length);
            result.Append(text, from, at - from).Append('[').Append(chord.Name).Append(']');
            from = at;
        }

        return result.Append(text, from, text.Length - from).ToString();
    }

    /// <summary>
    /// Turns a ChordPro text into lines the LRC reader understands: <c>{title}</c> and <c>{artist}</c> become LRC tags, comments become
    /// lines of their own, tab sections and every other directive and <c>#</c> remark are dropped.
    /// </summary>
    public static string Preprocess(string text)
    {
        var output = new List<string>();
        var inTab = false;
        foreach (var line in Lyrics.SplitLines(text))
        {
            if (line.TrimStart().StartsWith('#'))
                continue;
            if (Directive().Match(line) is not { Success: true } match)
            {
                if (!inTab)
                    output.Add(line);
                continue;
            }

            var value = match.Groups[2].Value.Replace('[', '(').Replace(']', ')');
            switch (match.Groups[1].Value.ToLowerInvariant())
            {
                case "title" or "t": output.Add($"[ti:{value}]"); break;
                case "artist": output.Add($"[ar:{value}]"); break;
                case "album": output.Add($"[al:{value}]"); break;
                case "comment" or "c" or "comment_italic" or "ci" or "comment_box" or "cb" or "highlight": output.Add(value); break;
                case "start_of_tab" or "sot": inTab = true; break;
                case "end_of_tab" or "eot": inTab = false; break;
                case "start_of_chorus" or "soc" when value.Length > 0: output.Add(value); break;
                case "end_of_chorus" or "eoc" or "end_of_verse" or "eov": output.Add(string.Empty); break;
            }
        }

        return string.Join("\n", output);
    }

    // ---- Transposing ------------------------------------------------------------------------------------

    private static int NoteIndex(char letter) => letter switch { 'C' => 0, 'D' => 2, 'E' => 4, 'F' => 5, 'G' => 7, 'A' => 9, 'B' => 11, _ => -1 };

    /// <summary>The chord moved by <paramref name="semitones"/> and spelled with sharps or flats (the bass note after "/" moves too).</summary>
    public static string Transpose(string chord, int semitones, bool flats)
    {
        if (chord.StartsWith('N'))
            return chord; // no chord
        var slash = chord.LastIndexOf('/');
        if (slash > 0 && slash + 1 < chord.Length && NoteIndex(chord[slash + 1]) >= 0)
            return MoveRoot(chord[..slash], semitones, flats) + "/" + MoveRoot(chord[(slash + 1)..], semitones, flats);
        return MoveRoot(chord, semitones, flats);
    }

    private static string MoveRoot(string chord, int semitones, bool flats)
    {
        if (chord.Length == 0 || NoteIndex(chord[0]) is not (>= 0 and var note))
            return chord;
        var length = 1;
        if (chord.Length > 1 && chord[1] is '#' or '♯')
        {
            note++;
            length = 2;
        }
        else if (chord.Length > 1 && chord[1] is 'b' or '♭')
        {
            note--;
            length = 2;
        }

        var moved = ((note + semitones) % 12 + 12) % 12;
        return (flats ? FlatNames : SharpNames)[moved] + chord[length..];
    }

    /// <summary>Whether a sheet is written with flats (more flat roots than sharp ones).</summary>
    public static bool UsesFlats(Lyrics lyrics)
    {
        int flat = 0, sharp = 0;
        foreach (var chord in lyrics.Lines.SelectMany(l => l.Chords ?? []))
        {
            if (chord.Name.Length < 2)
                continue;
            if (chord.Name[1] is 'b' or '♭')
                flat++;
            else if (chord.Name[1] is '#' or '♯')
                sharp++;
        }

        return flat > sharp;
    }

    /// <summary>The chord as shown: transposed and spelled the chosen way.</summary>
    public static string Display(string chord, int semitones, ChordSpelling spelling, bool sheetUsesFlats)
    {
        if (spelling == ChordSpelling.Auto && semitones % 12 == 0)
            return chord;
        return Transpose(chord, semitones, spelling == ChordSpelling.Flats || (spelling == ChordSpelling.Auto && sheetUsesFlats));
    }

    // ---- Attaching a sheet to timed lyrics --------------------------------------------------------------

    private static string Letters(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>
    /// Puts the chords of <paramref name="sheet"/> on the timed lines of <paramref name="lyrics"/> that have the same words (compared
    /// without punctuation, spacing and case; a line sung again gets its chords again). Null when fewer than half of the sheet's chorded
    /// word lines find their words: the two texts are then too different to merge.
    /// </summary>
    public static Lyrics? Attach(Lyrics lyrics, Lyrics sheet)
    {
        var byWords = new Dictionary<string, List<LyricLine>>(StringComparer.Ordinal);
        var chorded = 0;
        foreach (var line in sheet.Lines.Where(l => l.Chords is not null))
        {
            var key = Letters(line.Text);
            if (key.Length == 0)
                continue;
            chorded++;
            if (!byWords.TryGetValue(key, out var same))
                byWords[key] = same = [];
            same.Add(line);
        }

        if (chorded == 0)
            return null;

        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        var matched = new HashSet<LyricLine>();
        var lines = new List<LyricLine>(lyrics.Lines.Count);
        foreach (var line in lyrics.Lines)
        {
            var key = Letters(line.Text);
            if (key.Length == 0 || !byWords.TryGetValue(key, out var same))
            {
                lines.Add(line);
                continue;
            }

            // The n-th time the words come round takes the n-th such line of the sheet, then the last one again.
            var n = used.GetValueOrDefault(key);
            used[key] = n + 1;
            var source = same[Math.Min(n, same.Count - 1)];
            matched.Add(source);
            lines.Add(new LyricLine(line.Time, line.Text, null, MapChords(source, line.Text)));
        }

        // Lines of the sheet that were repeated there but sung once still count as found.
        var found = byWords.Where(pair => pair.Value.Any(matched.Contains)).Sum(pair => pair.Value.Count);
        if (found * 2 < chorded)
            return null;
        return new Lyrics(lines, lyrics.Synced)
        {
            Title = lyrics.Title, Artist = lyrics.Artist, Album = lyrics.Album, Author = lyrics.Author, Length = lyrics.Length,
            Source = lyrics.Source, OffsetMs = lyrics.OffsetMs,
        };
    }

    // Chord positions carried to a line whose punctuation or spacing differs: by counting letters before each chord.
    private static List<LyricChord> MapChords(LyricLine from, string to)
    {
        var result = new List<LyricChord>();
        foreach (var chord in from.Chords!)
        {
            var before = from.Text.Take(chord.At).Count(char.IsLetterOrDigit);
            var at = 0;
            for (var seen = 0; at < to.Length && seen < before; at++)
            {
                if (char.IsLetterOrDigit(to[at]))
                    seen++;
            }

            // Land on the next word's first letter, not on the space before it.
            while (at < to.Length && !char.IsLetterOrDigit(to[at]) && chord.At < from.Text.Length && char.IsLetterOrDigit(from.Text[chord.At]))
                at++;
            result.Add(new LyricChord(at, chord.Name));
        }

        return result;
    }
}
