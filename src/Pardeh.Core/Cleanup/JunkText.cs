using System.Text;
using System.Text.RegularExpressions;

namespace Pardeh.Core.Cleanup;

/// <summary>The user's own words: phrases to take out wherever they are, and text that is never junk.</summary>
public sealed record JunkRules(IReadOnlyList<string> Junk, IReadOnlyList<string> Keep)
{
    public static readonly JunkRules None = new([], []);
}

/// <param name="Text">The text without the junk (the input itself when nothing was found).</param>
/// <param name="Found">What was taken out.</param>
public sealed record JunkResult(string Text, IReadOnlyList<string> Found)
{
    public bool Changed => Found.Count > 0;
}

/// <summary>
/// Finds what download sites write into song details: web addresses, account names, "download from …" lines and bitrate
/// labels. It works from the shape of the text, not from a list of sites. A part of a title that only held junk goes with it,
/// separator and brackets included; text with nothing to remove is returned exactly as it was.
/// </summary>
public static partial class JunkText
{
    // Endings that are rarely the end of anything but a web address.
    private const string SureEndings = "com|ir|net|org|info|biz|xyz|pw|site|online|website|mobi|icu|vip|club|store";

    // Endings that also end ordinary dotted words ("Call.Me"): taken only when the name before them looks like a site.
    private const string UnsureEndings = "me|in|co|fm|live|fun|space|top|link|blog|music|tv|app|cc|ws|ru|pk|am|to|ly|us|uk|de";

    [GeneratedRegex(@"(?:https?://|www\.)[^\s\[\]\(\)\{\}|<>""]+", RegexOptions.IgnoreCase)]
    private static partial Regex Address();

    [GeneratedRegex(@"(?<![\w@.])(?:t\.me|telegram\.me|instagram\.com)/[^\s\[\]\(\)\{\}|]+", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelAddress();

    [GeneratedRegex(@"(?<![A-Za-z0-9@.\-_])(?:[a-z0-9](?:[a-z0-9\-_]*[a-z0-9])?\.)+(?:" + SureEndings + @")(?:\.ir)?(?:/[^\s\[\]\(\)\{\}|]*)?(?![A-Za-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex Domain();

    [GeneratedRegex(@"(?<![A-Za-z0-9@.\-_])(?<name>[a-z0-9](?:[a-z0-9\-_]*[a-z0-9])?)\.(?:" + UnsureEndings + @")(?:/[^\s\[\]\(\)\{\}|]*)?(?![A-Za-z0-9.])", RegexOptions.IgnoreCase)]
    private static partial Regex MaybeDomain();

    [GeneratedRegex(@"music|song|muzik|mp3|ahang|tarane|taraneh|remix|radio|download|media|seda|navahang|^dl|dl$", RegexOptions.IgnoreCase)]
    private static partial Regex SiteWord();

    [GeneratedRegex(@"(?<![\w.])@[A-Za-z][A-Za-z0-9_.]{3,}")]
    private static partial Regex Handle();

    [GeneratedRegex(@"(?<![\w.])(?:64|96|112|128|160|192|224|256|320)\s*(?:kbps|kb/s|kbit/s|kbs|کیلوبیت)(?![A-Za-z])")]
    private static partial Regex Bitrate();

    // Matched against the folded text (Persian digits and letters made uniform).
    [GeneratedRegex(@"(?:free\s+)?download(?:ed)?\s+(?:it\s+)?(?:free\s+)?(?:from|at|now\s+from)\b.*$|(?:join\s+)?(?:our\s+)?telegram\s*(?:channel)?\s*[:@].*$|کانال\s+تلگرام.*$|دانلود\s+(?:شده\s+)?از\s+(?:سایت|رسانه|کانال|وب\s*سایت).*$")]
    private static partial Regex DownloadedFrom();

    [GeneratedRegex(@"^\s*دانلود\s+(?:رایگان\s+)?(?:آهنگ|اهنگ|موزیک|ترانه)(?:\s+(?:جدید|زیبای))?(?=\s|$)")]
    private static partial Regex DownloadSong();

    // Parts of a text: separators and brackets stand on their own.
    [GeneratedRegex(@"(\s+[\-–—]+\s+|\s*\|+\s*|\s*::\s*|\s*~+\s*|\s+[•·]\s+|\s*//+(?=\s)|[\[\(\{【«]|[\]\)\}】»])")]
    private static partial Regex Delimiter();

    private static readonly HashSet<string> PromoWords = new(StringComparer.Ordinal)
    {
        "download", "downloaded", "from", "new", "exclusive", "official", "site", "website", "free", "music", "song", "by", "visit", "at", "the", "join",
        "us", "telegram", "channel", "instagram", "page", "mp3", "www", "kbps", "quality", "hq", "full", "track", "uploaded", "more", "for", "on", "our",
        "دانلود", "آهنگ", "اهنگ", "موزیک", "جدید", "سایت", "کانال", "تلگرام", "رسانه", "از", "با", "کیفیت", "عالی", "اصلی", "اختصاصی", "ترانه", "رایگان", "وب", "اینستاگرام", "پیج",
    };

    // Words that only make sense pointing at a site. Without one of these, "Song" next to an address is the song's name.
    private static readonly HashSet<string> PointingWords = new(StringComparer.Ordinal)
    {
        "download", "downloaded", "from", "exclusive", "official", "site", "website", "visit", "join", "channel", "telegram", "instagram", "uploaded", "on", "by", "at", "more",
        "دانلود", "سایت", "کانال", "تلگرام", "رسانه", "از", "اختصاصی", "وب", "اینستاگرام", "پیج",
    };

    private static readonly HashSet<string> BareBitrates = new(StringComparer.Ordinal) { "128", "192", "256", "320" };

    private static readonly char[] Openers = ['[', '(', '{', '【', '«'];
    private static readonly char[] Closers = [']', ')', '}', '】', '»'];

    public static JunkResult Clean(string? text, JunkRules? rules = null)
    {
        var input = text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return new JunkResult(input, []);
        rules ??= JunkRules.None;
        var found = new List<string>();

        // What must stay is hidden from every rule, then put back.
        var kept = new List<string>();
        var work = input;
        foreach (var phrase in rules.Keep.Where(p => p.Trim().Length > 0))
        {
            int at;
            while ((at = IndexOf(work, phrase.Trim())) >= 0)
            {
                kept.Add(work.Substring(at, phrase.Trim().Length));
                work = work[..at] + (char)(0xE000 + kept.Count - 1) + work[(at + phrase.Trim().Length)..];
            }
        }

        foreach (var phrase in rules.Junk.Select(p => p.Trim()).Where(p => p.Length > 1))
        {
            int at;
            while ((at = IndexOf(work, phrase)) >= 0)
            {
                found.Add(work.Substring(at, phrase.Length));
                work = work[..at] + work[(at + phrase.Length)..];
            }
        }

        var parts = Delimiter().Split(work).ToList(); // even places: text; odd places: delimiters
        for (var i = 0; i < parts.Count; i += 2)
        {
            var bracketed = i > 0 && i + 1 < parts.Count && Is(parts[i - 1], Openers) && Is(parts[i + 1], Closers);
            parts[i] = CleanPart(parts[i], bracketed, isLast: i == parts.Count - 1 && i > 0, found);
        }

        if (found.Count == 0)
            return new JunkResult(input, []);

        var result = Tidy(parts);
        for (var i = 0; i < kept.Count; i++)
            result = result.Replace(((char)(0xE000 + i)).ToString(), kept[i]);
        return new JunkResult(result, found);
    }

    private static bool Is(string delimiter, char[] set) => delimiter.Trim() is { Length: 1 } one && set.Contains(one[0]);

    // Case, Arabic and Persian letter forms and digits don't matter.
    private static int IndexOf(string text, string phrase) => TextFold.SameLength(text).IndexOf(TextFold.SameLength(phrase), StringComparison.Ordinal);

    private static string CleanPart(string part, bool bracketed, bool isLast, List<string> found)
    {
        if (string.IsNullOrWhiteSpace(part))
            return part;
        var trimmed = TextFold.SameLength(part).Trim();
        // A bare number is a bitrate only where nothing else can be meant: alone in brackets, or as the last part.
        if ((bracketed || isLast) && BareBitrates.Contains(trimmed) || bracketed && trimmed is "mp3")
        {
            found.Add(part.Trim());
            return string.Empty;
        }

        var site = false;
        string Cut(string value, Regex rule, bool onFolded = false, Func<Match, bool>? only = null)
        {
            var searched = onFolded ? TextFold.SameLength(value) : value;
            var result = new StringBuilder(value);
            foreach (var match in rule.Matches(searched).Reverse())
            {
                if (only is not null && !only(match))
                    continue;
                found.Add(value.Substring(match.Index, match.Length).Trim());
                result.Remove(match.Index, match.Length);
                site = true;
            }

            return result.ToString();
        }

        var text = Cut(part, DownloadedFrom(), onFolded: true);
        text = Cut(text, DownloadSong(), onFolded: true);
        text = Cut(text, ChannelAddress());
        text = Cut(text, Address());
        text = Cut(text, Domain());
        text = Cut(text, MaybeDomain(), only: m => m.Groups["name"].Value is var name && (SiteWord().IsMatch(name) || name.Any(char.IsDigit) || name.Contains('-')));
        text = Cut(text, Handle());
        text = Cut(text, Bitrate(), onFolded: true);

        // "Exclusive on …" left without its address says nothing by itself.
        if (site)
        {
            var words = TextFold.Key(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.All(w => PromoWords.Contains(w) || w.All(char.IsDigit)) && (words.All(w => w.All(char.IsDigit)) || words.Any(PointingWords.Contains)))
                return string.Empty;
        }

        return text;
    }

    [GeneratedRegex(@"[\[\(\{【«]\s*[\]\)\}】»]")]
    private static partial Regex EmptyBrackets();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    private static readonly char[] Loose = [' ', '-', '–', '—', '|', '~', ':', '•', '·', '_', ',', ';', '/', '+', '=', '.', '\t'];

    private static bool IsSeparator(string delimiter) => !Is(delimiter, Openers) && !Is(delimiter, Closers);

    private static string Tidy(List<string> parts)
    {
        // Parts that became empty take one separator with them.
        var pieces = new List<(string Text, bool Separator)>();
        for (var i = 0; i < parts.Count; i++)
        {
            var separator = i % 2 == 1 && IsSeparator(parts[i]);
            var value = i % 2 == 0 ? parts[i] : parts[i];
            if (i % 2 == 0 && value.Trim(Loose).Length == 0)
                continue;
            if (separator && (pieces.Count == 0 || pieces[^1].Separator))
                continue;
            pieces.Add((value, separator));
        }

        var result = string.Concat(pieces.Select(p => p.Text));
        string before;
        do
        {
            before = result;
            result = EmptyBrackets().Replace(result, " ");
        }
        while (result != before);

        result = Spaces().Replace(result, " ").Trim();
        // Separators left hanging at either end; a closing dot of a real word ("R.E.M.") stays when nothing was cut there.
        result = result.TrimStart(Loose).TrimEnd(Loose.Where(c => c != '.').ToArray());
        foreach (var separator in new[] { " -", " |", " ~", " –" })
            while (result.EndsWith(separator, StringComparison.Ordinal))
                result = result[..^separator.Length].TrimEnd();
        return Spaces().Replace(result, " ").Replace("( ", "(").Replace(" )", ")").Replace("[ ", "[").Replace(" ]", "]").Trim();
    }
}
