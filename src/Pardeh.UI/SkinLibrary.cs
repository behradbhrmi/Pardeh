using System.Diagnostics;

namespace Pardeh.UI;

/// <summary>Where skins live: the user's skin folder (plus the repo's private/skins during development).</summary>
internal static class SkinLibrary
{
    public static string UserSkinFolder => Path.Combine(AppPaths.DataDirectory, "Skins");

    private static string LastSkinFile => Path.Combine(AppPaths.DataDirectory, "last-skin.txt");

    /// <summary>All skins we know about, by file name.</summary>
    public static IReadOnlyList<string> Discover()
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in Folders())
        {
            // Archive skins: .wmz (Windows Media Player) and .wsz (Winamp classic).
            foreach (var file in Directory.EnumerateFiles(folder, "*.wmz").Concat(Directory.EnumerateFiles(folder, "*.wsz")))
                found.TryAdd(Path.GetFileName(file), file);
        }

        return found.Values.OrderBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The last skin used, if it still exists.</summary>
    public static string? LastUsed()
    {
        try
        {
            var path = File.Exists(LastSkinFile) ? File.ReadAllText(LastSkinFile).Trim() : null;
            return path is not null && File.Exists(path) ? path : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static void RememberCurrent(string path)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(LastSkinFile, path);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Copies a skin into the user's skin folder. Returns the installed path, or null on failure.</summary>
    public static string? Install(string path)
    {
        try
        {
            if (!path.EndsWith(".wmz", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".wsz", StringComparison.OrdinalIgnoreCase))
                return null;
            Directory.CreateDirectory(UserSkinFolder);
            var target = Path.Combine(UserSkinFolder, Path.GetFileName(path));
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                File.Copy(path, target, overwrite: true);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string ModeFile => Path.Combine(AppPaths.DataDirectory, "mode.txt");

    /// <summary>"full" or "skin": the mode Pardeh was last in.</summary>
    public static string LastMode
    {
        get
        {
            try
            {
                return File.Exists(ModeFile) ? File.ReadAllText(ModeFile).Trim() : "full";
            }
            catch (IOException)
            {
                return "full";
            }
        }
        set
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                File.WriteAllText(ModeFile, value);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// Optional folder of replacement full-mode images (same file names as the generated ones), e.g. originals
    /// extracted for personal use: %APPDATA%\Pardeh\FullMode, or private/fullmode in the repo during development.
    /// </summary>
    public static string? FullModeAssetFolder()
    {
        var user = Path.Combine(AppPaths.DataDirectory, "FullMode");
        if (Directory.Exists(user))
            return user;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "private", "fullmode");
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public static void OpenUserFolder()
    {
        Directory.CreateDirectory(UserSkinFolder);
        PlatformServices.Current.OpenFolder(UserSkinFolder);
    }

    private static IEnumerable<string> Folders()
    {
        if (Directory.Exists(UserSkinFolder))
            yield return UserSkinFolder;

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var privateSkins = Path.Combine(dir.FullName, "private", "skins");
            if (Directory.Exists(privateSkins))
            {
                yield return privateSkins;
                yield break;
            }
        }
    }
}
