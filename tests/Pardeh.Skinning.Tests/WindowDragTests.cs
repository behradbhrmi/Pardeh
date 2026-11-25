using System.Text;
using Pardeh.Skinning.Runtime;
using Pardeh.Skinning.Runtime.Elements;
using Pardeh.Skinning.Wmp;

namespace Pardeh.Skinning.Tests;

/// <summary>Where a left press moves the window: the same rule the skin canvas applies to what is under the pointer.</summary>
public class WindowDragTests
{
    /// <summary>True when the nearest interactive element under the point (or nothing) lets the press move the window.</summary>
    private static bool Moves(SkinView view, double x, double y)
    {
        for (var e = view.HitTest(x, y); e is not null; e = e.Parent)
        {
            if (e.IsInteractive)
                return e.MovesWindow;
        }

        return true;
    }

    [Fact]
    public void The_view_and_decorative_buttons_move_the_window_but_controls_do_not()
    {
        // The original full mode's shape: a view with a right-click handler, and bars made of buttons that do nothing.
        var package = WmzLoader.FromFiles(new()
        {
            ["t.wms"] = Encoding.UTF8.GetBytes("""
                <THEME><VIEW width="200" height="100" onmousedown="JScript:var x = 1;">
                  <SUBVIEW id="back" left="0" top="40" width="200" height="30" backgroundColor="#202020"/>
                  <BUTTON id="bar" left="0" top="0" width="200" height="20"/>
                  <BUTTON id="own" left="0" top="80" width="40" height="20" onclick="JScript:var y = 2;"/>
                  <PLAYBUTTON id="play" left="50" top="80" width="40" height="20"/>
                  <BUTTON id="toggle" left="100" top="80" width="40" height="20" sticky="true"/>
                  <BUTTON id="grip" left="150" top="80" width="20" height="20" cursor="sizenwse" onmousedown="JScript:var z = 3;"/>
                </VIEW></THEME>
                """),
        }, "memory://drag");
        var (session, _, _, _) = TestSkins.Start(package);
        session.Tick(TimeSpan.FromMilliseconds(30));
        var view = session.CurrentView!;

        Assert.True(Moves(view, 100, 10), "a decorative bar");
        Assert.True(Moves(view, 100, 50), "the background");
        Assert.True(Moves(view, 190, 75), "empty view");
        Assert.False(Moves(view, 20, 90), "a button with onclick");
        Assert.False(Moves(view, 70, 90), "a transport button");
        Assert.False(Moves(view, 120, 90), "a sticky button");
        Assert.False(Moves(view, 160, 90), "the resize grip");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_original_full_mode_can_be_dragged_by_its_top_and_bottom(bool bars)
    {
        var resources = WmpResources.Find();
        Assert.SkipWhen(resources is null, "the WMP9 resources are not imported");
        var theme = new WszDecoder(resources!.GetText("res://wmploc/RT_TEXT/MAINAPPSKIN.WSZ")!, resources.GetString).Decode();
        var preferences = new MemoryPreferences();
        preferences.Save("app", "titleBarVisible", bars ? "true" : "false");
        var session = new SkinSession(WmzLoader.FromNode(MainAppSkin.Adapt(theme), "wmp9://mainappskin"),
            new Pardeh.Core.Playback.MediaPlayer(new FakeEngine(), new FakeInfoReader()), new FakeHost(),
            new Pardeh.Skinning.Imaging.BmpDecoder(), preferences) { Resources = resources };
        session.Start();
        for (var i = 0; i < 5; i++)
            session.Tick(TimeSpan.FromMilliseconds(30));
        var view = session.CurrentView!;
        var (w, h) = (view.Root.Width, view.Root.Height);

        // With the bars: the strips along the very top and bottom. Without: the curved frame just below and above them.
        var top = bars ? 8 : 28;
        var bottom = bars ? h - 8 : h - 40;
        foreach (var x in new double[] { 60, 200, 300 })
        {
            Assert.True(Moves(view, x, top), $"top bar at {x}");
            if (view.HitTest(x, bottom) is not ButtonGroupElement)
                Assert.True(Moves(view, x, bottom), $"bottom bar at {x}");
        }

        Assert.True(Moves(view, w * 0.6, h - 24), "bottom bar right of the transport buttons");
        var grip = view.FindById("btnResizeShow");
        if (grip is { Visible: true })
        {
            var (gx, gy) = grip.AbsolutePosition;
            Assert.False(Moves(view, gx + grip.Width / 2, gy + grip.Height / 2), "the resize grip");
        }
    }
}
