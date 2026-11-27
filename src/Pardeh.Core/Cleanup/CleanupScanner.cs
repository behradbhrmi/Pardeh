using System.Text.Json;
using System.Text.Json.Serialization;
using Pardeh.Core.Changes;
using Pardeh.Core.Library;

namespace Pardeh.Core.Cleanup;

/// <summary>Reads what the local checks need from a song, from any kind of source.</summary>
public interface ITrackFactsReader
{
    /// <summary>Null when the file can't be read.</summary>
    TrackFacts? Read(string source);
}

public sealed record ScanProgress(int Done, int Total, bool Running);

/// <summary>
/// Runs the local checks over the library in the background. What was read from each file is kept (with its size and time
/// of change), so only new and changed files are read again, and a stopped scan goes on where it was. Nothing is changed
/// and nothing leaves the device.
/// </summary>
public sealed class CleanupScanner : IDisposable
{
    private const int CacheVersion = 1;
    private const int SaveEvery = 200;

    private sealed record Cached(long Size, DateTime Modified, TrackFacts? Facts);

    private sealed record CacheFile(int Version, Dictionary<string, Cached> Tracks);

    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };

    private readonly MediaLibrary _library;
    private readonly ITrackFactsReader _reader;
    private readonly CleanupStore _store;
    private readonly string? _cachePath;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Cached> _cache = new(StringComparer.Ordinal);
    private Dictionary<string, CleanupProposal> _proposals = new(StringComparer.OrdinalIgnoreCase);
    private LibrarySignals _signals = LibrarySignals.None;
    private CancellationTokenSource _stop = new();
    private Task _running = Task.CompletedTask;
    private bool _again, _busy;
    private int _paused;

    public CleanupScanner(MediaLibrary library, ITrackFactsReader reader, CleanupStore store, string? cachePath)
    {
        _library = library;
        _reader = reader;
        _store = store;
        _cachePath = cachePath;
        _store.RulesChanged += (_, _) => _ = ScanAsync();
        try
        {
            if (cachePath is not null && File.Exists(cachePath)
                && JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(cachePath), Json) is { Version: CacheVersion } loaded)
                foreach (var (source, entry) in loaded.Tracks)
                    _cache[source] = entry;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Everything is read again.
        }
    }

    public CleanupStore Store => _store;

    /// <summary>Raised (on any thread) as results arrive and when a scan ends.</summary>
    public event EventHandler? Changed;

    public ScanProgress Progress { get; private set; } = new(0, 0, false);

    /// <summary>True once a scan has gone through the whole library.</summary>
    public bool Complete { get; private set; }

    /// <summary>The result for a song, or null when it has not been looked at.</summary>
    public CleanupProposal? ProposalFor(string source)
    {
        lock (_gate)
            return _proposals.GetValueOrDefault(source);
    }

    /// <summary>Whether the badge shows: something to clean up, not ignored, and badges are on.</summary>
    public bool NeedsCleanup(string source) =>
        _store.ShowBadges && ProposalFor(source) is { Level: not CleanupLevel.Clean } && !_store.IsIgnored(source);

    /// <summary>The songs with something to do, ignored ones left out, in library order.</summary>
    public IReadOnlyList<CleanupProposal> Pending()
    {
        var found = new List<CleanupProposal>();
        foreach (var item in _library.Items)
            if (ProposalFor(item.Path) is { Level: not CleanupLevel.Clean } proposal && !_store.IsIgnored(item.Path))
                found.Add(proposal);
        return found;
    }

    /// <summary>Looks at one song now, with what the last scan learned about the library.</summary>
    public CleanupProposal? Analyze(string source)
    {
        if (_reader.Read(source) is not { } facts)
            return null;
        var proposal = CleanupAnalyzer.Analyze(facts, _signals, _store.Rules);
        lock (_gate)
            _proposals[source] = proposal;
        return proposal;
    }

    /// <summary>Starts a scan; asked for while one runs, another follows it.</summary>
    public Task ScanAsync()
    {
        lock (_gate)
        {
            if (_busy)
            {
                _again = true;
                return _running;
            }

            _busy = true;
            _stop = new CancellationTokenSource();
            var cancel = _stop.Token;
            _running = Task.Run(() => Run(cancel));
            return _running;
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            _again = false;
            _stop.Cancel();
        }
    }

    /// <summary>Holds the scan while files are being changed; dispose to go on.</summary>
    public IDisposable Pause()
    {
        Interlocked.Increment(ref _paused);
        return new Resume(this);
    }

    private sealed class Resume(CleanupScanner scanner) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) == 0)
                Interlocked.Decrement(ref scanner._paused);
        }
    }

    private void Run(CancellationToken cancel)
    {
        while (true)
        {
            try
            {
                Scan(cancel);
            }
            catch (OperationCanceledException)
            {
                SaveCache();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            lock (_gate)
            {
                if (!_again || cancel.IsCancellationRequested)
                {
                    _busy = false;
                    break;
                }

                _again = false;
            }
        }

        Progress = Progress with { Running = false };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Scan(CancellationToken cancel)
    {
        var items = _library.Items.Where(i => !i.IsVideo).ToList();
        var facts = new List<(string Source, TrackFacts Facts)>(items.Count);
        var read = 0;
        var lastNotice = DateTime.UtcNow;
        for (var i = 0; i < items.Count; i++)
        {
            while (Volatile.Read(ref _paused) > 0)
            {
                cancel.ThrowIfCancellationRequested();
                Thread.Sleep(50);
            }

            cancel.ThrowIfCancellationRequested();
            var item = items[i];
            Cached? entry;
            lock (_gate)
                _cache.TryGetValue(item.Path, out entry);
            if (entry is null || entry.Size != item.FileSize || entry.Modified != item.Modified)
            {
                TrackFacts? fresh = null;
                try
                {
                    fresh = _reader.Read(item.Path);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // One unreadable file is skipped.
                }

                entry = new Cached(item.FileSize, item.Modified, fresh);
                lock (_gate)
                    _cache[item.Path] = entry;
                if (++read % SaveEvery == 0)
                    SaveCache();
            }

            if (entry.Facts is { } known)
                facts.Add((item.Path, known));
            if (DateTime.UtcNow - lastNotice > TimeSpan.FromSeconds(1))
            {
                lastNotice = DateTime.UtcNow;
                Progress = new ScanProgress(i + 1, items.Count, true);
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        // What repeats can only be told once everything has been read.
        var rules = _store.Rules;
        var signals = LibrarySignals.Build(facts.Select(f => f.Facts), rules);
        var proposals = new Dictionary<string, CleanupProposal>(StringComparer.OrdinalIgnoreCase);
        foreach (var (source, known) in facts)
        {
            cancel.ThrowIfCancellationRequested();
            proposals[source] = CleanupAnalyzer.Analyze(known, signals, rules);
        }

        lock (_gate)
        {
            _signals = signals;
            _proposals = proposals;
            var present = items.Select(i => i.Path).ToHashSet(StringComparer.Ordinal);
            if (present.Count > 0)
                foreach (var gone in _cache.Keys.Where(k => !present.Contains(k)).ToList())
                    _cache.Remove(gone);
        }

        Complete = true;
        Progress = new ScanProgress(items.Count, items.Count, true);
        if (read > 0)
            SaveCache();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Files were changed by the app: what was known about them is forgotten.</summary>
    public void Forget(IEnumerable<string> sources)
    {
        lock (_gate)
        {
            foreach (var source in sources)
            {
                _cache.Remove(source);
                _proposals.Remove(source);
            }
        }
    }

    private void SaveCache()
    {
        if (_cachePath is null)
            return;
        try
        {
            byte[] data;
            lock (_gate)
                data = JsonSerializer.SerializeToUtf8Bytes(new CacheFile(CacheVersion, _cache), Json);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_cachePath))!);
            SafeFiles.WriteDurably(_cachePath, data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Dispose() => Cancel();
}
