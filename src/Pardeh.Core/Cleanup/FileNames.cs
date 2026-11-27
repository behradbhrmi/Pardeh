using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Pardeh.Core.Changes;

namespace Pardeh.Core.Cleanup;

/// <param name="Structured">The name held an artist and a title apart, so they are fairly sure.</param>
public sealed record FileNameGuess(string? Artist, string? Title, uint Track, bool Structured);

/// <summary>Artist and title read from names such as "01 - Artist - Title (site).mp3".</summary>
public static partial class FileNameReader
{
    [GeneratedRegex(@"^\s*(?<n>\d{1,3})(?:\s*[\-._)]\s*|\s+(?=\D))")]
    private static partial Regex TrackNumber();

    [GeneratedRegex(@"^(?:128|192|256|320)[\s\-.]+")]
    private static partial Regex Bitrate();

    [GeneratedRegex(@"\s+[\-–—]+\s+")]
    private static partial Regex SpacedDash();

    // Matched against the folded key: a word that only counts files ("track 01"), a recorder's or chat app's name, or a number.
    [GeneratedRegex(@"^(?:(?:track|audio|song|file|voice|recording|new recording|آهنگ|ترک)\s*\d+|(?:aud|ptt)\s\d.*|new recording|unknown|untitled|ناشناس|[\d\s]+)$")]
    private static partial Regex Generic();

    /// <summary>A value that stands for "nothing": "Track 01", "Unknown Artist", a bare number.</summary>
    public static bool IsPlaceholder(string? value)
    {
        var key = TextFold.Key(value);
        return key.Length == 0 || Generic().IsMatch(key)
            || key is "unknown artist" or "unknown album" or "unknown title" or "no title" or "no artist" or "various" or "<unknown>" or "نامشخص" or "ناشناس";
    }

    public static FileNameGuess Read(string fileName, JunkRules? rules = null)
    {
        var name = Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Trim();
        // "128 site.com Artist - Title": the number in front of a site's name is its bitrate, not a track number.
        var cleaned = JunkText.Clean(name, rules);
        if (cleaned.Changed && Bitrate().Match(name) is { Success: true } rate)
            cleaned = JunkText.Clean(name[rate.Length..], rules);
        name = cleaned.Text;
        uint track = 0;
        if (TrackNumber().Match(name) is { Success: true } number && number.Length < name.Length)
        {
            track = uint.Parse(number.Groups["n"].Value, CultureInfo.InvariantCulture);
            name = name[number.Length..];
        }

        var parts = SpacedDash().Split(name).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        if (parts.Count == 1 && parts[0].Count(c => c == '-') == 1)
            parts = parts[0].Split('-').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();

        string? Real(string? value) => IsPlaceholder(value) ? null : value;
        if (parts.Count >= 2)
        {
            var (artist, title) = (Real(parts[0]), Real(parts[^1]));
            return new FileNameGuess(artist, title, track, artist is not null && title is not null);
        }

        return new FileNameGuess(null, Real(parts.FirstOrDefault()), track, false);
    }
}

/// <summary>
/// A pattern for naming files, such as "{artist} - {title}". An empty field takes its separator or brackets with it, and
/// characters a file name can't have are replaced. A pattern only renames; it never moves a file to another folder.
/// </summary>
public static partial class NamePattern
{
    public static readonly string[] Presets = ["{artist} - {title}", "{title} - {artist}", "{track} - {title}", "{artist} - {album} - {track} - {title}"];

    public static readonly string[] Fields = ["title", "artist", "albumartist", "album", "track", "year", "genre"];

    /// <summary>Room for the extension and a number added when the name is taken (the service allows 240 bytes).</summary>
    private const int MaxBytes = 220;

    [GeneratedRegex(@"\{(?<field>[^{}]*)\}")]
    private static partial Regex Token();

    [GeneratedRegex(@"[\(\[][^\(\)\[\]]*[\)\]]")]
    private static partial Regex Group();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();

    /// <summary>Null when the pattern can be used, else what is wrong with it.</summary>
    public static string? Check(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return "Type a pattern.";
        var unknown = Token().Matches(pattern).Select(m => m.Groups["field"].Value).FirstOrDefault(f => !Fields.Contains(f));
        if (unknown is not null)
            return $"There is no field {{{unknown}}}.";
        var literal = Token().Replace(pattern, string.Empty);
        if (literal.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|', '{', '}']) >= 0)
            return "A file name can't contain \\ / : * ? \" < > | or a lone brace.";
        return pattern.Contains("{title}", StringComparison.Ordinal) ? null : "A pattern needs {title}, or songs would get the same name.";
    }

    private static string ValueOf(string field, TagFields tags)
    {
        static string List(string[]? values) => string.Join(", ", (values ?? []).Select(v => v.Trim()).Where(v => v.Length > 0));
        return field switch
        {
            "title" => tags.Title ?? string.Empty,
            "artist" => List(tags.Artists) is { Length: > 0 } artists ? artists : List(tags.AlbumArtists),
            "albumartist" => List(tags.AlbumArtists),
            "album" => tags.Album ?? string.Empty,
            "track" => tags.Track > 0 ? tags.Track.Value.ToString("00", CultureInfo.InvariantCulture) : string.Empty,
            "year" => tags.Year > 0 ? tags.Year.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
            "genre" => List(tags.Genres),
            _ => string.Empty,
        };
    }

    private const char Gone = '\u0001';

    /// <summary>The new file name with <paramref name="extension"/>, or null when there is no title (the old name stays).</summary>
    public static string? Apply(string pattern, TagFields tags, string extension)
    {
        if (Check(pattern) is not null || string.IsNullOrWhiteSpace(tags.Title) || Sanitize(tags.Title).Length == 0)
            return null;
        var filled = Token().Replace(pattern, m => Sanitize(ValueOf(m.Groups["field"].Value, tags)) is { Length: > 0 } value ? value.Replace(' ', '\u0002') : Gone.ToString());

        // Brackets that held only empty fields go entirely.
        filled = Group().Replace(filled, m => m.Value.Contains(Gone) && !m.Value[1..^1].Any(char.IsLetterOrDigit) ? string.Empty : m.Value);

        // Each empty field takes the separator before it, or the one after it when it comes first.
        int at;
        while ((at = filled.IndexOf(Gone)) >= 0)
        {
            var from = at;
            while (from > 0 && IsSeparator(filled[from - 1]))
                from--;
            var to = at + 1;
            if (from == 0)
                while (to < filled.Length && IsSeparator(filled[to]))
                    to++;
            filled = filled[..from] + filled[to..];
        }

        var stem = Spaces().Replace(filled.Replace('\u0002', ' '), " ").Trim(' ', '-', '_', ',', '.');
        stem = Shorten(stem, MaxBytes - Encoding.UTF8.GetByteCount(extension)).TrimEnd(' ', '.', '-');
        if (stem.Length == 0)
            return null;
        if (FileChangeService.IsReservedName(stem))
            stem += "_";
        return stem + extension;
    }

    private static bool IsSeparator(char c) => c is ' ' or '-' or '_' or ',' or '.' or '–' or '—' or '+' or '~';

    private static string Sanitize(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            result.Append(c switch
            {
                '/' or '\\' or '|' => "-",
                ':' => " -",
                '*' => " ",
                '?' => string.Empty,
                '"' => "'",
                '<' => "(",
                '>' => ")",
                _ when char.IsControl(c) => " ",
                _ => c.ToString(),
            });
        }

        return Spaces().Replace(result.ToString(), " ").Trim(' ', '.');
    }

    // Cuts by what the file system counts (bytes: Persian letters take two), never through a letter and its marks.
    private static string Shorten(string text, int bytes)
    {
        if (Encoding.UTF8.GetByteCount(text) <= bytes)
            return text;
        var result = new StringBuilder();
        var used = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            used += Encoding.UTF8.GetByteCount(element);
            if (used > bytes)
                break;
            result.Append(element);
        }

        return result.ToString();
    }
}
