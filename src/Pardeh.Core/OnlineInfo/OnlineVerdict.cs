using Pardeh.Core.Changes;
using Pardeh.Core.Cleanup;

namespace Pardeh.Core.OnlineInfo;

/// <summary>Values of a strong match that are still not sure enough to tick.</summary>
/// <param name="Album">The album, its artist and the track number: the release may well not be the one the file is from.</param>
/// <param name="Year">Other entries for the song say it came out earlier.</param>
public readonly record struct OnlineDoubts(bool Album, bool Year);

/// <summary>What a look-up that nobody has looked at comes to.</summary>
public enum OnlineGroup
{
    /// <summary>A strong match with something safe to change: ticked in a batch.</summary>
    Strong,

    /// <summary>Someone has to look.</summary>
    Review,

    NoMatch,

    /// <summary>A strong match, and nothing it would change by itself.</summary>
    Checked,
}

/// <param name="Proposal">The local fixes with the best match's values beside them (no cover: covers are fetched later).</param>
/// <param name="Why">For a song that needs a look: why.</param>
/// <param name="WantsCover">A strong match for a song with no cover, or with a suspected site picture.</param>
public sealed record OnlineVerdict(OnlineGroup Group, CleanupProposal Proposal, MetadataMatch? Match, MetadataRelease? Release, string Why, bool WantsCover);

/// <summary>
/// Decides what may be done with a look-up without anyone reading it. A wrong match applied in a batch is the main risk, so
/// whatever is in doubt is left for the user.
/// </summary>
public static class OnlineJudge
{
    /// <summary>What stays unticked when this match and release are taken.</summary>
    public static OnlineDoubts Doubts(CleanupProposal local, TagFields tags, LocalSong song, IReadOnlyList<MetadataMatch> matches, MetadataMatch match,
        MetadataRelease? release)
    {
        // A match the file's own details contradict fills nothing in by itself.
        if (TagsDisagree(tags, local, match))
            return new OnlineDoubts(true, true);

        var album = release is not null && !MetadataMatcher.SameName(release.Title, song.Album)
            && !(release is { Official: true, SecondaryTypes.Count: 0, PrimaryType: "Album" or "EP" or "Single" }
                && !MetadataMatcher.SameName(release.AlbumArtist, "Various Artists"));
        var year = match.Candidate.FirstYear;
        var earlier = year > 0 && matches.Any(m => m.Level == MatchLevel.Strong && m.Candidate.FirstYear is > 0 and var other && other + 1 < year);
        return new OnlineDoubts(album, earlier);
    }

    /// <summary>
    /// Whether the file's own title or artist, when it has a real one, says something else than the match. That happens when
    /// the match came from the file name or from title and artist swapped.
    /// </summary>
    private static bool TagsDisagree(TagFields tags, CleanupProposal local, MetadataMatch match)
    {
        string Own(CleanupField field, string current)
        {
            var value = local.Suggestions.FirstOrDefault(s => s.Field == field) is { Selected: true } better ? better.Suggested : current;
            return FileNameReader.IsPlaceholder(value) ? string.Empty : value.Trim();
        }

        var title = Own(CleanupField.Title, tags.Title ?? string.Empty);
        if (title.Length > 0 && !MetadataMatcher.SameName(MetadataMatcher.SplitTitle(title).Base, MetadataMatcher.SplitTitle(match.Candidate.Title).Base))
            return true;
        var artists = Own(CleanupField.Artists, string.Join("; ", tags.Artists ?? []))
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return artists.Length > 0
            && MetadataMatcher.Match(new LocalSong(match.Candidate.Title, artists, string.Empty, 0), match.Candidate).Artist != Likeness.Same;
    }

    /// <param name="song">The song as the search that found the matches read it.</param>
    /// <param name="matches">Best first.</param>
    public static OnlineVerdict Judge(CleanupProposal local, TagFields tags, LocalSong song, IReadOnlyList<MetadataMatch> matches, bool databaseScript)
    {
        if (matches.Count == 0 || matches[0].Level == MatchLevel.None)
            return new OnlineVerdict(OnlineGroup.NoMatch, local, null, null, "Nothing usable on MusicBrainz", false);

        var match = matches[0];
        var release = MetadataMatcher.Releases(match.Candidate, song.Album).FirstOrDefault();
        var proposal = OnlineSuggestions.Merge(local, tags, match, release, null, databaseScript, Doubts(local, tags, song, matches, match, release));
        if (match.Level != MatchLevel.Strong)
            return new OnlineVerdict(OnlineGroup.Review, proposal, match, release, match.Reason, false);
        if (TagsDisagree(tags, local, match))
            return new OnlineVerdict(OnlineGroup.Review, proposal, match, release, "The file's own title or artist says something else", false);

        var wantsCover = tags.Pictures is not { Length: > 0 } || local.Suggestions.Any(s => s is { Field: CleanupField.Cover, Reason: CleanupReason.RepeatedCover });
        return new OnlineVerdict(proposal.Suggestions.Any(s => s.Selected) ? OnlineGroup.Strong : OnlineGroup.Checked, proposal, match, release, match.Reason, wantsCover);
    }
}
