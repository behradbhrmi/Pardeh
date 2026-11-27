using System.Text.Json;
using Android.Content;
using Android.Provider;
using Microsoft.Win32.SafeHandles;
using Pardeh.Core.Changes;
using Pardeh.Core.LyricsKit;
using Pardeh.Core.Playback;
using AndroidUri = Android.Net.Uri;

namespace Pardeh.Android;

/// <summary>
/// Files of a folder the user picked (the storage access framework). Everything is a content URI, and a rename or restore
/// gives a new one. Changing needs a write grant on the picked folder. There is no system trash: removed files wait in a
/// ".Pardeh Removed" folder inside the picked folder, which other media apps skip.
/// </summary>
internal sealed class AndroidTreeFileBackend : IFileBackend
{
    private const string HoldingName = ".Pardeh Removed";

    private static Context AppContext => global::Android.App.Application.Context;

    private static ContentResolver Resolver => AppContext.ContentResolver ?? throw new IOException("Android storage is not available.");

    private sealed record Child(string Id, string Name, string Mime, long Size, DateTime ModifiedUtc);

    public bool Handles(string source) => source.StartsWith("content://", StringComparison.OrdinalIgnoreCase);

    private static bool IsTreeDocument(AndroidUri uri) => uri.PathSegments is { Count: >= 4 } parts && parts[0] == "tree" && parts[2] == "document";

    private static AndroidUri Parse(string source)
    {
        var uri = AndroidUri.Parse(source)!;
        return IsTreeDocument(uri) ? uri : throw new IOException("Only files of a folder added to the library can be changed.");
    }

    // ---- the document tree --------------------------------------------------------------------

    private static string IdOf(AndroidUri document) => DocumentsContract.GetDocumentId(document)!;

    private static AndroidUri DocumentIn(AndroidUri tree, string id) => DocumentsContract.BuildDocumentUriUsingTree(tree, id)!;

    // Storage document ids are paths ("primary:Music/a.mp3"). A provider with other ids gets the top of the picked folder.
    private static string ParentId(AndroidUri document)
    {
        var id = IdOf(document);
        var slash = id.LastIndexOf('/');
        var colon = id.IndexOf(':');
        return slash >= 0 ? id[..slash] : colon >= 0 ? id[..(colon + 1)] : DocumentsContract.GetTreeDocumentId(document)!;
    }

    private static List<Child> Children(AndroidUri tree, string parentId)
    {
        var found = new List<Child>();
        var uri = DocumentsContract.BuildChildDocumentsUriUsingTree(tree, parentId)!;
        using var cursor = Resolver.Query(uri,
        [
            DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName, DocumentsContract.Document.ColumnMimeType,
            DocumentsContract.Document.ColumnSize, DocumentsContract.Document.ColumnLastModified,
        ], null, null, null) ?? throw new IOException("The folder can't be listed.");
        while (cursor.MoveToNext())
            found.Add(new Child(cursor.GetString(0)!, cursor.GetString(1) ?? string.Empty, cursor.GetString(2) ?? string.Empty,
                cursor.GetLong(3), DateTimeOffset.FromUnixTimeMilliseconds(cursor.GetLong(4)).UtcDateTime));
        return found;
    }

    private static Child? Look(AndroidUri document)
    {
        try
        {
            using var cursor = Resolver.Query(document,
            [
                DocumentsContract.Document.ColumnDocumentId, DocumentsContract.Document.ColumnDisplayName, DocumentsContract.Document.ColumnMimeType,
                DocumentsContract.Document.ColumnSize, DocumentsContract.Document.ColumnLastModified,
            ], null, null, null);
            return cursor is not null && cursor.MoveToFirst()
                ? new Child(cursor.GetString(0)!, cursor.GetString(1) ?? string.Empty, cursor.GetString(2) ?? string.Empty,
                    cursor.GetLong(3), DateTimeOffset.FromUnixTimeMilliseconds(cursor.GetLong(4)).UtcDateTime)
                : null;
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or Java.IO.FileNotFoundException or ArgumentException)
        {
            return null; // the storage provider throws for a document that is gone
        }
    }

    private static Child? FindIn(AndroidUri tree, string parentId, string name) =>
        Children(tree, parentId).FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static AndroidUri Create(AndroidUri parent, string mime, string name) =>
        DocumentsContract.CreateDocument(Resolver, parent, mime, name) ?? throw new IOException($"{name} could not be created.");

    private static string MimeFor(string name) =>
        global::Android.Webkit.MimeTypeMap.Singleton?.GetMimeTypeFromExtension(Path.GetExtension(name).TrimStart('.').ToLowerInvariant())
        ?? "application/octet-stream";

    private static FileStream Open(AndroidUri document, string mode)
    {
        try
        {
            var handle = Resolver.OpenFileDescriptor(document, mode) ?? throw new IOException("The file can't be opened.");
            return new FileStream(new SafeFileHandle(handle.DetachFd(), true), mode == "r" ? FileAccess.Read : FileAccess.Write);
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or Java.IO.FileNotFoundException)
        {
            throw new IOException("The file can't be opened: " + ex.Message, ex);
        }
    }

    private static string HashOf(AndroidUri document)
    {
        using var stream = Open(document, "r");
        return SafeFiles.Hash(stream);
    }

    private static void Write(AndroidUri document, Stream contents)
    {
        using var target = Open(document, "rwt");
        target.SetLength(0);
        contents.CopyTo(target);
        target.Flush(flushToDisk: true);
    }

    private static void Delete(AndroidUri document)
    {
        try
        {
            DocumentsContract.DeleteDocument(Resolver, document);
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or Java.IO.FileNotFoundException)
        {
            throw new IOException("The file could not be deleted: " + ex.Message, ex);
        }

        if (Look(document) is not null)
            throw new IOException("The file could not be deleted.");
    }

    // ---- IFileBackend ---------------------------------------------------------------------------

    public string? WhyUnchangeable(string source)
    {
        var uri = AndroidUri.Parse(source)!;
        if (!IsTreeDocument(uri))
            return "Only files of a folder added to the library, or of Device music, can be changed.";
        return Look(uri) is null ? "The file is missing." : null;
    }

    private static bool MayWrite(AndroidUri document) => Resolver.PersistedUriPermissions.Any(p =>
        p.IsWritePermission && p.Uri?.Authority == document.Authority && DocumentsContract.GetTreeDocumentId(p.Uri) == DocumentsContract.GetTreeDocumentId(document));

    // A folder added before Pardeh asked for write access is picked once more.
    public async Task<IReadOnlyCollection<string>> RequestAccessAsync(IReadOnlyList<string> sources, FileAccessKind kind)
    {
        var refused = new List<string>();
        foreach (var group in sources.Select(s => (Source: s, Uri: Parse(s))).Where(s => !MayWrite(s.Uri))
                     .GroupBy(s => (s.Uri.Authority, Tree: DocumentsContract.GetTreeDocumentId(s.Uri))))
        {
            var tree = DocumentsContract.BuildTreeDocumentUri(group.Key.Authority, group.Key.Tree)!;
            var granted = MainActivity.Current is { } activity && await activity.PickAgainAsync(tree);
            if (!granted)
                refused.AddRange(group.Select(s => s.Source));
        }

        return refused;
    }

    public FileStat? Stat(string source) => Look(Parse(source)) is { } found ? new FileStat(found.Size, found.ModifiedUtc, found.Name) : null;

    public Stream OpenRead(string source) => Open(Parse(source), "r");

    public bool ReplacesAtomically(string source) => false;

    public string WorkPath(string source, string id, string scratchFolder) => Path.Combine(scratchFolder, id + ".pardehwork");

    public void ReplaceContent(string source, string localFile)
    {
        using var from = File.OpenRead(localFile);
        Write(Parse(source), from);
    }

    public bool NameTaken(string source, string name)
    {
        var uri = Parse(source);
        return FindIn(uri, ParentId(uri), name) is { } other && other.Id != IdOf(uri);
    }

    public string Rename(string source, string name)
    {
        if (NameTaken(source, name))
            throw new IOException($"There is already a file named {name}.");
        try
        {
            var uri = Parse(source);
            // Null means the document kept its URI.
            return (DocumentsContract.RenameDocument(Resolver, uri, name) ?? uri).ToString()!;
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or Java.IO.FileNotFoundException)
        {
            throw new IOException("The file could not be renamed: " + ex.Message, ex);
        }
    }

    public string? FindRenamed(string source, string name)
    {
        var uri = Parse(source);
        if (Look(uri) is { } same)
            return same.Name == name ? source : null;
        var renamed = Children(uri, ParentId(uri)).FirstOrDefault(c => c.Name == name);
        return renamed is null ? null : DocumentIn(uri, renamed.Id).ToString();
    }

    public IReadOnlyList<SideFile> SideFiles(string source)
    {
        try
        {
            var uri = Parse(source);
            if (Look(uri) is not { } track)
                return [];
            var stem = Path.GetFileNameWithoutExtension(track.Name);
            var extensions = new HashSet<string>([.. SidecarLyricsSource.Extensions, .. ChordNotation.Extensions, TabNotation.Extension, .. Subtitles.Extensions], StringComparer.OrdinalIgnoreCase);
            var siblings = Children(uri, ParentId(uri)).Where(c => Path.GetFileNameWithoutExtension(c.Name).Equals(stem, StringComparison.OrdinalIgnoreCase)).ToList();
            var shared = siblings.Any(c => c.Id != track.Id && (c.Mime.StartsWith("audio/", StringComparison.Ordinal) || c.Mime.StartsWith("video/", StringComparison.Ordinal)));
            return siblings.Where(c => extensions.Contains(Path.GetExtension(c.Name)))
                .Select(c => new SideFile(DocumentIn(uri, c.Id).ToString()!, c.Name, shared)).ToList();
        }
        catch (Exception ex) when (ex is IOException or Java.Lang.Exception)
        {
            return [];
        }
    }

    public string CopySideFile(string source, string name)
    {
        var uri = Parse(source);
        var copy = Create(DocumentIn(uri, ParentId(uri)), "application/octet-stream", name);
        using (var from = Open(uri, "r"))
            Write(copy, from);
        return copy.ToString()!;
    }

    // ---- the holding folder -------------------------------------------------------------------------

    private static AndroidUri HoldingFolderOf(AndroidUri document)
    {
        var rootId = DocumentsContract.GetTreeDocumentId(document)!;
        if (FindIn(document, rootId, HoldingName) is { } existing)
            return DocumentIn(document, existing.Id);
        var folder = Create(DocumentIn(document, rootId), DocumentsContract.Document.MimeTypeDir, HoldingName);
        Create(folder, "application/octet-stream", ".nomedia");
        return folder;
    }

    private static string HeldName(string name, string id) => $"{(name.Length > 80 ? name[..80] : name)}.{id}{HoldingFolder.Extension}";

    public HeldFile Hold(string source, string id)
    {
        var uri = Parse(source);
        var track = Look(uri) ?? throw new IOException("The file is missing.");
        var folder = HoldingFolderOf(uri);
        var hash = HashOf(uri);
        var heldName = HeldName(track.Name, id);
        var copy = Create(folder, "application/octet-stream", heldName);
        AndroidUri? manifest = null;
        try
        {
            var held = new HeldFile(id, source, copy.ToString()!, track.Name, track.Size, DateTime.UtcNow, hash);
            manifest = Create(folder, "application/octet-stream", heldName + ".json");
            Write(manifest, new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(held)));
            using (var from = Open(uri, "r"))
                Write(copy, from);
            if (HashOf(copy) != hash)
                throw new IOException("The copy does not match the original.");
            Delete(uri);
            return held;
        }
        catch when (Look(uri) is not null)
        {
            // The original is still there, so what was made in the holding folder goes.
            TryDelete(copy);
            if (manifest is not null)
                TryDelete(manifest);
            throw;
        }
    }

    private static void TryDelete(AndroidUri document)
    {
        try
        {
            Delete(document);
        }
        catch (IOException)
        {
        }
    }

    private static HeldFile? ReadRecord(AndroidUri manifest)
    {
        try
        {
            using var stream = Open(manifest, "r");
            return JsonSerializer.Deserialize<HeldFile>(stream);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    // The record and the held file of an id, whether complete or not.
    private static (HeldFile Record, AndroidUri File, AndroidUri Manifest)? Locate(AndroidUri tree, string id)
    {
        var rootId = DocumentsContract.GetTreeDocumentId(tree)!;
        if (FindIn(tree, rootId, HoldingName) is not { } folder)
            return null;
        var files = Children(tree, folder.Id);
        foreach (var manifest in files.Where(f => f.Name.EndsWith($".{id}{HoldingFolder.Extension}.json", StringComparison.Ordinal)))
        {
            var held = files.FirstOrDefault(f => f.Name == manifest.Name[..^".json".Length]);
            if (held is not null && ReadRecord(DocumentIn(tree, manifest.Id)) is { } record && record.Id == id)
                return (record with { HeldSource = DocumentIn(tree, held.Id).ToString()! }, DocumentIn(tree, held.Id), DocumentIn(tree, manifest.Id));
        }

        return null;
    }

    public HeldFile? FindHeld(string source, string id)
    {
        var uri = Parse(source);
        if (Locate(uri, id) is not { } found)
            return null;
        var complete = HashOf(found.File) == found.Record.Sha256;
        if (Look(uri) is null)
            return complete ? found.Record : null;
        if (HashOf(uri) == found.Record.Sha256)
        {
            TryDelete(found.File);
            TryDelete(found.Manifest);
        }

        return null;
    }

    public string RestoreHeld(HeldFile held)
    {
        var tree = Parse(held.OriginalSource);
        var found = Locate(tree, held.Id) ?? throw new IOException("The held file is no longer in the holding folder.");
        var parentId = ParentId(tree);
        List<Child> there;
        try
        {
            there = Children(tree, parentId);
        }
        catch (Exception ex) when (ex is IOException or Java.Lang.Exception)
        {
            parentId = DocumentsContract.GetTreeDocumentId(tree)!; // its folder is gone: back to the top of the picked one
            there = Children(tree, parentId);
        }

        var name = SafeFiles.FreeName(held.Name, candidate => there.Any(c => c.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)), "restored");
        var target = Create(DocumentIn(tree, parentId), MimeFor(name), name);
        try
        {
            using (var from = Open(found.File, "r"))
                Write(target, from);
            if (HashOf(target) != found.Record.Sha256)
                throw new IOException("The restored copy does not match.");
        }
        catch
        {
            TryDelete(target);
            throw;
        }

        Delete(found.File);
        TryDelete(found.Manifest);
        return target.ToString()!;
    }

    public IReadOnlyList<HeldFile> ListHeld()
    {
        var found = new List<HeldFile>();
        foreach (var grant in Resolver.PersistedUriPermissions)
        {
            try
            {
                if (grant.Uri is not { } tree || FindIn(tree, DocumentsContract.GetTreeDocumentId(tree)!, HoldingName) is not { } folder)
                    continue;
                var files = Children(tree, folder.Id);
                foreach (var manifest in files.Where(f => f.Name.EndsWith(HoldingFolder.Extension + ".json", StringComparison.Ordinal)))
                    if (files.FirstOrDefault(f => f.Name == manifest.Name[..^".json".Length]) is { } held && ReadRecord(DocumentIn(tree, manifest.Id)) is { } record)
                        found.Add(record with { HeldSource = DocumentIn(tree, held.Id).ToString()!, Size = held.Size });
            }
            catch (Exception ex) when (ex is IOException or Java.Lang.Exception)
            {
            }
        }

        return found.OrderBy(h => h.HeldUtc).ToList();
    }

    public void DeleteHeld(HeldFile held)
    {
        var found = Locate(Parse(held.OriginalSource), held.Id) ?? throw new IOException("That is not a held file.");
        Delete(found.File);
        TryDelete(found.Manifest);
    }

    public void DeletePermanently(string source) => Delete(Parse(source));
}
