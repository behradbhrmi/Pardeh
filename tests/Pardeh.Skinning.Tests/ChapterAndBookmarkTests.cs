using System.Text;
using Pardeh.Core.Library;
using Pardeh.Nava.Ffmpeg;

namespace Pardeh.Skinning.Tests;

public sealed class BookmarkStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"pardeh-bm-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }

    [Fact]
    public void Bookmarks_round_trip_per_path_sorted()
    {
        var file = Path.Combine(_folder, "bookmarks.json");
        var store = new BookmarkStore(file);
        Assert.Empty(store.Get(@"C:\a.mp4"));

        store.Add(@"C:\a.mp4", 30.5);
        store.Add(@"C:\a.mp4", 10);
        store.Add(@"C:\b.mp3", 5);
        store.RemoveAt(@"C:\b.mp3", 0);

        var reloaded = new BookmarkStore(file);
        Assert.Equal([10d, 30.5], reloaded.Get(@"c:\A.MP4")); // paths compare case-insensitively
        Assert.Empty(reloaded.Get(@"C:\b.mp3"));

        reloaded.RemoveAt(@"C:\a.mp4", 0);
        Assert.Equal([30.5], new BookmarkStore(file).Get(@"C:\a.mp4"));
    }

    [Fact]
    public void A_damaged_file_loads_as_empty()
    {
        Directory.CreateDirectory(_folder);
        var file = Path.Combine(_folder, "bookmarks.json");
        File.WriteAllText(file, "{ not json");
        Assert.Empty(new BookmarkStore(file).Get("x"));
    }
}

public sealed class ChapterTests : IDisposable
{
    private readonly string _mka = Path.Combine(Path.GetTempPath(), $"pardeh-chap-{Guid.NewGuid():N}.mka");

    public void Dispose() => File.Delete(_mka);

    [Fact]
    public void Reads_chapters_from_a_matroska_file()
    {
        Assert.SkipWhen(!FfmpegLibrary.IsAvailable, $"FFmpeg not available: {FfmpegLibrary.LoadError}");
        WriteMatroskaWithChapters(_mka);
        if (Environment.GetEnvironmentVariable("PARDEH_TEST_MKA_COPY") is { Length: > 0 } copy)
            File.Copy(_mka, copy, true);

        var chapters = FfmpegAudioSource.ListChapters(_mka);

        Assert.Equal(3, chapters.Count);
        Assert.Equal("Intro", chapters[0].Title);
        Assert.Equal("Verse one", chapters[1].Title);
        Assert.Equal("Chapter 3", chapters[2].Title); // no title → numbered
        Assert.Equal(0, chapters[0].Start.TotalSeconds, 2);
        Assert.Equal(60, chapters[1].Start.TotalSeconds, 2);
        Assert.Equal(125, chapters[2].Start.TotalSeconds, 2);
        Assert.Equal(60, chapters[0].End.TotalSeconds, 2);
    }

    [Fact]
    public void A_file_without_chapters_has_none()
    {
        Assert.SkipWhen(!FfmpegLibrary.IsAvailable, $"FFmpeg not available: {FfmpegLibrary.LoadError}");
        var wav = Path.ChangeExtension(_mka, ".wav");
        try
        {
            using (var writer = new BinaryWriter(File.Create(wav)))
            {
                writer.Write("RIFF"u8);
                writer.Write(36 + 4000);
                writer.Write("WAVEfmt "u8);
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)2);
                writer.Write(44100);
                writer.Write(44100 * 4);
                writer.Write((short)4);
                writer.Write((short)16);
                writer.Write("data"u8);
                writer.Write(4000);
                writer.Write(new byte[4000]);
            }

            Assert.Empty(FfmpegAudioSource.ListChapters(wav));
        }
        finally
        {
            File.Delete(wav);
        }
    }

    // ---- A minimal Matroska writer (PCM audio, one cluster, three chapters). ----

    private static byte[] Element(string id, params byte[][] children) => Element(id, children.SelectMany(c => c).ToArray());

    private static byte[] Element(string id, byte[] payload)
    {
        var idBytes = Convert.FromHexString(id);
        var size = new byte[8];
        size[0] = 0x01;
        for (var i = 1; i < 8; i++)
            size[i] = (byte)(payload.LongLength >> (8 * (7 - i)));
        return [.. idBytes, .. size, .. payload];
    }

    private static byte[] UInt(string id, long value) => Element(id, BitConverter.GetBytes(value).Reverse().SkipWhile(b => b == 0).DefaultIfEmpty((byte)0).ToArray());

    private static byte[] Str(string id, string value) => Element(id, Encoding.ASCII.GetBytes(value));

    private static byte[] Chapter(long uid, double startSeconds, double endSeconds, string? title) =>
        Element("B6", UInt("73C4", uid), UInt("91", (long)(startSeconds * 1e9)), UInt("92", (long)(endSeconds * 1e9)),
            title is null ? [] : Element("80", Str("85", title), Str("437C", "eng")));

    private static void WriteMatroskaWithChapters(string path)
    {
        var header = Element("1A45DFA3", UInt("4286", 1), UInt("42F7", 1), UInt("42F2", 4), UInt("42F3", 8), Str("4282", "matroska"), UInt("4287", 4), UInt("4285", 2));
        var info = Element("1549A966", UInt("2AD7B1", 1_000_000));
        var audio = Element("E1", Element("B5", BitConverter.GetBytes(44100.0).Reverse().ToArray()), UInt("9F", 2), UInt("6264", 16));
        var tracks = Element("1654AE6B", Element("AE", UInt("D7", 1), UInt("73C5", 1), UInt("83", 2), Str("86", "A_PCM/INT/LIT"), audio));
        var chapters = Element("1043A770", Element("45B9",
            Chapter(1, 0, 60, "Intro"), Chapter(2, 60, 125, "Verse one"), Chapter(3, 125, 180, null)));
        var blocks = Enumerable.Range(0, 4).Select(i => Element("A3", [0x81, (byte)(i * 1000 >> 8), (byte)(i * 1000), 0x80, .. new byte[44100 * 4]])).ToArray();
        var cluster = Element("1F43B675", [.. UInt("E7", 0), .. blocks.SelectMany(b => b)]);
        var segmentPayload = new[] { info, tracks, chapters, cluster }.SelectMany(b => b).ToArray();
        File.WriteAllBytes(path, [.. header, .. Element("18538067", segmentPayload)]);
    }
}
