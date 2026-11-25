using System.Globalization;
using System.Text;
using Pardeh.Core.Playback;

namespace Pardeh.Core.Library;

/// <summary>What a track list can be ordered by.</summary>
public enum SortField
{
    /// <summary>The list's own order (a playlist's order, "recently played", a queue's order of adding).</summary>
    Default,
    Title,
    Artist,
    Album,
    Genre,
    Year,
    Duration,

    /// <summary>When the file was added or last changed (the library keeps the file's modified time).</summary>
    DateAdded,
    TrackNumber,
    Rating,
    PlayCount,

    /// <summary>The number of songs in an artist / album / genre (Library group pages only).</summary>
    SongCount,

    /// <summary>The order of the songs in the current playlist queue (Library song lists only; songs not queued follow in their own order).</summary>
    QueueOrder,
}

/// <summary>A sort choice: the field and the direction.</summary>
public readonly record struct SortSpec(SortField Field, bool Descending = false)
{
    /// <summary>Compact text for settings: "Title:asc", "Year:desc".</summary>
    public override string ToString() => $"{Field}:{(Descending ? "desc" : "asc")}";

    public static SortSpec Parse(string? text, SortSpec fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
            return fallback;
        var parts = text.Split(':');
        if (!Enum.TryParse<SortField>(parts[0], ignoreCase: true, out var field))
            return fallback;
        return new SortSpec(field, parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The label of the field, as the sort sheet and the list header show it.</summary>
    public static string Label(SortField field) => field switch
    {
        SortField.Default => "Default order",
        SortField.Title => "Title",
        SortField.Artist => "Artist",
        SortField.Album => "Album",
        SortField.Genre => "Genre",
        SortField.Year => "Year",
        SortField.Duration => "Duration",
        SortField.DateAdded => "Date added",
        SortField.TrackNumber => "Track number",
        SortField.Rating => "Rating",
        SortField.PlayCount => "Play count",
        SortField.SongCount => "Number of songs",
        SortField.QueueOrder => "Playlist queue",
        _ => field.ToString(),
    };
}

/// <summary>
/// The sort keys of one track, whatever it came from (a library item or a queue item). Text is compared by
/// <see cref="TrackSort.CompareText"/>; numbers by value; empty values (no artist, year 0 ...) always go last.
/// </summary>
public readonly record struct TrackKey(string Title, string Artist, string Album, string Genre, int Year, double Duration, DateTime Added, int Track, int Rating, int Plays)
{
    public static TrackKey Of(LibraryItem item) => new(item.Title, item.Artist, item.Album, item.Genre, ParseInt(item.Year), item.DurationSeconds, item.Modified, item.TrackNumber, item.Rating, item.PlayCount);

    public static TrackKey Of(MediaItem item) => new(
        item.Name, item.GetAttribute("Author"), item.GetAttribute("Album"), item.GetAttribute("Genre"), ParseInt(item.GetAttribute("Year")),
        item.Duration.TotalSeconds, DateTime.MinValue, ParseInt(item.GetAttribute("TrackNumber")), 0, 0);

    /// <summary>The leading number of "2019", "2019-05-01" or "3/12"; 0 when there is none.</summary>
    private static int ParseInt(string text)
    {
        var digits = 0;
        var value = 0;
        foreach (var c in text.Trim())
        {
            if (c is < '0' or > '9' || digits >= 9)
                break;
            value = value * 10 + (c - '0');
            digits++;
        }

        return value;
    }
}

/// <summary>
/// Sorting of tracks for the phone Library and the queue: stable, culture-safe (the result never depends on the phone's
/// language), Persian / Arabic letters in alphabet order, numbers in names by value ("2" before "10"), a leading "The " and
/// bracketed tags ("[ MusicViral.ir ] ...") ignored, empty values last in both directions.
/// </summary>
public static class TrackSort
{
    // The Persian alphabet in order (code points are not: pe, che, zhe, gaf, kaf, yeh sit after the Arabic block), written as escapes.
    private const string PersianLetters =
        "آابپتثجچحخدذرزژسشصضطظعغفقکگلمنوهی";

    private static readonly Dictionary<char, int> PersianRank = BuildRanks();

    private static Dictionary<char, int> BuildRanks()
    {
        var ranks = new Dictionary<char, int>();
        var rank = 0;
        foreach (var c in PersianLetters)
            ranks[c] = ++rank * 4;
        // Variants take the place of their letter, a little after it.
        void Alias(char variant, char of) => ranks[variant] = ranks[of] + 1;
        Alias('أ', 'ا'); Alias('إ', 'ا'); Alias('ٱ', 'ا'); // alef with hamza above / below, alef wasla
        Alias('ك', 'ک');                                                     // Arabic kaf
        Alias('ي', 'ی'); Alias('ى', 'ی'); Alias('ئ', 'ی'); // Arabic yeh, alef maksura, yeh with hamza
        Alias('ؤ', 'و');                                                     // waw with hamza
        Alias('ة', 'ه'); Alias('ھ', 'ه');                           // teh marbuta, heh doachashmee
        Alias('ء', 'ا'); ranks['ء'] += 1;                               // hamza
        return ranks;
    }

    /// <summary>The text a title is ordered by: leading bracketed tags and "The " dropped, Arabic letter forms and digits unified, trimmed.</summary>
    public static string SortText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var s = text.Trim();

        // "[ MusicViral.ir ] Name", "(Live) Name", "{x} Name": the tag is skipped when something follows it.
        while (s.Length > 0 && s[0] is '[' or '(' or '{' or '«' or '【')
        {
            var close = s[0] switch { '[' => ']', '(' => ')', '{' => '}', '«' => '»', _ => '】' };
            var end = s.IndexOf(close);
            if (end < 0 || end >= 60)
                break;
            var rest = s[(end + 1)..].TrimStart();
            if (rest.Length == 0)
                break;
            s = rest;
        }

        if (s.StartsWith("the ", StringComparison.OrdinalIgnoreCase) && s.Length > 4)
            s = s[4..].TrimStart();

        return Fold(s);
    }

    /// <summary>Unifies letter forms for comparing and searching: Arabic yeh / kaf become the Persian ones, tatweel, diacritics, ZWNJ and direction marks are dropped, Persian and Arabic-Indic digits become 0-9.</summary>
    public static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case >= '۰' and <= '۹': // Persian digits
                    sb.Append((char)('0' + (c - '۰')));
                    break;
                case >= '٠' and <= '٩': // Arabic-Indic digits
                    sb.Append((char)('0' + (c - '٠')));
                    break;
                case 'ـ' or '‌' or '‍' or '‎' or '‏': // tatweel, ZWNJ, ZWJ, direction marks
                    break;
                case >= 'ً' and <= 'ٟ': // Arabic diacritics
                case 'ٰ':
                    break;
                case 'ي' or 'ى':
                    sb.Append('ی');
                    break;
                case 'ك':
                    sb.Append('ک');
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }

    private static bool IsArabicScript(char c) => c is >= '؀' and <= 'ۿ' or >= 'ݐ' and <= 'ݿ' or >= 'ﭐ' and <= '﷿' or >= 'ﹰ' and <= '﻿';

    private static int Kind(char c) => c is >= '0' and <= '9' ? 1 : IsArabicScript(c) && char.IsLetter(c) ? 2 : 0;

    /// <summary>
    /// Culture-independent ordering of two texts (after <see cref="SortText"/>): runs of digits by value, runs of Arabic-script
    /// letters in Persian alphabet order, everything else case-insensitively (the invariant culture, kana and width ignored).
    /// </summary>
    public static int CompareText(string? a, string? b)
    {
        a = SortText(a ?? string.Empty);
        b = SortText(b ?? string.Empty);
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            var kind = Kind(a[i]);
            var kindB = Kind(b[j]);
            var runA = Run(a, i, kind);
            var runB = Run(b, j, kindB);
            if (kind != kindB)
            {
                // Digits, then other text, then Arabic script.
                return Order(kind).CompareTo(Order(kindB));
            }

            var result = kind switch
            {
                1 => CompareNumbers(a.AsSpan(i, runA), b.AsSpan(j, runB)),
                2 => CompareArabic(a.AsSpan(i, runA), b.AsSpan(j, runB)),
                _ => string.Compare(a.Substring(i, runA), b.Substring(j, runB), CultureInfo.InvariantCulture, CompareOptions.IgnoreCase | CompareOptions.IgnoreKanaType | CompareOptions.IgnoreWidth),
            };
            if (result != 0)
                return Math.Sign(result);
            i += runA;
            j += runB;
        }

        var left = a.Length - i;
        var right = b.Length - j;
        if (left != right)
            return left == 0 ? -1 : 1;
        return 0;
    }

    private static int Order(int kind) => kind switch { 1 => 0, 0 => 1, _ => 2 };

    private static int Run(string s, int start, int kind)
    {
        var end = start;
        while (end < s.Length && Kind(s[end]) == kind)
            end++;
        return end - start;
    }

    private static int CompareNumbers(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        a = a.TrimStart('0');
        b = b.TrimStart('0');
        if (a.Length != b.Length)
            return a.Length < b.Length ? -1 : 1;
        return Math.Sign(a.CompareTo(b, StringComparison.Ordinal));
    }

    private static int CompareArabic(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        var n = Math.Min(a.Length, b.Length);
        for (var k = 0; k < n; k++)
        {
            var x = Weight(a[k]);
            var y = Weight(b[k]);
            if (x != y)
                return x < y ? -1 : 1;
        }

        return a.Length.CompareTo(b.Length);
    }

    private static int Weight(char c) => PersianRank.TryGetValue(c, out var rank) ? rank : 1000 + c;

    /// <summary>Compares two tracks by one field; empty values (and zero numbers) sort after the others whatever the direction.</summary>
    public static int Compare(in TrackKey a, in TrackKey b, SortSpec sort)
    {
        var direction = sort.Descending ? -1 : 1;
        switch (sort.Field)
        {
            case SortField.Title: return Text(a.Title, b.Title, direction);
            case SortField.Artist: return Text(a.Artist, b.Artist, direction);
            case SortField.Album: return Text(a.Album, b.Album, direction);
            case SortField.Genre: return Text(a.Genre, b.Genre, direction);
            case SortField.Year: return Number(a.Year, b.Year, direction);
            case SortField.Duration: return Number(a.Duration, b.Duration, direction);
            case SortField.TrackNumber: return Number(a.Track, b.Track, direction);
            case SortField.Rating: return Number(a.Rating, b.Rating, direction);
            case SortField.PlayCount: return Number(a.Plays, b.Plays, direction);
            case SortField.DateAdded:
                if (a.Added == b.Added) return 0;
                if (a.Added == DateTime.MinValue) return 1;
                if (b.Added == DateTime.MinValue) return -1;
                return direction * a.Added.CompareTo(b.Added);
            default: return 0;
        }
    }

    private static int Text(string a, string b, int direction)
    {
        var emptyA = string.IsNullOrWhiteSpace(a);
        var emptyB = string.IsNullOrWhiteSpace(b);
        if (emptyA || emptyB)
            return emptyA == emptyB ? 0 : emptyA ? 1 : -1;
        return direction * CompareText(a, b);
    }

    private static int Number(double a, double b, int direction)
    {
        var emptyA = a <= 0;
        var emptyB = b <= 0;
        if (emptyA || emptyB)
            return emptyA == emptyB ? 0 : emptyA ? 1 : -1;
        return direction * a.CompareTo(b);
    }

    /// <summary>The items in the new order. The sort is stable: equal items keep their relative order. <see cref="SortField.Default"/> keeps the order given.</summary>
    public static List<T> Sort<T>(IEnumerable<T> items, Func<T, TrackKey> key, SortSpec sort)
    {
        var list = items.ToList();
        if (sort.Field == SortField.Default)
            return sort.Descending ? [.. Enumerable.Reverse(list)] : list;
        var keys = list.Select(key).ToArray();
        var order = Enumerable.Range(0, list.Count).OrderBy(i => i, Comparer<int>.Create((x, y) => Compare(keys[x], keys[y], sort)));
        return order.Select(i => list[i]).ToList();
    }

    public static List<LibraryItem> Sort(IEnumerable<LibraryItem> items, SortSpec sort) => Sort(items, TrackKey.Of, sort);

    /// <summary>The sort options a track list offers (the field "Default order" only where the list has an order of its own).</summary>
    public static IReadOnlyList<SortField> Fields(bool withDefault) =>
        withDefault
            ? [SortField.Default, SortField.Title, SortField.Artist, SortField.Album, SortField.Year, SortField.Duration, SortField.DateAdded, SortField.TrackNumber, SortField.Rating, SortField.PlayCount]
            : [SortField.Title, SortField.Artist, SortField.Album, SortField.Year, SortField.Duration, SortField.DateAdded, SortField.TrackNumber, SortField.Rating, SortField.PlayCount];

    /// <summary>The identity of a track for "the same song twice": the system's canonical file identity, else the path.</summary>
    public static string IdentityOf(string source)
    {
        var key = MediaSources.IdentityKey?.Invoke(source) ?? source;
        return key.Replace('\\', '/');
    }
}
