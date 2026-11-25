using System.Text.Json;

namespace Pardeh.Core.Library;

/// <summary>A marked time in a track; <see cref="Name"/> is empty for a plain bookmark.</summary>
public sealed record Marker(double Seconds, string Name);

/// <summary>
/// Bookmarked times (seconds), optionally named ("chorus", "solo"), per media path, kept in one JSON file (path → entries sorted by
/// time). An unnamed entry is written as a bare number (the old format, still read), a named one as <c>{"t": 72.0, "n": "chorus"}</c>.
/// </summary>
public sealed class BookmarkStore
{
    /// <summary>Longest marker name kept.</summary>
    public const int MaxNameLength = 60;

    private readonly string _path;
    private readonly Dictionary<string, List<Marker>> _marks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public BookmarkStore(string path)
    {
        _path = path;
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Dictionary<string, List<JsonElement>>>(File.ReadAllText(path)) is { } loaded)
            {
                foreach (var (source, list) in loaded)
                {
                    var marks = list.Select(Read).OfType<Marker>().OrderBy(m => m.Seconds).ToList();
                    if (marks.Count > 0)
                        _marks[source] = marks;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged or unreadable file: start empty.
        }
    }

    private static Marker? Read(JsonElement entry)
    {
        if (entry.ValueKind == JsonValueKind.Number)
            return new Marker(entry.GetDouble(), string.Empty);
        if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("t", out var time) || time.ValueKind != JsonValueKind.Number)
            return null;
        var name = entry.TryGetProperty("n", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? string.Empty : string.Empty;
        return new Marker(time.GetDouble(), Tidy(name));
    }

    private static string Tidy(string? name)
    {
        var text = new string((name ?? string.Empty).Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return text.Length > MaxNameLength ? text[..MaxNameLength] : text;
    }

    /// <summary>The bookmarked times of a media source, ascending.</summary>
    public IReadOnlyList<double> Get(string source) => [.. GetMarkers(source).Select(m => m.Seconds)];

    /// <summary>The markers of a media source, ascending by time.</summary>
    public IReadOnlyList<Marker> GetMarkers(string source)
    {
        lock (_lock)
            return _marks.TryGetValue(source, out var list) ? [.. list] : [];
    }

    /// <summary>Adds a marker and returns its place in <see cref="GetMarkers"/>.</summary>
    public int Add(string source, double seconds, string name = "")
    {
        lock (_lock)
        {
            if (!_marks.TryGetValue(source, out var list))
                _marks[source] = list = [];
            var at = list.FindIndex(m => m.Seconds > seconds);
            if (at < 0)
                at = list.Count;
            list.Insert(at, new Marker(seconds, Tidy(name)));
            Save();
            return at;
        }
    }

    public void Rename(string source, int index, string name)
    {
        lock (_lock)
        {
            if (!_marks.TryGetValue(source, out var list) || index < 0 || index >= list.Count)
                return;
            list[index] = list[index] with { Name = Tidy(name) };
            Save();
        }
    }

    public void RemoveAt(string source, int index)
    {
        lock (_lock)
        {
            if (!_marks.TryGetValue(source, out var list) || index < 0 || index >= list.Count)
                return;
            list.RemoveAt(index);
            if (list.Count == 0)
                _marks.Remove(source);
            Save();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var data = _marks.ToDictionary(pair => pair.Key, pair => pair.Value.Select(m => m.Name.Length == 0 ? (object)m.Seconds : new { t = m.Seconds, n = m.Name }).ToList());
            File.WriteAllText(_path, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
