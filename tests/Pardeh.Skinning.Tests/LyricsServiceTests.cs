using Pardeh.Core.Library;
using Pardeh.Core.LyricsKit;
using Pardeh.Core.Playback;
using static Pardeh.Skinning.Tests.LyricsTestData;

namespace Pardeh.Skinning.Tests;

[Collection("MediaSources statics")]
public sealed class LyricsServiceTests : IDisposable
{
    private readonly string _data = NewTempFolder("pardeh-lyr-data-");
    private readonly string _music = NewTempFolder("pardeh-lyr-music-");
    private DateTime _now = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose()
    {
        Directory.Delete(_data, recursive: true);
        Directory.Delete(_music, recursive: true);
    }

    private LyricsService Service(ILyricsSource[] sources, ILyricsProvider[]? providers = null, LyricsOptions? options = null, bool store = true)
    {
        var current = options ?? new LyricsOptions();
        return new LyricsService(store ? new LyricsStore(Path.Combine(_data, "lyrics")) : null, sources, providers, () => current, () => _now);
    }

    private MediaItem Track(string title = "Song", string artist = "Artist") => Item(Path.Combine(_music, title + ".mp3"), title, artist);

    private static FakeLyricsSource Source(string name, Lyrics? lyrics) => new(name, _ => lyrics);

    [Fact]
    public async Task The_users_own_lyrics_win_over_everything()
    {
        var embedded = Source("embedded", Synced("tag"));
        var service = Service([embedded]);
        var item = Track();
        service.SaveOverride(item, Plain("mine"));

        var lyrics = await service.GetAsync(item);

        Assert.Equal("mine", lyrics!.Lines[0].Text);
        Assert.Equal(LyricsSources.Override, lyrics.Source);
        Assert.Equal(0, embedded.Calls);
    }

    [Fact]
    public async Task The_override_survives_a_restart_and_can_be_deleted()
    {
        var item = Track();
        Service([]).SaveOverride(item, Synced("a", "b"));

        var fresh = Service([Source("embedded", Synced("tag"))]);
        var lyrics = await fresh.GetAsync(item);
        Assert.Equal(["a", "b"], lyrics!.Lines.Select(l => l.Text));
        Assert.True(lyrics.Synced);

        Assert.True(fresh.DeleteOverride(item));
        Assert.Equal("tag", (await fresh.GetAsync(item))!.Lines[0].Text);
        Assert.False(fresh.DeleteOverride(item));
        Assert.Empty(Directory.GetFiles(Path.Combine(_data, "lyrics"), "*.lrc"));
    }

    [Fact]
    public async Task Embedded_lyrics_come_before_a_sidecar()
    {
        var service = Service([Source("embedded", Synced("tag")), Source("sidecar", Synced("file"))]);
        Assert.Equal("tag", (await service.GetAsync(Track()))!.Lines[0].Text);
    }

    [Fact]
    public async Task Synchronised_lyrics_beat_plain_ones_from_an_earlier_source()
    {
        var service = Service([Source("embedded", Plain("tag text")), Source("sidecar", Synced("file synced"))]);
        var lyrics = await service.GetAsync(Track());
        Assert.True(lyrics!.Synced);
        Assert.Equal("file synced", lyrics.Lines[0].Text);
    }

    [Fact]
    public async Task Between_plain_lyrics_the_first_source_wins()
    {
        var service = Service([Source("embedded", Plain("tag text")), Source("sidecar", Plain("file text"))]);
        Assert.Equal("tag text", (await service.GetAsync(Track()))!.Lines[0].Text);
    }

    [Fact]
    public async Task A_source_that_throws_or_has_nothing_is_skipped()
    {
        var service = Service([new FakeLyricsSource("bad", _ => throw new InvalidOperationException()), Source("empty", null),
            Source("blank", Plain("", "  ")), Source("good", Plain("ok"))]);
        Assert.Equal("ok", (await service.GetAsync(Track()))!.Lines[0].Text);
    }

    [Fact]
    public async Task Nothing_found_gives_null()
    {
        Assert.Null(await Service([Source("a", null)]).GetAsync(Track()));
    }

    [Fact]
    public async Task Turned_off_nothing_is_read()
    {
        var source = Source("a", Plain("x"));
        var service = Service([source], options: new LyricsOptions(Enabled: false));
        Assert.Null(await service.GetAsync(Track()));
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task Streams_have_no_lyrics_and_nothing_is_sent()
    {
        var provider = new FakeLyricsProvider(_ => Plain("x"));
        var source = Source("a", null);
        var service = Service([source], [provider], new LyricsOptions(Online: true));
        Assert.Null(await service.GetAsync(Item("http://radio.example/stream", "Live", "DJ")));
        Assert.Empty(provider.Queries);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task Online_is_never_used_when_the_setting_is_off()
    {
        var provider = new FakeLyricsProvider(_ => Synced("online"));
        var service = Service([Source("a", null)], [provider], new LyricsOptions(Online: false));
        Assert.Null(await service.GetAsync(Track()));
        Assert.Empty(provider.Queries);
    }

    [Fact]
    public async Task Online_lyrics_are_looked_up_saved_and_found_again_without_the_network()
    {
        var provider = new FakeLyricsProvider(_ => Synced("from the net", "second"));
        var online = new LyricsOptions(Online: true);
        var item = Track();

        var lyrics = await Service([Source("a", null)], [provider], online).GetAsync(item);

        Assert.Equal("from the net", lyrics!.Lines[0].Text);
        Assert.Equal(LyricsSources.Online, lyrics.Source);
        var query = Assert.Single(provider.Queries);
        Assert.Equal(new LyricsQuery("song", "artist", "album", 200), query);
        Assert.True(File.Exists(Directory.GetFiles(Path.Combine(_data, "lyrics", "online"), "*.lrc").Single()));

        // A new session: from the saved copy, no request.
        var again = await Service([Source("a", null)], [provider], online).GetAsync(item);
        Assert.Equal("from the net", again!.Lines[0].Text);
        Assert.Equal(LyricsSources.OnlineCache, again.Source);
        Assert.Single(provider.Queries);
    }

    [Fact]
    public async Task Fetched_copies_are_not_used_while_online_is_off()
    {
        var item = Track();
        await Service([Source("a", null)], [new FakeLyricsProvider(_ => Synced("net"))], new LyricsOptions(Online: true)).GetAsync(item);
        Assert.Null(await Service([Source("a", null)], null, new LyricsOptions(Online: false)).GetAsync(item));
    }

    [Fact]
    public async Task Online_results_are_not_saved_when_that_is_off()
    {
        var provider = new FakeLyricsProvider(_ => Synced("net"));
        var service = Service([Source("a", null)], [provider], new LyricsOptions(Online: true, SaveFetched: false));
        Assert.NotNull(await service.GetAsync(Track()));
        Assert.False(Directory.Exists(Path.Combine(_data, "lyrics", "online")));
    }

    [Fact]
    public async Task Local_lyrics_mean_no_lookup()
    {
        var provider = new FakeLyricsProvider(_ => Synced("net"));
        var service = Service([Source("a", Plain("local"))], [provider], new LyricsOptions(Online: true));
        Assert.Equal("local", (await service.GetAsync(Track()))!.Lines[0].Text);
        Assert.Empty(provider.Queries);
    }

    [Fact]
    public async Task Without_artist_or_title_nothing_is_sent()
    {
        var provider = new FakeLyricsProvider(_ => Synced("net"));
        var service = Service([Source("a", null)], [provider], new LyricsOptions(Online: true));
        Assert.Null(await service.GetAsync(Item(Path.Combine(_music, "x.mp3"), "Song", artist: "")));
        Assert.Empty(provider.Queries);
    }

    [Fact]
    public async Task A_miss_is_remembered_for_seven_days()
    {
        var provider = new FakeLyricsProvider(_ => null);
        var online = new LyricsOptions(Online: true);
        var item = Track();

        Assert.Null(await Service([Source("a", null)], [provider], online).GetAsync(item));
        Assert.Single(provider.Queries);

        _now += TimeSpan.FromDays(6.9);
        Assert.Null(await Service([Source("a", null)], [provider], online).GetAsync(item));
        Assert.Single(provider.Queries);

        _now += TimeSpan.FromDays(0.2);
        Assert.Null(await Service([Source("a", null)], [provider], online).GetAsync(item));
        Assert.Equal(2, provider.Queries.Count);
    }

    [Fact]
    public async Task A_miss_mark_keeps_only_a_hash_and_a_date()
    {
        await Service([Source("a", null)], [new FakeLyricsProvider(_ => null)], new LyricsOptions(Online: true)).GetAsync(Track("Secret Title", "Secret Artist"));
        var index = File.ReadAllText(Path.Combine(_data, "lyrics", "index.json"));
        Assert.DoesNotContain("Secret", index, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NotFoundUtc", index);
    }

    [Fact]
    public async Task A_failed_request_is_not_remembered_as_a_miss()
    {
        var calls = 0;
        var provider = new FakeLyricsProvider(_ => ++calls == 1 ? throw new HttpRequestException("down") : Synced("later"));
        var online = new LyricsOptions(Online: true);
        var item = Track();

        Assert.Null(await Service([Source("a", null)], [provider], online).GetAsync(item));
        Assert.Equal("later", (await Service([Source("a", null)], [provider], online).GetAsync(item))!.Lines[0].Text);
        Assert.Equal(2, provider.Queries.Count);
    }

    [Fact]
    public async Task The_next_provider_is_tried_when_one_has_nothing()
    {
        var first = new FakeLyricsProvider(_ => null);
        var second = new FakeLyricsProvider(_ => Plain("second"));
        var service = Service([Source("a", null)], [first, second], new LyricsOptions(Online: true));
        Assert.Equal("second", (await service.GetAsync(Track()))!.Lines[0].Text);
        Assert.Single(first.Queries);
        Assert.Single(second.Queries);
    }

    [Fact]
    public async Task Results_are_remembered_in_memory_until_invalidated()
    {
        var source = Source("a", Plain("x"));
        var service = Service([source]);
        var item = Track();
        var first = await service.GetAsync(item);
        Assert.Same(first, await service.GetAsync(item));
        Assert.Equal(1, source.Calls);

        service.Invalidate(item);
        await service.GetAsync(item);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task The_memory_key_is_the_identity_key_when_the_head_has_one()
    {
        var old = MediaSources.IdentityKey;
        try
        {
            MediaSources.IdentityKey = s => s.ToUpperInvariant().Replace("ALIAS", "REAL");
            var source = Source("a", Plain("x"));
            var service = Service([source]);
            await service.GetAsync(Item("/alias/a.mp3"));
            await service.GetAsync(Item("/REAL/a.mp3"));
            Assert.Equal(1, source.Calls);
        }
        finally
        {
            MediaSources.IdentityKey = old;
        }
    }

    [Fact]
    public async Task A_remembered_miss_is_looked_up_again_when_online_is_turned_on()
    {
        var provider = new FakeLyricsProvider(_ => Synced("net"));
        var options = new LyricsOptions(Online: false);
        var service = new LyricsService(new LyricsStore(Path.Combine(_data, "lyrics")), [Source("a", null)], [provider], () => options, () => _now);
        var item = Track();
        Assert.Null(await service.GetAsync(item));
        options = new LyricsOptions(Online: true);
        Assert.NotNull(await service.GetAsync(item));
    }

    [Fact]
    public async Task Cancelling_stops_the_lookup_and_remembers_nothing()
    {
        var gate = new TaskCompletionSource();
        var provider = new FakeLyricsProvider(_ => Synced("net")) { Gate = gate.Task };
        var service = Service([Source("a", null)], [provider], new LyricsOptions(Online: true));
        var item = Track();
        using var cancel = new CancellationTokenSource();

        var pending = service.GetAsync(item, cancel.Token);
        while (provider.Queries.Count == 0)
            await Task.Delay(5);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);

        gate.SetResult();
        Assert.NotNull(await service.GetAsync(item)); // asked again, not a remembered null
        Assert.Equal(2, provider.Queries.Count);
        Assert.False(File.Exists(Path.Combine(_data, "lyrics", "index.json")) && File.ReadAllText(Path.Combine(_data, "lyrics", "index.json")).Contains("NotFoundUtc"));
    }

    [Fact]
    public async Task An_already_cancelled_request_throws_without_reading()
    {
        var source = Source("a", Plain("x"));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service([source]).GetAsync(Track(), cancel.Token));
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task The_offset_is_saved_per_track_and_applied_on_load()
    {
        var item = Track();
        var service = Service([Source("a", Synced("a", "b"))]);
        var lyrics = (await service.GetAsync(item))!;
        Assert.Equal(0, lyrics.OffsetMs);

        service.SetOffset(item, -1500);
        Assert.Equal(-1500, lyrics.OffsetMs); // the held lyrics follow at once

        var fresh = Service([Source("a", Synced("a", "b"))]);
        Assert.Equal(-1500, (await fresh.GetAsync(item))!.OffsetMs);

        fresh.SetOffset(item, 0);
        Assert.Equal(0, (await Service([Source("a", Synced("a"))]).GetAsync(item))!.OffsetMs);
        Assert.DoesNotContain("OffsetMs", File.ReadAllText(Path.Combine(_data, "lyrics", "index.json"))); // a zero offset leaves nothing behind
    }

    [Fact]
    public async Task Saving_lyrics_keeps_their_offset_and_the_file_has_no_offset_baked_in()
    {
        var item = Track();
        var lyrics = Synced("a", "b");
        lyrics.OffsetMs = 700;
        var service = Service([]);
        service.SaveOverride(item, lyrics);

        var file = Directory.GetFiles(Path.Combine(_data, "lyrics"), "*.lrc").Single();
        Assert.StartsWith("[00:05.00]a", File.ReadAllText(file));
        Assert.Equal(700, (await Service([]).GetAsync(item))!.OffsetMs);
    }

    [Fact]
    public async Task Nothing_is_ever_written_next_to_the_music()
    {
        var item = Track();
        File.WriteAllText(item.Source, "x");
        var service = Service([Source("a", null)], [new FakeLyricsProvider(_ => Synced("net"))], new LyricsOptions(Online: true));
        await service.GetAsync(item);
        service.SaveOverride(item, Plain("mine"));
        service.SetOffset(item, 300);
        service.DeleteOverride(item);

        Assert.Equal([item.Source], Directory.GetFileSystemEntries(_music));
    }

    [Fact]
    public async Task Without_a_store_everything_still_works_in_memory()
    {
        var service = Service([Source("a", Plain("x"))], store: false);
        var item = Track();
        Assert.NotNull(await service.GetAsync(item));
        service.SaveOverride(item, Plain("y"));
        service.SetOffset(item, 5);
        Assert.False(service.DeleteOverride(item));
    }

    [Fact]
    public void The_track_hash_is_stable_and_tolerant_of_spelling()
    {
        var a = LyricsTrackKey.From(Item("/a.mp3", "Hello  World", "Ali", "Disc", 200));
        var b = LyricsTrackKey.From(Item("/elsewhere/b.flac", " hello world ", "ALI", "disc", 200));
        Assert.Equal(a.Hash, b.Hash);
        Assert.Equal(32, a.Hash.Length);
        Assert.Matches("^[0-9a-f]{32}$", a.Hash);
        Assert.NotEqual(a.Hash, LyricsTrackKey.From(Item("/a.mp3", "Hello World", "Ali", "Disc", 201)).Hash);
        Assert.NotEqual(a.Hash, LyricsTrackKey.From(Item("/a.mp3", "Hello World", "Ali", "Other", 200)).Hash);

        // Arabic and Persian letter variants of the same title are one track.
        var arabic = LyricsTrackKey.From(Item("/p.mp3", "علي كتاب", "x"));
        var persian = LyricsTrackKey.From(Item("/p.mp3", "علی کتاب", "x"));
        Assert.Equal(persian.Hash, arabic.Hash);

        // Known value, so that a change of the recipe (which would orphan saved lyrics) is noticed.
        Assert.Equal("1ad34366a81d6e6632daf9d2f64b93ea", new LyricsTrackKey("a", "b", "c", 1).Hash);
    }

    [Fact]
    public async Task A_damaged_index_is_ignored_and_the_files_stay()
    {
        var item = Track();
        Service([]).SaveOverride(item, Plain("mine"));
        File.WriteAllText(Path.Combine(_data, "lyrics", "index.json"), "{ not json");
        Assert.Null(await Service([]).GetAsync(item)); // unknown without the index
        Service([]).SetOffset(item, 10); // and the store repairs itself
        Assert.Contains("OffsetMs", File.ReadAllText(Path.Combine(_data, "lyrics", "index.json")));
    }

    [Fact]
    public async Task Plain_override_is_saved_as_txt_and_a_switch_to_synced_removes_the_old_file()
    {
        var item = Track();
        var service = Service([]);
        service.SaveOverride(item, Plain("a", "b"));
        Assert.Single(Directory.GetFiles(Path.Combine(_data, "lyrics"), "*.txt"));
        service.SaveOverride(item, Synced("a", "b"));
        Assert.Empty(Directory.GetFiles(Path.Combine(_data, "lyrics"), "*.txt"));
        Assert.Single(Directory.GetFiles(Path.Combine(_data, "lyrics"), "*.lrc"));
        Assert.True((await Service([]).GetAsync(item))!.Synced);
    }

    [Fact]
    public async Task Fetched_lyrics_can_be_cleared_without_touching_the_users_own()
    {
        var item = Track();
        var store = new LyricsStore(Path.Combine(_data, "lyrics"));
        var service = new LyricsService(store, [Source("a", null)], [new FakeLyricsProvider(_ => Synced("net"))], () => new LyricsOptions(Online: true), () => _now);
        await service.GetAsync(item);
        var other = Track("Other");
        service.SaveOverride(other, Plain("mine"));

        store.ClearFetched();

        Assert.Null(store.GetFetched(LyricsTrackKey.From(item)));
        Assert.NotNull(store.GetOverride(LyricsTrackKey.From(other)));
    }
}
