using Pardeh.Core.Changes;
using Pardeh.Core.Cleanup;

namespace Pardeh.Core.OnlineInfo;

/// <summary>One thing to search for, and the song as it would be if that reading of it is the right one.</summary>
public sealed record LookupStep(MetadataSearch Search, LocalSong Song);

/// <param name="Matches">Best first.</param>
/// <param name="Searched">What was sent last, for the user.</param>
public sealed record LookupResult(IReadOnlyList<MetadataMatch> Matches, MetadataSearch? Searched);

/// <summary>
/// Looks one song up: the cleaned tags first; if nothing matches, the file name, then artist and title swapped, then the title
/// alone (the artist is then compared here, with the database's other spellings, which finds names in another script).
/// </summary>
public sealed class MetadataLookup(IMetadataProvider provider)
{
    private readonly Dictionary<MetadataSearch, IReadOnlyList<MetadataCandidate>> _found = [];

    public IMetadataProvider Provider => provider;

    /// <summary>
    /// What is known of a song once cleaned, and the searches to try for it. Only suggestions that are safe by themselves
    /// stand in for the tags.
    /// </summary>
    /// <param name="tags">The file's tags now.</param>
    /// <param name="local">The local checks of it.</param>
    public static IReadOnlyList<LookupStep> Steps(TagFields tags, CleanupProposal local, double seconds, JunkRules? rules = null)
    {
        string Cleaned(CleanupField field, string current)
        {
            var suggestion = local.Suggestions.FirstOrDefault(s => s.Field == field);
            var value = suggestion is { Selected: true } || suggestion is not null && FileNameReader.IsPlaceholder(current) ? suggestion.Suggested : current;
            return FileNameReader.IsPlaceholder(value) ? string.Empty : value.Trim();
        }

        var title = Cleaned(CleanupField.Title, tags.Title ?? string.Empty);
        var artists = Cleaned(CleanupField.Artists, string.Join("; ", tags.Artists ?? []))
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var album = Cleaned(CleanupField.Album, tags.Album ?? string.Empty);
        var name = FileNameReader.Read(local.FileName, rules ?? JunkRules.None);
        var fileTitle = name.Title is { } t && !FileNameReader.IsPlaceholder(t) ? t.Trim() : string.Empty;
        var fileArtist = name.Artist?.Trim() ?? string.Empty;
        var hints = local.FileName;

        var steps = new List<LookupStep>();
        var seen = new HashSet<(string, string)>();
        void Add(string songTitle, IReadOnlyList<string> songArtists, bool withArtist = true)
        {
            if (songTitle.Length == 0)
                return;
            var song = new LocalSong(songTitle, songArtists, album, seconds, hints);
            var artist = withArtist ? MainArtist(songArtists) : null;
            if (withArtist && artist is null)
                return;
            var search = new MetadataSearch(Words(MetadataMatcher.SplitTitle(songTitle).Base), artist, MetadataMatcher.WantsLive(song));
            if (seen.Add((TextFold.Key(search.Title), TextFold.Key(search.Artist))))
                steps.Add(new LookupStep(search, song));
        }

        Add(title, artists);
        Add(fileTitle, fileArtist.Length > 0 ? [fileArtist] : []);
        if (artists.Count == 1)
            Add(artists[0], [title]);

        var every = artists.Append(fileArtist).Where(a => a.Length > 0).Distinct().ToList();
        Add(title.Length > 0 ? title : fileTitle, every, withArtist: false);
        return steps;
    }

    // Persian typed on an Arabic keyboard is searched as Persian.
    private static string Words(string text) => text.Replace('ي', 'ی').Replace('ك', 'ک');

    private static string? MainArtist(IReadOnlyList<string> artists)
    {
        if (artists.FirstOrDefault(a => a.Length > 0) is not { } first)
            return null;
        var main = MetadataMatcher.SplitTitle(first).Base;
        return main.Length > 0 ? Words(main) : null;
    }

    /// <summary>Drops what is remembered, when the user asks for a fresh answer.</summary>
    public void Forget()
    {
        lock (_found)
            _found.Clear();
    }

    private async Task<IReadOnlyList<MetadataCandidate>> Search(MetadataSearch search, CancellationToken cancel)
    {
        // Kept while the app runs: asking the same thing twice is a request wasted. Failures are not kept.
        lock (_found)
        {
            if (_found.TryGetValue(search, out var known))
                return known;
        }

        var found = await provider.SearchAsync(search, cancel).ConfigureAwait(false);
        lock (_found)
        {
            if (_found.Count >= 40)
                _found.Clear();
            _found[search] = found;
        }

        return found;
    }

    /// <summary>Tries the steps in order and stops at the first with a match.</summary>
    public async Task<LookupResult> FindAsync(IReadOnlyList<LookupStep> steps, CancellationToken cancel = default)
    {
        MetadataSearch? last = null;
        foreach (var step in steps)
        {
            cancel.ThrowIfCancellationRequested();
            last = step.Search;
            var matches = MetadataMatcher.Rank(step.Song, await Search(step.Search, cancel).ConfigureAwait(false))
                .Where(m => m.Level != MatchLevel.None).ToList();
            if (matches.Count > 0)
                return new LookupResult(matches, last);
        }

        return new LookupResult([], last);
    }

    /// <summary>A search the user typed. Everything found is listed, poor matches too: the user knows what was meant.</summary>
    /// <param name="song">The song on this device, for its length and album.</param>
    public async Task<LookupResult> SearchAsync(string title, string artist, LocalSong song, CancellationToken cancel = default)
    {
        var typed = song with { Title = title.Trim(), Artists = artist.Trim().Length > 0 ? [artist.Trim()] : [], VersionHints = string.Empty };
        if (typed.Title.Length == 0)
            return new LookupResult([], null);
        var search = new MetadataSearch(Words(MetadataMatcher.SplitTitle(typed.Title).Base), MainArtist(typed.Artists), MetadataMatcher.WantsLive(typed));
        var matches = MetadataMatcher.Rank(typed, await Search(search, cancel).ConfigureAwait(false));
        return new LookupResult(matches, search);
    }
}
