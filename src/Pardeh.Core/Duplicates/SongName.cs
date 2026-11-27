using System.Globalization;
using System.Text.RegularExpressions;
using Pardeh.Core.Cleanup;
using Pardeh.Core.OnlineInfo;

namespace Pardeh.Core.Duplicates;

/// <summary>How two songs that are not byte for byte the same stand to each other, by their names and lengths.</summary>
public enum NameRelation
{
    None,

    /// <summary>The same title, artists and version, and the same length: probably the same recording.</summary>
    Probable,

    /// <summary>The same song in another form (a remix, a live take, another length): never a duplicate.</summary>
    OtherVersion,

    /// <summary>Names close enough to look at, with nothing to say they are one song.</summary>
    Similar,
}

/// <param name="Reason">Why, for the user.</param>
public sealed record NameVerdict(NameRelation Relation, string Reason);

/// <summary>
/// A song's names, cleaned for comparing: junk taken out, and the file name used where the tags say nothing. A wrong
/// "duplicate" is the worst mistake here, so whatever can't be compared counts against it.
/// </summary>
public sealed partial class SongName
{
    /// <summary>Probable copies differ by no more than this many seconds.</summary>
    public const double SameLength = 2;

    private SongName()
    {
    }

    /// <summary>The cleaned title, as shown.</summary>
    public string Title { get; private init; } = string.Empty;

    /// <summary>The title without featured names and version words, compacted.</summary>
    public string Base { get; private init; } = string.Empty;

    /// <summary>Every artist named, featured ones too, compacted.</summary>
    public IReadOnlySet<string> Artists { get; private init; } = new HashSet<string>();

    /// <summary>Kinds of version named in the title, the file name or the album.</summary>
    public IReadOnlySet<string> Versions { get; private init; } = new HashSet<string>();

    private string VersionText { get; init; } = string.Empty;

    private string Album { get; init; } = string.Empty;

    // The file's name, cleaned and compacted.
    private string Stem { get; init; } = string.Empty;

    /// <summary>Groups songs that could be one: the same base title.</summary>
    public string Bucket => Base;

    [GeneratedRegex(@"^\s*0\d(?:\s*[\-._)]\s*|\s+)(?=\p{L})")]
    private static partial Regex PaddedNumber();

    [GeneratedRegex(@"(?:\s+-\s+copy(?:\s*\(\d+\))?|\s*\(\d{1,2}\))+\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CopyMark();

    [GeneratedRegex(@"(?:^|[\s\(\[])(?:feat\.?|ft\.?|featuring)\s+(?<who>[^\(\)\[\]]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Featured();

    private static string Cleaned(string? value, JunkRules rules)
    {
        var text = JunkText.Clean(value ?? string.Empty, rules).Text.Trim();
        return FileNameReader.IsPlaceholder(text) ? string.Empty : text;
    }

    private static string StripNumber(string title, uint track)
    {
        var result = PaddedNumber().Replace(title, string.Empty);
        if (track > 0 && result == title)
            result = Regex.Replace(title, "^\\s*" + track.ToString(CultureInfo.InvariantCulture) + @"(?:\s*[\-._)]\s+|\s+-\s*)(?=\p{L})", string.Empty);
        return result.Trim().Length > 0 ? result.Trim() : title;
    }

    public static SongName Read(string fileName, Changes.TagFields tags, JunkRules rules)
    {
        var stem = CopyMark().Replace(Path.GetFileNameWithoutExtension(fileName), string.Empty);
        var guess = FileNameReader.Read(stem + Path.GetExtension(fileName), rules);
        var title = Cleaned(tags.Title, rules);
        if (title.Length == 0)
            title = guess.Title?.Trim() ?? string.Empty;
        title = StripNumber(title, tags.Track ?? 0);

        var fields = (tags.Artists ?? []).Select(a => Cleaned(a, rules)).Where(a => a.Length > 0).ToList();
        if (fields.Count == 0)
            fields = (tags.AlbumArtists ?? []).Select(a => Cleaned(a, rules)).Where(a => a.Length > 0).ToList();
        if (fields.Count == 0 && guess.Artist is { Length: > 0 } named)
            fields.Add(named);
        var guests = Featured().Matches(title).Select(m => m.Groups["who"].Value);
        var artists = fields.Concat(guests).SelectMany(MetadataMatcher.SplitArtists)
            .Select(a => MetadataMatcher.Compact(a.StartsWith("the ", StringComparison.OrdinalIgnoreCase) ? a[4..] : a))
            .Where(a => a.Length > 0).ToHashSet(StringComparer.Ordinal);

        var (bare, versions) = MetadataMatcher.SplitTitle(title);
        var cleanStem = JunkText.Clean(stem.Replace('_', ' '), rules).Text;
        var album = Cleaned(tags.Album, rules);
        var every = new HashSet<string>(versions, StringComparer.Ordinal);
        every.UnionWith(MetadataMatcher.VersionsIn(cleanStem));
        every.UnionWith(MetadataMatcher.VersionsIn(album));
        var key = MetadataMatcher.Compact(bare);
        return new SongName
        {
            Title = title,
            Base = key,
            Artists = artists,
            Versions = every,
            VersionText = MetadataMatcher.VersionText(title),
            Album = MetadataMatcher.Compact(album),
            Stem = MetadataMatcher.Compact(cleanStem),
        };
    }

    // Tags may be a site's words, the same on every song it gave out. Two file names that both say something else, neither
    // mentioning the title, speak against trusting them.
    private bool FileNamesDisagree(SongName other) =>
        Stem.Length >= 6 && other.Stem.Length >= 6 && !Stem.Contains(Base, StringComparison.Ordinal) && !other.Stem.Contains(other.Base, StringComparison.Ordinal)
        && !Stem.Contains(other.Stem, StringComparison.Ordinal) && !other.Stem.Contains(Stem, StringComparison.Ordinal)
        && MetadataMatcher.Compare(Stem, other.Stem) == Likeness.No;

    private static string Seconds(double difference) => Math.Max(1, Math.Round(difference)).ToString(CultureInfo.InvariantCulture) + " s";

    /// <param name="seconds">This song's length, or 0 when unknown.</param>
    public NameVerdict Compare(SongName other, double seconds, double otherSeconds)
    {
        if (Base.Length == 0 || other.Base.Length == 0)
            return new NameVerdict(NameRelation.None, string.Empty);
        var sharedArtist = Artists.Overlaps(other.Artists);
        double? difference = seconds > 0 && otherSeconds > 0 ? Math.Abs(seconds - otherSeconds) : null;
        if (Base != other.Base)
        {
            return sharedArtist && MetadataMatcher.Compare(Base, other.Base) == Likeness.Close
                ? new NameVerdict(NameRelation.Similar, "Close title, same artist")
                : new NameVerdict(NameRelation.None, string.Empty);
        }

        if (!sharedArtist)
            return new NameVerdict(NameRelation.Similar, Artists.Count == 0 || other.Artists.Count == 0 ? "Same title, no artist to compare" : "Same title, another artist");
        if (!Versions.SetEquals(other.Versions) || VersionText != other.VersionText)
        {
            var named = Versions.Union(other.Versions).Except(Versions.Intersect(other.Versions)).Order().ToList();
            return new NameVerdict(NameRelation.OtherVersion, named.Count > 0 ? "Another version (" + string.Join(", ", named) + ")" : "Another version, by its title");
        }

        if (!Artists.SetEquals(other.Artists))
            return new NameVerdict(NameRelation.OtherVersion, "Different artists are listed");
        if (difference is not { } apart)
            return new NameVerdict(NameRelation.Similar, "Same title and artist, but a length is unknown");
        if (apart > SameLength)
            return new NameVerdict(NameRelation.OtherVersion, "Length differs by " + Seconds(apart));
        if (Artists.Contains(Base))
            return new NameVerdict(NameRelation.Similar, "Title and artist are the same words, which says little");
        if (MetadataMatcher.IsCommonTitle(Title) && (Album.Length == 0 || Album != other.Album))
            return new NameVerdict(NameRelation.Similar, "Many albums have a track with this title");
        if (FileNamesDisagree(other))
            return new NameVerdict(NameRelation.Similar, "Same tags, but the file names say different things");
        return new NameVerdict(NameRelation.Probable, apart < 0.5 ? "Same title and artist, same length" : "Same title and artist, length within " + Seconds(apart));
    }
}
