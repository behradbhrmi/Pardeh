using System.Text;
using System.Text.Json;
using Pardeh.Core.Library;
using Pardeh.Skinning.Imaging;
using Pardeh.Skinning.Runtime;
using Pardeh.Skinning.Runtime.Elements;
using Pardeh.Skinning.Wmp;

namespace Pardeh.Skinning.Tests;

public sealed class LibraryTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"pardeh-lib-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private (SkinSession Session, LibraryElement Library, FakeEngine Engine) Start()
    {
        Directory.CreateDirectory(_folder);
        // Real files so the cache keeps them; tags come from the cache.
        LibraryItem Track(string title, string artist, string album, int number)
        {
            var path = Path.Combine(_folder, title + ".mp3");
            File.WriteAllText(path, "x");
            return new LibraryItem(path, title, artist, album, "Jazz", "2003", number, 60, false, 1, DateTime.UtcNow);
        }

        var items = new List<LibraryItem>
        {
            Track("B Song", "Artist One", "First", 2),
            Track("A Song", "Artist One", "First", 1),
            Track("C Song", "Artist Two", "Second", 1),
        };
        var cache = Path.Combine(_folder, "library.json");
        File.WriteAllText(cache, JsonSerializer.Serialize(items));

        var library = new MediaLibrary(new FakeInfoReader(), cache);
        library.LoadCache();

        const string wms = """
            <THEME id="t"><VIEW id="v" width="600" height="400">
              <LIBRARY id="lib" left="0" top="0" width="600" height="400"/>
            </VIEW></THEME>
            """;
        var package = WmzLoader.FromFiles(new() { ["t.wms"] = Encoding.UTF8.GetBytes(wms) }, "memory://t");
        var engine = new FakeEngine();
        var player = new Pardeh.Core.Playback.MediaPlayer(engine, new FakeInfoReader());
        var session = new SkinSession(package, player, new FakeHost(), new BmpDecoder(), new MemoryPreferences())
        {
            Library = library,
            Playlists = new PlaylistStore(Path.Combine(_folder, "Playlists")),
        };
        session.Start();
        return (session, (LibraryElement)session.CurrentView!.FindById("lib")!, engine);
    }

    [Fact]
    public void All_music_lists_tracks_in_album_order()
    {
        var (_, library, _) = Start();

        Assert.Equal(["A Song", "B Song", "C Song"], library.Tracks.Select(t => t.Title));
        Assert.DoesNotContain(library.Nodes, n => n.Key == "artist"); // All Music starts collapsed, as in the player
        library.Toggle("music");
        Assert.Contains(library.Nodes, n => n.Key == "artist");
        Assert.Equal("03:00", library.TotalTimeText);
    }

    [Fact]
    public void Expanding_artist_lists_each_artist_and_filters()
    {
        var (_, library, _) = Start();

        library.Toggle("music");
        library.Toggle("artist");
        Assert.Contains(library.Nodes, n => n.Key == "artist:Artist Two" && n.Depth == 2);

        library.Select("artist:Artist Two");
        Assert.Equal(["C Song"], library.Tracks.Select(t => t.Title));
    }

    [Fact]
    public void Playing_from_the_library_queues_the_listed_tracks()
    {
        var (session, library, engine) = Start();

        library.PlayFrom(1);

        Assert.Equal(3, session.Player.CurrentPlaylist.Count);
        Assert.Equal("B Song", session.Player.CurrentMedia?.Name);
        Assert.True(engine.Playing);
    }

    [Fact]
    public void Search_lists_matches_and_headers_sort()
    {
        var (_, library, _) = Start();

        library.Search("two");
        Assert.Equal("search", library.SelectedNodeKey);
        Assert.Equal(["C Song"], library.Tracks.Select(t => t.Title));

        library.Search(string.Empty);
        // Click the Title header twice: descending.
        var titleHeader = (library.ListLeft + 10, LibraryElement.ToolbarHeight + 5);
        library.OnPointerPressed(titleHeader.Item1, titleHeader.Item2);
        library.OnPointerPressed(titleHeader.Item1, titleHeader.Item2);
        Assert.Equal(["C Song", "B Song", "A Song"], library.Tracks.Select(t => t.Title));
    }

    [Fact]
    public void Removed_items_stay_removed_after_reload()
    {
        var (session, library, _) = Start();
        var removed = library.Tracks[0].Path;

        session.Library!.Remove([removed]);
        library.Invalidate();
        Assert.DoesNotContain(library.Tracks, t => t.Path == removed);

        var reloaded = new MediaLibrary(new FakeInfoReader(), Path.Combine(_folder, "library.json"));
        reloaded.LoadSettings();
        reloaded.LoadCache();
        Assert.DoesNotContain(reloaded.Items, t => t.Path == removed);
    }

    [Fact]
    public void Saved_playlists_appear_under_my_playlists()
    {
        var (session, library, _) = Start();
        session.Playlists!.Save("Road Trip", library.Tracks.Take(2).Select(t => t.Path));
        library.Invalidate();

        Assert.Contains(library.Nodes, n => n.Key == "playlist:Road Trip" && n.Depth == 1);
        library.Select("playlist:Road Trip");
        Assert.Equal(["A Song", "B Song"], library.Tracks.Select(t => t.Title));
    }

    [Fact]
    public void Deduplicate_folds_the_same_file_seen_by_path_and_by_content_uri()
    {
        LibraryItem Item(string path, string title, long size = 100, int rating = 0) =>
            new(path, title, "Artist", "Album", "", "", 1, 5.2, false, size, DateTime.UtcNow) { Rating = rating };

        var items = new List<LibraryItem>
        {
            Item("content://x/tree/primary%3AMusic/document/primary%3AMusic%2Fa.wav", "A", rating: 4),
            Item("/storage/emulated/0/Music/a.wav", "A"),
            Item("/sdcard/Music/a.wav", "A"),
            Item("content://other/doc/77", "A"), // unknown identity, same tags and size as A
            Item("/storage/emulated/0/Music/b.wav", "B", 200),
            Item("/storage/emulated/0/Music/c.wav", "A", 300), // same title, different file
        };

        var old = MediaSources.IdentityKey;
        try
        {
            Assert.Same(items, MediaLibrary.Deduplicate(items)); // no hook (Windows): untouched
            MediaSources.IdentityKey = s =>
                s.StartsWith("/sdcard/", StringComparison.Ordinal) ? "/storage/emulated/0/" + s[8..]
                : s.Contains("primary%3AMusic%2F", StringComparison.Ordinal) ? "/storage/emulated/0/Music/" + s[(s.LastIndexOf("%2F", StringComparison.Ordinal) + 3)..]
                : s.StartsWith("content://", StringComparison.Ordinal) ? null : s;
            var result = MediaLibrary.Deduplicate(items);

            Assert.Equal(["/storage/emulated/0/Music/a.wav", "/storage/emulated/0/Music/b.wav", "/storage/emulated/0/Music/c.wav"], result.Select(i => i.Path));
            Assert.Equal(4, result[0].Rating); // user data of the dropped copy survives
        }
        finally
        {
            MediaSources.IdentityKey = old;
        }
    }

    [Fact]
    public void Quick_hide_hides_panels_and_brings_them_back()
    {
        var (session, _, _) = Start();
        var center = session.MediaCenter;
        center.SetMember("showSettings", true);

        center.SetMember("quickHide", true);
        Assert.False(SkinValue.ToBool(center.GetMember("showPlaylist")));
        Assert.False(SkinValue.ToBool(center.GetMember("showSettings")));

        center.SetMember("quickHide", false);
        Assert.True(SkinValue.ToBool(center.GetMember("showPlaylist")));
        Assert.True(SkinValue.ToBool(center.GetMember("showSettings")));
    }
}
