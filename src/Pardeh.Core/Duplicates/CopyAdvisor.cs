using System.Globalization;
using System.Text.RegularExpressions;
using Pardeh.Core.Changes;
using Pardeh.Core.Cleanup;

namespace Pardeh.Core.Duplicates;

/// <param name="Keep">The copy suggested to keep (its index in the group), or null when it is not clear.</param>
/// <param name="Why">The reason for the suggestion, or why there is none.</param>
/// <param name="Notes">Per copy, what speaks for or against it.</param>
public sealed record CopyAdvice(int? Keep, string Why, IReadOnlyList<IReadOnlyList<string>> Notes);

/// <summary>A part of a song's information that can be taken from any copy.</summary>
public enum MergeField
{
    Title,
    Artists,
    Album,
    AlbumArtists,
    Year,
    Track,
    Genres,
    Cover,
}

/// <summary>
/// Which copy to suggest keeping, and which copy's information to suggest. It suggests nothing when it is not clear. What a
/// file says about its own quality is only that: a low-quality song saved again at a high bitrate sounds no better.
/// </summary>
public static partial class CopyAdvisor
{
    // A stated bitrate has to be this much higher to count for anything.
    private const double MuchHigher = 1.5;

    [GeneratedRegex(@"(?:\s-\scopy(?:\s*\(\d+\))?|\(\d{1,2}\))$", RegexOptions.IgnoreCase)]
    private static partial Regex CopyMark();

    public static bool IsFilled(SongCopy copy, MergeField field)
    {
        var tags = copy.Facts.Tags;
        return field switch
        {
            MergeField.Title => Good(tags.Title),
            MergeField.Artists => tags.Artists is { Length: > 0 } a && a.All(Good),
            MergeField.Album => Good(tags.Album),
            MergeField.AlbumArtists => tags.AlbumArtists is { Length: > 0 } b && b.All(Good),
            MergeField.Year => tags.Year > 0,
            MergeField.Track => tags.Track > 0,
            MergeField.Genres => tags.Genres is { Length: > 0 } g && g.All(Good),
            MergeField.Cover => copy.Facts.CoverHash is not null,
            _ => false,
        };
    }

    // A value worth having: not empty, not a stand-in like "Track 01", and with no download site's words in it.
    private static bool Good(string? value) => value is not null && !FileNameReader.IsPlaceholder(value) && !JunkText.Clean(value).Changed;

    /// <summary>The field as words, for a table ("" when empty).</summary>
    public static string Show(SongCopy copy, MergeField field)
    {
        var tags = copy.Facts.Tags;
        return field switch
        {
            MergeField.Title => tags.Title ?? string.Empty,
            MergeField.Artists => string.Join("; ", tags.Artists ?? []),
            MergeField.Album => tags.Album ?? string.Empty,
            MergeField.AlbumArtists => string.Join("; ", tags.AlbumArtists ?? []),
            MergeField.Year => tags.Year > 0 ? tags.Year.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
            MergeField.Track => tags.Track > 0 ? tags.Track.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
            MergeField.Genres => string.Join("; ", tags.Genres ?? []),
            MergeField.Cover => copy.Facts.CoverHash is null ? string.Empty : $"cover, {Math.Max(1, copy.Facts.CoverBytes / 1024)} KB",
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Where each part of the kept file's information would come from: the kept copy's own where it is good, else another
    /// copy's good value. Only parts that would change are listed.
    /// </summary>
    public static IReadOnlyDictionary<MergeField, int> DefaultSources(IReadOnlyList<SongCopy> copies, int keep)
    {
        var sources = new Dictionary<MergeField, int>();
        foreach (var field in Enum.GetValues<MergeField>())
        {
            if (IsFilled(copies[keep], field))
                continue;
            var others = Enumerable.Range(0, copies.Count).Where(i => i != keep && IsFilled(copies[i], field));
            // The largest cover; else the value of the copy with the most good information.
            var best = field == MergeField.Cover
                ? others.OrderByDescending(i => copies[i].Facts.CoverBytes).Cast<int?>().FirstOrDefault()
                : others.OrderByDescending(i => InfoScore(copies[i])).Cast<int?>().FirstOrDefault();
            if (best is { } from)
                sources[field] = from;
        }

        return sources;
    }

    private static int InfoScore(SongCopy copy)
    {
        var tags = copy.Facts.Tags;
        var score = Enum.GetValues<MergeField>().Count(f => IsFilled(copy, f)) * 2 + (IsFilled(copy, MergeField.Cover) ? 1 : 0);
        var texts = new[] { tags.Title, tags.Album, tags.Comment, tags.Copyright }.Concat(tags.Artists ?? []).Concat(tags.Genres ?? []).Concat(tags.Composers ?? [])
            .Concat((tags.Texts ?? []).Select(t => t.Value));
        return score - texts.Count(t => !string.IsNullOrEmpty(t) && JunkText.Clean(t).Changed) * 2;
    }

    // Lower is plainer: no "(1)" or "- Copy", no site's words.
    private static int NameClutter(SongCopy copy)
    {
        var stem = Path.GetFileNameWithoutExtension(copy.Facts.FileName);
        return (CopyMark().IsMatch(stem) ? 2 : 0) + (JunkText.Clean(stem.Replace('_', ' ')).Changed ? 1 : 0);
    }

    private static string Quality(SongCopy copy)
    {
        var format = copy.Scan is { Codec.Length: > 0 } scan ? scan.Codec.ToUpperInvariant() : Path.GetExtension(copy.Facts.FileName).TrimStart('.').ToUpperInvariant();
        if (format.StartsWith("PCM_", StringComparison.Ordinal))
            format = "PCM";
        return copy.Scan is { Lossless: true } ? format + ", lossless"
            : copy.Facts.Kbps > 0 ? string.Create(CultureInfo.InvariantCulture, $"{format} {copy.Facts.Kbps} kb/s")
            : format;
    }

    /// <summary>The quality a file states for itself, such as "MP3 320 kb/s".</summary>
    public static string StatedQuality(SongCopy copy) => Quality(copy);

    private static IReadOnlyList<string> NotesFor(SongCopy copy)
    {
        var notes = new List<string>();
        if (copy.Unreadable)
            notes.Add("Can't be read to the end");
        else if (copy.Scan is { CutShort: true })
            notes.Add("Cut short: less sound than it says");
        if (copy.Item.Rating > 0)
            notes.Add($"Rated {copy.Item.Rating} of 5");
        if (copy.Item.PlayCount > 0)
            notes.Add(copy.Item.PlayCount == 1 ? "Played once" : $"Played {copy.Item.PlayCount} times");
        if (copy.Aliases.Count > 0)
            notes.Add($"One file, listed {copy.Aliases.Count + 1} ways");
        if (!copy.KnownFile)
            notes.Add("Can't be removed here: it may be the same file as another");
        return notes;
    }

    private static bool Sound(SongCopy copy) => !copy.Unreadable && copy.Scan is not { CutShort: true };

    /// <param name="extra">More that speaks for a copy, by index (in playlists, has lyrics): higher is better.</param>
    public static CopyAdvice Advise(DuplicateGroup group, Func<int, int>? extra = null)
    {
        var copies = group.Copies;
        var notes = copies.Select(NotesFor).ToList();
        CopyAdvice None(string why) => new(null, why, notes);
        CopyAdvice Keep(int index, string why) => new(index, why, notes);
        var used = (int i) => copies[i].Item.Rating * 10 + Math.Min(copies[i].Item.PlayCount, 9) + (extra?.Invoke(i) ?? 0) * 3;
        var all = Enumerable.Range(0, copies.Count).ToList();
        var whole = all.Where(i => Sound(copies[i])).ToList();
        if (whole.Count == 0)
            return None("No copy could be read to its end.");
        if (whole.Count == 1)
            return Keep(whole[0], "The only copy that reads complete.");

        // The first of these that tells the candidates apart decides.
        (int Index, string Why) ByInfo(IReadOnlyList<int> among, string sameSound)
        {
            var info = among.OrderByDescending(i => InfoScore(copies[i])).ToList();
            if (InfoScore(copies[info[0]]) > InfoScore(copies[info[1]]))
                return (info[0], sameSound + " This one has the cleanest, fullest information.");
            var best = among.Where(i => InfoScore(copies[i]) == InfoScore(copies[info[0]])).ToList();
            var cover = best.OrderByDescending(i => copies[i].Facts.CoverBytes).ToList();
            if (cover.Count > 1 && copies[cover[0]].Facts.CoverBytes > copies[cover[1]].Facts.CoverBytes)
                return (cover[0], sameSound + " This one has the larger cover.");
            var kept = best.OrderByDescending(used).ToList();
            if (kept.Count > 1 && used(kept[0]) > used(kept[1]))
                return (kept[0], sameSound + " This is the one you use (rating, plays, playlists or lyrics).");
            var plain = best.OrderBy(i => NameClutter(copies[i])).ThenBy(i => copies[i].Facts.FileName.Length).ThenBy(i => copies[i].Source.Length)
                .ThenBy(i => copies[i].Source, StringComparer.Ordinal).First();
            return (plain, sameSound + " Any would do; this one has the plainer name.");
        }

        if (group.IsExact)
        {
            var (index, why) = ByInfo(whole, group.Kind == DuplicateKind.Identical ? "The files are the same." : "The sound is the same.");
            return Keep(index, why);
        }

        // Only probably the same recording: suggest on sound alone, and only on a clear difference.
        if (whole.Any(i => copies[i].Scan is null))
            return None("The sound has not been compared yet.");
        if (whole.Max(i => copies[i].Seconds) - whole.Min(i => copies[i].Seconds) > 1)
            return None("The lengths differ: one may have an added intro or end. Listen before choosing.");
        var lossless = whole.Where(i => copies[i].Scan!.Lossless).ToList();
        if (lossless.Count == 1)
        {
            return Keep(lossless[0], "The only lossless file. That is its format; a lossless file made from a lossy one sounds no better.");
        }

        if (lossless.Count > 1)
            return None("More than one lossless file: nothing says which is better.");
        if (whole.Select(i => copies[i].Scan!.Codec).Distinct().Count() > 1)
            return None("Different formats: their bitrates can't be compared.");
        if (whole.Any(i => copies[i].Facts.Kbps <= 0))
            return None("A bitrate is not stated.");
        var byRate = whole.OrderByDescending(i => copies[i].Facts.Kbps).ToList();
        if (copies[byRate[0]].Facts.Kbps >= copies[byRate[1]].Facts.Kbps * MuchHigher)
        {
            return Keep(byRate[0], string.Create(CultureInfo.InvariantCulture,
                $"A much higher stated bitrate ({copies[byRate[0]].Facts.Kbps} against {copies[byRate[1]].Facts.Kbps} kb/s). That is no proof of better sound: listen if it matters."));
        }

        return None("Their stated quality is about the same. Listen before choosing.");
    }
}
