using Avalonia;
using Avalonia.Headless;
using Pardeh.Core.Playback;
using Pardeh.Skinning.Runtime;
using Pardeh.Skinning.Runtime.Elements;
using Pardeh.UI.FullMode;
using Pardeh.UI.Rendering;

namespace Pardeh.Skinning.Tests;

/// <summary>The PC versions of the retro device looks: they build, resize like a desktop window, and keep the phone looks apart.</summary>
[Collection("Avalonia rendering")]
public sealed class DesktopRetroTests
{
    private static readonly object Gate = new();
    private static bool _platformUp;

    private static void EnsurePlatform()
    {
        lock (Gate)
        {
            if (_platformUp)
                return;
            try
            {
                AppBuilder.Configure<Application>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            }
            catch (InvalidOperationException)
            {
            }

            _platformUp = true;
        }
    }

    public static TheoryData<string> Looks => [.. PhoneSkin.DesktopRetro.Select(l => l.Key)];

    [Fact]
    public void There_is_a_pc_version_of_each_device_look_and_of_Winamp_and_Groove_under_its_own_key()
    {
        Assert.Equal(9, PhoneSkin.DesktopRetro.Length);
        Assert.All(new[] { "winamp-phone", "groove-phone" }, phone => Assert.Contains(PhoneSkin.DesktopRetro, l => l.Phone == phone));
        foreach (var (key, _, phone) in PhoneSkin.DesktopRetro)
        {
            Assert.Contains(phone, PhoneSkin.Keys);
            Assert.False(PhoneSkin.IsPhoneKey(key));
            Assert.Contains(FullModeDesign.All, d => d.Key == key && d.Retro?.PhoneKey == phone);
        }

        // The designed looks that were here before are as they were: no retro style.
        Assert.All(new[] { FullModeDesign.Horizon, FullModeDesign.Luna, FullModeDesign.Fluent }, d => Assert.Null(d.Retro));
    }

    [Fact]
    public void Winamps_equalizer_drawer_opens_from_its_key_and_works_the_players_equalizer()
    {
        EnsurePlatform();
        Assert.Equal(["winamp"], FullModeDesign.All.Where(d => d.Retro is { Equalizer: true }).Select(d => d.Key));
        var package = FullModeDesigner.Build(FullModeDesign.All.Single(d => d.Key == "winamp"));
        var player = new MediaPlayer(new FakeEngine(), new FakeInfoReader());
        var session = new SkinSession(package, player, new FakeHost(), new AvaloniaImageDecoder(), new MemoryPreferences());
        session.Start();
        var view = session.CurrentView!;
        void Tick() => session.Tick(TimeSpan.FromMilliseconds(30));
        void Click(SkinElement element)
        {
            var (x, y) = element.AbsolutePosition;
            Assert.Same(element, view.HitTest(x + element.Width / 2, y + element.Height / 2));
            element.OnPointerPressed(element.Width / 2, element.Height / 2);
            element.OnPointerReleased(element.Width / 2, element.Height / 2, inside: true);
            Tick();
        }

        view.Root.Width = 680; // the narrowest window
        view.Root.Height = 460;
        Tick();
        var drawer = view.FindById("svEq")!;
        Assert.False(drawer.Visible);
        var key = view.Elements.OfType<ButtonElement>().Single(b => b.ToolTip == "Equalizer");
        Click(key);
        Assert.True(drawer.Visible);

        var stage = view.FindById("svStage")!;
        var faders = Enumerable.Range(1, 10).Select(n => (SliderElement)view.FindById("eqBand" + n)!).ToList();
        Assert.All(faders, fader =>
        {
            var (x, _) = fader.AbsolutePosition;
            Assert.True(x >= stage.AbsolutePosition.X && x + fader.Width <= stage.AbsolutePosition.X + stage.Width, "every fader is inside the stage");
        });

        // A preset switches the equalizer on (it starts off) and moves the faders.
        Assert.True(player.Equalizer.Bypass);
        Click(view.FindById("eqNext")!);
        Assert.False(player.Equalizer.Bypass);
        Assert.NotEqual(0, player.Equalizer.CurrentPreset);
        Assert.All(Enumerable.Range(0, 10), band => Assert.Equal(player.Equalizer.GetGain(band), faders[band].Value, 0.001));

        // The top of a fader is the most that band can be raised; taking hold of one switches the equalizer on too.
        player.Equalizer.Bypass = true;
        faders[2].OnPointerPressed(7, 0);
        faders[2].OnPointerReleased(7, 0, inside: true);
        Tick();
        Assert.Equal(14, player.Equalizer.GetGain(2), 0.001);
        Assert.False(player.Equalizer.Bypass);

        // And the faders keep following afterwards.
        var preset = player.Equalizer.CurrentPreset;
        Click(view.FindById("eqPrev")!);
        Assert.NotEqual(preset, player.Equalizer.CurrentPreset);
        Assert.Equal(player.Equalizer.GetGain(2), faders[2].Value, 0.001);

        Click(view.FindById("eqOnOff")!);
        Assert.True(player.Equalizer.Bypass);
        Click(key);
        Assert.False(drawer.Visible);
    }

    [Theory]
    [MemberData(nameof(Looks))]
    public void A_pc_look_builds_resizes_and_keeps_its_window_and_playback_controls(string key)
    {
        EnsurePlatform();
        var artScale = PhoneSkin.ArtScale;
        var package = FullModeDesigner.Build(FullModeDesign.All.Single(d => d.Key == key));
        Assert.Equal(artScale, PhoneSkin.ArtScale); // building the PC art leaves the phone setting alone
        var player = new MediaPlayer(new FakeEngine(), new FakeInfoReader());
        var session = new SkinSession(package, player, new FakeHost(), new AvaloniaImageDecoder(), new MemoryPreferences());
        session.Start();
        player.OpenFiles(["C:/music/a.mp3"]);
        session.Tick(TimeSpan.FromMilliseconds(30));
        var view = session.CurrentView!;

        foreach (var (width, height) in new[] { (780.0, 540.0), (680.0, 460.0), (1600.0, 900.0) })
        {
            view.Root.Width = width;
            view.Root.Height = height;
            session.Tick(TimeSpan.FromMilliseconds(30));

            var list = view.FindById("svList")!;
            Assert.Equal(width - 4, list.AbsolutePosition.X + list.Width, 0.5);
            var stage = view.FindById("svStage")!;
            var frame = FullModeDesign.All.Single(d => d.Key == key).Retro!.Framed ? FullModeDesigner.StageInset : 0;
            Assert.Equal(list.AbsolutePosition.X, stage.AbsolutePosition.X + stage.Width + frame, 0.5);
            var play = view.Elements.OfType<ButtonElement>().First(b => b.Tag == "PLAYBUTTON");
            Assert.False(play.MovesWindow);
            var (px, py) = play.AbsolutePosition;
            Assert.True(py + play.Height <= height && px >= 0, "the play key is inside the window");
            var close = view.Elements.OfType<ButtonElement>().First(b => b.ToolTip == "Close");
            Assert.True(close.AbsolutePosition.X + close.Width <= width, "the close button is inside the window");

            // The top bar between the emblem and the window buttons moves the window.
            var hit = view.HitTest(width / 2, 4);
            for (var e = hit; e is not null; e = e.Parent)
            {
                if (e.IsInteractive)
                {
                    Assert.True(e.MovesWindow, $"{e.Tag}#{e.Id} blocks the top bar");
                    break;
                }
            }
        }
    }
}
