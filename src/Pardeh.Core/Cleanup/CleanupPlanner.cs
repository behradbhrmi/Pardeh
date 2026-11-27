using Pardeh.Core.Changes;
using Pardeh.Core.Library;

namespace Pardeh.Core.Cleanup;

/// <summary>What the user accepted for one song.</summary>
/// <param name="Accepted">The ticked suggestions, with any edits.</param>
/// <param name="Pattern">The pattern to rename the file by, or null to keep its name.</param>
public sealed record CleanupChoice(string Source, IReadOnlyList<CleanupSuggestion> Accepted, string? Pattern);

/// <summary>The changes a set of choices comes to, counted for the summary shown before anything is applied.</summary>
public sealed record CleanupPlan(IReadOnlyList<ChangeRequest> Requests, int Files, int Titles, int Artists, int Albums, int Covers, int OtherFields, int Renames,
    IReadOnlyList<string> Problems)
{
    public bool IsEmpty => Requests.Count == 0;

    /// <summary>"42 files: 40 titles, 3 artists, 18 covers, 0 renames".</summary>
    public string Summary
    {
        get
        {
            static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
            var parts = new List<string>();
            if (Titles > 0) parts.Add(Count(Titles, "title", "titles"));
            if (Artists > 0) parts.Add(Count(Artists, "artist", "artists"));
            if (Albums > 0) parts.Add(Count(Albums, "album", "albums"));
            if (Covers > 0) parts.Add(Count(Covers, "cover removed", "covers removed"));
            if (OtherFields > 0) parts.Add(Count(OtherFields, "other field", "other fields"));
            parts.Add(Count(Renames, "rename", "renames"));
            return $"{Count(Files, "file", "files")}: {string.Join(", ", parts)}";
        }
    }
}

public static class CleanupPlanner
{
    /// <summary>
    /// Reads each file's tags as they are now and works out the changes. A suggestion whose field was changed since it was
    /// made is left out.
    /// </summary>
    public static async Task<CleanupPlan> PlanAsync(FileChangeService service, IReadOnlyList<CleanupChoice> choices, IProgress<(int Done, int Total)>? progress = null,
        CancellationToken cancel = default)
    {
        var requests = new List<ChangeRequest>();
        var problems = new List<string>();
        int files = 0, titles = 0, artists = 0, albums = 0, covers = 0, other = 0, renames = 0;
        for (var i = 0; i < choices.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            var choice = choices[i];
            progress?.Report((i, choices.Count));
            var name = MediaSources.DisplayName(choice.Source);
            try
            {
                var current = await service.ReadTagsAsync(choice.Source);
                var change = CleanupAnalyzer.ToChange(current, choice.Accepted, CleanupAnalyzer.HashCover);
                var effective = current.Differences(change);
                var changed = false;
                if (!effective.IsEmpty)
                {
                    requests.Add(ChangeRequest.EditTags(choice.Source, effective));
                    changed = true;
                    if (effective.Title is not null) titles++;
                    if (effective.Artists is not null) artists++;
                    if (effective.Album is not null) albums++;
                    if (effective.Pictures is not null) covers++;
                    other += (effective.AlbumArtists is null ? 0 : 1) + (effective.Genres is null ? 0 : 1) + (effective.Composers is null ? 0 : 1)
                        + (effective.Comment is null ? 0 : 1) + (effective.Copyright is null ? 0 : 1) + (effective.Texts?.Length ?? 0);
                }

                if (choice.Pattern is { } pattern && NewName(pattern, current, effective, name) is { } renamed && renamed != name)
                {
                    requests.Add(ChangeRequest.Rename(choice.Source, renamed));
                    changed = true;
                    renames++;
                }

                if (changed)
                    files++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TagEditException or NotSupportedException)
            {
                problems.Add($"{name}: {ex.Message}");
            }
        }

        progress?.Report((choices.Count, choices.Count));
        return new CleanupPlan(requests, files, titles, artists, albums, covers, other, renames, problems);
    }

    /// <summary>The name a pattern gives the file once <paramref name="change"/> is made, or null to keep the old one.</summary>
    public static string? NewName(string pattern, TagFields current, TagFields change, string fileName)
    {
        var after = current with
        {
            Title = change.Title ?? current.Title,
            Artists = change.Artists ?? current.Artists,
            AlbumArtists = change.AlbumArtists ?? current.AlbumArtists,
            Album = change.Album ?? current.Album,
            Genres = change.Genres ?? current.Genres,
            Year = change.Year ?? current.Year,
            Track = change.Track ?? current.Track,
        };
        return NamePattern.Apply(pattern, after, Path.GetExtension(fileName));
    }
}
