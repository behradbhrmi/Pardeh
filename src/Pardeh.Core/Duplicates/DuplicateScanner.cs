using System.Text.Json;
using System.Text.Json.Serialization;
using Pardeh.Core.Changes;
using Pardeh.Core.Cleanup;
using Pardeh.Core.Library;

namespace Pardeh.Core.Duplicates;

public enum DuplicateScanState
{
    /// <summary>Not run since the app started. What was read before is still there.</summary>
    Idle,
    Running,
    Paused,

    /// <summary>Went through every song.</summary>
    Done,

    /// <summary>Stopped before the end. What was read is kept, and the next run goes on from there.</summary>
    Stopped,
}

/// <param name="Stage">What it is doing, for the user.</param>
public sealed record DuplicateProgress(DuplicateScanState State, string Stage, int Done, int Total)
{
    public bool IsActive => State is DuplicateScanState.Running or DuplicateScanState.Paused;
}

/// <summary>The library as the duplicate check sees it: one copy for each physical file read so far.</summary>
/// <param name="NotRead">Songs not read yet (or changed since): they are in no group.</param>
/// <param name="Unreadable">Songs that could not be read.</param>
/// <param name="Folded">Listings that turned out to be a file already listed.</param>
public sealed record DuplicateReport(IReadOnlyList<SongCopy> Copies, IReadOnlyList<DuplicateGroup> Groups, int NotRead, int Unreadable, int Folded)
{
    public static readonly DuplicateReport Empty = new([], [], 0, 0, 0);

    public SongCopy? CopyOf(string source) =>
        Copies.FirstOrDefault(c => c.Source == source) ?? Copies.FirstOrDefault(c => c.Aliases.Contains(source));
}

/// <summary>
/// Reads the library for the duplicate check, in stages that each narrow down what the next has to read: every song's tags
/// and the start of its sound; all bytes of files of equal size; all sound of files that may be one recording. What was read
/// is kept with the file's size and time of change, so only new and changed files are read again and a stopped run goes on
/// where it was. Runs only when asked, on a background thread of low priority. Nothing is changed.
/// </summary>
public sealed class DuplicateScanner : IDisposable
{
    private const int CacheVersion = 1;
    private const int SaveEvery = 100;
    private static readonly TimeSpan SaveAfter = TimeSpan.FromSeconds(20);

    private sealed record Cached
    {
        public long Size { get; init; }
        public DateTime Modified { get; init; }
        public AudioFacts? Facts { get; init; }
        public string? ContentHash { get; init; }
        public AudioScan? Scan { get; init; }
        public bool ScanFailed { get; init; }
    }

    private sealed record CacheFile(int Version, Dictionary<string, Cached> Tracks);

    private sealed record Listing(LibraryItem Item, IReadOnlyList<string> Aliases, bool KnownFile);

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };

    private readonly MediaLibrary _library;
    private readonly IDuplicateAnalyzer _analyzer;
    private readonly Func<JunkRules> _rules;
    private readonly string? _cachePath;
    private readonly Lock _gate = new();
    private readonly Lock _saving = new();
    private readonly Dictionary<string, Cached> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, long, DateTime), string?> _ids = [];
    private readonly ManualResetEventSlim _go = new(true);
    private CancellationTokenSource? _stop;
    private Task _running = Task.CompletedTask;
    private int _held, _unsaved;
    private bool _paused;
    private DateTime _saved = DateTime.UtcNow, _noticed;

    public DuplicateScanner(MediaLibrary library, IDuplicateAnalyzer analyzer, DuplicateStore store, string? cachePath, Func<JunkRules>? rules = null)
    {
        _library = library;
        _analyzer = analyzer;
        Store = store;
        _cachePath = cachePath;
        _rules = rules ?? (() => JunkRules.None);
        try
        {
            if (cachePath is not null && File.Exists(cachePath)
                && JsonSerializer.Deserialize<CacheFile>(File.ReadAllBytes(cachePath), Json) is { Version: CacheVersion, Tracks: { } tracks })
                foreach (var (source, entry) in tracks)
                    _cache[source] = entry;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Everything is read again.
        }
    }

    public DuplicateStore Store { get; }

    public IDuplicateAnalyzer Analyzer => _analyzer;

    /// <summary>Raised (on any thread) as a run goes on and when it ends.</summary>
    public event EventHandler? Changed;

    public DuplicateProgress Progress { get; private set; } = new(DuplicateScanState.Idle, string.Empty, 0, 0);

    /// <summary>Starts a run, or goes on with a paused one.</summary>
    public Task Start()
    {
        lock (_gate)
        {
            if (_stop is not null && Progress.IsActive)
            {
                _paused = false;
                Gate();
                if (Progress.State == DuplicateScanState.Paused)
                    Progress = Progress with { State = DuplicateScanState.Running };
            }
            else
            {
                var stop = new CancellationTokenSource();
                var before = _running;
                _stop = stop;
                _paused = false;
                Gate();
                Progress = new DuplicateProgress(DuplicateScanState.Running, "Looking at the library", 0, 0);
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _running = done.Task;
                var thread = new Thread(() =>
                {
                    before.Wait();
                    Run(stop.Token);
                    done.SetResult();
                })
                { IsBackground = true, Name = "Duplicate check", Priority = ThreadPriority.BelowNormal };
                thread.Start();
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return _running;
    }

    public void Pause()
    {
        lock (_gate)
        {
            if (Progress.State != DuplicateScanState.Running)
                return;
            _paused = true;
            Gate();
            Progress = Progress with { State = DuplicateScanState.Paused };
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        lock (_gate)
            _stop?.Cancel();
    }

    /// <summary>Holds the run while files are being changed; dispose to go on.</summary>
    public IDisposable Hold()
    {
        lock (_gate)
        {
            _held++;
            Gate();
        }

        return new Release(this);
    }

    private sealed class Release(DuplicateScanner scanner) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0)
                return;
            lock (scanner._gate)
            {
                scanner._held--;
                scanner.Gate();
            }
        }
    }

    private void Gate()
    {
        if (_paused || _held > 0)
            _go.Reset();
        else
            _go.Set();
    }

    /// <summary>Files were changed by the app: what was read from them is forgotten.</summary>
    public void Forget(IEnumerable<string> sources)
    {
        lock (_gate)
        {
            foreach (var source in sources)
                if (_cache.Remove(source))
                    _unsaved++;
        }
    }

    // ---- what is known ------------------------------------------------------------------------------

    private Cached? Known(LibraryItem item)
    {
        lock (_gate)
            return _cache.GetValueOrDefault(item.Path) is { } entry && entry.Size == item.FileSize && entry.Modified == item.Modified ? entry : null;
    }

    private void Keep(LibraryItem item, Cached entry)
    {
        lock (_gate)
        {
            _cache[item.Path] = entry;
            _unsaved++;
        }

        if (_unsaved >= SaveEvery || DateTime.UtcNow - _saved >= SaveAfter)
            Save();
    }

    private string? IdOf(LibraryItem item)
    {
        var key = (item.Path, item.FileSize, item.Modified);
        lock (_ids)
        {
            if (_ids.TryGetValue(key, out var known))
                return known;
        }

        var id = PhysicalFiles.IdOf(item.Path);
        lock (_ids)
            _ids[key] = id;
        return id;
    }

    // How many links the path goes through: the file itself, and the folders on the way (junctions among them).
    private static int LinksIn(string path)
    {
        if (MediaSources.IsContent(path))
            return 0;
        try
        {
            var links = new FileInfo(path).LinkTarget is null ? 0 : 1;
            for (var folder = new FileInfo(path).Directory; folder is not null; folder = folder.Parent)
                if (folder.LinkTarget is not null)
                    links++;
            return links;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    // One listing for each physical file. Two ways to the same file share a file number or a route.
    private List<Listing> Fold(CancellationToken cancel)
    {
        var songs = _library.Music.ToList();
        var owner = new int[songs.Count];
        var byKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var ids = new string?[songs.Count];

        int Root(int i)
        {
            while (owner[i] != i)
                i = owner[i] = owner[owner[i]];
            return i;
        }

        for (var i = 0; i < songs.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            owner[i] = i;
            ids[i] = IdOf(songs[i]);
            var keys = new List<string> { "r" + TrackSort.IdentityOf(songs[i].Path) };
            if (ids[i] is { } id)
                keys.Add("i" + id);
            foreach (var key in keys)
            {
                if (byKey.TryGetValue(key, out var other))
                    owner[Root(i)] = Root(other);
                else
                    byKey[key] = i;
            }
        }

        var listings = new List<Listing>(songs.Count);
        foreach (var same in Enumerable.Range(0, songs.Count).GroupBy(Root))
        {
            var members = same.ToList();
            if (members.Count == 1)
            {
                listings.Add(new Listing(songs[members[0]], [], ids[members[0]] is not null));
                continue;
            }

            // The plain path of the file itself stands for it: not a link, not a content URI, the shortest.
            var first = members.OrderBy(i => MediaSources.IsContent(songs[i].Path)).ThenBy(i => LinksIn(songs[i].Path))
                .ThenBy(i => songs[i].Path.Length).ThenBy(i => songs[i].Path, StringComparer.Ordinal).First();
            var all = members.Select(i => songs[i]).ToList();
            var item = songs[first] with
            {
                Rating = all.Max(a => a.Rating),
                PlayCount = all.Max(a => a.PlayCount),
                LastPlayed = all.Max(a => a.LastPlayed),
            };
            listings.Add(new Listing(item, [.. all.Where(a => a.Path != item.Path).Select(a => a.Path)], members.Any(i => ids[i] is not null)));
        }

        return listings;
    }

    /// <summary>The groups and copies from what has been read so far. No song is read: it is quick, but not for the UI thread.</summary>
    public DuplicateReport Report(CancellationToken cancel = default)
    {
        var rules = _rules();
        var listings = Fold(cancel);
        var copies = new List<SongCopy>(listings.Count);
        var notRead = 0;
        var unreadable = 0;
        foreach (var listing in listings)
        {
            if (Known(listing.Item) is not { } entry)
            {
                notRead++;
                continue;
            }

            if (entry.Facts is not { } facts)
            {
                unreadable++;
                continue;
            }

            copies.Add(new SongCopy(listing.Item, facts, SongName.Read(facts.FileName, facts.Tags, rules))
            {
                Aliases = listing.Aliases,
                ContentHash = entry.ContentHash,
                Scan = entry.Scan,
                Unreadable = entry.ScanFailed,
                KnownFile = listing.KnownFile,
            });
        }

        return new DuplicateReport(copies, DuplicateFinder.Groups(copies, Store), notRead, unreadable, _library.Music.Count() - listings.Count);
    }

    // ---- a run ---------------------------------------------------------------------------------------

    private void Step(string stage, int done, int total, bool now = false)
    {
        var publish = now || DateTime.UtcNow - _noticed >= TimeSpan.FromMilliseconds(500);
        lock (_gate)
            Progress = new DuplicateProgress(_paused ? DuplicateScanState.Paused : DuplicateScanState.Running, stage, done, total);
        if (!publish)
            return;
        _noticed = DateTime.UtcNow;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Wait(CancellationToken cancel)
    {
        _go.Wait(cancel);
        cancel.ThrowIfCancellationRequested();
    }

    private void Run(CancellationToken cancel)
    {
        var end = DuplicateScanState.Stopped;
        try
        {
            Wait(cancel);
            var listings = Fold(cancel);

            // 1: tags and the start of the sound of every song.
            var n = 0;
            foreach (var listing in listings)
            {
                Wait(cancel);
                Step("Reading songs", n++, listings.Count);
                if (Known(listing.Item) is null)
                    Keep(listing.Item, new Cached { Size = listing.Item.FileSize, Modified = listing.Item.Modified, Facts = _analyzer.Read(listing.Item.Path) });
            }

            var readable = listings.Where(l => Known(l.Item)?.Facts is not null).ToList();

            // 2: all bytes, where sizes are equal.
            var sameSize = readable.Where(l => l.Item.FileSize > 0).GroupBy(l => l.Item.FileSize).Where(g => g.Count() > 1).SelectMany(g => g).ToList();
            n = 0;
            foreach (var listing in sameSize)
            {
                Wait(cancel);
                Step("Comparing files of the same size", n++, sameSize.Count);
                if (Known(listing.Item) is { ContentHash: null } entry && _analyzer.ContentHash(listing.Item.Path, cancel) is { } hash)
                    Keep(listing.Item, entry with { ContentHash = hash });
            }

            // 3: all sound, where the start is the same or the names say the songs may be one.
            var candidates = Candidates(readable);
            n = 0;
            var byBytes = new Dictionary<string, (AudioScan? Scan, bool Failed)>(StringComparer.Ordinal);
            foreach (var listing in candidates)
            {
                Wait(cancel);
                Step("Comparing sound", n++, candidates.Count);
                if (Known(listing.Item) is not { } entry)
                    continue;
                if (entry.Scan is not null || entry.ScanFailed)
                {
                    if (entry.ContentHash is { } done)
                        byBytes[done] = (entry.Scan, entry.ScanFailed);
                    continue;
                }

                // The same bytes need no second reading.
                var (scan, failed) = entry.ContentHash is { } bytes && byBytes.TryGetValue(bytes, out var twin)
                    ? twin
                    : _analyzer.Scan(listing.Item.Path, cancel) is { } read ? (read, false) : (null, true);
                if (entry.ContentHash is { } key)
                    byBytes[key] = (scan, failed);
                Keep(listing.Item, entry with { Scan = scan, ScanFailed = failed });
            }

            Prune();
            end = DuplicateScanState.Done;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            Save();
            lock (_gate)
            {
                // Not when another run has been asked for meanwhile: its state is the one to show.
                if (_stop is { } current && current.Token == cancel)
                    Progress = Progress with { State = end };
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private List<Listing> Candidates(List<Listing> readable)
    {
        var rules = _rules();
        var chosen = new HashSet<string>(StringComparer.Ordinal);

        void AddShared(IEnumerable<(string Key, Listing Listing)> keyed)
        {
            foreach (var shared in keyed.Where(k => k.Key.Length > 0).GroupBy(k => k.Key, StringComparer.Ordinal).Where(g => g.Count() > 1))
                foreach (var (_, listing) in shared)
                    chosen.Add(listing.Item.Path);
        }

        AddShared(readable.Select(l => (Known(l.Item)?.Facts?.Start ?? string.Empty, l)));
        AddShared(readable.Select(l => (Known(l.Item)?.ContentHash ?? string.Empty, l)));
        AddShared(readable.Select(l => (Known(l.Item)?.Facts is { } f ? SongName.Read(f.FileName, f.Tags, rules).Bucket : string.Empty, l)));
        return readable.Where(l => chosen.Contains(l.Item.Path)).ToList();
    }

    // What belongs to songs no longer listed goes. An empty library (not scanned yet, or a drive away) drops nothing.
    private void Prune()
    {
        var present = _library.Items.Select(i => i.Path).ToHashSet(StringComparer.Ordinal);
        if (present.Count == 0)
            return;
        lock (_gate)
            foreach (var gone in _cache.Keys.Where(k => !present.Contains(k)).ToList())
            {
                _cache.Remove(gone);
                _unsaved++;
            }
    }

    private void Save()
    {
        lock (_saving)
        {
            _saved = DateTime.UtcNow;
            if (_cachePath is null || _unsaved == 0)
                return;
            try
            {
                byte[] data;
                lock (_gate)
                {
                    data = JsonSerializer.SerializeToUtf8Bytes(new CacheFile(CacheVersion, _cache), Json);
                    _unsaved = 0;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_cachePath))!);
                SafeFiles.WriteDurably(_cachePath, data);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public void Dispose() => Stop();
}
