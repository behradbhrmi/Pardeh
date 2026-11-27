using System.Text;
using System.Text.Json;

namespace Pardeh.Core.Changes;

/// <summary>
/// The holding folder for plain paths: where removed files wait instead of being deleted. A held file gets the extension
/// <c>.pardehheld</c>, so that neither the library nor other media apps list it, and a small <c>.json</c> next to it says where
/// it came from. Both live in the folder itself, so they stay when the app's data is gone. Nothing is ever emptied by itself.
/// </summary>
/// <param name="folderFor">The holding folder for a file (usually one per drive).</param>
/// <param name="folders">Every holding folder there may be, for listing.</param>
/// <param name="canMove">
/// True where a file can simply be moved there. False (Android shared storage) copies, verifies and then deletes: a moved file
/// would keep an owner under which the app can no longer read it.
/// </param>
public sealed class HoldingFolder(Func<string, string> folderFor, Func<IEnumerable<string>> folders, bool canMove = true)
{
    public const string FolderName = "Pardeh Removed";
    public const string Extension = ".pardehheld";
    private const int MaxStem = 80;

    public static HoldingFolder At(string folder, bool canMove = true) => new(_ => folder, () => [folder], canMove);

    /// <summary>
    /// The desktop's holding folders: "Pardeh Removed" in the user's home folder, and on another drive at that drive's top
    /// (so that holding is a move, not a copy), falling back to the home one where it can't be made. They are outside the
    /// app's own folders, so they stay when it is uninstalled.
    /// </summary>
    public static HoldingFolder ForDesktop(string home)
    {
        var homeFolder = Path.Combine(home, FolderName);
        var homeRoot = Path.GetPathRoot(homeFolder);
        string For(string source)
        {
            var root = Path.GetPathRoot(Path.GetFullPath(source));
            if (root is null || string.Equals(root, homeRoot, StringComparison.OrdinalIgnoreCase))
                return homeFolder;
            var there = Path.Combine(root, FolderName);
            try
            {
                Directory.CreateDirectory(there);
                return there;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return homeFolder;
            }
        }

        IEnumerable<string> All() => DriveInfo.GetDrives().Where(d => d.IsReady).Select(d => Path.Combine(d.RootDirectory.FullName, FolderName)).Prepend(homeFolder);
        return new HoldingFolder(For, All);
    }

    private static string ManifestOf(string heldPath) => heldPath + ".json";

    private static string HeldPath(string folder, string name, string id)
    {
        var stem = name.Length > MaxStem ? name[..MaxStem] : name;
        // Don't cut a surrogate pair in half.
        if (stem.Length > 0 && char.IsHighSurrogate(stem[^1]))
            stem = stem[..^1];
        return Path.Combine(folder, $"{stem}.{id}{Extension}");
    }

    public HeldFile Hold(string source, string id)
    {
        var folder = folderFor(source);
        Directory.CreateDirectory(folder);
        var info = new FileInfo(source);
        var name = info.Name;
        var held = new HeldFile(id, source, HeldPath(folder, name, id), name, info.Length, DateTime.UtcNow, SafeFiles.Hash(source));
        if (File.Exists(held.HeldSource))
            throw new IOException("The holding folder already has a file with that id.");

        // The record comes first: whatever happens next, the file can be traced back to where it was.
        SafeFiles.WriteDurably(ManifestOf(held.HeldSource), JsonSerializer.SerializeToUtf8Bytes(held));
        try
        {
            Transfer(source, held.HeldSource, held.Sha256);
        }
        catch
        {
            if (!File.Exists(held.HeldSource))
                SafeFiles.TryDelete(ManifestOf(held.HeldSource));
            throw;
        }

        return held;
    }

    // Moves a file to a name that must be free. The file at "from" stays until the one at "to" is known to be complete.
    private void Transfer(string from, string to, string hash)
    {
        if (canMove && string.Equals(Path.GetPathRoot(Path.GetFullPath(from)), Path.GetPathRoot(Path.GetFullPath(to)), StringComparison.OrdinalIgnoreCase))
        {
            File.Move(from, to); // throws when "to" exists
            return;
        }

        SafeFiles.CopyNew(from, to);
        try
        {
            if (SafeFiles.Hash(to) != hash)
                throw new IOException("The copy does not match the original.");
            File.Delete(from);
            if (File.Exists(from)) // Android can refuse a delete without saying so
                throw new IOException("The original could not be removed (no permission?).");
        }
        catch when (File.Exists(from))
        {
            SafeFiles.TryDelete(to);
            throw;
        }
    }

    public HeldFile? Find(string source, string id)
    {
        var held = HeldPath(folderFor(source), Path.GetFileName(source), id);
        if (Read(ManifestOf(held)) is not { } record)
        {
            return null;
        }

        var complete = File.Exists(held) && SafeFiles.Hash(held) == record.Sha256;
        if (!File.Exists(source))
            return complete ? record : null;

        // The original is still there: whatever is in the holding folder is a leftover of an unfinished copy.
        if (SafeFiles.Hash(source) == record.Sha256)
        {
            SafeFiles.TryDelete(held);
            SafeFiles.TryDelete(ManifestOf(held));
        }

        return null;
    }

    public string Restore(HeldFile held)
    {
        if (!File.Exists(held.HeldSource))
            throw new IOException("The held file is no longer in the holding folder.");
        var folder = Path.GetDirectoryName(held.OriginalSource)!;
        Directory.CreateDirectory(folder);
        var name = SafeFiles.FreeName(held.Name, candidate => File.Exists(Path.Combine(folder, candidate)) || Directory.Exists(Path.Combine(folder, candidate)), "restored");
        var target = Path.Combine(folder, name);
        Transfer(held.HeldSource, target, held.Sha256);
        SafeFiles.TryDelete(ManifestOf(held.HeldSource));
        return target;
    }

    public IReadOnlyList<HeldFile> List()
    {
        var found = new List<HeldFile>();
        foreach (var folder in folders().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(folder))
                    continue;
                foreach (var manifest in Directory.EnumerateFiles(folder, "*" + Extension + ".json"))
                {
                    var held = manifest[..^".json".Length];
                    // The folder may have been moved: the file next to the manifest is the one meant.
                    if (File.Exists(held) && Read(manifest) is { } record)
                        found.Add(record with { HeldSource = held, Size = new FileInfo(held).Length });
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return found.OrderBy(h => h.HeldUtc).ToList();
    }

    public void Delete(HeldFile held)
    {
        // Only ever a file this class made.
        if (!held.HeldSource.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(ManifestOf(held.HeldSource)))
            throw new IOException("That is not a held file.");
        File.Delete(held.HeldSource);
        if (File.Exists(held.HeldSource))
            throw new IOException("The held file could not be deleted.");
        SafeFiles.TryDelete(ManifestOf(held.HeldSource));
    }

    private static HeldFile? Read(string manifest)
    {
        try
        {
            return File.Exists(manifest) ? JsonSerializer.Deserialize<HeldFile>(File.ReadAllText(manifest, Encoding.UTF8)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
