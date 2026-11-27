using Pardeh.Core.Library;

namespace Pardeh.Core.Duplicates;

/// <summary>One physical file with what the check knows of it. However many ways the library lists it, it is one copy.</summary>
public sealed record SongCopy(LibraryItem Item, AudioFacts Facts, SongName Name)
{
    public string Source => Item.Path;

    /// <summary>Other listings of this same file.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    public string? ContentHash { get; init; }

    public AudioScan? Scan { get; init; }

    /// <summary>The sound could not be read to its end.</summary>
    public bool Unreadable { get; init; }

    /// <summary>False when it can't be told whether this is a file of its own: it is then never removed as a copy.</summary>
    public bool KnownFile { get; init; } = true;

    public double Seconds => Scan is { Seconds: > 0 } scan ? scan.Seconds : Facts.Seconds > 0 ? Facts.Seconds : Item.DurationSeconds;

    /// <summary>What "not duplicates" is remembered by: the sound where it is known, which renaming and retagging keep.</summary>
    public string Mark => Scan?.Digest ?? ContentHash ?? Source;
}

/// <summary>How alike the copies of a group are: the least alike pair decides.</summary>
public enum DuplicateKind
{
    /// <summary>The same bytes.</summary>
    Identical,

    /// <summary>The same sound, with different details.</summary>
    SameAudio,

    /// <summary>Probably the same recording, by names and length. Someone has to listen or look.</summary>
    Probable,
}

public sealed record DuplicateGroup(DuplicateKind Kind, IReadOnlyList<SongCopy> Copies)
{
    /// <summary>Keeps one group apart from another from one check to the next.</summary>
    public string Id => string.Join('\n', Copies.Select(c => c.Source).Order(StringComparer.Ordinal));

    /// <summary>Only groups with the same sound in every copy may be dealt with all at once.</summary>
    public bool IsExact => Kind != DuplicateKind.Probable;
}

public enum RelatedList
{
    Copies,
    OtherVersions,
    Similar,
}

public sealed record RelatedSong(SongCopy Copy, RelatedList List, string Reason);

/// <summary>Groups copies and finds what belongs with a song. It only looks at what was read before: no file is opened.</summary>
public static class DuplicateFinder
{
    private static DuplicateKind? Exact(SongCopy a, SongCopy b) =>
        a.ContentHash is not null && a.ContentHash == b.ContentHash ? DuplicateKind.Identical
        : a.Scan is { } first && b.Scan is { } second && first.Digest == second.Digest ? DuplicateKind.SameAudio
        : null;

    private static NameVerdict Names(SongCopy a, SongCopy b) => a.Name.Compare(b.Name, a.Seconds, b.Seconds);

    public static IReadOnlyList<DuplicateGroup> Groups(IReadOnlyList<SongCopy> copies, DuplicateStore store)
    {
        // First the copies with the same bytes or the same sound. A copy joins a set only if the user has not told it
        // apart from any member.
        var sets = new List<List<SongCopy>>();
        var bySound = new Dictionary<string, List<List<SongCopy>>>(StringComparer.Ordinal);
        foreach (var copy in copies.OrderBy(c => c.Source, StringComparer.Ordinal))
        {
            var keys = new[] { copy.Scan?.Digest is { } sound ? "s" + sound : null, copy.ContentHash is { } bytes ? "c" + bytes : null }.OfType<string>().ToList();
            var home = keys.SelectMany(k => bySound.GetValueOrDefault(k) ?? []).FirstOrDefault(s => s.All(o => !store.AreApart(o, copy)));
            if (home is null)
                sets.Add(home = []);
            home.Add(copy);
            foreach (var key in keys)
            {
                if (!bySound.TryGetValue(key, out var list))
                    bySound[key] = list = [];
                if (!list.Contains(home))
                    list.Add(home);
            }
        }

        // Then sets that are probably the same recording. Every pair of the joined group must be within the length
        // tolerance (no chains), and no pair may name different versions or be told apart by the user.
        var of = new Dictionary<SongCopy, List<SongCopy>>(ReferenceEqualityComparer.Instance);
        foreach (var set in sets)
            foreach (var copy in set)
                of[copy] = set;

        bool MayJoin(List<SongCopy> first, List<SongCopy> second) => first.All(a => second.All(b =>
            !store.AreApart(a, b) && a.Seconds > 0 && b.Seconds > 0 && Math.Abs(a.Seconds - b.Seconds) <= SongName.SameLength
            && !a.Unreadable && !b.Unreadable && a.Name.Versions.SetEquals(b.Name.Versions)
            && (a.Name.Base != b.Name.Base || Names(a, b).Relation == NameRelation.Probable)));

        foreach (var bucket in copies.Where(c => c.Name.Bucket.Length > 0).GroupBy(c => c.Name.Bucket, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var members = bucket.OrderBy(c => c.Seconds).ThenBy(c => c.Source, StringComparer.Ordinal).ToList();
            for (var i = 0; i < members.Count; i++)
            {
                for (var j = i + 1; j < members.Count; j++)
                {
                    var (first, second) = (of[members[i]], of[members[j]]);
                    if (ReferenceEquals(first, second) || Names(members[i], members[j]).Relation != NameRelation.Probable || !MayJoin(first, second))
                        continue;
                    var joined = first.Concat(second).ToList();
                    foreach (var copy in joined)
                        of[copy] = joined;
                }
            }
        }

        var groups = new List<DuplicateGroup>();
        var seen = new HashSet<List<SongCopy>>(ReferenceEqualityComparer.Instance);
        foreach (var group in of.Values)
        {
            if (group.Count < 2 || !seen.Add(group))
                continue;
            var ordered = group.OrderBy(c => c.Source, StringComparer.OrdinalIgnoreCase).ToList();
            var kind = ordered.Skip(1).All(c => Exact(ordered[0], c) == DuplicateKind.Identical) ? DuplicateKind.Identical
                : ordered.Skip(1).All(c => ordered[0].Scan is { } a && c.Scan is { } b && a.Digest == b.Digest || Exact(ordered[0], c) == DuplicateKind.Identical)
                    ? DuplicateKind.SameAudio
                    : DuplicateKind.Probable;
            groups.Add(new DuplicateGroup(kind, ordered));
        }

        return [.. groups.OrderBy(g => g.Kind).ThenBy(g => g.Copies[0].Name.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(g => g.Id, StringComparer.Ordinal)];
    }

    /// <summary>What belongs with one song: its copies, other versions of it and songs with close names, each with why.</summary>
    public static IReadOnlyList<RelatedSong> Related(SongCopy song, IReadOnlyList<SongCopy> copies, IReadOnlyList<DuplicateGroup> groups, DuplicateStore store)
    {
        var together = groups.FirstOrDefault(g => g.Copies.Any(c => c.Source == song.Source));
        var found = new List<RelatedSong>();
        foreach (var other in copies)
        {
            if (other.Source == song.Source || store.AreUnrelated(song, other))
                continue;
            if (together?.Copies.Any(c => c.Source == other.Source) == true)
            {
                found.Add(new RelatedSong(other, RelatedList.Copies, Exact(song, other) switch
                {
                    DuplicateKind.Identical => "Identical file",
                    DuplicateKind.SameAudio => "Same audio, different details",
                    _ => "Probably the same recording: " + Names(song, other).Reason.ToLowerInvariant(),
                }));
                continue;
            }

            var verdict = Names(song, other);
            // Told apart by the user, or kept out of the group by another of its members.
            var apart = store.AreApart(song, other);
            switch (verdict.Relation)
            {
                case NameRelation.Probable:
                    found.Add(new RelatedSong(other, RelatedList.Similar, apart ? "You said these are not duplicates" : verdict.Reason + ", but not close enough to the other copies"));
                    break;
                case NameRelation.OtherVersion:
                    found.Add(new RelatedSong(other, RelatedList.OtherVersions, verdict.Reason));
                    break;
                case NameRelation.Similar:
                    found.Add(new RelatedSong(other, RelatedList.Similar, verdict.Reason));
                    break;
                case NameRelation.None when apart && Exact(song, other) is not null:
                    found.Add(new RelatedSong(other, RelatedList.Similar, "Same audio; you said these are not duplicates"));
                    break;
            }
        }

        return [.. found.OrderBy(r => r.List).ThenBy(r => r.Copy.Name.Title, StringComparer.CurrentCultureIgnoreCase)];
    }
}
