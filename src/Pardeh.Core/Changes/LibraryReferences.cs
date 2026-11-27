using Pardeh.Core.Library;
using Pardeh.Core.LyricsKit;
using Pardeh.Core.Playback;

namespace Pardeh.Core.Changes;

/// <summary>The play queue (or its saved copy, before the app has one).</summary>
public interface IQueueLink
{
    void Relink(string oldSource, string newSource, MediaItem fresh);

    void Remove(string source);
}

/// <summary>What pointed at a file before it was changed.</summary>
public sealed record ReferenceSnapshot(LyricsTrackKey Key, LibraryItem? Item);

/// <summary>What a change moves or takes out. It is saved in the journal before it is carried out, so it can be repeated.</summary>
public sealed record RelinkPlan
{
    public LyricsTrackKey? NewKey { get; init; }
    public LyricsPieces Lyrics { get; init; }

    /// <summary>What was already saved under the new key before the change.</summary>
    public LyricsPieces TargetHad { get; init; }

    /// <summary>Another track still has the old lyrics key, so the lyrics are copied.</summary>
    public bool KeepOldLyrics { get; init; }

    public List<PlaylistLine> Playlists { get; init; } = [];
    public FavoriteEntry? Favorite { get; init; }
}

/// <summary>
/// Keeps what the app remembers about a track attached to it: rating and play history, the saved lyrics, chords, timing and
/// transpose, playlists, favourites, bookmarks, video positions and the queue. Any store may be absent.
/// </summary>
public sealed class LibraryReferences(MediaLibrary library, IMediaInfoReader reader)
{
    public LyricsStore? Lyrics { get; init; }

    /// <summary>Called for each old and new source so that lyrics remembered in memory are looked up again.</summary>
    public Action<MediaItem>? LyricsChanged { get; init; }

    public PlaylistStore? Playlists { get; init; }
    public FavoritesStore? Favorites { get; init; }
    public BookmarkStore? Bookmarks { get; init; }
    public VideoPositionStore? VideoPositions { get; init; }

    /// <summary>Which songs the user told cleanup to leave alone.</summary>
    public Cleanup.CleanupStore? Cleanup { get; init; }

    public IQueueLink? Queue { get; set; }

    public ReferenceSnapshot Capture(string source) => new(LyricsTrackKey.From(reader.Read(source)), library.Find(source));

    // The length is compared first: making a key hashes text, which adds up over a large library in a long batch.
    private bool KeyUsedElsewhere(LyricsTrackKey key, params string?[] except) =>
        library.Items.Any(i => (int)Math.Round(TimeSpan.FromSeconds(i.DurationSeconds).TotalSeconds) == key.DurationSeconds
            && !except.Contains(i.Path, StringComparer.OrdinalIgnoreCase) && LyricsTrackKey.From(i.ToMediaItem()) == key);

    // ---- A rename or a tag change ---------------------------------------------------------------

    public RelinkPlan PlanMove(string source, ReferenceSnapshot before, string newSource)
    {
        var key = LyricsTrackKey.From(reader.Read(newSource));
        return new RelinkPlan
        {
            NewKey = key,
            Lyrics = Lyrics?.PlanTransfer(before.Key, key) ?? LyricsPieces.None,
            TargetHad = before.Key.Hash == key.Hash ? LyricsPieces.None : Lyrics?.PiecesOf(key) ?? LyricsPieces.None,
            KeepOldLyrics = KeyUsedElsewhere(before.Key, source, newSource),
            Playlists = source == newSource ? [] : [.. Playlists?.FindEntries(source) ?? []],
        };
    }

    /// <summary>Safe to run twice.</summary>
    public void ApplyMove(string source, ReferenceSnapshot before, string newSource, FileStat file, RelinkPlan plan)
    {
        var fresh = reader.Read(newSource);
        library.Relink(source, new SourceFile(newSource, file.Size, file.ModifiedUtc, file.Name), before.Item);
        if (plan.NewKey is { } key)
            Lyrics?.Transfer(before.Key, key, plan.Lyrics, plan.KeepOldLyrics);
        if (plan.Playlists.Count > 0)
            Playlists?.Repoint(plan.Playlists, source, newSource);
        var title = fresh.GetAttribute("Title");
        Favorites?.Move(source, newSource, title, fresh.GetAttribute("Author"));
        if (source != newSource)
        {
            Bookmarks?.Move(source, newSource);
            VideoPositions?.Move(source, newSource);
            Cleanup?.Move(source, newSource);
        }

        Queue?.Relink(source, newSource, fresh);
        LyricsChanged?.Invoke(fresh);
    }

    // ---- A removal and its undo -----------------------------------------------------------------

    public RelinkPlan PlanRemove(string source) => new()
    {
        Playlists = [.. Playlists?.FindEntries(source) ?? []],
        Favorite = Favorites?.Find(source),
    };

    /// <summary>
    /// The file has left. Lyrics, bookmarks and video positions stay where they are: they are not listed anywhere, and are
    /// found again when the file comes back. Safe to run twice.
    /// </summary>
    public void ApplyRemove(string source, RelinkPlan plan)
    {
        library.Detach(source);
        Playlists?.RemoveEntries(plan.Playlists, source);
        if (plan.Favorite is not null)
            Favorites?.Set(source, false);
        Queue?.Remove(source);
    }

    /// <summary>The removed file is back, perhaps under another name. The queue is left as it is. Safe to run twice.</summary>
    public void ApplyRestore(string source, ReferenceSnapshot before, string restored, FileStat file, RelinkPlan removal)
    {
        var fresh = reader.Read(restored);
        library.Relink(source, new SourceFile(restored, file.Size, file.ModifiedUtc, file.Name), before.Item);
        Playlists?.InsertEntries(removal.Playlists, restored);
        if (removal.Favorite is { } favorite)
            Favorites?.Restore(favorite, restored);
        if (!string.Equals(source, restored, StringComparison.Ordinal))
        {
            var key = LyricsTrackKey.From(fresh);
            if (Lyrics is { } lyrics)
                lyrics.Transfer(before.Key, key, lyrics.PlanTransfer(before.Key, key), KeyUsedElsewhere(before.Key, source, restored));
            Bookmarks?.Move(source, restored);
            VideoPositions?.Move(source, restored);
            Cleanup?.Move(source, restored);
        }

        LyricsChanged?.Invoke(fresh);
    }
}
