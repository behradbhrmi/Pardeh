using System.Text.Json;
using Pardeh.Core.Playback;

namespace Pardeh.Core.Library;

/// <summary>A track or video known to the library.</summary>
public sealed record LibraryItem(
    string Path,
    string Title,
    string Artist,
    string Album,
    string Genre,
    string Year,
    int TrackNumber,
    double DurationSeconds,
    bool IsVideo,
    long FileSize,
    DateTime Modified)
{
    /// <summary>Star rating, 0 (unrated) – 5.</summary>
    public int Rating { get; init; }

    public int PlayCount { get; init; }

    public DateTime LastPlayed { get; init; }

    public MediaItem ToMediaItem()
    {
        var item = new MediaItem(Path) { Name = Title, Duration = TimeSpan.FromSeconds(DurationSeconds) };
        item.Attributes["Title"] = Title;
        if (Artist.Length > 0) item.Attributes["Author"] = Artist;
        if (Album.Length > 0) item.Attributes["Album"] = Album;
        if (Genre.Length > 0) item.Attributes["Genre"] = Genre;
        if (Year.Length > 0) item.Attributes["Year"] = Year;
        return item;
    }
}

/// <summary>
/// The Media Library: scans folders for media, reads tags, and caches the result as JSON so the next start is instant.
/// A SQLite store can replace the JSON cache later without changing callers.
/// </summary>
public sealed class MediaLibrary
{
    public static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wma", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".opus", ".aif", ".aiff", ".ape", ".wv", ".mpc", ".ac3", ".mid", ".midi",
    };

    public static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mkv", ".webm", ".avi", ".wmv", ".asf", ".mov", ".mpg", ".mpeg", ".ts", ".m2ts", ".flv", ".3gp",
    };

    private readonly IMediaInfoReader _reader;
    private readonly string _cachePath;
    private readonly Lock _gate = new();
    private List<LibraryItem> _items = [];
    private CancellationTokenSource? _scan;

    public MediaLibrary(IMediaInfoReader reader, string cachePath)
    {
        _reader = reader;
        _cachePath = cachePath;
    }

    /// <summary>Folders the library watches (default: the user's Music and Videos folders).</summary>
    public List<string> Folders { get; } =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
    ];

    public bool IsScanning { get; private set; }

    /// <summary>Raised (on any thread) when items were added or the scan finished.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<LibraryItem> Items
    {
        get
        {
            lock (_gate)
                return _items;
        }
    }

    public IEnumerable<LibraryItem> Music => Items.Where(i => !i.IsVideo);

    public IEnumerable<LibraryItem> Video => Items.Where(i => i.IsVideo);

    public void LoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath))
                return;
            var cached = JsonSerializer.Deserialize<List<LibraryItem>>(File.ReadAllText(_cachePath));
            if (cached is null)
                return;
            lock (_gate)
                _items = Deduplicate(cached.Where(i => MediaSources.Exists(i.Path)).ToList());
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A broken cache just means a full rescan.
        }
    }

    /// <summary>Scans the folders in the background; unchanged files reuse their cached tags.</summary>
    public Task ScanAsync()
    {
        _scan?.Cancel();
        var cancel = new CancellationTokenSource();
        _scan = cancel;
        return Task.Run(() => Scan(cancel.Token));
    }

    private void Scan(CancellationToken cancel)
    {
        IsScanning = true;
        try
        {
            Dictionary<string, LibraryItem> known;
            lock (_gate)
                known = _items.ToDictionary(i => i.Path, StringComparer.OrdinalIgnoreCase);

            var found = new List<LibraryItem>();
            var identity = MediaSources.IdentityKey;
            var firstSeen = new HashSet<string>(StringComparer.Ordinal);
            var lastNotify = DateTime.UtcNow;
            foreach (var source in EnumerateMedia(cancel))
            {
                var file = source.Path;
                if (identity is not null && MediaSources.IsContent(file) && identity(file) is { } same && !known.ContainsKey(file) && !firstSeen.Add(same))
                    continue; // a document-tree URI of a file already listed by its real path: not read again
                if (identity is not null && !MediaSources.IsContent(file) && identity(file) is { } real)
                    firstSeen.Add(real);
                if (known.TryGetValue(file, out var cached) && cached.FileSize == source.Size && cached.Modified == source.ModifiedUtc)
                {
                    found.Add(cached);
                }
                else
                {
                    // Re-read the tags, but keep what the user gave it (rating, play history).
                    var fresh = Read(source);
                    found.Add(cached is null ? fresh : fresh with { Rating = cached.Rating, PlayCount = cached.PlayCount, LastPlayed = cached.LastPlayed });
                }

                if (DateTime.UtcNow - lastNotify > TimeSpan.FromSeconds(1))
                {
                    lastNotify = DateTime.UtcNow;
                    Publish(Deduplicate(found.ToList()));
                }
            }

            found = Deduplicate(found);
            Publish(found);
            SaveCache(found);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsScanning = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Folds the same file seen through several routes (a real path, the media store, a document-tree URI) into one entry.
    /// Same <see cref="MediaSources.IdentityKey"/> means the same file; otherwise, when one of the two is a content URI, equal
    /// size, duration, title, artist and album do. A real path wins over a content URI; ratings and play history are kept.
    /// Does nothing when no identity hook is installed (Windows).
    /// </summary>
    public static List<LibraryItem> Deduplicate(List<LibraryItem> items)
    {
        var identity = MediaSources.IdentityKey;
        if (identity is null || items.Count < 2)
            return items;

        var result = new List<LibraryItem>(items.Count);
        var byKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var bySignature = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var item in items)
        {
            var key = identity(item.Path) ?? item.Path;
            var signature = item.FileSize > 0 ? $"{item.FileSize}|{Math.Round(item.DurationSeconds)}|{item.Title}|{item.Artist}|{item.Album}" : null;
            var at = -1;
            if (byKey.TryGetValue(key, out var sameKey))
            {
                at = sameKey;
            }
            else if (signature is not null && bySignature.TryGetValue(signature, out var sameTags)
                && (MediaSources.IsContent(item.Path) || MediaSources.IsContent(result[sameTags].Path)))
            {
                at = sameTags;
            }

            if (at < 0)
            {
                byKey[key] = result.Count;
                if (signature is not null)
                    bySignature.TryAdd(signature, result.Count);
                result.Add(item);
                continue;
            }

            changed = true;
            var kept = result[at];
            var winner = MediaSources.IsContent(kept.Path) && !MediaSources.IsContent(item.Path) ? item : kept;
            result[at] = winner with
            {
                Rating = Math.Max(kept.Rating, item.Rating),
                PlayCount = Math.Max(kept.PlayCount, item.PlayCount),
                LastPlayed = kept.LastPlayed > item.LastPlayed ? kept.LastPlayed : item.LastPlayed,
            };
            byKey.TryAdd(key, at);
        }

        return changed ? result : items;
    }

    private void Publish(List<LibraryItem> items)
    {
        lock (_gate)
            _items = items;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private IEnumerable<SourceFile> EnumerateMedia(CancellationToken cancel)
    {
        // Snapshots: the UI thread may add or remove while a scan runs.
        var removed = new HashSet<string>(_removed.ToList(), StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = _files.ToList();
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System };
        foreach (var folder in Folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (MediaSources.Provider is { } provider && provider.Handles(folder))
            {
                // An Android document tree or the device's media store.
                foreach (var found in provider.Enumerate(folder, cancel))
                {
                    cancel.ThrowIfCancellationRequested();
                    var extension = Path.GetExtension(found.DisplayName);
                    if (!removed.Contains(found.Path) && (AudioExtensions.Contains(extension) || VideoExtensions.Contains(extension)) && seen.Add(found.Path))
                        yield return found;
                }

                continue;
            }

            if (!Directory.Exists(folder))
                continue;
            foreach (var file in Directory.EnumerateFiles(folder, "*", options))
            {
                cancel.ThrowIfCancellationRequested();
                var extension = Path.GetExtension(file);
                if (!removed.Contains(file) && (AudioExtensions.Contains(extension) || VideoExtensions.Contains(extension)) && seen.Add(file))
                {
                    var info = new FileInfo(file);
                    yield return new SourceFile(file, info.Length, info.LastWriteTimeUtc, info.Name);
                }
            }
        }

        foreach (var file in files)
        {
            if (!MediaSources.Exists(file) || Folders.Any(f => file.StartsWith(f, StringComparison.OrdinalIgnoreCase)) || !seen.Add(file))
                continue;
            if (MediaSources.IsContent(file))
                yield return new SourceFile(file, 0, DateTime.MinValue, Uri.UnescapeDataString(file[(file.LastIndexOf('/') + 1)..]));
            else
            {
                var info = new FileInfo(file);
                yield return new SourceFile(file, info.Length, info.LastWriteTimeUtc, info.Name);
            }
        }
    }

    private LibraryItem Read(SourceFile file)
    {
        MediaItem media;
        try
        {
            media = _reader.Read(file.Path);
        }
        catch (Exception)
        {
            media = new MediaItem(file.Path);
        }

        // A content URI has no file name in it: the title falls back to the display name.
        var name = MediaSources.IsContent(file.Path) || media.Name == Path.GetFileNameWithoutExtension(file.Path) ? Path.GetFileNameWithoutExtension(file.DisplayName) : media.Name;
        if (!media.Attributes.ContainsKey("Title") && name.Length > 0)
            media.Name = name;
        var extensionOfName = Path.GetExtension(file.DisplayName);
        int.TryParse(media.GetAttribute("TrackNumber"), out var track);
        return new LibraryItem(
            file.Path,
            media.Name,
            media.GetAttribute("Author"),
            media.GetAttribute("Album"),
            media.GetAttribute("Genre"),
            media.GetAttribute("Year"),
            track,
            media.Duration.TotalSeconds,
            VideoExtensions.Contains(extensionOfName) || media.VideoWidth > 0,
            file.Size,
            file.ModifiedUtc);
    }


    // ---- What the user changed: added folders and files, removed items --------------------------

    private sealed record Settings(List<string> Folders, List<string> Files, List<string> Removed);

    private readonly HashSet<string> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _removed = new(StringComparer.OrdinalIgnoreCase);

    private string SettingsPath => Path.Combine(Path.GetDirectoryName(_cachePath)!, "library-settings.json");

    /// <summary>Restores the folders, files and removals saved by <see cref="AddFolder"/>, <see cref="AddFiles"/> and <see cref="Remove"/>.</summary>
    public void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath) || JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath)) is not { } settings)
                return;
            Folders.Clear();
            Folders.AddRange(settings.Folders);
            _files.UnionWith(settings.Files);
            _removed.UnionWith(settings.Removed);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new Settings(Folders.ToList(), _files.ToList(), _removed.ToList())));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Watches another folder (saved) and rescans.</summary>
    public void AddFolder(string folder)
    {
        if (!Folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            Folders.Add(folder);
        WatchFolders();
        _removed.RemoveWhere(p => p.StartsWith(folder, StringComparison.OrdinalIgnoreCase));
        SaveSettings();
        _ = ScanAsync();
    }

    /// <summary>Stops watching a folder (its items leave the library at the next scan).</summary>
    public void RemoveFolder(string folder)
    {
        Folders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
        SaveSettings();
        WatchFolders();
        _ = ScanAsync();
    }

    /// <summary>Adds single files (outside the watched folders too) and rescans.</summary>
    public void AddFiles(IEnumerable<string> files)
    {
        foreach (var file in files)
        {
            _files.Add(file);
            _removed.Remove(file);
        }

        SaveSettings();
        _ = ScanAsync();
    }

    /// <summary>Removes items from the library (the files stay on disk; later scans skip them).</summary>
    public void Remove(IEnumerable<string> paths)
    {
        var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        _removed.UnionWith(set);
        _files.ExceptWith(set);
        List<LibraryItem> items;
        lock (_gate)
            items = _items = _items.Where(i => !set.Contains(i.Path)).ToList();
        SaveSettings();
        SaveCache(items);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---- Ratings, play history, folder watching -------------------------------------------------

    /// <summary>Sets a star rating (0 – 5).</summary>
    public void SetRating(string path, int stars) => Update(path, item => item with { Rating = Math.Clamp(stars, 0, 5) });

    /// <summary>Counts a play (called when an item starts).</summary>
    public void RecordPlay(string path) => Update(path, item => item with { PlayCount = item.PlayCount + 1, LastPlayed = DateTime.UtcNow });

    private void Update(string path, Func<LibraryItem, LibraryItem> change)
    {
        List<LibraryItem> items;
        lock (_gate)
        {
            var index = _items.FindIndex(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return;
            items = _items.ToList();
            items[index] = change(items[index]);
            _items = items;
        }

        SaveCache(items);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private readonly List<FileSystemWatcher> _watchers = [];
    private Timer? _rescanSoon;

    /// <summary>Rescans a few seconds after files change in the watched folders.</summary>
    public void WatchFolders()
    {
        foreach (var watcher in _watchers)
            watcher.Dispose();
        _watchers.Clear();
        foreach (var folder in Folders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite };
                FileSystemEventHandler changed = (_, e) =>
                {
                    var extension = Path.GetExtension(e.FullPath);
                    if (extension.Length == 0 || AudioExtensions.Contains(extension) || VideoExtensions.Contains(extension))
                        ScheduleRescan();
                };
                watcher.Created += changed;
                watcher.Deleted += changed;
                watcher.Changed += changed;
                watcher.Renamed += (_, e) => ScheduleRescan();
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
            }
        }
    }

    private void ScheduleRescan()
    {
        _rescanSoon?.Dispose();
        _rescanSoon = new Timer(_ => ScanAsync(), null, TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan);
    }
    private void SaveCache(List<LibraryItem> items)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
            File.WriteAllText(_cachePath, JsonSerializer.Serialize(items));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
