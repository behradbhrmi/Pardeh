using System.Text;

namespace Pardeh.Core.Library;

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
