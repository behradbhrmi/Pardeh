using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Pardeh.Core.Library;
using Pardeh.Core.Notices;
using Pardeh.Core.Playback;
using Pardeh.UI.Rendering;

namespace Pardeh.Skinning.Tests;

/// <summary>On-screen messages: one at a time, what each change says, and where the card goes.</summary>
public sealed class NoticeTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("pardeh-notices").FullName;
    private long _now = 1_000_000;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    // ---- The one message showing ----------------------------------------------------------------------------

    private static Notice Message(string key, NoticeCategory category = NoticeCategory.Playback, bool automatic = false) =>
        new(key, category, NoticeIcon.Info, key) { Automatic = automatic };

    [Fact]
    public void The_newest_message_replaces_the_one_before_and_leaves_after_its_time()
    {
        var center = new NoticeCenter(() => _now);
        center.Post(Message("a"));
        center.Post(Message("b"));
        Assert.Equal("b", center.Current!.Key);

        _now += 1999;
        Assert.NotNull(center.Current);
        _now += 1;
        Assert.Null(center.Current);
        Assert.Equal(0, center.Opacity);
    }

    [Fact]
    public void The_same_message_again_starts_its_time_again_without_fading_in_again()
    {
        var center = new NoticeCenter(() => _now);
        center.Post(Message("volume"));
        _now += 1500;
        center.Post(Message("volume"));
        Assert.Equal(1, center.Opacity);
        _now += 1500;
        Assert.NotNull(center.Current);
    }

    [Fact]
    public void It_fades_in_and_out()
    {
        var center = new NoticeCenter(() => _now);
        center.Post(Message("a"));
        Assert.Equal(0, center.Opacity);
        _now += NoticeCenter.FadeInMs;
        Assert.Equal(1, center.Opacity);
        _now += 2000 - NoticeCenter.FadeInMs - NoticeCenter.FadeOutMs / 2;
        Assert.Equal(0.5, center.Opacity, 2);
    }

    [Fact]
    public void Time_on_screen_follows_the_setting()
    {
        var center = new NoticeCenter(() => _now) { DurationScale = 2 };
        center.Post(Message("a"));
        _now += 3900;
        Assert.NotNull(center.Current);
    }

    [Fact]
    public void A_held_message_stays_until_it_is_released()
    {
        var center = new NoticeCenter(() => _now);
        center.Post(Message("scan") with { Held = true });
        _now += 60_000;
        Assert.NotNull(center.Current);
        center.Release("scan");
        _now += NoticeCenter.FadeOutMs;
        Assert.Null(center.Current);
    }

    [Fact]
    public void A_fresh_problem_is_not_covered_by_what_happens_by_itself()
    {
        var center = new NoticeCenter(() => _now);
        center.Post(Message("error", NoticeCategory.Problem));
        Assert.False(center.Post(Message("title", NoticeCategory.SongTitle, automatic: true)));
        Assert.Equal("error", center.Current!.Key);
        // What the user does is answered at once.
        Assert.True(center.Post(Message("volume", NoticeCategory.Sound)));

        center.Post(Message("error", NoticeCategory.Problem));
        _now += NoticeCenter.ProblemProtectedMs;
        Assert.True(center.Post(Message("title", NoticeCategory.SongTitle, automatic: true)));
    }

    [Fact]
    public void A_group_that_is_off_shows_nothing()
    {
        var center = new NoticeCenter(() => _now) { Allowed = c => c != NoticeCategory.Sound };
        Assert.False(center.Post(Message("volume", NoticeCategory.Sound)));
        Assert.Null(center.Current);
    }

    // ---- What the player says -----------------------------------------------------------------------------

    private sealed record Rig(MediaPlayer Player, FakeEngine Engine, NoticeCenter Center, PlayerNotices Notices)
    {
        public Notice? Message => Center.Current;
    }

    /// <summary>A player that has been playing the first of three songs for a while: nothing is showing.</summary>
    private Rig Playing(params string[] files)
    {
        var engine = new FakeEngine();
        var player = new MediaPlayer(engine, new FakeInfoReader()) { Bookmarks = new BookmarkStore(Path.Combine(_folder, "marks.json")) };
        var center = new NoticeCenter(() => _now);
        var notices = new PlayerNotices(player, center, () => _now);
        player.OpenFiles(files.Length > 0 ? files : ["one.mp3", "two.mp3", "three.mp3"]);
        notices.Update();
        center.Clear();
        return new Rig(player, engine, center, notices);
    }

    private Notice Said(Rig rig, Action<MediaPlayer> change)
    {
        change(rig.Player);
        rig.Notices.Update();
        return rig.Message ?? throw new Xunit.Sdk.XunitException("nothing was shown");
    }

    private void Later(Rig rig, double seconds = 10)
    {
        _now += (long)(seconds * 1000);
        rig.Notices.Update();
    }

    [Fact]
    public void Nothing_is_said_when_nothing_changes_or_while_it_is_quiet()
    {
        var rig = Playing();
        rig.Notices.Update();
        Assert.Null(rig.Message);

        rig.Notices.Hush(1);
        rig.Player.Volume = 10;
        rig.Player.Shuffle = true;
        rig.Player.CurrentPosition = 30;
        rig.Notices.Update();
        Assert.Null(rig.Message);
        // What changed meanwhile is not told afterwards.
        Later(rig);
        Assert.Null(rig.Message);
    }

    [Fact]
    public void Volume_shows_its_value_and_mute_is_named()
    {
        var rig = Playing();
        var volume = Said(rig, p => p.Volume = 65);
        Assert.Equal(("Volume", "65%", 0.65), (volume.Title, volume.Value, volume.Level!.Value));

        var muted = Said(rig, p => p.Mute = true);
        Assert.Equal(("Muted", NoticeIcon.Muted), (muted.Title, muted.Icon));
    }

    [Fact]
    public void Volume_at_its_limit_still_answers()
    {
        var rig = Playing();
        Said(rig, p => p.Volume = 100);
        Later(rig);
        rig.Player.Volume += 5;
        rig.Notices.Update();
        Assert.Null(rig.Message);
        rig.Notices.ShowVolume();
        Assert.Equal("100%", rig.Message!.Value);
    }

    [Fact]
    public void A_skip_shows_the_step_the_time_and_what_is_left()
    {
        var rig = Playing();
        rig.Engine.Position = TimeSpan.FromSeconds(60);
        var seek = Said(rig, p => p.SeekBy(10));
        Assert.Equal("+10 s", seek.Title);
        Assert.Equal(new NoticeTimeline(70, 180), seek.Timeline);
        Assert.Equal(110, seek.Timeline!.Remaining);
    }

    [Fact]
    public void Skips_in_one_direction_add_up_while_the_message_stands()
    {
        var rig = Playing();
        rig.Engine.Position = TimeSpan.FromSeconds(60);
        Said(rig, p => p.SeekBy(10));
        _now += 500;
        Assert.Equal("+20 s", Said(rig, p => p.SeekBy(10)).Title);
        // Turning back starts a new sum, and so does a pause.
        Assert.Equal("-10 s", Said(rig, p => p.SeekBy(-10)).Title);
        Later(rig);
        Assert.Equal("-10 s", Said(rig, p => p.SeekBy(-10)).Title);
    }

    [Fact]
    public void A_drag_of_the_seek_bar_counts_from_where_it_began()
    {
        var rig = Playing();
        rig.Engine.Position = TimeSpan.FromSeconds(20);
        Said(rig, p => p.CurrentPosition = 50);
        _now += 100;
        var drag = Said(rig, p => p.CurrentPosition = 100);
        Assert.Equal("+1:20", drag.Title);
        Assert.Equal(100, drag.Timeline!.Position);

        _now += 1500; // let go, and later another place is picked
        Assert.Equal("-1:00", Said(rig, p => p.CurrentPosition = 40).Title);
    }

    [Fact]
    public void The_time_keeps_running_while_the_message_shows()
    {
        var rig = Playing();
        Said(rig, p => p.SeekBy(10));
        rig.Engine.Position = TimeSpan.FromSeconds(11);
        _now += 1000;
        rig.Notices.Update();
        Assert.Equal(11, rig.Message!.Timeline!.Position);
        _now += 1000;
        Assert.Null(rig.Message);
    }

    [Fact]
    public void The_jumps_of_a_repeated_section_are_silent()
    {
        var rig = Playing();
        rig.Engine.Position = TimeSpan.FromSeconds(10);
        Assert.Equal("A-B repeat: A at 00:10", Said(rig, p => p.CycleSectionLoop()).Title);
        rig.Engine.Position = TimeSpan.FromSeconds(20);
        Assert.Equal("Repeating 00:10 to 00:20", Said(rig, p => p.CycleSectionLoop()).Title);
        Later(rig);

        rig.Player.Tick(TimeSpan.FromMilliseconds(30));
        Assert.Equal(10, rig.Player.CurrentPosition);
        rig.Notices.Update();
        Assert.Null(rig.Message);
        Assert.Equal("A-B repeat off", Said(rig, p => p.CycleSectionLoop()).Title);
    }

    [Fact]
    public void A_marker_is_announced_when_added_and_names_a_jump_to_it()
    {
        var rig = Playing();
        rig.Engine.Position = TimeSpan.FromSeconds(42);
        var added = Said(rig, p => p.AddMarker("Chorus"));
        Assert.Equal(("Marker added", "Chorus at 00:42"), (added.Title, added.Detail));

        rig.Engine.Position = TimeSpan.Zero;
        var jump = Said(rig, p => p.JumpToMarker(0));
        Assert.Equal(("Chorus", NoticeIcon.Marker), (jump.Title, jump.Icon));
    }

    [Fact]
    public void Pause_play_and_stop_say_so_with_the_place()
    {
        var rig = Playing();
        rig.Engine.Position = TimeSpan.FromSeconds(75);
        var paused = Said(rig, p => p.Pause());
        Assert.Equal(("Pause", 75.0), (paused.Title, paused.Timeline!.Position));
        Assert.Equal("Play", Said(rig, p => p.Play()).Title);
        Assert.Equal("Stop", Said(rig, p => p.Stop()).Title);
    }

    [Fact]
    public void Scanning_holds_a_message_and_then_says_how_far_it_went()
    {
        var rig = Playing();
        rig.Engine.Position = TimeSpan.FromSeconds(30);
        var scan = Said(rig, p => p.FastForward());
        Assert.Equal("Fast forward 5×", scan.Title);
        rig.Player.Tick(TimeSpan.FromSeconds(4));
        Later(rig, 4);
        Assert.Equal(50, rig.Message!.Timeline!.Position);

        var done = Said(rig, p => p.Play());
        Assert.Equal(("Play", "+20 s"), (done.Title, done.Value));
    }

    [Fact]
    public void The_end_of_the_queue_is_told()
    {
        var rig = Playing("only.mp3");
        rig.Engine.End();
        rig.Notices.Update();
        Assert.Equal("End of the queue", rig.Message!.Title);
    }

    [Fact]
    public void A_new_track_is_named_in_full_screen_only_unless_asked_for_always()
    {
        var rig = Playing();
        rig.Player.Next();
        rig.Notices.Update();
        Assert.Null(rig.Message);

        rig.Notices.FullScreen = () => true;
        var next = Said(rig, p => p.Next());
        Assert.Equal(("three", "Test Artist · Test Album", "3 of 3"), (next.Title, next.Detail, next.Value));

        rig.Notices.SongTitle = SongTitleNotices.Never;
        Later(rig);
        rig.Player.Previous();
        rig.Notices.Update();
        Assert.Null(rig.Message);
    }

    [Fact]
    public void A_new_track_with_a_picture_is_named_in_a_window_too()
    {
        var rig = Playing("a.mp4", "b.mp4");
        rig.Engine.VideoSize = (1280, 720);
        Assert.Equal("b", Said(rig, p => p.Next()).Title);
    }

    [Fact]
    public void What_a_new_track_resets_is_not_news()
    {
        var rig = Playing();
        Said(rig, p => p.SetSubtitleDelay(TimeSpan.FromSeconds(1)));
        Later(rig);
        rig.Player.Next(); // the delay goes back to none with the new track
        rig.Notices.Update();
        Assert.Null(rig.Message);
    }

    [Fact]
    public void Modes_speed_and_balance_show_their_new_value()
    {
        var rig = Playing();
        Assert.Equal(("Shuffle", "On"), Pair(Said(rig, p => p.Shuffle = true)));
        Assert.Equal(("Repeat", "On"), Pair(Said(rig, p => p.Loop = true)));
        Assert.Equal(("Speed", "1.25×"), Pair(Said(rig, p => p.Rate = 1.25)));
        var balance = Said(rig, p => p.Balance = -40);
        Assert.Equal(("Balance", "40 left"), Pair(balance));
        Assert.True(balance.Centered);
        Assert.Equal(0.3, balance.Level!.Value, 3);

        static (string, string?) Pair(Notice n) => (n.Title, n.Value);
    }

    [Fact]
    public void The_equalizer_and_the_sound_enhancements_speak()
    {
        var rig = Playing();
        Assert.Equal("Rock", Said(rig, p =>
        {
            p.Equalizer.CurrentPreset = 1;
            p.Equalizer.Bypass = false;
        }).Value);
        Assert.Equal("Off", Said(rig, p => p.Equalizer.Bypass = true).Value);
        Assert.Equal("Crossfading", Said(rig, p => p.Equalizer.CrossFade = true).Title);
        Assert.Equal("Little difference", Said(rig, p => p.Equalizer.QuietMode = 2).Value);
    }

    [Fact]
    public void A_chosen_audio_track_is_named_with_its_place()
    {
        var rig = Playing("film.mkv", "next.mkv");
        rig.Engine.Tracks.AddRange([new AudioTrack("film.mkv", 1, "English (stereo)"), new AudioTrack("film.mkv", 2, "Persian (5.1)")]);
        rig.Player.PlayItem(0);
        rig.Notices.Update();
        rig.Center.Clear();

        var chosen = Said(rig, p => p.SelectAudioTrack(1));
        Assert.Equal(("Audio track", "Persian (5.1)", "2 of 2"), (chosen.Title, chosen.Detail, chosen.Value));

        // The next film starts with the language chosen, by itself: that is not news.
        Later(rig);
        rig.Player.Next();
        rig.Notices.Update();
        Assert.Equal(1, rig.Player.CurrentAudioTrack);
        Assert.Null(rig.Message);
    }

    [Fact]
    public void Subtitles_are_named_when_they_come_and_go()
    {
        var srt = Path.Combine(_folder, "film.en.srt");
        File.WriteAllText(srt, "1\n00:00:10,000 --> 00:00:12,000\nLine\n");
        var rig = Playing("film.mp4");
        var loaded = Said(rig, p => p.LoadSubtitles(srt));
        Assert.Equal(("Subtitles", "film.en.srt"), (loaded.Title, loaded.Detail));
        Assert.Equal("Subtitles off", Said(rig, p => p.ShowSubtitles = false).Title);
        Assert.Equal("film.en.srt", Said(rig, p => p.ShowSubtitles = true).Detail);
        Assert.Equal("+0.1 s", Said(rig, p => p.NudgeSubtitleDelay(1)).Value);
    }

    [Fact]
    public void Picture_settings_show_their_value_and_a_reset_says_so()
    {
        var rig = Playing("film.mp4");
        Assert.Equal("16:9", Said(rig, p => p.Video.Aspect = "16:9").Value);
        var bright = Said(rig, p => p.Video.Brightness = 20);
        Assert.Equal(("Brightness", "+20"), (bright.Title, bright.Value));
        Said(rig, p => p.Video.Contrast = -5);
        Assert.Equal("Picture reset", Said(rig, p => p.Video.Reset()).Title);
    }

    [Fact]
    public void The_sleep_timer_tells_when_it_is_set_nearly_over_and_done()
    {
        var rig = Playing();
        Assert.Equal("Sleep timer: 2 min", Said(rig, p => p.StartSleepTimer(TimeSpan.FromMinutes(2))).Title);
        Later(rig);

        rig.Player.Tick(TimeSpan.FromSeconds(30));
        rig.Notices.Update();
        Assert.Null(rig.Message);
        Assert.Equal("Sleep timer: pausing in 1 minute", Said(rig, p => p.Tick(TimeSpan.FromSeconds(31))).Title);
        Later(rig);

        rig.Player.Tick(TimeSpan.FromSeconds(58.5));
        rig.Notices.Update();
        Assert.Equal("Sleep timer: paused", Said(rig, p => p.Tick(TimeSpan.FromSeconds(2))).Title);

        Assert.Equal("Sleep timer: end of this track", Said(rig, p => p.StartSleepAtEndOfTrack()).Title);
        Assert.Equal("Sleep timer off", Said(rig, p => p.CancelSleepTimer()).Title);

        rig.Player.Play();
        Said(rig, p => p.StartSleepAtEndOfTrack());
        rig.Notices.FullScreen = () => true; // the next track's title must not cover why it is paused
        rig.Engine.End();
        rig.Notices.Update();
        Assert.Equal("Sleep timer: paused", rig.Message!.Title);
    }

    [Fact]
    public void Adding_to_the_queue_is_confirmed_but_opening_files_is_not_adding()
    {
        var rig = Playing();
        var added = Said(rig, p => p.Enqueue(["four.mp3"]));
        Assert.Equal(("Added to the queue", "four", "4 in all"), (added.Title, added.Detail, added.Value));
        Later(rig);
        Assert.Equal("2 added to the queue", Said(rig, p => p.Enqueue(["five.mp3", "six.mp3"])).Title);
        Later(rig);

        rig.Player.OpenFiles(["a.mp3", "b.mp3", "c.mp3", "d.mp3", "e.mp3", "f.mp3", "g.mp3", "h.mp3"]);
        rig.Notices.Update();
        Assert.Null(rig.Message);
    }

    [Fact]
    public void Letting_go_of_the_playing_file_to_change_it_says_nothing()
    {
        var rig = Playing();
        rig.Notices.FullScreen = () => true;
        rig.Engine.Position = TimeSpan.FromSeconds(30);
        var source = rig.Player.CurrentMedia!.Source;
        var release = rig.Player.ReleaseFile(s => s == source);
        rig.Notices.Update();
        Assert.Null(rig.Message);

        rig.Player.ResumeFile(release, source);
        rig.Notices.Update();
        Assert.Equal(PlayState.Playing, rig.Player.PlayState);
        Assert.Null(rig.Message);
        // Afterwards it speaks as before.
        Assert.Equal("Pause", Said(rig, p => p.Pause()).Title);
    }

    [Fact]
    public void An_announcement_is_shown_each_time_even_with_the_same_words()
    {
        var rig = Playing();
        Said(rig, p => p.Announce("The change was undone"));
        Later(rig);
        Assert.Equal("The change was undone", Said(rig, p => p.Announce("The change was undone")).Title);
    }

    [Fact]
    public void Show_position_says_when_it_will_end_at_this_speed()
    {
        var rig = Playing();
        rig.Engine.Position = TimeSpan.FromSeconds(60);
        rig.Player.Rate = 2;
        rig.Notices.Update();
        Assert.True(rig.Notices.ShowPosition(new DateTime(2026, 11, 28, 22, 0, 0)));
        Assert.Equal(("one", "Ends at 22:01"), (rig.Message!.Title, rig.Message.Detail));
    }

    [Fact]
    public void A_swipe_aims_without_moving_playback()
    {
        var rig = Playing();
        rig.Notices.Aim(10, 40);
        rig.Engine.Position = TimeSpan.FromSeconds(12);
        rig.Notices.Update();
        Assert.Equal(("+30 s", 40.0), (rig.Message!.Title, rig.Message.Timeline!.Position));
        rig.Notices.EndAim();
        _now += 1000;
        Assert.Null(rig.Message);
    }

    [Theory]
    [InlineData(10, "+10 s")]
    [InlineData(-3, "-3 s")]
    [InlineData(60, "+1:00")]
    [InlineData(-605, "-10:05")]
    [InlineData(3725, "+1:02:05")]
    public void A_step_is_written_with_its_sign(double seconds, string text) => Assert.Equal(text, PlayerNotices.Delta(seconds));

    // ---- The card -------------------------------------------------------------------------------------------

    private static readonly Color Accent = Color.FromRgb(76, 194, 255);

    private static Notice Volume => new("volume", NoticeCategory.Sound, NoticeIcon.Volume, "Volume") { Value = "65%", Level = 0.65 };

    private static Notice Seek => new("seek", NoticeCategory.Position, NoticeIcon.FastForward, "+30 s")
    { Timeline = new NoticeTimeline(754, 3731, 600, 900), Detail = "Chapter 3: The crossing" };

    [Theory]
    [InlineData(NoticePlace.Top)]
    [InlineData(NoticePlace.TopLeft)]
    [InlineData(NoticePlace.TopRight)]
    [InlineData(NoticePlace.Centre)]
    [InlineData(NoticePlace.Bottom)]
    internal void The_card_stays_inside_the_picture(NoticePlace place)
    {
        TestPlatform.Ensure();
        foreach (var area in new[] { new Rect(20, 40, 800, 450), new Rect(0, 0, 260, 80), new Rect(0, 0, 90, 40) })
        {
            foreach (var notice in new[] { Volume, Seek, Volume with { Level = null, Title = new string('x', 300), Detail = new string('y', 300) } })
            {
                var card = NoticeLayer.Layout(notice, new NoticeFrame(area, place, Accent, 1, 20, 20));
                Assert.True(area.Contains(card.TopLeft) && card.Right <= area.Right + 0.5 && (card.Bottom <= area.Bottom + 0.5 || card.Height > area.Height),
                    $"{place} {area}: {card}");
            }
        }
    }

    [Fact]
    public void A_small_skin_gets_a_low_card()
    {
        TestPlatform.Ensure();
        var small = NoticeLayer.Layout(Seek, new NoticeFrame(new Rect(0, 0, 240, 90), NoticePlace.Top, Accent));
        var large = NoticeLayer.Layout(Seek, new NoticeFrame(new Rect(0, 0, 800, 450), NoticePlace.Top, Accent));
        Assert.True(small.Height < large.Height / 2 + 8, $"{small} {large}");
    }

    /// <summary>Every layout drawn on one sheet; PARDEH_NOTICE_SHEET=file.png keeps the picture, for a look.</summary>
    [Fact]
    public void Every_icon_and_layout_draws()
    {
        TestPlatform.Ensure();
        var samples = new List<Notice>
        {
            Volume,
            Seek,
            new("balance", NoticeCategory.Sound, NoticeIcon.Balance, "Balance") { Value = "40 left", Level = 0.3, Centered = true },
            new("title", NoticeCategory.SongTitle, NoticeIcon.Music, "آهنگ آزمایشی (Test song)") { Detail = "Test Artist · Test Album", Value = "3 of 12" },
            new("error", NoticeCategory.Problem, NoticeIcon.Warning, "Can't play this file") { Detail = "broken.mkv" },
            new("scan", NoticeCategory.Playback, NoticeIcon.Rewind, "Rewind 5×") { Timeline = new NoticeTimeline(42, 0) },
        };
        samples.AddRange(Enum.GetValues<NoticeIcon>().Select(icon => new Notice(icon.ToString(), NoticeCategory.Playback, icon, icon.ToString())));

        const int width = 1080, row = 128, columns = 3;
        var size = new PixelSize(width, (samples.Count + columns - 1) / columns * row + 100);
        using var sheet = new RenderTargetBitmap(size, new Vector(96, 96));
        using (var context = sheet.CreateDrawingContext())
        {
            context.FillRectangle(new SolidColorBrush(Color.FromRgb(70, 100, 90)), new Rect(0, 0, size.Width, size.Height));
            for (var i = 0; i < samples.Count; i++)
            {
                var area = new Rect(i % columns * (width / columns), i / columns * row, width / columns, row);
                NoticeLayer.Draw(context, samples[i], 1, new NoticeFrame(area, NoticePlace.Centre, Accent));
            }

            // The one-line form for a small skin.
            var bottom = size.Height - 80;
            NoticeLayer.Draw(context, Seek, 1, new NoticeFrame(new Rect(0, bottom, 260, 70), NoticePlace.Centre, Accent));
            NoticeLayer.Draw(context, Volume, 1, new NoticeFrame(new Rect(320, bottom, 220, 70), NoticePlace.Centre, Accent));
        }

        if (Environment.GetEnvironmentVariable("PARDEH_NOTICE_SHEET") is { Length: > 0 } file)
            sheet.Save(file, PngBitmapEncoderOptions.Default);
    }
}
