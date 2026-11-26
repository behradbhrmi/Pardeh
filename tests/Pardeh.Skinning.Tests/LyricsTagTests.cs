using Pardeh.Core.Library;
using Pardeh.Core.LyricsKit;
using Pardeh.Core.Playback;
using Pardeh.Nava;
using TagLib.Id3v2;
using static Pardeh.Skinning.Tests.LyricsTestData;

namespace Pardeh.Skinning.Tests;

/// <summary>
/// Embedded lyrics read from real audio files made at test time with the LGPL ffmpeg of tools/ffmpeg (skipped when it is not there).
/// ffmpeg writes Vorbis comments, MP4 ©lyr and (for mp3) a TXXX user text frame itself; the frames it cannot write (ID3 USLT and SYLT,
/// ASF WM/Lyrics, APEv2 Lyrics) are added with TagLib# afterwards.
/// </summary>
[Collection("MediaSources statics")]
public sealed class LyricsTagTests : IDisposable
{
    private const string Plain = "First line\nSecond line\nThird line";
    private const string Lrc = "[00:01.00]First line\n[00:02.50]Second line\n[00:04.00]Third line";
    private const string Persian = "[00:01.00]سلام دنیا\n[00:05.00]این یک آزمایش است";

    private readonly string _folder = NewTempFolder("pardeh-lyr-tags-");

    public LyricsTagTests() => Assert.SkipWhen(FfmpegTool.Tool is null, "ffmpeg is not in tools/ffmpeg (scripts/get-ffmpeg.ps1)");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Make(string name, string codec, params (string Key, string Value)[] tags)
    {
        var path = Path.Combine(_folder, name);
        FfmpegTool.MakeAudio(path, codec, tags.ToDictionary(t => t.Key, t => t.Value));
        return path;
    }

    /// <summary>An mp3 with a real ID3 USLT frame (and the other tags given).</summary>
    private string MakeUslt(string name, string text, params (string Key, string Value)[] tags)
    {
        var path = Make(name, "libmp3lame", tags);
        using var file = TagLib.File.Create(path);
        var id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, true);
        id3.AddFrame(new UnsynchronisedLyricsFrame("", "eng", TagLib.StringType.UTF8) { Text = text });
        file.Save();
        return path;
    }

    private static Lyrics? Read(string path) => new TagLyricsSource().Read(new MediaItem(path), CancellationToken.None);

    private static void AssertPlain(Lyrics? lyrics)
    {
        Assert.NotNull(lyrics);
        Assert.False(lyrics.Synced);
        Assert.Equal(LyricsSources.Embedded, lyrics.Source);
        Assert.Equal(["First line", "Second line", "Third line"], lyrics.Lines.Select(l => l.Text));
    }

    private static void AssertSynced(Lyrics? lyrics)
    {
        Assert.NotNull(lyrics);
        Assert.True(lyrics.Synced);
        Assert.Equal(["First line", "Second line", "Third line"], lyrics.Lines.Select(l => l.Text));
        Assert.Equal(TimeSpan.FromMilliseconds(2500), lyrics.Lines[1].Time);
    }

    [Theory]
    [InlineData("mp3", "libmp3lame")] // ID3v2 TXXX as ffmpeg writes it
    [InlineData("m4a", "aac")] // MP4 ©lyr
    [InlineData("flac", "flac")] // Vorbis comment LYRICS
    [InlineData("ogg", "libvorbis")]
    [InlineData("opus", "libopus")]
    public void Reads_plain_lyrics_from_the_tags_of_each_format(string extension, string codec)
    {
        AssertPlain(Read(Make("plain." + extension, codec, ("title", "T"), ("lyrics", Plain))));
    }

    [Theory]
    [InlineData("mp3", "libmp3lame")]
    [InlineData("m4a", "aac")]
    [InlineData("flac", "flac")]
    [InlineData("ogg", "libvorbis")]
    [InlineData("opus", "libopus")]
    public void Lrc_text_inside_a_lyrics_tag_counts_as_synced(string extension, string codec)
    {
        AssertSynced(Read(Make("lrc." + extension, codec, ("lyrics", Lrc))));
    }

    [Theory]
    [InlineData("mp3", "libmp3lame")]
    [InlineData("flac", "flac")]
    [InlineData("m4a", "aac")]
    public void Reads_persian_lyrics_from_the_tags(string extension, string codec)
    {
        var lyrics = Read(Make("fa." + extension, codec, ("lyrics", Persian)))!;
        Assert.True(lyrics.Synced);
        Assert.Equal(["سلام دنیا", "این یک آزمایش است"], lyrics.Lines.Select(l => l.Text));
        Assert.Equal(LyricsTextDirection.RightToLeft, lyrics.Lines[0].Direction);
    }

    [Fact]
    public void Reads_a_real_id3_uslt_frame_plain_lrc_and_persian()
    {
        AssertPlain(Read(MakeUslt("uslt.mp3", Plain)));
        AssertSynced(Read(MakeUslt("uslt-lrc.mp3", Lrc)));
        var persian = Read(MakeUslt("uslt-fa.mp3", Persian))!;
        Assert.Equal(["سلام دنیا", "این یک آزمایش است"], persian.Lines.Select(l => l.Text));
    }

    [Fact]
    public void Reads_the_unsyncedlyrics_and_syncedlyrics_fields_and_prefers_synced()
    {
        AssertPlain(Read(Make("u.flac", "flac", ("unsyncedlyrics", Plain))));
        AssertSynced(Read(Make("s.flac", "flac", ("syncedlyrics", Lrc), ("unsyncedlyrics", "plain only"))));
        AssertSynced(Read(Make("s.opus", "libopus", ("syncedlyrics", Lrc), ("lyrics", "plain only"))));
        // The same names as ID3 user text frames (foobar2000 and others write them).
        AssertPlain(Read(Make("u.mp3", "libmp3lame", ("unsyncedlyrics", Plain))));
        AssertSynced(Read(Make("s.mp3", "libmp3lame", ("syncedlyrics", Lrc), ("unsyncedlyrics", "plain only"))));
    }

    [Fact]
    public void Reads_an_id3_sylt_frame_and_prefers_it_over_uslt()
    {
        var path = MakeUslt("sylt.mp3", "plain uslt text");
        using (var file = TagLib.File.Create(path))
        {
            var id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, true);
            id3.AddFrame(new SynchronisedLyricsFrame("lyrics", "eng", SynchedTextType.Lyrics)
            {
                Format = TimestampFormat.AbsoluteMilliseconds,
                Text = [new SynchedText(1000, "First line"), new SynchedText(2500, "\nSecond line"), new SynchedText(4000, "Third line")],
            });
            file.Save();
        }

        AssertSynced(Read(path));
    }

    [Fact]
    public void A_sylt_frame_counted_in_mpeg_frames_is_ignored_in_favour_of_the_text()
    {
        var path = MakeUslt("frames.mp3", Plain);
        using (var file = TagLib.File.Create(path))
        {
            var id3 = (TagLib.Id3v2.Tag)file.GetTag(TagLib.TagTypes.Id3v2, true);
            id3.AddFrame(new SynchronisedLyricsFrame("l", "eng", SynchedTextType.Lyrics)
            {
                Format = TimestampFormat.AbsoluteMpegFrames,
                Text = [new SynchedText(10, "frames")],
            });
            file.Save();
        }

        AssertPlain(Read(path));
    }

    [Fact]
    public void Reads_wma_asf_lyrics()
    {
        var path = Make("a.wma", "wmav2", ("title", "T"));
        using (var file = TagLib.File.Create(path))
        {
            file.Tag.Lyrics = Lrc;
            file.Save();
        }

        AssertSynced(Read(path));
    }

    [Fact]
    public void Reads_apev2_lyrics()
    {
        var path = Make("ape.mp3", "libmp3lame", ("title", "T"));
        using (var file = TagLib.File.Create(path))
        {
            var ape = file.GetTag(TagLib.TagTypes.Ape, true);
            ape.Lyrics = Plain;
            file.Save();
        }

        AssertPlain(Read(path));
    }

    [Fact]
    public void Files_without_lyrics_or_with_other_tags_give_nothing()
    {
        Assert.Null(Read(Make("none.mp3", "libmp3lame", ("title", "T"), ("artist", "A"), ("comment", "hello"))));
        Assert.Null(Read(Make("none.flac", "flac", ("title", "T"))));
        Assert.Null(Read(Make("none.m4a", "aac", ("title", "T"))));
        Assert.Null(Read(Make("blank.flac", "flac", ("lyrics", "  "))));
    }

    [Fact]
    public void Missing_damaged_and_unsupported_files_give_nothing_and_never_throw()
    {
        Assert.Null(Read(Path.Combine(_folder, "missing.mp3")));
        var junk = Path.Combine(_folder, "junk.mp3");
        File.WriteAllBytes(junk, new byte[5000]);
        Assert.Null(Read(junk));
        var text = Path.Combine(_folder, "notaudio.flac");
        File.WriteAllText(text, "this is not a flac file");
        Assert.Null(Read(text));
        var truncated = MakeUslt("cut.mp3", Plain);
        File.WriteAllBytes(truncated, File.ReadAllBytes(truncated)[..40]);
        _ = Read(truncated); // a damaged tag: nothing or the text, but no exception
        Assert.Null(Read(Path.Combine(_folder, "x.unknownext")));
    }

    [Fact]
    public void Reads_through_the_content_stream_hooks_like_an_android_content_uri()
    {
        var path = Make("stream.flac", "flac", ("lyrics", Lrc));
        var oldOpener = MediaSources.ContentOpener;
        var oldName = MediaSources.NameResolver;
        try
        {
            MediaSources.ContentOpener = uri => uri == "content://docs/stream" ? new FileStream(path, FileMode.Open, FileAccess.Read) : null;
            MediaSources.NameResolver = _ => "stream.flac";
            AssertSynced(new TagLyricsSource().Read(new MediaItem("content://docs/stream"), CancellationToken.None));
            Assert.Null(new TagLyricsSource().Read(new MediaItem("content://docs/other"), CancellationToken.None));
        }
        finally
        {
            MediaSources.ContentOpener = oldOpener;
            MediaSources.NameResolver = oldName;
        }
    }

    [Fact]
    public async Task The_service_prefers_synced_text_from_a_sidecar_over_plain_embedded_lyrics()
    {
        var path = MakeUslt("both.mp3", Plain, ("title", "T"), ("artist", "A"));
        File.WriteAllText(Path.ChangeExtension(path, ".lrc"), Lrc);
        var service = new LyricsService(null, [new TagLyricsSource(), new SidecarLyricsSource()]);

        var lyrics = (await service.GetAsync(new TagMediaInfoReader().Read(path), TestContext.Current.CancellationToken))!;

        Assert.True(lyrics.Synced);
        Assert.Equal(LyricsSources.Sidecar, lyrics.Source);
    }

    [Fact]
    public async Task The_service_uses_the_embedded_lyrics_of_a_real_file_with_its_tags_as_the_key()
    {
        var path = Make("one.flac", "flac", ("title", "Song"), ("artist", "Artist"), ("album", "Album"), ("lyrics", Lrc));
        var item = new TagMediaInfoReader().Read(path);
        Assert.Equal("Artist", item.GetAttribute("Author"));
        Assert.Equal(1, item.Duration.TotalSeconds, 0.2);

        var lyrics = await new LyricsService(null, [new TagLyricsSource()]).GetAsync(item, TestContext.Current.CancellationToken);
        AssertSynced(lyrics);
    }
}
