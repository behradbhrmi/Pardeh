using Pardeh.Core.Duplicates;
using Pardeh.Core.Library;
using Pardeh.Core.LyricsKit;

namespace Pardeh.Core.Changes;

/// <summary>A file as it was when something was decided about it.</summary>
public sealed record FileStamp(long Size, DateTime ModifiedUtc)
{
    /// <summary>Whether the file still looks like that. Some sources keep times only to the second or two.</summary>
    public bool Matches(FileStat now) => now.Size == Size && Math.Abs((now.ModifiedUtc - ModifiedUtc).TotalSeconds) <= 2;
}

/// <summary>The copy that stays when another copy of the same song is removed.</summary>
public sealed record KeptCopy(string Source)
{
    /// <summary>The kept file as it was looked at, or null not to check.</summary>
    public FileStamp? Kept { get; init; }

    /// <summary>The file to remove as it was looked at, or null not to check.</summary>
    public FileStamp? Removed { get; init; }

    /// <summary>Other ways the library lists the file that is removed. Those that are gone with it are dealt with too.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];
}

/// <summary>One change to one file.</summary>
public sealed record ChangeRequest(string Source, ChangeKind Kind)
{
    public TagFields? Tags { get; init; }
    public string? NewName { get; init; }

    /// <summary>
    /// Set on the removal of an extra copy of a song: the copy that stays. Before removing, the service makes sure that the
    /// kept copy is there and is another file; afterwards what the app knew about the removed one goes to it.
    /// </summary>
    public KeptCopy? Keep { get; init; }

    public static ChangeRequest EditTags(string source, TagFields tags) => new(source, ChangeKind.Tags) { Tags = tags };

    /// <summary>A new file name, with the same extension. If another file has it, a number is added.</summary>
    public static ChangeRequest Rename(string source, string newName) => new(source, ChangeKind.Rename) { NewName = newName };
}

public enum ResultStatus
{
    Done,

    /// <summary>The file already was that way.</summary>
    Unchanged,

    Failed,

    /// <summary>Left for later: the file is playing.</summary>
    Deferred,

    /// <summary>The system or the user refused access.</summary>
    Denied,

    /// <summary>The user stopped the batch before this file.</summary>
    Cancelled,
}

/// <param name="Source">The file as it was asked for.</param>
/// <param name="NewSource">Where it is now (null: removed, or nothing happened).</param>
/// <param name="Message">Why it failed, or something the user should know.</param>
public sealed record ChangeResult(string Source, ChangeKind Kind, ResultStatus Status, string? NewSource = null, string? Message = null)
{
    /// <summary>The checks on the copy to keep held this removal back: nothing may be forced.</summary>
    public bool Refused { get; init; }
}

/// <param name="BatchId">Null when there was nothing to write down.</param>
public sealed record BatchResult(string? BatchId, IReadOnlyList<ChangeResult> Results, bool CanUndo)
{
    public int Count(ResultStatus status) => Results.Count(r => r.Status == status);
}

public sealed record ChangeOptions
{
    /// <summary>Asked once when files of a batch are playing. Null, or false: those files are left for later.</summary>
    public Func<IReadOnlyList<string>, Task<bool>>? MayInterruptPlayback { get; init; }

    /// <summary>Told how many files of the batch are finished.</summary>
    public IProgress<(int Done, int Total)>? Progress { get; init; }

    /// <summary>Stops between two files: those done stay done (and can be undone), the rest are left alone.</summary>
    public CancellationToken Cancel { get; init; }
}

/// <summary>
/// The one way the app changes the user's files: tags, names and removals. For every file it writes down what was there,
/// makes the change so that a failure leaves the original, reads it back, moves everything the app remembers about the track
/// to its new name, and can undo the whole batch. A failure on one file does not stop the others.
/// <para>
/// Call it on the player's (UI) thread: file work runs in the background, and references are moved back on the caller's thread.
/// </para>
/// </summary>
public sealed class FileChangeService(
    IReadOnlyList<IFileBackend> backends, ITagEditor editor, LibraryReferences references, ChangeJournal journal, MediaLibrary library)
{
    private readonly SemaphoreSlim _one = new(1, 1);

    public IPlaybackGuard? Playback { get; set; }

    public ChangeJournal Journal => journal;

    public IReadOnlyList<IFileBackend> Backends => backends;

    /// <summary>Test hook: called with a step name just before and after the dangerous moments.</summary>
    internal Action<string>? Checkpoint { get; set; }

    /// <summary>Raised after a batch, an undo or a recovery, with the batch's results (none for a recovery).</summary>
    public event EventHandler<IReadOnlyList<ChangeResult>>? Changed;

    /// <summary>
    /// Files that an interrupted change left half written and that <see cref="Recover"/> could not repair, usually because
    /// the system wants to ask the user first. Their originals are kept until <see cref="RepairAsync"/> puts them back.
    /// </summary>
    public IReadOnlyList<string> PendingRepairs => journal.Ids
        .Where(journal.NeedsRecovery)
        .Select(journal.Load)
        .OfType<JournalBatch>()
        .SelectMany(b => b.Items)
        .Where(i => i.State == ItemState.Started && i.BackupPath is { } backup && File.Exists(backup))
        .Select(i => i.Source)
        .Distinct()
        .ToList();

    /// <summary>Asks for access to the files waiting for repair and runs the recovery again. Returns what still needs attention.</summary>
    public async Task<IReadOnlyList<string>> RepairAsync()
    {
        await _one.WaitAsync();
        try
        {
            using var noScans = await library.SuspendScansAsync();
            foreach (var group in PendingRepairs.GroupBy(Required).ToList())
                await group.Key.RequestAccessAsync(group.ToList(), FileAccessKind.Write);
            return Recover();
        }
        finally
        {
            _one.Release();
        }
    }

    private IFileBackend? BackendFor(string source) => backends.FirstOrDefault(b => b.Handles(source));

    private IFileBackend Required(string source) => BackendFor(source) ?? throw new NotSupportedException("This kind of source can't be changed.");

    /// <summary>The removal to ask for: the system trash where there is one, else the holding folder.</summary>
    public ChangeRequest Remove(string source) =>
        new(source, BackendFor(source)?.HasSystemTrash(source) == true ? ChangeKind.Trash : ChangeKind.Hold);

    public JournalBatch? LastUndoable() => journal.LastUndoable();

    /// <summary>The files waiting in the holding folders.</summary>
    public IReadOnlyList<HeldFile> ListHeld() => [.. backends.SelectMany(b => b.ListHeld()).DistinctBy(h => h.HeldSource).OrderBy(h => h.HeldUtc)];

    /// <summary>
    /// Brings one held file back. When the journal still has its removal, that one change is undone, so playlists and history
    /// come back with it; otherwise the file alone is restored and the library finds it at its next scan.
    /// </summary>
    public async Task<ChangeResult> RestoreHeldAsync(HeldFile held, ChangeOptions? options = null)
    {
        foreach (var id in journal.Ids)
        {
            if (journal.Load(id) is not { CanUndo: true } batch)
                continue;
            if (batch.Items.FirstOrDefault(i => i is { State: ItemState.Done, Undone: false, Kind: ChangeKind.Hold } && i.Held?.Id == held.Id) is { } item)
                return (await UndoAsync(id, options, new HashSet<int> { item.Index })).Results[0];
        }

        await _one.WaitAsync();
        try
        {
            using var noScans = await library.SuspendScansAsync();
            var backend = backends.First(b => b.ListHeld().Any(h => h.HeldSource == held.HeldSource));
            var restored = await Task.Run(() => backend.RestoreHeld(held));
            _ = library.ScanAsync();
            var result = new ChangeResult(held.OriginalSource, ChangeKind.RestoreHeld, ResultStatus.Done, restored);
            Changed?.Invoke(this, [result]);
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            return new ChangeResult(held.OriginalSource, ChangeKind.RestoreHeld, ResultStatus.Failed, null, ex.Message);
        }
        finally
        {
            _one.Release();
        }
    }

    /// <summary>Deletes held files for good. Returns why for those that could not be deleted.</summary>
    public async Task<IReadOnlyList<string>> DeleteHeldAsync(IReadOnlyList<HeldFile> files)
    {
        await _one.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                var problems = new List<string>();
                foreach (var held in files)
                {
                    try
                    {
                        var backend = backends.FirstOrDefault(b => b.ListHeld().Any(h => h.HeldSource == held.HeldSource))
                            ?? throw new IOException("It is no longer in the holding folder.");
                        backend.DeleteHeld(held);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                    {
                        problems.Add($"{held.Name}: {ex.Message}");
                    }
                }

                return problems;
            });
        }
        finally
        {
            _one.Release();
        }
    }

    /// <summary>Null when the file can be changed, else the reason to show.</summary>
    public string? WhyUnchangeable(string source) =>
        BackendFor(source) is { } backend ? backend.WhyUnchangeable(source) : "This kind of source can't be changed.";

    /// <summary>The file as it is now, or null when it is missing or of a kind that can't be changed.</summary>
    public FileStat? Stat(string source)
    {
        try
        {
            return BackendFor(source)?.Stat(source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The tags a file has now, for an editor to start from. Reads a copy, so any source works.</summary>
    public Task<TagFields> ReadTagsAsync(string source)
    {
        var backend = Required(source);
        return Task.Run(() =>
        {
            var name = backend.Stat(source)?.Name ?? throw new IOException("The file is missing.");
            Directory.CreateDirectory(journal.Folder);
            var copy = Path.Combine(journal.Folder, $"read-{Guid.NewGuid():N}.pardehwork");
            try
            {
                using (var original = backend.OpenRead(source))
                    SafeFiles.CopyNew(original, copy);
                return editor.Read(copy, name);
            }
            finally
            {
                SafeFiles.TryDelete(copy);
            }
        });
    }

    public Task<BatchResult> ApplyAsync(string description, IReadOnlyList<ChangeRequest> requests, ChangeOptions? options = null)
    {
        var items = requests.Select(r => new JournalItem
        {
            Kind = r.Kind,
            Source = r.Source,
            Change = r.Tags,
            NewName = r.NewName,
            Keep = r.Keep,
        }).ToList();
        return RunAsync(description, items, null, options ?? new ChangeOptions());
    }

    /// <param name="only">Undo just these items of the batch (by <see cref="JournalItem.Index"/>); null: all of it.</param>
    public Task<BatchResult> UndoAsync(string batchId, ChangeOptions? options = null, IReadOnlySet<int>? only = null)
    {
        if (journal.Load(batchId) is not { CanUndo: true } batch)
            return Task.FromResult(new BatchResult(null, [], false));

        var undo = new List<JournalItem>();
        foreach (var done in batch.Items.Where(i => i.State == ItemState.Done && !i.Undone && only?.Contains(i.Index) != false).Reverse())
        {
            var item = done.Kind switch
            {
                ChangeKind.Tags => new JournalItem
                {
                    Kind = ChangeKind.Tags,
                    Source = done.NewSource!,
                    Change = done.OldTags,
                    RestoreTagKinds = done.TagKindsBefore,
                    KeepLyrics = done.Plan?.TargetHad ?? LyricsPieces.None,
                },
                ChangeKind.Rename => new JournalItem
                {
                    Kind = ChangeKind.Rename,
                    Source = done.NewSource!,
                    NewName = done.OriginalName,
                    KeepLyrics = done.Plan?.TargetHad ?? LyricsPieces.None,
                    SideFiles = [.. done.SideFiles.Where(s => s.To is not null).Select(s => s.Copy
                        ? new SideFileMove(s.To!, s.ToName, string.Empty, Copy: true) { Original = s.From }
                        : new SideFileMove(s.To!, s.ToName, s.FromName, Copy: false))],
                },
                ChangeKind.Hold => new JournalItem { Kind = ChangeKind.RestoreHeld, Source = done.Source, Held = done.Held, Before = done.Before, Plan = done.Plan },
                ChangeKind.Trash => new JournalItem { Kind = ChangeKind.Untrash, Source = done.Source, Trashed = done.Trashed, Before = done.Before, Plan = done.Plan },
                _ => null, // a permanent delete can't be undone
            };
            if (item is null)
                continue;
            item.Undoes = done.Index;
            item.ExpectedSource = done.Source;
            undo.Add(item);
        }

        return RunAsync("Undo: " + batch.Description, undo, batch, options ?? new ChangeOptions());
    }

    private async Task<BatchResult> RunAsync(string description, List<JournalItem> items, JournalBatch? undone, ChangeOptions options)
    {
        if (items.Count == 0)
            return new BatchResult(null, [], false);
        await _one.WaitAsync();
        try
        {
            using var noScans = await library.SuspendScansAsync();
            var asked = items.Select(i => i.Source).ToList(); // later items may be repointed as files are renamed
            var broken = PendingRepairs; // before this batch is written down
            var batch = journal.Create(description, items, undone?.Id);
            var refused = new Dictionary<int, ResultStatus>();

            // Changing a file that still waits for its original would let the repair overwrite the new change later.
            foreach (var item in batch.Items.Where(i => broken.Contains(i.Source)))
                Skip(batch, item, refused, ResultStatus.Failed, "An earlier change to this file was interrupted. Repair it first.");
            Preflight(batch, refused);
            await RequestAccessAsync(batch, refused);
            await AskAboutPlaybackAsync(batch, refused, options);
            // Extra copies go last: the copy that stays is given its details first, and only if that works are they removed.
            var trashLast = batch.Items.Any(i => i.Keep is not null);
            if (!trashLast)
                await TrashAsync(batch, undone);

            var waiting = batch.Items.Where(i => i.State == ItemState.Pending && i.Kind != ChangeKind.Trash).ToList();
            for (var n = 0; n < waiting.Count; n++)
            {
                var item = waiting[n];
                options.Progress?.Report((n, waiting.Count));
                if (options.Cancel.IsCancellationRequested)
                {
                    Skip(batch, item, refused, ResultStatus.Cancelled, "Stopped before this file.");
                    continue;
                }

                await ExecuteOneAsync(batch, item);
                Finish(batch, item, undone);
            }

            options.Progress?.Report((waiting.Count, waiting.Count));

            if (trashLast)
            {
                if (options.Cancel.IsCancellationRequested)
                    foreach (var item in batch.Items.Where(i => i.State == ItemState.Pending))
                        Skip(batch, item, refused, ResultStatus.Cancelled, "Stopped before this file.");
                await TrashAsync(batch, undone);
            }

            batch.Finished = true;
            journal.Save(batch);
            journal.Prune();

            var results = batch.Items.Select((item, n) => new ChangeResult(
                asked[n],
                item.Kind,
                refused.TryGetValue(item.Index, out var why) ? why : item.State switch
                {
                    ItemState.Done => ResultStatus.Done,
                    ItemState.Unchanged => ResultStatus.Unchanged,
                    _ => ResultStatus.Failed, // "Changed" too: the file changed but the library could not follow (retried at the next start)
                },
                item.State is ItemState.Done or ItemState.Changed ? item.NewSource : null,
                item.Message) { Refused = item.Refused }).ToList();
            Changed?.Invoke(this, results);
            return new BatchResult(batch.Id, results, batch.CanUndo);
        }
        finally
        {
            _one.Release();
        }
    }

    private void Skip(JournalBatch batch, JournalItem item, Dictionary<int, ResultStatus> refused, ResultStatus status, string message)
    {
        item.State = status == ResultStatus.Failed ? ItemState.Failed : ItemState.Skipped;
        item.Message = message;
        refused[item.Index] = status;
        journal.Save(batch, item);
    }

    private static bool ChangesAFile(ChangeKind kind) => kind is not (ChangeKind.RestoreHeld or ChangeKind.Untrash);

    private void Preflight(JournalBatch batch, Dictionary<int, ResultStatus> refused)
    {
        foreach (var item in batch.Items.Where(i => i.State == ItemState.Pending))
        {
            if (BackendFor(item.Source) is not { } backend)
                Skip(batch, item, refused, ResultStatus.Failed, "This kind of source can't be changed.");
            else if (ChangesAFile(item.Kind) && !ComesLater(batch, item) && backend.WhyUnchangeable(item.Source) is { } why)
                Skip(batch, item, refused, ResultStatus.Failed, why);
        }

        // At least one copy stays: no batch may remove what another of its removals counts on keeping.
        var removals = batch.Items.Where(i => i.State == ItemState.Pending && Removes(i.Kind)).ToList();
        var circular = removals.Where(i => i.Keep is { } keep && removals.Any(r => IsOneFile(r.Source, keep.Source))).ToList();
        foreach (var item in circular)
        {
            item.Refused = true;
            Skip(batch, item, refused, ResultStatus.Failed, "The copy to keep is being removed too.");
        }
    }

    private static bool Removes(ChangeKind kind) => kind is ChangeKind.Hold or ChangeKind.Trash or ChangeKind.Delete;

    private static bool IsOneFile(string first, string second) =>
        PhysicalFiles.SameRoute(first, second) || PhysicalFiles.IdOf(first) is { } id && id == PhysicalFiles.IdOf(second);

    // Asked right before an extra copy is removed. Null when it may go.
    private string? WhyNotRemovable(JournalBatch batch, JournalItem item)
    {
        var why = CheckKeptCopy(batch, item);
        item.Refused = why is not null;
        return why;
    }

    private string? CheckKeptCopy(JournalBatch batch, JournalItem item)
    {
        if (item.Keep is not { } keep)
            return null;
        var earlier = batch.Items.Take(item.Index).Where(i => !Removes(i.Kind) && (i.Source == keep.Source || i.ExpectedSource == keep.Source)).ToList();
        if (earlier.Any(i => i.State is not (ItemState.Done or ItemState.Unchanged)))
            return "The copy to keep could not be updated, so nothing was removed.";
        if (BackendFor(keep.Source) is not { } keeper || keeper.Stat(keep.Source) is not { Size: > 0 } kept)
            return "The copy to keep is missing.";
        if (keep.Kept is { } was && !earlier.Any(i => i.State == ItemState.Done) && !was.Matches(kept))
            return "The copy to keep has changed since the check. Check again.";
        if (Required(item.Source).Stat(item.Source) is not { } removed)
            return "The file is missing.";
        if (keep.Removed is { } seen && !seen.Matches(removed))
            return "This file has changed since the check. Check again.";
        try
        {
            using var readable = keeper.OpenRead(keep.Source);
            if (readable.ReadByte() < 0)
                return "The copy to keep is empty.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return "The copy to keep can't be read. " + ex.Message;
        }

        return PhysicalFiles.WhyNotDistinct(item.Source, keep.Source);
    }

    private bool KeptCopyIsThere(JournalItem item) => item.Keep is not { } keep || Required(keep.Source).Stat(keep.Source) is not null;

    // An earlier item of the batch gives this one its file (an undo renaming back, then restoring tags).
    private static bool ComesLater(JournalBatch batch, JournalItem item) =>
        batch.Items.Take(item.Index).Any(i => i.ExpectedSource == item.Source);

    private async Task RequestAccessAsync(JournalBatch batch, Dictionary<int, ResultStatus> refused)
    {
        // One question per kind and platform. A trash request is its own question.
        var needed = batch.Items
            .Where(i => i.State == ItemState.Pending && i.Kind is ChangeKind.Tags or ChangeKind.Rename or ChangeKind.Hold or ChangeKind.Delete)
            .GroupBy(i => (Backend: Required(i.Source), Access: i.Kind is ChangeKind.Tags or ChangeKind.Rename ? FileAccessKind.Write : FileAccessKind.Remove));
        foreach (var group in needed.ToList())
        {
            var sources = group.Select(i => i.Source).Distinct().ToList();
            IReadOnlyCollection<string> denied;
            try
            {
                denied = await group.Key.Backend.RequestAccessAsync(sources, group.Key.Access);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                denied = sources;
            }

            foreach (var item in group.Where(i => denied.Contains(i.Source)))
                Skip(batch, item, refused, ResultStatus.Denied, "Permission to change the file was not given.");
        }

        // Several files to bring back from a system trash are asked about together.
        var trashed = batch.Items.Where(i => i is { State: ItemState.Pending, Kind: ChangeKind.Untrash, Trashed: not null }).GroupBy(i => Required(i.Source));
        foreach (var group in trashed.Where(g => g.Count() > 1).ToList())
        {
            IReadOnlyCollection<TrashedFile> no;
            try
            {
                no = await group.Key.PrepareUntrashAsync([.. group.Select(i => i.Trashed!)]);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                continue; // each is then asked about by itself
            }

            foreach (var item in group.Where(i => no.Contains(i.Trashed!)))
                Skip(batch, item, refused, ResultStatus.Denied, "Permission to restore the file was not given.");
        }
    }

    private async Task AskAboutPlaybackAsync(JournalBatch batch, Dictionary<int, ResultStatus> refused, ChangeOptions options)
    {
        if (Playback is not { } guard)
            return;
        var playing = batch.Items.Where(i => i.State == ItemState.Pending && ChangesAFile(i.Kind) && guard.IsInUse(i.Source)).ToList();
        if (playing.Count == 0)
            return;
        if (options.MayInterruptPlayback is { } ask && await ask(playing.Select(i => i.Source).Distinct().ToList()))
        {
            foreach (var item in playing)
                item.MayInterrupt = true;
            return;
        }

        foreach (var item in playing)
            Skip(batch, item, refused, ResultStatus.Deferred, "The file is playing.");
    }

    // A file that began to play while the batch ran is left alone unless the user agreed to interrupting it.
    private bool TryRelease(JournalItem item, out IPlaybackHold? hold)
    {
        hold = null;
        if (!ChangesAFile(item.Kind) || Playback is not { } guard)
            return true;
        if (guard.IsInUse(item.Source) && !item.MayInterrupt)
            return false;
        hold = guard.Release(item.Source); // also keeps it from being queued up while it changes
        return true;
    }

    private async Task TrashAsync(JournalBatch batch, JournalBatch? undone)
    {
        foreach (var group in batch.Items.Where(i => i.State == ItemState.Pending && i.Kind == ChangeKind.Trash).GroupBy(i => Required(i.Source)).ToList())
        {
            var holds = new List<(JournalItem Item, IPlaybackHold? Hold)>();
            try
            {
                var ready = new List<JournalItem>();
                foreach (var item in group)
                {
                    if (!TryRelease(item, out var hold))
                    {
                        Fail(batch, item, "The file started playing.");
                        continue;
                    }

                    holds.Add((item, hold));
                    try
                    {
                        if (WhyNotRemovable(batch, item) is { } why)
                            throw new IOException(why);
                        item.Before = references.Capture(item.Source);
                        // An extra copy's plan is made once it has gone: what it gives the kept copy adds to what others gave.
                        item.Plan = item.Keep is null ? references.PlanRemove(item.Source) : null;
                        item.State = ItemState.Started;
                        journal.Save(batch, item);
                        ready.Add(item);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        Fail(batch, item, ex.Message);
                    }
                }

                if (ready.Count == 0)
                    continue;
                IReadOnlyDictionary<string, TrashedFile?> moved;
                try
                {
                    moved = await group.Key.TrashAsync(ready.Select(i => i.Source).ToList());
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    foreach (var item in ready)
                        Fail(batch, item, ex.Message);
                    continue;
                }

                Checkpoint?.Invoke("trashed");
                foreach (var item in ready)
                {
                    if (moved.GetValueOrDefault(item.Source) is not { } trashed)
                    {
                        Fail(batch, item, "It was not moved to the trash. The file is where it was.");
                        continue;
                    }

                    item.Trashed = trashed;
                    item.State = ItemState.Changed;
                    journal.Save(batch, item);
                    if (!KeptCopyIsThere(item))
                        AddMessage(item, "The copy to keep is gone as well: look for the file in the trash.");
                    TryRelink(batch, item);
                    Finish(batch, item, undone);
                }
            }
            finally
            {
                foreach (var (item, hold) in holds)
                    hold?.Resume(After(item));
            }
        }
    }

    private async Task ExecuteOneAsync(JournalBatch batch, JournalItem item)
    {
        if (!TryRelease(item, out var hold))
        {
            Fail(batch, item, "The file started playing.");
            return;
        }

        try
        {
            if (item.Kind == ChangeKind.Untrash)
                await UntrashAsync(batch, item);
            else
                await Task.Run(() => Execute(batch, item));
            if (item.State == ItemState.Changed)
                TryRelink(batch, item);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Fail(batch, item, ex.Message);
        }
        finally
        {
            hold?.Resume(After(item));
        }
    }

    // What playback goes on with once a released file has been dealt with: for a removed extra copy, the one that stayed.
    private static string? After(JournalItem item) =>
        item.State is not (ItemState.Changed or ItemState.Done) ? item.Source
        : Removes(item.Kind) ? item.Keep?.Source
        : item.NewSource;

    private void Fail(JournalBatch batch, JournalItem item, string message)
    {
        // A change that has happened stays recorded as such, so that it can be followed up and undone.
        if (item.State != ItemState.Changed)
            item.State = ItemState.Failed;
        item.Message = message;
        journal.Save(batch, item);
    }

    private void Finish(JournalBatch batch, JournalItem item, JournalBatch? undone)
    {
        if (item.State is not (ItemState.Done or ItemState.Unchanged))
            return;

        // Later items of this batch about the same file follow it to its new name.
        if (item.NewSource is { } now && now != item.Source)
        {
            foreach (var later in batch.Items.Skip(item.Index + 1).Where(i => i.Source == item.Source || i.Source == item.ExpectedSource))
            {
                later.Source = now;
                later.MayInterrupt |= item.MayInterrupt; // the agreement to stop playback goes with the file
                journal.Save(batch, later);
            }
        }

        if (undone is not null && item.Undoes is { } index)
        {
            undone.Items[index].Undone = true;
            journal.Save(undone, undone.Items[index]);
        }
    }

    private string HashOf(IFileBackend backend, string source)
    {
        using var stream = backend.OpenRead(source);
        return SafeFiles.Hash(stream);
    }

    private void Started(JournalBatch batch, JournalItem item)
    {
        item.State = ItemState.Started;
        journal.Save(batch, item);
        Checkpoint?.Invoke("started");
    }

    private void MarkChanged(JournalBatch batch, JournalItem item, string? newSource)
    {
        Checkpoint?.Invoke("replaced");
        item.NewSource = newSource;
        item.State = ItemState.Changed;
        journal.Save(batch, item);
    }

    private static string IdOf(JournalBatch batch, JournalItem item) => $"{batch.Id}-{item.Index}";

    private void Execute(JournalBatch batch, JournalItem item)
    {
        var backend = Required(item.Source);
        switch (item.Kind)
        {
            case ChangeKind.Tags:
                ExecuteTags(batch, item, backend);
                break;
            case ChangeKind.Rename:
                ExecuteRename(batch, item, backend);
                break;
            case ChangeKind.Hold:
                if (WhyNotRemovable(batch, item) is { } stays)
                    throw new IOException(stays);
                item.Before = references.Capture(item.Source);
                item.Plan = item.Keep is null ? references.PlanRemove(item.Source) : null;
                Started(batch, item);
                item.Held = backend.Hold(item.Source, IdOf(batch, item));
                if (!KeptCopyIsThere(item))
                {
                    backend.RestoreHeld(item.Held);
                    throw new IOException("The copy to keep went with it, so they were one file. It was put back.");
                }

                MarkChanged(batch, item, null);
                break;
            case ChangeKind.Delete:
                if (backend.Stat(item.Source) is null)
                    throw new IOException("The file is missing.");
                if (WhyNotRemovable(batch, item) is { } remains)
                    throw new IOException(remains);
                item.Before = references.Capture(item.Source);
                item.Plan = item.Keep is null ? references.PlanRemove(item.Source) : null;
                Started(batch, item);
                backend.DeletePermanently(item.Source);
                MarkChanged(batch, item, null);
                break;
            case ChangeKind.RestoreHeld:
                Started(batch, item);
                MarkChanged(batch, item, backend.RestoreHeld(item.Held ?? throw new IOException("The journal has no record of the held file.")));
                break;
            default:
                throw new NotSupportedException();
        }
    }

    private async Task UntrashAsync(JournalBatch batch, JournalItem item)
    {
        var backend = Required(item.Source);
        Started(batch, item);
        var restored = await backend.UntrashAsync(item.Trashed ?? new TrashedFile(item.Source, string.Empty));
        MarkChanged(batch, item, restored);
    }

    private void ExecuteTags(JournalBatch batch, JournalItem item, IFileBackend backend)
    {
        var file = backend.Stat(item.Source) ?? throw new IOException("The file is missing.");
        item.Before = references.Capture(item.Source);
        var scratch = journal.FolderOf(batch.Id);
        var atomic = backend.ReplacesAtomically(item.Source);
        var work = backend.WorkPath(item.Source, IdOf(batch, item), scratch);
        item.WorkPath = work;
        journal.Save(batch, item);
        var keepBackup = false;
        try
        {
            using (var original = backend.OpenRead(item.Source))
                SafeFiles.CopyNew(original, work);
            item.OldHash = SafeFiles.Hash(work);

            var edit = editor.Edit(work, file.Name, item.Change ?? new TagFields(), item.RestoreTagKinds);
            if (!edit.Written)
            {
                item.State = ItemState.Unchanged;
                item.NewSource = item.Source;
                journal.Save(batch, item);
                return;
            }

            item.OldTags = edit.Before;
            item.Change = edit.Applied;
            item.TagKindsBefore = edit.TagKindsBefore;
            item.NewHash = SafeFiles.Hash(work);

            if (!atomic)
            {
                var backup = Path.Combine(scratch, $"backup-{item.Index}.pardehbackup");
                using (var original = backend.OpenRead(item.Source))
                    SafeFiles.CopyNew(original, backup);
                item.BackupPath = backup;
            }

            if ((atomic ? HashOf(backend, item.Source) : SafeFiles.Hash(item.BackupPath!)) != item.OldHash)
                throw new IOException("The file changed while it was being edited.");

            Started(batch, item);
            try
            {
                backend.ReplaceContent(item.Source, work);
                Checkpoint?.Invoke("written");
                if (HashOf(backend, item.Source) != item.NewHash)
                    throw new IOException("The file does not read back as written.");
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && !atomic)
            {
                if (!TryRestoreBackup(backend, item))
                {
                    keepBackup = true;
                    throw new IOException($"{ex.Message} The original could not be put back; a copy of it is at {item.BackupPath}", ex);
                }

                throw new IOException($"{ex.Message} The original was put back.", ex);
            }

            MarkChanged(batch, item, item.Source);
        }
        finally
        {
            SafeFiles.TryDelete(work);
            if (!keepBackup)
                SafeFiles.TryDelete(item.BackupPath);
        }
    }

    private bool TryRestoreBackup(IFileBackend backend, JournalItem item)
    {
        try
        {
            if (item.BackupPath is not { } backup || !File.Exists(backup) || SafeFiles.Hash(backup) != item.OldHash)
                return false;
            if (backend.Stat(item.Source) is not null && HashOf(backend, item.Source) == item.OldHash)
                return true;
            // The backup itself is never handed over: writing may consume what it is given.
            var copy = backup + ".restore";
            File.Copy(backup, copy, overwrite: true);
            try
            {
                backend.ReplaceContent(item.Source, copy);
            }
            finally
            {
                SafeFiles.TryDelete(copy);
            }

            return HashOf(backend, item.Source) == item.OldHash;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    // Names that would be refused on some platform the file may travel to.
    private static readonly char[] Forbidden = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6",
        "LPT7", "LPT8", "LPT9",
    };

    /// <summary>A name Windows keeps for devices ("CON", "COM1"), which no file may have.</summary>
    public static bool IsReservedName(string stem) => Reserved.Contains(stem.Split('.')[0].TrimEnd());

    internal static void CheckName(string name, string oldName)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        if (stem.Trim().Length == 0 || name.IndexOfAny(Forbidden) >= 0 || name.Any(char.IsControl) || name != name.Trim() || name.StartsWith('.')
            || name.EndsWith('.') || stem.EndsWith(' ') || Reserved.Contains(stem.Split('.')[0].TrimEnd()))
            throw new IOException($"\"{name}\" can't be used as a file name.");
        if (!Path.GetExtension(name).Equals(Path.GetExtension(oldName), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Renaming can't change the file's type.");
        if (System.Text.Encoding.UTF8.GetByteCount(name) > 240)
            throw new IOException("The name is too long.");
    }

    private void ExecuteRename(JournalBatch batch, JournalItem item, IFileBackend backend)
    {
        var file = backend.Stat(item.Source) ?? throw new IOException("The file is missing.");
        var wanted = item.NewName ?? file.Name;
        if (wanted == file.Name)
        {
            item.State = ItemState.Unchanged;
            item.NewSource = item.Source;
            journal.Save(batch, item);
            return;
        }

        CheckName(wanted, file.Name);
        var name = SafeFiles.FreeName(wanted, candidate => backend.NameTaken(item.Source, candidate));
        if (name != wanted)
            item.Message = $"There is already a file named {wanted}, so it was named {name}.";
        item.OriginalName = file.Name;
        item.NewName = name;
        item.Before = references.Capture(item.Source);

        if (item.Undoes is null)
        {
            var oldStem = Path.GetFileNameWithoutExtension(file.Name);
            var newStem = Path.GetFileNameWithoutExtension(name);
            item.SideFiles = [.. backend.SideFiles(item.Source).Select(s => new SideFileMove(s.Source, s.Name, newStem + s.Name[oldStem.Length..], s.Shared))];
        }

        Started(batch, item);
        var renamed = backend.Rename(item.Source, name);
        MarkChanged(batch, item, renamed);
        MoveSideFiles(batch, item, backend);
    }

    private void MoveSideFiles(JournalBatch batch, JournalItem item, IFileBackend backend)
    {
        foreach (var side in item.SideFiles.Where(s => s.To is null))
        {
            try
            {
                if (side.ToName.Length == 0)
                {
                    // Undoing a copy: it goes only while it still equals what it was copied from.
                    if (side.Original is { } original && backend.Stat(side.From) is not null && backend.Stat(original) is not null
                        && HashOf(backend, side.From) == HashOf(backend, original))
                        backend.DeletePermanently(side.From);
                    side.To = string.Empty;
                }
                else if (!backend.NameTaken(side.From, side.ToName))
                {
                    side.To = side.Copy ? backend.CopySideFile(side.From, side.ToName) : backend.Rename(side.From, side.ToName);
                }
                else
                {
                    AddMessage(item, $"{side.FromName} was left: {side.ToName} already exists.");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                AddMessage(item, $"{side.FromName} was left: {ex.Message}");
            }

            journal.Save(batch, item);
        }
    }

    private static void AddMessage(JournalItem item, string message) =>
        item.Message = item.Message is null ? message : item.Message + " " + message;

    private void TryRelink(JournalBatch batch, JournalItem item)
    {
        try
        {
            Relink(batch, item);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AddMessage(item, "The file was changed, but the library could not be updated yet: " + ex.Message);
            journal.Save(batch, item);
        }
    }

    private void Relink(JournalBatch batch, JournalItem item)
    {
        Checkpoint?.Invoke("relinking");
        var before = item.Before ?? throw new InvalidOperationException("The journal has no record of the track.");
        switch (item.Kind)
        {
            case ChangeKind.Tags or ChangeKind.Rename:
            {
                var target = item.NewSource!;
                var file = Required(target).Stat(target) ?? throw new IOException("The file is missing.");
                if (item.Plan is null)
                {
                    var plan = references.PlanMove(item.Source, before, target);
                    item.Plan = plan with { Lyrics = plan.Lyrics & ~item.KeepLyrics };
                    journal.Save(batch, item);
                }

                references.ApplyMove(item.Source, before, target, file, item.Plan);
                break;
            }

            case ChangeKind.Hold or ChangeKind.Trash or ChangeKind.Delete:
                if (item.Keep is { } keep && Required(keep.Source).Stat(keep.Source) is not null)
                {
                    if (item.Plan?.Merge is null)
                    {
                        var gone = keep.Aliases.Where(a => !PhysicalFiles.SameRoute(a, keep.Source) && (BackendFor(a)?.Stat(a) is null));
                        item.Plan = references.PlanMerge([item.Source, .. gone], before, keep.Source);
                        journal.Save(batch, item);
                    }

                    references.ApplyMerge(item.Source, before, item.Plan);
                }
                else
                {
                    if (item.Plan is null)
                    {
                        item.Plan = references.PlanRemove(item.Source);
                        journal.Save(batch, item);
                    }

                    references.ApplyRemove(item.Source, item.Plan);
                }

                break;
            default:
            {
                var restored = item.NewSource!;
                var file = Required(restored).Stat(restored) ?? throw new IOException("The restored file is missing.");
                // The original's source, as the library knew it: a batch may have repointed Source.
                references.ApplyRestore(item.ExpectedSource ?? item.Source, before, restored, file, item.Plan!);
                break;
            }
        }

        item.State = ItemState.Done;
        journal.Save(batch, item);
    }

    // ---- After a crash ----------------------------------------------------------------------------

    /// <summary>
    /// Finishes what the app was doing when it stopped: a half-written file gets its original back, a completed change gets
    /// its references moved, and what had not started is left alone. Call once at start, before the first library scan and
    /// before the queue is restored. Returns a message for each file that needed attention.
    /// </summary>
    public IReadOnlyList<string> Recover()
    {
        var report = new List<string>();
        foreach (var id in journal.Ids.Reverse())
        {
            if (!journal.NeedsRecovery(id) || journal.Load(id) is not { } batch)
                continue;

            var undone = batch.UndoOf is { } original ? journal.Load(original) : null;
            foreach (var item in batch.Items)
            {
                try
                {
                    if (item.State == ItemState.Pending)
                    {
                        item.State = ItemState.Skipped;
                        item.Message = "Not started: Pardeh closed first.";
                        SafeFiles.TryDelete(item.WorkPath);
                        SafeFiles.TryDelete(item.BackupPath); // the file itself was never touched
                        journal.Save(batch, item);
                        continue;
                    }

                    if (item.State == ItemState.Started)
                        Resolve(batch, item);
                    if (item.State == ItemState.Changed)
                    {
                        if (item.Kind == ChangeKind.Rename)
                            MoveSideFiles(batch, item, Required(item.NewSource!));
                        Relink(batch, item);
                        SafeFiles.TryDelete(item.WorkPath);
                        SafeFiles.TryDelete(item.BackupPath);
                    }

                    Finish(batch, item, undone);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    Fail(batch, item, ex.Message);
                }

                if (item.State != ItemState.Done)
                    report.Add($"{item.Source}: {item.Message}");
            }

            batch.Interrupted |= !batch.Finished;
            batch.Finished = true;
            journal.Save(batch);
            Changed?.Invoke(this, []);
        }

        return report;
    }

    // An item that may or may not have reached the file: the file itself says which.
    private void Resolve(JournalBatch batch, JournalItem item)
    {
        var backend = Required(item.Source);

        void Changed(string? newSource)
        {
            item.NewSource = newSource;
            item.State = ItemState.Changed;
            journal.Save(batch, item);
        }

        void NotChanged(string message = "Interrupted. The file is as it was.") => Fail(batch, item, message);

        switch (item.Kind)
        {
            case ChangeKind.Tags:
            {
                var hash = backend.Stat(item.Source) is null ? null : HashOf(backend, item.Source);
                if (hash == item.NewHash)
                {
                    Changed(item.Source);
                    return;
                }

                SafeFiles.TryDelete(item.WorkPath);
                if (hash == item.OldHash)
                {
                    SafeFiles.TryDelete(item.BackupPath);
                    NotChanged();
                }
                else if (item.BackupPath is { } backup && File.Exists(backup))
                {
                    if (!TryRestoreBackup(backend, item))
                    {
                        // Stays "Started": tried again at the next start, and the backup is kept.
                        item.Message = $"The file was left half written and could not be repaired yet. A copy of the original is at {backup}";
                        journal.Save(batch, item);
                        return;
                    }

                    SafeFiles.TryDelete(backup);
                    NotChanged("Interrupted while writing. The original was put back.");
                }
                else
                {
                    NotChanged("Interrupted, and the file has been changed since.");
                }

                return;
            }

            case ChangeKind.Rename:
                if (backend.FindRenamed(item.Source, item.NewName!) is { } renamed)
                    Changed(renamed);
                else
                    NotChanged();
                return;

            case ChangeKind.Hold:
                if (backend.FindHeld(item.Source, IdOf(batch, item)) is { } held)
                {
                    item.Held = held;
                    Changed(null);
                }
                else
                {
                    NotChanged();
                }

                return;

            case ChangeKind.Delete or ChangeKind.Trash:
                if (backend.Stat(item.Source) is null)
                {
                    item.Trashed ??= item.Kind == ChangeKind.Trash ? new TrashedFile(item.Source, string.Empty) : null;
                    Changed(null);
                }
                else
                {
                    NotChanged();
                }

                return;

            default: // a restore
                var gone = item.Kind == ChangeKind.RestoreHeld && item.Held is { } waiting && backend.ListHeld().All(h => h.Id != waiting.Id);
                if (backend.Stat(item.Source) is not null && (item.Kind == ChangeKind.Untrash || gone))
                    Changed(item.Source);
                else
                    NotChanged("Interrupted before the file was restored.");
                return;
        }
    }
}
