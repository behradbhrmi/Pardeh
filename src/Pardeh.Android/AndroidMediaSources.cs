using Android.Content;
using Android.Database;
using Android.OS;
using Android.Provider;
using Microsoft.Win32.SafeHandles;
using Pardeh.Core.Library;
using AndroidUri = Android.Net.Uri;

namespace Pardeh.Android;

/// <summary>
/// Content URIs and the media store for the shared player: a picked file or folder is a content URI. FFmpeg reads it through a
/// custom AVIOContext over a seekable stream (ParcelFileDescriptor), TagLib through the same kind of stream. "Device music"
/// lists the MediaStore with real file paths (READ_MEDIA_AUDIO / READ_MEDIA_VIDEO), and a picked folder tree is walked with
/// DocumentsContract.
/// </summary>
internal sealed class AndroidMediaSources : ILibrarySourceProvider
{
    private static Context AppContext => global::Android.App.Application.Context;

    public static void Install()
    {
        MediaSources.Provider = new AndroidMediaSources();
        MediaSources.ContentOpener = OpenStream;
        MediaSources.NameResolver = QueryName;
        MediaSources.PersistAccess = Persist;
        MediaSources.IdentityKey = IdentityKey;
        MediaSources.SiblingResolver = Siblings;
    }

    /// <summary>
    /// The files in the same folder as a document of a picked tree whose name is the same apart from the extension (lyrics .lrc / .txt
    /// next to a song). Only a document reached through a folder tree can be asked; anything else (a single picked file, another
    /// provider's URI) has no siblings. Not verified on a device.
    /// </summary>
    private static IReadOnlyList<string> Siblings(string source, IReadOnlyList<string> extensions)
    {
        try
        {
            var uri = AndroidUri.Parse(source)!;
            if (uri.PathSegments is not { } segments || !segments.Contains("tree"))
                return [];
            var documentId = DocumentsContract.GetDocumentId(uri);
            if (documentId is null)
                return [];
            var slash = documentId.LastIndexOf('/');
            var colon = documentId.IndexOf(':');
            var parentId = slash >= 0 ? documentId[..slash] : colon >= 0 ? documentId[..(colon + 1)] : null;
            if (parentId is null || QueryName(source) is not { Length: > 0 } name)
                return [];
            var stem = System.IO.Path.GetFileNameWithoutExtension(name);

            var children = DocumentsContract.BuildChildDocumentsUriUsingTree(uri, parentId)!;
            var found = new List<(int Rank, string Uri)>();
            using var cursor = AppContext.ContentResolver?.Query(children, [DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName], null, null, null);
            while (cursor is not null && cursor.MoveToNext())
            {
                var childName = cursor.GetString(1) ?? string.Empty;
                if (!System.IO.Path.GetFileNameWithoutExtension(childName).Equals(stem, StringComparison.OrdinalIgnoreCase))
                    continue;
                var rank = -1;
                for (var i = 0; i < extensions.Count; i++)
                    if (extensions[i].Equals(System.IO.Path.GetExtension(childName), StringComparison.OrdinalIgnoreCase))
                        rank = i;
                if (rank >= 0)
                    found.Add((rank, DocumentsContract.BuildDocumentUriUsingTree(uri, cursor.GetString(0)!)!.ToString()!));
            }

            return found.OrderBy(f => f.Rank).Select(f => f.Uri).ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static readonly (string Alias, string Real)[] PathAliases =
    [
        ("/sdcard/", "/storage/emulated/0/"), ("/storage/self/primary/", "/storage/emulated/0/"), ("/mnt/sdcard/", "/storage/emulated/0/"),
        ("/data/media/0/", "/storage/emulated/0/"), ("/data/data/", "/data/user/0/"),
    ];

    /// <summary>
    /// The real file path behind a source: a document URI ("primary:Music/a.mp3") becomes /storage/emulated/0/Music/a.mp3 and
    /// the aliases of the same storage (/sdcard, /storage/self/primary) are folded together. Null: unknown (a picker URI
    /// of another provider), the library falls back to comparing tags.
    /// </summary>
    internal static string? IdentityKey(string source)
    {
        if (!MediaSources.IsContent(source))
        {
            foreach (var (alias, real) in PathAliases)
                if (source.StartsWith(alias, StringComparison.Ordinal))
                    return real + source[alias.Length..];
            return source;
        }

        if (!source.Contains("com.android.externalstorage.documents", StringComparison.Ordinal))
            return null;
        var marker = source.LastIndexOf("/document/", StringComparison.Ordinal);
        if (marker < 0)
            return null;
        var id = Uri.UnescapeDataString(source[(marker + "/document/".Length)..]);
        var colon = id.IndexOf(':');
        if (colon <= 0)
            return null;
        var volume = id[..colon];
        var rest = id[(colon + 1)..];
        return volume == "primary" ? "/storage/emulated/0/" + rest : "/storage/" + volume + "/" + rest;
    }

    private static Stream? OpenStream(string source)
    {
        try
        {
            var pfd = AppContext.ContentResolver?.OpenFileDescriptor(AndroidUri.Parse(source)!, "r");
            return pfd is null ? null : new FileStream(new SafeFileHandle((IntPtr)pfd.DetachFd(), true), FileAccess.Read);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? QueryName(string source)
    {
        try
        {
            using var cursor = AppContext.ContentResolver?.Query(AndroidUri.Parse(source)!, [global::Android.Provider.IOpenableColumns.DisplayName], null, null, null);
            return cursor is not null && cursor.MoveToFirst() ? cursor.GetString(0) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void Persist(string uri) =>
        AppContext.ContentResolver?.TakePersistableUriPermission(AndroidUri.Parse(uri)!, ActivityFlags.GrantReadUriPermission);

    public bool Handles(string folder) => folder.StartsWith("mediastore:", StringComparison.Ordinal) || MediaSources.IsContent(folder);

    public IEnumerable<SourceFile> Enumerate(string folder, CancellationToken cancel)
    {
        if (folder.StartsWith("mediastore:", StringComparison.Ordinal))
            return QueryStore(folder == MediaSources.DeviceVideo ? MediaStore.Video.Media.ExternalContentUri! : MediaStore.Audio.Media.ExternalContentUri!);
        return WalkTree(folder, cancel);
    }

    private static IEnumerable<SourceFile> QueryStore(AndroidUri table)
    {
        var rows = new List<SourceFile>();
        try
        {
#pragma warning disable CA1422 // _data is deprecated but is the only way to give FFmpeg and TagLib a real path
            string[] columns = ["_data", "_size", "date_modified", "_display_name"];
            using var cursor = AppContext.ContentResolver?.Query(table, columns, null, null, null);
            if (cursor is null)
                return rows;
            while (cursor.MoveToNext())
            {
                var path = cursor.GetString(0);
                if (string.IsNullOrEmpty(path))
                    continue;
                rows.Add(new SourceFile(path, cursor.GetLong(1), DateTimeOffset.FromUnixTimeSeconds(cursor.GetLong(2)).UtcDateTime, cursor.GetString(3) ?? System.IO.Path.GetFileName(path)));
            }
#pragma warning restore CA1422
        }
        catch (Exception)
        {
            // No permission (yet): nothing to list.
        }

        return rows;
    }

    private static IEnumerable<SourceFile> WalkTree(string tree, CancellationToken cancel)
    {
        var treeUri = AndroidUri.Parse(tree)!;
        var rootId = DocumentsContract.GetTreeDocumentId(treeUri)!;
        var pending = new Stack<string>();
        pending.Push(rootId);
        string[] columns = [DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName, DocumentsContract.Document.ColumnMimeType,
            DocumentsContract.Document.ColumnSize, DocumentsContract.Document.ColumnLastModified];
        while (pending.Count > 0)
        {
            cancel.ThrowIfCancellationRequested();
            var children = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, pending.Pop())!;
            var found = new List<SourceFile>();
            try
            {
                using var cursor = AppContext.ContentResolver?.Query(children, columns, null, null, null);
                if (cursor is null)
                    continue;
                while (cursor.MoveToNext())
                {
                    var id = cursor.GetString(0)!;
                    var name = cursor.GetString(1) ?? string.Empty;
                    if (cursor.GetString(2) == DocumentsContract.Document.MimeTypeDir)
                    {
                        pending.Push(id);
                        continue;
                    }

                    var uri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, id)!.ToString()!;
                    found.Add(new SourceFile(uri, cursor.GetLong(3), DateTimeOffset.FromUnixTimeMilliseconds(cursor.GetLong(4)).UtcDateTime, name));
                }
            }
            catch (Exception)
            {
                // A folder that cannot be listed is skipped.
            }

            foreach (var file in found)
                yield return file;
        }
    }
}
