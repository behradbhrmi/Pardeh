using System.Text;
using Pardeh.Core.Library;
using Pardeh.Core.LyricsKit;
using Pardeh.Core.Playback;
using Pardeh.Nava;
using static Pardeh.Skinning.Tests.LyricsTestData;

namespace Pardeh.Skinning.Tests;

/// <summary>Encodings, files next to the audio and the content-URI hooks.</summary>
[Collection("MediaSources statics")]
public sealed class LyricsFileTests : IDisposable
{
    private const string PersianLrc = "[ti:سلام]\r\n[00:01.00]سلام دنیا\r\n[00:05.50]این یک آزمایش است\r\n[00:09.00]کتاب و یاد\r\n";
    private readonly string _folder = NewTempFolder("pardeh-lyr-files-");

    static LyricsFileTests() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string Write(string name, string text, Encoding? encoding = null) => Write(name, (encoding ?? new UTF8Encoding(false)).GetBytes(text));

    private MediaItem Song(string name = "song.mp3") => new(Write(name, "audio"));

    // --- encodings ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Decodes_utf8_with_and_without_a_bom()
    {
        Assert.Equal(PersianLrc, LyricsDecoder.Decode(new UTF8Encoding(false).GetBytes(PersianLrc)));
        Assert.Equal(PersianLrc, LyricsDecoder.Decode([.. Encoding.UTF8.GetPreamble(), .. new UTF8Encoding(false).GetBytes(PersianLrc)]));
    }

    [Fact]
    public void Decodes_utf16_with_a_bom_in_both_byte_orders()
    {
        Assert.Equal(PersianLrc, LyricsDecoder.Decode([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(PersianLrc)]));
        Assert.Equal(PersianLrc, LyricsDecoder.Decode([.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(PersianLrc)]));
    }

    [Fact]
    public void Decodes_latin_utf16_without_a_bom()
    {
        const string Text = "[00:01.00]Hello world\r\n[00:02.00]Second";
        Assert.Equal(Text, LyricsDecoder.Decode(Encoding.Unicode.GetBytes(Text)));
        Assert.Equal(Text, LyricsDecoder.Decode(Encoding.BigEndianUnicode.GetBytes(Text)));
    }

    [Fact]
    public void Decodes_windows_1256_persian()
    {
        var bytes = Encoding.GetEncoding(1256).GetBytes(PersianLrc);
        Assert.Contains(bytes, b => b >= 0x80);
        // Windows-1256 has no Persian yeh and keheh: the file holds the Arabic letters, and so does the decoded text.
        Assert.Equal(Through1256(PersianLrc), LyricsDecoder.Decode(bytes));
        Assert.Contains("سلام دنيا", LyricsDecoder.Decode(bytes));
    }

    private static string Through1256(string text) => Encoding.GetEncoding(1256).GetString(Encoding.GetEncoding(1256).GetBytes(text));

    [Fact]
    public void Decodes_windows_1256_arabic_with_latin_words_mixed_in()
    {
        const string Text = "[00:01.00]مرحبا بالعالم\r\n[00:02.00]Hello (live)\r\n[00:03.00]أغنية جميلة جدا\r\n";
        Assert.Equal(Text, LyricsDecoder.Decode(Encoding.GetEncoding(1256).GetBytes(Text)));
    }

    [Fact]
    public void Decodes_windows_1252_western_european()
    {
        const string Text = "[00:01.00]Café déjà vu\r\n[00:02.00]Ça va très bien, señor — “quoted”\r\n[00:03.00]Größe über naïve façade\r\n";
        Assert.Equal(Text, LyricsDecoder.Decode(Encoding.GetEncoding(1252).GetBytes(Text)));
    }

    [Fact]
    public void Plain_ascii_and_nothing_decode_as_they_are()
    {
        Assert.Equal("[00:01.00]Hello", LyricsDecoder.Decode("[00:01.00]Hello"u8));
        Assert.Equal(string.Empty, LyricsDecoder.Decode([]));
    }

    [Fact]
    public void Random_bytes_never_throw()
    {
        var random = new Random(7);
        for (var i = 0; i < 50; i++)
        {
            var bytes = new byte[random.Next(1, 400)];
            random.NextBytes(bytes);
            _ = LyricsDecoder.Decode(bytes);
        }
    }

    // --- files next to the audio -------------------------------------------------------------------------------------------

    [Fact]
    public void Reads_an_lrc_file_with_the_same_name()
    {
        var item = Song();
        Write("song.lrc", "[ar:Someone]\n[00:01.00]one\n[00:02.00]two");
        var lyrics = new SidecarLyricsSource().Read(item, CancellationToken.None)!;
        Assert.True(lyrics.Synced);
        Assert.Equal(LyricsSources.Sidecar, lyrics.Source);
        Assert.Equal(["one", "two"], lyrics.Lines.Select(l => l.Text));
        Assert.Equal("Someone", lyrics.Artist);
    }

    [Fact]
    public void An_lrc_file_is_preferred_over_a_txt_file()
    {
        var item = Song();
        Write("song.txt", "plain text");
        Write("song.lrc", "[00:01.00]synced");
        Assert.Equal("synced", new SidecarLyricsSource().Read(item, CancellationToken.None)!.Lines[0].Text);
    }

    [Fact]
    public void A_txt_file_is_plain_lyrics_and_an_lrc_inside_a_txt_is_synced()
    {
        var item = Song();
        Write("song.txt", "first\nsecond");
        var plain = new SidecarLyricsSource().Read(item, CancellationToken.None)!;
        Assert.False(plain.Synced);
        Assert.Equal(["first", "second"], plain.Lines.Select(l => l.Text));

        Write("song.txt", "[00:01.00]first\n[00:02.00]second");
        Assert.True(new SidecarLyricsSource().Read(item, CancellationToken.None)!.Synced);
    }

    [Fact]
    public void The_name_match_ignores_case_and_ignores_other_names()
    {
        var item = Song("Song One.mp3");
        Write("Song One 2.lrc", "[00:01.00]wrong");
        Write("Song One.mp3.lrc", "[00:01.00]wrong too");
        Write("other.lrc", "[00:01.00]wrong three");
        Assert.Null(new SidecarLyricsSource().Read(item, CancellationToken.None));

        Write("SONG ONE.LRC", "[00:01.00]right");
        Assert.Equal("right", new SidecarLyricsSource().Read(item, CancellationToken.None)!.Lines[0].Text);
    }

    [Fact]
    public void Reads_srt_and_vtt_cues_as_synced_lyrics()
    {
        var item = Song();
        Write("song.srt", "1\r\n00:00:01,500 --> 00:00:03,000\r\n<i>First</i>\r\nline\r\n\r\n2\r\n00:00:04,000 --> 00:00:05,000\r\nSecond\r\n");
        var srt = new SidecarLyricsSource().Read(item, CancellationToken.None)!;
        Assert.True(srt.Synced);
        Assert.Equal(["First line", "Second"], srt.Lines.Select(l => l.Text));
        Assert.Equal(TimeSpan.FromMilliseconds(1500), srt.Lines[0].Time);

        File.Delete(Path.Combine(_folder, "song.srt"));
        Write("song.vtt", "WEBVTT\n\n00:01.000 --> 00:02.000\nShort form\n");
        Assert.Equal("Short form", new SidecarLyricsSource().Read(item, CancellationToken.None)!.Lines[0].Text);
    }

    [Fact]
    public void Reads_persian_lrc_files_in_every_encoding()
    {
        var item = Song();
        foreach (var (encoding, bom) in new (Encoding, bool)[]
                 {
                     (new UTF8Encoding(false), false), (new UTF8Encoding(true), true), (Encoding.Unicode, true), (Encoding.BigEndianUnicode, true), (Encoding.GetEncoding(1256), false),
                 })
        {
            var body = encoding.GetBytes(PersianLrc);
            var bytes = bom ? encoding.GetPreamble().Concat(body).ToArray() : body;
            Write("song.lrc", bytes);
            var lyrics = new SidecarLyricsSource().Read(item, CancellationToken.None)!;
            var expected = encoding.CodePage == 1256 ? Through1256("سلام دنیا\nاین یک آزمایش است\nکتاب و یاد").Split('\n') : ["سلام دنیا", "این یک آزمایش است", "کتاب و یاد"];
            Assert.Equal(expected, lyrics.Lines.Select(l => l.Text));
            Assert.Equal("سلام", lyrics.Title);
            Assert.All(lyrics.Lines, l => Assert.Equal(LyricsTextDirection.RightToLeft, l.Direction));
        }
    }

    [Fact]
    public void A_file_over_one_megabyte_is_skipped_and_the_next_one_tried()
    {
        var item = Song();
        Write("song.lrc", new byte[LyricsLimits.MaxBytes + 1].Select(_ => (byte)'a').ToArray());
        Assert.Null(new SidecarLyricsSource().Read(item, CancellationToken.None));
        Write("song.txt", "small");
        Assert.Equal("small", new SidecarLyricsSource().Read(item, CancellationToken.None)!.Lines[0].Text);
    }

    [Fact]
    public void An_empty_or_blank_file_gives_nothing()
    {
        var item = Song();
        Write("song.lrc", "");
        Assert.Null(new SidecarLyricsSource().Read(item, CancellationToken.None));
        Write("song.lrc", "\r\n  \r\n");
        Assert.Null(new SidecarLyricsSource().Read(item, CancellationToken.None));
    }

    [Fact]
    public void Urls_and_missing_folders_have_no_sidecar()
    {
        Assert.Empty(SidecarLyricsSource.Find("http://example.com/a.mp3"));
        Assert.Empty(SidecarLyricsSource.Find(Path.Combine(_folder, "nope", "a.mp3")));
        Assert.Empty(SidecarLyricsSource.Find(""));
        Assert.Null(new SidecarLyricsSource().Read(new MediaItem("a.mp3"), CancellationToken.None));
    }

    [Fact]
    public void A_content_uri_goes_through_the_sibling_hook_and_the_content_opener()
    {
        var lrc = Write("hook.lrc", "[00:01.00]from a content uri");
        var oldFinder = MediaSources.SiblingResolver;
        var oldOpener = MediaSources.ContentOpener;
        var oldName = MediaSources.NameResolver;
        try
        {
            IReadOnlyList<string>? asked = null;
            MediaSources.SiblingResolver = (source, extensions) =>
            {
                asked = extensions;
                return source.EndsWith("song.mp3", StringComparison.Ordinal) ? ["content://docs/tree/x/document/x%3Asong.lrc"] : [];
            };
            MediaSources.ContentOpener = uri => uri.EndsWith("song.lrc", StringComparison.Ordinal) ? File.OpenRead(lrc) : null;
            MediaSources.NameResolver = uri => uri[(uri.LastIndexOf('%') + 3)..];

            var lyrics = new SidecarLyricsSource().Read(new MediaItem("content://docs/tree/x/document/x%3Asong.mp3"), CancellationToken.None)!;
            Assert.Equal("from a content uri", lyrics.Lines[0].Text);
            Assert.Equal([".lrc", ".txt", ".srt", ".vtt"], asked);

            Assert.Null(new SidecarLyricsSource().Read(new MediaItem("content://docs/tree/x/document/x%3Aother.mp3"), CancellationToken.None));
            MediaSources.SiblingResolver = null; // a head without the hook: no sidecar, no error
            Assert.Null(new SidecarLyricsSource().Read(new MediaItem("content://docs/tree/x/document/x%3Asong.mp3"), CancellationToken.None));
        }
        finally
        {
            MediaSources.SiblingResolver = oldFinder;
            MediaSources.ContentOpener = oldOpener;
            MediaSources.NameResolver = oldName;
        }
    }

    [Fact]
    public async Task The_service_reads_a_sidecar_end_to_end()
    {
        var item = Song();
        Write("song.lrc", PersianLrc, Encoding.GetEncoding(1256));
        var service = new LyricsService(null, [new TagLyricsSource(), new SidecarLyricsSource()]);
        var lyrics = (await service.GetAsync(item, TestContext.Current.CancellationToken))!;
        Assert.Equal(LyricsSources.Sidecar, lyrics.Source);
        Assert.Equal(Through1256("کتاب و یاد"), lyrics.Lines[2].Text);
        Assert.Equal(1, lyrics.IndexAt(TimeSpan.FromSeconds(6)));
    }
}
