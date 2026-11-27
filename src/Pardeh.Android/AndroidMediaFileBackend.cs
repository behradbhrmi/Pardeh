using Android.Content;
using Android.OS;
using Android.Provider;
using Pardeh.Core.Changes;
using AndroidUri = Android.Net.Uri;

namespace Pardeh.Android;

/// <summary>
/// Songs and videos in shared storage ("Device music": real paths, listed by the media store). What the system allows decides
/// the shape of everything here:
/// <list type="bullet">
/// <item>Android 11 and later: nothing may be changed until the user agrees in a system dialog, which can cover a whole
/// batch. There is a system trash.</item>
/// <item>No file that is not media may be created next to a song, so a new version can't be swapped in: the file is
/// rewritten in place, and the service keeps a backup until it is verified.</item>
/// <item>A delete can be refused without any error, so every delete is checked.</item>
/// <item>The holding folder is Documents/Pardeh Removed, filled by copying: a file moved there keeps its owner, and the app
/// can then no longer read it.</item>
/// </list>
/// Android 7 to 10 use the storage permission and plain file access.
/// </summary>
internal sealed class AndroidMediaFileBackend : IFileBackend
{
    private static Context AppContext => global::Android.App.Application.Context;

    private readonly HoldingFolder _holding = HoldingFolder.At(
        Path.Combine(global::Android.OS.Environment.ExternalStorageDirectory!.AbsolutePath,
            global::Android.OS.Environment.DirectoryDocuments!, HoldingFolder.FolderName),
        canMove: false);

    // Plain path operations are the same as anywhere else.
    private readonly LocalFileBackend _paths = new(HoldingFolder.At("/unused"));

    public bool Handles(string source) =>
        !source.Contains("://", StringComparison.Ordinal)
        && (source.StartsWith("/storage/", StringComparison.Ordinal) || source.StartsWith("/sdcard/", StringComparison.Ordinal)
            || source.StartsWith("/mnt/", StringComparison.Ordinal))
        && !source.Contains($"/Android/data/{AppContext.PackageName}/", StringComparison.Ordinal);

    public string? WhyUnchangeable(string source)
    {
        if (!File.Exists(source))
            return "The file is missing.";
        return OperatingSystem.IsAndroidVersionAtLeast(30) && Find(source) is null
            ? "Android doesn't list this file as music or video, so apps can't change it."
            : null;
    }

    private static AndroidUri? Find(string path)
    {
        try
        {
            var table = MediaStore.Files.GetContentUri("external")!;
            using var cursor = AppContext.ContentResolver?.Query(table, ["_id", "media_type"], "_data=?", [path], null);
            if (cursor is null || !cursor.MoveToFirst())
                return null;
            var id = cursor.GetLong(0);
            // The write, delete and trash requests only take items of the media tables.
            var collection = cursor.GetInt(1) switch
            {
                2 => MediaStore.Audio.Media.ExternalContentUri,
                3 => MediaStore.Video.Media.ExternalContentUri,
                _ => null,
            };
            return collection is null ? null : ContentUris.WithAppendedId(collection, id);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool CanOpenForWriting(AndroidUri uri)
    {
        try
        {
            using var handle = AppContext.ContentResolver?.OpenFileDescriptor(uri, "rw"); // opened, not touched
            return handle is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<IReadOnlyCollection<string>> RequestAccessAsync(IReadOnlyList<string> sources, FileAccessKind kind)
    {
        if (MainActivity.Current is not { } activity)
            return [.. sources];
        if (!OperatingSystem.IsAndroidVersionAtLeast(30))
            return await activity.RequestAsync("android.permission.WRITE_EXTERNAL_STORAGE") ? [] : [.. sources];

        var needed = new List<(string Source, AndroidUri Uri)>();
        foreach (var source in sources)
            if (Find(source) is { } uri && !CanOpenForWriting(uri))
                needed.Add((source, uri));
        if (needed.Count == 0)
            return [];
        var request = MediaStore.CreateWriteRequest(AppContext.ContentResolver!, needed.Select(n => n.Uri).ToList<AndroidUri>());
        return await activity.AgreeAsync(request) ? [] : needed.Select(n => n.Source).ToList();
    }

    public FileStat? Stat(string source) => _paths.Stat(source);

    public Stream OpenRead(string source) => _paths.OpenRead(source);

    public bool ReplacesAtomically(string source) => false;

    public string WorkPath(string source, string id, string scratchFolder) => Path.Combine(scratchFolder, id + ".pardehwork");

    public void ReplaceContent(string source, string localFile)
    {
        using (var from = File.OpenRead(localFile))
        using (var to = new FileStream(source, FileMode.Open, FileAccess.Write))
        {
            to.SetLength(0);
            from.CopyTo(to);
            to.Flush(flushToDisk: true);
        }

        Rescan(source);
    }

    // The media store notices most changes itself; asking makes its tags and lists follow at once.
    private static void Rescan(params string[] paths) =>
        global::Android.Media.MediaScannerConnection.ScanFile(AppContext, paths, null, (global::Android.Media.MediaScannerConnection.IOnScanCompletedListener?)null);

    public bool NameTaken(string source, string name) => _paths.NameTaken(source, name);

    public string Rename(string source, string name)
    {
        var renamed = _paths.Rename(source, name);
        if (!File.Exists(renamed) || File.Exists(source) && !string.Equals(source, renamed, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Android did not rename the file.");
        Rescan(source, renamed);
        return renamed;
    }

    public string? FindRenamed(string source, string name) => _paths.FindRenamed(source, name);

    // Without access to all files an app can't see the lyrics or subtitle files next to a song here.
    public IReadOnlyList<SideFile> SideFiles(string source) => [];

    public bool HasSystemTrash(string source) => OperatingSystem.IsAndroidVersionAtLeast(30) && Find(source) is not null;

    public async Task<IReadOnlyDictionary<string, TrashedFile?>> TrashAsync(IReadOnlyList<string> sources)
    {
        var result = sources.Distinct().ToDictionary(s => s, _ => (TrashedFile?)null, StringComparer.Ordinal);
        if (!OperatingSystem.IsAndroidVersionAtLeast(30) || MainActivity.Current is not { } activity)
            return result;
        var found = result.Keys.Select(s => (Source: s, Uri: Find(s))).Where(f => f.Uri is not null).ToList();
        if (found.Count == 0)
            return result;
        var request = MediaStore.CreateTrashRequest(AppContext.ContentResolver!, found.Select(f => f.Uri!).ToList<AndroidUri>(), true);
        if (await activity.AgreeAsync(request))
            foreach (var (source, uri) in found)
                if (!File.Exists(source))
                    result[source] = new TrashedFile(source, uri!.ToString()!);
        return result;
    }

    public async Task<IReadOnlyCollection<TrashedFile>> PrepareUntrashAsync(IReadOnlyList<TrashedFile> files)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(30) || MainActivity.Current is not { } activity)
            return [];
        var waiting = files.Where(f => f.Token.Length > 0 && !File.Exists(f.OriginalSource)).ToList();
        if (waiting.Count < 2)
            return [];
        // A file that has left the trash for good makes this throw: the caller then asks about each by itself.
        var request = MediaStore.CreateTrashRequest(AppContext.ContentResolver!, waiting.Select(f => AndroidUri.Parse(f.Token)!).ToList<AndroidUri>(), false);
        return await activity.AgreeAsync(request) ? [] : waiting;
    }

    public async Task<string> UntrashAsync(TrashedFile trashed)
    {
        if (File.Exists(trashed.OriginalSource))
            return trashed.OriginalSource; // restored already, in the Files app
        if (!OperatingSystem.IsAndroidVersionAtLeast(30) || MainActivity.Current is not { } activity || trashed.Token.Length == 0)
            throw new IOException("Restore the file from the trash in your Files app, then undo again.");
        var uri = AndroidUri.Parse(trashed.Token)!;
        bool agreed;
        try
        {
            agreed = await activity.AgreeAsync(MediaStore.CreateTrashRequest(AppContext.ContentResolver!, [uri], false));
        }
        catch (Exception ex)
        {
            throw new IOException("The file is no longer in the trash.", ex);
        }

        if (!agreed)
            throw new IOException("Permission to restore the file was not given.");
        using var cursor = AppContext.ContentResolver?.Query(uri, ["_data"], null, null, null);
        var path = cursor is not null && cursor.MoveToFirst() ? cursor.GetString(0) : null;
        return path is not null && File.Exists(path) ? path : throw new IOException("The file is no longer in the trash.");
    }

    public HeldFile Hold(string source, string id)
    {
        var held = _holding.Hold(source, id);
        Rescan(source);
        return held;
    }

    public HeldFile? FindHeld(string source, string id) => _holding.Find(source, id);

    public string RestoreHeld(HeldFile held)
    {
        var restored = _holding.Restore(held);
        Rescan(restored);
        return restored;
    }

    public IReadOnlyList<HeldFile> ListHeld() => _holding.List();

    public void DeleteHeld(HeldFile held) => _holding.Delete(held);

    public void DeletePermanently(string source)
    {
        _paths.DeletePermanently(source);
        Rescan(source);
    }
}
