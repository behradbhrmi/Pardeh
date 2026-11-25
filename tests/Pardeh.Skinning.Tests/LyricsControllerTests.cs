using System.Text.Json;
using Pardeh.Core.LyricsKit;
using Pardeh.Core.Playback;
using Pardeh.UI;
using Pardeh.UI.LyricsKit;
using static Pardeh.Skinning.Tests.LyricsTestData;

namespace Pardeh.Skinning.Tests;

internal sealed class FakePlayback : ILyricsPlayback
{
    public MediaItem? CurrentMedia { get; private set; }

    /// <summary>The fake clock: tests move it.</summary>
    public TimeSpan Position { get; set; }

    public List<TimeSpan> Seeks { get; } = [];

    public event EventHandler? CurrentMediaChanged;

    public void Seek(TimeSpan position)
    {
        Seeks.Add(position);
        Position = position;
    }

    public void Play(MediaItem? item)
    {
        CurrentMedia = item;
        Position = TimeSpan.Zero;
        CurrentMediaChanged?.Invoke(this, EventArgs.Empty);
    }
}

[Collection("MediaSources statics")]
public sealed class LyricsControllerTests : IDisposable
{
    private readonly string _data = NewTempFolder("pardeh-lyr-ctl-");
    private readonly FakePlayback _playback = new();
    private readonly List<LyricsChangeKind> _events = [];
    private readonly List<LyricsController> _controllers = [];
    private bool _enabled = true;

    public void Dispose()
    {
        foreach (var controller in _controllers)
            controller.Dispose();
        Directory.Delete(_data, recursive: true);
    }

    private static MediaItem Track(string name) => Item("/music/" + name + ".mp3", name);

    private LyricsController Create(Func<MediaItem, Lyrics?> read, Action<Action>? post = null)
    {
        var service = new LyricsService(new LyricsStore(Path.Combine(_data, "lyrics")), [new FakeLyricsSource("fake", read)]);
        var controller = new LyricsController(_playback, service, () => _enabled, post);
        controller.Changed += (_, e) =>
        {
            lock (_events)
                _events.Add(e.Kind);
        };
        _controllers.Add(controller);
        return controller;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 1000 && !condition(); i++)
            await Task.Delay(5);
        Assert.True(condition(), "timed out");
    }

    private static Lyrics Four() => LrcParser.Parse("[00:10.00]a\n[00:20.00]b\n[00:30.00]c\n[00:40.00]d")!;

    [Fact]
    public async Task Loads_the_lyrics_of_a_new_track_in_the_background()
    {
        // The source waits until "loading" has been seen: an instant one could finish before the check.
        using var seen = new ManualResetEventSlim();
        var controller = Create(_ =>
        {
            seen.Wait(TimeSpan.FromSeconds(10));
            return Four();
        });
        Assert.Null(controller.Current);

        _playback.Play(Track("one"));
        Assert.True(controller.IsLoading);
        seen.Set();
        // The event is raised just after the lyrics appear: wait for both (waiting for the lyrics alone raced).
        await WaitUntil(() => controller.Current is not null && _events.Contains(LyricsChangeKind.Lyrics));

        Assert.False(controller.IsLoading);
        Assert.Equal(4, controller.Current!.Lines.Count);
        Assert.Equal(-1, controller.CurrentIndex);
        Assert.Contains(LyricsChangeKind.Lyrics, _events);
    }

    [Fact]
    public async Task The_index_follows_the_position_and_events_fire_only_on_a_new_line()
    {
        var controller = Create(_ => Four());
        _playback.Play(Track("one"));
        await WaitUntil(() => controller.Current is not null);
        _events.Clear();

        var seen = new List<int>();
        foreach (var seconds in new[] { 1.0, 9.9, 10.0, 15, 19.99, 20, 25, 45, 3 })
        {
            _playback.Position = TimeSpan.FromSeconds(seconds);
            controller.Tick();
            controller.Tick(); // a second tick at the same position changes nothing
            seen.Add(controller.CurrentIndex);
        }

        Assert.Equal([-1, -1, 0, 0, 0, 1, 1, 3, -1], seen);
        Assert.Equal(4, _events.Count(e => e == LyricsChangeKind.Index)); // -1 to 0, 0 to 1, 1 to 3, 3 to -1
    }

    [Fact]
    public async Task A_late_start_finds_the_right_line_at_once()
    {
        var release = new TaskCompletionSource();
        var controller = Create(_ =>
        {
            release.Task.Wait(TimeSpan.FromSeconds(10));
            return Four();
        });
        _playback.Play(Track("one"));
        _playback.Position = TimeSpan.FromSeconds(31); // the track plays on while the lyrics are still loading
        release.SetResult();
        await WaitUntil(() => controller.Current is not null);
        Assert.Equal(2, controller.CurrentIndex);
    }

    [Fact]
    public async Task Unsynchronised_lyrics_never_highlight_a_line()
    {
        var controller = Create(_ => LrcParser.Parse("just text\nmore text"));
        _playback.Play(Track("one"));
        await WaitUntil(() => controller.Current is not null);
        _playback.Position = TimeSpan.FromMinutes(1);
        controller.Tick();
        Assert.False(controller.Current!.Synced);
        Assert.Equal(-1, controller.CurrentIndex);
        Assert.False(controller.SeekToLine(0));
    }

    [Fact]
    public async Task Seeking_to_a_line_asks_the_player_to_go_there()
    {
        var controller = Create(_ => Four());
        _playback.Play(Track("one"));
        await WaitUntil(() => controller.Current is not null);

        Assert.True(controller.SeekToLine(2));
        Assert.InRange(_playback.Seeks.Single().TotalSeconds, 30.0, 30.1);
        controller.Tick();
        Assert.Equal(2, controller.CurrentIndex);

        Assert.False(controller.SeekToLine(-1));
        Assert.False(controller.SeekToLine(4));
        Assert.Single(_playback.Seeks);
    }

    [Fact]
    public async Task The_offset_moves_the_lines_and_is_saved_for_the_track()
    {
        var controller = Create(_ => Four());
        var item = Track("one");
        _playback.Play(item);
        await WaitUntil(() => controller.Current is not null);

        _playback.Position = TimeSpan.FromSeconds(19);
        controller.Tick();
        Assert.Equal(0, controller.CurrentIndex);

        _events.Clear();
        controller.NudgeOffset(-1500); // lyrics 1.5 s sooner: b is due at 18.5
        Assert.Equal(-1500, controller.OffsetMs);
        Assert.Equal(1, controller.CurrentIndex);
        Assert.Contains(LyricsChangeKind.Offset, _events);

        // Saved (in the background) and used by the next load of the same track.
        await WaitUntil(() => new LyricsStore(Path.Combine(_data, "lyrics")).GetOffset(LyricsTrackKey.From(item)) == -1500);

        controller.SetOffset(5_000_000); // clamped
        Assert.Equal(600_000, controller.OffsetMs);
    }

    [Fact]
    public async Task A_slow_load_for_an_old_track_is_dropped()
    {
        var release = new TaskCompletionSource();
        var controller = Create(item =>
        {
            if (item.Name == "slow")
                release.Task.Wait(TimeSpan.FromSeconds(10));
            return item.Name == "slow" ? LrcParser.Parse("[00:01.00]slow lyrics") : LrcParser.Parse("[00:01.00]fast lyrics");
        });

        _playback.Play(Track("slow"));
        _playback.Play(Track("fast"));
        await WaitUntil(() => controller.Current is not null);
        Assert.Equal("fast lyrics", controller.Current!.Lines[0].Text);

        release.SetResult();
        await Task.Delay(100);
        Assert.Equal("fast lyrics", controller.Current!.Lines[0].Text);
    }

    [Fact]
    public async Task A_track_change_clears_the_old_lyrics_at_once()
    {
        var controller = Create(item => item.Name == "one" ? Four() : null);
        _playback.Play(Track("one"));
        await WaitUntil(() => controller.Current is not null);

        _playback.Play(Track("two"));
        Assert.Null(controller.Current);
        Assert.Equal(-1, controller.CurrentIndex);
        await WaitUntil(() => !controller.IsLoading);
        Assert.Null(controller.Current);

        _playback.Play(null);
        Assert.False(controller.IsLoading);
    }

    [Fact]
    public async Task A_source_that_fails_leaves_no_lyrics_and_no_spinner()
    {
        var controller = Create(_ => throw new InvalidOperationException("boom"));
        _playback.Play(Track("one"));
        await WaitUntil(() => !controller.IsLoading);
        Assert.Null(controller.Current);
    }

    [Fact]
    public async Task Turned_off_nothing_is_loaded_until_it_is_turned_on_again()
    {
        var controller = Create(_ => Four());
        _enabled = false;
        _playback.Play(Track("one"));
        await Task.Delay(50);
        Assert.Null(controller.Current);
        Assert.False(controller.IsLoading);

        _enabled = true;
        controller.Refresh();
        await WaitUntil(() => controller.Current is not null);
    }

    [Fact]
    public async Task Events_go_through_the_ui_post_function()
    {
        var queue = new Queue<Action>();
        var controller = Create(_ => Four(), a =>
        {
            lock (queue)
                queue.Enqueue(a);
        });
        _playback.Play(Track("one"));
        await WaitUntil(() => !controller.IsLoading);
        await Task.Delay(30);

        Assert.Empty(_events); // nothing raised before the UI thread ran the posted work
        List<Action> posted;
        lock (queue)
            posted = [.. queue];
        Assert.NotEmpty(posted);
        foreach (var action in posted)
            action();
        Assert.Contains(LyricsChangeKind.Lyrics, _events);
    }

    [Fact]
    public async Task Saving_the_users_own_lyrics_shows_them_and_deleting_brings_the_others_back()
    {
        var controller = Create(_ => Four());
        _playback.Play(Track("one"));
        await WaitUntil(() => controller.Current is not null);

        controller.SaveOverride(Synced("mine one", "mine two"));
        await WaitUntil(() => controller.Current?.Lines[0].Text == "mine one");
        Assert.Equal(LyricsSources.Override, controller.Current!.Source);

        controller.DeleteOverride();
        await WaitUntil(() => controller.Current?.Lines[0].Text == "a");
    }

    [Fact]
    public async Task After_dispose_nothing_happens()
    {
        var controller = Create(_ => Four());
        _playback.Play(Track("one"));
        await WaitUntil(() => controller.Current is not null);
        controller.Dispose();
        _events.Clear();

        _playback.Play(Track("two"));
        await Task.Delay(50);
        Assert.Empty(_events);
    }

    [Fact]
    public async Task Follows_the_real_media_player()
    {
        var engine = new FakeEngine();
        var player = new MediaPlayer(engine, new FakeInfoReader());
        var service = new LyricsService(null, [new FakeLyricsSource("fake", _ => Four())]);
        using var controller = new LyricsController(new MediaPlayerLyricsPlayback(player), service);

        player.OpenFiles(["/music/a.mp3"]);
        await WaitUntil(() => controller.Current is not null);
        Assert.Equal("/music/a.mp3", controller.Item!.Source);

        engine.Position = TimeSpan.FromSeconds(25);
        controller.Tick();
        Assert.Equal(1, controller.CurrentIndex);

        Assert.True(controller.SeekToLine(3));
        Assert.InRange(engine.Position.TotalSeconds, 40.0, 40.1);
        controller.Tick();
        Assert.Equal(3, controller.CurrentIndex);
    }

    [Fact]
    public async Task The_app_factory_wires_tags_and_sidecars_to_the_player_and_the_settings()
    {
        var music = NewTempFolder("pardeh-lyr-factory-");
        try
        {
            var song = Path.Combine(music, "song.mp3");
            File.WriteAllText(song, "not really audio");
            File.WriteAllText(Path.Combine(music, "song.lrc"), "[00:01.00]from the factory");
            var engine = new FakeEngine();
            var player = new MediaPlayer(engine, new FakeInfoReader());
            using var controller = LyricsController.Create(player, new AppSettings { LyricsEnabled = true, LyricsOnline = false });

            player.OpenFiles([song]);
            await WaitUntil(() => controller.Current is not null);

            Assert.Equal("from the factory", controller.Current!.Lines[0].Text);
            Assert.Equal(LyricsSources.Sidecar, controller.Current.Source);
        }
        finally
        {
            Directory.Delete(music, recursive: true);
        }
    }

    // --- the settings --------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_lyrics_settings_have_the_agreed_defaults()
    {
        var settings = new AppSettings();
        Assert.True(settings.LyricsEnabled);
        Assert.False(settings.LyricsOnline);
        Assert.True(settings.LyricsSaveFetched);
        Assert.Equal(1.0, settings.LyricsTextSize);
        Assert.True(settings.LyricsFollow);
    }

    [Fact]
    public void The_text_size_is_kept_between_0_8_and_1_6()
    {
        var settings = new AppSettings { LyricsTextSize = 0.1 };
        Assert.Equal(0.8, settings.LyricsTextSize);
        settings.LyricsTextSize = 9;
        Assert.Equal(1.6, settings.LyricsTextSize);
        settings.LyricsTextSize = 1.25;
        Assert.Equal(1.25, settings.LyricsTextSize);
        settings.LyricsTextSize = double.NaN;
        Assert.Equal(1.0, settings.LyricsTextSize);
    }

    [Fact]
    public void The_settings_round_trip_and_an_old_file_without_them_gets_the_defaults()
    {
        var written = JsonSerializer.Serialize(new AppSettings { LyricsEnabled = false, LyricsOnline = true, LyricsSaveFetched = false, LyricsTextSize = 1.4, LyricsFollow = false });
        var read = JsonSerializer.Deserialize<AppSettings>(written)!;
        Assert.False(read.LyricsEnabled);
        Assert.True(read.LyricsOnline);
        Assert.False(read.LyricsSaveFetched);
        Assert.Equal(1.4, read.LyricsTextSize);
        Assert.False(read.LyricsFollow);

        var old = JsonSerializer.Deserialize<AppSettings>("""{"AlwaysOnTop":true,"FullModeLook":"luna","GlassTintHue":200}""")!;
        Assert.True(old.AlwaysOnTop);
        Assert.True(old.LyricsEnabled);
        Assert.False(old.LyricsOnline);
        Assert.Equal(1.0, old.LyricsTextSize);
    }
}
