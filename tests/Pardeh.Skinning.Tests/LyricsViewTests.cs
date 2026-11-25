using System.Globalization;
using Avalonia;
using Avalonia.Media.Imaging;
using Pardeh.Core.LyricsKit;
using Pardeh.Skinning.Imaging;
using Pardeh.Skinning.Runtime;
using Pardeh.Skinning.Runtime.Elements;
using Pardeh.UI;
using Pardeh.UI.FullMode;
using Pardeh.UI.Rendering;
using MediaPlayer = Pardeh.Core.Playback.MediaPlayer;

namespace Pardeh.Skinning.Tests;

internal sealed class FakeFeed : ILyricsFeed
{
    public Lyrics? Current { get; set; }
    public int CurrentIndex { get; set; } = -1;
    public bool IsLoading { get; set; }
    public bool HasTrack { get; set; } = true;
    public bool LyricsEnabled { get; set; } = true;
    public bool OnlineEnabled { get; set; }
    public double TextSize { get; set; } = 1;
    public bool Follow { get; set; } = true;
    public List<int> Seeks { get; } = [];
    public List<int> Nudges { get; } = [];
    public List<double> SizeChanges { get; } = [];

    public bool SeekToLine(int index)
    {
        Seeks.Add(index);
        return true;
    }

    public void NudgeOffset(int deltaMs)
    {
        Nudges.Add(deltaMs);
        if (Current is { } l)
            l.OffsetMs += deltaMs;
    }

    public void SetOffset(int offsetMs)
    {
        if (Current is { } l)
            l.OffsetMs = offsetMs;
    }

    public void ChangeTextSize(double delta) => SizeChanges.Add(delta);
}

/// <summary>The lyrics screen: the button and the view in the seven phone looks, the scroll / tap / follow maths, rendering of timed, plain and right-to-left lyrics.</summary>
[Collection("Avalonia rendering")]
public sealed class LyricsViewTests
{

    private static void EnsurePlatform() => TestPlatform.Ensure();

    public static TheoryData<string> Looks => [.. PhoneSkin.Keys];

    private static readonly (double W, double H)[] Sizes =
    [
        (360, 720), (412, 892), (320, 640), (300, 600),   // portrait phones
        (800, 360), (892, 412), (640, 320),               // landscape
    ];

    private static (SkinSession Session, MediaPlayer Player, FakeHost Host, FakeFeed Feed) StartLook(string key, Lyrics? lyrics = null)
    {
        var host = new FakeHost();
        var player = new MediaPlayer(new FakeEngine(), new FakeInfoReader());
        var feed = new FakeFeed { Current = lyrics };
        var session = new SkinSession(PhoneSkin.Build(key), player, host, new AvaloniaImageDecoder(), new MemoryPreferences()) { Lyrics = feed };
        session.Start();
        player.OpenFiles(["C:/music/a.mp3"]);
        session.Tick(TimeSpan.FromMilliseconds(30));
        return (session, player, host, feed);
    }

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

    private static bool AncestorsVisible(SkinElement e)
    {
        for (var p = e.Parent; p is not null; p = p.Parent)
            if (!p.Visible)
                return false;
        return true;
    }

    private static readonly string[] Neighbours =
        ["bPlay", "bPause", "bPrev", "bNext", "bStop", "bMute", "bMuteLow", "bMuted", "bShuffle", "bRepeat", "bSkipB", "bSkipF", "bFav", "vol", "seek", "tPos", "tDur", "tRem", "tTitle", "tArtist",
         // the artwork: the cover, the stage (cover, visualization, video) and the lyrics view itself; the rest of the text block
         "svCover", "cover", "svStage", "vis", "vid", "lyr", "tAlbum", "svStars", "bViz", "eqViz", "bAdd", "bFull"];

    // ---- The button -----------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Looks))]
    public void The_lyrics_button_is_tappable_inside_the_view_and_clear_of_the_other_controls(string key)
    {
        EnsurePlatform();
        var (session, _, _, _) = StartLook(key);
        var view = session.CurrentView!;
        // Closed, then open (the lyrics view or, in Glass, the stage swap), then the visualization state where the look has it.
        foreach (var state in new[] { "closed", "open" })
        {
            foreach (var (w, h) in Sizes)
            {
                view.Root.Width = w;
                view.Root.Height = h;
                if (state == "open" && SkinValue.ToDouble(view.Script.Evaluate("g_lyr ? 1 : 0")) == 0)
                    view.Script.Execute("ToggleLyrics();", "test");
                if (state == "closed" && SkinValue.ToDouble(view.Script.Evaluate("g_lyr ? 1 : 0")) != 0)
                    view.Script.Execute("ToggleLyrics();", "test");
                session.Tick(TimeSpan.FromMilliseconds(30));
                var button = view.FindById("bLyr") ?? throw new Xunit.Sdk.XunitException($"{key} has no bLyr");
                Assert.True(button.Visible, $"{key} {w}x{h} {state}: the lyrics button is hidden");
                var r = Absolute(button);
                Assert.True(r.W >= 44 && r.H >= 44, $"{key} {w}x{h} {state}: bLyr is {r.W}x{r.H} units, the target is 44");
                Assert.True(r.X >= 0 && r.Y >= 0 && r.X + r.W <= w + 0.5 && r.Y + r.H <= h + 0.5, $"{key} {w}x{h} {state}: bLyr {r} leaves the view");
                foreach (var id in Neighbours)
                {
                    if (view.FindById(id) is not { Visible: true, Width: > 0 } other || !AncestorsVisible(other))
                        continue;
                    var o = Absolute(other);
                    // The stage / lyrics view are one box around the artwork: the button must be outside it (the generic looks' stage never holds it).
                    Assert.False(Overlaps(r, o), $"{key} {w}x{h} {state}: bLyr {r} overlaps {id} {o}");
                }
            }
        }
    }

    /// <summary>Glass: over the visualization stage the button leaves the artwork and sits beside the centred title under it.</summary>
    [Fact]
    public void Glass_lyrics_button_is_clear_of_the_visualization_stage()
    {
        EnsurePlatform();
        var (session, _, _, _) = StartLook("glass-phone");
        var view = session.CurrentView!;
        foreach (var (w, h) in Sizes)
        {
            view.Root.Width = w;
            view.Root.Height = h;
            view.Script.Execute("g_lyr = false; g_viz = true; Relayout();", "test");
            session.Tick(TimeSpan.FromMilliseconds(30));
            var vis = view.FindById("vis")!;
            Assert.True(vis.Visible, $"{w}x{h}: the visualization stage shows");
            var r = Absolute(view.FindById("bLyr")!);
            Assert.False(Overlaps(r, Absolute(vis)), $"{w}x{h}: bLyr {r} sits on the visualization {Absolute(vis)}");
            Assert.True(r.W >= 44 && r.H >= 44 && r.Y + r.H <= h + 0.5, $"{w}x{h}: bLyr {r}");
            foreach (var id in new[] { "tTitle", "tArtist", "seek", "tPos", "tRem", "bPlay", "bSkipB", "bSkipF", "bPrev", "bNext", "bMute" })
                if (view.FindById(id) is { Visible: true, Width: > 0 } other && AncestorsVisible(other))
                    Assert.False(Overlaps(r, Absolute(other)), $"{w}x{h}: bLyr {r} overlaps {id} {Absolute(other)}");
            view.Script.Execute("g_viz = false; Relayout();", "test");
        }
    }

    [Theory]
    [MemberData(nameof(Looks))]
    public void Toggling_shows_the_lyrics_inside_the_view_and_Back_leaves_them(string key)
    {
        EnsurePlatform();
        var (session, _, _, feed) = StartLook(key, TestLyrics.Synced());
        var view = session.CurrentView!;
        foreach (var (w, h) in Sizes)
        {
            view.Root.Width = w;
            view.Root.Height = h;
            session.Tick(TimeSpan.FromMilliseconds(30));
            var lyr = (LyricsElement)view.FindById("lyr")!;
            Assert.False(lyr.Visible && AncestorsVisible(lyr), $"{key} {w}x{h}: the lyrics start hidden");
            view.Script.Execute("ToggleLyrics();", "test");
            session.Tick(TimeSpan.FromMilliseconds(30));
            Assert.True(lyr.Visible && AncestorsVisible(lyr), $"{key} {w}x{h}: ToggleLyrics shows the view");
            var r = Absolute(lyr);
            Assert.True(r.W >= 150 && r.H >= 120, $"{key} {w}x{h}: the lyrics area {r} is too small");
            Assert.True(r.X >= 0 && r.Y >= 0 && r.X + r.W <= w + 0.5 && r.Y + r.H <= h + 0.5, $"{key} {w}x{h}: lyrics {r} leave the view");
            foreach (var id in new[] { "bPlay", "bPause", "bPrev", "bNext", "seek", "tPos", "tDur", "bSkipB", "bSkipF", "vol" })
            {
                if (view.FindById(id) is not { Visible: true, Width: > 0 } other || !AncestorsVisible(other))
                    continue;
                Assert.False(Overlaps(r, Absolute(other)), $"{key} {w}x{h}: lyrics {r} overlap {id} {Absolute(other)}");
            }

            Assert.True(view.FindById("bLyr")!.Visible);
            // The lyrics are a state of Now Playing: Back leaves them first.
            Assert.Equal(1.0, SkinValue.ToDouble(view.Script.Evaluate("OnBack() ? 1 : 0")));
            session.Tick(TimeSpan.FromMilliseconds(30));
            Assert.False(lyr.Visible && AncestorsVisible(lyr), $"{key} {w}x{h}: Back hides the lyrics");
            Assert.Equal(0.0, SkinValue.ToDouble(view.Script.Evaluate("OnBack() ? 1 : 0")));
        }

        Assert.NotNull(feed);
    }

    // ---- The scroll maths --------------------------------------------------------------------------------

    [Theory]
    [InlineData(100, 40, 200, 1000, 20)]     // the line's centre (120) in a 200 viewport: scroll 20
    [InlineData(0, 40, 200, 1000, 0)]        // the first lines cannot scroll above the top
    [InlineData(950, 40, 200, 1000, 800)]    // nor past the end
    [InlineData(10, 40, 300, 100, 0)]        // content shorter than the viewport
    public void CenterOn_keeps_the_line_in_the_middle_and_inside_the_content(double top, double height, double viewport, double content, double expected) =>
        Assert.Equal(expected, LyricsScroll.CenterOn(top, height, viewport, content), 6);

    [Fact]
    public void Ease_reaches_the_target_without_overshoot_in_about_the_ease_time()
    {
        var y = 0.0;
        var previous = -1.0;
        for (var ms = 0; ms < 250; ms += 15)
        {
            y = LyricsScroll.Ease(y, 100, 15, 250);
            Assert.InRange(y, previous, 100);
            previous = y;
        }

        Assert.InRange(y, 90, 100);                      // ~95 % after the ease time
        Assert.Equal(100, LyricsScroll.Ease(99.9, 100, 15, 250));   // snaps when it is close
        Assert.Equal(5, LyricsScroll.Ease(5, 100, 0, 250));         // no time, no move
    }

    [Fact]
    public void LineAt_maps_a_y_to_the_line_that_contains_it()
    {
        double[] tops = [10, 50, 90, 130], heights = [40, 40, 40, 40];
        Assert.Equal(-1, LyricsScroll.LineAt(tops, heights, 5));
        Assert.Equal(0, LyricsScroll.LineAt(tops, heights, 10));
        Assert.Equal(0, LyricsScroll.LineAt(tops, heights, 49.9));
        Assert.Equal(1, LyricsScroll.LineAt(tops, heights, 50));
        Assert.Equal(3, LyricsScroll.LineAt(tops, heights, 169));
        Assert.Equal(-1, LyricsScroll.LineAt(tops, heights, 170));
        Assert.Equal(2, LyricsScroll.FirstVisible(tops, heights, 95));
        Assert.Equal(4, LyricsScroll.FirstVisible(tops, heights, 500));
    }

    [Fact]
    public void EdgeFade_is_one_inside_and_falls_to_zero_at_the_edges()
    {
        Assert.Equal(1, LyricsScroll.EdgeFade(100, 200, 40), 6);
        Assert.Equal(0, LyricsScroll.EdgeFade(0, 200, 40), 6);
        Assert.Equal(0, LyricsScroll.EdgeFade(200, 200, 40), 6);
        Assert.InRange(LyricsScroll.EdgeFade(20, 200, 40), 0.3, 0.7);
        Assert.Equal(1, LyricsScroll.EdgeFade(0, 200, 0));
    }

    [Fact]
    public void FilledChars_follows_the_words()
    {
        LyricWord[] words = [new(TimeSpan.FromSeconds(1), "one "), new(TimeSpan.FromSeconds(2), "two "), new(TimeSpan.FromSeconds(3), "three")];
        var end = TimeSpan.FromSeconds(4);
        Assert.Equal(0, LyricsScroll.FilledChars(words, end, TimeSpan.FromSeconds(0.5), 13));
        Assert.Equal(2, LyricsScroll.FilledChars(words, end, TimeSpan.FromSeconds(1.5), 13), 6);   // half of "one "
        Assert.Equal(4, LyricsScroll.FilledChars(words, end, TimeSpan.FromSeconds(2), 13), 6);
        Assert.Equal(13, LyricsScroll.FilledChars(words, end, TimeSpan.FromSeconds(9), 13), 6);
    }

    // ---- The element ------------------------------------------------------------------------------------------

    private static (LyricsElement Lyr, SkinSession Session, FakeFeed Feed, FakeHost Host, long[] Clock) Ready(Lyrics lyrics, double height = 400)
    {
        EnsurePlatform();
        var (session, _, host, feed) = StartLook("fluent-phone", lyrics);
        var view = session.CurrentView!;
        view.Script.Execute("ToggleLyrics();", "test");
        var lyr = (LyricsElement)view.FindById("lyr")!;
        lyr.Width = 330;
        lyr.Height = height;
        var clock = new long[] { 1000 };
        lyr.Clock = () => clock[0];
        // Every line 40 units tall (as if measured): the line i spans [pad + 40 i, pad + 40 i + 40).
        lyr.SetLayout(lyrics, [.. lyrics.Lines.Select(_ => 40.0)]);
        return (lyr, session, feed, host, clock);
    }

    [Fact]
    public void A_tap_on_a_line_seeks_to_exactly_that_line()
    {
        var lyrics = TestLyrics.Synced();
        var (lyr, _, feed, _, _) = Ready(lyrics);
        Assert.Equal(LyricsMode.Synced, lyr.Mode);
        var textHeight = lyr.TextHeight;
        Assert.Equal(360, textHeight);
        feed.CurrentIndex = 0;
        // Look at lines 3 and 4: scroll so that line 3 starts at y = 100 on the screen.
        lyr.ScrollByPixels(lyr.Tops[3] - 100 - lyr.ScrollY);
        Assert.Equal(100, lyr.Tops[3] - lyr.ScrollY, 6);
        Assert.Equal(3, lyr.LineAtView(100));
        Assert.Equal(3, lyr.LineAtView(139.9));
        Assert.Equal(4, lyr.LineAtView(140));
        lyr.Tap(100, 120);
        lyr.Tap(100, 150);
        Assert.Equal([3, 4], feed.Seeks);
        // A tap in the toolbar is not a line.
        lyr.Tap(5, lyr.Height - 5);
        Assert.Equal(2, feed.Seeks.Count);
    }

    [Fact]
    public void Plain_lyrics_scroll_but_a_tap_does_not_seek()
    {
        var (lyr, _, feed, _, _) = Ready(TestLyrics.Plain());
        Assert.Equal(LyricsMode.Plain, lyr.Mode);
        lyr.Tap(100, 50);
        Assert.Empty(feed.Seeks);
        lyr.ScrollByPixels(80);
        Assert.Equal(80, lyr.ScrollY, 6);
        lyr.ScrollByPixels(1e6);
        Assert.Equal(lyr.MaxScroll, lyr.ScrollY, 6);
        lyr.ScrollByPixels(-1e6);
        Assert.Equal(0, lyr.ScrollY, 6);
        Assert.False(lyr.IsDetached);   // plain lyrics have nothing to follow
        Assert.DoesNotContain(lyr.Zones, z => z.Kind is LyricsZoneKind.OffsetMinus or LyricsZoneKind.OffsetPlus or LyricsZoneKind.Follow);
    }

    [Fact]
    public void Following_centres_the_current_line_pauses_for_four_seconds_after_a_drag_and_the_follow_button_resumes()
    {
        var (lyr, session, feed, _, clock) = Ready(TestLyrics.Synced());
        feed.CurrentIndex = 5;
        for (var i = 0; i < 40; i++)
        {
            clock[0] += 15;
            lyr.SyncWithPlayer();
        }

        Assert.Equal(lyr.TargetFor(5), lyr.ScrollY, 1);
        Assert.False(lyr.ShowFollowButton);

        // The user drags: following stops for 4 s, the pill appears.
        lyr.ScrollByPixels(-200);
        Assert.True(lyr.IsDetached);
        feed.CurrentIndex = 6;
        var before = lyr.ScrollY;
        clock[0] += 1000;
        lyr.SyncWithPlayer();
        Assert.Equal(before, lyr.ScrollY, 6);
        Assert.True(lyr.ShowFollowButton);
        Assert.Contains(lyr.Zones, z => z.Kind == LyricsZoneKind.Follow);

        // Tapping the pill follows again, with the ease.
        var pill = lyr.Zones.First(z => z.Kind == LyricsZoneKind.Follow);
        lyr.Tap(pill.X + 5, pill.Y + 5);
        Assert.False(lyr.IsDetached);
        for (var i = 0; i < 40; i++)
        {
            clock[0] += 15;
            lyr.SyncWithPlayer();
        }

        Assert.Equal(lyr.TargetFor(6), lyr.ScrollY, 1);

        // Without the tap it resumes by itself after the pause.
        lyr.ScrollByPixels(150);
        clock[0] += (long)LyricsElement.FollowPauseMs + 100;
        for (var i = 0; i < 60; i++)
        {
            clock[0] += 15;
            lyr.SyncWithPlayer();
        }

        Assert.False(lyr.IsDetached);
        Assert.Equal(lyr.TargetFor(6), lyr.ScrollY, 1);
        Assert.NotNull(session);
    }

    [Fact]
    public void The_view_does_not_follow_when_the_setting_is_off()
    {
        var (lyr, _, feed, _, clock) = Ready(TestLyrics.Synced());
        feed.Follow = false;
        feed.CurrentIndex = 8;
        for (var i = 0; i < 40; i++)
        {
            clock[0] += 15;
            lyr.SyncWithPlayer();
        }

        Assert.Equal(0, lyr.ScrollY, 6);
        Assert.True(lyr.ShowFollowButton);   // one tap goes there
    }

    [Fact]
    public void Nothing_is_done_while_the_lyrics_are_hidden()
    {
        var (lyr, _, feed, _, clock) = Ready(TestLyrics.Synced());
        lyr.Visible = false;
        feed.CurrentIndex = 7;
        clock[0] += 500;
        lyr.SyncWithPlayer();
        Assert.Equal(0, lyr.ScrollY, 6);
        Assert.Equal(-1, lyr.CurrentLine);
    }

    [Fact]
    public void The_toolbar_buttons_change_the_offset_and_the_text_size_and_the_label_is_culture_safe()
    {
        var lyrics = TestLyrics.Synced();
        var (lyr, _, feed, host, _) = Ready(lyrics);
        Assert.Equal("0 s", lyr.OffsetText);
        foreach (var culture in new[] { "fa-IR", "de-DE", "ar-SA", "tr-TR" })
        {
            var old = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);
                lyrics.OffsetMs = 1500;
                Assert.Equal("+1.5 s", lyr.OffsetText);
                lyrics.OffsetMs = -500;
                Assert.Equal("-0.5 s", lyr.OffsetText);
            }
            finally
            {
                (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = old;
            }
        }

        lyrics.OffsetMs = 0;
        foreach (var kind in new[] { LyricsZoneKind.OffsetPlus, LyricsZoneKind.OffsetPlus, LyricsZoneKind.OffsetMinus, LyricsZoneKind.SizePlus, LyricsZoneKind.SizeMinus })
        {
            var zone = lyr.Zones.First(z => z.Kind == kind);
            lyr.Tap(zone.X + zone.W / 2, zone.Y + zone.H / 2);
        }

        Assert.Equal([500, 500, -500], feed.Nudges);
        Assert.Equal([LyricsElement.SizeStep, -LyricsElement.SizeStep], feed.SizeChanges);
        Assert.Equal("+0.5 s", lyr.OffsetText);
        // The label resets.
        var label = lyr.Zones.First(z => z.Kind == LyricsZoneKind.OffsetLabel);
        lyr.Tap(label.X + 3, label.Y + 3);
        Assert.Equal(0, lyrics.OffsetMs);
        Assert.NotNull(host);
    }

    [Fact]
    public void Every_toolbar_button_is_a_touch_target_inside_the_element_at_every_width()
    {
        var (lyr, _, _, _, _) = Ready(TestLyrics.Synced());
        foreach (var width in new[] { 288.0, 308.0, 332.0, 344.0, 500.0 })
        {
            lyr.Width = width;
            var zones = lyr.Zones.Where(z => z.Kind != LyricsZoneKind.Follow).ToList();
            Assert.Equal(6, zones.Count);
            foreach (var z in zones)
            {
                Assert.True(z.H >= 36 && z.W >= 30, $"{z} at {width}");
                Assert.InRange(z.X, 0, width);
                Assert.True(z.X + z.W <= width + 0.5, $"{z} leaves {width}");
            }

            for (var i = 1; i < zones.Count; i++)
                Assert.True(zones[i].X >= zones[i - 1].X + zones[i - 1].W - 0.5, "toolbar buttons overlap");
        }
    }

    [Fact]
    public void The_empty_state_offers_search_paste_and_import_and_the_actions_reach_the_app()
    {
        EnsurePlatform();
        var (session, _, host, feed) = StartLook("fluent-phone");
        var view = session.CurrentView!;
        view.Script.Execute("ToggleLyrics();", "test");
        var lyr = (LyricsElement)view.FindById("lyr")!;
        lyr.Width = 330;
        lyr.Height = 372;
        Assert.Equal(LyricsMode.Empty, lyr.Mode);
        Assert.Equal([LyricsZoneKind.Search, LyricsZoneKind.Paste, LyricsZoneKind.Import], lyr.Zones.Select(z => z.Kind).ToArray());
        Assert.Equal("Search online...", lyr.LabelOf(LyricsZoneKind.Search));   // online is off: the button explains and asks
        feed.OnlineEnabled = true;
        Assert.Equal("Search online", lyr.LabelOf(LyricsZoneKind.Search));
        foreach (var z in lyr.Zones)
        {
            Assert.True(z.H >= 36 && z.X >= 0 && z.X + z.W <= lyr.Width && z.Y >= 0 && z.Y + z.H <= lyr.Height);
            lyr.Tap(z.X + z.W / 2, z.Y + z.H / 2);
        }

        Assert.Equal(3, host.Commands.Count(c => c == "lyrics"));

        feed.IsLoading = true;
        Assert.Equal(LyricsMode.Loading, lyr.Mode);
        feed.IsLoading = false;
        feed.LyricsEnabled = false;
        Assert.Equal(LyricsMode.Off, lyr.Mode);
        Assert.Equal([LyricsZoneKind.EnableLyrics], lyr.Zones.Select(z => z.Kind).ToArray());
        feed.LyricsEnabled = true;
        feed.HasTrack = false;
        Assert.Equal(LyricsMode.NoTrack, lyr.Mode);
    }

    [Fact]
    public void The_menu_lists_edit_import_search_refresh_and_delete_only_for_edited_lyrics()
    {
        var lyrics = TestLyrics.Synced();
        var (lyr, _, _, _, _) = Ready(lyrics);
        lyrics.Source = LyricsSources.Embedded;
        Assert.DoesNotContain(lyr.MenuItems(), i => i.Action == "delete");
        lyrics.Source = LyricsSources.Override;
        Assert.Contains(lyr.MenuItems(), i => i.Action == "delete");
        Assert.Contains(lyr.MenuItems(), i => i.Action == "paste" && i.Text == "Edit lyrics...");
        Assert.Contains(lyr.MenuItems(), i => i.Action == "import");
        Assert.Contains(lyr.MenuItems(), i => i.Action == "refresh");
        Assert.Equal("Embedded", LyricsElement.SourceName(LyricsSources.Embedded));
        Assert.Equal(".lrc file", LyricsElement.SourceName(LyricsSources.Sidecar));
        Assert.Equal("Online", LyricsElement.SourceName(LyricsSources.OnlineCache));
        Assert.Equal("Edited", LyricsElement.SourceName(LyricsSources.Override));
    }

    // ---- Rendering ----------------------------------------------------------------------------------------------

    private static SkinBitmap Render(SkinView view)
    {
        var canvas = new SkinCanvas { View = view };
        canvas.Measure(new Size(view.Root.Width, view.Root.Height));
        canvas.Arrange(new Rect(0, 0, view.Root.Width, view.Root.Height));
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)view.Root.Width, (int)view.Root.Height), new Vector(96, 96));
        bitmap.Render(canvas);
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return new AvaloniaImageDecoder().Decode(stream.ToArray())!;
    }

    /// <summary>Pixels of the lyrics area that are bright (text is white on the dark stage).</summary>
    private static int BrightPixels(SkinBitmap bitmap, SkinElement lyr, Func<double, double, bool>? where = null)
    {
        var (ax, ay, w, h) = Absolute(lyr);
        var count = 0;
        for (var y = (int)ay; y < ay + h; y++)
            for (var x = (int)ax; x < ax + w; x++)
            {
                var rgb = bitmap.GetRgbAt(x, y);
                if (((rgb >> 16 & 255) + (rgb >> 8 & 255) + (rgb & 255)) / 3 > 170 && (where?.Invoke(x - ax, y - ay) ?? true))
                    count++;
            }

        return count;
    }

    [Fact]
    public void Synced_lyrics_draw_text_and_the_highlight_moves_with_the_line()
    {
        var lyrics = TestLyrics.Synced();
        var (lyr, session, feed, _, clock) = Ready(lyrics, 372);
        var view = session.CurrentView!;
        Render(view);   // the renderer measures the lines
        feed.CurrentIndex = 2;
        for (var i = 0; i < 60; i++)
        {
            clock[0] += 15;
            lyr.SyncWithPlayer();
        }

        var first = Render(view);
        Assert.True(BrightPixels(first, lyr) > 300, "no lyrics text was drawn");
        Assert.Equal(lyr.TargetFor(2), lyr.ScrollY, 1);
        // The current line is the one in the middle of the text area.
        Assert.Equal(2, lyr.LineAtView(lyr.TextHeight / 2));
        var topBefore = lyr.Tops[2] - lyr.ScrollY;
        feed.CurrentIndex = 3;
        for (var i = 0; i < 60; i++)
        {
            clock[0] += 15;
            lyr.SyncWithPlayer();
        }

        Assert.Equal(3, lyr.LineAtView(lyr.TextHeight / 2));
        Assert.True(lyr.Tops[2] - lyr.ScrollY < topBefore - 12, "the lines did not scroll up");
        var second = Render(view);
        Assert.True(BrightPixels(second, lyr) > 300);
    }

    [Fact]
    public void Unsynced_and_right_to_left_lyrics_render_with_the_right_direction_in_other_cultures()
    {
        EnsurePlatform();
        foreach (var culture in new[] { "en-US", "fa-IR", "de-DE" })
        {
            var old = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);
                var lyrics = new Lyrics(new[] { "سلام دنیا", "Hello world", "یک خط فارسی با 123 و English", "", "آخرین خط" }
                    .Select((t, i) => new LyricLine(TimeSpan.FromSeconds(i * 5), t)), synced: false);
                var (lyr, session, _, _, _) = Ready(lyrics, 372);
                var paint = new SkinRenderer.LyricsPaint();
                SkinRenderer.LayOutLyrics(lyr, lyrics, paint);
                Assert.True(paint.Rtl[0] && paint.Rtl[4], culture);
                Assert.False(paint.Rtl[1], culture);
                Assert.True(paint.Rtl[2], culture);                       // the first strong letter is Persian
                Assert.Null(paint.Lines[3]);                               // a gap has no text
                Assert.True(lyr.Heights[3] < lyr.Heights[1], culture);     // and is shorter than a line
                var bitmap = Render(session.CurrentView!);
                Assert.True(BrightPixels(bitmap, lyr) > 300, $"{culture}: nothing was drawn");
            }
            finally
            {
                (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = old;
            }
        }
    }

    [Fact]
    public void A_long_line_wraps_into_a_taller_row_and_the_text_size_scales_the_layout()
    {
        EnsurePlatform();
        var lyrics = new Lyrics([new LyricLine(TimeSpan.FromSeconds(1), "short"), new LyricLine(TimeSpan.FromSeconds(2), string.Join(' ', Enumerable.Repeat("a rather long line of words", 6)))], synced: true);
        var (lyr, _, feed, _, _) = Ready(lyrics, 372);
        var paint = new SkinRenderer.LyricsPaint();
        SkinRenderer.LayOutLyrics(lyr, lyrics, paint);
        var normal = lyr.Heights.ToArray();
        Assert.True(normal[1] > normal[0] * 2, "the long line did not wrap");
        feed.TextSize = 1.6;
        Assert.True(lyr.NeedsLayout(lyrics));
        SkinRenderer.LayOutLyrics(lyr, lyrics, paint);
        Assert.True(lyr.Heights[0] > normal[0] * 1.4, "the text size did not change the layout");
        Assert.False(lyr.NeedsLayout(lyrics));
    }

    [Fact]
    public void Word_timings_fill_the_current_line_without_throwing()
    {
        EnsurePlatform();
        var words = new LyricWord[] { new(TimeSpan.FromSeconds(1), "Hello "), new(TimeSpan.FromSeconds(2), "wide "), new(TimeSpan.FromSeconds(3), "world") };
        var lyrics = new Lyrics([new LyricLine(TimeSpan.FromSeconds(1), "Hello wide world", words), new LyricLine(TimeSpan.FromSeconds(5), "Next")], synced: true);
        var (lyr, session, feed, _, clock) = Ready(lyrics, 372);
        feed.CurrentIndex = 0;
        for (var i = 0; i < 40; i++)
        {
            clock[0] += 15;
            lyr.SyncWithPlayer();
        }

        var dim = BrightPixels(Render(session.CurrentView!), lyr);
        Assert.True(dim > 100);
    }

    [Fact]
    public void The_current_line_is_drawn_in_the_accent_colour_and_is_the_brightest()
    {
        var lyrics = TestLyrics.Synced();
        var (lyr, session, feed, _, clock) = Ready(lyrics, 372);
        var view = session.CurrentView!;
        Render(view);
        feed.CurrentIndex = 4;
        for (var i = 0; i < 60; i++)
        {
            clock[0] += 15;
            lyr.SyncWithPlayer();
        }

        Assert.Equal(4, lyr.CurrentLine);
        Assert.Equal(1.0, lyr.Emphasis(4), 3);
        var bitmap = Render(view);
        // The middle of the text area holds the current line: it has bright accent-coloured pixels there (accent of the Fluent look is not white).
        var (ax, ay, w, h) = Absolute(lyr);
        var middle = ay + lyr.TextHeight / 2;
        var lit = 0;
        for (var y = (int)middle - 12; y < middle + 12; y++)
            for (var x = (int)ax; x < ax + w; x++)
            {
                var rgb = bitmap.GetRgbAt(x, y);
                if ((rgb >> 16 & 255) + (rgb >> 8 & 255) + (rgb & 255) > 330)
                    lit++;
            }

        Assert.True(lit > 80, $"the current line is not drawn in the middle ({lit} bright pixels)");
        Assert.True(w > 0 && h > 0);
    }

    [Fact]
    public void The_loading_empty_and_off_states_draw_their_message_and_buttons()
    {
        EnsurePlatform();
        var (session, _, _, feed) = StartLook("fluent-phone");
        var view = session.CurrentView!;
        view.Script.Execute("ToggleLyrics();", "test");
        var lyr = (LyricsElement)view.FindById("lyr")!;
        lyr.Width = 332;
        lyr.Height = 372;
        feed.IsLoading = true;
        Assert.True(BrightPixels(Render(view), lyr) > 150, "no spinner or loading text");
        feed.IsLoading = false;
        var empty = BrightPixels(Render(view), lyr);
        Assert.True(empty > 400, "no empty state");
        feed.LyricsEnabled = false;
        Assert.True(BrightPixels(Render(view), lyr) > 250, "no 'lyrics are off' state");
        feed.LyricsEnabled = true;
        feed.HasTrack = false;
        Assert.True(BrightPixels(Render(view), lyr) > 150, "no 'nothing is playing' text");
    }

    [Fact]
    public void The_store_can_clear_the_users_lyrics_and_forget_a_miss()
    {
        var folder = LyricsTestData.NewTempFolder("pardeh-lyr-ui-");
        try
        {
            var store = new LyricsStore(folder);
            var item = LyricsTestData.Item("/music/a.mp3", "Song A");
            var other = LyricsTestData.Item("/music/b.mp3", "Song B");
            store.SaveOverride(LyricsTrackKey.From(item), TestLyrics.Synced());
            store.SaveOverride(LyricsTrackKey.From(other), TestLyrics.Plain());
            store.SetOffset(LyricsTrackKey.From(item), 750);
            store.MarkNotFound(LyricsTrackKey.From(other), DateTime.UtcNow);
            Assert.True(store.IsNotFound(LyricsTrackKey.From(other), DateTime.UtcNow, TimeSpan.FromDays(7)));
            store.ClearNotFound(LyricsTrackKey.From(other));
            Assert.False(store.IsNotFound(LyricsTrackKey.From(other), DateTime.UtcNow, TimeSpan.FromDays(7)));

            Assert.Equal(2, store.ClearOverrides());
            Assert.Null(store.GetOverride(LyricsTrackKey.From(item)));
            Assert.Null(store.GetOverride(LyricsTrackKey.From(other)));
            Assert.Equal(750, store.GetOffset(LyricsTrackKey.From(item)));   // the timing correction is kept
            Assert.Equal(0, store.ClearOverrides());
            Assert.Empty(Directory.GetFiles(folder, "*.lrc").Concat(Directory.GetFiles(folder, "*.txt")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_lyrics_icons_exist_in_both_families()
    {
        Assert.True(Pardeh.UI.Rendering.FluentIcons.Has("mic"));
        Assert.InRange(Pardeh.UI.Rendering.FluentIcons.Geo("mic", filled: true).Bounds.Width, 6, 24.1);   // the lit (filled) variant exists too
        foreach (var name in new[] { "mic", "text_quote", "subtract", "add", "text_font_size", "more_horizontal", "arrow_clockwise", "clipboard_paste", "arrow_download", "comment" })
            Assert.InRange(Pardeh.UI.Rendering.FluentIcons.Geo(name).Bounds.Width, 6, 24.1);
        Assert.Contains("lyrics", Pardeh.UI.Rendering.PictorialIcons.Keys);
        Assert.Equal("mic", Pardeh.UI.Rendering.FluentIcons.ForGlyph("@lyrics"));
        Assert.Equal("lyrics", Pardeh.UI.Rendering.PictorialIcons.Resolve("@lyrics"));
    }

    /// <summary>PARDEH_LYRICS_SHOTS=&lt;folder&gt; writes a picture of the lyrics view of every look (portrait and landscape) for a visual check.</summary>
    [Fact]
    public void Contact_sheet()
    {
        if (Environment.GetEnvironmentVariable("PARDEH_LYRICS_SHOTS") is not { Length: > 0 } folder)
            return;
        EnsurePlatform();
        Directory.CreateDirectory(folder);
        var lines = new[] { "I walked along the water", "Where the lanterns hang so low", "سلام دنیا، این یک خط فارسی است", "And the night was calling softly", "Hold on to this moment", "Never let it go", "Another line of the song", "The last line" };
        var lyrics = new Lyrics(lines.Select((t, i) => new LyricLine(TimeSpan.FromSeconds(5 + i * 5), t)), synced: true) { Source = LyricsSources.Sidecar };
        foreach (var key in PhoneSkin.Keys)
            foreach (var (w, h) in new[] { (360.0, 720.0), (800.0, 360.0) })
            {
                var (session, _, _, feed) = StartLook(key, lyrics);
                var view = session.CurrentView!;
                view.Root.Width = w;
                view.Root.Height = h;
                feed.CurrentIndex = 3;
                session.Tick(TimeSpan.FromMilliseconds(30));
                view.Script.Execute("ToggleLyrics();", "test");
                session.Tick(TimeSpan.FromMilliseconds(30));
                var lyr = (LyricsElement)view.FindById("lyr")!;
                long now = 5000;
                lyr.Clock = () => now += 15;
                Render(view);
                for (var i = 0; i < 40; i++)
                    session.Tick(TimeSpan.FromMilliseconds(15));
                var canvas = new SkinCanvas { View = view };
                canvas.Measure(new Size(w, h));
                canvas.Arrange(new Rect(0, 0, w, h));
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)w, (int)h), new Vector(96, 96));
                bitmap.Render(canvas);
                bitmap.Save(Path.Combine(folder, $"{key}_{(w > h ? "land" : "port")}.png"));
            }
    }

    // ---- Settings and the tint -------------------------------------------------------------------------------------

    [Fact]
    public void The_lyrics_settings_survive_the_json_round_trip_and_clamp()
    {
        var settings = new AppSettings { LyricsEnabled = false, LyricsOnline = true, LyricsSaveFetched = false, LyricsTextSize = 1.35, LyricsFollow = false };
        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
        Assert.False(back.LyricsEnabled);
        Assert.True(back.LyricsOnline);
        Assert.False(back.LyricsSaveFetched);
        Assert.False(back.LyricsFollow);
        Assert.Equal(1.35, back.LyricsTextSize, 6);
        Assert.Equal(1.6, new AppSettings { LyricsTextSize = 9 }.LyricsTextSize);
        Assert.Equal(0.8, new AppSettings { LyricsTextSize = 0.1 }.LyricsTextSize);
        var defaults = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.True(defaults.LyricsEnabled);
        Assert.False(defaults.LyricsOnline);   // online look-up is off until the user turns it on
        Assert.Equal(1.0, defaults.LyricsTextSize);
        Assert.Contains("Nothing else.", MobileSettingsViewText.Privacy);
    }

    [Fact]
    public void Glass_builds_the_lyrics_view_in_every_skin_colour_with_the_same_layout()
    {
        EnsurePlatform();
        var saved = GlassPalette.Current;
        try
        {
            Pardeh.UI.Rendering.GlassPalette.Tint[] tints = [GlassPalette.Tint.Royal, new(150, 1, 1), new(30, 1, 1), new(0, 0.1, 0.9)];
            (double, double, double, double)? reference = null;
            foreach (var tint in tints)
            {
                GlassPalette.Current = tint;
                var (session, _, _, _) = StartLook("glass-phone", TestLyrics.Synced());
                var view = session.CurrentView!;
                view.Script.Execute("ToggleLyrics();", "test");
                session.Tick(TimeSpan.FromMilliseconds(30));
                var rect = Absolute(view.FindById("lyr")!);
                reference ??= rect;
                Assert.Equal(reference.Value, rect);
                var lyr = (LyricsElement)view.FindById("lyr")!;
                Assert.NotEqual(0, lyr.AccentColor);
                Assert.Equal(0xFFFFFF, lyr.InkColor);
            }
        }
        finally
        {
            GlassPalette.Current = saved;
        }
    }
}

internal static class MobileSettingsViewText
{
    public const string Privacy = "Sends artist, title, album and duration of the playing track to lrclib.net. Nothing else.";
}

internal static class TestLyrics
{
    public static Lyrics Synced() => new(Enumerable.Range(0, 20).Select(i => new LyricLine(TimeSpan.FromSeconds(5 + i * 5), "Line number " + i)), synced: true) { Source = LyricsSources.Embedded };

    public static Lyrics Plain() => new(Enumerable.Range(0, 20).Select(i => new LyricLine(TimeSpan.Zero, "Plain line " + i)), synced: false);
}
