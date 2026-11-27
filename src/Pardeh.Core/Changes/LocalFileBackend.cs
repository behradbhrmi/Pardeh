using Pardeh.Core.Library;
using Pardeh.Core.LyricsKit;
using Pardeh.Core.Playback;

namespace Pardeh.Core.Changes;

/// <summary>The system trash of a desktop, supplied by the head.</summary>
public interface ISystemTrash
{
    /// <summary>True when a file at this path would really go to the trash (not be deleted at once).</summary>
    bool Accepts(string path);

    /// <summary>Moves a file to the trash. Throws when it was not moved there.</summary>
    void Trash(string path);
}

/// <summary>
/// Plain paths (Windows, and an app's own folders elsewhere). New contents are built in a temporary file next to the original
/// and swapped in with one rename, so the file is always either the old or the new one.
/// </summary>
public sealed class LocalFileBackend(HoldingFolder holding, ISystemTrash? trash = null) : IFileBackend
{
    private const string WorkExtension = ".pardehwork"; // not a media extension: the library and the folder watcher ignore it
    private const int Attempts = 5;

    public bool Handles(string source) => !source.Contains("://", StringComparison.Ordinal);

    public string? WhyUnchangeable(string source)
    {
        if (!Path.IsPathFullyQualified(source))
            return "This item has no full file path.";
        try
        {
            if (!File.Exists(source))
                return "The file is missing.";
            return (File.GetAttributes(source) & FileAttributes.ReadOnly) != 0 ? "The file is read-only." : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
    }

    public FileStat? Stat(string source)
    {
        var info = new FileInfo(source);
        return info.Exists ? new FileStat(info.Length, info.LastWriteTimeUtc, info.Name) : null;
    }

    public Stream OpenRead(string source) => new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);

    public bool ReplacesAtomically(string source) => true;

    public string WorkPath(string source, string id, string scratchFolder) =>
        Path.Combine(Path.GetDirectoryName(source)!, $".pardeh-{id}{WorkExtension}");

    public void ReplaceContent(string source, string localFile) => Retry(() =>
    {
        try
        {
            // Keeps the original's attributes, permissions and alternate streams.
            File.Replace(localFile, source, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        catch (PlatformNotSupportedException)
        {
            File.Move(localFile, source, overwrite: true);
        }
    });

    // Something that reads the file for a moment (a thumbnail, the loudness measurement, a virus scanner) is waited out.
    private static void Retry(Action action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (attempt < Attempts && ex is IOException or UnauthorizedAccessException && ex is not FileNotFoundException)
            {
                Thread.Sleep(150 * attempt);
            }
        }
    }

    private static string Sibling(string source, string name) => Path.Combine(Path.GetDirectoryName(source)!, name);

    private static bool SameFileName(string a, string b) =>
        string.Equals(a, b, OperatingSystem.IsLinux() || OperatingSystem.IsAndroid() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    public bool NameTaken(string source, string name)
    {
        var target = Sibling(source, name);
        return !SameFileName(Path.GetFileName(source), name) && (File.Exists(target) || Directory.Exists(target));
    }

    public string Rename(string source, string name)
    {
        var target = Sibling(source, name);
        Retry(() =>
        {
            if (NameTaken(source, name))
                throw new IOException($"There is already a file named {name}.");
            File.Move(source, target); // never overwrites
        });
        return target;
    }

    public string? FindRenamed(string source, string name)
    {
        var target = Sibling(source, name);
        if (!File.Exists(target))
            return null;
        if (!SameFileName(Path.GetFileName(source), name))
            return File.Exists(source) ? null : target;
        // Only the case differs: the directory says which one it is.
        return Directory.EnumerateFiles(Path.GetDirectoryName(source)!, name).Any(f => Path.GetFileName(f) == name) ? target : null;
    }

    /// <summary>
    /// "song.lrc", "song.cho", "song.tab", and subtitles such as "movie.en.srt". Shared when another media file has the same name.
    /// </summary>
    public IReadOnlyList<SideFile> SideFiles(string source)
    {
        var folder = Path.GetDirectoryName(source)!;
        var stem = Path.GetFileNameWithoutExtension(source);
        var sideExtensions = new HashSet<string>([.. SidecarLyricsSource.Extensions, .. ChordNotation.Extensions, TabNotation.Extension, .. Subtitles.Extensions], StringComparer.OrdinalIgnoreCase);
        try
        {
            var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true, MatchType = MatchType.Simple };
            var siblings = Directory.EnumerateFiles(folder, "*", options).Where(f => Path.GetFileName(f).StartsWith(stem + ".", StringComparison.OrdinalIgnoreCase)).ToList();
            var shared = siblings.Any(f => !SameFileName(Path.GetFileName(f), Path.GetFileName(source))
                && Path.GetFileNameWithoutExtension(f).Equals(stem, StringComparison.OrdinalIgnoreCase) && IsMedia(f));
            var found = new List<SideFile>();
            foreach (var file in siblings)
            {
                var extension = Path.GetExtension(file);
                if (!sideExtensions.Contains(extension))
                    continue;
                var middle = Path.GetFileNameWithoutExtension(file);
                var exact = middle.Equals(stem, StringComparison.OrdinalIgnoreCase);
                // "name.en.srt": a language between the name and the extension, for subtitles only.
                var language = !exact && Subtitles.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
                    && middle.Length > stem.Length + 1 && middle.Length - stem.Length - 1 <= 12 && !middle[(stem.Length + 1)..].Contains('.')
                    && !siblings.Any(f => IsMedia(f) && Path.GetFileNameWithoutExtension(f).Equals(middle, StringComparison.OrdinalIgnoreCase));
                if (exact || language)
                    found.Add(new SideFile(file, Path.GetFileName(file), shared));
            }

            return found;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    private static bool IsMedia(string path) =>
        MediaLibrary.AudioExtensions.Contains(Path.GetExtension(path)) || MediaLibrary.VideoExtensions.Contains(Path.GetExtension(path));

    public string CopySideFile(string source, string name)
    {
        var target = Sibling(source, name);
        SafeFiles.CopyNew(source, target);
        return target;
    }

    public bool HasSystemTrash(string source) => trash?.Accepts(source) == true;

    public Task<IReadOnlyDictionary<string, TrashedFile?>> TrashAsync(IReadOnlyList<string> sources)
    {
        var result = new Dictionary<string, TrashedFile?>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            try
            {
                if (trash is null || !trash.Accepts(source))
                    throw new IOException("This drive has no Recycle Bin.");
                trash.Trash(source);
                result[source] = File.Exists(source) ? null : new TrashedFile(source, string.Empty);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result[source] = null;
            }
        }

        return Task.FromResult<IReadOnlyDictionary<string, TrashedFile?>>(result);
    }

    // A desktop trash is restored by the user, in the system's own window; undo then picks the file up again.
    public Task<string> UntrashAsync(TrashedFile trashed) =>
        File.Exists(trashed.OriginalSource)
            ? Task.FromResult(trashed.OriginalSource)
            : Task.FromException<string>(new IOException("Restore the file from the Recycle Bin first, then undo again."));

    public HeldFile Hold(string source, string id) => holding.Hold(source, id);

    public HeldFile? FindHeld(string source, string id) => holding.Find(source, id);

    public string RestoreHeld(HeldFile held) => holding.Restore(held);

    public IReadOnlyList<HeldFile> ListHeld() => holding.List();

    public void DeleteHeld(HeldFile held) => holding.Delete(held);

    public void DeletePermanently(string source)
    {
        File.Delete(source);
        if (File.Exists(source))
            throw new IOException("The file could not be deleted.");
    }
}
