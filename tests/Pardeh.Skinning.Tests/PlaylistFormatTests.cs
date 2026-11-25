using Pardeh.Core.Library;

namespace Pardeh.Skinning.Tests;

public sealed class PlaylistFormatTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("pardeh-pl-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Write(string name, string text)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void Reads_the_classic_playlist_formats()
    {
        const string absolute = @"C:\music\b.mp3";
        var expected = new[] { Path.Combine(_folder, "a.mp3"), absolute, "http://radio.example/stream" };
        Assert.Equal(expected, PlaylistFormats.Read(Write("t.m3u", $"#EXTM3U\n#EXTINF:1,A\na.mp3\n{absolute}\nhttp://radio.example/stream\n")));
        Assert.Equal(expected, PlaylistFormats.Read(Write("t.pls", $"[playlist]\nFile1=a.mp3\nTitle1=A\nFile2={absolute}\nFile3=http://radio.example/stream\nNumberOfEntries=3\n")));
        Assert.Equal(expected, PlaylistFormats.Read(Write("t.wpl", $"<?wpl version=\"1.0\"?><smil><body><seq><media src=\"a.mp3\"/><media src=\"{absolute}\"/><media src=\"http://radio.example/stream\"/></seq></body></smil>")));
        Assert.Equal(expected, PlaylistFormats.Read(Write("t.asx", $"<ASX version=\"3.0\"><Entry><Ref href=\"a.mp3\"/></Entry><ENTRY><REF HREF=\"{absolute}\"/></ENTRY><entry><ref href=\"http://radio.example/stream\" /></entry></ASX>")));
    }

    [Fact]
    public void Ratings_and_plays_survive_a_rescan()
    {
        var song = Write("song.mp3", "x");
        var cache = Path.Combine(_folder, "library.json");
        var library = new MediaLibrary(new FakeInfoReader(), cache);
        library.Folders.Clear();
        library.Folders.Add(_folder);
        library.ScanAsync().Wait();

        library.SetRating(song, 4);
        library.RecordPlay(song);
        File.SetLastWriteTimeUtc(song, DateTime.UtcNow.AddMinutes(1)); // changed on disk: tags are re-read
        library.ScanAsync().Wait();

        var item = Assert.Single(library.Items);
        Assert.Equal(4, item.Rating);
        Assert.Equal(1, item.PlayCount);
    }
}
