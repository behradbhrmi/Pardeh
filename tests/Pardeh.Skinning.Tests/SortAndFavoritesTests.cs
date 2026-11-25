using System.Globalization;
using Pardeh.Core.Library;
using Pardeh.Core.Playback;

namespace Pardeh.Skinning.Tests;

/// <summary>Sorting (library lists and the queue), the queue operations and the favourites store.</summary>
public sealed class SortAndFavoritesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"pardeh-fav-{Guid.NewGuid():N}");
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly Func<string, string?>? _identity = MediaSources.IdentityKey;

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        MediaSources.IdentityKey = _identity;
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static LibraryItem Track(string title, string artist = "", string album = "", string year = "", int track = 0, double seconds = 0, int rating = 0, int plays = 0, DateTime? modified = null) =>
        new($"/music/{title}.mp3", title, artist, album, string.Empty, year, track, seconds, false, 1, modified ?? DateTime.UtcNow) { Rating = rating, PlayCount = plays };

    private static string[] Titles(IEnumerable<LibraryItem> items) => items.Select(i => i.Title).ToArray();

    // ---- Comparers ------------------------------------------------------------------------------

    [Fact]
    public void Numbers_in_names_sort_by_value()
    {
        var sorted = TrackSort.Sort([Track("Song 10"), Track("Song 2"), Track("Song 1"), Track("Song 20")], new SortSpec(SortField.Title));
        Assert.Equal(["Song 1", "Song 2", "Song 10", "Song 20"], Titles(sorted));
    }

    [Fact]
    public void Leading_the_and_bracket_tags_are_ignored()
    {
        var sorted = TrackSort.Sort([Track("The Wall"), Track("[ MusicViral.ir ] Azure"), Track("Beta"), Track("(Live) Cobalt"), Track("The")], new SortSpec(SortField.Title));
        Assert.Equal(["[ MusicViral.ir ] Azure", "Beta", "(Live) Cobalt", "The", "The Wall"], Titles(sorted));
    }

    [Fact]
    public void Persian_letters_sort_in_alphabet_order()
    {
        // alef, be, pe, te, jim, che, he, dal, re, ze, zhe, sin, kaf, gaf, mim, nun, he, ye
        string[] alphabet = ["ان", "بهار", "پاییز", "تهران", "جان", "چشم", "حال", "دل", "راه", "زمستان", "ژاله", "سال", "کوه", "گل", "ماه", "نام", "هوش", "یار"];
        var shuffled = alphabet.OrderBy(s => s.GetHashCode()).Reverse().ToArray();
        var sorted = TrackSort.Sort(shuffled.Select(t => Track(t)), new SortSpec(SortField.Title));
        Assert.Equal(alphabet, Titles(sorted));
    }

    [Fact]
    public void Persian_titles_with_digits_and_arabic_letter_forms()
    {
        // Persian digits count as numbers; Arabic yeh / kaf equal the Persian ones; Latin comes before Persian.
        var a = TrackSort.CompareText("آهنگ ۲", "آهنگ ۱۰");
        Assert.True(a < 0);
        Assert.Equal(0, TrackSort.CompareText("کار", "كار"));
        Assert.Equal(0, TrackSort.CompareText("دیر", "دير"));
        Assert.True(TrackSort.CompareText("Zebra", "الف") < 0);
        Assert.True(TrackSort.CompareText("7 Song", "A Song") < 0);
    }

    [Fact]
    public void Sort_is_stable_and_empty_values_go_last_in_both_directions()
    {
        var items = new[] { Track("1", "B"), Track("2", ""), Track("3", "A"), Track("4", "B"), Track("5", "A"), Track("6", "") };
        Assert.Equal(["3", "5", "1", "4", "2", "6"], Titles(TrackSort.Sort(items, new SortSpec(SortField.Artist))));
        Assert.Equal(["1", "4", "3", "5", "2", "6"], Titles(TrackSort.Sort(items, new SortSpec(SortField.Artist, Descending: true))));
    }

    [Fact]
    public void Numeric_fields_sort_by_value_and_zero_is_unknown()
    {
        var items = new[] { Track("c", year: "2019", seconds: 300, track: 3, rating: 2, plays: 9), Track("a", year: "1999", seconds: 60, track: 1, rating: 5, plays: 1), Track("x"), Track("b", year: "2005-05-01", seconds: 120, track: 2, rating: 4, plays: 4) };
        Assert.Equal(["a", "b", "c", "x"], Titles(TrackSort.Sort(items, new SortSpec(SortField.Year))));
        Assert.Equal(["c", "b", "a", "x"], Titles(TrackSort.Sort(items, new SortSpec(SortField.Duration, true))));
        Assert.Equal(["a", "b", "c", "x"], Titles(TrackSort.Sort(items, new SortSpec(SortField.TrackNumber))));
        Assert.Equal(["a", "b", "c", "x"], Titles(TrackSort.Sort(items, new SortSpec(SortField.Rating, true))));
        Assert.Equal(["c", "b", "a", "x"], Titles(TrackSort.Sort(items, new SortSpec(SortField.PlayCount, true))));
    }

    [Fact]
    public void Date_added_uses_the_modified_time()
    {
        var now = DateTime.UtcNow;
        var items = new[] { Track("old", modified: now.AddDays(-10)), Track("new", modified: now), Track("mid", modified: now.AddDays(-3)) };
        Assert.Equal(["old", "mid", "new"], Titles(TrackSort.Sort(items, new SortSpec(SortField.DateAdded))));
        Assert.Equal(["new", "mid", "old"], Titles(TrackSort.Sort(items, new SortSpec(SortField.DateAdded, true))));
    }

    [Fact]
    public void Default_order_keeps_the_list_and_descending_reverses_it()
    {
        var items = new[] { Track("z"), Track("a"), Track("m") };
        Assert.Equal(["z", "a", "m"], Titles(TrackSort.Sort(items, new SortSpec(SortField.Default))));
        Assert.Equal(["m", "a", "z"], Titles(TrackSort.Sort(items, new SortSpec(SortField.Default, true))));
    }

    [Theory]
    [InlineData("fa-IR")]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    public void Sorting_does_not_depend_on_the_phone_language(string culture)
    {
        // Turkish dotless i and German sharp s must not change the order, nor the Persian settings format.
        var items = new[] { Track("Istanbul 10"), Track("istanbul 2"), Track("Äpfel"), Track("Zebra"), Track("apple") };
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        var expected = Titles(TrackSort.Sort(items, new SortSpec(SortField.Title)));
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        Assert.Equal(expected, Titles(TrackSort.Sort(items, new SortSpec(SortField.Title))));
        Assert.Equal("Title:desc", new SortSpec(SortField.Title, true).ToString());
        Assert.Equal(new SortSpec(SortField.Year, true), SortSpec.Parse("Year:desc", default));
    }

    [Fact]
    public void Sort_spec_parses_with_a_fallback()
    {
        var fallback = new SortSpec(SortField.Artist);
        Assert.Equal(fallback, SortSpec.Parse(null, fallback));
        Assert.Equal(fallback, SortSpec.Parse("Nonsense:asc", fallback));
        Assert.Equal(new SortSpec(SortField.DateAdded, true), SortSpec.Parse("dateadded:DESC", fallback));
    }

    // ---- Queue ----------------------------------------------------------------------------------

    private static Playlist Queue(params (string Name, string Artist, double Seconds)[] tracks)
    {
        var list = new Playlist("Now Playing");
        foreach (var (name, artist, seconds) in tracks)
        {
            var item = new MediaItem($"/music/{name}.mp3") { Name = name, Duration = TimeSpan.FromSeconds(seconds) };
            if (artist.Length > 0)
                item.Attributes["Author"] = artist;
            list.Add(item);
        }

        return list;
    }

    private static string[] Names(Playlist list) => list.Items.Select(i => i.Name).ToArray();

    [Fact]
    public void Queue_sort_keeps_the_playing_item_current()
    {
        var list = Queue(("Charlie", "Z", 30), ("alpha", "Y", 10), ("Bravo", "X", 20));
        list.SetCurrent(0); // Charlie
        var changed = 0;
        list.Changed += (_, _) => changed++;
        list.Sort(new SortSpec(SortField.Title));
        Assert.Equal(["alpha", "Bravo", "Charlie"], Names(list));
        Assert.Equal("Charlie", list.Current?.Name);
        Assert.Equal(2, list.CurrentIndex);
        Assert.Equal(1, changed);
        list.Sort(new SortSpec(SortField.Artist));
        Assert.Equal(["Bravo", "alpha", "Charlie"], Names(list));
        Assert.Equal("Charlie", list.Current?.Name);
        list.Sort(new SortSpec(SortField.Duration, true));
        Assert.Equal(["Charlie", "Bravo", "alpha"], Names(list));
        Assert.Equal(0, list.CurrentIndex);
    }

    [Fact]
    public void Queue_restores_the_order_things_were_added_in()
    {
        var list = Queue(("c", "", 1), ("a", "", 2), ("b", "", 3));
        list.SetCurrent(1);
        list.Sort(new SortSpec(SortField.Title));
        list.Add(new MediaItem("/music/d.mp3") { Name = "d" });
        list.Sort(new SortSpec(SortField.Default));
        Assert.Equal(["c", "a", "b", "d"], Names(list));
        Assert.Equal("a", list.Current?.Name);
        list.Sort(new SortSpec(SortField.Default, true));
        Assert.Equal(["d", "b", "a", "c"], Names(list));
        Assert.Equal("a", list.Current?.Name);
    }

    [Fact]
    public void Queue_reverse_keeps_the_current_item()
    {
        var list = Queue(("a", "", 1), ("b", "", 1), ("c", "", 1), ("d", "", 1));
        list.SetCurrent(1);
        list.Reverse();
        Assert.Equal(["d", "c", "b", "a"], Names(list));
        Assert.Equal("b", list.Current?.Name);
        Assert.Equal(2, list.CurrentIndex);
    }

    [Fact]
    public void Queue_randomize_puts_the_current_item_first_and_keeps_every_item()
    {
        var list = Queue(Enumerable.Range(0, 20).Select(i => ($"t{i:00}", "", 1.0)).ToArray());
        list.SetCurrent(7);
        var before = Names(list).Order().ToArray();
        list.Randomize();
        Assert.Equal(before, Names(list).Order().ToArray());
        Assert.Equal("t07", list.Current?.Name);
        Assert.Equal(0, list.CurrentIndex);
        Assert.NotEqual(Enumerable.Range(0, 20).Select(i => $"t{i:00}"), Names(list)); // 1 in 20! of staying in order
    }

    [Fact]
    public void Queue_with_nothing_current_can_be_sorted_and_shuffled()
    {
        var list = Queue(("b", "", 1), ("a", "", 1));
        list.Sort(new SortSpec(SortField.Title));
        Assert.Equal(-1, list.CurrentIndex);
        list.Randomize();
        Assert.Equal(-1, list.CurrentIndex);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void Queue_next_follows_the_new_order_and_shuffle_stays_valid()
    {
        var list = Queue(("c", "", 1), ("a", "", 1), ("b", "", 1));
        list.SetCurrent(1); // a
        list.Sort(new SortSpec(SortField.Title));
        Assert.True(list.MoveNext());
        Assert.Equal("b", list.Current?.Name);
        list.Shuffle = true;
        list.Reverse();
        var seen = new HashSet<string> { list.Current!.Name };
        while (list.MoveNext())
            seen.Add(list.Current!.Name);
        Assert.Equal(3, seen.Count); // shuffle order rebuilt: every item is still reachable
    }

    [Fact]
    public void Queue_removes_duplicates_keeping_the_playing_one()
    {
        var list = Queue(("a", "", 1), ("b", "", 1), ("a", "", 1), ("c", "", 1), ("b", "", 1));
        list.SetCurrent(2); // the second "a" is playing
        var playing = list.Current;
        Assert.Equal(2, list.RemoveDuplicates());
        Assert.Equal(["b", "a", "c"], Names(list)); // the first "a" went, the playing one stayed
        Assert.Same(playing, list.Current);
        Assert.Equal(1, list.CurrentIndex);
        Assert.Equal(0, list.RemoveDuplicates());
    }

    [Fact]
    public void Queue_duplicates_fold_a_path_and_a_tree_uri_when_the_platform_knows_they_are_one_file()
    {
        MediaSources.IdentityKey = s => s.StartsWith("content://", StringComparison.Ordinal) ? "/storage/emulated/0/Music/x.mp3" : s.Replace("/sdcard/", "/storage/emulated/0/", StringComparison.Ordinal);
        var list = new Playlist("q");
        list.Add(new MediaItem("/sdcard/Music/x.mp3"));
        list.Add(new MediaItem("content://tree/Music/x.mp3"));
        list.Add(new MediaItem("/sdcard/Music/y.mp3"));
        Assert.Equal(1, list.RemoveDuplicates());
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void Player_sort_does_not_restart_playback()
    {
        var engine = new FakeEngine();
        using var player = new MediaPlayer(engine, new FakeInfoReader());
        player.OpenItems(new[] { "b", "a", "c" }.Select(n => new MediaItem($"/music/{n}.mp3") { Name = n }), 0);
        var media = player.CurrentMedia;
        var playlistChanges = 0;
        player.PlaylistChanged += (_, _) => playlistChanges++;
        player.CurrentPlaylist.Sort(new SortSpec(SortField.Title));
        Assert.Same(media, player.CurrentMedia);
        Assert.Equal(PlayState.Playing, player.PlayState);
        Assert.Equal(1, player.CurrentPlaylist.CurrentIndex);
        Assert.Equal(1, playlistChanges);
    }

    // ---- Favourites -----------------------------------------------------------------------------

    [Fact]
    public void Favorites_toggle_persist_and_reload()
    {
        var file = Path.Combine(_folder, "favorites.json");
        var store = new FavoritesStore(file);
        var events = 0;
        store.Changed += (_, _) => events++;
        Assert.True(store.Set("/music/a.mp3", true, "Song A", "Artist"));
        Assert.False(store.Set("/music/a.mp3", true));
        Assert.True(store.Toggle("/music/b.mp3"));
        Assert.Equal(2, events);
        Assert.True(File.Exists(file));
        Assert.Contains("\"favorites\"", File.ReadAllText(file));

        var again = new FavoritesStore(file);
        Assert.Equal(2, again.Count);
        Assert.True(again.IsFavorite("/music/a.mp3"));
        Assert.Equal("Song A", again.Entries.First(e => e.Path == "/music/a.mp3").Title);
        Assert.False(again.Toggle("/music/b.mp3"));
        Assert.Equal(1, new FavoritesStore(file).Count);
        Assert.False(again.Set("/music/zzz.mp3", false));
    }

    [Fact]
    public void Favorites_treat_a_path_and_a_tree_uri_of_one_file_as_one_favourite()
    {
        MediaSources.IdentityKey = s => s.StartsWith("content://", StringComparison.Ordinal) ? "/storage/emulated/0/Music/x.mp3" : s.Replace("/sdcard/", "/storage/emulated/0/", StringComparison.Ordinal);
        var store = new FavoritesStore(Path.Combine(_folder, "f.json"));
        store.Set("/sdcard/Music/x.mp3", true, "X");
        Assert.True(store.IsFavorite("content://com.android.externalstorage.documents/tree/primary%3AMusic/document/primary%3AMusic%2Fx.mp3"));
        Assert.False(store.Set("content://tree/doc/x", true)); // already a favourite
        Assert.Equal(1, store.Count);
        Assert.True(store.Set("content://tree/doc/x", false)); // un-hearting through the other route removes it
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Favorites_work_for_tracks_that_are_not_in_the_library_and_survive_a_damaged_file()
    {
        Directory.CreateDirectory(_folder);
        var file = Path.Combine(_folder, "broken.json");
        File.WriteAllText(file, "{ not json");
        var store = new FavoritesStore(file);
        Assert.Equal(0, store.Count);
        store.Set("/sdcard/Download/adhoc.flac", true, "Ad hoc", "Nobody");
        Assert.Equal(1, new FavoritesStore(file).Count);
        Assert.False(store.IsFavorite(null));
        Assert.False(store.IsFavorite(""));
        Assert.False(store.Set("", true));
    }

    [Fact]
    public void Favorites_are_thread_safe()
    {
        var store = new FavoritesStore(Path.Combine(_folder, "threads.json"));
        Parallel.For(0, 200, i => store.Set($"/music/{i % 50}.mp3", i % 3 != 0, "t"));
        Assert.True(store.Count <= 50);
        var reloaded = new FavoritesStore(Path.Combine(_folder, "threads.json"));
        Assert.Equal(store.Count, reloaded.Count);
    }

    [Fact]
    public void Favorites_json_is_culture_safe()
    {
        var file = Path.Combine(_folder, "culture.json");
        foreach (var culture in new[] { "fa-IR", "de-DE", "ar-SA" })
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var store = new FavoritesStore(file);
            store.Set($"/music/{culture}.mp3", true, "تهران", "ارتیست");
            Assert.True(new FavoritesStore(file).IsFavorite($"/music/{culture}.mp3"));
        }

        CultureInfo.CurrentCulture = _culture;
        var final = new FavoritesStore(file);
        Assert.Equal(3, final.Count);
        Assert.Equal("تهران", final.Entries[0].Title);
    }

    [Fact]
    public void Player_favorite_follows_the_store_and_raises_events()
    {
        using var player = new MediaPlayer(new FakeEngine(), new FakeInfoReader());
        var store = new FavoritesStore();
        player.Favorites = store;
        var events = 0;
        player.FavoritesChanged += (_, _) => events++;
        player.OpenItems([new MediaItem("/music/q.mp3") { Name = "Q" }], 0);
        Assert.False(player.CurrentIsFavorite);
        Assert.True(player.ToggleFavorite());
        Assert.True(player.CurrentIsFavorite);
        Assert.True(store.IsFavorite("/music/q.mp3"));
        Assert.Equal(1, events);
        player.SetFavorite(false);
        Assert.False(player.CurrentIsFavorite);
        Assert.Equal(2, events);
    }
}
