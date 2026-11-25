using Pardeh.Skinning.Runtime.Elements;
using Pardeh.Skinning.Wmp;

namespace Pardeh.Skinning.Tests;

public class TransportTests
{
    private FakeHost Host { get; } = new();

    [Fact]
    public void Next_and_previous_move_one_track_in_the_original_full_mode()
    {
        var folder = TestSkins.FindPrivate("wmp9", "res");
        Assert.SkipWhen(folder is null, "private wmp9 resources not present");
        var resources = new WmpResources(folder!);
        var theme = new WszDecoder(resources.GetText("res://wmploc/RT_TEXT/MAINAPPSKIN.WSZ")!, resources.GetString).Decode();
        var package = WmzLoader.FromNode(MainAppSkin.Adapt(theme), "wmp9://mainappskin");
        var player = new Pardeh.Core.Playback.MediaPlayer(new FakeEngine(), new FakeInfoReader());
        var session = new Pardeh.Skinning.Runtime.SkinSession(package, player, Host, new Pardeh.Skinning.Imaging.BmpDecoder(), new MemoryPreferences()) { Resources = resources };
        session.Start();
        player.OpenFiles(Enumerable.Range(1, 5).Select(i => $"C:/music/{i}.mp3"));
        for (var i = 0; i < 5; i++) session.Tick(TimeSpan.FromMilliseconds(30));

        var log = new List<string>();
        var items = session.CurrentView!.Elements.OfType<ButtonGroupItem>().ToList();
        var next = items.First(i => i.GetString("upToolTip") == "Next");
        var previous = items.First(i => i.GetString("upToolTip") == "Previous");
        void Press(ButtonGroupItem item, string name)
        {
            item.Click();
            for (var i = 0; i < 5; i++) session.Tick(TimeSpan.FromMilliseconds(30));
            log.Add($"{name} -> {player.CurrentPlaylist.CurrentIndex}");
        }

        Press(next, "next");
        Press(next, "next");
        Press(previous, "prev");
        Press(previous, "prev");
        File.WriteAllLines(Path.Combine(folder!, "..", "transport.txt"), log);
        Assert.Equal(["next -> 1", "next -> 2", "prev -> 1", "prev -> 0"], log);
    }

    [Fact]
    public void Rail_menu_waits_for_the_choice_and_switches_task()
    {
        var folder = TestSkins.FindPrivate("wmp9", "res");
        Assert.SkipWhen(folder is null, "private wmp9 resources not present");
        var resources = new WmpResources(folder!);
        var theme = new WszDecoder(resources.GetText("res://wmploc/RT_TEXT/MAINAPPSKIN.WSZ")!, resources.GetString).Decode();
        var package = WmzLoader.FromNode(MainAppSkin.Adapt(theme), "wmp9://mainappskin");
        var player = new Pardeh.Core.Playback.MediaPlayer(new FakeEngine(), new FakeInfoReader());
        var session = new Pardeh.Skinning.Runtime.SkinSession(package, player, Host, new Pardeh.Skinning.Imaging.BmpDecoder(), new MemoryPreferences()) { Resources = resources };
        session.Start();

        Host.PopupChoice = 3; // the fourth entry: Media Library
        session.CurrentView!.Script.Execute("ShowTaskMenu(taskmenu);", "test");

        Assert.Equal(8, Host.LastPopup!.Count);
        Assert.Equal("Media Library", Host.LastPopup[3]);
        Assert.Equal("MediaLibrary", session.App.CurrentTask);
    }

    [Fact]
    public void Visualization_arrows_cycle_presets()
    {
        var player = new Pardeh.Core.Playback.MediaPlayer(new FakeEngine(), new FakeInfoReader());
        var package = WmzLoader.FromFiles(new() { ["t.wms"] = System.Text.Encoding.UTF8.GetBytes("<THEME><VIEW width=\"10\" height=\"10\"/></THEME>") }, "memory://t");
        var session = new Pardeh.Skinning.Runtime.SkinSession(package, player, Host, new Pardeh.Skinning.Imaging.BmpDecoder(), new MemoryPreferences());
        session.Start();

        // Starts on Ambience : Water, like the 2003 player.
        Assert.Equal("Ambience : Water", Pardeh.Skinning.Runtime.VisualizationCatalog.All[session.CurrentVisualization].Title);

        session.MediaCenter.SetMember("vizRequest", 2.0); // next preset
        Assert.Equal("Ambience", session.MediaCenter.GetMember("effectType"));
        Assert.Equal(1.0, session.MediaCenter.GetMember("effectPreset"));

        session.MediaCenter.SetMember("vizRequest", 4.0); // Shift: next family
        Assert.Equal("Bars and Waves : Bars", Pardeh.Skinning.Runtime.VisualizationCatalog.All[session.CurrentVisualization].Title);

        session.MediaCenter.SetMember("vizRequest", 1.0); // previous preset wraps into the previous family
        Assert.Equal("Ambience : Warp", Pardeh.Skinning.Runtime.VisualizationCatalog.All[session.CurrentVisualization].Title);
    }

    [Fact]
    public void Shrinking_after_widening_the_sidebar_keeps_a_usable_layout()
    {
        var folder = TestSkins.FindPrivate("wmp9", "res");
        Assert.SkipWhen(folder is null, "private wmp9 resources not present");
        var resources = new WmpResources(folder!);
        var theme = new WszDecoder(resources.GetText("res://wmploc/RT_TEXT/MAINAPPSKIN.WSZ")!, resources.GetString).Decode();
        var package = WmzLoader.FromNode(MainAppSkin.Adapt(theme), "wmp9://mainappskin");
        var player = new Pardeh.Core.Playback.MediaPlayer(new FakeEngine(), new FakeInfoReader());
        var session = new Pardeh.Skinning.Runtime.SkinSession(package, player, Host, new Pardeh.Skinning.Imaging.BmpDecoder(), new MemoryPreferences()) { Resources = resources };
        session.Start();
        void Tick() { for (var i = 0; i < 4; i++) session.Tick(TimeSpan.FromMilliseconds(30)); }
        Tick();
        var root = session.CurrentView!.Root;
        var log = new List<string>();
        Pardeh.Skinning.Runtime.SkinView Pane() => session.CurrentView!.Elements.OfType<HostAreaElement>().First(h => h.IsTaskCenter).Embedded!;
        void Dump(string when)
        {
            var pane = Pane();
            log.Add($"{when}: pane={pane.Root.Width} svMain={pane.FindById("svMain")!.Width} sidebar={pane.FindById("svSidebar")!.Left}+{pane.FindById("svSidebar")!.Width} vis={pane.FindById("svSidebar")!.Visible} playlistWidth={session.MediaCenter.GetMember("playlistWidth")}");
        }

        Dump("start");
        root.Width = 1800;
        Tick();
        Dump("wide");
        var paneView = Pane();
        paneView.Script.Execute("OnVResizeStart(); OnVResize(vMain.width - 502); OnVResizeEnd();", "test");
        Tick();
        Dump("dragged");
        root.Width = 690;
        Tick();
        Dump("restored");
        File.WriteAllLines(Path.Combine(folder!, "..", "resize.txt"), log);
        var final = Pane();
        Assert.True(final.FindById("svMain")!.Width >= 200, string.Join("\n", log));
    }
}
