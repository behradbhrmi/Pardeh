using Pardeh.Core.Playback;

namespace Pardeh.Skinning.Tests;

/// <summary>Sleep timer and A-B repeat.</summary>
public sealed class PlaybackToolsTests
{
    private static MediaItem Item(string name) => new($"/music/{name}.mp3") { Name = name, Duration = TimeSpan.FromMinutes(3) };

    private static (MediaPlayer Player, FakeEngine Engine) Playing(params string[] names)
    {
        var engine = new FakeEngine();
        var player = new MediaPlayer(engine, new FakeInfoReader());
        player.OpenItems(names.Select(Item), 0);
        return (player, engine);
    }

    [Fact]
    public void The_sleep_timer_pauses_after_its_time_and_fades_out_first()
    {
        var (player, engine) = Playing("a", "b");
        player.Volume = 80;
        player.StartSleepTimer(TimeSpan.FromMinutes(5));
        Assert.True(player.SleepTimerActive);

        player.Tick(TimeSpan.FromMinutes(4));
        Assert.Equal(PlayState.Playing, player.PlayState);
        Assert.Equal(0.8f, engine.Volume, 3);

        player.Tick(TimeSpan.FromSeconds(54)); // 6 s left of a 12 s fade: half volume
        Assert.Equal(0.4f, engine.Volume, 2);
        Assert.Equal(PlayState.Playing, player.PlayState);

        player.Tick(TimeSpan.FromSeconds(7));
        Assert.Equal(PlayState.Paused, player.PlayState);
        Assert.False(player.SleepTimerActive);
        Assert.Equal(0.8f, engine.Volume, 3); // the next Play is at the user's volume
    }

    [Fact]
    public void Cancelling_the_sleep_timer_restores_the_volume()
    {
        var (player, engine) = Playing("a");
        player.Volume = 50;
        player.StartSleepTimer(TimeSpan.FromSeconds(20));
        player.Tick(TimeSpan.FromSeconds(15));
        Assert.True(engine.Volume < 0.5f);
        player.CancelSleepTimer();
        Assert.Equal(0.5f, engine.Volume, 3);
        player.Tick(TimeSpan.FromMinutes(1));
        Assert.Equal(PlayState.Playing, player.PlayState);
    }

    [Fact]
    public void Sleep_at_the_end_of_the_track_loads_the_next_track_paused()
    {
        var (player, engine) = Playing("a", "b", "c");
        player.StartSleepAtEndOfTrack();
        engine.End();

        Assert.Equal(PlayState.Paused, player.PlayState);
        Assert.Equal("b", player.CurrentMedia!.Name);
        Assert.False(engine.Playing);
        Assert.False(player.SleepTimerActive);
        player.Play();
        Assert.Equal(PlayState.Playing, player.PlayState);
    }

    [Fact]
    public void The_sleep_fade_runs_from_full_to_silent_over_its_last_seconds()
    {
        Assert.Equal(1, MediaPlayer.SleepFade(TimeSpan.FromMinutes(1)));
        Assert.Equal(0.5, MediaPlayer.SleepFade(TimeSpan.FromSeconds(MediaPlayer.SleepFadeSeconds / 2)), 3);
        Assert.Equal(0, MediaPlayer.SleepFade(TimeSpan.Zero));
    }

    [Fact]
    public void A_B_repeat_marks_a_then_b_then_clears()
    {
        var (player, engine) = Playing("a");
        engine.Position = TimeSpan.FromSeconds(10);
        player.CycleSectionLoop();
        Assert.Equal(10, player.LoopStart);
        Assert.Null(player.LoopEnd);
        Assert.False(player.SectionLoopActive);

        engine.Position = TimeSpan.FromSeconds(10.2); // too close to A: B is not set
        player.CycleSectionLoop();
        Assert.Null(player.LoopEnd);

        engine.Position = TimeSpan.FromSeconds(20);
        player.CycleSectionLoop();
        Assert.True(player.SectionLoopActive);

        engine.Position = TimeSpan.FromSeconds(20.1);
        player.Tick(TimeSpan.FromMilliseconds(30));
        Assert.Equal(10, engine.Position.TotalSeconds); // back to A

        engine.Position = TimeSpan.FromSeconds(15);
        player.Tick(TimeSpan.FromMilliseconds(30));
        Assert.Equal(15, engine.Position.TotalSeconds); // inside the section: left alone

        player.CycleSectionLoop();
        Assert.False(player.SectionLoopActive);
        Assert.Null(player.LoopStart);
    }

    [Fact]
    public void Set_A_and_set_B_work_on_their_own()
    {
        var (player, engine) = Playing("a");
        Assert.False(player.SetSectionEnd()); // no A yet

        engine.Position = TimeSpan.FromSeconds(30);
        Assert.True(player.SetSectionStart());
        engine.Position = TimeSpan.FromSeconds(30.2);
        Assert.False(player.SetSectionEnd()); // too close
        engine.Position = TimeSpan.FromSeconds(40);
        Assert.True(player.SetSectionEnd());
        Assert.Equal((30.0, 40.0), (player.LoopStart!.Value, player.LoopEnd!.Value));

        engine.Position = TimeSpan.FromSeconds(35);
        player.SetSectionStart(); // a new A inside the section keeps B
        Assert.Equal(40.0, player.LoopEnd);
        engine.Position = TimeSpan.FromSeconds(50);
        player.SetSectionStart(); // a new A after B drops it
        Assert.Null(player.LoopEnd);
        Assert.Equal(50.0, player.LoopStart);
    }

    [Fact]
    public void Markers_are_jumped_to_looped_and_snapped_to()
    {
        var (player, engine) = Playing("a");
        player.Bookmarks = new Pardeh.Core.Library.BookmarkStore(Path.Combine(Path.GetTempPath(), $"pardeh-mk-{Guid.NewGuid():N}.json"));
        engine.Position = TimeSpan.FromSeconds(30);
        Assert.Equal(0, player.AddMarker("verse"));
        Assert.Equal(1, player.AddMarker("chorus", 60));

        Assert.True(player.StepMarker(1));
        Assert.Equal(60, engine.Position.TotalSeconds);
        engine.Position = TimeSpan.FromSeconds(61); // just after it: back goes one further
        Assert.True(player.StepMarker(-1));
        Assert.Equal(30, engine.Position.TotalSeconds);

        Assert.True(player.LoopMarkerSection(0));
        Assert.Equal((30.0, 60.0), (player.LoopStart!.Value, player.LoopEnd!.Value));
        player.ClearSectionLoop();

        engine.Position = TimeSpan.FromSeconds(31); // within the snap distance of "verse"
        player.SetSectionStart();
        engine.Position = TimeSpan.FromSeconds(58.9);
        player.SetSectionEnd();
        Assert.Equal((30.0, 60.0), (player.LoopStart!.Value, player.LoopEnd!.Value));

        Assert.True(player.JumpToMarker(1)); // B is outside the half-open section: the repeat ends
        Assert.False(player.SectionLoopActive);
        Assert.False(player.SetSectionPointAtMarker(0, end: true)); // no A
        Assert.True(player.SetSectionPointAtMarker(0, end: false));
        Assert.True(player.SetSectionPointAtMarker(1, end: true));
    }

    [Fact]
    public void A_section_ends_with_its_track()
    {
        var (player, engine) = Playing("a", "b");
        engine.Position = TimeSpan.FromSeconds(5);
        player.CycleSectionLoop();
        engine.Position = TimeSpan.FromSeconds(9);
        player.CycleSectionLoop();
        Assert.True(player.SectionLoopActive);
        player.Next();
        Assert.False(player.SectionLoopActive);
    }

    [Theory]
    [InlineData(-6.0, 0.5, 0.5012)] // -6 dB is half amplitude; a 0.5 peak is no problem when attenuating
    public void Replay_gain_converts_decibels_and_never_clips_the_peak(double gainDb, double peak, double expected)
    {
        Assert.Equal(expected, ReplayGain.Factor(gainDb, peak), 3);
    }

    [Fact]
    public void Replay_gain_limits_a_boost_by_the_track_peak_and_ignores_missing_tags()
    {
        Assert.Equal(1.0 / 0.9, ReplayGain.Factor(+9, 0.9), 3); // +9 dB would clip a 0.9 peak: held at 1 / peak
        Assert.Equal(1, ReplayGain.Factor(double.NaN, 0.5));
        Assert.Equal(2.2387, ReplayGain.Factor(+7, double.NaN), 3); // no peak known: the gain alone, within the cap
        Assert.Equal(ReplayGain.MaxFactor, ReplayGain.Factor(+40, double.NaN), 3);
        Assert.Equal(-6.5, ReplayGain.ParseDb("-6.50 dB"));
        Assert.Equal(3.2, ReplayGain.ParseDb("+3.2"));
        Assert.True(double.IsNaN(ReplayGain.ParseDb("loud")));
    }
}
