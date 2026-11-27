using Pardeh.Core.Changes;
using Pardeh.Core.Cleanup;

namespace Pardeh.Core.OnlineInfo;

/// <summary>The match whose details the user took.</summary>
public sealed record OnlineUse(MetadataCandidate Candidate, LocalSong Song);

/// <summary>A song the user wants changed in a batch from online results.</summary>
/// <param name="Reviewed">What the user chose after looking at it, or null to take what is safe.</param>
/// <param name="Used">The match a reviewed choice took details from, or null.</param>
public sealed record BatchPick(string Source, OnlineEntry? Entry, CleanupChoice? Reviewed = null, OnlineUse? Used = null);

/// <param name="Choices">What to change, ready for the planner.</param>
/// <param name="Used">By source: the match the choice takes details from.</param>
/// <param name="Covers">How many songs get a downloaded cover.</param>
/// <param name="CoversFailed">Songs whose cover could not be asked for.</param>
/// <param name="CoversLeft">Songs given no cover because the release it would come from is in doubt.</param>
/// <param name="NotSure">Songs left out because they are no longer sure enough to change without looking.</param>
public sealed record PreparedBatch(IReadOnlyList<CleanupChoice> Choices, IReadOnlyDictionary<string, OnlineUse> Used, int Covers, int CoversFailed, int CoversLeft,
    int NotSure);

/// <summary>
/// Turns the ticked songs into changes, from the files as they are now. Covers are only downloaded here: for songs the user
/// has chosen to change, when covers were asked for. Nothing is written.
/// </summary>
public static class OnlineBatch
{
    // This many covers failing one after another: the service is down, and the rest are not asked for.
    private const int GiveUpAfter = 3;

    /// <param name="covers">Also add a cover where there is none or a suspected site picture.</param>
    /// <param name="pattern">The pattern to rename by, or null to keep names. A reviewed song keeps its own choice.</param>
    public static async Task<PreparedBatch> PrepareAsync(IReadOnlyList<BatchPick> picks, Func<string, Task<SongFacts?>> read, IMetadataProvider provider, bool covers,
        string? pattern, bool databaseScript, IProgress<(int Done, int Total)>? progress = null, CancellationToken cancel = default)
    {
        var choices = new List<CleanupChoice>();
        var used = new Dictionary<string, OnlineUse>(StringComparer.Ordinal);
        // One copy of each picture, however many songs of an album take it.
        var fetched = new Dictionary<string, CoverImage?>(StringComparer.Ordinal);
        int added = 0, failed = 0, left = 0, notSure = 0, failedInARow = 0;
        for (var i = 0; i < picks.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            progress?.Report((i, picks.Count));
            var pick = picks[i];
            if (pick.Reviewed is { } reviewed)
            {
                choices.Add(reviewed);
                if (pick.Used is { } taken)
                    used[pick.Source] = taken;
                continue;
            }

            SongFacts? facts;
            try
            {
                facts = await read(pick.Source);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TagEditException or NotSupportedException)
            {
                facts = null;
            }

            if (facts is null || pick.Entry is not { } entry)
            {
                notSure++;
                continue;
            }

            var matches = entry.Matches();
            var verdict = OnlineJudge.Judge(facts.Local, facts.Tags, entry.Song, matches, databaseScript);
            var onlyCover = verdict is { Group: OnlineGroup.Checked, WantsCover: true };
            if (verdict.Group != OnlineGroup.Strong && !(covers && onlyCover))
            {
                notSure++;
                continue;
            }

            var proposal = verdict.Proposal;
            var gotCover = false;
            if (covers && verdict.WantsCover && verdict is { Match: { } match, Release: { } release })
            {
                var doubts = OnlineJudge.Doubts(facts.Local, facts.Tags, entry.Song, matches, match, release);
                if (doubts.Album)
                {
                    left++;
                }
                else
                {
                    var known = fetched.TryGetValue(release.Id, out var image);
                    if (!known && failedInARow < GiveUpAfter)
                    {
                        try
                        {
                            image = await provider.CoverAsync(release, cancel, small: true);
                            fetched[release.Id] = image;
                            failedInARow = 0;
                        }
                        catch (MetadataException)
                        {
                            failed++;
                            failedInARow++;
                        }
                    }
                    else if (!known)
                    {
                        failed++;
                    }

                    if (image is not null)
                    {
                        proposal = OnlineSuggestions.Merge(facts.Local, facts.Tags, match, release, image, databaseScript, doubts);
                        gotCover = proposal.Suggestions.Any(s => s is { NewCover: not null, Selected: true });
                    }
                }
            }

            var accepted = proposal.Suggestions.Where(s => s.Selected).ToList();
            if (accepted.Count == 0)
                continue; // only a cover was wanted, and there is none
            choices.Add(new CleanupChoice(pick.Source, accepted, pattern));
            if (gotCover)
                added++;
            if (verdict.Match is { } found)
                used[pick.Source] = new OnlineUse(found.Candidate, entry.Song);
        }

        progress?.Report((picks.Count, picks.Count));
        return new PreparedBatch(choices, used, added, failed, left, notSure);
    }
}
