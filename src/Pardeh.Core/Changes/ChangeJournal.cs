using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pardeh.Core.Library;

namespace Pardeh.Core.Changes;

[JsonConverter(typeof(JsonStringEnumConverter<ChangeKind>))]
public enum ChangeKind
{
    Tags,
    Rename,

    /// <summary>Remove to the holding folder.</summary>
    Hold,

    /// <summary>Remove to the system trash.</summary>
    Trash,

    /// <summary>Delete for good.</summary>
    Delete,

    /// <summary>Undoes <see cref="Hold"/>.</summary>
    RestoreHeld,

    /// <summary>Undoes <see cref="Trash"/>.</summary>
    Untrash,
}

[JsonConverter(typeof(JsonStringEnumConverter<ItemState>))]
public enum ItemState
{
    /// <summary>Nothing has been touched.</summary>
    Pending,

    /// <summary>Everything needed to undo is saved; the file may or may not have been changed.</summary>
    Started,

    /// <summary>The file is changed and verified; references may still point at the old one.</summary>
    Changed,

    Done,
    Unchanged,
    Failed,
    Skipped,
}

/// <summary>A side file renamed or copied along with its track.</summary>
public sealed record SideFileMove(string From, string FromName, string ToName, bool Copy)
{
    /// <summary>Set once it has happened.</summary>
    public string? To { get; set; }

    /// <summary>In an undo that removes a copy: the file it was copied from.</summary>
    public string? Original { get; set; }
}

/// <summary>One file of a batch: what was there before, what was asked, and how far it got.</summary>
public sealed class JournalItem
{
    public int Index { get; set; }
    public ChangeKind Kind { get; set; }
    public string Source { get; set; } = string.Empty;

    /// <summary>Where the file is after the change (null: removed, or not known yet).</summary>
    public string? NewSource { get; set; }

    public ItemState State { get; set; }
    public string? Message { get; set; }

    public string? OriginalName { get; set; }
    public string? NewName { get; set; }

    /// <summary>The change asked for.</summary>
    public TagFields? Change { get; set; }

    /// <summary>The old values of what was changed, pictures included.</summary>
    public TagFields? OldTags { get; set; }

    public long? TagKindsBefore { get; set; }

    /// <summary>In an undo: the tag kinds to leave in the file.</summary>
    public long? RestoreTagKinds { get; set; }

    public string? WorkPath { get; set; }

    /// <summary>A copy of the whole original, kept until the new contents are verified (only where they can't be swapped in at once).</summary>
    public string? BackupPath { get; set; }

    public string? OldHash { get; set; }
    public string? NewHash { get; set; }

    /// <summary>For the removal of an extra copy: the copy that stays, which the removed one's history goes to.</summary>
    public KeptCopy? Keep { get; set; }

    /// <summary>What pointed at the file before the change.</summary>
    public ReferenceSnapshot? Before { get; set; }

    /// <summary>What was moved or taken out; written before it happens.</summary>
    public RelinkPlan? Plan { get; set; }

    public List<SideFileMove> SideFiles { get; set; } = [];

    public HeldFile? Held { get; set; }
    public TrashedFile? Trashed { get; set; }

    /// <summary>In an undo batch: the item of the original batch this one reverses.</summary>
    public int? Undoes { get; set; }

    /// <summary>In an undo batch: the source the file had before the change being reversed.</summary>
    public string? ExpectedSource { get; set; }

    /// <summary>In an undo: lyrics that were saved under the new details before the change, so they stay there.</summary>
    public LyricsKit.LyricsPieces KeepLyrics { get; set; }

    /// <summary>The user agreed to stop playback for this file.</summary>
    [JsonIgnore]
    public bool MayInterrupt { get; set; }

    public bool Undone { get; set; }

    /// <summary>The checks on the copy to keep held this removal back.</summary>
    [JsonIgnore]
    public bool Refused { get; set; }
}

public sealed class JournalBatch
{
    public string Id { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }

    /// <summary>The batch this one undoes.</summary>
    public string? UndoOf { get; set; }

    public bool Finished { get; set; }

    /// <summary>The app stopped before the batch was finished.</summary>
    public bool Interrupted { get; set; }

    /// <summary>An item is still half way (a backup waiting, or references not moved): the next start tries again.</summary>
    public bool Unsettled { get; set; }

    [JsonIgnore]
    public List<JournalItem> Items { get; set; } = [];

    /// <summary>Something in it can still be undone.</summary>
    [JsonIgnore]
    public bool CanUndo => UndoOf is null && Items.Any(i => i.State == ItemState.Done && !i.Undone && i.Kind != ChangeKind.Delete);
}

/// <summary>
/// The journal of changes to files, in the app data folder: one folder per batch, with one small file per item so that a
/// large batch stays cheap to update. Every write reaches the disk before the change it describes is made.
/// </summary>
public sealed class ChangeJournal(string folder, int keep = 20)
{
    private static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public string Folder { get; } = folder;

    public string FolderOf(string batchId) => Path.Combine(Folder, batchId);

    private string ItemPath(string batchId, int index) => Path.Combine(FolderOf(batchId), "items", $"{index:00000}.json");

    public JournalBatch Create(string description, IEnumerable<JournalItem> items, string? undoOf = null)
    {
        var batch = new JournalBatch
        {
            // Sorts by age.
            Id = $"{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid().ToString("N")[..6]}",
            Description = description,
            CreatedUtc = DateTime.UtcNow,
            UndoOf = undoOf,
            Items = [.. items],
        };
        Directory.CreateDirectory(Path.Combine(FolderOf(batch.Id), "items"));
        for (var i = 0; i < batch.Items.Count; i++)
        {
            batch.Items[i].Index = i;
            Save(batch, batch.Items[i]);
        }

        Save(batch); // last: a batch without its header was never started
        return batch;
    }

    public void Save(JournalBatch batch)
    {
        batch.Unsettled = batch.Items.Any(i => i.State is ItemState.Started or ItemState.Changed);
        SafeFiles.WriteDurably(Path.Combine(FolderOf(batch.Id), "batch.json"), JsonSerializer.SerializeToUtf8Bytes(batch, Json));
    }

    /// <summary>True when a batch needs attention at start. Only its header is read.</summary>
    public bool NeedsRecovery(string id)
    {
        try
        {
            var header = Path.Combine(FolderOf(id), "batch.json");
            return File.Exists(header) && JsonSerializer.Deserialize<JournalBatch>(File.ReadAllText(header, Encoding.UTF8), Json) is { } batch
                && (!batch.Finished || batch.Unsettled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    public void Save(JournalBatch batch, JournalItem item) =>
        SafeFiles.WriteDurably(ItemPath(batch.Id, item.Index), JsonSerializer.SerializeToUtf8Bytes(item, Json));

    public JournalBatch? Load(string id)
    {
        try
        {
            var header = Path.Combine(FolderOf(id), "batch.json");
            if (!File.Exists(header) || JsonSerializer.Deserialize<JournalBatch>(File.ReadAllText(header, Encoding.UTF8), Json) is not { } batch)
                return null;
            batch.Items = Directory.EnumerateFiles(Path.Combine(FolderOf(id), "items"), "*.json")
                .Order(StringComparer.Ordinal)
                .Select(f => JsonSerializer.Deserialize<JournalItem>(File.ReadAllText(f, Encoding.UTF8), Json)!)
                .ToList();
            return batch;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null; // left on disk: it may hold the only copy of old values
        }
    }

    /// <summary>The batch ids, newest first.</summary>
    public IReadOnlyList<string> Ids
    {
        get
        {
            try
            {
                return Directory.Exists(Folder)
                    ? Directory.EnumerateDirectories(Folder).Select(Path.GetFileName).OfType<string>().OrderDescending(StringComparer.Ordinal).ToList()
                    : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }
    }

    public JournalBatch? LastUndoable() => Ids.Select(Load).FirstOrDefault(b => b is { Finished: true, CanUndo: true });

    /// <summary>Drops the oldest finished batches, never one that still keeps a backup of a file.</summary>
    public void Prune()
    {
        foreach (var id in Ids.Skip(keep))
        {
            var batch = Load(id);
            if (batch is not { Finished: true } || batch.Items.Any(i => i.State is ItemState.Started or ItemState.Changed || (i.BackupPath is { } backup && File.Exists(backup))))
                continue;
            try
            {
                Directory.Delete(FolderOf(id), recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
