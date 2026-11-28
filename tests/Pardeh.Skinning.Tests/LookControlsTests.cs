using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Pardeh.Core.Library;
using Pardeh.Core.Playback;
using Pardeh.Skinning.Imaging;
using Pardeh.Skinning.Model;
using Pardeh.Skinning.Runtime;
using Pardeh.Skinning.Runtime.Elements;
using Pardeh.UI.FullMode;
using Pardeh.UI.Rendering;
using Xunit;

namespace Pardeh.Skinning.Tests;

/// <summary>
/// The controls every built-in look shares: the mouse on the picture (in a window and over the full screen), buttons that
/// show what is on, and side panels that can be resized.
/// </summary>
[Collection("Avalonia rendering")]
public sealed class LookControlsTests
{
    private static IEnumerable<(string Key, Func<SkinPackage> Build)> Looks()
    {
        foreach (var key in PhoneSkin.Keys)
            yield return (key, () => PhoneSkin.Build(key));
        yield return ("ember", EmberSkin.Build);
        yield return ("aero10", TenSkin.Build);
        yield return ("horizon", HorizonSkin.Build);
        yield return ("pardeh", () => FullModeSkin.Build(null));
        foreach (var design in FullModeDesign.All.Where(d => d.Key != "horizon"))
            yield return (design.Key, () => FullModeDesigner.Build(design));
    }

    public static TheoryData<string> AllLooks => [.. Looks().Select(l => l.Key)];

    /// <summary>The PC looks with a playlist column on the right.</summary>
    public static TheoryData<string> ListLooks => [.. Looks().Select(l => l.Key).Where(k => k != "ember" && !k.EndsWith("-phone", StringComparison.Ordinal))];

    private static (SkinSession Session, MediaPlayer Player) Start(string key, bool video = true)
    {
        TestPlatform.Ensure();
        var engine = new FakeEngine { VideoSize = video ? (640, 360) : (0, 0) };
        var player = new MediaPlayer(engine, new FakeInfoReader());
        var session = new SkinSession(Looks().Single(l => l.Key == key).Build(), player, new QuietHost(), new AvaloniaImageDecoder(), new MemoryPreferences());
        session.Start();
        player.OpenFiles([video ? "C:/media/movie.mp4" : "C:/media/song.mp3"]);
        Tick(session);
        return (session, player);
    }

    private static void Tick(SkinSession session)
    {
        for (var i = 0; i < 5; i++)
            session.Tick(TimeSpan.FromMilliseconds(100));
    }

    /// <summary>What a canvas asked of the player's window.</summary>
    private sealed class Seen
    {
        public int Enter, Exit, Picture, Background, Menu;

        public Seen(SkinCanvas canvas)
        {
            canvas.FullScreenEnterRequested += (_, _) => Enter++;
            canvas.FullScreenExitRequested += (_, _) => Exit++;
            canvas.PicturePressed += (_, _) => Picture++;
            canvas.BackgroundPressed += (_, _) => Background++;
            canvas.ContextRequestedOnSkin += (_, _) => Menu++;
        }
    }

    private static Window Show(Control content, double width, double height)
    {
        var window = new Window { Width = width, Height = height, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Click(Window window, Point at, MouseButton button = MouseButton.Left)
    {
        window.MouseDown(at, button);
        window.MouseUp(at, button);
        Dispatcher.UIThread.RunJobs();
    }

    [Theory]
    [MemberData(nameof(AllLooks))]
    public void A_double_click_on_the_video_asks_for_full_screen(string look)
    {
        // The look is built as in the other tests: its art is kept in drawing objects that belong to the thread making them.
        var (session, _) = Start(look);
        using var owned = session;
        TestPlatform.OnUiThread(() => DoubleClickOnVideo(session));
    }

    private static void DoubleClickOnVideo(SkinSession session)
    {
        var view = session.CurrentView!;
        var video = view.Elements.OfType<VideoElement>().Single(e => e.Visible && e.Width > 40);
        var canvas = new SkinCanvas { View = view };
        var seen = new Seen(canvas);
        var window = Show(canvas, view.Root.Width, view.Root.Height);
        try
        {
            var (x, y) = video.AbsolutePosition;
            var middle = new Point(x + video.Width / 2, y + video.Height / 2);
            Click(window, middle);
            // One click goes nowhere but to a window move that waits for the pointer to travel.
            Assert.Equal((0, 1, 0), (seen.Enter, seen.Picture, seen.Background));

            Click(window, middle);
            Assert.Equal((1, 1, 0), (seen.Enter, seen.Picture, seen.Background));

            Click(window, middle, MouseButton.Right);
            Assert.Equal(1, seen.Menu);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void In_full_screen_a_right_click_opens_the_menu_and_a_double_click_leaves()
    {
        var (session, _) = Start("ember");
        using var owned = session;
        TestPlatform.OnUiThread(() => MouseOverFullScreen(session));
    }

    private static void MouseOverFullScreen(SkinSession session)
    {
        var canvas = new SkinCanvas { View = session.CurrentView, FullScreen = true };
        var seen = new Seen(canvas);
        var window = Show(canvas, 860, 540);
        try
        {
            var middle = new Point(430, 200);
            Click(window, middle, MouseButton.Right);
            Assert.Equal((1, 0), (seen.Menu, seen.Exit));

            // Another place, so these are not counted with the click above.
            Click(window, new Point(100, 100), MouseButton.Middle);
            Assert.Equal((1, 0), (seen.Menu, seen.Exit));

            Click(window, new Point(600, 250));
            Click(window, new Point(600, 250));
            Assert.Equal((1, 1), (seen.Menu, seen.Exit));
        }
        finally
        {
            window.Close();
        }
    }

    [Theory]
    [MemberData(nameof(AllLooks))]
    public void Every_shuffle_repeat_and_mute_button_looks_different_when_it_is_on(string look)
    {
        var (session, player) = Start(look);
        using var owned = session;
        var buttons = session.CurrentView!.Elements.OfType<ButtonElement>()
            .Where(b => b.Tag is "SHUFFLEBUTTON" or "REPEATBUTTON" or "MUTEBUTTON").ToList();
        var off = buttons.ConvertAll(b => b.CurrentImage);

        player.Shuffle = true;
        player.Loop = true;
        player.Mute = true;
        Tick(session);

        for (var i = 0; i < buttons.Count; i++)
        {
            Assert.True(buttons[i].Down, $"{buttons[i].Tag} {buttons[i].Id}");
            Assert.NotSame(off[i], buttons[i].CurrentImage);
        }
    }

    [Theory]
    [MemberData(nameof(ListLooks))]
    public void The_playlist_column_can_be_made_wider_and_the_picture_gives_it_the_room(string look)
    {
        var (session, _) = Start(look);
        using var owned = session;
        var view = session.CurrentView!;
        var list = SideList.Find(view);
        Assert.NotNull(list);
        Assert.True(SideList.IsShown(list));
        var video = view.Elements.OfType<VideoElement>().Single(e => e.Visible && e.Width > 40);
        var playlist = view.Elements.OfType<PlaylistElement>().First(p => p.Parent == list);
        var usual = list.Width;
        (double Right, double VideoRight, double Video, double Rows) Measure() =>
            (list.AbsolutePosition.X + list.Width, video.AbsolutePosition.X + video.Width, video.Width, playlist.Width);
        var before = Measure();
        var gap = list.AbsolutePosition.X - before.VideoRight;

        // Room to grow: a window wider than the look's own.
        view.Root.Width += 200;
        var wide = Measure();
        SideList.Set(view, list, usual + 80, usual);

        var after = Measure();
        Assert.Equal(usual + 80, list.Width);
        Assert.Equal(wide.Right, after.Right); // its right edge stays put
        Assert.Equal(wide.Video - 80, after.Video);
        Assert.Equal(wide.Rows + 80, after.Rows);
        Assert.Equal(gap, list.AbsolutePosition.X - after.VideoRight); // neither a hole nor an overlap

        // The window is still followed afterwards.
        view.Root.Width -= 200;
        Assert.Equal(before.Right, Measure().Right);
        Assert.Equal(before.Video - 80, video.Width);
        Assert.Equal(gap, list.AbsolutePosition.X - Measure().VideoRight);

        // Back to the usual width: everything is where it was.
        SideList.Set(view, list, usual, usual);
        Assert.Equal(before, Measure());

        // The limits hold.
        SideList.Set(view, list, 5000, usual);
        Assert.Equal(SideList.Limits(list, usual).Max, list.Width);
        SideList.Set(view, list, 1, usual);
        Assert.Equal(SideList.Limits(list, usual).Min, list.Width);
    }

    [Fact]
    public void Winamps_equalizer_button_stays_lit_while_the_drawer_shows()
    {
        var (session, _) = Start("winamp", video: false);
        using var owned = session;
        var view = session.CurrentView!;
        var button = Assert.IsType<ButtonElement>(view.FindById("bEq"));
        Assert.False(button.Down);

        view.Script.Execute("ToggleEqualizer();", "test");
        Assert.True(button.Down);

        view.Script.Execute("ShowPane('library');", "test");
        Assert.False(button.Down);
        view.Script.Execute("ShowPane('now');", "test");
        Assert.True(button.Down);

        view.Script.Execute("ToggleEqualizer();", "test");
        Assert.False(button.Down);
    }

    [Fact]
    public void Glass_lights_its_video_buttons()
    {
        var (session, player) = Start("glass-phone");
        using var owned = session;
        var view = session.CurrentView!;
        var repeat = Assert.IsType<ButtonElement>(view.FindById("bRepeatV"));
        var subtitles = Assert.IsType<ButtonElement>(view.FindById("bCC"));
        var aspect = Assert.IsType<ButtonElement>(view.FindById("bAspect"));

        player.Loop = true;
        Tick(session);
        view.Script.Execute("OnPanels('', false, true, true, false, false);", "test");
        Assert.Equal((true, true, false), (repeat.Down, subtitles.Down, aspect.Down));
        view.Script.Execute("OnPanels('', false, false, true, false, true);", "test");
        Assert.Equal((false, true), (subtitles.Down, aspect.Down));
    }

    [Fact]
    public void Ember_lights_the_buttons_the_player_reports_and_puts_them_out_again()
    {
        var (session, _) = Start("ember");
        using var owned = session;
        var view = session.CurrentView!;
        string Image(string id) => view.Script.Evaluate(id + ".image")?.ToString() ?? string.Empty;

        view.Script.Execute("OnPanels('music', true, false, true, false, false);", "test");
        Assert.Equal(("i_side_active.png", "i_viz_active.png", "i_captions.png", "i_skins_active.png", "i_queue.png"),
            (Image("bSide"), Image("bViz"), Image("bCaptions"), Image("bSettings"), Image("bQueue")));
        Assert.Equal(("i_music_active.png", "i_home.png"), (Image("railMusic"), Image("railHome")));

        view.Script.Execute("OnPanels('', false, true, false, true, false);", "test");
        Assert.Equal(("i_side.png", "i_viz.png", "i_captions_active.png", "i_skins.png", "i_queue_active.png"),
            (Image("bSide"), Image("bViz"), Image("bCaptions"), Image("bSettings"), Image("bQueue")));
        Assert.Equal("i_music.png", Image("railMusic"));
    }

    [Theory]
    [InlineData(false, 100, 400)] // the library panel's right edge, dragged right: wider
    [InlineData(true, 100, 200)] // the queue's left edge, dragged right: narrower
    public void A_panels_edge_can_be_dragged_and_double_clicked(bool queue, int travel, int expected) => TestPlatform.OnUiThread(() =>
    {
        var host = new PanelHost();
        var panel = new UserControl { Width = 300, Height = 400 };
        panel.Content = EmberStyle.Resizable(panel, new Border(), host, queue);
        host.Panel = panel;
        Canvas.SetLeft(panel, 200);
        var window = Show(new Canvas { Children = { panel } }, 900, 500);
        try
        {
            var edge = new Point(queue ? 203 : 497, 200);
            window.MouseDown(edge, MouseButton.Left);
            window.MouseMove(edge + new Point(travel, 0), RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            window.MouseUp(edge + new Point(travel, 0), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal((queue, (double?)expected, true), host.Asked[^1]);
            Assert.False(host.Asked[0].Done);

            host.Asked.Clear();
            var now = new Point(queue ? 203 : 200 + expected - 3, 100); // away from the drag's start, so the clicks count from one
            Click(window, now);
            Click(window, now);
            Assert.Equal((queue, (double?)null, true), Assert.Single(host.Asked));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>Gives the panel the width asked for, as the player does, and remembers what was asked.</summary>
    private sealed class PanelHost : IEmberHost
    {
        public UserControl? Panel;
        public List<(bool Queue, double? Width, bool Done)> Asked { get; } = [];

        public void ResizePanel(bool queue, double? width, bool done)
        {
            Asked.Add((queue, width, done));
            Panel!.Width = width ?? 300;
            Dispatcher.UIThread.RunJobs();
        }

        public MediaPlayer Player => throw new NotSupportedException();
        public MediaLibrary Library => throw new NotSupportedException();
        public PlaylistStore Playlists => throw new NotSupportedException();
        public void OpenFiles() { }
        public void OpenUrl() { }
        public void AddFolder() { }
        public void ShowPage(string pane) { }
        public Task<string?> Ask(string question, string initial) => Task.FromResult<string?>(null);
    }

    private sealed class QuietHost : ISkinHost
    {
        public void ViewChanged(SkinView view) { }
        public void ResizeView(SkinView view, double width, double height) { }
        public (double Width, double Height) MaxViewSize => (1920, 1040);
        public void MinimizeView(SkinView view) { }
        public void BeginResize(SkinView view, string edge) { }
        public void CloseSkin() { }
        public void ReturnToFullMode() { }
        public string? ShowOpenFileDialog(string dialogType, string filter) => null;
        public void LaunchUrl(string url) { }
        public void ShowPopup(SkinView view, IReadOnlyList<string> items, int selectedIndex, Action<int> onSelected) { }
        public int ShowPopupModal(SkinView view, IReadOnlyList<string> items) => -1;
        public void ShowMessage(string text) { }
        public double MeasureText(string text, string fontFamily, double sizePoints, bool bold) => text.Length * sizePoints * 0.75;
        public object? RunCommand(string name, IReadOnlyList<object?> args) => null;
        public void Log(string message) { }
    }
}
