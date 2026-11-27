using Pardeh.Core.Changes;
using Pardeh.Nava;
using static Pardeh.Skinning.Tests.LyricsTestData;

namespace Pardeh.Skinning.Tests;

/// <summary>Tag writing on real files of every format, made at test time.</summary>
[Collection("MediaSources statics")]
public sealed class TagEditorTests : IDisposable
{
    private readonly string _folder = NewTempFolder("pardeh-tagedit-");

    public TagEditorTests() => Assert.SkipWhen(FfmpegTool.Tool is null, "ffmpeg is not in tools/ffmpeg (scripts/get-ffmpeg.ps1)");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Make(string extension, string codec, params (string, string)[] tags)
    {
        var path = Path.Combine(_folder, "t." + extension);
        FfmpegTool.MakeAudio(path, codec, tags.ToDictionary(t => t.Item1, t => t.Item2));
        return path;
    }

    private TagPicture Cover(int seed) =>
        new("FrontCover", "image/jpeg", "", FfmpegTool.MakeJpeg(Path.Combine(_folder, $"cover{seed}.jpg"), seed));

    public static TheoryData<string, string> Formats => new()
    {
        { "mp3", "libmp3lame" }, { "m4a", "aac" }, { "flac", "flac" }, { "ogg", "libvorbis" }, { "opus", "libopus" },
        { "wma", "wmav2" }, { "wav", "pcm_s16le" }, { "aiff", "pcm_s16be" }, { "aac", "aac" },
    };

    [Theory]
    [MemberData(nameof(Formats))]
    public void Writes_and_takes_back_every_field_without_touching_the_sound(string extension, string codec)
    {
        var path = Make(extension, codec, ("title", "Old title"), ("artist", "Old artist"), ("album", "Old album"));
        var name = Path.GetFileName(path);
        var editor = new TagEditor();
        var digest = AudioDigest.Compute(path);
        Assert.NotNull(digest);
        var original = editor.Read(path, name);
        var change = new TagFields
        {
            Title = "ترانه‌ی تازه with a title that is longer than the thirty characters of ID3v1",
            Artists = ["خواننده"],
            Album = "New album",
            Genres = ["Pop"],
            Year = 1975,
            Track = 3,
            Pictures = [Cover(1)],
        };

        var edit = editor.Edit(path, name, change);

        var now = editor.Read(path, name);
        Assert.Equal(change.Title, now.Title);
        Assert.Equal(change.Artists, now.Artists);
        Assert.Equal(change.Album, now.Album);
        Assert.Equal(1975u, now.Year);
        Assert.Equal(change.Pictures[0].Data, Assert.Single(now.Pictures!).Data);
        Assert.Equal(digest, AudioDigest.Compute(path));

        editor.Edit(path, name, edit.Before, edit.TagKindsBefore);

        var back = editor.Read(path, name);
        Assert.True(back.Differences(original with { Pictures = null }).IsEmpty, back.Differences(original with { Pictures = null }).ToString());
        Assert.Empty(back.Pictures!);
        Assert.Equal(digest, AudioDigest.Compute(path));
    }

    [Fact]
    public void An_mp3_gains_no_tag_kind_it_did_not_have()
    {
        var path = Make("mp3", "libmp3lame", ("title", "Old"));
        new TagEditor().Edit(path, "t.mp3", new TagFields { Title = "عنوان" });

        using var file = TagLib.File.Create(path);
        Assert.Equal(TagLib.TagTypes.Id3v2, file.TagTypesOnDisk);
    }

    [Fact]
    public void A_wav_gains_only_the_id3_tag()
    {
        var path = Make("wav", "pcm_s16le", ("title", "Old"), ("comment", "keep"));
        new TagEditor().Edit(path, "t.wav", new TagFields { Title = "New" });

        using var file = TagLib.File.Create(path);
        Assert.Equal(TagLib.TagTypes.None, file.TagTypesOnDisk & (TagLib.TagTypes.MovieId | TagLib.TagTypes.DivX));
        Assert.Equal("keep", file.Tag.Comment);
    }

    [Fact]
    public void An_mp3_with_only_id3v1_can_take_a_long_persian_title()
    {
        var path = Make("mp3", "libmp3lame");
        using (var file = TagLib.File.Create(path))
        {
            file.RemoveTags(TagLib.TagTypes.AllTags);
            var old = file.GetTag(TagLib.TagTypes.Id3v1, true);
            old.Title = "Old";
            old.Album = "Kept album";
            file.RemoveTags(TagLib.TagTypes.Id3v2);
            file.Save();
        }

        const string title = "یک عنوان بسیار بلند فارسی که در برچسب قدیمی جا نمی‌شود";
        new TagEditor().Edit(path, "t.mp3", new TagFields { Title = title });

        var now = new TagEditor().Read(path, "t.mp3");
        Assert.Equal(title, now.Title);
        Assert.Equal("Kept album", now.Album);
    }

    [Fact]
    public void Data_the_change_does_not_name_is_kept_frame_by_frame()
    {
        var path = Make("mp3", "libmp3lame", ("title", "Old"), ("custom_note", "mine"));
        var cover = Cover(5).Data;
        using (var file = TagLib.File.Create(path))
        {
            var id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, true);
            id3.AddFrame(new TagLib.Id3v2.UnsynchronisedLyricsFrame("", "eng", TagLib.StringType.UTF8) { Text = "the words" });
            var rating = TagLib.Id3v2.PopularimeterFrame.Get(id3, "someone@example.com", true);
            rating.Rating = 196;
            var secret = TagLib.Id3v2.PrivateFrame.Get(id3, "owner", true);
            secret.PrivateData = new TagLib.ByteVector(new byte[] { 1, 2, 3 });
            file.Tag.Disc = 2;
            file.Tag.Pictures = [new TagLib.Picture(new TagLib.ByteVector(cover)) { Type = TagLib.PictureType.FrontCover, MimeType = "image/jpeg" }];
            file.Save();
        }

        new TagEditor().Edit(path, "t.mp3", new TagFields { Title = "New", Year = 2001 });

        using var after = TagLib.File.Create(path);
        var tag = (TagLib.Id3v2.Tag)after.GetTag(TagLib.TagTypes.Id3v2);
        Assert.Equal("the words", after.Tag.Lyrics);
        Assert.Equal(196, TagLib.Id3v2.PopularimeterFrame.Get(tag, "someone@example.com", false)!.Rating);
        Assert.Equal(new byte[] { 1, 2, 3 }, TagLib.Id3v2.PrivateFrame.Get(tag, "owner", false)!.PrivateData.Data);
        Assert.Contains(tag.GetFrames<TagLib.Id3v2.UserTextInformationFrame>(), f => f.Description == "custom_note" && f.Text.Contains("mine"));
        Assert.Equal(2u, after.Tag.Disc);
        Assert.Equal(cover, Assert.Single(after.Tag.Pictures).Data.Data);
    }

    [Fact]
    public void Vorbis_comments_the_change_does_not_name_are_kept()
    {
        var path = Make("flac", "flac", ("title", "Old"), ("mood", "calm"), ("lyrics", "the words"), ("composer", "C"));
        new TagEditor().Edit(path, "t.flac", new TagFields { Title = "New" });

        using var after = TagLib.File.Create(path);
        var xiph = (TagLib.Ogg.XiphComment)after.GetTag(TagLib.TagTypes.Xiph);
        Assert.Equal("calm", xiph.GetFirstField("MOOD"));
        Assert.Equal("the words", after.Tag.Lyrics);
        Assert.Equal("C", after.Tag.FirstComposer);
    }

    [Fact]
    public void Nothing_is_written_when_nothing_differs()
    {
        var path = Make("mp3", "libmp3lame", ("title", "Same"));
        var bytes = File.ReadAllBytes(path);

        var edit = new TagEditor().Edit(path, "t.mp3", new TagFields { Title = "Same", Genres = [], Year = 0 });

        Assert.False(edit.Written);
        Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [Fact]
    public void A_write_that_changes_the_sound_is_refused()
    {
        var path = Make("mp3", "libmp3lame", ("title", "Old"));
        var calls = 0;
        var editor = new TagEditor { Digest = _ => "digest " + calls++ };

        var error = Assert.Throws<TagEditException>(() => editor.Edit(path, "t.mp3", new TagFields { Title = "New" }));
        Assert.Contains("sound", error.Message);
    }

    [Fact]
    public void A_file_that_is_not_audio_is_refused()
    {
        var path = Path.Combine(_folder, "broken.flac");
        File.WriteAllText(path, "this is not a flac file at all");

        Assert.Throws<TagEditException>(() => new TagEditor().Edit(path, "broken.flac", new TagFields { Title = "New" }));
    }
}
