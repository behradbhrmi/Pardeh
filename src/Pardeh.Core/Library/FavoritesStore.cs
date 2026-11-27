using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pardeh.Core.Library;

/// <summary>One favourite: the identity it is known by, and what is needed to list it when it is not in the library.</summary>
public sealed class FavoriteEntry
{
    /// <summary>The identity key (<see cref="FavoritesStore.KeyOf"/>).</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>The path or content URI it was marked under (played from here when it is not in the library).</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonPropertyName("added")]
    public DateTime Added { get; set; }
}

/// <summary>
/// The tracks marked with a heart. A favourite is a track's identity (<see cref="MediaSources.IdentityKey"/>: the real file behind a
/// document-tree URI, /sdcard folded into /storage/emulated/0; else the path), so the same file reached as a path and as a tree URI
/// is one favourite, and a track that is not in the library (a file opened by hand) can be a favourite too. Kept as one plain,
/// backup-friendly JSON file (<c>favorites.json</c>); every change is saved at once. Thread-safe; <see cref="Changed"/> fires
/// (outside the lock, on the thread that made the change) after each change.
/// </summary>
public sealed class FavoritesStore
{
    private readonly string? _path;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, FavoriteEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    private sealed class FileModel
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("favorites")]
        public List<FavoriteEntry> Favorites { get; set; } = [];
    }

    /// <summary>A store kept in <paramref name="path"/> (null: memory only, for tests).</summary>
    public FavoritesStore(string? path = null)
    {
        _path = path;
        if (path is null)
            return;
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<FileModel>(File.ReadAllText(path)) is { } loaded)
                foreach (var entry in loaded.Favorites)
                {
                    // Keys are rebuilt from the stored path: a key from an older identity hook (a tree URI) folds into the real file's.
                    var key = entry.Path.Length > 0 ? KeyOf(entry.Path) : entry.Key;
                    if (key.Length > 0)
                    {
                        entry.Key = key;
                        _entries.TryAdd(key, entry);
                    }
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged or unreadable file: start empty (the file is only replaced by the next change).
        }
    }

    /// <summary>Raised after a favourite was added or removed.</summary>
    public event EventHandler? Changed;

    public int Count
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    /// <summary>The identity of a source: the platform's canonical file identity, else the path with one kind of slash.</summary>
    public static string KeyOf(string source) => string.IsNullOrWhiteSpace(source) ? string.Empty : TrackSort.IdentityOf(source);

    public bool IsFavorite(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;
        var key = KeyOf(source);
        lock (_gate)
            return _entries.ContainsKey(key);
    }

    /// <summary>All favourites, oldest first.</summary>
    public IReadOnlyList<FavoriteEntry> Entries
    {
        get
        {
            lock (_gate)
                return _entries.Values.OrderBy(e => e.Added).ToList();
        }
    }

    /// <summary>Marks or unmarks a track. Returns true when something changed.</summary>
    public bool Set(string source, bool favorite, string title = "", string artist = "")
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;
        var key = KeyOf(source);
        lock (_gate)
        {
            if (favorite)
            {
                if (_entries.ContainsKey(key))
                    return false;
                _entries[key] = new FavoriteEntry { Key = key, Path = source, Title = title, Artist = artist, Added = DateTime.UtcNow };
            }
            else if (!_entries.Remove(key))
            {
                return false;
            }

            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Flips a track's state and returns the new one.</summary>
    public bool Toggle(string source, string title = "", string artist = "")
    {
        var now = !IsFavorite(source);
        Set(source, now, title, artist);
        return now;
    }

    /// <summary>The entry a source is a favourite under, or null.</summary>
    public FavoriteEntry? Find(string source)
    {
        lock (_gate)
            return _entries.TryGetValue(KeyOf(source), out var entry) ? entry : null;
    }

    /// <summary>A file was renamed or retagged: the favourite follows it, keeping when it was added.</summary>
    public void Move(string from, string to, string title, string artist)
    {
        var oldKey = KeyOf(from);
        var key = KeyOf(to);
        if (key.Length == 0)
            return;
        lock (_gate)
        {
            if (!_entries.TryGetValue(oldKey, out var old))
                return;
            _entries.Remove(oldKey);
            _entries[key] = new FavoriteEntry { Key = key, Path = to, Title = title, Artist = artist, Added = old.Added };
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Puts back a favourite that left with its file.</summary>
    public void Restore(FavoriteEntry entry, string source)
    {
        var key = KeyOf(source);
        if (key.Length == 0)
            return;
        lock (_gate)
        {
            if (_entries.ContainsKey(key))
                return;
            _entries[key] = new FavoriteEntry { Key = key, Path = source, Title = entry.Title, Artist = entry.Artist, Added = entry.Added };
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (_entries.Count == 0)
                return;
            _entries.Clear();
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Save()
    {
        if (_path is null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            var model = new FileModel { Favorites = [.. _entries.Values.OrderBy(e => e.Added)] };
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort, like the other stores.
        }
    }
}
