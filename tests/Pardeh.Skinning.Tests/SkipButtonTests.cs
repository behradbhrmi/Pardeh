using System.Globalization;
using Avalonia;
using Pardeh.Skinning.Runtime.Elements;
using Pardeh.UI.FullMode;
using MediaPlayer = Pardeh.Core.Playback.MediaPlayer;

namespace Pardeh.Skinning.Tests;

/// <summary>
/// The skip 10 seconds buttons of the phone looks: the relative seek (clamped to the item, from a script too) and the
/// layout of the two buttons in all seven looks, portrait and landscape: inside the view, tappable, not on top of anything.
/// </summary>
[Collection("Avalonia rendering")]
public sealed class SkipButtonTests
{

    private static void EnsurePlatform() => TestPlatform.Ensure();

    // ---- The seek ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(100, 10, 300, 110)]
    [InlineData(100, -10, 300, 90)]
    [InlineData(4, -10, 300, 0)]          // clamps at the start
    [InlineData(295, 10, 300, 299.5)]     // clamps just before the end (the item would otherwise finish and skip on)
    [InlineData(299.5, 10, 300, 299.5)]
    [InlineData(0, 10, 0.4, 0.4)]         // a very short item: its end
    [InlineData(5, 10, 0, 0)]             // unknown duration
    [InlineData(-3, 10, 300, 10)]
    [InlineData(double.NaN, 10, 300, 10)]
    public void ClampSeek_keeps_the_target_inside_the_item(double position, double delta, double duration, double expected) =>
        Assert.Equal(expected, MediaPlayer.ClampSeek(position, delta, duration), 6);

    private static (MediaPlayer Player, FakeEngine Engine) OpenPlayer()
    {
        var engine = new FakeEngine();
        var player = new MediaPlayer(engine, new FakeInfoReader());
        player.OpenFiles(["C:/music/a.mp3"]);
        return (player, engine);
    }

    /// <summary>A phone look running against a fake engine; the pictures are decoded for real, so the buttons get their sizes.</summary>
    private static (Pardeh.Skinning.Runtime.SkinSession Session, MediaPlayer Player, FakeEngine Engine) StartLook(string key)
    {
        var engine = new FakeEngine();
        var player = new MediaPlayer(engine, new FakeInfoReader());
        var session = new Pardeh.Skinning.Runtime.SkinSession(PhoneSkin.Build(key), player, new FakeHost(), new Pardeh.UI.Rendering.AvaloniaImageDecoder(), new MemoryPreferences());
        session.Start();
        session.Tick(TimeSpan.FromMilliseconds(30));
        return (session, player, engine);
    }

    [Fact]
    public void SeekBy_moves_the_position_by_ten_seconds_and_clamps_at_both_ends()
    {
        var (player, engine) = OpenPlayer();          // the fake engine opens a 3 minute item at 0
        var raised = 0;
        player.Seeked += (_, _) => raised++;

        player.SeekBy(10);
        Assert.Equal(10, engine.Position.TotalSeconds, 3);
        player.SeekBy(10);
        Assert.Equal(20, engine.Position.TotalSeconds, 3);
        player.SeekBy(-10);
        Assert.Equal(10, engine.Position.TotalSeconds, 3);
        player.SeekBy(-10);
        player.SeekBy(-10);
        Assert.Equal(0, engine.Position.TotalSeconds, 3);

        engine.Position = TimeSpan.FromSeconds(175);
        player.SeekBy(10);
        Assert.Equal(179.5, engine.Position.TotalSeconds, 3);
        Assert.True(raised >= 5);
    }

    [Fact]
    public void SeekBy_does_nothing_when_nothing_is_open()
    {
        var engine = new FakeEngine();
        var player = new MediaPlayer(engine, new FakeInfoReader());
        player.SeekBy(10);
        Assert.Equal(0, engine.Position.TotalSeconds);
    }

    [Fact]
    public void A_script_can_seek_by_through_player_controls()
    {
        EnsurePlatform();
        var (session, player, engine) = StartLook("fluent-phone");
        player.OpenFiles(["C:/music/a.mp3"]);
        session.Tick(TimeSpan.FromMilliseconds(30));

        session.CurrentView!.Script.Execute("player.controls.seekBy(10);", "test");
        Assert.Equal(10, engine.Position.TotalSeconds, 3);
        session.CurrentView!.Script.Execute("player.controls.seekBy(-25);", "test");
        Assert.Equal(0, engine.Position.TotalSeconds, 3);
    }

    // ---- The buttons in the seven looks -------------------------------------------------------------

    public static TheoryData<string> Looks => [.. PhoneSkin.Keys];

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

    private static readonly string[] Neighbours =
        ["bPlay", "bPause", "bPrev", "bNext", "bStop", "bMute", "bMuteLow", "bMuted", "bShuffle", "bRepeat", "vol", "seek", "tPos", "tDur", "tRem", "tTitle", "tArtist"];

    private static readonly (double W, double H)[] Sizes =
    [
        (360, 720), (412, 892), (320, 640), (300, 600),   // portrait phones
        (800, 360), (892, 412), (640, 320),               // landscape
    ];

    [Theory]
    [MemberData(nameof(Looks))]
    public void Both_skip_buttons_are_tappable_inside_the_view_and_clear_of_the_other_controls(string key)
    {
        EnsurePlatform();
        var (session, player, _) = StartLook(key);
        player.OpenFiles(["C:/music/a.mp3"]);
        var view = session.CurrentView!;
        foreach (var (w, h) in Sizes)
        {
            view.Root.Width = w;
            view.Root.Height = h;
            session.Tick(TimeSpan.FromMilliseconds(30));
            var back = view.FindById("bSkipB") ?? throw new Xunit.Sdk.XunitException($"{key} has no bSkipB");
            var forward = view.FindById("bSkipF") ?? throw new Xunit.Sdk.XunitException($"{key} has no bSkipF");
            Assert.True(back.Visible && forward.Visible, $"{key} {w}x{h}: skip buttons hidden");
            var a = Absolute(back);
            var b = Absolute(forward);
            foreach (var (name, r) in new[] { ("bSkipB", a), ("bSkipF", b) })
            {
                Assert.True(r.W >= 40 && r.H >= 40, $"{key} {w}x{h}: {name} is {r.W}x{r.H} units, a touch target needs 40+");
                Assert.True(r.X >= 0 && r.Y >= 0 && r.X + r.W <= w + 0.5 && r.Y + r.H <= h + 0.5, $"{key} {w}x{h}: {name} {r} leaves the view");
            }

            Assert.False(Overlaps(a, b), $"{key} {w}x{h}: the skip buttons overlap each other");
            foreach (var id in Neighbours)
            {
                if (view.FindById(id) is not { Visible: true, Width: > 0 } other || !AncestorsVisible(other))
                    continue;
                var o = Absolute(other);
                Assert.False(Overlaps(a, o), $"{key} {w}x{h}: bSkipB {a} overlaps {id} {o}");
                Assert.False(Overlaps(b, o), $"{key} {w}x{h}: bSkipF {b} overlaps {id} {o}");
            }
        }
    }

    private static bool AncestorsVisible(SkinElement e)
    {
        for (var p = e.Parent; p is not null; p = p.Parent)
            if (!p.Visible)
                return false;
        return true;
    }

    [Theory]
    [MemberData(nameof(Looks))]
    public void Pressing_the_skip_buttons_moves_the_position_by_ten_seconds(string key)
    {
        EnsurePlatform();
        var (session, player, engine) = StartLook(key);
        player.OpenFiles(["C:/music/a.mp3"]);
        engine.Position = TimeSpan.FromSeconds(60);
        var view = session.CurrentView!;
        session.Tick(TimeSpan.FromMilliseconds(30));

        // Every point of the 40+ unit box is a hit, not only the strokes of the glyph (a finger lands anywhere in it).
        foreach (var id in new[] { "bSkipB", "bSkipF" })
        {
            var button = view.FindById(id)!;
            foreach (var (fx, fy) in new[] { (0.5, 0.5), (0.2, 0.2), (0.8, 0.5), (0.5, 0.85), (0.15, 0.8) })
                Assert.True(button.HitTest(button.Width * fx, button.Height * fy), $"{key} {id} misses at {fx},{fy}");
        }

        view.Script.Execute(view.FindById("bSkipF")!.Handlers["onclick"], "test");
        Assert.Equal(70, engine.Position.TotalSeconds, 3);
        view.Script.Execute(view.FindById("bSkipB")!.Handlers["onclick"], "test");
        view.Script.Execute(view.FindById("bSkipB")!.Handlers["onclick"], "test");
        Assert.Equal(50, engine.Position.TotalSeconds, 3);
    }

    [Theory]
    [InlineData("fa-IR")]
    [InlineData("de-DE")]
    public void The_skip_art_does_not_depend_on_the_regional_format(string culture)
    {
        EnsurePlatform();
        var saved = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            var reference = PhoneSkin.Keys.ToDictionary(k => k, k => PhoneSkin.Build(k).ReadFile("skipb.png")!);
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);
            foreach (var key in PhoneSkin.Keys)
                Assert.Equal(reference[key], PhoneSkin.Build(key).ReadFile("skipb.png"));
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = saved;
        }
    }

    [Fact]
    public void The_fluent_skip_icons_are_in_the_icon_set()
    {
        Assert.True(Pardeh.UI.Rendering.FluentIcons.Has("skip_back_10"));
        Assert.True(Pardeh.UI.Rendering.FluentIcons.Has("skip_forward_10"));
    }
}
