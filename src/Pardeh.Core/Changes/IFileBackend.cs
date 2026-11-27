namespace Pardeh.Core.Changes;

/// <summary>What a platform may have to ask the user for before a batch.</summary>
public enum FileAccessKind
{
    /// <summary>Change contents or the name.</summary>
    Write,

    /// <summary>Move into the holding folder, or delete.</summary>
    Remove,
}

public sealed record FileStat(long Size, DateTime ModifiedUtc, string Name);

/// <summary>A file with the track's name next to it (lyrics, chords, subtitles). <paramref name="Shared"/>: another track uses it too.</summary>
public sealed record SideFile(string Source, string Name, bool Shared);

/// <summary>A file kept in a holding folder until the user restores it or empties the folder.</summary>
public sealed record HeldFile(string Id, string OriginalSource, string HeldSource, string Name, long Size, DateTime HeldUtc, string Sha256);

/// <summary>A file moved to the system's trash. <paramref name="Token"/> is whatever the platform needs to bring it back.</summary>
public sealed record TrashedFile(string OriginalSource, string Token);

/// <summary>
/// How one kind of source is changed. A source is whatever the library calls it (a path, a content URI); nothing outside a
/// backend may assume it is a path. No operation may overwrite another file. Every operation either completes or throws and
/// leaves the original file intact.
/// </summary>
public interface IFileBackend
{
    bool Handles(string source);

    /// <summary>Null when the source can be changed, else the reason to show the user.</summary>
    string? WhyUnchangeable(string source);

    /// <summary>Asks the system once for everything a batch needs. Returns the sources that were refused.</summary>
    Task<IReadOnlyCollection<string>> RequestAccessAsync(IReadOnlyList<string> sources, FileAccessKind kind) =>
        Task.FromResult<IReadOnlyCollection<string>>([]);

    /// <summary>Null when the file does not exist.</summary>
    FileStat? Stat(string source);

    Stream OpenRead(string source);

    // ---- contents ------------------------------------------------------------------------------

    /// <summary>
    /// True when <see cref="ReplaceContent"/> either fully happens or not at all, also when the process dies. When false, the
    /// service keeps a backup of the whole file until the new contents are verified.
    /// </summary>
    bool ReplacesAtomically(string source);

    /// <summary>Where the local working copy of a file is made (next to it where the swap needs that, else in <paramref name="scratchFolder"/>).</summary>
    string WorkPath(string source, string id, string scratchFolder);

    /// <summary>Gives the file the contents of <paramref name="localFile"/>. The local file may be consumed.</summary>
    void ReplaceContent(string source, string localFile);

    // ---- names ---------------------------------------------------------------------------------

    /// <summary>True when another file next to the source already has <paramref name="name"/>.</summary>
    bool NameTaken(string source, string name);

    /// <summary>Renames in place and returns the new source. Throws when the name is taken.</summary>
    string Rename(string source, string name);

    /// <summary>After a crash: the source the file has now if the rename happened, else null.</summary>
    string? FindRenamed(string source, string name);

    /// <summary>The files that belong to a track by their name. Empty where the platform cannot see them.</summary>
    IReadOnlyList<SideFile> SideFiles(string source) => [];

    /// <summary>Copies a side file to <paramref name="name"/> next to it (never overwriting) and returns the copy's source.</summary>
    string CopySideFile(string source, string name) => throw new NotSupportedException("Side files can't be copied here.");

    // ---- removal -------------------------------------------------------------------------------

    /// <summary>True where removal goes to the system's trash; otherwise it goes to a holding folder.</summary>
    bool HasSystemTrash(string source) => false;

    /// <summary>Moves files to the system trash, asking once where the system asks. Per source: the token, or null when it was not moved.</summary>
    Task<IReadOnlyDictionary<string, TrashedFile?>> TrashAsync(IReadOnlyList<string> sources) =>
        throw new NotSupportedException("There is no system trash here.");

    /// <summary>
    /// Brings a trashed file back and returns its source. Also succeeds when the user already restored it by hand. Throws
    /// with a message to show when it can't.
    /// </summary>
    Task<string> UntrashAsync(TrashedFile trashed) => throw new NotSupportedException("There is no system trash here.");

    /// <summary>
    /// Where the system asks before bringing files back: asks once for all of them, ahead of <see cref="UntrashAsync"/>,
    /// which then finds them back. Returns those the user refused. Doing nothing is fine.
    /// </summary>
    Task<IReadOnlyCollection<TrashedFile>> PrepareUntrashAsync(IReadOnlyList<TrashedFile> files) =>
        Task.FromResult<IReadOnlyCollection<TrashedFile>>([]);

    /// <summary>Moves a file into the holding folder.</summary>
    HeldFile Hold(string source, string id);

    /// <summary>After a crash: the held file with this id, when it is complete and the original is gone; else null (the original stays).</summary>
    HeldFile? FindHeld(string source, string id);

    /// <summary>Puts a held file back where it was, under another name if that one is taken. Returns its source.</summary>
    string RestoreHeld(HeldFile held);

    IReadOnlyList<HeldFile> ListHeld();

    /// <summary>Deletes a held file for good (emptying the holding folder).</summary>
    void DeleteHeld(HeldFile held);

    /// <summary>Deletes a file for good. Throws when the file is still there afterwards.</summary>
    void DeletePermanently(string source);
}
