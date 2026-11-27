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

    /// <summary>Set when the removed file was an extra copy: what goes to the copy that stayed.</summary>
    public MergePlan? Merge { get; init; }
}

/// <summary>A track's rating and play history.</summary>
public sealed record TrackHistory(int Rating, int PlayCount, DateTime LastPlayed)
{
    public static TrackHistory Of(LibraryItem? item) => new(item?.Rating ?? 0, item?.PlayCount ?? 0, item?.LastPlayed ?? default);
}

/// <summary>
/// What a removed copy hands to the kept one. Values are the results, not amounts to add, so carrying it out twice does no
/// harm.
/// </summary>
public sealed record MergePlan(string Kept)
{
    /// <summary>The ways the removed file was listed that are gone, itself first.</summary>
    public List<string> Gone { get; init; } = [];

    public LyricsTrackKey? KeptKey { get; init; }

    /// <summary>Lyrics data copied to the kept copy: only what it did not have.</summary>
    public LyricsPieces Lyrics { get; init; }

    public TrackHistory Before { get; init; } = new(0, 0, default);
    public TrackHistory After { get; init; } = new(0, 0, default);

    /// <summary>The kept copy became a favourite because a removed one was.</summary>
    public bool FavoriteAdded { get; init; }

    public List<FavoriteEntry> Favorites { get; init; } = [];
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

    /// <summary>What the user said about which songs are not duplicates.</summary>
    public Duplicates.DuplicateStore? Duplicates { get; init; }

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
            Duplicates?.Move(source, newSource);
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

    // ---- An extra copy: removed, with what it had going to the copy that stays ----------------------

    /// <param name="gone">The removed file's source first, then other listings of it that went with it.</param>
    public RelinkPlan PlanMerge(IReadOnlyList<string> gone, ReferenceSnapshot before, string kept)
    {
        var keptKey = LyricsTrackKey.From(reader.Read(kept));
        var had = TrackHistory.Of(library.Find(kept));
        var theirs = TrackHistory.Of(before.Item);
        var favorites = gone.Select(g => Favorites?.Find(g)).OfType<FavoriteEntry>().DistinctBy(f => f.Key).ToList();
        return new RelinkPlan
        {
            Playlists = [.. gone.SelectMany(g => Playlists?.FindEntries(g) ?? []).DistinctBy(e => (e.Playlist, e.Line))],
            Merge = new MergePlan(kept)
            {
                Gone = [.. gone],
                KeptKey = keptKey,
                Lyrics = Lyrics?.PlanTransfer(before.Key, keptKey) ?? LyricsPieces.None,
                Before = had,
                After = new TrackHistory(had.Rating > 0 ? Math.Max(had.Rating, theirs.Rating) : theirs.Rating, had.PlayCount + theirs.PlayCount,
                    had.LastPlayed > theirs.LastPlayed ? had.LastPlayed : theirs.LastPlayed),
                FavoriteAdded = favorites.Count > 0 && Favorites?.IsFavorite(kept) == false,
                Favorites = favorites,
            },
        };
    }

    /// <summary>
    /// Playlists and the queue point at the kept copy, which takes the higher rating, the plays of both, the later date, the
    /// favourite mark and the lyrics data it lacks. The removed copy's own lyrics data stays for when it comes back, as do its
    /// bookmarks. Safe to run twice.
    /// </summary>
    public void ApplyMerge(string source, ReferenceSnapshot before, RelinkPlan plan)
    {
        var merge = plan.Merge!;
        var fresh = reader.Read(merge.Kept);
        foreach (var gone in merge.Gone)
            library.Detach(gone);
        library.SetHistory(merge.Kept, merge.After.Rating, merge.After.PlayCount, merge.After.LastPlayed);
        if (merge.KeptKey is { } key)
            Lyrics?.Transfer(before.Key, key, merge.Lyrics, keepOld: true);
        foreach (var line in plan.Playlists.GroupBy(e => e.Text.Trim()))
            Playlists?.Repoint(line.ToList(), line.Key, merge.Kept);
        if (merge.FavoriteAdded && merge.Favorites.Count > 0)
            Favorites?.Restore(merge.Favorites[0], merge.Kept);
        foreach (var favorite in merge.Favorites)
            Favorites?.Set(favorite.Path, false);
        foreach (var gone in merge.Gone)
            Queue?.Relink(gone, merge.Kept, fresh);
        LyricsChanged?.Invoke(fresh);
    }

    // The removed copy is back. What the kept one was given is taken back, unless it has changed since.
    private void UndoMerge(string source, ReferenceSnapshot before, string restored, RelinkPlan plan)
    {
        var merge = plan.Merge!;
        if (library.Find(merge.Kept) is { } kept && TrackHistory.Of(kept) == merge.After)
            library.SetHistory(merge.Kept, merge.Before.Rating, merge.Before.PlayCount, merge.Before.LastPlayed);
        if (merge.KeptKey is { } key)
            Lyrics?.Withdraw(before.Key, key, merge.Lyrics);
        foreach (var line in plan.Playlists.GroupBy(e => e.Text.Trim()))
            Playlists?.PointBack(line.ToList(), merge.Kept, string.Equals(line.Key, source, StringComparison.OrdinalIgnoreCase) ? restored : line.Key);
        if (merge.FavoriteAdded)
            Favorites?.Set(merge.Kept, false);
        foreach (var favorite in merge.Favorites)
            Favorites?.Restore(favorite, string.Equals(favorite.Path, source, StringComparison.OrdinalIgnoreCase) ? restored : favorite.Path);
    }

    /// <summary>The removed file is back, perhaps under another name. The queue is left as it is. Safe to run twice.</summary>
    public void ApplyRestore(string source, ReferenceSnapshot before, string restored, FileStat file, RelinkPlan removal)
    {
        var fresh = reader.Read(restored);
        library.Relink(source, new SourceFile(restored, file.Size, file.ModifiedUtc, file.Name), before.Item);
        // A file the user brought back by hand (from a Recycle Bin) may have been listed again, as a new song, before the
        // undo: it still gets the rating and play history it left with.
        if (before.Item is { } had && library.Find(restored) is { } now)
        {
            library.SetHistory(restored, now.Rating != 0 ? now.Rating : had.Rating, Math.Max(now.PlayCount, had.PlayCount),
                now.LastPlayed > had.LastPlayed ? now.LastPlayed : had.LastPlayed);
        }

        if (removal.Merge is not null)
        {
            UndoMerge(source, before, restored, removal);
        }
        else
        {
            Playlists?.InsertEntries(removal.Playlists, restored);
            if (removal.Favorite is { } favorite)
                Favorites?.Restore(favorite, restored);
        }

        if (!string.Equals(source, restored, StringComparison.Ordinal))
        {
            var key = LyricsTrackKey.From(fresh);
            if (Lyrics is { } lyrics)
                lyrics.Transfer(before.Key, key, lyrics.PlanTransfer(before.Key, key), KeyUsedElsewhere(before.Key, source, restored));
            Bookmarks?.Move(source, restored);
            VideoPositions?.Move(source, restored);
            Cleanup?.Move(source, restored);
            Duplicates?.Move(source, restored);
        }

        LyricsChanged?.Invoke(fresh);
    }
}
