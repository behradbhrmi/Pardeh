using System.Text.Json;

namespace Pardeh.Core.Library;

/// <summary>Where each video was left (seconds), in one JSON file; the oldest entries go first when there are too many.</summary>
public sealed class VideoPositionStore
{
    private const int MaxEntries = 500;

    private sealed record Entry(string Source, double Seconds);

    private readonly string _path;
    private readonly List<Entry> _entries = []; // oldest first
    private readonly object _lock = new();

    public VideoPositionStore(string path)
    {
        _path = path;
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(path)) is { } loaded)
                _entries.AddRange(loaded.Where(e => e is { Source.Length: > 0 } && double.IsFinite(e.Seconds)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged file: videos start at the beginning.
        }
    }

    private static string KeyOf(string source) => MediaSources.IdentityKey?.Invoke(source) ?? source;

    private int IndexOf(string key) => _entries.FindIndex(e => string.Equals(e.Source, key, StringComparison.OrdinalIgnoreCase));

    public double? Get(string source)
    {
        lock (_lock)
            return IndexOf(KeyOf(source)) is >= 0 and var at ? _entries[at].Seconds : null;
    }

    /// <summary>Remembers a place, or forgets the video's place (null).</summary>
    public void Set(string source, double? seconds)
    {
        var key = KeyOf(source);
        lock (_lock)
        {
            var at = IndexOf(key);
            if (seconds is not { } value)
            {
                if (at < 0)
                    return;
                _entries.RemoveAt(at);
            }
            else
            {
                if (at >= 0 && Math.Abs(_entries[at].Seconds - value) < 1)
                    return;
                if (at >= 0)
                    _entries.RemoveAt(at);
                _entries.Add(new Entry(key, Math.Round(value, 1)));
                while (_entries.Count > MaxEntries)
                    _entries.RemoveAt(0);
            }

            Save();
        }
    }

    /// <summary>A file was renamed: its remembered place follows it.</summary>
    public void Move(string from, string to)
    {
        lock (_lock)
        {
            var at = IndexOf(KeyOf(from));
            var key = KeyOf(to);
            if (at < 0 || _entries[at].Source == key)
                return;
            var entry = _entries[at] with { Source = key };
            _entries.RemoveAt(at);
            if (IndexOf(key) is >= 0 and var there)
                _entries.RemoveAt(there);
            _entries.Add(entry);
            Save();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_entries));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
