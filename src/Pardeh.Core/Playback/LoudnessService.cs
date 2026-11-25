using System.Globalization;
using System.Text.Json;

namespace Pardeh.Core.Playback;

/// <summary>
/// Measures tracks one at a time in the background and remembers the results in one JSON file. A result is tied to the file's size
/// and time of change (a content URI to neither). A file that cannot be measured is not tried again in this session.
/// </summary>
public sealed class LoudnessService : IDisposable
{
    private sealed record Entry(string Stamp, double? Lufs, double Peak); // Lufs null: silence

    private const int MaxEntries = 20_000;

    private readonly string? _path;
    private readonly Func<string, CancellationToken, LoudnessResult?> _measure;
    private readonly Dictionary<string, Entry> _cache;
    private readonly LinkedList<string> _queue = new();
    private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _stop = new();
    private readonly object _lock = new();
    private string? _working;
    private bool _running;

    /// <param name="cachePath">Where results are kept (null: only in memory).</param>
    /// <param name="measure">Decodes and measures a track; null when it cannot be read. Runs on a background thread.</param>
    public LoudnessService(string? cachePath, Func<string, CancellationToken, LoudnessResult?> measure)
    {
        _path = cachePath;
        _measure = measure;
        Dictionary<string, Entry>? loaded = null;
        try
        {
            if (cachePath is not null && File.Exists(cachePath))
                loaded = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(cachePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged cache: tracks are simply measured again.
        }

        _cache = new Dictionary<string, Entry>(loaded ?? [], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Raised (on a background thread) when a track has been measured.</summary>
    public event Action<string>? Measured;

    private static string KeyOf(string source) => Library.MediaSources.IdentityKey?.Invoke(source) ?? source;

    // What tells a changed file apart: its size and time of change.
    private static string StampOf(string source)
    {
        try
        {
            return !source.Contains("://", StringComparison.Ordinal) && new FileInfo(source) is { Exists: true } file
                ? string.Create(CultureInfo.InvariantCulture, $"{file.Length}/{file.LastWriteTimeUtc.Ticks}")
                : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return string.Empty;
        }
    }

    /// <summary>The remembered loudness of a track, or null when it has not been measured (or the file has changed since).</summary>
    public LoudnessResult? Find(string source)
    {
        var stamp = StampOf(source);
        lock (_lock)
            return _cache.TryGetValue(KeyOf(source), out var entry) && entry.Stamp == stamp ? new LoudnessResult(entry.Lufs ?? double.NegativeInfinity, entry.Peak) : null;
    }

    /// <summary>Asks for a track to be measured. <paramref name="first"/>: before the ones already waiting (the playing track).</summary>
    public void Request(string source, bool first = false)
    {
        if (Find(source) is not null)
            return;
        lock (_lock)
        {
            if (_stop.IsCancellationRequested || _failed.Contains(source) || source == _working)
                return;
            _queue.Remove(source);
            if (first)
                _queue.AddFirst(source);
            else
                _queue.AddLast(source);
            while (_queue.Count > 8)
                _queue.RemoveLast(); // only what is playing and about to play matters
            if (_running)
                return;
            _running = true;
        }

        Task.Run(Work);
    }

    /// <summary>True while something is being measured or waiting.</summary>
    public bool Busy
    {
        get
        {
            lock (_lock)
                return _running;
        }
    }

    private void Work()
    {
        while (true)
        {
            string source;
            lock (_lock)
            {
                _working = null;
                if (_queue.First is not { } head || _stop.IsCancellationRequested)
                {
                    _running = false;
                    return;
                }

                source = _working = head.Value;
                _queue.RemoveFirst();
            }

            LoudnessResult? result = null;
            var stamp = StampOf(source);
            try
            {
                result = _measure(source, _stop.Token);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Unreadable or cancelled: treated as not measurable.
            }

            lock (_lock)
            {
                if (result is null)
                {
                    _failed.Add(source);
                    continue;
                }

                if (_cache.Count >= MaxEntries)
                    _cache.Clear();
                _cache[KeyOf(source)] = new Entry(stamp, double.IsFinite(result.Lufs) ? result.Lufs : null, result.Peak);
                Save();
            }

            Measured?.Invoke(source);
        }
    }

    private void Save()
    {
        if (_path is null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path + ".tmp", JsonSerializer.Serialize(_cache));
            File.Move(_path + ".tmp", _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Dispose() => _stop.Cancel();
}
