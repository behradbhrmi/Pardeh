using Pardeh.Core.Changes;
using Pardeh.Core.Playback;
using Pardeh.Nava;
using static Pardeh.Skinning.Tests.LyricsTestData;

namespace Pardeh.Skinning.Tests;

/// <summary>A backend that rewrites files in place, as Android shared storage forces, and can fail or be stopped half way.</summary>
internal sealed class InPlaceBackend(LocalFileBackend inner) : IFileBackend
{
    /// <summary>Called with the file half written.</summary>
    public Action? HalfWritten { get; set; }

    /// <summary>How many of the next writes fail half way.</summary>
    public int FailingWrites { get; set; }

    public List<(FileAccessKind Kind, int Count)> Requests { get; } = [];
    public Func<string, bool>? Deny { get; set; }

    public Task<IReadOnlyCollection<string>> RequestAccessAsync(IReadOnlyList<string> sources, FileAccessKind kind)
    {
        Requests.Add((kind, sources.Count));
        return Task.FromResult<IReadOnlyCollection<string>>([.. sources.Where(s => Deny?.Invoke(s) == true)]);
    }

    public bool ReplacesAtomically(string source) => false;
    public string WorkPath(string source, string id, string scratchFolder) => Path.Combine(scratchFolder, id + ".pardehwork");

    public void ReplaceContent(string source, string localFile)
    {
        var bytes = File.ReadAllBytes(localFile);
        using var target = new FileStream(source, FileMode.Open, FileAccess.Write);
        target.SetLength(0);
        target.Write(bytes, 0, bytes.Length / 2);
        target.Flush(true);
        if (FailingWrites > 0)
        {
            FailingWrites--;
            throw new IOException("No space left on the device.");
        }

        if (HalfWritten is { } hook)
        {
            hook();
        }

        target.Write(bytes, bytes.Length / 2, bytes.Length - bytes.Length / 2);
    }

    public bool Handles(string source) => inner.Handles(source);
    public string? WhyUnchangeable(string source) => inner.WhyUnchangeable(source);
    public FileStat? Stat(string source) => inner.Stat(source);
    public Stream OpenRead(string source) => new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    public bool NameTaken(string source, string name) => inner.NameTaken(source, name);
    public string Rename(string source, string name) => inner.Rename(source, name);
    public string? FindRenamed(string source, string name) => inner.FindRenamed(source, name);
    public HeldFile Hold(string source, string id) => inner.Hold(source, id);
    public HeldFile? FindHeld(string source, string id) => inner.FindHeld(source, id);
    public string RestoreHeld(HeldFile held) => inner.RestoreHeld(held);
    public IReadOnlyList<HeldFile> ListHeld() => inner.ListHeld();
    public void DeleteHeld(HeldFile held) => inner.DeleteHeld(held);
    public void DeletePermanently(string source) => inner.DeletePermanently(source);
}

/// <summary>A pretend system trash: a folder.</summary>
internal sealed class FolderTrash(string folder) : ISystemTrash
{
    public bool Accepts(string path) => true;

    public void Trash(string path)
    {
        Directory.CreateDirectory(folder);
        File.Move(path, Path.Combine(folder, Path.GetFileName(path)));
    }
}

[Collection("MediaSources statics")]
public sealed class FileChangeServiceTests : IDisposable
{
    private readonly ChangeFixture _f = new();
    private readonly List<string> _snapshots = [];

    public void Dispose()
    {
        _f.Dispose();
        foreach (var folder in _snapshots.Where(Directory.Exists))
            Directory.Delete(folder, true);
    }

    private static TagFields Title(string title) => new() { Title = title };

    private static string TitleOf(string path) => new TagEditor().Read(path, Path.GetFileName(path)).Title!;

    [Fact]
    public void The_audio_digest_ignores_tags_and_notices_other_sound()
    {
        var a = _f.Make("a.mp3", "One");
        var b = _f.Make("b.mp3", "Two", "Other", "Else");
        var other = Path.Combine(_f.Music, "other.flac");
        FfmpegTool.MakeAudio(other, "flac");

        Assert.Equal(AudioDigest.Compute(a), AudioDigest.Compute(b));
        var cut = Path.Combine(_f.Music, "cut.mp3");
        File.WriteAllBytes(cut, File.ReadAllBytes(a)[..^600]);
        Assert.NotEqual(AudioDigest.Compute(a), AudioDigest.Compute(cut));
    }

    [Fact]
    public async Task Undo_puts_back_tags_name_history_and_lyrics()
    {
        var path = _f.Make("old.mp3", "Junk | site.example");
        await _f.ScanAsync();
        _f.Library.SetRating(path, 5);
        _f.Playlists.Save("Mine", [path]);
        _f.Lyrics.SaveOverride(ChangeFixture.Info(path), Plain("words"));
        var key = ChangeFixture.Key(path);

        // Tags and a rename of the same file in one batch: the second follows the first.
        var result = await _f.Apply(ChangeRequest.EditTags(path, Title("Song")), ChangeRequest.Rename(path, "new.mp3"));
        ChangeFixture.AssertAll(ResultStatus.Done, result);
        var renamed = result.Results[1].NewSource!;
        Assert.Equal("Song", TitleOf(renamed));
        Assert.True(result.CanUndo);

        var undo = await _f.Service.UndoAsync(result.BatchId!);

        ChangeFixture.AssertAll(ResultStatus.Done, undo);
        Assert.False(File.Exists(renamed));
        Assert.Equal("Junk | site.example", TitleOf(path));
        var item = Assert.Single(_f.Library.Items);
        Assert.Equal((path, 5, "Junk | site.example"), (item.Path, item.Rating, item.Title));
        Assert.Equal([path], _f.Playlists.Read("Mine"));
        Assert.NotNull(_f.Lyrics.Store!.GetOverride(key));
        Assert.Null(_f.Service.LastUndoable());
        Assert.Empty(_f.Leftovers());
    }

    [Fact]
    public async Task Undo_restores_the_old_cover_from_the_journal_after_a_restart()
    {
        var path = _f.Make("cover.mp3");
        var old = new TagPicture("FrontCover", "image/jpeg", "", FfmpegTool.MakeJpeg(Path.Combine(_f.Root, "old.jpg"), 1));
        var fresh = old with { Data = FfmpegTool.MakeJpeg(Path.Combine(_f.Root, "new.jpg"), 2) };
        await _f.ScanAsync();
        await _f.Apply(ChangeRequest.EditTags(path, new TagFields { Pictures = [old] }));
        var result = await _f.Apply(ChangeRequest.EditTags(path, new TagFields { Pictures = [fresh] }));

        _f.Build();
        ChangeFixture.AssertAll(ResultStatus.Done, await _f.Service.UndoAsync(result.BatchId!));

        Assert.Equal(old.Data, Assert.Single(new TagEditor().Read(path, "cover.mp3").Pictures!).Data);
    }

    [Fact]
    public async Task A_rename_never_overwrites_another_file()
    {
        var path = _f.Make("one.mp3", "One");
        var other = _f.Make("two.mp3", "Two");
        var bytes = File.ReadAllBytes(other);
        await _f.ScanAsync();

        var result = await _f.Apply(ChangeRequest.Rename(path, "two.mp3"));

        Assert.Equal(Path.Combine(_f.Music, "two (2).mp3"), result.Results[0].NewSource);
        Assert.Contains("already a file", result.Results[0].Message);
        Assert.Equal(bytes, File.ReadAllBytes(other));
    }

    [Fact]
    public async Task Two_songs_given_one_name_both_survive_and_a_case_change_works()
    {
        var a = _f.Make("a.mp3", "A");
        var b = _f.Make("b.mp3", "B");

        ChangeFixture.AssertAll(ResultStatus.Done, await _f.Apply(ChangeRequest.Rename(a, "same.mp3"), ChangeRequest.Rename(b, "same.mp3")));
        ChangeFixture.AssertAll(ResultStatus.Done, await _f.Apply(ChangeRequest.Rename(Path.Combine(_f.Music, "same.mp3"), "Same.mp3")));

        Assert.Equal(["Same.mp3", "same (2).mp3"], Directory.GetFiles(_f.Music).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("a/b.mp3")]
    [InlineData("..\\b.mp3")]
    [InlineData("CON.mp3")]
    [InlineData("what?.mp3")]
    [InlineData("song.flac")]
    [InlineData(" .mp3")]
    [InlineData(".hidden.mp3")]
    public async Task A_name_that_cannot_be_used_is_refused(string name)
    {
        var path = _f.Make("a.mp3");

        var result = await _f.Apply(ChangeRequest.Rename(path, name));

        Assert.Equal(ResultStatus.Failed, result.Results[0].Status);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task A_failure_on_one_file_does_not_stop_the_batch()
    {
        var good = _f.Make("good.mp3", "Old");
        var locked = _f.Make("readonly.mp3", "Old");
        var broken = Path.Combine(_f.Music, "broken.flac");
        File.WriteAllText(broken, "not audio");
        var last = _f.Make("last.mp3", "Old");
        File.SetAttributes(locked, FileAttributes.ReadOnly);
        await _f.ScanAsync();

        var result = await _f.Apply(
        [
            ChangeRequest.EditTags(good, Title("New")),
            ChangeRequest.EditTags(locked, Title("New")),
            ChangeRequest.EditTags(Path.Combine(_f.Music, "missing.mp3"), Title("New")),
            ChangeRequest.EditTags(broken, Title("New")),
            ChangeRequest.EditTags("https://example.com/a.mp3", Title("New")),
            ChangeRequest.EditTags(last, Title("New")),
        ]);

        Assert.Equal([ResultStatus.Done, ResultStatus.Failed, ResultStatus.Failed, ResultStatus.Failed, ResultStatus.Failed, ResultStatus.Done],
            result.Results.Select(r => r.Status));
        Assert.All(result.Results.Where(r => r.Status == ResultStatus.Failed), r => Assert.False(string.IsNullOrWhiteSpace(r.Message)));
        Assert.Equal("not audio", File.ReadAllText(broken));
        Assert.Equal("Old", TitleOf(locked));
        Assert.Equal("New", TitleOf(last));
        Assert.Empty(_f.Leftovers());
    }

    [Fact]
    public async Task A_write_that_fails_half_way_leaves_the_original()
    {
        var path = _f.Make("a.mp3", "Old");
        var bytes = File.ReadAllBytes(path);
        var backend = new InPlaceBackend(new LocalFileBackend(HoldingFolder.At(_f.HeldFolder))) { FailingWrites = 1 };
        _f.Build(backend);

        var result = await _f.Apply(ChangeRequest.EditTags(path, Title("New")));

        Assert.Equal(ResultStatus.Failed, result.Results[0].Status);
        Assert.Contains("put back", result.Results[0].Message);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Empty(_f.Leftovers());
    }

    [Fact]
    public async Task When_even_the_original_cannot_be_put_back_its_copy_is_kept_and_used_at_the_next_start()
    {
        var path = _f.Make("a.mp3", "Old");
        var bytes = File.ReadAllBytes(path);
        var backend = new InPlaceBackend(new LocalFileBackend(HoldingFolder.At(_f.HeldFolder))) { FailingWrites = 2 };
        _f.Build(backend);

        var result = await _f.Apply(ChangeRequest.EditTags(path, Title("New")));

        Assert.Contains("copy of it is at", result.Results[0].Message);
        var backup = Assert.Single(_f.Leftovers(), f => f.EndsWith(".pardehbackup"));
        Assert.Equal(bytes, File.ReadAllBytes(backup));
    }

    [Fact]
    public async Task Access_is_asked_once_per_batch_and_a_refusal_skips_only_that_file()
    {
        var a = _f.Make("a.mp3", "Old");
        var b = _f.Make("b.mp3", "Old");
        var c = _f.Make("c.mp3", "Old");
        var backend = new InPlaceBackend(new LocalFileBackend(HoldingFolder.At(_f.HeldFolder))) { Deny = s => s.EndsWith("b.mp3") };
        _f.Build(backend);

        var result = await _f.Apply(ChangeRequest.EditTags(a, Title("New")), ChangeRequest.EditTags(b, Title("New")), ChangeRequest.EditTags(c, Title("New")));

        Assert.Equal([(FileAccessKind.Write, 3)], backend.Requests);
        Assert.Equal([ResultStatus.Done, ResultStatus.Denied, ResultStatus.Done], result.Results.Select(r => r.Status));
        Assert.Equal("Old", TitleOf(b));
    }

    // ---- Removal --------------------------------------------------------------------------------

    [Fact]
    public async Task A_removed_file_waits_in_the_holding_folder_and_comes_back_with_everything()
    {
        var path = _f.Make("a.mp3");
        var other = _f.Make("z.mp3", "Other");
        var bytes = File.ReadAllBytes(path);
        await _f.ScanAsync();
        _f.Library.SetRating(path, 3);
        _f.Library.RecordPlay(path);
        _f.Favorites.Set(path, true, "Song", "Artist");
        _f.Playlists.Save("Mine", [path, other]);
        _f.Player.Enqueue([path, other]);

        var result = await _f.Apply(_f.Service.Remove(path));

        ChangeFixture.AssertAll(ResultStatus.Done, result);
        Assert.False(File.Exists(path));
        var held = Assert.Single(_f.Service.Backends[0].ListHeld());
        Assert.Equal((path, bytes.Length), (held.OriginalSource, (int)held.Size));
        Assert.EndsWith(HoldingFolder.Extension, held.HeldSource);
        Assert.Equal([other], _f.Library.Items.Select(i => i.Path));
        Assert.False(_f.Favorites.IsFavorite(path));
        Assert.Equal([other], _f.Playlists.Read("Mine"));
        Assert.Equal([other], _f.Player.CurrentPlaylist.Items.Select(i => i.Source));

        // Also after a restart: the journal and the holding folder have what is needed.
        _f.Build();
        ChangeFixture.AssertAll(ResultStatus.Done, await _f.Service.UndoAsync(result.BatchId!));
        await _f.ScanAsync();

        Assert.Equal(bytes, File.ReadAllBytes(path));
        var item = _f.Library.Find(path)!;
        Assert.Equal((3, 1), (item.Rating, item.PlayCount));
        Assert.True(_f.Favorites.IsFavorite(path));
        Assert.Equal([path, other], _f.Playlists.Read("Mine"));
        Assert.Empty(_f.Service.Backends[0].ListHeld());
    }

    [Fact]
    public async Task Restoring_never_overwrites_a_file_that_took_the_old_place()
    {
        var path = _f.Make("a.mp3", "Removed");
        await _f.ScanAsync();
        _f.Playlists.Save("Mine", [path]);
        _f.Lyrics.SaveOverride(ChangeFixture.Info(path), Plain("words"));
        var result = await _f.Apply(_f.Service.Remove(path));
        _f.Make("a.mp3", "Newcomer");
        await _f.ScanAsync();

        ChangeFixture.AssertAll(ResultStatus.Done, await _f.Service.UndoAsync(result.BatchId!));

        var restored = Path.Combine(_f.Music, "a (restored).mp3");
        Assert.Equal("Newcomer", TitleOf(path));
        Assert.Equal("Removed", TitleOf(restored));
        Assert.Equal([restored], _f.Playlists.Read("Mine"));
        Assert.NotNull(_f.Lyrics.Store!.GetOverride(ChangeFixture.Key(restored)));
    }

    [Fact]
    public async Task A_file_that_cannot_be_held_stays_where_it_is()
    {
        var path = _f.Make("a.mp3");
        File.WriteAllText(_f.HeldFolder, "a file where the folder should be");
        await _f.ScanAsync();

        var result = await _f.Apply(_f.Service.Remove(path));

        Assert.Equal(ResultStatus.Failed, result.Results[0].Status);
        Assert.True(File.Exists(path));
        Assert.Single(_f.Library.Items);
    }

    [Fact]
    public void The_copying_holding_folder_holds_and_restores_and_only_deletes_its_own_files()
    {
        var path = _f.Make("آهنگ " + new string('x', 100) + ".mp3");
        var bytes = File.ReadAllBytes(path);
        var holding = HoldingFolder.At(_f.HeldFolder, canMove: false);

        var held = holding.Hold(path, "one");
        Assert.False(File.Exists(path));
        Assert.Equal(bytes, File.ReadAllBytes(held.HeldSource));
        Assert.Equal(held, Assert.Single(holding.List()));
        Assert.Equal(path, holding.Restore(held));
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Empty(holding.List());

        Assert.Throws<IOException>(() => holding.Delete(held with { HeldSource = path }));
        Assert.True(File.Exists(path));
        var again = holding.Hold(path, "two");
        holding.Delete(again);
        Assert.Empty(Directory.GetFiles(_f.HeldFolder));
    }

    [Fact]
    public async Task The_system_trash_is_used_where_there_is_one_and_undo_waits_for_the_file_to_be_restored()
    {
        var path = _f.Make("a.mp3");
        var trash = Path.Combine(_f.Root, "bin");
        _f.Build(new LocalFileBackend(HoldingFolder.At(_f.HeldFolder), new FolderTrash(trash)));
        await _f.ScanAsync();
        _f.Library.SetRating(path, 4);

        var request = _f.Service.Remove(path);
        Assert.Equal(ChangeKind.Trash, request.Kind);
        var result = await _f.Apply(request);
        ChangeFixture.AssertAll(ResultStatus.Done, result);
        Assert.Empty(_f.Library.Items);

        var early = await _f.Service.UndoAsync(result.BatchId!);
        Assert.Contains("Recycle Bin", early.Results[0].Message);

        File.Move(Path.Combine(trash, "a.mp3"), path); // the user restores it in the system's window
        ChangeFixture.AssertAll(ResultStatus.Done, await _f.Service.UndoAsync(result.BatchId!));
        Assert.Equal(4, _f.Library.Find(path)!.Rating);
    }

    [Fact]
    public async Task A_permanent_delete_removes_the_file_and_cannot_be_undone()
    {
        var path = _f.Make("a.mp3");
        await _f.ScanAsync();

        var result = await _f.Apply(new ChangeRequest(path, ChangeKind.Delete));

        ChangeFixture.AssertAll(ResultStatus.Done, result);
        Assert.False(File.Exists(path));
        Assert.False(result.CanUndo);
        Assert.Empty(_f.Service.Backends[0].ListHeld());
        Assert.Empty(_f.Library.Items);
    }

    // ---- Lyrics shared between tracks ------------------------------------------------------------

    [Fact]
    public async Task Lyrics_two_tracks_share_stay_with_the_one_not_changed()
    {
        var a = _f.Make("a.mp3", "Same");
        var b = _f.Make("b.mp3", "Same");
        await _f.ScanAsync();
        _f.Lyrics.SaveOverride(ChangeFixture.Info(a), Plain("words"));
        var shared = ChangeFixture.Key(b);

        var result = await _f.Apply(ChangeRequest.EditTags(a, Title("Different")));

        Assert.NotNull(_f.Lyrics.Store!.GetOverride(ChangeFixture.Key(a)));
        Assert.NotNull(_f.Lyrics.Store!.GetOverride(shared));
        await _f.Service.UndoAsync(result.BatchId!);
        Assert.NotNull(_f.Lyrics.Store!.GetOverride(shared));
    }

    [Fact]
    public async Task Lyrics_already_saved_under_the_new_details_are_not_replaced()
    {
        var a = _f.Make("a.mp3", "Junk");
        var b = _f.Make("b.mp3", "Clean");
        await _f.ScanAsync();
        _f.Lyrics.SaveOverride(ChangeFixture.Info(a), Plain("from the junk one"));
        _f.Lyrics.SetOffset(ChangeFixture.Info(a), 500);
        _f.Lyrics.SaveOverride(ChangeFixture.Info(b), Plain("already clean"));
        var (junk, clean) = (ChangeFixture.Key(a), ChangeFixture.Key(b));

        var result = await _f.Apply(ChangeRequest.EditTags(a, Title("Clean")));

        var store = _f.Lyrics.Store!;
        Assert.Equal("already clean", store.GetOverride(clean)!.Lines[0].Text);
        Assert.Equal("from the junk one", store.GetOverride(junk)!.Lines[0].Text); // nothing is lost
        Assert.Equal(500, store.GetOffset(clean));

        await _f.Service.UndoAsync(result.BatchId!);
        Assert.Equal("already clean", store.GetOverride(clean)!.Lines[0].Text);
        Assert.Equal("from the junk one", store.GetOverride(junk)!.Lines[0].Text);
        Assert.Equal(500, store.GetOffset(junk));
    }

    // ---- The playing file ------------------------------------------------------------------------

    [Fact]
    public async Task A_playing_file_is_left_for_later_unless_the_user_agrees()
    {
        var path = _f.Make("a.mp3", "Old");
        await _f.ScanAsync();
        _f.Player.OpenFiles([path]);

        var result = await _f.Apply(ChangeRequest.EditTags(path, Title("New")));

        Assert.Equal(ResultStatus.Deferred, result.Results[0].Status);
        Assert.Equal("Old", TitleOf(path));
        Assert.True(_f.Engine.IsOpen);
        Assert.True(_f.Engine.Playing);
    }

    [Fact]
    public async Task A_playing_file_is_released_renamed_and_taken_up_where_it_was()
    {
        var path = _f.Make("a.mp3", "Old");
        var next = _f.Make("b.mp3", "Next");
        await _f.ScanAsync();
        _f.Player.OpenFiles([path, next]);
        _f.Engine.Position = TimeSpan.FromSeconds(30);
        var plays = 0;
        _f.Player.CurrentMediaChanged += (_, _) => plays += _f.Player.IsResumingFile ? 0 : 1;
        var openedWhileWriting = new List<bool>();
        _f.Service.Checkpoint = step =>
        {
            if (step is "started" or "replaced")
                openedWhileWriting.Add(_f.Engine.IsOpen);
        };
        var asked = new List<string>();

        var result = await _f.Service.ApplyAsync("test",
            [ChangeRequest.EditTags(path, Title("New")), ChangeRequest.Rename(path, "renamed.mp3")],
            new ChangeOptions { MayInterruptPlayback = files => { asked.AddRange(files); return Task.FromResult(true); } });

        ChangeFixture.AssertAll(ResultStatus.Done, result);
        Assert.Equal([path], asked);
        Assert.All(openedWhileWriting, Assert.False);
        var renamed = result.Results[1].NewSource!;
        Assert.Equal([renamed, next], _f.Player.CurrentPlaylist.Items.Select(i => i.Source));
        Assert.Equal(renamed, _f.Player.CurrentMedia!.Source);
        Assert.Equal("New", _f.Player.CurrentMedia.Name);
        Assert.True(_f.Engine.Playing);
        Assert.Equal(30, _f.Engine.Position.TotalSeconds);
        Assert.Equal(0, plays); // not counted as another play
    }

    [Fact]
    public async Task Undoing_a_tag_change_and_rename_of_the_playing_file_does_both()
    {
        var path = _f.Make("a.mp3", "Old");
        await _f.ScanAsync();
        _f.Player.OpenFiles([path]);
        var options = new ChangeOptions { MayInterruptPlayback = _ => Task.FromResult(true) };
        var result = await _f.Service.ApplyAsync("test", [ChangeRequest.EditTags(path, Title("New")), ChangeRequest.Rename(path, "renamed.mp3")], options);

        var undo = await _f.Service.UndoAsync(result.BatchId!, options);

        ChangeFixture.AssertAll(ResultStatus.Done, undo);
        Assert.Equal("Old", TitleOf(path));
        Assert.Equal(path, _f.Player.CurrentMedia!.Source);
        Assert.True(_f.Engine.Playing);
    }

    [Fact]
    public async Task Removing_the_playing_file_leaves_the_rest_of_the_queue()
    {
        var path = _f.Make("a.mp3");
        var next = _f.Make("b.mp3", "Next");
        await _f.ScanAsync();
        _f.Player.OpenFiles([path, next]);

        var result = await _f.Service.ApplyAsync("test", [_f.Service.Remove(path)],
            new ChangeOptions { MayInterruptPlayback = _ => Task.FromResult(true) });

        ChangeFixture.AssertAll(ResultStatus.Done, result);
        Assert.Equal([next], _f.Player.CurrentPlaylist.Items.Select(i => i.Source));
        Assert.Null(_f.Player.CurrentMedia);
        Assert.False(_f.Engine.IsOpen);
    }

    // ---- Scans ------------------------------------------------------------------------------------

    [Fact]
    public async Task Scans_asked_for_during_a_batch_wait_for_its_end()
    {
        var path = _f.Make("a.mp3", "Old");
        await _f.ScanAsync();
        _f.Library.SetRating(path, 2);
        var during = new List<bool>();
        _f.Service.Checkpoint = step =>
        {
            _ = _f.Library.ScanAsync(); // as the folder watcher would
            during.Add(_f.Library.IsScanning);
        };

        await _f.Apply(ChangeRequest.EditTags(path, Title("New")), ChangeRequest.Rename(path, "b.mp3"));

        Assert.NotEmpty(during);
        Assert.All(during, Assert.False);
        await _f.ScanAsync();
        Assert.Equal(2, Assert.Single(_f.Library.Items).Rating);
    }

    // ---- Crashes ------------------------------------------------------------------------------------

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var folder in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, folder)));
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            // The file being written is still open, so it is read with sharing.
            using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var target = File.Create(Path.Combine(to, Path.GetRelativePath(from, file)));
            source.CopyTo(target);
        }
    }

    private string Snapshot()
    {
        var copy = _f.Root + "-snapshot" + _snapshots.Count;
        CopyTree(_f.Root, copy);
        _snapshots.Add(copy);
        return copy;
    }

    // Puts the disk back as it was at that moment, as if the process had died there.
    private void RestoreDisk(string snapshot)
    {
        Directory.Delete(_f.Root, true);
        CopyTree(snapshot, _f.Root);
    }

    [Theory]
    [InlineData("started", true, "Old")] // journalled, nothing written
    [InlineData("written", true, "New")] // swapped in, not yet recorded
    [InlineData("replaced", true, "New")]
    [InlineData("relinking", true, "New")] // recorded, references not moved
    [InlineData("started", false, "Old")]
    [InlineData("halfwritten", false, "Old")] // the dangerous one: a broken file
    [InlineData("written", false, "New")]
    [InlineData("relinking", false, "New")]
    public async Task A_crash_during_a_tag_change_is_repaired_at_the_next_start(string at, bool atomic, string expected)
    {
        var path = _f.Make("a.mp3", "Old");
        var second = _f.Make("b.mp3", "Second");
        var bytes = File.ReadAllBytes(path);
        var local = new LocalFileBackend(HoldingFolder.At(_f.HeldFolder));
        var inPlace = atomic ? null : new InPlaceBackend(local);
        _f.Build(inPlace is null ? null : inPlace);
        await _f.ScanAsync();
        _f.Library.SetRating(path, 5);
        _f.Lyrics.SaveOverride(ChangeFixture.Info(path), Plain("words"));
        string? snapshot = null;
        void Crash() => snapshot ??= Snapshot();
        _f.Service.Checkpoint = step =>
        {
            if (step == at)
                Crash();
        };
        if (at == "halfwritten")
            inPlace!.HalfWritten = Crash;

        await _f.Apply(ChangeRequest.EditTags(path, Title("New")), ChangeRequest.EditTags(second, Title("Never")));
        Assert.NotNull(snapshot);
        RestoreDisk(snapshot);
        _f.Build(atomic ? null : new InPlaceBackend(local));

        _f.Service.Recover();
        await _f.ScanAsync();

        Assert.Equal(expected, TitleOf(path));
        if (expected == "Old")
            Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal("Second", TitleOf(second)); // not started, so not touched
        Assert.Equal(5, _f.Library.Find(path)!.Rating);
        Assert.NotNull(_f.Lyrics.Store!.GetOverride(ChangeFixture.Key(path)));
        Assert.Empty(_f.Leftovers());
        var batch = _f.Journal.Load(_f.Journal.Ids[0])!;
        Assert.True(batch is { Finished: true, Interrupted: true });
        // What did happen can still be undone.
        Assert.Equal(expected == "New", batch.CanUndo);
        if (batch.CanUndo)
        {
            await _f.Service.UndoAsync(batch.Id);
            Assert.Equal("Old", TitleOf(path));
        }

        // A second recovery finds nothing to do.
        Assert.Empty(_f.Service.Recover());
    }

    [Fact]
    public async Task A_half_written_file_that_may_not_be_written_at_start_waits_for_permission()
    {
        var path = _f.Make("a.mp3", "Old");
        var bytes = File.ReadAllBytes(path);
        var local = new LocalFileBackend(HoldingFolder.At(_f.HeldFolder));
        var first = new InPlaceBackend(local);
        _f.Build(first);
        await _f.ScanAsync();
        string? snapshot = null;
        first.HalfWritten = () => snapshot ??= Snapshot();
        await _f.Apply(ChangeRequest.EditTags(path, Title("New")));
        RestoreDisk(snapshot!);

        // As on Android after a restart: writing is refused until the user has been asked again.
        var later = new InPlaceBackend(local) { FailingWrites = 1 };
        _f.Build(later);
        var report = _f.Service.Recover();

        Assert.Single(report);
        Assert.Equal([path], _f.Service.PendingRepairs);
        Assert.NotEqual(bytes, File.ReadAllBytes(path));
        var blocked = await _f.Apply(ChangeRequest.EditTags(path, Title("Other")));
        Assert.Contains("interrupted", blocked.Results[0].Message);

        Assert.DoesNotContain(await _f.Service.RepairAsync(), m => m.Contains("could not"));

        Assert.Contains((FileAccessKind.Write, 1), later.Requests);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Empty(_f.Service.PendingRepairs);
        Assert.Empty(_f.Leftovers());
    }

    [Theory]
    [InlineData("started")]
    [InlineData("replaced")]
    [InlineData("relinking")]
    public async Task A_crash_during_a_rename_is_repaired_at_the_next_start(string at)
    {
        var path = _f.Make("a.mp3");
        File.WriteAllText(Path.ChangeExtension(path, ".lrc"), "[00:01.00]line");
        await _f.ScanAsync();
        _f.Library.SetRating(path, 5);
        _f.Playlists.Save("Mine", [path]);
        string? snapshot = null;
        _f.Service.Checkpoint = step =>
        {
            if (step == at)
                snapshot ??= Snapshot();
        };

        await _f.Apply(ChangeRequest.Rename(path, "b.mp3"));
        RestoreDisk(snapshot!);
        _f.Build();
        _f.Service.Recover();
        await _f.ScanAsync();

        var now = at == "started" ? path : Path.Combine(_f.Music, "b.mp3");
        Assert.True(File.Exists(now));
        Assert.True(File.Exists(Path.ChangeExtension(now, ".lrc")));
        Assert.Equal(5, Assert.Single(_f.Library.Items).Rating);
        Assert.Equal([now], _f.Playlists.Read("Mine"));
    }

    [Theory]
    [InlineData("started")]
    [InlineData("replaced")]
    [InlineData("relinking")]
    public async Task A_crash_during_a_removal_is_repaired_at_the_next_start(string at)
    {
        var path = _f.Make("a.mp3");
        var bytes = File.ReadAllBytes(path);
        await _f.ScanAsync();
        _f.Library.SetRating(path, 5);
        _f.Playlists.Save("Mine", [path]);
        string? snapshot = null;
        _f.Service.Checkpoint = step =>
        {
            if (step == at)
                snapshot ??= Snapshot();
        };

        await _f.Apply(_f.Service.Remove(path));
        RestoreDisk(snapshot!);
        _f.Build();
        _f.Service.Recover();

        if (at == "started")
        {
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Equal([path], _f.Playlists.Read("Mine"));
        }
        else
        {
            Assert.Empty(_f.Playlists.Read("Mine"));
            var batch = _f.Journal.Load(_f.Journal.Ids[0])!;
            ChangeFixture.AssertAll(ResultStatus.Done, await _f.Service.UndoAsync(batch.Id));
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Equal(5, _f.Library.Find(path)!.Rating);
        }
    }
}
