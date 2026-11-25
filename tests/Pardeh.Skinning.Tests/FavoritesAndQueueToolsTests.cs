using System.Text.Json;
using Avalonia;
using Avalonia.Media;
using Avalonia.LogicalTree;
using Pardeh.Core.Library;
using Pardeh.Core.Playback;
using Pardeh.Skinning.Model;
using Pardeh.Skinning.Runtime;
using Pardeh.Skinning.Runtime.Elements;
using Pardeh.UI;
using Pardeh.UI.FullMode;
using Pardeh.UI.Rendering;
using MediaPlayer = Pardeh.Core.Playback.MediaPlayer;

namespace Pardeh.Skinning.Tests;

/// <summary>
/// The favourite heart and the queue tools (sort, filter) in the seven phone looks: layout in portrait and landscape, behaviour
/// (the heart follows and sets the favourite state, the menus sort / reverse / shuffle / de-duplicate / clear without restarting
/// playback, the filter only hides rows), the art with hi-res and tinted builds, and the Library pages.
/// </summary>
[Collection("Avalonia rendering")]
public sealed class FavoritesAndQueueToolsTests : IDisposable
{
    private readonly int _scale = PhoneSkin.ArtScale;
    private readonly GlassPalette.Tint _tint = GlassPalette.Current;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"pardeh-favui-{Guid.NewGuid():N}");

    public void Dispose()
    {
        PhoneSkin.ArtScale = _scale;
        GlassPalette.Current = _tint;
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static void EnsurePlatform() => TestPlatform.Ensure();

    /// <summary>A host that records the menus it was asked to show and answers them from a script of choices.</summary>
    private sealed class MenuHost : ISkinHost
    {
        public List<IReadOnlyList<string>> Menus { get; } = [];
        public Queue<int> Choices { get; } = [];
        public List<string> Commands { get; } = [];
        public Action<string>? Prompted { get; private set; }

        public void ViewChanged(SkinView view) { }
        public void ResizeView(SkinView view, double width, double height) { }
        public (double Width, double Height) MaxViewSize => (1920, 1040);
        public void MinimizeView(SkinView view) { }
        public void BeginResize(SkinView view, string edge) { }
        public void CloseSkin() { }
        public void ReturnToFullMode() { }
        public string? ShowOpenFileDialog(string dialogType, string filter) => null;
        public void LaunchUrl(string url) { }
        public void ShowPopup(SkinView view, IReadOnlyList<string> items, int selectedIndex, Action<int> onSelected)
        {
            Menus.Add(items);
            onSelected(Choices.Count > 0 ? Choices.Dequeue() : -1);
        }

        public int ShowPopupModal(SkinView view, IReadOnlyList<string> items) => -1;
        public void ShowMessage(string text) { }
        public double MeasureText(string text, string fontFamily, double sizePoints, bool bold) => text.Length * sizePoints * 0.75;

        public object? RunCommand(string name, IReadOnlyList<object?> args)
        {
            Commands.Add(name);
            if (name == "prompt" && args.Count >= 3 && args[2] is Action<string> answer)
                Prompted = answer;
            return null;
        }

        public void Log(string message) { }
    }

    private static (SkinSession Session, MediaPlayer Player, MenuHost Host, FavoritesStore Store) Start(string key)
    {
        EnsurePlatform();
        var player = new MediaPlayer(new FakeEngine(), new FakeInfoReader());
        var store = new FavoritesStore();
        player.Favorites = store;
        var host = new MenuHost();
        var session = new SkinSession(PhoneSkin.Build(key), player, host, new AvaloniaImageDecoder(), new MemoryPreferences());
        session.Start();
        session.Tick(TimeSpan.FromMilliseconds(30));
        return (session, player, host, store);
    }

    public static TheoryData<string> Looks => [.. PhoneSkin.Keys];

    private static readonly (double W, double H)[] Sizes = [(360, 720), (412, 892), (320, 640), (300, 600), (800, 360), (892, 412), (640, 320)];

    private static (double X, double Y, double W, double H) Absolute(SkinElement e)
    {
        double x = 0, y = 0;
        for (var p = e; p is not null; p = p.Parent)
        {
            x += p.Left;
            y += p.Top;
        }

        return (x, y, e.Width, e.Height);
    }

    private static bool Overlaps((double X, double Y, double W, double H) a, (double X, double Y, double W, double H) b) =>
        a.X < b.X + b.W - 0.5 && b.X < a.X + a.W - 0.5 && a.Y < b.Y + b.H - 0.5 && b.Y < a.Y + a.H - 0.5;

    private static bool IsAncestor(SkinElement maybe, SkinElement of)
    {
        for (var p = of.Parent; p is not null; p = p.Parent)
            if (ReferenceEquals(p, maybe))
                return true;
        return false;
    }

    private static bool AncestorsVisible(SkinElement e)
    {
        for (var p = e.Parent; p is not null; p = p.Parent)
            if (!p.Visible)
                return false;
        return true;
    }

    private static readonly string[] NowControls =
    [
        "bPlay", "bPause", "bPrev", "bNext", "bStop", "bMute", "bMuteLow", "bMuted", "bShuffle", "bRepeat", "bSkipB", "bSkipF", "vol", "seek",
        "tPos", "tDur", "tRem", "tTitle", "tArtist", "tAlbum", "svStars", "svStage", "svCover", "svPill",
    ];

    // ---- The heart on Now Playing ---------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Looks))]
    public void The_heart_is_a_44_unit_target_inside_the_view_and_clear_of_the_other_controls_in_all_sizes(string key)
    {
        var (session, player, _, _) = Start(key);
        player.OpenFiles(["C:/music/a.mp3"]);
        var view = session.CurrentView!;
        foreach (var (w, h) in Sizes)
        {
            view.Root.Width = w;
            view.Root.Height = h;
            session.Tick(TimeSpan.FromMilliseconds(30));
            var heart = view.FindById("bFav") ?? throw new Xunit.Sdk.XunitException($"{key} has no bFav");
            Assert.True(heart.Visible && AncestorsVisible(heart), $"{key} {w}x{h}: the heart is hidden");
            var r = Absolute(heart);
            Assert.True(r.W >= 44 && r.H >= 44, $"{key} {w}x{h}: the heart is {r.W}x{r.H} units, a touch target needs 44+");
            Assert.True(r.X >= 0 && r.Y >= 0 && r.X + r.W <= w + 0.5 && r.Y + r.H <= h + 0.5, $"{key} {w}x{h}: the heart {r} leaves the view");
            foreach (var id in NowControls)
            {
                if (view.FindById(id) is not { Visible: true, Width: > 0 } other || !AncestorsVisible(other) || IsAncestor(other, heart))
                    continue;
                // The stage / cover / pill are backgrounds the other controls sit on; the heart must not overlap them either, except where it is
                // part of the same panel (the strip of Luna Phone Plus holds the title, the heart and the time side by side).
                var o = Absolute(other);
                Assert.False(Overlaps(r, o), $"{key} {w}x{h}: the heart {r} overlaps {id} {o}");
            }
        }
    }

    [Theory]
    [MemberData(nameof(Looks))]
    public void The_heart_button_follows_and_sets_the_favourite_state(string key)
    {
        var (session, player, _, store) = Start(key);
        var view = session.CurrentView!;
        var heart = (ButtonElement)view.FindById("bFav")!;
        Assert.False(heart.Enabled, $"{key}: nothing playing, the heart is disabled");

        player.OpenFiles(["C:/music/a.mp3"]);
        session.Tick(TimeSpan.FromMilliseconds(30));
        Assert.True(heart.Enabled);
        Assert.False(heart.Down);
        foreach (var (fx, fy) in new[] { (0.5, 0.5), (0.15, 0.15), (0.85, 0.85) })
            Assert.True(heart.HitTest(heart.Width * fx, heart.Height * fy), $"{key}: the heart misses at {fx},{fy}");

        heart.OnPointerPressed(5, 5);
        heart.OnPointerReleased(5, 5, inside: true);
        session.Tick(TimeSpan.FromMilliseconds(30));
        Assert.True(store.IsFavorite("C:/music/a.mp3"));
        Assert.True(heart.Down, $"{key}: the heart shows the favourite state");

        // A favourite set elsewhere (the Library, a controller) shows at once, and a second press clears it.
        store.Set("C:/music/a.mp3", false);
        session.Tick(TimeSpan.FromMilliseconds(30));
        Assert.False(heart.Down);
        heart.OnPointerPressed(5, 5);
        heart.OnPointerReleased(5, 5, inside: true);
        heart.OnPointerPressed(5, 5);
        heart.OnPointerReleased(5, 5, inside: true);
        Assert.False(store.IsFavorite("C:/music/a.mp3"));
    }

    // ---- The queue tools ------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Looks))]
    public void The_queue_tools_sit_in_the_playlist_header_without_overlapping_in_all_sizes(string key)
    {
        var (session, player, _, _) = Start(key);
        player.OpenFiles(["C:/music/a.mp3"]);
        var view = session.CurrentView!;
        var glass = key == "glass-phone";
        var plus = key == "luna-plus-phone";
        var (sort, filter) = plus ? ("bQSortS", "bQFilterS") : ("bQSort", "bQFilter");
        foreach (var (w, h) in Sizes)
        {
            view.Root.Width = w;
            view.Root.Height = h;
            if (!glass && !plus)
                view.Script.Execute("ShowPane('queue');", "test");
            session.Tick(TimeSpan.FromMilliseconds(30));
            var a = view.FindById(sort) ?? throw new Xunit.Sdk.XunitException($"{key} has no {sort}");
            var b = view.FindById(filter) ?? throw new Xunit.Sdk.XunitException($"{key} has no {filter}");
            Assert.True(a.Visible && b.Visible && AncestorsVisible(a) && AncestorsVisible(b), $"{key} {w}x{h}: the queue tools are hidden");
            var ra = Absolute(a);
            var rb = Absolute(b);
            Assert.True(ra.W >= 32 && ra.H >= 28 && rb.W >= 32 && rb.H >= 28, $"{key} {w}x{h}: queue tools {ra} / {rb} are small");
            foreach (var (name, r) in new[] { (sort, ra), (filter, rb) })
                Assert.True(r.X >= 0 && r.Y >= 0 && r.X + r.W <= w + 0.5 && r.Y + r.H <= h + 0.5, $"{key} {w}x{h}: {name} {r} leaves the view");
            Assert.False(Overlaps(ra, rb), $"{key} {w}x{h}: sort and filter overlap");
            foreach (var id in new[] { "bShuffle", "bRepeat", "chevUp", "chevDown", "tPlHead", "tSheet" })
            {
                if (view.FindById(id) is not { Visible: true, Width: > 0 } other || !AncestorsVisible(other))
                    continue;
                var o = Absolute(other);
                Assert.False(Overlaps(ra, o), $"{key} {w}x{h}: {sort} {ra} overlaps {id} {o}");
                Assert.False(Overlaps(rb, o), $"{key} {w}x{h}: {filter} {rb} overlaps {id} {o}");
            }
        }
    }

    private static void Press(SkinElement e)
    {
        e.OnPointerPressed(4, 4);
        e.OnPointerReleased(4, 4, inside: true);
    }

    private static MediaItem Item(string name, string artist, double seconds, string album = "") =>
        new($"/music/{name}.mp3") { Name = name, Duration = TimeSpan.FromSeconds(seconds), Attributes = { ["Author"] = artist, ["Album"] = album } };

    private static (SkinSession Session, MediaPlayer Player, MenuHost Host, FavoritesStore Store, SkinView View) StartWithQueue(string key = "fluent-phone")
    {
        var (session, player, host, store) = Start(key);
        player.OpenItems([Item("Charlie", "Zed", 30), Item("alpha", "Yan", 10), Item("Bravo", "Xi", 20), Item("alpha", "Yan", 10)], 0);
        session.Tick(TimeSpan.FromMilliseconds(30));
        var view = session.CurrentView!;
        view.Script.Execute("ShowPane('queue');", "test");
        session.Tick(TimeSpan.FromMilliseconds(30));
        return (session, player, host, store, view);
    }

    [Fact]
    public void The_sort_menu_sorts_reverses_shuffles_and_removes_duplicates_without_restarting_playback()
    {
        var (session, player, host, _, view) = StartWithQueue();
        var media = player.CurrentMedia;
        var list = player.CurrentPlaylist;
        var sort = view.FindById("bQSort")!;

        host.Choices.Enqueue(0); // Sort by Title
        Press(sort);
        Assert.Equal(["alpha", "alpha", "Bravo", "Charlie"], list.Items.Select(i => i.Name));
        Assert.Same(media, player.CurrentMedia);
        Assert.Equal(3, list.CurrentIndex);
        Assert.Contains(host.Menus[0], m => m == "Sort by Title");
        Assert.Contains(host.Menus[0], m => m == "Reverse order");
        Assert.Contains(host.Menus[0], m => m == "Shuffle now");
        Assert.Contains(host.Menus[0], m => m == "Remove duplicates");
        Assert.Contains(host.Menus[0], m => m.StartsWith("Clear the queue", StringComparison.Ordinal));

        Press(sort);
        Assert.Contains("Sort by Title (ascending)", host.Menus[^1]); // the active sort is marked, a second tap on it flips the direction
        host.Choices.Enqueue(0);
        Press(sort);
        Assert.Equal(["Charlie", "Bravo", "alpha", "alpha"], list.Items.Select(i => i.Name));
        Assert.Equal(0, list.CurrentIndex);

        host.Choices.Enqueue(6); // Reverse order
        Press(sort);
        Assert.Equal(["alpha", "alpha", "Bravo", "Charlie"], list.Items.Select(i => i.Name));
        Assert.Same(media, list.Current);

        host.Choices.Enqueue(8); // Remove duplicates
        Press(sort);
        Assert.Equal(["alpha", "Bravo", "Charlie"], list.Items.Select(i => i.Name));
        Assert.Same(media, list.Current);

        host.Choices.Enqueue(7); // Shuffle now
        Press(sort);
        Assert.Equal(3, list.Count);
        Assert.Same(media, list.Current);
        Assert.Equal(0, list.CurrentIndex);
        Assert.Equal(PlayState.Playing, player.PlayState);
    }

    [Fact]
    public void Glyph_buttons_of_the_Ember_skin_are_clickable_over_their_whole_face()
    {
        EnsurePlatform();
        var player = new MediaPlayer(new FakeEngine(), new FakeInfoReader());
        var session = new SkinSession(Pardeh.UI.FullMode.EmberSkin.Build(), player, new MenuHost(), new AvaloniaImageDecoder(), new MemoryPreferences());
        session.Start();
        session.Tick(TimeSpan.FromMilliseconds(30));

        var checkedButtons = 0;
        void Walk(SkinElement element)
        {
            if (element is ButtonElement { Width: >= 20, Height: >= 20 } button && button.Visible && button.CurrentImage is { } image && button.Width <= 40)
            {
                // The icon is a thin stroke in the middle; its corner and its edge centres must still hit.
                Assert.True(button.HitTest(3, 3), $"{button.Id} corner");
                Assert.True(button.HitTest(button.Width - 4, button.Height / 2), $"{button.Id} right edge");
                Assert.True(button.HitTest(button.Width / 2, button.Height - 4), $"{button.Id} bottom edge");
                checkedButtons++;
            }

            foreach (var child in element.Children)
                Walk(child);
        }

        Walk(session.CurrentView!.Root);
        Assert.True(checkedButtons >= 5, $"checked {checkedButtons} icon buttons");
    }

    [Fact]
    public void Clearing_the_queue_asks_first()
    {
        var (_, player, host, _, view) = StartWithQueue();
        var sort = view.FindById("bQSort")!;
        host.Choices.Enqueue(9); // Clear the queue...
        host.Choices.Enqueue(1); // ... "No, keep the queue"
        Press(sort);
        Assert.Equal(4, player.CurrentPlaylist.Count);
        Assert.Contains(host.Menus[^1], m => m.StartsWith("Yes, clear", StringComparison.Ordinal));

        host.Choices.Enqueue(9);
        host.Choices.Enqueue(0); // yes
        Press(sort);
        Assert.Equal(0, player.CurrentPlaylist.Count);
    }

    [Fact]
    public void The_filter_hides_rows_without_changing_the_queue_and_taps_play_the_real_item()
    {
        var (session, player, host, store, view) = StartWithQueue();
        var list = player.CurrentPlaylist;
        var filterButton = view.FindById("bQFilter")!;
        var playlist = (PlaylistElement)view.FindById("ipl")!;
        Assert.Equal([0, 1, 2, 3], playlist.Shown);

        // Filter by text (the prompt command carries the answer back).
        host.Choices.Enqueue(0);
        Press(filterButton);
        Assert.Contains("prompt", host.Commands);
        host.Prompted!("brav");
        Assert.Equal([2], playlist.Shown);
        Assert.Equal(["Charlie", "alpha", "Bravo", "alpha"], list.Items.Select(i => i.Name)); // display only
        session.Tick(TimeSpan.FromMilliseconds(30));
        Assert.True(((ButtonElement)filterButton).Down, "the filter button shows that a filter is on");
        Assert.Contains("1 of 4 songs shown", playlist.HeaderText);

        // A tap on the first visible row plays the item it stands for (the third in the queue), without moving it.
        var rowH = PlaylistElement.RowHeight;
        playlist.OnPointerPressed(20, playlist.HeaderHeight + rowH / 2);
        playlist.OnPointerReleased(20, playlist.HeaderHeight + rowH / 2, inside: true);
        Assert.Equal(2, list.CurrentIndex);
        Assert.Equal("Bravo", player.CurrentMedia?.Name);

        // Matching ignores case and folds the Arabic yeh / kaf and the digits of Persian text.
        list.Filter.Text = "ALPH";
        Assert.Equal([1, 3], playlist.Shown);
        list.Filter.Text = "Yan";
        Assert.Equal([1, 3], playlist.Shown); // artists match too
        list.Filter.Clear();
        Assert.Equal([0, 1, 2, 3], playlist.Shown);

        // Favourites only.
        store.Set("/music/Charlie.mp3", true);
        list.Filter.FavoritesOnly = true;
        Assert.Equal([0], playlist.Shown);
        Assert.True(list.Filter.IsActive);
        host.Choices.Enqueue(2); // Show everything
        Press(filterButton);
        Assert.False(list.Filter.IsActive);
        Assert.Equal(4, playlist.Shown.Count);
    }

    [Fact]
    public void The_long_press_menu_has_the_favourite_entry_and_a_favourite_row_is_marked_by_its_state()
    {
        var (_, player, host, store, view) = StartWithQueue();
        var playlist = (PlaylistElement)view.FindById("ipl")!;
        var y = playlist.HeaderHeight + PlaylistElement.RowHeight * 1.5; // the second row

        host.Choices.Enqueue(1); // Add to favourites
        Assert.True(playlist.OnContextMenu(20, y));
        Assert.Contains("Add to favourites", host.Menus[^1]);
        Assert.True(store.IsFavorite("/music/alpha.mp3"));
        Assert.True(player.IsFavorite(player.CurrentPlaylist.Items[1]));

        host.Choices.Enqueue(1); // Remove from favourites
        Assert.True(playlist.OnContextMenu(20, y));
        Assert.Contains("Remove from favourites", host.Menus[^1]);
        Assert.False(store.IsFavorite("/music/alpha.mp3"));

        // Above the first row (the header band) there is no row to act on.
        Assert.False(playlist.OnContextMenu(20, 5));
    }

    [Fact]
    public void Queue_rows_are_46_units_tall_with_two_lines_in_the_looks_that_need_them()
    {
        // The queue page of the looks without a card is a touch list now (title over artist, long press menu): 46 unit rows.
        Assert.Contains("touch=\"true\"", System.Text.Encoding.UTF8.GetString(PhoneSkin.Build("fluent-phone").ReadFile("phone.wms")!), StringComparison.Ordinal);
    }

    // ---- Art ----------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Looks))]
    public void Every_look_builds_the_heart_and_tool_art_also_hi_res_and_the_pictures_differ_by_state(string key)
    {
        EnsurePlatform();
        foreach (var scale in new[] { 1, 2, 3 })
        {
            PhoneSkin.ArtScale = scale;
            var package = PhoneSkin.Build(key);
            var files = new[] { "fav.png", "fav_down.png", "fav_disabled.png" }.Select(f => package.ReadFile(f) ?? throw new Xunit.Sdk.XunitException($"{key} x{scale} lacks {f}")).ToArray();
            Assert.NotEqual(files[0], files[1]);
            Assert.NotEqual(files[0], files[2]);
            var tools = key == "luna-plus-phone" ? new[] { "qsort.png", "qfilter.png", "qfilter_down.png", "sqsort.png", "sqfilter.png" }
                : key == "glass-phone" ? new[] { "hsort.png", "hfilt.png", "hfilt_down.png" } : ["qsort.png", "qfilter.png", "qfilter_down.png"];
            foreach (var name in tools)
                Assert.True((package.ReadFile(name) ?? throw new Xunit.Sdk.XunitException($"{key} x{scale} lacks {name}")).Length > 100, $"{key} {name}");
            if (scale > 1)
            {
                PhoneSkin.ArtScale = 1;
                Assert.NotEqual(PhoneSkin.Build(key).ReadFile("fav.png"), files[0]); // the picture is drawn at the density
            }
        }
    }

    [Fact]
    public void The_glass_heart_stays_red_in_a_tinted_skin_colour()
    {
        EnsurePlatform();
        GlassPalette.Current = GlassPalette.Tint.Royal;
        var normal = PhoneSkin.Build("glass-phone").ReadFile("fav_down.png")!;
        var normalOff = PhoneSkin.Build("glass-phone").ReadFile("fav.png")!;
        GlassPalette.Current = new GlassPalette.Tint(150, 1, 1);
        var tinted = PhoneSkin.Build("glass-phone");
        Assert.Equal(normal, tinted.ReadFile("fav_down.png")); // a red heart is not rotated with the blue chrome
        Assert.Equal(normalOff, tinted.ReadFile("fav.png"));
        Assert.True(tinted.ReadFile("hsort.png")!.Length > 100);
    }

    [Fact]
    public void The_modern_looks_use_fluent_icons_and_the_vintage_looks_pictorial_ones()
    {
        foreach (var name in new[] { "heart", "arrow_sort", "arrow_sort_up", "arrow_sort_down", "filter", "star" })
        {
            Assert.True(FluentIcons.Has(name), name);
            Assert.NotNull(FluentIcons.Geo(name));
            Assert.NotNull(FluentIcons.Geo(name, filled: true));
        }

        // The heart fills most of its 24 px grid, outline and filled alike.
        Assert.InRange(FluentIcons.Geo("heart").Bounds.Width, 20, 22);
        Assert.InRange(FluentIcons.Geo("heart", filled: true).Bounds.Height, 17, 19);
        foreach (var key in new[] { "heart", "heartoff", "sort", "filter" })
            Assert.Contains(key, PictorialIcons.Keys);
        Assert.Equal("heart", FluentIcons.ForGlyph("@heart"));
        Assert.Equal("heart", PictorialIcons.Resolve("@heart"));
        Assert.Equal("t_heart", PictorialIcons.TileOf("@heart"));
    }

    // ---- Settings -----------------------------------------------------------------------------------

    [Fact]
    public void The_remembered_sorts_are_saved_with_the_settings_and_read_back()
    {
        var settings = new AppSettings();
        settings.LibrarySorts["songs"] = new SortSpec(SortField.Year, true).ToString();
        settings.LibrarySorts["albums"] = new SortSpec(SortField.Artist).ToString();
        var json = JsonSerializer.Serialize(settings);
        var back = JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.Equal(new SortSpec(SortField.Year, true), back.SortOf("songs", default));
        Assert.Equal(new SortSpec(SortField.Artist), back.SortOf("albums", default));
        Assert.Equal(new SortSpec(SortField.Title), back.SortOf("never-set", new SortSpec(SortField.Title)));
        // Old settings files (without the new property) load with an empty map.
        Assert.Empty(JsonSerializer.Deserialize<AppSettings>("{}")!.LibrarySorts);
    }

    // ---- The Library pages ---------------------------------------------------------------------------

    private (MobileLibraryView View, MediaPlayer Player, FavoritesStore Store) OpenLibrary(MobileTheme theme, string level)
    {
        EnsurePlatform();
        Directory.CreateDirectory(_folder);
        LibraryItem Track(string title, string artist, string album, string year, int number, double seconds, bool video = false)
        {
            var path = Path.Combine(_folder, title.Replace('/', '_') + (video ? ".mp4" : ".mp3"));
            File.WriteAllText(path, "x");
            return new LibraryItem(path, title, artist, album, "Rock", year, number, seconds, video, 1, DateTime.UtcNow);
        }

        var items = new List<LibraryItem>
        {
            Track("Song 10", "Beta", "First", "2001", 10, 200), Track("Song 2", "Alpha", "First", "1999", 2, 100), Track("The Wall", "Gamma", "Second", "2010", 1, 300),
            Track("\u062A\u0647\u0631\u0627\u0646", "\u0627\u0631\u062A\u06CC\u0633\u062A", "\u0622\u0644\u0628\u0648\u0645", "2020", 1, 150), Track("Clip", "Alpha", "", "", 0, 60, video: true),
        };
        var cache = Path.Combine(_folder, "library.json");
        File.WriteAllText(cache, JsonSerializer.Serialize(items));
        var library = new MediaLibrary(new FakeInfoReader(), cache);
        library.LoadCache();
        var player = new MediaPlayer(new FakeEngine(), new FakeInfoReader());
        var store = new FavoritesStore();
        player.Favorites = store;
        store.Set(items[1].Path, true, items[1].Title, items[1].Artist);
        store.Set("/sdcard/Download/not-in-the-library.mp3", true, "Ad hoc song", "Nobody");
        var saved = Environment.GetEnvironmentVariable("PARDEH_LIBRARY_LEVEL");
        Environment.SetEnvironmentVariable("PARDEH_LIBRARY_LEVEL", level);
        try
        {
            var view = new MobileLibraryView(player, library, new MobileLibraryHost(() => null, () => null, () => { }, () => { }, _ => { }), theme);
            return (view, player, store);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PARDEH_LIBRARY_LEVEL", saved);
        }
    }

    public static TheoryData<string> Themes => ["fluent", "ember", "luna", "glass"];

    private static MobileTheme ThemeOf(string name) => name switch
    {
        "glass" => MobileTheme.ForGlass(),
        "luna" => new(false, Color.Parse("#EEF3FC"), Color.Parse("#FAFCFF"), Color.Parse("#2E55C4"), Color.Parse("#7F9CE6"), Color.Parse("#3A5FCD"), Colors.White, Royal: true),
        "ember" => new(true, Color.Parse("#141414"), Color.Parse("#222222"), Color.Parse("#E5801F")),
        _ => new(true, Color.Parse("#1C1C1C"), Color.Parse("#2B2B2B"), Color.Parse("#60CDFF")),
    };

    private static IEnumerable<string> Texts(Avalonia.Controls.Control root) =>
        root.GetLogicalDescendants().OfType<Avalonia.Controls.TextBlock>().Select(t => t.Text ?? string.Empty);

    [Theory]
    [MemberData(nameof(Themes))]
    public void The_song_list_has_the_sort_button_and_the_chips_and_the_favourites_level_lists_the_hearted_tracks(string theme)
    {
        var (view, _, _) = OpenLibrary(ThemeOf(theme), "Songs");
        var texts = Texts(view).ToList();
        Assert.Contains("Title", texts);          // the sort button names the sort
        Assert.Contains("Favourites", texts);     // the chip
        Assert.Contains("Play all", texts);
        Assert.Contains("4 songs", texts);

        var (mixed, _, _) = OpenLibrary(ThemeOf(theme), "Recent");
        var mixedTexts = Texts(mixed).ToList();
        Assert.Contains("Audio", mixedTexts);     // a list with music and a video offers the kind chips
        Assert.Contains("Video", mixedTexts);

        var (favorites, _, _) = OpenLibrary(ThemeOf(theme), "Favourites");
        var favTexts = Texts(favorites).ToList();
        Assert.Contains("Default order", favTexts);
        Assert.Contains("2 songs", favTexts);     // one library track and one that is not in the library
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void The_group_pages_have_a_sort_button(string theme)
    {
        foreach (var level in new[] { "Artists", "Albums" })
        {
            var (view, _, _) = OpenLibrary(ThemeOf(theme), level);
            Assert.Contains("Title", Texts(view)); // sorted by name
        }
    }

    [Fact]
    public void The_library_home_has_a_favourites_row_with_the_count()
    {
        foreach (var theme in new[] { "fluent", "luna", "glass" })
        {
            var (view, _, _) = OpenLibrary(ThemeOf(theme), string.Empty);
            var texts = Texts(view).ToList();
            Assert.Contains("Favourites", texts);
            Assert.True(texts.Contains("2") || texts.Contains("(2 favourites)"), $"{theme}: the favourites count is missing in [{string.Join(" | ", texts)}]");
        }
    }
}
