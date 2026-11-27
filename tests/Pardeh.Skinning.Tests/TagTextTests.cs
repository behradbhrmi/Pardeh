using Pardeh.Core.Changes;
using Pardeh.Nava;
using static Pardeh.Skinning.Tests.LyricsTestData;

namespace Pardeh.Skinning.Tests;

/// <summary>The text fields without a property of their own: read, cleared one by one, and put back by an undo.</summary>
[Collection("MediaSources statics")]
public sealed class TagTextTests : IDisposable
{
    private const string Ad = "Download from www.site.example";
    private readonly string _folder = NewTempFolder("pardeh-tagtext-");

    public TagTextTests() => Assert.SkipWhen(FfmpegTool.Tool is null, "ffmpeg is not in tools/ffmpeg (scripts/get-ffmpeg.ps1)");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Make(string extension, string codec)
    {
        var path = Path.Combine(_folder, "t." + extension);
        FfmpegTool.MakeAudio(path, codec, new Dictionary<string, string> { ["title"] = "Song" });
        return path;
    }

    [Fact]
    public void Id3_frames_of_every_kind_are_read_removed_and_restored()
    {
        var path = Make("mp3", "libmp3lame");
        using (var file = TagLib.File.Create(path))
        {
            var id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, true);
            id3.SetTextFrame("TPUB", Ad);
            TagLib.Id3v2.UserTextInformationFrame.Get(id3, "website", true).Text = [Ad];
            TagLib.Id3v2.UserTextInformationFrame.Get(id3, "mood", true).Text = ["calm"];
            var url = TagLib.Id3v2.UrlLinkFrame.Get(id3, "WOAR", true);
            url.Text = ["http://site.example/artist"];
            file.Tag.Comment = "the real comment";
            id3.AddFrame(new TagLib.Id3v2.CommentsFrame("advert", "eng", TagLib.StringType.UTF8) { Text = Ad });
            id3.AddFrame(new TagLib.Id3v2.UnsynchronisedLyricsFrame("", "eng", TagLib.StringType.UTF8) { Text = "words www.site.example" });
            file.Save();
        }

        // TagLib# can't write this one, so the editor does.
        var editor = new TagEditor();
        editor.Edit(path, "t.mp3", new TagFields { Texts = [new TagText("id3v2:WXXX:home", "http://site.example/home")] });
        var before = editor.Read(path, "t.mp3");
        var ids = before.Texts!.Select(t => t.Id).ToList();
        string[] junk = ["id3v2:COMM:eng:advert", "id3v2:TPUB", "id3v2:TXXX:website", "id3v2:WOAR", "id3v2:WXXX:home"];
        Assert.All(junk, id => Assert.Contains(id, ids));
        Assert.DoesNotContain(ids, id => id.Contains("USLT"));
        var digest = AudioDigest.Compute(path);

        var edit = editor.Edit(path, "t.mp3", new TagFields { Texts = [.. junk.Select(id => new TagText(id, ""))] });

        var cleaned = editor.Read(path, "t.mp3");
        Assert.All(junk, id => Assert.Equal("", cleaned.TextOf(id)));
        Assert.Equal("calm", cleaned.TextOf("id3v2:TXXX:mood"));
        Assert.Equal("the real comment", cleaned.Comment);
        using (var file = TagLib.File.Create(path))
            Assert.Equal("words www.site.example", file.Tag.Lyrics);

        editor.Edit(path, "t.mp3", edit.Before, edit.TagKindsBefore);

        var back = editor.Read(path, "t.mp3");
        Assert.All(junk, id => Assert.Equal(before.TextOf(id), back.TextOf(id)));
        Assert.Equal(digest, AudioDigest.Compute(path));
    }

    [Fact]
    public void Clearing_the_comment_keeps_another_comment_the_file_has()
    {
        var path = Make("mp3", "libmp3lame");
        using (var file = TagLib.File.Create(path))
        {
            file.Tag.Comment = Ad;
            var id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, true);
            id3.AddFrame(new TagLib.Id3v2.CommentsFrame("mine", "eng", TagLib.StringType.UTF8) { Text = "keep me" });
            file.Save();
        }

        new TagEditor().Edit(path, "t.mp3", new TagFields { Comment = "" });

        using var after = TagLib.File.Create(path);
        var frames = ((TagLib.Id3v2.Tag)after.GetTag(TagLib.TagTypes.Id3v2)).GetFrames<TagLib.Id3v2.CommentsFrame>().ToList();
        Assert.Equal(["keep me"], frames.Select(f => f.Text));
    }

    [Theory]
    [InlineData("flac", "flac", "xiph:ORGANIZATION")]
    [InlineData("ogg", "libvorbis", "xiph:WEBSITE")]
    [InlineData("opus", "libopus", "xiph:CONTACT")]
    [InlineData("wma", "wmav2", "asf:WM/Publisher")]
    [InlineData("m4a", "aac", "apple:desc")]
    public void The_fields_of_other_formats_can_be_set_cleared_and_restored(string extension, string codec, string id)
    {
        var path = Make(extension, codec);
        var name = "t." + extension;
        var editor = new TagEditor();
        editor.Edit(path, name, new TagFields { Texts = [new TagText(id, Ad)] });
        Assert.Equal(Ad, editor.Read(path, name).TextOf(id));

        var edit = editor.Edit(path, name, new TagFields { Texts = [new TagText(id, "")] });
        Assert.Equal("", editor.Read(path, name).TextOf(id));
        Assert.Equal("Song", editor.Read(path, name).Title);

        editor.Edit(path, name, edit.Before, edit.TagKindsBefore);
        Assert.Equal(Ad, editor.Read(path, name).TextOf(id));
    }
}
