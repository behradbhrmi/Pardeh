using Pardeh.Core.Changes;
using Pardeh.Core.Library;

namespace Pardeh.Core.Duplicates;

/// <summary>What to do with one group: the copy that stays, the ones that go, and where its information comes from.</summary>
/// <param name="Keep">The index of the copy that stays.</param>
/// <param name="Remove">The indexes of the copies to remove.</param>
/// <param name="Merge">Parts of the kept file's information to take from other copies; empty: its file is not written.</param>
public sealed record GroupChoice(DuplicateGroup Group, int Keep, IReadOnlyList<int> Remove, IReadOnlyDictionary<MergeField, int> Merge);

public sealed record DuplicatePlan(IReadOnlyList<ChangeRequest> Requests, IReadOnlyList<string> Problems, int Removals, int Merged, long Bytes)
{
    public bool IsEmpty => Removals == 0 && Merged == 0;
}

/// <summary>
/// Turns choices into changes for the file change service, looking at every file again first: whatever has changed since the
/// check, or is not shown to be a file of its own, is left alone.
/// </summary>
public static class DuplicatePlanner
{
    /// <param name="permanent">Delete for good instead of the trash or holding folder (one group at a time, after a warning).</param>
    public static async Task<DuplicatePlan> PlanAsync(FileChangeService changes, IDuplicateAnalyzer analyzer, IReadOnlyList<GroupChoice> choices,
        IProgress<(int Done, int Total)>? progress = null, CancellationToken cancel = default, bool permanent = false)
    {
        var requests = new List<ChangeRequest>();
        var merges = new List<ChangeRequest>();
        var removals = new List<ChangeRequest>();
        var problems = new List<string>();
        var bytes = 0L;
        var done = 0;
        foreach (var choice in choices)
        {
            progress?.Report((done++, choices.Count));
            cancel.ThrowIfCancellationRequested();
            var copies = choice.Group.Copies;
            var kept = copies[choice.Keep];
            var name = kept.Facts.FileName;
            var removing = choice.Remove.Where(i => i != choice.Keep && i >= 0 && i < copies.Count).Distinct().ToList();
            if (removing.Count != choice.Remove.Distinct().Count())
                problems.Add($"{name}: the copy to keep is never removed.");
            if (await Task.Run(() => Stamp(changes, kept), cancel) is not { } keptStamp)
            {
                problems.Add($"{name}: the copy to keep is missing or has changed since the check.");
                continue;
            }

            ChangeRequest? merge = null;
            if (choice.Merge.Count > 0)
            {
                if (changes.WhyUnchangeable(kept.Source) is { } why)
                {
                    problems.Add($"{name}: its information can't be changed. {why}");
                    continue; // nothing is removed when what should be saved from it can't be
                }

                try
                {
                    merge = await MergeAsync(changes, choice);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TagEditException or NotSupportedException)
                {
                    problems.Add($"{name}: the information could not be read. {ex.Message}");
                    continue;
                }
            }

            var gone = new List<ChangeRequest>();
            foreach (var index in removing)
            {
                var copy = copies[index];
                var (stamp, why) = await Task.Run(() => Check(changes, analyzer, kept, copy, cancel), cancel);
                if (why is not null)
                {
                    problems.Add($"{copy.Facts.FileName}: {why}");
                    continue;
                }

                var request = permanent ? new ChangeRequest(copy.Source, ChangeKind.Delete) : changes.Remove(copy.Source);
                gone.Add(request with { Keep = new KeptCopy(kept.Source) { Kept = keptStamp, Removed = stamp, Aliases = copy.Aliases } });
                bytes += stamp!.Size;
            }

            if (merge is not null)
                merges.Add(merge);
            removals.AddRange(gone);
        }

        progress?.Report((choices.Count, choices.Count));
        // Every kept file gets its information before anything is removed.
        requests.AddRange(merges);
        requests.AddRange(removals);
        return new DuplicatePlan(requests, problems, removals.Count, merges.Count, bytes);
    }

    // The file as it is now, if that is still what was checked.
    private static FileStamp? Stamp(FileChangeService changes, SongCopy copy)
    {
        if (changes.Stat(copy.Source) is not { } now || now.Size == 0)
            return null;
        // A content URI added by itself has no size in the library.
        return copy.Item.FileSize > 0 && !new FileStamp(copy.Item.FileSize, copy.Item.Modified).Matches(now) ? null : new FileStamp(now.Size, now.ModifiedUtc);
    }

    private static (FileStamp? Stamp, string? Why) Check(FileChangeService changes, IDuplicateAnalyzer analyzer, SongCopy kept, SongCopy copy, CancellationToken cancel)
    {
        if (!copy.KnownFile)
            return (null, "Pardeh can't tell whether this is a file of its own, so it is not removed. It can be hidden.");
        if (changes.WhyUnchangeable(copy.Source) is { } unchangeable)
            return (null, unchangeable);
        if (Stamp(changes, copy) is not { } stamp)
            return (null, "The file is missing or has changed since the check.");
        if (PhysicalFiles.WhyNotDistinct(copy.Source, kept.Source) is { } same)
            return (null, same);

        // "The same file" and "the same sound" are checked once more on the two files themselves.
        if (kept.ContentHash is { } bytes && bytes == copy.ContentHash)
        {
            if (analyzer.ContentHash(kept.Source, cancel) is not { } now || now != analyzer.ContentHash(copy.Source, cancel))
                return (null, "The two files are no longer identical. Check again.");
        }
        else if (kept.Scan is { } first && copy.Scan is { } second && first.Digest == second.Digest)
        {
            if (analyzer.Scan(kept.Source, cancel) is not { } now || now.Digest != analyzer.Scan(copy.Source, cancel)?.Digest)
                return (null, "The two no longer have the same sound. Check again.");
        }

        return (stamp, null);
    }

    // The kept file's new information, or null when it already has it.
    private static async Task<ChangeRequest?> MergeAsync(FileChangeService changes, GroupChoice choice)
    {
        var copies = choice.Group.Copies;
        var kept = copies[choice.Keep];
        var current = await changes.ReadTagsAsync(kept.Source);
        var read = new Dictionary<int, TagFields>();
        var wanted = new TagFields();
        foreach (var (field, from) in choice.Merge)
        {
            if (from == choice.Keep || from < 0 || from >= copies.Count)
                continue;
            if (!read.TryGetValue(from, out var theirs))
                read[from] = theirs = await changes.ReadTagsAsync(copies[from].Source);
            wanted = field switch
            {
                MergeField.Title => wanted with { Title = theirs.Title ?? string.Empty },
                MergeField.Artists => wanted with { Artists = theirs.Artists ?? [] },
                MergeField.Album => wanted with { Album = theirs.Album ?? string.Empty },
                MergeField.AlbumArtists => wanted with { AlbumArtists = theirs.AlbumArtists ?? [] },
                MergeField.Year => wanted with { Year = theirs.Year ?? 0 },
                MergeField.Track => wanted with { Track = theirs.Track ?? 0 },
                MergeField.Genres => wanted with { Genres = theirs.Genres ?? [] },
                MergeField.Cover => FrontOf(theirs) is { } cover
                    ? wanted with { Pictures = [cover with { Type = "FrontCover" }, .. (current.Pictures ?? []).Where(p => p.Type != "FrontCover" && !ReferenceEquals(p, FrontOf(current)))] }
                    : wanted,
                _ => wanted,
            };
        }

        return current.Differences(wanted).IsEmpty ? null : ChangeRequest.EditTags(kept.Source, current.Differences(wanted));
    }

    private static TagPicture? FrontOf(TagFields tags) =>
        tags.Pictures?.FirstOrDefault(p => p.Type == "FrontCover") ?? tags.Pictures?.FirstOrDefault();
}
