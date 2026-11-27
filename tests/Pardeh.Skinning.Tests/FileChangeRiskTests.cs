using Pardeh.Core.Changes;
using Pardeh.Core.LyricsKit;
using static Pardeh.Skinning.Tests.LyricsTestData;

namespace Pardeh.Skinning.Tests;

/// <summary>
/// What breaks when a file's details or name change behind the app's back (these all failed when a file was simply edited or
/// renamed), and that a change made through the change service keeps each of them.
/// </summary>
[Collection("MediaSources statics")]
public sealed class FileChangeRiskTests : IDisposable
{
    private readonly ChangeFixture _f = new();

    public void Dispose() => _f.Dispose();

    private void SaveAllLyricsData(string path)
    {
        var item = ChangeFixture.Info(path);
        _f.Lyrics.SaveOverride(item, Plain("my own words"));
        _f.Lyrics.SaveChordsText(item, "[Am]my [C]chords");
        _f.Lyrics.SetOffset(item, 700);
        _f.Lyrics.SetTranspose(item, 2);
    }

    private void AssertAllLyricsData(string path)
    {
        var store = _f.Lyrics.Store!;
        var key = ChangeFixture.Key(path);
        Assert.NotNull(store.GetOverride(key));
        Assert.Equal("[Am]my [C]chords", store.GetChordsText(key));
        Assert.Equal(700, store.GetOffset(key));
        Assert.Equal(2, store.GetTranspose(key));
    }

    [Theory]
    [InlineData("title")]
    [InlineData("artist")]
    [InlineData("album")]
    public async Task Saved_lyrics_chords_offset_and_transpose_survive_an_edit(string field)
    {
        var path = _f.Make("a.mp3", "Song | site.example");
        await _f.ScanAsync();
        SaveAllLyricsData(path);
        var oldKey = ChangeFixture.Key(path);

        var change = field switch
        {
            "title" => new TagFields { Title = "Song" },
            "artist" => new TagFields { Artists = ["Someone Else"] },
            _ => new TagFields { Album = "Another Album" },
        };
        ChangeFixture.AssertAll(ResultStatus.Done, await _f.Apply(ChangeRequest.EditTags(path, change)));

        Assert.NotEqual(oldKey.Hash, ChangeFixture.Key(path).Hash);
        AssertAllLyricsData(path);
        Assert.Equal(LyricsPieces.None, _f.Lyrics.Store!.PiecesOf(oldKey));
    }

    [Fact]
    public async Task Rating_and_play_history_survive_a_rename()
    {
        var path = _f.Make("old name.mp3");
        await _f.ScanAsync();
        _f.Library.SetRating(path, 4);
        _f.Library.RecordPlay(path);

        var result = await _f.Apply(ChangeRequest.Rename(path, "new name.mp3"));
        await _f.ScanAsync();

        var item = Assert.Single(_f.Library.Items);
        Assert.Equal(result.Results[0].NewSource, item.Path);
        Assert.Equal(4, item.Rating);
        Assert.Equal(1, item.PlayCount);
    }

    [Fact]
    public async Task Playlist_entries_still_point_at_the_file_after_a_rename()
    {
        var path = _f.Make("old name.mp3");
        await _f.ScanAsync();
        _f.Playlists.Save("Mine", [path]);

        var result = await _f.Apply(ChangeRequest.Rename(path, "new name.mp3"));

        Assert.Equal([result.Results[0].NewSource!], _f.Playlists.Read("Mine"));
    }

    [Fact]
    public async Task Playlist_entries_do_not_dangle_after_a_removal()
    {
        var path = _f.Make("gone.mp3");
        var other = _f.Make("stays.mp3", "Other");
        await _f.ScanAsync();
        _f.Playlists.Save("Mine", [other, path, other]);

        ChangeFixture.AssertAll(ResultStatus.Done, await _f.Apply(_f.Service.Remove(path)));

        Assert.Equal([other, other], _f.Playlists.Read("Mine"));
    }

    [Fact]
    public async Task A_hidden_file_stays_hidden_after_a_rename()
    {
        var path = _f.Make("hidden.mp3");
        await _f.ScanAsync();
        _f.Library.Remove([path]);

        ChangeFixture.AssertAll(ResultStatus.Done, await _f.Apply(ChangeRequest.Rename(path, "hidden renamed.mp3")));
        await _f.ScanAsync();

        Assert.Empty(_f.Library.Items);
    }

    [Fact]
    public async Task A_single_added_file_stays_in_the_library_after_a_rename()
    {
        var outside = Directory.CreateDirectory(Path.Combine(_f.Root, "elsewhere")).FullName;
        var path = Path.Combine(outside, "single.mp3");
        FfmpegTool.MakeAudio(path, "libmp3lame");
        _f.Library.AddFiles([path]);
        await _f.ScanAsync();
        Assert.Single(_f.Library.Items);

        ChangeFixture.AssertAll(ResultStatus.Done, await _f.Apply(ChangeRequest.Rename(path, "single renamed.mp3")));
        await _f.ScanAsync();

        Assert.Single(_f.Library.Items);
        // And the saved list says so after a restart.
        _f.Build();
        await _f.ScanAsync();
        Assert.Single(_f.Library.Items);
    }

    [Fact]
    public async Task A_favourite_bookmarks_and_a_video_position_survive_a_rename()
    {
        var path = _f.Make("fav.mp3");
        await _f.ScanAsync();
        _f.Favorites.Set(path, true, "Song", "Artist");
        _f.Bookmarks.Add(path, 12, "chorus");
        _f.VideoPositions.Set(path, 42);

        var renamed = (await _f.Apply(ChangeRequest.Rename(path, "fav renamed.mp3"))).Results[0].NewSource!;

        Assert.True(_f.Favorites.IsFavorite(renamed));
        Assert.False(_f.Favorites.IsFavorite(path));
        Assert.Equal("chorus", Assert.Single(_f.Bookmarks.GetMarkers(renamed)).Name);
        Assert.Equal(42, _f.VideoPositions.Get(renamed));
    }

    [Fact]
    public async Task Side_files_are_renamed_with_their_track()
    {
        var path = _f.Make("side.mp3");
        File.WriteAllText(Path.ChangeExtension(path, ".lrc"), "[00:01.00]line");
        File.WriteAllText(Path.ChangeExtension(path, ".cho"), "[Am]line");
        File.WriteAllText(Path.Combine(_f.Music, "side.en.srt"), "1\n00:00:01,000 --> 00:00:02,000\nhi\n");
        File.WriteAllText(Path.Combine(_f.Music, "side notes.txt"), "not a side file");
        await _f.ScanAsync();

        ChangeFixture.AssertAll(ResultStatus.Done, await _f.Apply(ChangeRequest.Rename(path, "renamed.mp3")));

        Assert.Equal(["renamed.cho", "renamed.en.srt", "renamed.lrc", "renamed.mp3", "side notes.txt"],
            Directory.GetFiles(_f.Music).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.NotNull(new SidecarLyricsSource().Read(ChangeFixture.Info(Path.Combine(_f.Music, "renamed.mp3")), CancellationToken.None));
    }

    [Fact]
    public async Task A_side_file_that_two_tracks_share_is_copied_not_taken()
    {
        var mp3 = _f.Make("both.mp3");
        _f.Make("both.flac", codec: "flac");
        File.WriteAllText(Path.Combine(_f.Music, "both.lrc"), "[00:01.00]line");
        await _f.ScanAsync();

        var result = await _f.Apply(ChangeRequest.Rename(mp3, "mine.mp3"));
        Assert.True(File.Exists(Path.Combine(_f.Music, "both.lrc")));
        Assert.True(File.Exists(Path.Combine(_f.Music, "mine.lrc")));

        // Undo takes the copy away again.
        ChangeFixture.AssertAll(ResultStatus.Done, await _f.Service.UndoAsync(result.BatchId!));
        Assert.Equal(["both.flac", "both.lrc", "both.mp3"], Directory.GetFiles(_f.Music).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Lyrics_of_a_file_without_a_title_tag_survive_a_rename()
    {
        var path = _f.Make("untitled.mp3", title: "");
        await _f.ScanAsync();
        SaveAllLyricsData(path);

        var renamed = (await _f.Apply(ChangeRequest.Rename(path, "renamed.mp3"))).Results[0].NewSource!;

        AssertAllLyricsData(renamed);
    }

    [Fact]
    public async Task Delete_from_Library_leaves_the_file_on_disk()
    {
        var path = _f.Make("keep.mp3");
        await _f.ScanAsync();
        var bytes = File.ReadAllBytes(path);

        _f.Library.Remove([path]);
        await _f.ScanAsync();

        Assert.Empty(_f.Library.Items);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Null(_f.Service.LastUndoable());
    }
}
