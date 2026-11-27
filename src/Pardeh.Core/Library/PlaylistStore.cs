using System.Text;

namespace Pardeh.Core.Library;

/// <summary>One entry of a saved playlist: its name, the line in the file and what the line said.</summary>
public sealed record PlaylistLine(string Playlist, int Line, string Text);

/// <summary>"My Playlists": playlists saved as .m3u8 files in one folder.</summary>
public sealed class PlaylistStore(string folder)
{
    public string Folder { get; } = folder;

    /// <summary>Names of the saved playlists, alphabetically.</summary>
    public IReadOnlyList<string> Names
    {
        get
        {
            try
            {
                return Directory.Exists(Folder)
                    ? Directory.EnumerateFiles(Folder, "*.m3u8").Select(Path.GetFileNameWithoutExtension).OfType<string>()
                        .Order(StringComparer.CurrentCultureIgnoreCase).ToList()
                    : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }
    }

    /// <summary>The media paths / URLs in a playlist.</summary>
    public IReadOnlyList<string> Read(string name)
    {
        try
        {
            var path = PathFor(name);
            return File.Exists(path)
                ? File.ReadAllLines(path, Encoding.UTF8).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Save(string name, IEnumerable<string> sources)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllLines(PathFor(name), ["#EXTM3U", .. sources], new UTF8Encoding(false));
    }

    /// <summary>Where a source is listed: playlist name and line number (comments and all).</summary>
    public IReadOnlyList<PlaylistLine> FindEntries(string source)
    {
        var found = new List<PlaylistLine>();
        foreach (var name in Names)
        {
            var lines = ReadLines(name);
            for (var i = 0; i < lines.Count; i++)
                if (IsEntry(lines[i], source))
                    found.Add(new PlaylistLine(name, i, lines[i]));
        }

        return found;
    }

    /// <summary>Points the given entries at <paramref name="to"/>. Comments and other lines stay. Safe to run twice.</summary>
    public void Repoint(IEnumerable<PlaylistLine> entries, string from, string to)
    {
        foreach (var playlist in entries.GroupBy(e => e.Playlist))
        {
            var lines = ReadLines(playlist.Key);
            var changed = false;
            foreach (var entry in playlist)
            {
                // The recorded line, or wherever the entry has moved to since.
                var at = entry.Line < lines.Count && IsEntry(lines[entry.Line], from) ? entry.Line : lines.FindIndex(l => IsEntry(l, from));
                if (at < 0 || lines[at] == to)
                    continue;
                lines[at] = to;
                changed = true;
            }

            if (changed)
                WriteLines(playlist.Key, lines);
        }
    }

    /// <summary>Takes the given entries out. Safe to run twice.</summary>
    public void RemoveEntries(IEnumerable<PlaylistLine> entries, string source)
    {
        foreach (var playlist in entries.GroupBy(e => e.Playlist))
        {
            var lines = ReadLines(playlist.Key);
            if (lines.RemoveAll(l => IsEntry(l, source)) > 0)
                WriteLines(playlist.Key, lines);
        }
    }

    /// <summary>Puts removed entries back at their old lines (as <paramref name="source"/>). Safe to run twice.</summary>
    public void InsertEntries(IEnumerable<PlaylistLine> entries, string source)
    {
        foreach (var playlist in entries.GroupBy(e => e.Playlist))
        {
            if (!File.Exists(PathFor(playlist.Key)))
                continue; // the playlist was deleted since
            var lines = ReadLines(playlist.Key);
            var missing = playlist.Count() - lines.Count(l => IsEntry(l, source));
            if (missing <= 0)
                continue;
            foreach (var entry in playlist.OrderBy(e => e.Line).Take(missing))
                lines.Insert(Math.Clamp(entry.Line, 0, lines.Count), source);
            WriteLines(playlist.Key, lines);
        }
    }

    private static bool IsEntry(string line, string source)
    {
        var text = line.Trim();
        return text.Length > 0 && !text.StartsWith('#') && string.Equals(text, source, StringComparison.OrdinalIgnoreCase);
    }

    private List<string> ReadLines(string name)
    {
        var path = PathFor(name);
        return File.Exists(path) ? [.. File.ReadAllLines(path, Encoding.UTF8)] : [];
    }

    private void WriteLines(string name, List<string> lines)
    {
        var path = PathFor(name);
        var temp = path + ".tmp";
        File.WriteAllLines(temp, lines, new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    public void Delete(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path))
            File.Delete(path);
    }

    private string PathFor(string name)
    {
        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        return Path.Combine(Folder, (safe.Length == 0 ? "Playlist" : safe) + ".m3u8");
    }
}
