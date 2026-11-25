using Pardeh.Core.Library;
using Pardeh.Core.Playback;

namespace Pardeh.Skinning.Tests;

public sealed class MarkerTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("pardeh-markers-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string File_ => Path.Combine(_folder, "bookmarks.json");

    [Fact]
    public void Names_are_kept_and_the_old_number_format_still_loads()
    {
        File.WriteAllText(File_, """{"c:\\a.mp3":[40.5,12]}""");
        var store = new BookmarkStore(File_);
        Assert.Equal([12, 40.5], store.Get(@"c:\a.mp3"));

        Assert.Equal(1, store.Add(@"c:\a.mp3", 20, "  chorus\n"));
        store.Rename(@"c:\a.mp3", 2, "solo");

        var again = new BookmarkStore(File_);
        Assert.Equal([new Marker(12, ""), new Marker(20, "chorus"), new Marker(40.5, "solo")], again.GetMarkers(@"c:\a.mp3"));
        again.RemoveAt(@"c:\a.mp3", 1);
        Assert.Equal([12, 40.5], new BookmarkStore(File_).Get(@"c:\a.mp3"));
    }

    [Fact]
    public void A_damaged_entry_is_skipped()
    {
        File.WriteAllText(File_, """{"x":[5,"text",{"n":"no time"},{"t":9,"n":"ok"}]}""");
        Assert.Equal([new Marker(5, ""), new Marker(9, "ok")], new BookmarkStore(File_).GetMarkers("x"));
    }
}
