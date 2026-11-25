using Pardeh.Core.Playback;
using Pardeh.Skinning.Runtime.Elements;

namespace Pardeh.Skinning.Tests;

/// <summary>Runs the real WMP 9 default skin ("Corona") headlessly. Skipped when the private skin file is absent.</summary>
public class CoronaSkinTests
{
    private const string SkinFile = "9SeriesDefault.wmz";

    [Fact]
    public void Loads_without_script_errors()
    {
        var (session, _, _, host) = TestSkins.Start(TestSkins.LoadPrivate(SkinFile));

        Assert.Equal("vPlayer", session.CurrentView!.Id);
        Assert.Empty(host.Logs);
    }

    [Fact]
    public void Computes_layout_from_script_attributes()
    {
        var (session, _, _, _) = TestSkins.Start(TestSkins.LoadPrivate(SkinFile));
        var view = session.CurrentView!;

        Assert.Equal(859, view.Root.Width);
        Assert.Equal(468, view.Root.Height);
        Assert.Equal(346, view.FindById("svMain")!.Width);          // view.width - svStub.width - svMain.left
        Assert.Equal(266, view.FindById("svBottom")!.Top);          // svMiddle.top + svMiddle.height
        var eq1 = view.FindById("eq1")!;
        var eq10 = view.FindById("eq10")!;
        Assert.True(eq1.Left > 0 && eq10.Left > eq1.Left);          // GetEqSliderLeft() from Corona.js
        Assert.Equal(299, view.FindById("svTopLeft")!.Width);
    }

    [Fact]
    public void Transport_follows_the_player()
    {
        var (session, player, engine, host) = TestSkins.Start(TestSkins.LoadPrivate(SkinFile));
        var view = session.CurrentView!;
        var play = view.Elements.OfType<ButtonGroupItem>().First(i => i.Action == TransportAction.Play);
        var pause = view.Elements.OfType<ButtonElement>().First(b => b.Action == TransportAction.Pause);

        session.Tick(TimeSpan.FromMilliseconds(30));
        Assert.False(play.Enabled);                                  // nothing to play yet
        Assert.False(pause.Visible);

        player.OpenFiles(["C:/music/song.mp3"]);
        session.Tick(TimeSpan.FromMilliseconds(30));

        Assert.True(engine.Playing);
        Assert.True(pause.Visible);                                  // visible="wmpenabled:player.controls.pause"
        Assert.Equal("Playing", view.FindById("metadata")!.GetString("value"));
        Assert.True(view.FindById("vis")!.Visible, "visualization should show while audio plays");
        Assert.Empty(host.Logs);
    }

    [Fact]
    public void Clicking_buttons_runs_skin_scripts()
    {
        var (session, player, _, host) = TestSkins.Start(TestSkins.LoadPrivate(SkinFile));
        var view = session.CurrentView!;
        var eqPanel = view.FindById("svEqualizer")!;
        var startTop = eqPanel.Top;

        // bEq is a BUTTONELEMENT in the top button group: onClick="ToggleEqualizer();"
        var bEq = (ButtonGroupItem)view.FindById("bEq")!;
        bEq.Click();
        for (var i = 0; i < 20; i++)
            session.Tick(TimeSpan.FromMilliseconds(30));

        Assert.Equal(startTop + 124, eqPanel.Top);                   // EQ_PANEL_TRAVEL_Y, animated by moveTo()
        Assert.True(bEq.Down);

        // Equalizer on/off: onClick="JScript:eq.bypass=!eq.bypass;"
        var onOff = (ButtonElement)view.FindById("bEqOnOff")!;
        onOff.OnPointerEnter();
        onOff.OnPointerPressed(5, 5);
        onOff.OnPointerReleased(5, 5, inside: true);
        Assert.False(player.Equalizer.Bypass);

        // Mute button element: onClick="jscript:player.settings.mute=down;"
        var mute = (ButtonGroupItem)view.FindById("bMute")!;
        mute.Click();
        Assert.True(player.Mute);

        Assert.Empty(host.Logs);
    }

    [Fact]
    public void Open_file_button_uses_the_host_dialog()
    {
        var (session, player, engine, host) = TestSkins.Start(TestSkins.LoadPrivate(SkinFile));
        host.NextOpenFile = "C:/music/opened.mp3";

        ((ButtonGroupItem)session.CurrentView!.FindById("bOpenFile")!).Click();

        Assert.Equal("C:/music/opened.mp3", player.CurrentMedia?.Source);
        Assert.Equal(PlayState.Playing, player.PlayState);
        Assert.True(engine.Playing);
        Assert.Empty(host.Logs);
    }

    [Fact]
    public void Compact_view_can_be_opened_and_closed()
    {
        var (session, _, _, host) = TestSkins.Start(TestSkins.LoadPrivate(SkinFile));

        session.RequestViewSwitch("viewTiny");
        session.Tick(TimeSpan.FromMilliseconds(30));

        Assert.Equal("viewTiny", session.CurrentView!.Id);
        Assert.Equal("viewTiny", host.View!.Id);
        Assert.Empty(host.Logs);
    }
}

public class ModalDialogTests
{
    [Fact]
    public void A_slow_file_dialog_does_not_time_out_the_script()
    {
        var (session, player, _, host) = TestSkins.Start(TestSkins.LoadPrivate("9SeriesDefault.wmz"));
        host.NextOpenFile = "C:/music/slow.mp3";
        host.DialogDelay = TimeSpan.FromSeconds(3.5); // longer than the 3 s script limit

        ((Pardeh.Skinning.Runtime.Elements.ButtonGroupItem)session.CurrentView!.FindById("bOpenFile")!).Click();

        Assert.Equal("C:/music/slow.mp3", player.CurrentMedia?.Source);
        Assert.Empty(host.Logs);
    }

    [Fact]
    public void An_infinite_loop_is_stopped()
    {
        var (session, _, _, host) = TestSkins.Start(TestSkins.LoadPrivate("9SeriesDefault.wmz"));

        session.CurrentView!.Script.Execute("while (true) { }", "evil.js");

        Assert.Contains(host.Logs, l => l.Contains("longer than"));
    }
}
