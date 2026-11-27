using System.Globalization;
using System.Text.RegularExpressions;
using Pardeh.Core.Cleanup;

namespace Pardeh.Core.OnlineInfo;

/// <summary>What is known on this device about the song being matched. Only parts of the title and artists are ever sent.</summary>
/// <param name="VersionHints">More text that may say which version this is, such as the file name.</param>
/// <param name="Seconds">The song's length, or 0 when unknown.</param>
public sealed record LocalSong(string Title, IReadOnlyList<string> Artists, string Album, double Seconds, string VersionHints = "");

public enum MatchLevel
{
    None,
    Possible,
    Strong,
}

public enum Likeness
{
    No,
    Close,
    Same,
}

/// <param name="Reason">Why it got its level, for the user.</param>
/// <param name="LengthDifference">In seconds, or null when a length is unknown.</param>
public sealed record MetadataMatch(MetadataCandidate Candidate, MatchLevel Level, string Reason, Likeness Title, Likeness Artist, bool SameVersion,
    double? LengthDifference, bool SameAlbum);

/// <summary>
/// Scores recordings against a song on title, artist, length, album and version. "Strong" needs all of title, artist and
/// version to agree and the length to be within a few seconds: text alone is never Strong.
/// </summary>
public static partial class MetadataMatcher
{
    /// <summary>Encoders pad and trim, and databases round: this much is the same length.</summary>
    public const double SameLength = 3;

    private static readonly (string Word, string Version)[] VersionWords =
    [
        ("remix", "remix"), ("remixed", "remix"), ("mix", "remix"), ("ریمیکس", "remix"), ("live", "live"), ("اجرای زنده", "live"),
        ("instrumental", "instrumental"), ("بی کلام", "instrumental"), ("بیکلام", "instrumental"), ("acoustic", "acoustic"), ("unplugged", "acoustic"),
        ("edit", "edit"), ("remaster", "remaster"), ("remastered", "remaster"), ("karaoke", "karaoke"), ("demo", "demo"),
    ];

    // Words that make a loose "mix" mean nothing new.
    private static readonly string[] PlainMixes = ["original mix", "album mix", "stereo mix", "mono mix"];

    // These at the end of a bare title name a version; "live" and "mix" alone are too often part of a name ("Let It Live").
    private static readonly string[] TrailingWords = ["remix", "instrumental", "karaoke", "ریمیکس", "بی کلام"];

    [GeneratedRegex(@"[\(\[\{]([^\(\)\[\]\{\}]*)[\)\]\}]")]
    private static partial Regex Bracketed();

    [GeneratedRegex(@"(?:^|\s)(?:feat\.?|ft\.?|featuring)\s.*$", RegexOptions.IgnoreCase)]
    private static partial Regex Featured();

    [GeneratedRegex(@"\s*(?:;|,|&|\+|/|\s(?:and|x|vs\.?|feat\.?|ft\.?|featuring|with)\s|\sو\s|،)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex ArtistBreaks();

    /// <summary>
    /// A title without its additions ("(feat. X)", "[Remastered 2011]", " - Live"), and the versions those and the title name.
    /// </summary>
    public static (string Base, IReadOnlySet<string> Versions) SplitTitle(string title)
    {
        var versions = new HashSet<string>(StringComparer.Ordinal);
        var rest = Bracketed().Replace(title, found =>
        {
            var inside = found.Groups[1].Value;
            var named = VersionsIn(inside);
            versions.UnionWith(named);
            return named.Count > 0 || Featured().IsMatch(inside) || PlainMixes.Contains(TextFold.Key(inside)) ? " " : found.Value;
        });

        var parts = rest.Split(" - ", StringSplitOptions.TrimEntries).ToList();
        while (parts.Count > 1 && VersionsIn(parts[^1]) is { Count: > 0 } named)
        {
            versions.UnionWith(named);
            parts.RemoveAt(parts.Count - 1);
        }

        rest = Featured().Replace(string.Join(" - ", parts), string.Empty).Trim();
        foreach (var word in TrailingWords)
        {
            if (rest.Length > word.Length + 1 && rest.EndsWith(" " + word, StringComparison.OrdinalIgnoreCase))
            {
                versions.UnionWith(VersionsIn(word));
                rest = rest[..^(word.Length + 1)].TrimEnd(' ', '-', '|');
            }
        }

        rest = string.Join(' ', rest.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return (rest.Length > 0 ? rest : title.Trim(), versions);
    }

    private static HashSet<string> VersionsIn(string text)
    {
        var key = " " + TextFold.Key(text) + " ";
        foreach (var plain in PlainMixes)
            key = key.Replace(" " + plain + " ", " ");
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (word, version) in VersionWords)
        {
            if (key.Contains(" " + TextFold.Key(word) + " ", StringComparison.Ordinal))
                found.Add(version);
        }

        return found;
    }

    private static HashSet<string> VersionsOf(LocalSong song)
    {
        var versions = new HashSet<string>(SplitTitle(song.Title).Versions, StringComparer.Ordinal);
        foreach (Match part in Bracketed().Matches(song.VersionHints))
            versions.UnionWith(VersionsIn(part.Groups[1].Value));
        return versions;
    }

    private static HashSet<string> VersionsOf(MetadataCandidate candidate)
    {
        var versions = new HashSet<string>(SplitTitle(candidate.Title).Versions, StringComparer.Ordinal);
        versions.UnionWith(VersionsIn(candidate.Disambiguation));
        if (candidate.Releases.Count > 0 && candidate.Releases.All(r => r.SecondaryTypes.Contains("Live")))
            versions.Add("live");
        return versions;
    }

    public static bool WantsLive(LocalSong song) => VersionsOf(song).Contains("live");

    // Spaces go too: Persian words are written joined, spaced or with a half-space.
    private static string Compact(string text) => TextFold.Key(text).Replace(" ", string.Empty);

    private static Likeness Compare(string first, string second)
    {
        var a = Compact(first);
        var b = Compact(second);
        if (a.Length == 0 || b.Length == 0)
            return Likeness.No;
        if (a == b)
            return Likeness.Same;
        var longest = Math.Max(a.Length, b.Length);
        return longest >= 5 && 1 - (double)Distance(a, b) / longest >= 0.82 ? Likeness.Close : Likeness.No;
    }

    private static int Distance(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 40 || a.Length > 300 || b.Length > 300)
            return Math.Max(a.Length, b.Length);
        var row = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            row[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            var corner = row[0];
            row[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var above = row[j];
                row[j] = Math.Min(Math.Min(above + 1, row[j - 1] + 1), corner + (a[i - 1] == b[j - 1] ? 0 : 1));
                corner = above;
            }
        }

        return row[b.Length];
    }

    private static string WithoutThe(string name) => name.StartsWith("the ", StringComparison.OrdinalIgnoreCase) ? name[4..] : name;

    /// <summary>The names in an artist field, the whole field first ("Simon &amp; Garfunkel" is one name, "A &amp; B" two).</summary>
    public static IEnumerable<string> ArtistNames(IEnumerable<string> fields)
    {
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field))
                continue;
            yield return field.Trim();
            var parts = ArtistBreaks().Split(field).Where(p => p.Trim().Length > 0).ToList();
            if (parts.Count > 1)
            {
                foreach (var part in parts)
                    yield return part.Trim();
            }
        }
    }

    private static Likeness CompareArtists(LocalSong song, MetadataCandidate candidate)
    {
        var wanted = ArtistNames(song.Artists).Select(WithoutThe).ToList();
        var offered = candidate.Artists.SelectMany(a => a.OtherNames.Prepend(a.Name)).Prepend(candidate.ArtistText).Select(WithoutThe).ToList();
        var best = Likeness.No;
        foreach (var one in wanted)
        {
            foreach (var other in offered)
            {
                var likeness = Compare(one, other);
                if (likeness > best)
                    best = likeness;
                if (best == Likeness.Same)
                    return best;
            }
        }

        return best;
    }

    public static MetadataMatch Match(LocalSong song, MetadataCandidate candidate)
    {
        var (wantedBase, _) = SplitTitle(song.Title);
        var (offeredBase, _) = SplitTitle(candidate.Title);
        var title = Compare(wantedBase, offeredBase);
        var knowsArtist = song.Artists.Any(a => !string.IsNullOrWhiteSpace(a));
        var artist = knowsArtist ? CompareArtists(song, candidate) : Likeness.No;
        var wantedVersions = VersionsOf(song);
        var offeredVersions = VersionsOf(candidate);
        var sameVersion = wantedVersions.SetEquals(offeredVersions);
        double? difference = song.Seconds > 0 && candidate.LengthMs > 0 ? Math.Abs(candidate.LengthMs / 1000.0 - song.Seconds) : null;
        var sameLength = difference <= SameLength;
        var album = Compact(song.Album);
        var sameAlbum = album.Length > 0 && candidate.Releases.Any(r => Compact(r.Title) == album);

        var level = title == Likeness.Same && artist == Likeness.Same && sameVersion && sameLength ? MatchLevel.Strong
            : title != Likeness.No && artist != Likeness.No ? MatchLevel.Possible
            : title == Likeness.Same && sameLength ? MatchLevel.Possible
            : MatchLevel.None;

        var reasons = new List<string>
        {
            (title, artist) switch
            {
                (Likeness.Same, Likeness.Same) => "title and artist match",
                _ => (title switch { Likeness.Same => "title matches", Likeness.Close => "title is close", _ => "title differs" })
                    + ", " + (!knowsArtist ? "no artist to compare" : artist switch { Likeness.Same => "artist matches", Likeness.Close => "artist is close", _ => "artist differs" }),
            },
            difference is not { } seconds ? "length can't be compared"
                : sameLength ? string.Create(CultureInfo.InvariantCulture, $"length within {Math.Max(1, Math.Ceiling(seconds)):0} s")
                : string.Create(CultureInfo.InvariantCulture, $"length differs by {Math.Round(seconds):0} s"),
        };
        if (!sameVersion)
        {
            var theirs = offeredVersions.Except(wantedVersions).ToList();
            var ours = wantedVersions.Except(offeredVersions).ToList();
            reasons.Add(theirs.Count > 0 ? "another version (" + string.Join(", ", theirs.Order()) + ")" : "not marked as " + string.Join(", ", ours.Order()));
        }

        var reason = string.Join(", ", reasons);
        return new MetadataMatch(candidate, level, char.ToUpperInvariant(reason[0]) + reason[1..], title, artist, sameVersion, difference, sameAlbum);
    }

    /// <summary>Matches, best first.</summary>
    public static IReadOnlyList<MetadataMatch> Rank(LocalSong song, IEnumerable<MetadataCandidate> candidates) =>
    [
        .. candidates.Select(c => Match(song, c))
            .OrderByDescending(m => m.Level)
            .ThenByDescending(m => m.SameVersion)
            .ThenByDescending(m => m.Title)
            .ThenByDescending(m => m.Artist)
            .ThenBy(m => m.LengthDifference is not { } d ? 2 : d <= SameLength ? 0 : d <= 10 ? 1 : 3)
            .ThenByDescending(m => m.SameAlbum)
            .ThenByDescending(m => m.Candidate.Releases.Any(r => r.Official))
            // Of many entries for one song, the one on the most releases is the one people mean.
            .ThenByDescending(m => m.Candidate.Releases.Count)
            .ThenBy(m => m.LengthDifference ?? double.MaxValue)
            .ThenBy(m => m.Candidate.FirstYear is 0 ? uint.MaxValue : m.Candidate.FirstYear),
    ];

    /// <summary>
    /// The releases to offer for a recording, one of each, the likeliest first: the song's own album, then official albums,
    /// shorter releases, and compilations and live ones last; the earliest of each kind first.
    /// </summary>
    public static IReadOnlyList<MetadataRelease> Releases(MetadataCandidate candidate, string album)
    {
        static string Date(MetadataRelease release) => release.Date is { Length: > 0 } date ? date : "9999";
        static int Kind(MetadataRelease release) =>
            release.SecondaryTypes.Contains("Live") ? 5
            : release.SecondaryTypes.Contains("Compilation") ? 4
            : release.SecondaryTypes.Count > 0 ? 3
            : release.PrimaryType switch { "Album" => 0, "EP" => 1, "Single" => 2, _ => 3 };

        var wanted = Compact(album);
        return
        [
            .. candidate.Releases
                .GroupBy(r => r.GroupId ?? Compact(r.Title))
                .Select(g => g.OrderByDescending(r => r.Official).ThenBy(Date, StringComparer.Ordinal).ThenByDescending(r => r.Track > 0).First())
                .OrderByDescending(r => wanted.Length > 0 && Compact(r.Title) == wanted)
                .ThenByDescending(r => r.Official)
                .ThenBy(Kind)
                .ThenBy(Date, StringComparer.Ordinal)
                .Take(25),
        ];
    }
}
