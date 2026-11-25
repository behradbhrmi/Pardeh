using System.Collections.Concurrent;
using Pardeh.Core.Library;
using Pardeh.Core.Playback;

namespace Pardeh.Core.LyricsKit;

/// <summary>The lyrics switches of the settings, read every time lyrics are asked for.</summary>
public sealed record LyricsOptions(bool Enabled = true, bool Online = false, bool SaveFetched = true);

/// <summary>
/// Finds the lyrics of a track. Order: the user's saved lyrics, then the local sources (tags, a file next to the track; synchronised
/// text wins over plain text, and between equals the order of <c>localSources</c>), then, only when <see cref="LyricsOptions.Online"/>
/// is on, lyrics fetched earlier and finally an online lookup. Everything runs on background threads; a result is remembered in
/// memory for the session. Nothing is ever written next to the user's music.
/// </summary>
public sealed class LyricsService
{
    /// <summary>How long a lookup that found nothing is not repeated.</summary>
    public static readonly TimeSpan NotFoundWindow = TimeSpan.FromDays(7);

    private const int MaxCached = 300;

    private sealed record Cached(Lyrics? Lyrics, bool OnlineTried);

    private readonly LyricsStore? _store;
    private readonly IReadOnlyList<ILyricsSource> _sources;
    private readonly IReadOnlyList<ILyricsProvider> _providers;
    private readonly Func<LyricsOptions> _options;
    private readonly Func<DateTime> _utcNow;
    private readonly ConcurrentDictionary<string, Cached> _cache = new(StringComparer.Ordinal);

    /// <param name="store">Where user lyrics, fetched lyrics and offsets live (null: nothing is saved).</param>
    /// <param name="localSources">Tag and sidecar readers, in order of preference.</param>
    /// <param name="providers">Online services, tried in order. Used only when the options say so.</param>
    /// <param name="options">Read on every request.</param>
    /// <param name="utcNow">The clock (tests).</param>
    public LyricsService(LyricsStore? store, IEnumerable<ILyricsSource> localSources, IEnumerable<ILyricsProvider>? providers = null,
        Func<LyricsOptions>? options = null, Func<DateTime>? utcNow = null)
    {
        _store = store;
        _sources = localSources.ToList();
        _providers = providers?.ToList() ?? [];
        _options = options ?? (() => new LyricsOptions());
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public LyricsStore? Store => _store;

    /// <summary>Where chord sheets are found (a file next to the track); null: only chords inside the lyrics and the user's own text.</summary>
    public ILyricsSource? ChordSource { get; init; }

    /// <summary>Where a track's guitar tab file is found; null: only tabs in the user's own chords text.</summary>
    public Func<MediaItem, CancellationToken, string?>? TabSource { get; init; }

    private static string CacheKey(MediaItem item) => MediaSources.IdentityKey?.Invoke(item.Source) ?? item.Source;

    /// <summary>Streams and radio have no lyrics (and nothing about them is sent anywhere).</summary>
    private static bool IsLocalTrack(MediaItem item) =>
        !string.IsNullOrEmpty(item.Source) && (MediaSources.IsContent(item.Source) || !item.Source.Contains("://", StringComparison.Ordinal) ||
            item.Source.StartsWith("file://", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The lyrics of the track, or null. Throws <see cref="OperationCanceledException"/> when <paramref name="cancel"/> is cancelled
    /// (nothing is remembered then).
    /// </summary>
    public async Task<Lyrics?> GetAsync(MediaItem item, CancellationToken cancel = default)
    {
        var options = _options();
        if (!options.Enabled || !IsLocalTrack(item))
            return null;
        cancel.ThrowIfCancellationRequested();

        var id = CacheKey(item);
        if (_cache.TryGetValue(id, out var hit) && (hit.Lyrics is not null || hit.OnlineTried || !options.Online))
            return hit.Lyrics;

        var key = LyricsTrackKey.From(item);
        var (lyrics, sheet, tab) = await Task.Run(() => (LoadLocal(item, key, options, cancel), LoadChords(item, key, cancel), LoadTab(item, key, cancel)), cancel).ConfigureAwait(false);
        lyrics = WithChordSheet(lyrics, sheet);
        var onlineTried = false;
        if (lyrics is null && options.Online)
        {
            onlineTried = true;
            lyrics = await LookupOnlineAsync(key, options, cancel).ConfigureAwait(false);
        }

        if (tab is not null)
        {
            lyrics ??= new Lyrics([], synced: false) { Source = LyricsSources.TabFile };
            lyrics.Tab = tab;
        }

        if (lyrics is not null && _store is not null)
        {
            lyrics.OffsetMs = _store.GetOffset(key);
            lyrics.Transpose = _store.GetTranspose(key);
        }

        Remember(id, new Cached(lyrics, onlineTried));
        return lyrics;
    }

    private void Remember(string id, Cached value)
    {
        if (_cache.Count >= MaxCached)
            _cache.Clear();
        _cache[id] = value;
    }

    private Lyrics? LoadLocal(MediaItem item, LyricsTrackKey key, LyricsOptions options, CancellationToken cancel)
    {
        if (_store?.GetOverride(key) is { } own)
            return own;

        Lyrics? plain = null;
        foreach (var source in _sources)
        {
            cancel.ThrowIfCancellationRequested();
            Lyrics? found;
            try
            {
                found = source.Read(item, cancel);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                found = null; // a source that fails is a source with nothing
            }

            if (found is null || !found.HasText)
                continue;
            if (found.Synced)
                return found;
            plain ??= found;
        }

        if (plain is not null)
            return plain;
        return options.Online ? _store?.GetFetched(key) : null;
    }

    /// <summary>The user's own chords, else a chord sheet next to the track.</summary>
    private Lyrics? LoadChords(MediaItem item, LyricsTrackKey key, CancellationToken cancel)
    {
        if (_store?.GetChordsText(key) is { } text && LrcParser.Parse(text) is { } own)
        {
            own.Source = LyricsSources.OwnChords;
            return own;
        }

        try
        {
            return ChordSource?.Read(item, cancel);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>The tab sections of the user's own chords text, else the track's tab file.</summary>
    private string? LoadTab(MediaItem item, LyricsTrackKey key, CancellationToken cancel)
    {
        if (_store?.GetChordsText(key) is { } text && TabNotation.FromChordPro(text) is { } own)
            return own;
        try
        {
            return TabSource?.Invoke(item, cancel);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// A chord sheet and the lyrics together: timed lyrics keep their times and take the sheet's chords on the matching lines; in every
    /// other case (no lyrics, plain lyrics, a sheet whose words differ, a timed sheet of the user's own) the sheet is shown.
    /// </summary>
    internal static Lyrics? WithChordSheet(Lyrics? lyrics, Lyrics? sheet)
    {
        if (sheet is null)
            return lyrics;
        if (lyrics is not { Synced: true } || (sheet.Source == LyricsSources.OwnChords && sheet.Synced))
            return sheet;
        if (ChordNotation.Attach(lyrics, sheet) is not { } merged)
            return sheet.Source == LyricsSources.ChordFile && lyrics.HasChords ? lyrics : sheet;
        merged.ChordSource = sheet.Source;
        return merged;
    }

    private async Task<Lyrics?> LookupOnlineAsync(LyricsTrackKey key, LyricsOptions options, CancellationToken cancel)
    {
        if (_providers.Count == 0 || string.IsNullOrWhiteSpace(key.Title) || string.IsNullOrWhiteSpace(key.Artist))
            return null; // without a title and an artist a search is guesswork: do not send anything
        var now = _utcNow();
        if (_store?.IsNotFound(key, now, NotFoundWindow) == true)
            return null;

        var query = new LyricsQuery(key.Title, key.Artist, string.IsNullOrEmpty(key.Album) ? null : key.Album, key.DurationSeconds);
        var failed = false;
        foreach (var provider in _providers)
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                var found = await provider.FindAsync(query, cancel).ConfigureAwait(false);
                if (found is null || !found.HasText)
                    continue;
                found.Source = LyricsSources.Online;
                if (options.SaveFetched)
                    TrySaveFetched(key, found, provider.Name, now);
                return found;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                failed = true;
            }
        }

        if (!failed)
            _store?.MarkNotFound(key, now);
        return null;
    }

    private void TrySaveFetched(LyricsTrackKey key, Lyrics lyrics, string provider, DateTime now)
    {
        try
        {
            _store?.SaveFetched(key, lyrics, provider, now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The lyrics still show for this session; they just are not kept.
        }
    }

    // --- the user's edits ------------------------------------------------------------------------------------------------------

    /// <summary>Saves the user's own lyrics for the track (they win over every other source) with the lyrics' offset.</summary>
    public void SaveOverride(MediaItem item, Lyrics lyrics)
    {
        if (_store is null)
            return;
        _store.SaveOverride(LyricsTrackKey.From(item), lyrics);
        lyrics.Source = LyricsSources.Override;
        Remember(CacheKey(item), new Cached(lyrics, OnlineTried: false));
    }

    /// <summary>Removes the user's lyrics for the track; the next request falls back to the other sources. True if there were any.</summary>
    public bool DeleteOverride(MediaItem item)
    {
        _cache.TryRemove(CacheKey(item), out _);
        return _store?.DeleteOverride(LyricsTrackKey.From(item)) ?? false;
    }

    /// <summary>The user's own chords text for the track, or null.</summary>
    public string? GetChordsText(MediaItem item) => _store?.GetChordsText(LyricsTrackKey.From(item));

    /// <summary>Saves the user's own chords text for the track (empty text deletes it).</summary>
    public void SaveChordsText(MediaItem item, string text)
    {
        if (_store is null)
            return;
        if (string.IsNullOrWhiteSpace(text))
            _store.DeleteChordsText(LyricsTrackKey.From(item));
        else
            _store.SaveChordsText(LyricsTrackKey.From(item), text);
        Invalidate(item);
    }

    /// <summary>Removes the user's own chords. True if there were any.</summary>
    public bool DeleteChordsText(MediaItem item)
    {
        Invalidate(item);
        return _store?.DeleteChordsText(LyricsTrackKey.From(item)) ?? false;
    }

    /// <summary>Saves the transposition of the track's chords and applies it to the lyrics held in memory.</summary>
    public void SetTranspose(MediaItem item, int semitones)
    {
        _store?.SetTranspose(LyricsTrackKey.From(item), semitones);
        if (_cache.TryGetValue(CacheKey(item), out var hit) && hit.Lyrics is { } lyrics)
            lyrics.Transpose = semitones;
    }

    /// <summary>Saves the timing correction of the track (milliseconds, see <see cref="Lyrics.OffsetMs"/>) and applies it to the lyrics held in memory.</summary>
    public void SetOffset(MediaItem item, int offsetMs)
    {
        offsetMs = Math.Clamp(offsetMs, -600_000, 600_000);
        _store?.SetOffset(LyricsTrackKey.From(item), offsetMs);
        if (_cache.TryGetValue(CacheKey(item), out var hit) && hit.Lyrics is { } lyrics)
            lyrics.OffsetMs = offsetMs;
    }

    /// <summary>Forgets that an online look-up found nothing for the track, so the next request tries again (the user asked to search).</summary>
    public void ForgetMiss(MediaItem item)
    {
        _store?.ClearNotFound(LyricsTrackKey.From(item));
        Invalidate(item);
    }

    /// <summary>Forgets what is remembered in memory for the track (call after a lyrics file was added next to it).</summary>
    public void Invalidate(MediaItem item) => _cache.TryRemove(CacheKey(item), out _);

    /// <summary>Forgets everything remembered in memory.</summary>
    public void InvalidateAll() => _cache.Clear();
}
