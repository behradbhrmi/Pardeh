using System.Text.RegularExpressions;

namespace Pardeh.Core.LyricsKit;

/// <summary>
/// Guitar (and bass, ukulele ...) tabs: plain monospaced text. They come from the <c>{start_of_tab}</c> ... <c>{end_of_tab}</c>
/// sections of a ChordPro sheet, or from a <c>.tab</c> file next to the track. Nothing is looked up online.
/// </summary>
public static partial class TabNotation
{
    /// <summary>The file extension of a tab file next to a track.</summary>
    public const string Extension = ".tab";

    /// <summary>Most lines kept from a tab.</summary>
    private const int MaxLines = 2000;

    private const int MaxLineLength = 400;

    [GeneratedRegex(@"^\s*\{\s*(start_of_tab|sot|end_of_tab|eot)\s*(?:[:\s]\s*(.*?))?\s*\}\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex TabDirective();

    // A string of a tab: an optional note name, then a bar line ("e|---", "Bb |-3-").
    [GeneratedRegex(@"^\s{0,3}[A-Ga-g][#b]?\s{0,2}\|", RegexOptions.CultureInvariant)]
    private static partial Regex StringHead();

    /// <summary>The tab sections of a ChordPro text, one after the other (a section's label is a line above it), or null.</summary>
    public static string? FromChordPro(string text)
    {
        var output = new List<string>();
        var inTab = false;
        foreach (var line in Lyrics.SplitLines(text))
        {
            if (TabDirective().Match(line) is { Success: true } match)
            {
                var start = match.Groups[1].Value.StartsWith('s') || match.Groups[1].Value.StartsWith('S');
                if (start && !inTab)
                {
                    if (output.Count > 0)
                        output.Add(string.Empty);
                    var label = match.Groups[2].Value.Trim();
                    if (label.StartsWith("label=", StringComparison.OrdinalIgnoreCase))
                        label = label[6..].Trim('"', ' ');
                    if (label.Length > 0)
                        output.Add(label);
                }

                inTab = start;
                continue;
            }

            if (inTab && output.Count < MaxLines)
                output.Add(CleanLine(line));
        }

        return Join(output);
    }

    /// <summary>The text of a <c>.tab</c> file: its ChordPro tab sections when it has any, else the whole file. Null when empty.</summary>
    public static string? FromTabFile(string text) =>
        FromChordPro(text) ?? Join(Lyrics.SplitLines(text).Take(MaxLines).Select(CleanLine).ToList());

    private static string CleanLine(string line)
    {
        // Tabs become spaces (the columns of the strings must stay lined up); other control characters go.
        var text = line.Replace("\t", "    ").Trim('﻿', '\u0000').TrimEnd();
        if (text.Length > MaxLineLength)
            text = text[..MaxLineLength];
        return text.Any(char.IsControl) ? new string(text.Where(c => !char.IsControl(c)).ToArray()) : text;
    }

    private static string? Join(List<string> lines)
    {
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        while (lines.Count > 0 && lines[0].Length == 0)
            lines.RemoveAt(0);
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    /// <summary>
    /// Lines no wider than <paramref name="columns"/> characters. The lines of one system (a block without blank lines) are cut
    /// at the same column, so that the strings stay lined up; the cut is made after a bar line when one is near, and each
    /// following piece starts with the strings' names again.
    /// </summary>
    public static IReadOnlyList<string> Wrap(string tab, int columns)
    {
        columns = Math.Max(8, columns);
        var result = new List<string>();
        var block = new List<string>();

        void Flush()
        {
            if (block.Count > 0)
                WrapBlock(block, columns, result);
            block.Clear();
        }

        foreach (var line in tab.Split('\n'))
        {
            if (line.Length == 0)
            {
                Flush();
                result.Add(string.Empty);
            }
            else
            {
                block.Add(line);
            }
        }

        Flush();
        return result;
    }

    private static void WrapBlock(List<string> block, int columns, List<string> result)
    {
        var length = block.Max(l => l.Length);
        if (length <= columns)
        {
            result.AddRange(block);
            return;
        }

        var heads = block.Select(l => StringHead().Match(l) is { Success: true } m ? m.Value : null).ToArray();
        var headWidth = heads.Max(h => h?.Length ?? 0);
        if (headWidth >= columns / 2)
            headWidth = 0;
        var strings = Enumerable.Range(0, block.Count).Where(i => heads[i] is not null).ToList();

        var from = 0;
        var first = true;
        while (from < length)
        {
            var room = first ? columns : columns - headWidth;
            var to = Math.Min(length, from + room);
            if (to < length && strings.Count > 0)
            {
                // The last bar line (shared by every string) in the second half of the room.
                for (var at = to - 1; at > from + room / 2; at--)
                {
                    if (strings.All(i => at < block[i].Length && block[i][at] == '|'))
                    {
                        to = at + 1;
                        break;
                    }
                }
            }

            if (!first)
                result.Add(string.Empty);
            for (var i = 0; i < block.Count; i++)
            {
                var piece = from < block[i].Length ? block[i][from..Math.Min(to, block[i].Length)] : string.Empty;
                var lead = first || headWidth == 0 ? string.Empty : (heads[i] ?? string.Empty).PadRight(headWidth);
                result.Add((lead + piece).TrimEnd());
            }

            from = to;
            first = false;
        }
    }
}
