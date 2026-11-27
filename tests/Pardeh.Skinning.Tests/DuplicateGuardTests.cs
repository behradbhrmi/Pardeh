using System.Diagnostics;
using System.Runtime.InteropServices;
using Pardeh.Core.Changes;
using Pardeh.Core.Duplicates;
using Pardeh.Core.LyricsKit;
using Pardeh.Core.Playback;
using static Pardeh.Skinning.Tests.ChangeFixture;

namespace Pardeh.Skinning.Tests;

/// <summary>Links made for tests: ways of reaching one file twice.</summary>
internal static partial class TestLinks
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string name, string existing, IntPtr security);

    public static void Hard(string name, string existing) => Assert.True(CreateHardLinkW(name, existing, IntPtr.Zero), "no hard link");

    /// <summary>A directory junction: needs no special rights.</summary>
    public static void Junction(string name, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "/c", "mklink", "/J", name, target })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0 && Directory.Exists(name), "no junction");
    }
}

/// <summary>The data-loss guard: nothing is removed as a copy unless it is shown to be another file than the one kept.</summary>
public sealed class PhysicalFileTests : IDisposable
{
    private readonly string _root = LyricsTestData.NewTempFolder("pardeh-physical-");

    public PhysicalFileTests() => Assert.SkipUnless(OperatingSystem.IsWindows(), "file numbers are read from Windows");

    public void Dispose()
    {
        foreach (var link in Directory.GetDirectories(_root).Where(d => new DirectoryInfo(d).LinkTarget is not null))
            Directory.Delete(link); // the link only, before the tree
        Directory.Delete(_root, recursive: true);
    }

    private string Make(string name, string content = "one song")
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static void AssertOneFile(string first, string second)
    {
        Assert.NotNull(PhysicalFiles.WhyNotDistinct(first, second));
        Assert.NotNull(PhysicalFiles.WhyNotDistinct(second, first));
    }

    [Fact]
    public void Two_files_with_the_same_bytes_are_two_files()
    {
        var first = Make("a.mp3");
        var second = Make("b.mp3");

        Assert.NotEqual(PhysicalFiles.IdOf(first), PhysicalFiles.IdOf(second));
        Assert.Null(PhysicalFiles.WhyNotDistinct(first, second));
    }

    [Fact]
    public void Spellings_of_one_path_are_one_file()
    {
        var file = Make("Music/a.mp3");

        AssertOneFile(file, file);
        AssertOneFile(file, file.ToUpperInvariant());
        AssertOneFile(file, file.Replace('\\', '/'));
        AssertOneFile(file, Path.Combine(_root, "Music", "..", "Music", "a.mp3"));
    }

    [Fact]
    public void A_hard_link_is_the_same_file()
    {
        var file = Make("a.mp3");
        var link = Path.Combine(_root, "other name.mp3");
        TestLinks.Hard(link, file);

        Assert.Equal(PhysicalFiles.IdOf(file), PhysicalFiles.IdOf(link));
        AssertOneFile(file, link);
    }

    [Fact]
    public void A_file_reached_through_a_junction_is_the_same_file()
    {
        var file = Make("real/a.mp3");
        TestLinks.Junction(Path.Combine(_root, "linked"), Path.Combine(_root, "real"));
        var through = Path.Combine(_root, "linked", "a.mp3");

        Assert.Equal(PhysicalFiles.IdOf(file), PhysicalFiles.IdOf(through));
        AssertOneFile(file, through);
    }

    [Fact]
    public void What_cant_be_told_is_not_called_distinct()
    {
        var file = Make("a.mp3");

        Assert.NotNull(PhysicalFiles.WhyNotDistinct(file, Path.Combine(_root, "missing.mp3")));
        Assert.NotNull(PhysicalFiles.WhyNotDistinct("content://some.provider/document/1", file));
    }
}

/// <summary>Removing an extra copy through the change service: the guard, what moves to the kept copy, and undo.</summary>
[Collection("MediaSources statics")]
public sealed class CopyRemovalTests : IDisposable
{
    private readonly ChangeFixture _f = new();

    public void Dispose()
    {
        if (Directory.Exists(_f.Music))
            foreach (var link in Directory.GetDirectories(_f.Music).Where(d => new DirectoryInfo(d).LinkTarget is not null))
                Directory.Delete(link);
        _f.Dispose();
    }

    private ChangeRequest Remove(string source, string keep, params string[] aliases) =>
        _f.Service.Remove(source) with { Keep = new KeptCopy(keep) { Aliases = aliases } };

    private static void AssertRefused(BatchResult result, params string[] stillThere)
    {
        Assert.All(result.Results.Where(r => r.Kind != ChangeKind.Tags), r => Assert.True(r.Status == ResultStatus.Failed, $"{r.Status} {r.Message}"));
        Assert.All(stillThere, f => Assert.True(File.Exists(f), f + " is gone"));
    }

    [Fact]
    public async Task A_file_is_never_removed_in_favour_of_itself_reached_another_way()
    {
        var real = _f.Make("a.mp3");
        TestLinks.Junction(Path.Combine(_f.Music, "linked"), _f.Music);
        var through = Path.Combine(_f.Music, "linked", "a.mp3");
        var hard = Path.Combine(_f.Music, "hard.mp3");
        TestLinks.Hard(hard, real);

        AssertRefused(await _f.Apply(Remove(real, through)), real);
        AssertRefused(await _f.Apply(Remove(through, real)), real);
        AssertRefused(await _f.Apply(Remove(hard, real)), real, hard);
        AssertRefused(await _f.Apply(Remove(real, real.ToUpperInvariant())), real);
        AssertRefused(await _f.Apply(new ChangeRequest(real, ChangeKind.Delete) { Keep = new KeptCopy(through) }), real);
    }

    [Fact]
    public async Task No_batch_removes_the_copies_it_means_to_keep()
    {
        var first = _f.Make("a.mp3");
        var second = _f.Make("b.mp3");

        AssertRefused(await _f.Apply(Remove(first, second), Remove(second, first)), first, second);
        // Also when the kept one is only removed without a word about copies.
        var third = _f.Make("c.mp3");
        var result = await _f.Apply(Remove(first, third), _f.Service.Remove(third));
        Assert.Equal(ResultStatus.Failed, result.Results[0].Status);
        Assert.True(File.Exists(first));
    }

    [Fact]
    public async Task Nothing_is_removed_when_the_kept_copy_is_missing_empty_or_changed()
    {
        var kept = _f.Make("a.mp3");
        var extra = _f.Make("b.mp3");
        var stamp = new FileStamp(new FileInfo(kept).Length, File.GetLastWriteTimeUtc(kept));
        var other = new FileStamp(new FileInfo(extra).Length, File.GetLastWriteTimeUtc(extra));

        AssertRefused(await _f.Apply(Remove(extra, Path.Combine(_f.Music, "none.mp3"))), extra);

        var empty = Path.Combine(_f.Music, "empty.mp3");
        File.WriteAllBytes(empty, []);
        AssertRefused(await _f.Apply(Remove(extra, empty)), extra);

        var changedKept = _f.Service.Remove(extra) with { Keep = new KeptCopy(kept) { Kept = stamp with { Size = stamp.Size + 1 }, Removed = other } };
        AssertRefused(await _f.Apply(changedKept), extra, kept);
        var changedExtra = _f.Service.Remove(extra) with { Keep = new KeptCopy(kept) { Kept = stamp, Removed = other with { ModifiedUtc = other.ModifiedUtc.AddMinutes(1) } } };
        AssertRefused(await _f.Apply(changedExtra), extra, kept);
    }

    private sealed class FailingEditor : ITagEditor
    {
        public TagFields Read(string path, string name) => new Pardeh.Nava.TagEditor().Read(path, name);

        public TagEditResult Edit(string path, string name, TagFields change, long? restoreTagKinds = null) => throw new TagEditException("No.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Nothing_is_removed_when_the_kept_copy_cant_be_given_its_details(bool systemTrash)
    {
        if (systemTrash)
            _f.Build(new LocalFileBackend(HoldingFolder.At(_f.HeldFolder), new FolderTrash(Path.Combine(_f.Root, "trash"))), new FailingEditor());
        else
            _f.Build(editor: new FailingEditor());
        var kept = _f.Make("a.mp3");
        var extra = _f.Make("b.mp3", title: "Better");

        var result = await _f.Apply(ChangeRequest.EditTags(kept, new TagFields { Title = "Better" }), Remove(extra, kept));

        Assert.All(result.Results, r => Assert.Equal(ResultStatus.Failed, r.Status));
        Assert.True(File.Exists(extra));
        Assert.True(File.Exists(kept));
    }

    private sealed record Setup(string Kept, string Extra, string Second);

    // Three copies of one song, with something remembered about each.
    private async Task<Setup> Remembered()
    {
        var kept = _f.Make("kept.mp3", title: "Kept");
        var extra = _f.Make("extra.mp3", title: "Extra");
        var second = _f.Make("second.mp3", title: "Second");
        await _f.ScanAsync();
        _f.Library.SetHistory(kept, 2, 3, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        _f.Library.SetHistory(extra, 5, 4, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
        _f.Library.SetHistory(second, 1, 10, new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        _f.Playlists.Save("mix", [extra, "x.mp3", kept, second]);
        _f.Favorites.Set(extra, true, "Extra", "Artist");
        _f.Lyrics.Store!.SaveOverride(Key(extra), LrcParser.Parse("[00:01.00]words", "test")!);
        _f.Player.CurrentPlaylist.Add(new MediaItem(extra));
        _f.Player.CurrentPlaylist.Add(new MediaItem("other.mp3"));
        return new Setup(kept, extra, second);
    }

    private void AssertAsBefore(Setup s)
    {
        Assert.True(File.Exists(s.Extra) && File.Exists(s.Second));
        var kept = _f.Library.Find(s.Kept)!;
        Assert.Equal((2, 3), (kept.Rating, kept.PlayCount));
        Assert.Equal((5, 4), (_f.Library.Find(s.Extra)!.Rating, _f.Library.Find(s.Extra)!.PlayCount));
        Assert.Equal(10, _f.Library.Find(s.Second)!.PlayCount);
        Assert.Equal([s.Extra, "x.mp3", s.Kept, s.Second], _f.Playlists.Read("mix"));
        Assert.True(_f.Favorites.IsFavorite(s.Extra));
        Assert.False(_f.Favorites.IsFavorite(s.Kept));
        Assert.Equal(LyricsPieces.None, _f.Lyrics.Store!.PiecesOf(Key(s.Kept)));
        Assert.Equal(LyricsPieces.Own, _f.Lyrics.Store.PiecesOf(Key(s.Extra)));
    }

    private void AssertMerged(Setup s)
    {
        Assert.False(File.Exists(s.Extra) || File.Exists(s.Second));
        var kept = _f.Library.Find(s.Kept)!;
        Assert.Equal((5, 17), (kept.Rating, kept.PlayCount)); // the higher ratings, all plays
        Assert.Equal(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), kept.LastPlayed);
        Assert.Null(_f.Library.Find(s.Extra));
        Assert.Equal([s.Kept, "x.mp3", s.Kept, s.Kept], _f.Playlists.Read("mix"));
        Assert.True(_f.Favorites.IsFavorite(s.Kept));
        Assert.False(_f.Favorites.IsFavorite(s.Extra));
        Assert.Equal(LyricsPieces.Own, _f.Lyrics.Store!.PiecesOf(Key(s.Kept)));
        Assert.Equal(s.Kept, _f.Player.CurrentPlaylist.Items[0].Source);
        Assert.Equal(2, _f.Player.CurrentPlaylist.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task What_is_remembered_goes_to_the_kept_copy_and_comes_back_with_undo(bool systemTrash)
    {
        var trash = Path.Combine(_f.Root, "trash");
        if (systemTrash)
            _f.Build(new LocalFileBackend(HoldingFolder.At(_f.HeldFolder), new FolderTrash(trash)));
        var s = await Remembered();

        var result = await _f.Apply(Remove(s.Extra, s.Kept), Remove(s.Second, s.Kept));

        AssertAll(ResultStatus.Done, result);
        AssertMerged(s);
        Assert.True(result.CanUndo);

        if (systemTrash) // a desktop trash is restored by the user
            foreach (var file in Directory.GetFiles(trash))
                File.Move(file, Path.Combine(_f.Music, Path.GetFileName(file)));
        var undone = await _f.Service.UndoAsync(result.BatchId!);

        AssertAll(ResultStatus.Done, undone);
        AssertAsBefore(s);
        Assert.Empty(_f.Leftovers());
    }

    [Fact]
    public async Task A_file_restored_from_the_bin_and_listed_again_before_undo_gets_its_history_back()
    {
        var trash = Path.Combine(_f.Root, "trash");
        _f.Build(new LocalFileBackend(HoldingFolder.At(_f.HeldFolder), new FolderTrash(trash)));
        var s = await Remembered();
        var result = await _f.Apply(Remove(s.Extra, s.Kept), Remove(s.Second, s.Kept));
        foreach (var file in Directory.GetFiles(trash))
            File.Move(file, Path.Combine(_f.Music, Path.GetFileName(file)));
        await _f.ScanAsync(); // the folder watcher gets there first
        Assert.Equal(0, _f.Library.Find(s.Extra)!.PlayCount);

        AssertAll(ResultStatus.Done, await _f.Service.UndoAsync(result.BatchId!));

        AssertAsBefore(s);
    }

    // A system trash that asks before it brings files back, all at once.
    private sealed class AskingBackend(LocalFileBackend inner, bool refuse) : IFileBackend
    {
        public List<int> Asked { get; } = [];

        public Task<IReadOnlyCollection<TrashedFile>> PrepareUntrashAsync(IReadOnlyList<TrashedFile> files)
        {
            Asked.Add(files.Count);
            return Task.FromResult<IReadOnlyCollection<TrashedFile>>(refuse ? [.. files] : []);
        }

        public bool Handles(string source) => inner.Handles(source);
        public string? WhyUnchangeable(string source) => inner.WhyUnchangeable(source);
        public FileStat? Stat(string source) => inner.Stat(source);
        public Stream OpenRead(string source) => inner.OpenRead(source);
        public bool ReplacesAtomically(string source) => inner.ReplacesAtomically(source);
        public string WorkPath(string source, string id, string scratchFolder) => inner.WorkPath(source, id, scratchFolder);
        public void ReplaceContent(string source, string localFile) => inner.ReplaceContent(source, localFile);
        public bool NameTaken(string source, string name) => inner.NameTaken(source, name);
        public string Rename(string source, string name) => inner.Rename(source, name);
        public string? FindRenamed(string source, string name) => inner.FindRenamed(source, name);
        public bool HasSystemTrash(string source) => inner.HasSystemTrash(source);
        public Task<IReadOnlyDictionary<string, TrashedFile?>> TrashAsync(IReadOnlyList<string> sources) => inner.TrashAsync(sources);
        public Task<string> UntrashAsync(TrashedFile trashed) => inner.UntrashAsync(trashed);
        public HeldFile Hold(string source, string id) => inner.Hold(source, id);
        public HeldFile? FindHeld(string source, string id) => inner.FindHeld(source, id);
        public string RestoreHeld(HeldFile held) => inner.RestoreHeld(held);
        public IReadOnlyList<HeldFile> ListHeld() => inner.ListHeld();
        public void DeleteHeld(HeldFile held) => inner.DeleteHeld(held);
        public void DeletePermanently(string source) => inner.DeletePermanently(source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Several_files_from_a_trash_are_asked_about_once(bool refuse)
    {
        var trash = Path.Combine(_f.Root, "trash");
        var asking = new AskingBackend(new LocalFileBackend(HoldingFolder.At(_f.HeldFolder), new FolderTrash(trash)), refuse);
        _f.Build(asking);
        var s = await Remembered();
        var result = await _f.Apply(Remove(s.Extra, s.Kept), Remove(s.Second, s.Kept));
        if (!refuse)
            foreach (var file in Directory.GetFiles(trash))
                File.Move(file, Path.Combine(_f.Music, Path.GetFileName(file)));

        var undone = await _f.Service.UndoAsync(result.BatchId!);

        Assert.Equal([2], asking.Asked);
        AssertAll(refuse ? ResultStatus.Denied : ResultStatus.Done, undone);
        if (refuse)
            AssertMerged(s); // nothing moved, nothing handed back
        else
            AssertAsBefore(s);
        Assert.Equal(refuse, _f.Journal.Load(result.BatchId!)!.CanUndo); // refused: it can be tried again
    }

    [Theory]
    [InlineData("replaced")]
    [InlineData("relinking")]
    public async Task A_removal_cut_off_by_a_crash_is_finished_at_the_next_start_once(string step)
    {
        var s = await Remembered();
        await _f.Apply(Remove(s.Second, s.Kept));
        _f.Service.Checkpoint = at =>
        {
            if (at == step)
                throw new OutOfMemoryException("the app is gone"); // nothing in the service catches this
        };
        await Assert.ThrowsAsync<OutOfMemoryException>(() => _f.Apply(Remove(s.Extra, s.Kept)));

        _f.Build();
        _f.Service.Recover();
        _f.Service.Recover();

        var kept = _f.Library.Find(s.Kept)!;
        Assert.Equal((5, 17), (kept.Rating, kept.PlayCount));
        Assert.Equal([s.Kept, "x.mp3", s.Kept, s.Kept], _f.Playlists.Read("mix"));
        Assert.True(_f.Favorites.IsFavorite(s.Kept));
        Assert.Equal(LyricsPieces.Own, _f.Lyrics.Store!.PiecesOf(Key(s.Kept)));
    }

    [Fact]
    public async Task Listings_that_go_with_the_removed_file_hand_over_too()
    {
        var kept = _f.Make("kept.mp3", title: "Kept");
        Directory.CreateDirectory(Path.Combine(_f.Music, "real"));
        var extra = _f.Make(Path.Combine("real", "extra.mp3"));
        TestLinks.Junction(Path.Combine(_f.Music, "linked"), Path.Combine(_f.Music, "real"));
        var through = Path.Combine(_f.Music, "linked", "extra.mp3");
        await _f.ScanAsync();
        _f.Playlists.Save("mix", [through]);

        var result = await _f.Apply(Remove(extra, kept, through));

        AssertAll(ResultStatus.Done, result);
        Assert.Equal([kept], _f.Playlists.Read("mix"));
        Assert.Null(_f.Library.Find(through));

        AssertAll(ResultStatus.Done, await _f.Service.UndoAsync(result.BatchId!));
        Assert.Equal([through], _f.Playlists.Read("mix"));
        Assert.True(File.Exists(through));
    }

    [Fact]
    public async Task Removing_the_playing_copy_goes_on_with_the_kept_one()
    {
        var kept = _f.Make("a.mp3");
        var extra = _f.Make("b.mp3");
        await _f.ScanAsync();
        _f.Player.CurrentPlaylist.Add(new MediaItem(extra));
        _f.Player.PlayItem(0);

        var result = await _f.Service.ApplyAsync("test", [Remove(extra, kept)], new ChangeOptions { MayInterruptPlayback = _ => Task.FromResult(true) });

        AssertAll(ResultStatus.Done, result);
        Assert.Equal(kept, _f.Player.CurrentMedia?.Source);
    }
}
