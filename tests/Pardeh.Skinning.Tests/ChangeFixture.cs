using Pardeh.Core.Changes;
using Pardeh.Core.Library;
using Pardeh.Core.LyricsKit;
using Pardeh.Core.Playback;
using Pardeh.Nava;
using static Pardeh.Skinning.Tests.LyricsTestData;

namespace Pardeh.Skinning.Tests;

/// <summary>
/// A whole app's worth of stores and a change service over generated files in a temporary folder. The library's folders are
/// replaced before anything is scanned, so the user's own music is never looked at.
/// </summary>
internal sealed class ChangeFixture : IDisposable
{
    public string Root { get; }
    public string Music { get; }
    public string Data { get; }
    public string HeldFolder { get; }

    public MediaLibrary Library { get; private set; } = null!;
    public LyricsService Lyrics { get; private set; } = null!;
    public PlaylistStore Playlists { get; private set; } = null!;
    public FavoritesStore Favorites { get; private set; } = null!;
    public BookmarkStore Bookmarks { get; private set; } = null!;
    public VideoPositionStore VideoPositions { get; private set; } = null!;
    public FakeEngine Engine { get; } = new();
    public MediaPlayer Player { get; private set; } = null!;
    public ChangeJournal Journal { get; private set; } = null!;
    public FileChangeService Service { get; private set; } = null!;

    public ChangeFixture(string? root = null)
    {
        Assert.SkipWhen(FfmpegTool.Tool is null, "ffmpeg is not in tools/ffmpeg (scripts/get-ffmpeg.ps1)");
        Root = root ?? NewTempFolder("pardeh-change-");
        Music = Directory.CreateDirectory(Path.Combine(Root, "music")).FullName;
        Data = Directory.CreateDirectory(Path.Combine(Root, "data")).FullName;
        HeldFolder = Path.Combine(Root, "held");
        Build();
    }

    /// <summary>Makes every store again from what is on disk, as a new start of the app would.</summary>
    public void Build(IFileBackend? backend = null, ITagEditor? editor = null)
    {
        Library = new MediaLibrary(new TagMediaInfoReader(), Path.Combine(Data, "library.json"));
        Library.LoadSettings();
        Library.Folders.Clear();
        Library.Folders.Add(Music);
        Library.LoadCache();
        var lyricsStore = new LyricsStore(Path.Combine(Data, "lyrics"));
        Lyrics = new LyricsService(lyricsStore, []);
        Playlists = new PlaylistStore(Path.Combine(Data, "Playlists"));
        Favorites = new FavoritesStore(Path.Combine(Data, "favorites.json"));
        Bookmarks = new BookmarkStore(Path.Combine(Data, "bookmarks.json"));
        VideoPositions = new VideoPositionStore(Path.Combine(Data, "video-positions.json"));
        Player = new MediaPlayer(Engine, new TagMediaInfoReader());
        Journal = new ChangeJournal(Path.Combine(Data, "changes"));
        var link = new PlayerFileLink(Player);
        var references = new LibraryReferences(Library, new TagMediaInfoReader())
        {
            Lyrics = lyricsStore,
            LyricsChanged = Lyrics.Invalidate,
            Playlists = Playlists,
            Favorites = Favorites,
            Bookmarks = Bookmarks,
            VideoPositions = VideoPositions,
            Queue = link,
        };
        Service = new FileChangeService([backend ?? new LocalFileBackend(HoldingFolder.At(HeldFolder))], editor ?? new TagEditor(), references, Journal, Library)
        {
            Playback = link,
        };
    }

    public string Make(string name, string title = "Song", string artist = "Artist", string album = "Album", string codec = "libmp3lame")
    {
        var path = Path.Combine(Music, name);
        var tags = new Dictionary<string, string>();
        if (title.Length > 0) tags["title"] = title;
        if (artist.Length > 0) tags["artist"] = artist;
        if (album.Length > 0) tags["album"] = album;
        FfmpegTool.MakeAudio(path, codec, tags);
        return path;
    }

    public Task ScanAsync() => Library.ScanAsync();

    public static MediaItem Info(string path) => new TagMediaInfoReader().Read(path);

    public static LyricsTrackKey Key(string path) => LyricsTrackKey.From(Info(path));

    public async Task<BatchResult> Apply(params ChangeRequest[] requests) => await Service.ApplyAsync("test", requests);

    public static void AssertAll(ResultStatus status, BatchResult result) =>
        Assert.All(result.Results, r => Assert.True(r.Status == status, $"{r.Kind} {Path.GetFileName(r.Source)}: {r.Status} {r.Message}"));

    /// <summary>Everything under the root, without the working files that should be gone.</summary>
    public IReadOnlyList<string> Leftovers() =>
        Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".pardehwork", StringComparison.Ordinal) || f.EndsWith(".pardehbackup", StringComparison.Ordinal) || f.EndsWith(".restore", StringComparison.Ordinal))
            .ToList();

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
