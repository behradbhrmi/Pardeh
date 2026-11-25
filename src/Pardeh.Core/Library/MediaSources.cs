namespace Pardeh.Core.Library;

/// <summary>One file found by a <see cref="ILibrarySourceProvider"/>.</summary>
public sealed record SourceFile(string Path, long Size, DateTime ModifiedUtc, string DisplayName);

/// <summary>
/// A library folder that is not a directory on disk: an Android document tree ("content://...") or the device's media
/// store ("mediastore:audio"). The head supplies the implementation; the library only asks it to list the files.
/// </summary>
public interface ILibrarySourceProvider
{
    bool Handles(string folder);

    IEnumerable<SourceFile> Enumerate(string folder, CancellationToken cancel);
}

/// <summary>
/// Hooks for sources that are not plain file paths. On Android, a picked file or folder is a content URI: the head turns it
/// into a path that FFmpeg and TagLib can open (a /proc/self/fd/N link of a ParcelFileDescriptor). Elsewhere nothing is set
/// and every source is a path or a URL as before.
/// </summary>
public static class MediaSources
{
    /// <summary>Content URI to an openable path (null: cannot be opened).</summary>
    public static Func<string, string?>? ContentResolver { get; set; }

    public static ILibrarySourceProvider? Provider { get; set; }

    /// <summary>Opens a content URI for reading (seekable). Null: cannot be opened.</summary>
    public static Func<string, Stream?>? ContentOpener { get; set; }

    /// <summary>The file name the system shows for a content URI (with its extension).</summary>
    public static Func<string, string?>? NameResolver { get; set; }

    /// <summary>
    /// A canonical identity for a source (the real file path behind a document-tree URI, /sdcard and /storage/emulated/0 folded
    /// together). Null: the library does no de-duplication (every path is already unique, as on Windows).
    /// </summary>
    public static Func<string, string?>? IdentityKey { get; set; }

    /// <summary>
    /// Finds the files next to a source that have the same name apart from the extension, for the extensions given (in order of
    /// preference; the result keeps that order). Used for lyrics sidecars of a content URI, whose folder is a document tree.
    /// Each result is something <see cref="ContentOpener"/> can open. Null: not supported (such tracks have no sidecar).
    /// </summary>
    public static Func<string, IReadOnlyList<string>, IReadOnlyList<string>>? SiblingResolver { get; set; }

    public static string DisplayName(string source)
    {
        if (!IsContent(source))
            return Path.GetFileName(source);
        if (NameResolver?.Invoke(source) is { Length: > 0 } name)
            return name;
        var last = Uri.UnescapeDataString(source[(source.LastIndexOf('/') + 1)..]);
        return last[(last.LastIndexOfAny(['/', ':']) + 1)..];
    }

    /// <summary>Asks the system to keep access to a picked folder or file across restarts (Android).</summary>
    public static Action<string>? PersistAccess { get; set; }

    public static bool IsContent(string source) => source.StartsWith("content://", StringComparison.OrdinalIgnoreCase);

    public const string DeviceAudio = "mediastore:audio", DeviceVideo = "mediastore:video";

    /// <summary>A short name for a library folder: the folder name, or "Device music" for the media store.</summary>
    public static string FolderLabel(string folder)
    {
        if (folder == DeviceAudio)
            return "Device music";
        if (folder == DeviceVideo)
            return "Device videos";
        if (IsContent(folder))
        {
            var last = Uri.UnescapeDataString(folder[(folder.LastIndexOf('/') + 1)..]);
            return last[(last.LastIndexOfAny(['/', ':']) + 1)..] is { Length: > 0 } leaf ? leaf : last;
        }

        return Path.GetFileName(folder.TrimEnd('/', (char)92)) is { Length: > 0 } name ? name : folder;
    }

    public static bool IsVirtualFolder(string folder) => Provider?.Handles(folder) == true;

    /// <summary>The path to open for a source: a content URI is resolved, everything else is returned unchanged.</summary>
    public static string Resolve(string source) =>
        IsContent(source) && ContentResolver?.Invoke(source) is { Length: > 0 } path ? path : source;

    /// <summary>True when the file exists (a content URI is assumed to exist: checking would open it).</summary>
    public static bool Exists(string source) => IsContent(source) || File.Exists(source);
}
