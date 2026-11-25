using Pardeh.Core.Playback;

namespace Pardeh.Skinning.Tests;

public sealed class SubtitleTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("pardeh-sub-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void Reads_srt_vtt_and_ass_and_finds_sidecars()
    {
        var media = Path.Combine(_folder, "movie.mp4");
        File.WriteAllText(media, "x");
        File.WriteAllText(Path.Combine(_folder, "movie.srt"), "1\r\n00:00:01,000 --> 00:00:03,500\r\n<i>Hello</i> there\r\n\r\n2\r\n00:00:04,000 --> 00:00:05,000\r\nSecond\r\nline\r\n");
        var srt = Subtitles.Load(Subtitles.FindFor(media)!)!;
        Assert.Equal("Hello there", srt.TextAt(TimeSpan.FromSeconds(2)));
        Assert.Equal("Second\nline", srt.TextAt(TimeSpan.FromSeconds(4.5)));
        Assert.Null(srt.TextAt(TimeSpan.FromSeconds(3.7)));

        var vtt = Path.Combine(_folder, "a.vtt");
        File.WriteAllText(vtt, "WEBVTT\n\n00:01.000 --> 00:02.000\nShort form\n");
        Assert.Equal("Short form", Subtitles.Load(vtt)!.TextAt(TimeSpan.FromSeconds(1.5)));

        var ass = Path.Combine(_folder, "a.ass");
        File.WriteAllText(ass, "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:01.00,0:00:02.00,Default,,0,0,0,," + @"{\i1}One, two\Nthree" + "\n");
        Assert.Equal("One, two\nthree", Subtitles.Load(ass)!.TextAt(TimeSpan.FromSeconds(1.5)));
    }
}
