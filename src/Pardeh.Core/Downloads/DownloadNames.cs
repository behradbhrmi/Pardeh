using System.Globalization;
using System.Text;

namespace Pardeh.Core.Downloads;

/// <summary>
/// Makes a file name out of what a server says. The server is not trusted: it gets no say in the folder or the extension.
/// </summary>
public static class DownloadNames
{
    private const int MaxCharacters = 100, MaxBytes = 180;
    private const string Fallback = "download";

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CLOCK$", "CONIN$", "CONOUT$",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>A name without an extension, safe on every system Pardeh runs on.</summary>
    public static string Stem(string? suggested)
    {
        var name = suggested ?? string.Empty;
        // No part of a path survives.
        name = name[(name.LastIndexOfAny(['/', '\\']) + 1)..];
        // The server's extension goes: the content decides which one the file gets.
        var dot = name.LastIndexOf('.');
        if (dot >= 0 && name.Length - dot is >= 2 and <= 6 && name.AsSpan(dot + 1).IndexOf(' ') < 0)
            name = name[..dot];

        var clean = new StringBuilder();
        foreach (var rune in name.EnumerateRunes()) // a lone half of a pair becomes U+FFFD here
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (Rune.IsWhiteSpace(rune)) // tabs and line breaks too, which are controls as well
            {
                clean.Append(' ');
                continue;
            }

            // Invisible marks can turn "song‮3pm.exe" around on screen. The two joiners are part of Persian and others.
            if (category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned
                    or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator && rune.Value is not (0x200C or 0x200D)
                || rune.Value == 0xFFFD)
                continue;
            if (rune.IsBmp && "<>:\"/\\|?*".Contains((char)rune.Value))
                clean.Append(' ');
            else
                clean.Append(rune.ToString());
        }

        var result = Edges(string.Join(' ', clean.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)));

        // Cut to length without splitting a character.
        var cut = new StringBuilder();
        int characters = 0, bytes = 0;
        foreach (var rune in result.EnumerateRunes())
        {
            if (++characters > MaxCharacters || (bytes += rune.Utf8SequenceLength) > MaxBytes)
                break;
            cut.Append(rune.ToString());
        }

        result = Edges(cut.ToString());
        if (result.Length == 0)
            return Fallback;
        var first = result.Split('.')[0].TrimEnd();
        return Reserved.Contains(first) ? "_" + result : result;
    }

    // Dots at the start hide a file (and mark Android's trashed ones); Windows drops dots and spaces at the end.
    private static string Edges(string name) => name.Trim(' ', '.', '‌', '‍');

    /// <summary>What a server suggests as a name: from its header, else from the end of the address.</summary>
    public static string Suggested(System.Net.Http.Headers.ContentDispositionHeaderValue? disposition, Uri address)
    {
        var named = disposition?.FileNameStar ?? disposition?.FileName?.Trim('"');
        if (!string.IsNullOrWhiteSpace(named))
            return named;
        var path = address.AbsolutePath.TrimEnd('/');
        var last = path[(path.LastIndexOf('/') + 1)..];
        return Uri.UnescapeDataString(last);
    }
}
