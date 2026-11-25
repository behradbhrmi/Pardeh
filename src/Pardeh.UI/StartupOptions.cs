namespace Pardeh.UI;

/// <summary>Command line: any .wmz/.wms/.wsz is the skin to use; everything else is media to play.</summary>
public sealed record StartupOptions(string? SkinPath, IReadOnlyList<string> MediaFiles, bool SkinExplicit = false)
{
    public static StartupOptions Parse(IEnumerable<string> args)
    {
        string? skin = null;
        var media = new List<string>();
        foreach (var arg in args)
        {
            if (IsSkinFile(arg))
                skin = Path.GetFullPath(arg);
            else if (Pardeh.Core.Library.MediaSources.IsContent(arg))
                media.Add(arg); // Android content URI (Open with, Share)
            else if (File.Exists(arg))
                media.Add(Path.GetFullPath(arg));
            else if (Pardeh.Core.Playback.NetworkMedia.IsNetwork(arg))
                media.Add(arg); // streams and internet radio
        }

        return new StartupOptions(skin ?? FindDefaultSkin(), media, skin is not null);
    }

    /// <summary>A file one of the skin loader plug-ins reads (.wmz, .wms, .wsz).</summary>
    public static bool IsSkinFile(string path) => Pardeh.Skinning.Model.SkinLoaders.IsSkinFile(path);

    /// <summary>The user's skin folder, then (for development) the repo's git-ignored private/skins folder.</summary>
    private static string? FindDefaultSkin()
    {
        if (SkinLibrary.LastUsed() is { } last)
            return last;

        var userSkins = Path.Combine(AppPaths.DataDirectory, "Skins");
        if (Directory.Exists(userSkins))
        {
            var found = Directory.EnumerateFiles(userSkins, "*.wmz").Order().FirstOrDefault();
            if (found is not null)
                return found;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var privateSkins = Path.Combine(dir.FullName, "private", "skins");
            if (Directory.Exists(privateSkins))
            {
                var preferred = Path.Combine(privateSkins, "9SeriesDefault.wmz");
                if (File.Exists(preferred))
                    return preferred;
                var any = Directory.EnumerateFiles(privateSkins, "*.wmz").Order().FirstOrDefault();
                if (any is not null)
                    return any;
            }
        }

        return null;
    }
}

/// <summary>The one bookmark / marker store of the app (two instances over one file would overwrite each other).</summary>
internal static class AppBookmarks
{
    private static Pardeh.Core.Library.BookmarkStore? _store;

    public static Pardeh.Core.Library.BookmarkStore Store => _store ??= new(Path.Combine(AppPaths.DataDirectory, "bookmarks.json"));
}

public static class AppPaths
{
    /// <summary>Where settings, library and preferences live. A head may set it once at startup (Android: the app's files dir).</summary>
    /// <remarks>PARDEH_DATA_DIR=&lt;folder&gt; is a development aid: snapshot runs leave the user's own settings and library alone.</remarks>
    public static string DataDirectory { get; set; } =
        Environment.GetEnvironmentVariable("PARDEH_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pardeh");

    public static string LogFile => Path.Combine(DataDirectory, "pardeh.log");
}
