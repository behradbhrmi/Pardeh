using Pardeh.Core.Library;
using Pardeh.Core.Playback;

namespace Pardeh.Skinning.Tests;

public sealed class VideoToolsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("pardeh-video-tools-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Positions => Path.Combine(_folder, "positions.json");

    private (MediaPlayer, FakeEngine) Player(params string[] names)
    {
        var engine = new FakeEngine { VideoSize = (640, 360) };
        var player = new MediaPlayer(engine, new FakeInfoReader()) { VideoPositions = new VideoPositionStore(Positions) };
        player.OpenItems(names.Select(n => new MediaItem(n)), 0);
        return (player, engine);
    }

    [Theory] // the fake engine's videos last 180 s
    [InlineData(5, null)]
    [InlineData(60, 60.0)]
    [InlineData(160, 160.0)]
    [InlineData(170, null)]
    public void A_place_is_kept_unless_at_the_start_or_the_end(double position, double? kept) =>
        Assert.Equal(kept, MediaPlayer.ResumePoint(position, 180));

    [Fact]
    public void Short_videos_have_no_resume_point() => Assert.Null(MediaPlayer.ResumePoint(30, 50));

    [Fact]
    public void A_video_carries_on_where_it_was_left_even_after_a_restart()
    {
        var (player, engine) = Player("one.mp4", "two.mp4");
        engine.Position = TimeSpan.FromSeconds(70);
        player.Next();
        Assert.Equal(0, engine.Position.TotalSeconds);
        engine.Position = TimeSpan.FromSeconds(40);
        player.Stop(); // Stop rewinds, but the place left is remembered

        var (again, other) = Player("one.mp4", "two.mp4");
        Assert.Equal(70, other.Position.TotalSeconds);
        Assert.Contains("resumed at 01:10", again.Status);
        again.Next();
        Assert.Equal(40, other.Position.TotalSeconds);
    }

    [Fact]
    public void A_film_seen_to_its_end_starts_afresh_and_the_switch_turns_resume_off()
    {
        var (player, engine) = Player("one.mp4", "two.mp4");
        engine.Position = TimeSpan.FromSeconds(70);
        player.Pause();
        Assert.Equal(70, new VideoPositionStore(Positions).Get("one.mp4"));
        player.Play();
        engine.Position = TimeSpan.FromSeconds(179);
        engine.End();
        Assert.Null(new VideoPositionStore(Positions).Get("one.mp4"));

        new VideoPositionStore(Positions).Set("three.mp4", 90);
        var off = new FakeEngine { VideoSize = (640, 360) };
        var silent = new MediaPlayer(off, new FakeInfoReader()) { VideoPositions = new VideoPositionStore(Positions), ResumeVideos = false };
        silent.OpenItems([new MediaItem("three.mp4")], 0);
        Assert.Equal(0, off.Position.TotalSeconds);
    }

    [Fact]
    public void Music_is_never_remembered()
    {
        var engine = new FakeEngine();
        var player = new MediaPlayer(engine, new FakeInfoReader()) { VideoPositions = new VideoPositionStore(Positions) };
        player.OpenItems([new MediaItem("song.mp3")], 0);
        engine.Position = TimeSpan.FromSeconds(70);
        player.Pause();
        Assert.Null(new VideoPositionStore(Positions).Get("song.mp3"));
    }

    [Fact]
    public void Subtitle_delay_shifts_the_text_and_belongs_to_one_video()
    {
        var srt = Path.Combine(_folder, "film.srt");
        File.WriteAllText(srt, "1\n00:00:10,000 --> 00:00:12,000\nLine\n");
        var (player, engine) = Player("a.mp4", "b.mp4");
        Assert.True(player.LoadSubtitles(srt));
        engine.Position = TimeSpan.FromSeconds(10.25);
        Assert.Equal("Line", player.CurrentSubtitle);

        player.NudgeSubtitleDelay(1);
        player.NudgeSubtitleDelay(1);
        player.NudgeSubtitleDelay(1);
        Assert.Equal(TimeSpan.FromMilliseconds(300), player.SubtitleDelay);
        Assert.Equal("+0.3 s", MediaPlayer.DescribeDelay(player.SubtitleDelay));
        // 0.3 s later: at 10.25 s the line has not appeared yet (the new delay is an on-screen message, not a subtitle).
        Assert.Null(player.CurrentSubtitle);
        engine.Position = TimeSpan.FromSeconds(10.35);
        Assert.Equal("Line", player.CurrentSubtitle);

        player.Next();
        Assert.Equal(TimeSpan.Zero, player.SubtitleDelay);
    }
}
