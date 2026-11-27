using System.Text.Json;
using System.Text.Json.Serialization;
using Pardeh.Core.Changes;
using Pardeh.Core.Cleanup;

namespace Pardeh.Core.OnlineInfo;

/// <summary>What a look-up of one file found. It is only good for the file as it was then.</summary>
public sealed record OnlineEntry
{
    public long Size { get; init; }
    public DateTime Modified { get; init; }
    public DateTime LookedUp { get; init; }

    /// <summary>The <see cref="OnlineStore.MatcherVersion"/> that judged it.</summary>
    public int Matcher { get; init; }

    public OnlineGroup Group { get; init; }

    /// <summary>The song as the search that found the matches read it.</summary>
    public LocalSong Song { get; init; } = new(string.Empty, [], string.Empty, 0);

    public MetadataSearch? Searched { get; init; }

    /// <summary>The best recordings found, best first.</summary>
    public IReadOnlyList<MetadataCandidate> Candidates { get; init; } = [];

    /// <summary>What a batch would change.</summary>
    public IReadOnlyList<CleanupField> Fields { get; init; } = [];

    /// <summary>The match's level in words, or why the song needs a look.</summary>
    public string Why { get; init; } = string.Empty;

    public bool WantsCover { get; init; }

    /// <summary>The user has applied these details to the file.</summary>
    public bool Applied { get; init; }

    /// <summary>The matches, judged again by the matcher of this copy of the app.</summary>
    public IReadOnlyList<MetadataMatch> Matches() =>
        [.. Candidates.Select(c => MetadataMatcher.Match(Song, c)).Where(m => m.Level != MatchLevel.None).OrderByDescending(m => m.Level)];
}

/// <summary>
/// The results of online look-ups, by file, so that closing the app does not mean asking again. One is forgotten when its file
/// changes. A look-up that failed is never saved: it says nothing about the song. One JSON file (null: memory only).
/// </summary>
public sealed class OnlineStore
{
    /// <summary>Raised when matching changes enough that an old "no match" may no longer hold.</summary>
    public const int MatcherVersion = 1;

    private const int FileVersion = 1;
    private const int KeptCandidates = 5, KeptReleases = 6;
    private const int SaveEvery = 10;

    /// <summary>How long a "no match" stands before the song may be asked about again.</summary>
    public static readonly TimeSpan RetryNoMatchAfter = TimeSpan.FromDays(30);

    private sealed record Model(int Version, Dictionary<string, OnlineEntry> Songs);

    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string? _path;
    private readonly Func<DateTime> _now;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, OnlineEntry> _songs = new(StringComparer.Ordinal);
    private int _unsaved;
    private DateTime _saved;

    public OnlineStore(string? path = null, Func<DateTime>? now = null)
    {
        _path = path;
        _now = now ?? (() => DateTime.UtcNow);
        _saved = _now();
        try
        {
            if (path is not null && File.Exists(path) && JsonSerializer.Deserialize<Model>(File.ReadAllBytes(path), Json) is { Version: FileVersion, Songs: { } songs })
                foreach (var (source, entry) in songs)
                    if (entry is { Song: not null, Candidates: not null })
                        _songs[source] = entry;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // Songs are asked about again when the user next asks.
        }
    }

    public DateTime Now => _now();

    /// <summary>Raised (on any thread) when a result is added or forgotten.</summary>
    public event EventHandler? Changed;

    /// <summary>The result for the file as it is now, or null.</summary>
    public OnlineEntry? Get(string source, long size, DateTime modified)
    {
        lock (_gate)
            return _songs.GetValueOrDefault(source) is { } entry && entry.Size == size && entry.Modified == modified ? entry : null;
    }

    /// <summary>Every saved result, by file.</summary>
    public IReadOnlyList<KeyValuePair<string, OnlineEntry>> All()
    {
        lock (_gate)
            return [.. _songs];
    }

    /// <summary>
    /// Whether asking about the file would be of use: nothing is saved for it as it is, or the saved "no match" is old enough
    /// to try again.
    /// </summary>
    public bool IsDue(string source, long size, DateTime modified) =>
        Get(source, size, modified) is not { } entry
        || entry is { Group: OnlineGroup.NoMatch, Applied: false } && (entry.Matcher != MatcherVersion || _now() - entry.LookedUp >= RetryNoMatchAfter);

    public void Put(string source, OnlineEntry entry)
    {
        bool save;
        lock (_gate)
        {
            _songs[source] = Trim(entry);
            save = ++_unsaved >= SaveEvery || _now() - _saved >= TimeSpan.FromSeconds(10);
        }

        if (save)
            Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Keeps only what a review needs: a famous song comes with hundreds of releases.</summary>
    private static OnlineEntry Trim(OnlineEntry entry) => entry with
    {
        Candidates =
        [
            .. entry.Candidates.Take(KeptCandidates).Select(c => c.Releases.Count <= KeptReleases
                ? c
                : c with { Releases = [.. MetadataMatcher.Releases(c, entry.Song.Album).Take(KeptReleases)] }),
        ],
    };

    /// <summary>The files were changed: what was found for them no longer holds.</summary>
    public void Forget(IEnumerable<string> sources)
    {
        var any = false;
        lock (_gate)
        {
            foreach (var source in sources)
                any |= _songs.Remove(source);
            if (any)
                _unsaved++;
        }

        if (!any)
            return;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Drops the results of files that are no longer in the library. An empty list drops nothing.</summary>
    public void Keep(IReadOnlySet<string> present)
    {
        if (present.Count == 0)
            return;
        lock (_gate)
            foreach (var gone in _songs.Keys.Where(k => !present.Contains(k)).ToList())
            {
                _songs.Remove(gone);
                _unsaved++;
            }
    }

    /// <summary>Writes what has not been written yet.</summary>
    public void Flush()
    {
        lock (_gate)
            if (_unsaved == 0)
                return;
        Save();
    }

    // Under the lock to the end: two saves at once would write the same temporary file.
    private void Save()
    {
        lock (_gate)
        {
            _saved = _now();
            if (_path is null)
            {
                _unsaved = 0;
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
                SafeFiles.WriteDurably(_path, JsonSerializer.SerializeToUtf8Bytes(new Model(FileVersion, _songs), Json));
                _unsaved = 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Tried again with the next result.
            }
        }
    }
}
