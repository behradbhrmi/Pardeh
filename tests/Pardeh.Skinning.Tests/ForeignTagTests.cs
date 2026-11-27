using System.Text;
using Pardeh.Core.Changes;
using Pardeh.Nava;
using static Pardeh.Skinning.Tests.LyricsTestData;

namespace Pardeh.Skinning.Tests;

/// <summary>
/// ID3v2 tags built byte by byte, as another program would write them. The frames are read back from the bytes, not through
/// TagLib#: it empties user web address frames (WXXX) on every save, and agrees with itself that nothing happened.
/// </summary>
[Collection("MediaSources statics")]
public sealed class ForeignTagTests : IDisposable
{
    private readonly string _folder = NewTempFolder("pardeh-foreign-");

    public ForeignTagTests() => Assert.SkipWhen(FfmpegTool.Tool is null, "ffmpeg is not in tools/ffmpeg (scripts/get-ffmpeg.ps1)");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static byte[] Latin(string text) => Encoding.Latin1.GetBytes(text);

    private static byte[] Wide(string text) => [0xFF, 0xFE, .. Encoding.Unicode.GetBytes(text)];

    private static byte[] Syncsafe(int size) => [(byte)(size >> 21 & 127), (byte)(size >> 14 & 127), (byte)(size >> 7 & 127), (byte)(size & 127)];

    private static byte[] Frame(string id, byte[] body, int version)
    {
        var n = body.Length;
        var size = version == 4 ? Syncsafe(n) : [(byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n];
        return [.. Latin(id), .. size, 0, 0, .. body];
    }

    private static List<(string Id, byte[] Body)> Foreign() =>
    [
        ("TIT2", [0, .. Latin("Old")]),
        ("TPE1", [1, .. Wide("خواننده")]),
        ("TALB", [0, .. Latin("Album"), 0]), // with a terminator, as some programs write it
        ("TXXX", [0, .. Latin("mood"), 0, .. Latin("calm")]),
        ("TXXX", [1, .. Wide("حال"), 0, 0, .. Wide("آرام")]),
        ("WXXX", [0, .. Latin("home"), 0, .. Latin("http://artist.example/home")]),
        ("WXXX", [1, .. Wide("صفحه"), 0, 0, .. Latin("http://artist.example/fa")]),
        ("WOAR", Latin("http://artist.example/artist")),
        ("WCOM", Latin("http://shop.example/buy")),
        ("COMM", [0, .. Latin("eng"), 0, .. Latin("the comment")]),
        ("COMM", [0, .. Latin("eng"), .. Latin("note"), 0, .. Latin("another comment")]),
        ("USLT", [0, .. Latin("eng"), 0, .. Latin("the words")]),
        ("GEOB", [0, .. Latin("text/plain"), 0, .. Latin("f.txt"), 0, .. Latin("thing"), 0, 1, 2, 3]),
        ("PRIV", [.. Latin("owner"), 0, 9, 8, 7]),
        ("POPM", [.. Latin("a@b.example"), 0, 196, 0, 0, 0, 5]),
        ("UFID", [.. Latin("http://ids.example"), 0, 1, 1, 2]),
        ("PCNT", [0, 0, 0, 42]),
        ("APIC", [0, .. Latin("image/jpeg"), 0, 3, .. Latin("front"), 0, 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 0xFF, 0xD9]),
        ("USER", [0, .. Latin("eng"), .. Latin("terms")]),
        ("XYZW", [1, 2, 3, 4]),
        ("TIPL", [0, .. Latin("producer"), 0, .. Latin("someone")]),
        ("TDRC", [0, .. Latin("1975-03-02")]),
        ("SYLT", [0, .. Latin("eng"), 2, 1, 0, .. Latin("la"), 0, 0, 0, 0, 9]),
        ("TKEY", [0, .. Latin("Am")]),
    ];

    private string Make(int version, List<(string Id, byte[] Body)> frames)
    {
        var plain = Path.Combine(_folder, "plain.mp3");
        FfmpegTool.MakeAudio(plain, "libmp3lame");
        var audio = File.ReadAllBytes(plain);
        var oldSize = (audio[6] << 21) | (audio[7] << 14) | (audio[8] << 7) | audio[9];
        audio = audio[(10 + oldSize)..];
        byte[] all = [.. frames.SelectMany(f => Frame(f.Id, f.Body, version))];
        var path = Path.Combine(_folder, $"v{version}.mp3");
        File.WriteAllBytes(path, [.. Latin("ID3"), (byte)version, 0, 0, .. Syncsafe(all.Length), .. all, .. audio]);
        return path;
    }

    private static List<RawFrame> Raw(string path)
    {
        using var stream = File.OpenRead(path);
        return RawId3.Read(stream)!;
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void A_title_change_keeps_every_other_frame_another_program_wrote(int version)
    {
        var frames = Foreign();
        var path = Make(version, frames);
        var before = Raw(path).Select(RawId3.Describe).Where(d => d.Key is not ("id3v2:TIT2" or "id3v2:TDRC")).ToList();
        Assert.Equal(frames.Count - 2, before.Count);

        new TagEditor().Edit(path, "a.mp3", new TagFields { Title = "New" });

        var after = Raw(path).Select(RawId3.Describe).ToList();
        Assert.All(before, frame => Assert.Contains(frame, after));
        // The date is the same date, with day and month in their places for the version of the tag.
        Assert.Equal("1975-03-02", RawId3.Date(Raw(path)));
        if (version == 3)
            Assert.Equal("0203", RawId3.Describe(Raw(path).Single(f => f.Id == "TDAT")).Content);
        Assert.Equal("New", new TagEditor().Read(path, "a.mp3").Title);
        // The address frames are the very same bytes.
        Assert.Equal(frames.Where(f => f.Id == "WXXX").Select(f => Convert.ToHexString(f.Body)).Order(),
            Raw(path).Where(f => f.Id == "WXXX").Select(f => Convert.ToHexString(f.Body)).Order());
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void An_address_can_be_read_removed_and_put_back(int version)
    {
        var path = Make(version, Foreign());
        var editor = new TagEditor();
        var read = editor.Read(path, "a.mp3");
        Assert.Equal("http://artist.example/home", read.TextOf("id3v2:WXXX:home"));
        Assert.Equal("http://artist.example/fa", read.TextOf("id3v2:WXXX:صفحه"));

        var edit = editor.Edit(path, "a.mp3", new TagFields { Texts = [new TagText("id3v2:WXXX:home", "")] });
        var cleared = editor.Read(path, "a.mp3");
        Assert.Equal("", cleared.TextOf("id3v2:WXXX:home"));
        Assert.Equal("http://artist.example/fa", cleared.TextOf("id3v2:WXXX:صفحه"));

        editor.Edit(path, "a.mp3", edit.Before, edit.TagKindsBefore);
        Assert.Equal("http://artist.example/home", editor.Read(path, "a.mp3").TextOf("id3v2:WXXX:home"));
    }

    [Fact]
    public void Clearing_the_comment_leaves_the_other_comment()
    {
        var path = Make(3, Foreign());

        new TagEditor().Edit(path, "a.mp3", new TagFields { Comment = "" });

        var comments = Raw(path).Where(f => f.Id == "COMM").Select(RawId3.Describe).ToList();
        Assert.Equal(["another comment"], comments.Select(c => c.Content));
    }
}
