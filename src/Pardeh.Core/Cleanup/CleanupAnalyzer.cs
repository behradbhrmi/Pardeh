using Pardeh.Core.Changes;

namespace Pardeh.Core.Cleanup;

/// <summary>What is known about one song for the local checks.</summary>
/// <param name="Tags">Its tags, without the picture data.</param>
/// <param name="CoverHash">A hash of the first embedded picture, or null.</param>
public sealed record TrackFacts(string Source, string FileName, TagFields Tags, string? CoverHash);

public enum CleanupField
{
    Title,
    Artists,
    AlbumArtists,
    Album,
    Genres,
    Composers,
    Comment,
    Copyright,

    /// <summary>One of the other text fields; <see cref="CleanupSuggestion.TextId"/> says which.</summary>
    Text,

    Cover,

    /// <summary>Suggested only from an online look-up.</summary>
    Year,

    /// <summary>Suggested only from an online look-up.</summary>
    Track,
}

public enum CleanupReason
{
    /// <summary>A web address, account name, download line or bitrate label, or one of the user's junk phrases.</summary>
    Junk,

    /// <summary>The field is empty; the value comes from the file name.</summary>
    FromFileName,

    /// <summary>Text stored with the wrong character set.</summary>
    BrokenText,

    /// <summary>The same words on songs of many unrelated artists.</summary>
    Repeated,

    /// <summary>The same picture on songs of many unrelated artists and albums.</summary>
    RepeatedCover,

    /// <summary>What an online database has.</summary>
    Online,
}

/// <param name="Current">The value now ("; " between list entries).</param>
/// <param name="Suggested">The value suggested; empty removes it.</param>
/// <param name="Selected">Ticked to begin with: only what is safe to take without looking.</param>
public sealed record CleanupSuggestion(CleanupField Field, string? TextId, string Current, string Suggested, CleanupReason Reason, bool Selected)
{
    /// <summary>The embedded picture meant (for <see cref="CleanupField.Cover"/>).</summary>
    public string? CoverHash { get; init; }

    /// <summary>
    /// For <see cref="CleanupField.Cover"/>: a picture to put in as the front cover, in place of the one there (or of the one
    /// <see cref="CoverHash"/> names). Without it, the named picture is only removed.
    /// </summary>
    public TagPicture? NewCover { get; init; }

    /// <summary>An online value that was not taken as the suggestion, offered beside it.</summary>
    public string? Online { get; init; }

    /// <summary>The local fix that an online suggestion stands in for, offered beside it.</summary>
    public string? Alternative { get; init; }

    /// <summary>Something that only repeats across the library, which a band name or a compilation cover can do too.</summary>
    public bool IsSuspicion => Reason is CleanupReason.Repeated or CleanupReason.RepeatedCover;
}

public enum CleanupLevel
{
    /// <summary>Nothing to do.</summary>
    Clean,

    /// <summary>It has safe suggestions, and all else is only a suspicion that can be left alone.</summary>
    Ready,

    /// <summary>Someone has to look: a guess, or something missing with nothing to suggest.</summary>
    Review,
}

public sealed record CleanupProposal(string Source, string FileName, IReadOnlyList<CleanupSuggestion> Suggestions, bool MissingDetails)
{
    // A site's logo is on most of its downloads: were a suspicion enough to need a look, no batch could do the sure work.
    public CleanupLevel Level =>
        Suggestions.Count == 0 && !MissingDetails ? CleanupLevel.Clean
        : !MissingDetails && Suggestions.Any(s => s.Selected) && Suggestions.All(s => s.Selected || s.IsSuspicion) ? CleanupLevel.Ready
        : CleanupLevel.Review;

    /// <summary>Suspected words or pictures that a batch leaves alone.</summary>
    public int Suspicions => Suggestions.Count(s => !s.Selected && s.IsSuspicion);
}

/// <summary>What only a look at the whole library can tell. Both are suspicions: a band name or a compilation cover can look the same.</summary>
public sealed class LibrarySignals
{
    private const int PhraseFiles = 5, PhraseArtists = 4, CoverFiles = 4, CoverArtists = 3, CoverAlbums = 3;

    public static readonly LibrarySignals None = new([], []);

    // Words that honestly repeat across artists.
    private static readonly HashSet<string> Ordinary = new(StringComparer.Ordinal)
    {
        "remix", "live", "instrumental", "acoustic", "remastered", "remaster", "original mix", "radio edit", "edit", "version", "single", "deluxe",
        "deluxe edition", "official", "official audio", "official video", "lyric video", "extended mix", "club mix", "bonus track", "cover",
        "intro", "outro", "interlude", "feat", "demo", "unplugged", "karaoke", "greatest hits", "best of", "soundtrack", "ost", "singles",
        "ریمیکس", "اجرای زنده", "بی کلام", "تک آهنگ", "نسخه اصلی",
    };

    private LibrarySignals(HashSet<string> phrases, HashSet<string> covers)
    {
        RepeatedPhrases = phrases;
        RepeatedCovers = covers;
    }

    /// <summary>Folded with <see cref="TextFold.Key"/>.</summary>
    public IReadOnlySet<string> RepeatedPhrases { get; }

    public IReadOnlySet<string> RepeatedCovers { get; }

    private static string ArtistKey(TrackFacts track) => TextFold.Key(JunkText.Clean(string.Join(" ", track.Tags.Artists ?? [])).Text);

    public static LibrarySignals Build(IEnumerable<TrackFacts> tracks, JunkRules? rules = null)
    {
        var phrases = new Dictionary<string, (int Files, HashSet<string> Artists)>(StringComparer.Ordinal);
        var covers = new Dictionary<string, (int Files, HashSet<string> Artists, HashSet<string> Albums)>(StringComparer.Ordinal);
        foreach (var track in tracks)
        {
            var artist = ArtistKey(track);
            if (artist.Length == 0)
                continue; // without an artist there is no telling whether songs are related
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var text in new[] { track.Tags.Title, track.Tags.Album, track.Tags.Comment })
            {
                var parts = PartsOf(JunkText.Clean(text, rules).Text);
                // A whole title repeats honestly ("Home"); a part of one set apart is what sites add.
                foreach (var part in ReferenceEquals(text, track.Tags.Title) && parts.Count < 2 ? [] : parts)
                {
                    var key = TextFold.Key(part);
                    if (key.Length < 4 || key.All(c => char.IsDigit(c) || c == ' ') || Ordinary.Contains(key) || artist == key || !seen.Add(key))
                        continue;
                    if (!phrases.TryGetValue(key, out var entry))
                        entry = (0, new HashSet<string>(StringComparer.Ordinal));
                    entry.Artists.Add(artist);
                    phrases[key] = (entry.Files + 1, entry.Artists);
                }
            }

            if (track.CoverHash is { } hash)
            {
                if (!covers.TryGetValue(hash, out var entry))
                    entry = (0, new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
                entry.Artists.Add(artist);
                entry.Albums.Add(TextFold.Key(track.Tags.Album));
                covers[hash] = (entry.Files + 1, entry.Artists, entry.Albums);
            }
        }

        return new LibrarySignals(
            [.. phrases.Where(p => p.Value.Files >= PhraseFiles && p.Value.Artists.Count >= PhraseArtists).Select(p => p.Key)],
            [.. covers.Where(c => c.Value.Files >= CoverFiles && c.Value.Artists.Count >= CoverArtists && c.Value.Albums.Count >= CoverAlbums).Select(c => c.Key)]);
    }

    private static readonly string[] Separators = [" - ", " – ", " — ", "|", "(", ")", "[", "]", "{", "}", "«", "»", "::", "~"];

    internal static List<string> PartsOf(string? text) =>
        [.. (text ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}

/// <summary>The local checks of one song: no internet. It only suggests; nothing is changed here.</summary>
public static class CleanupAnalyzer
{
    private const string Joiner = "; ";

    public static CleanupProposal Analyze(TrackFacts track, LibrarySignals? signals = null, JunkRules? rules = null)
    {
        signals ??= LibrarySignals.None;
        rules ??= JunkRules.None;
        var tags = track.Tags;
        var found = new List<CleanupSuggestion>();
        var guess = FileNameReader.Read(track.FileName, rules);
        var missing = false;

        // A single value: junk first, then a broken character set, then words the library says are a site's.
        (string Value, CleanupReason Reason, bool Selected)? Better(string value, bool repeats = true)
        {
            if (value.Length == 0)
                return null;
            var junk = JunkText.Clean(value, rules);
            if (junk.Changed)
                return (Mojibake.Repair(junk.Text) ?? junk.Text, CleanupReason.Junk, Mojibake.Repair(junk.Text) is null);
            if (Mojibake.Repair(value) is { } repaired)
                return (repaired, CleanupReason.BrokenText, false);
            if (!repeats || signals.RepeatedPhrases.Count == 0)
                return null;
            var without = JunkText.Clean(value, new JunkRules(
                [.. LibrarySignals.PartsOf(value).Where(p => signals.RepeatedPhrases.Contains(TextFold.Key(p)))], rules.Keep));
            return without.Changed ? (without.Text, CleanupReason.Repeated, false) : null;
        }

        // Title and artist: what they should not be without. The file name stands in for an empty or junk one.
        void Required(CleanupField field, string current, string? fromName)
        {
            var better = Better(current, repeats: field == CleanupField.Title);
            var value = better?.Value ?? current;
            if (value.Trim().Length > 0 && !FileNameReader.IsPlaceholder(value))
            {
                if (better is { } change)
                    found.Add(new CleanupSuggestion(field, null, current, change.Value, change.Reason, change.Selected));
                return;
            }

            // A loose file name doesn't beat what cleaning leaves of the tag.
            if (fromName is not null && TextFold.Key(fromName) != TextFold.Key(current) && (guess.Structured || better is null || value.Trim().Length == 0))
            {
                found.Add(new CleanupSuggestion(field, null, current, fromName, CleanupReason.FromFileName, guess.Structured));
            }
            else
            {
                missing = true;
                // The junk still goes, even when what is left ("Track 3", or nothing) is no real name.
                if (better is { } change && change.Value != current)
                    found.Add(new CleanupSuggestion(field, null, current, change.Value, change.Reason, change.Selected));
            }
        }

        var artists = Join(tags.Artists);
        Required(CleanupField.Title, tags.Title ?? string.Empty, guess.Title);
        if (artists.Length > 0 || Join(tags.AlbumArtists).Length == 0)
        {
            if (Many(tags.Artists) is { } cleaned && cleaned.Value.Length > 0 && !FileNameReader.IsPlaceholder(cleaned.Value))
                found.Add(new CleanupSuggestion(CleanupField.Artists, null, artists, cleaned.Value, cleaned.Reason, cleaned.Selected));
            else if (artists.Length == 0 || FileNameReader.IsPlaceholder(artists) || Many(tags.Artists) is { Value.Length: 0 })
                Required(CleanupField.Artists, artists, guess.Artist);
        }

        // Several values, each cleaned by itself; those that were only junk go.
        (string Value, CleanupReason Reason, bool Selected)? Many(string[]? values, bool repeats = false)
        {
            var changes = (values ?? []).Select(v => (Old: v, New: Better(v, repeats))).ToList();
            if (changes.All(c => c.New is null))
                return null;
            var cleaned = changes.Select(c => c.New?.Value ?? c.Old).Where(v => v.Trim().Length > 0);
            var worst = changes.Where(c => c.New is not null).Select(c => c.New!.Value).OrderBy(n => n.Selected).First();
            return (string.Join(Joiner, cleaned), worst.Reason, worst.Selected);
        }

        void Optional(CleanupField field, string current, (string Value, CleanupReason Reason, bool Selected)? better)
        {
            if (better is { } change && change.Value != current)
                found.Add(new CleanupSuggestion(field, null, current, change.Value, change.Reason, change.Selected));
        }

        Optional(CleanupField.AlbumArtists, Join(tags.AlbumArtists), Many(tags.AlbumArtists));
        Optional(CleanupField.Album, tags.Album ?? string.Empty, Better(tags.Album ?? string.Empty));
        Optional(CleanupField.Genres, Join(tags.Genres), Many(tags.Genres));
        Optional(CleanupField.Composers, Join(tags.Composers), Many(tags.Composers));
        Optional(CleanupField.Comment, tags.Comment ?? string.Empty, Better(tags.Comment ?? string.Empty));
        Optional(CleanupField.Copyright, tags.Copyright ?? string.Empty, Better(tags.Copyright ?? string.Empty, repeats: false));

        foreach (var text in tags.Texts ?? [])
        {
            var values = text.Value.Split(TagText.ValueSeparator);
            var cleaned = values.Select(v => JunkText.Clean(v, rules)).ToList();
            if (!cleaned.Any(c => c.Changed))
                continue;
            var kept = string.Join(TagText.ValueSeparator, cleaned.Select(c => c.Text).Where(v => v.Trim().Length > 0));
            found.Add(new CleanupSuggestion(CleanupField.Text, text.Id, text.Value, kept, CleanupReason.Junk, true));
        }

        if (track.CoverHash is { } cover && signals.RepeatedCovers.Contains(cover))
            found.Add(new CleanupSuggestion(CleanupField.Cover, null, string.Empty, string.Empty, CleanupReason.RepeatedCover, false) { CoverHash = cover });

        return new CleanupProposal(track.Source, track.FileName, found, missing);
    }

    private static string Join(string[]? values) => string.Join(Joiner, values ?? []);

    private static string[] Split(string value) => value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// The tag change for the suggestions the user accepted, whose <see cref="CleanupSuggestion.Suggested"/> may have been
    /// edited. A suggestion whose field no longer holds what it was made from is left out.
    /// </summary>
    /// <param name="current">The file's tags now, pictures included.</param>
    public static TagFields ToChange(TagFields current, IEnumerable<CleanupSuggestion> accepted, Func<byte[], string> coverHash)
    {
        var change = new TagFields();
        var texts = new List<TagText>();
        foreach (var s in accepted)
        {
            var value = s.Suggested.Trim();
            bool Still(string? now) => (now ?? string.Empty) == s.Current;
            switch (s.Field)
            {
                case CleanupField.Title when Still(current.Title):
                    change = change with { Title = value };
                    break;
                case CleanupField.Artists when Still(Join(current.Artists)):
                    change = change with { Artists = Split(value) };
                    break;
                case CleanupField.AlbumArtists when Still(Join(current.AlbumArtists)):
                    change = change with { AlbumArtists = Split(value) };
                    break;
                case CleanupField.Album when Still(current.Album):
                    change = change with { Album = value };
                    break;
                case CleanupField.Genres when Still(Join(current.Genres)):
                    change = change with { Genres = Split(value) };
                    break;
                case CleanupField.Composers when Still(Join(current.Composers)):
                    change = change with { Composers = Split(value) };
                    break;
                case CleanupField.Comment when Still(current.Comment):
                    change = change with { Comment = value };
                    break;
                case CleanupField.Copyright when Still(current.Copyright):
                    change = change with { Copyright = value };
                    break;
                case CleanupField.Text when s.TextId is { } id && current.TextOf(id) == s.Current:
                    texts.Add(new TagText(id, s.Suggested));
                    break;
                case CleanupField.Year when Still(Digits(current.Year)) && TryNumber(value, out var year):
                    change = change with { Year = year };
                    break;
                case CleanupField.Track when Still(Digits(current.Track)) && TryNumber(value, out var number):
                    change = change with { Track = number };
                    break;
                case CleanupField.Cover when s.NewCover is { } added:
                {
                    // The new front takes the place of the old front (or the suspected picture); other pictures stay.
                    var kept = (change.Pictures ?? current.Pictures ?? []).Where(p => s.CoverHash is null || coverHash(p.Data) != s.CoverHash).ToList();
                    if (s.CoverHash is null && kept.Count > 0)
                    {
                        if (kept.RemoveAll(p => p.Type == "FrontCover") == 0)
                            kept.RemoveAt(0);
                    }
                    else
                    {
                        kept.RemoveAll(p => p.Type == "FrontCover");
                    }

                    change = change with { Pictures = [added, .. kept] };
                    break;
                }

                case CleanupField.Cover when (change.Pictures ?? current.Pictures) is { } pictures && pictures.Any(p => coverHash(p.Data) == s.CoverHash):
                    // Only the suspected picture goes; a back cover of the user's own stays.
                    change = change with { Pictures = [.. pictures.Where(p => coverHash(p.Data) != s.CoverHash)] };
                    break;
            }
        }

        return texts.Count > 0 ? change with { Texts = [.. texts] } : change;
    }

    /// <summary>A year or track number as a suggestion shows it: empty for none.</summary>
    public static string Digits(uint? value) => value is > 0 ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>Reads a year or track number; empty clears it (0). Persian digits count.</summary>
    public static bool TryNumber(string text, out uint number)
    {
        number = 0;
        var folded = TextFold.SameLength(text.Trim());
        return folded.Length == 0 || folded.Length <= 4 && uint.TryParse(folded, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out number);
    }

    public static string HashCover(byte[] data) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
}
