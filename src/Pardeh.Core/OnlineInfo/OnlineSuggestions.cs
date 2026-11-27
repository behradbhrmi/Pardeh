using System.Globalization;
using Pardeh.Core.Changes;
using Pardeh.Core.Cleanup;

namespace Pardeh.Core.OnlineInfo;

/// <summary>
/// Puts what a database says beside what the local checks found, as one list of suggestions. Ticked to begin with, and only for
/// a strong match: fields that are empty or website text. A value that looks good stays, and so does a name written in another
/// script, unless the user chose the database's.
/// </summary>
public static class OnlineSuggestions
{
    private const string Joiner = "; ";

    private enum Script
    {
        None,
        Arabic,
        Other,
    }

    // Persian is written in the Arabic script; a text with any of it counts as that.
    private static Script ScriptOf(string text)
    {
        var script = Script.None;
        foreach (var c in text)
        {
            if (c is >= '؀' and <= 'ۿ' or >= 'ݐ' and <= 'ݿ' or >= 'ﭐ' and <= '﻿' && char.IsLetter(c))
                return Script.Arabic;
            if (char.IsLetter(c))
                script = Script.Other;
        }

        return script;
    }

    private static bool OtherScript(string first, string second) =>
        ScriptOf(first) is var a && ScriptOf(second) is var b && a != Script.None && b != Script.None && a != b;

    /// <param name="local">The local checks of the song.</param>
    /// <param name="current">The file's tags now, with pictures.</param>
    /// <param name="release">The release the album, year and cover come from, or null.</param>
    /// <param name="cover">That release's front cover, or null.</param>
    /// <param name="databaseScript">The user's choice to take names in the database's script.</param>
    /// <param name="doubts">What stays unticked even for a strong match.</param>
    public static CleanupProposal Merge(CleanupProposal local, TagFields current, MetadataMatch match, MetadataRelease? release, CoverImage? cover,
        bool databaseScript, OnlineDoubts doubts = default)
    {
        var strong = match.Level == MatchLevel.Strong;
        var candidate = match.Candidate;
        var merged = local.Suggestions.ToList();
        // The script the song itself uses, for fields it has nothing in.
        var own = string.Join(' ', new[]
        {
            Best(local, CleanupField.Title, current.Title ?? string.Empty),
            Best(local, CleanupField.Artists, string.Join(Joiner, current.Artists ?? [])),
        });

        void Offer(CleanupField field, string now, string online, bool doubtful = false)
        {
            online = online.Trim();
            var at = merged.FindIndex(s => s.Field == field);
            var known = at >= 0 ? merged[at] : null;
            if (online.Length == 0 || online == now)
                return;

            // What this field should be taken for: website text, nothing, or a value of the user's.
            var junk = known is { Reason: CleanupReason.Junk or CleanupReason.FromFileName or CleanupReason.BrokenText };
            var kept = junk ? known!.Suggested : now;
            var empty = kept.Length == 0 || field is CleanupField.Title or CleanupField.Artists && FileNameReader.IsPlaceholder(kept);
            if (online == kept && known is not null)
                return;
            var otherScript = field is not (CleanupField.Year or CleanupField.Track) && OtherScript(empty ? own : kept, online);

            if (known is not null && (!strong || doubtful || otherScript && !databaseScript))
            {
                // The local fix stays; the database's value is there to be chosen.
                merged[at] = known with { Online = online };
                return;
            }

            var take = strong && !doubtful && (junk || empty || otherScript && databaseScript) && (!otherScript || databaseScript);
            var suggestion = new CleanupSuggestion(field, null, now, online, CleanupReason.Online, take) { Alternative = junk && !empty ? kept : null };
            if (at >= 0)
                merged[at] = suggestion;
            else
                merged.Add(suggestion);
        }

        Offer(CleanupField.Title, current.Title ?? string.Empty, candidate.Title);
        Offer(CleanupField.Artists, string.Join(Joiner, current.Artists ?? []), string.Join(Joiner, candidate.Artists.Select(a => a.Name)));
        if (release is not null)
        {
            Offer(CleanupField.AlbumArtists, string.Join(Joiner, current.AlbumArtists ?? []), release.AlbumArtist, doubts.Album);
            Offer(CleanupField.Album, current.Album ?? string.Empty, release.Title, doubts.Album);
        }

        // The year it first came out, whichever release the user has.
        Offer(CleanupField.Year, Number(current.Year), Number(candidate.FirstYear is var year && year != 0 ? year : release?.Year), doubts.Year);
        if (release is not null)
            Offer(CleanupField.Track, Number(current.Track), Number(release.Track), doubts.Album);

        if (cover is not null && !(current.Pictures ?? []).Any(p => p.Data.AsSpan().SequenceEqual(cover.Data)))
        {
            var suspect = merged.FirstOrDefault(s => s.Field == CleanupField.Cover);
            var has = current.Pictures is { Length: > 0 };
            var take = strong && (!has || suspect is not null);
            // Replacing says what removing said. When not sure, removing stays a choice of its own.
            if (take && suspect is not null)
                merged.Remove(suspect);
            merged.Add(new CleanupSuggestion(CleanupField.Cover, null, string.Empty, string.Empty, CleanupReason.Online, take)
            {
                CoverHash = suspect?.CoverHash,
                NewCover = new TagPicture("FrontCover", cover.MimeType, string.Empty, cover.Data),
            });
        }

        return local with { Suggestions = [.. merged.OrderBy(Order)] };
    }

    private static int Order(CleanupSuggestion suggestion) => suggestion.Field switch
    {
        CleanupField.Title => 0,
        CleanupField.Artists => 1,
        CleanupField.AlbumArtists => 2,
        CleanupField.Album => 3,
        CleanupField.Year => 4,
        CleanupField.Track => 5,
        CleanupField.Genres => 6,
        CleanupField.Composers => 7,
        CleanupField.Comment => 8,
        CleanupField.Copyright => 9,
        CleanupField.Cover => 10,
        _ => 11,
    };

    private static string Best(CleanupProposal local, CleanupField field, string current) =>
        local.Suggestions.FirstOrDefault(s => s.Field == field) is { Selected: true } better ? better.Suggested : current;

    private static string Number(uint? value) => value is > 0 ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
}
