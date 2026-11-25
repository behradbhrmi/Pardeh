using System.Globalization;
using System.Text;

namespace Pardeh.Core.LyricsKit;

/// <summary>
/// Reads LRC lyrics: <c>[mm:ss.xx]</c>, <c>[mm:ss.xxx]</c>, <c>[mm:ss]</c> time stamps (several per line), the tags <c>[ar:] [ti:] [al:] [by:]
/// [offset:] [length:]</c> and enhanced word stamps <c>&lt;mm:ss.xx&gt;</c>. Text without any time stamp is returned as plain
/// (unsynchronised) lyrics. Numbers always use the invariant culture.
/// </summary>
public static class LrcParser
{
    private static readonly HashSet<string> IgnoredTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "re", "ve", "tool", "id", "la", "au", "lr", "encoding",
    };

    /// <summary>
    /// Parses lyrics text. Returns null when it has no text at all. Lines are sorted by time (stable), a file's <c>[offset:]</c>
    /// is applied to the times and at most <see cref="LyricsLimits.MaxLines"/> lines are kept.
    /// </summary>
    public static Lyrics? Parse(string? text, string? source = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        if (text.Length > LyricsLimits.MaxBytes)
            text = text[..LyricsLimits.MaxBytes];
        text = text.TrimStart('﻿');
        var chorded = ChordNotation.LooksLikeChords(text);
        if (chorded)
            text = ChordNotation.Preprocess(text); // ChordPro directives

        string? title = null, artist = null, album = null, author = null;
        TimeSpan? length = null;
        var offsetMs = 0;
        var timed = new List<RawLine>();
        var untimed = new List<string>();

        foreach (var raw in Lyrics.SplitLines(text))
        {
            var line = raw.AsSpan().Trim();
            var stamps = new List<TimeSpan>();
            var consumedTag = false;
            var pos = 0;
            while (pos < line.Length && line[pos] == '[')
            {
                var close = line[pos..].IndexOf(']');
                if (close < 0)
                    break;
                var inner = line.Slice(pos + 1, close - 1);
                if (TryParseStamp(inner, out var stamp))
                {
                    stamps.Add(stamp);
                }
                else if (stamps.Count == 0 && TryMetaTag(inner, out var key, out var value))
                {
                    consumedTag = true;
                    switch (key)
                    {
                        case "ti": title = value; break;
                        case "ar": artist = value; break;
                        case "al": album = value; break;
                        case "by": author = value; break;
                        case "offset":
                            if (int.TryParse(value.Trim().TrimStart('+'), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ms))
                                offsetMs = Math.Clamp(ms, -3_600_000, 3_600_000);
                            break;
                        case "length":
                            length = ParseLength(value);
                            break;
                    }
                }
                else
                {
                    break;
                }

                pos += close + 1;
            }

            var rest = line[pos..].ToString();
            if (stamps.Count == 0)
            {
                // A header tag line has no text of its own; anything else is plain text.
                if (consumedTag && rest.Trim().Length == 0)
                    continue;
                if (untimed.Count < LyricsLimits.MaxLines + 200)
                    untimed.Add(rest);
                continue;
            }

            if (timed.Count >= LyricsLimits.MaxLines)
                continue;
            var (clean, words) = ParseWords(rest);
            foreach (var stamp in stamps)
            {
                if (timed.Count >= LyricsLimits.MaxLines)
                    break;
                timed.Add(new RawLine(stamp, clean, words is null ? null : (stamp - stamps[0], words)));
            }
        }

        if (timed.Count == 0)
        {
            var plain = Lyrics.FromPlainText(string.Join("\n", untimed), source);
            if (chorded)
                plain = new Lyrics(plain.Lines.Select(l => WithChords(l.Time, l.Text, null)), synced: false);
            return plain.HasText
                ? new Lyrics(plain.Lines, synced: false) { Title = title, Artist = artist, Album = album, Author = author, Length = length, Source = source }
                : null;
        }

        var shift = TimeSpan.FromMilliseconds(offsetMs);
        var lines = timed
            .Select(t => chorded && t.Text.Contains('[')
                ? WithChords(Clamp(t.Time - shift), Lyrics.Clean(t.Text), null) // word timing would no longer line up with the shorter text
                : new LyricLine(Clamp(t.Time - shift), Lyrics.Clean(t.Text), t.Words is { } w ? ShiftWords(w.Words, w.Shift - shift) : null))
            .OrderBy(l => l.Time) // LINQ OrderBy is stable: equal times keep the file order
            .ToList();
        var result = new Lyrics(lines, synced: true) { Title = title, Artist = artist, Album = album, Author = author, Length = length, Source = source };
        return result.HasText ? result : null;
    }

    private static LyricLine WithChords(TimeSpan time, string text, IReadOnlyList<LyricWord>? words)
    {
        var (clean, chords) = ChordNotation.Extract(text);
        return new LyricLine(time, clean, words, chords);
    }

    private sealed record RawLine(TimeSpan Time, string Text, (TimeSpan Shift, List<LyricWord> Words)? Words);

    private static TimeSpan Clamp(TimeSpan time) => time < TimeSpan.Zero ? TimeSpan.Zero : time;

    private static List<LyricWord> ShiftWords(List<LyricWord> words, TimeSpan shift) =>
        words.Select(w => new LyricWord(Clamp(w.Time + shift), w.Text)).ToList();

    private static bool TryMetaTag(ReadOnlySpan<char> inner, out string key, out string value)
    {
        key = value = string.Empty;
        var colon = inner.IndexOf(':');
        if (colon <= 0)
            return false;
        var name = inner[..colon].Trim().ToString().ToLowerInvariant();
        if (name is not ("ti" or "ar" or "al" or "by" or "offset" or "length") && !IgnoredTags.Contains(name))
            return false;
        key = name;
        value = inner[(colon + 1)..].Trim().ToString();
        return true;
    }

    private static TimeSpan? ParseLength(string value)
    {
        if (TryParseStamp(value.AsSpan(), out var stamp))
            return stamp;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds is >= 0 and < 86400
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    /// <summary>
    /// A time stamp without its brackets: <c>mm:ss</c>, <c>mm:ss.x</c> to <c>mm:ss.xxx</c> (also with <c>:</c> or <c>,</c> before the
    /// fraction) and <c>h:mm:ss.xx</c>.
    /// </summary>
    public static bool TryParseStamp(ReadOnlySpan<char> text, out TimeSpan time)
    {
        time = default;
        text = text.Trim();
        if (text.Length < 3 || text.Length > 14)
            return false;

        Span<Range> parts = stackalloc Range[4];
        // Split on ':' first; the last part may carry a '.' or ',' fraction.
        var count = 0;
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != ':')
                continue;
            if (count == 4)
                return false;
            parts[count++] = start..i;
            start = i + 1;
        }

        if (count is < 2 or > 3)
            return false;

        long hours = 0, minutes, seconds, fractionMs = 0;
        var last = text[parts[count - 1]];
        var dot = last.IndexOfAny('.', ',');
        ReadOnlySpan<char> secondsText = dot >= 0 ? last[..dot] : last;
        ReadOnlySpan<char> fractionText = dot >= 0 ? last[(dot + 1)..] : default;

        if (count == 2)
        {
            if (!TryDigits(text[parts[0]], 1, 4, out minutes) || !TryDigits(secondsText, 1, 2, out seconds))
                return false;
        }
        else if (dot >= 0)
        {
            // h:mm:ss.xx
            if (!TryDigits(text[parts[0]], 1, 3, out hours) || !TryDigits(text[parts[1]], 1, 2, out minutes) || !TryDigits(secondsText, 1, 2, out seconds))
                return false;
        }
        else
        {
            // mm:ss:xx (a colon before the fraction)
            if (!TryDigits(text[parts[0]], 1, 4, out minutes) || !TryDigits(text[parts[1]], 1, 2, out seconds))
                return false;
            fractionText = secondsText;
            secondsText = default;
        }

        if (fractionText.Length > 0 || dot >= 0)
        {
            if (!TryDigits(fractionText, 1, 3, out var fraction))
                return false;
            fractionMs = fractionText.Length switch { 1 => fraction * 100, 2 => fraction * 10, _ => fraction };
        }

        time = TimeSpan.FromMilliseconds(((hours * 60 + minutes) * 60 + seconds) * 1000 + fractionMs);
        return true;
    }

    private static bool TryDigits(ReadOnlySpan<char> text, int min, int max, out long value)
    {
        value = 0;
        if (text.Length < min || text.Length > max)
            return false;
        foreach (var c in text)
        {
            if (!char.IsAsciiDigit(c))
                return false;
            value = value * 10 + (c - '0');
        }

        return true;
    }

    /// <summary>Splits an enhanced LRC line into its plain text and the timed words (null when it has no word stamps).</summary>
    private static (string Text, List<LyricWord>? Words) ParseWords(string rest)
    {
        if (!rest.Contains('<'))
            return (rest, null);

        var segments = new List<(TimeSpan? Time, StringBuilder Text)>();
        var current = new StringBuilder();
        TimeSpan? currentTime = null;
        var anyStamp = false;
        var i = 0;
        while (i < rest.Length)
        {
            if (rest[i] == '<')
            {
                var close = rest.IndexOf('>', i + 1);
                if (close > i && TryParseStamp(rest.AsSpan(i + 1, close - i - 1), out var stamp))
                {
                    if (current.Length > 0 || currentTime is not null)
                        segments.Add((currentTime, current));
                    current = new StringBuilder();
                    currentTime = stamp;
                    anyStamp = true;
                    i = close + 1;
                    continue;
                }
            }

            current.Append(rest[i]);
            i++;
        }

        if (current.Length > 0 || currentTime is not null)
            segments.Add((currentTime, current));
        if (!anyStamp)
            return (rest, null);

        var words = new List<LyricWord>();
        var firstTime = segments.FirstOrDefault(s => s.Time is not null).Time ?? TimeSpan.Zero;
        foreach (var (time, text) in segments)
        {
            var value = text.ToString();
            if (value.Length == 0)
                continue; // an end marker
            if (value.Trim().Length == 0 && words.Count > 0)
            {
                words[^1] = words[^1] with { Text = words[^1].Text + value };
                continue;
            }

            words.Add(new LyricWord(time ?? firstTime, value));
        }

        if (words.Count == 0)
            return (string.Empty, null);
        words[0] = words[0] with { Text = words[0].Text.TrimStart() };
        words[^1] = words[^1] with { Text = words[^1].Text.TrimEnd() };
        return (string.Concat(words.Select(w => w.Text)), words);
    }
}
