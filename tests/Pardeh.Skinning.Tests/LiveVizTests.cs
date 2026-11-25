using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using Pardeh.Core.Playback;
using Pardeh.Skinning.Runtime;
using Pardeh.Skinning.Runtime.Elements;
using Pardeh.UI;
using Pardeh.UI.FullMode;
using Pardeh.UI.Rendering;

namespace Pardeh.Skinning.Tests;

/// <summary>
/// The live bars (the Glass playing row and the bottom bar's visualization glyph), the Glass visualization styles, the picker's state and
/// the shared level smoothing: smoothing logic, settings persistence, every style rendering lively and freezing when the music stops,
/// tinted palettes, fa-IR safety and the classic presets on the picker's thumbnail path.
/// </summary>
[Collection("Avalonia rendering")]
public sealed class LiveVizTests : IDisposable
{
    private static readonly object Gate = new();
    private static bool _platformUp;

    private readonly GlassPalette.Tint _tint = GlassPalette.Current;
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;
    private readonly string _glassStyle = AppSettings.Current.GlassVizStyle;
    private readonly string _preset = AppSettings.Current.PhoneVizPreset;
    private readonly string _auto = AppSettings.Current.VizAuto;

    public void Dispose()
    {
        GlassPalette.Current = _tint;
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
        AppSettings.Current.GlassVizStyle = _glassStyle;
        AppSettings.Current.PhoneVizPreset = _preset;
        AppSettings.Current.VizAuto = _auto;
        VizDirector.Reload();
    }

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
                // Another test class set the platform up first.
            }

            _platformUp = true;
        }
    }

    // ---- Level smoothing -----------------------------------------------------------------------------

    private static float[] Constant(int n, float v) => Enumerable.Repeat(v, n).ToArray();

    [Fact]
    public void Levels_attack_quickly_decay_slowly_and_settle_to_zero()
    {
        var smoother = new LevelSmoother(4);
        smoother.Update(Constant(4, 1f), active: true, dt: 1.0 / 30);
        var afterOneFrame = smoother.Levels[0];
        Assert.InRange(afterOneFrame, 0.5f, 0.9f); // most of the way up within one 30 fps frame
        for (var i = 0; i < 10; i++)
            smoother.Update(Constant(4, 1f), true, 1.0 / 30);
        Assert.True(smoother.Levels[0] > 0.99f);
        Assert.False(smoother.Settled);

        // Released: it falls over several frames, not at once...
        smoother.Update(Constant(4, 0f), active: false, dt: 1.0 / 30);
        Assert.InRange(smoother.Levels[0], 0.6f, 0.95f);
        // ... and then settles exactly at zero, after which nothing changes any more (the bars freeze).
        for (var i = 0; i < 120; i++)
            smoother.Update(Constant(4, 0f), false, 1.0 / 30);
        Assert.True(smoother.Settled);
        Assert.All(smoother.Levels, l => Assert.Equal(0f, l));
        Assert.All(smoother.Peaks, p => Assert.Equal(0f, p));
    }

    [Fact]
    public void Smoothing_does_not_depend_on_how_often_it_is_updated()
    {
        LevelSmoother Run(int steps)
        {
            var s = new LevelSmoother(1);
            for (var i = 0; i < steps; i++)
                s.Update([0.8f], true, 0.2 / steps);
            for (var i = 0; i < steps; i++)
                s.Update([0f], false, 0.2 / steps);
            return s;
        }

        var coarse = Run(6).Levels[0];
        var fine = Run(24).Levels[0];
        Assert.InRange(Math.Abs(coarse - fine), 0, 0.03);
    }

    [Fact]
    public void Peaks_hold_a_moment_then_fall_but_never_below_the_level()
    {
        var s = new LevelSmoother(1);
        for (var i = 0; i < 12; i++)
            s.Update([0.9f], true, 1.0 / 30);
        var top = s.Peaks[0];
        Assert.True(top > 0.85f);
        s.Update([0.1f], true, 0.1); // still inside the hold time
        Assert.Equal(top, s.Peaks[0]);
        for (var i = 0; i < 30; i++)
            s.Update([0.1f], true, 1.0 / 30);
        Assert.True(s.Peaks[0] < top);
        Assert.True(s.Peaks[0] >= s.Levels[0]);
    }

    [Fact]
    public void The_feed_reads_the_audio_at_most_thirty_times_a_second_and_freezes_when_it_stops()
    {
        var demo = new DemoTap(() => 0);
        var feed = SpectrumFeed.Over(demo);
        Assert.True(feed.Update(0.0));
        Assert.False(feed.Update(0.010));
        Assert.False(feed.Update(0.020));
        Assert.True(feed.Update(0.040));
        Assert.Equal(2, feed.Updates);
        Assert.True(feed.Playing);
        Assert.False(feed.Frozen);

        demo.Playing = false;
        var t = 0.04;
        for (var i = 0; i < 90 && !feed.Frozen; i++)
            feed.Update(t += 1.0 / 30);
        Assert.True(feed.Frozen);
        var updates = feed.Updates;
        var snapshot = feed.Levels.ToArray();
        feed.Update(t += 1.0 / 30);
        Assert.Equal(snapshot, feed.Levels); // frozen: not one level moves
        Assert.True(feed.Updates > updates);
    }

    [Fact]
    public void Grouping_folds_the_bands_into_bars_with_contrast()
    {
        var feed = SpectrumFeed.Manual();
        var bands = new float[SpectrumFeed.Bands];
        for (var i = 0; i < 10; i++)
            bands[i] = 0.85f; // loud bass only
        for (var i = 0; i < 20; i++)
            feed.Apply(bands, true, 0.1);
        var bars = new float[3];
        feed.Group(bars);
        Assert.True(bars[0] > 0.9f, $"bass bar {bars[0]}");
        Assert.True(bars[2] < 0.05f, $"treble bar {bars[2]}");
        Assert.Equal(0f, SpectrumFeed.Contrast(0.1f));
        Assert.Equal(1f, SpectrumFeed.Contrast(0.95f));
        Assert.InRange(SpectrumFeed.Contrast(0.5f), 0.4f, 0.6f);
    }

    // ---- Settings and the picker's state --------------------------------------------------------------

    [Fact]
    public void The_new_settings_round_trip_and_an_old_file_loads_with_the_defaults()
    {
        var fresh = new AppSettings();
        Assert.Equal("bars", fresh.GlassVizStyle);
        Assert.Equal(string.Empty, fresh.PhoneVizPreset);
        Assert.Equal("off", fresh.VizAuto);

        var saved = new AppSettings { GlassVizStyle = "aurora", PhoneVizPreset = "Battery : Cottonstar", VizAuto = "cycle" };
        var loaded = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(saved))!;
        Assert.Equal("aurora", loaded.GlassVizStyle);
        Assert.Equal("Battery : Cottonstar", loaded.PhoneVizPreset);
        Assert.Equal("cycle", loaded.VizAuto);

        var old = JsonSerializer.Deserialize<AppSettings>("""{ "ClassicBranding": false, "ShuffleOn": true }""")!;
        Assert.False(old.ClassicBranding);
        Assert.True(old.ShuffleOn);
        Assert.Equal("bars", old.GlassVizStyle);
        Assert.Equal("off", old.VizAuto);
    }

    [Fact]
    public void Style_lists_are_complete_and_saved_keys_map_back_to_their_style()
    {
        Assert.Equal(11, VizStyles.Glass.Length); // the eight Glass styles and the three old-school Battery presets
        Assert.Equal(VizStyles.Glass.Length, VizStyles.Glass.Select(s => s.Key).Distinct().Count());
        Assert.Equal(Enum.GetValues<GlassVizStyle>().Length, VizStyles.Glass.Length);
        for (var i = 0; i < VizStyles.Glass.Length; i++)
            Assert.Equal(i, VizStyles.GlassIndex(VizStyles.Glass[i].Key));
        Assert.Equal(0, VizStyles.GlassIndex("no-such-style"));
        Assert.Equal(0, VizStyles.GlassIndex(null));
        Assert.Equal(GlassVizStyle.Led, VizStyles.GlassStyle(7));

        Assert.Equal(VisualizationCatalog.Default, VizStyles.ClassicIndex(string.Empty));
        Assert.Equal(VisualizationCatalog.Default, VizStyles.ClassicIndex("no such preset"));
        for (var i = 0; i < VisualizationCatalog.All.Count; i++)
            Assert.Equal(i, VizStyles.ClassicIndex(VisualizationCatalog.All[i].Title));
        Assert.Equal(3, VizStyles.AutoModes.Length);
        Assert.Equal(2, VizStyles.AutoIndex("cycle"));
    }

    [Fact]
    public void The_saved_default_starts_the_session_and_automatic_changes_leave_it_alone()
    {
        AppSettings.Current.GlassVizStyle = "rings";
        VizDirector.Reload();
        Assert.Equal(VizStyles.GlassIndex("rings"), VizDirector.GlassCurrent);

        VizDirector.Advance(glass: true);
        Assert.NotEqual(VizStyles.GlassIndex("rings"), VizDirector.GlassCurrent);
        Assert.Equal("rings", AppSettings.Current.GlassVizStyle); // not saved
        Assert.True(VizDirector.FlashUntil > SpectrumFeed.Now);
        Assert.False(string.IsNullOrEmpty(VizDirector.FlashText));

        VizDirector.Reload();
        Assert.Equal(VizStyles.GlassIndex("rings"), VizDirector.GlassCurrent);
    }

    [Fact]
    public void The_every_track_mode_changes_style_on_a_new_track_and_the_cycle_mode_every_fifteen_seconds_of_playing()
    {
        var engine = new FakeEngine();
        var player = new MediaPlayer(engine, new FakeInfoReader());
        AppSettings.Current.GlassVizStyle = "bars";
        VizDirector.Attach(player);
        VizDirector.Attach(player); // idempotent

        AppSettings.Current.VizAuto = "track";
        VizDirector.Reload();
        VizDirector.Tick(0.016, playing: true, glass: true);
        Assert.Equal(0, VizDirector.GlassCurrent);
        player.OpenFiles(["C:/music/a.mp3"]);
        VizDirector.Tick(0.016, true, true);
        Assert.NotEqual(0, VizDirector.GlassCurrent);

        AppSettings.Current.VizAuto = "cycle";
        VizDirector.Reload();
        for (var i = 0; i < 400; i++)
            VizDirector.Tick(0.016, playing: false, glass: true); // paused: the cycle does not run
        Assert.Equal(0, VizDirector.GlassCurrent);
        for (var i = 0; i < 800; i++)
            VizDirector.Tick(0.025, playing: true, glass: true); // 20 s of music
        Assert.NotEqual(0, VizDirector.GlassCurrent);

        AppSettings.Current.VizAuto = "off";
        VizDirector.Reload();
        var before = VizDirector.GlassCurrent;
        for (var i = 0; i < 2000; i++)
            VizDirector.Tick(0.025, true, true);
        Assert.Equal(before, VizDirector.GlassCurrent);
    }

    [Fact]
    public void Visibility_is_reported_once_per_change()
    {
        var seen = new List<bool>();
        void Handler(bool v) => seen.Add(v);
        AppVisibility.Changed += Handler;
        try
        {
            AppVisibility.Set(true); // already visible: no event
            AppVisibility.Set(false);
            AppVisibility.Set(false);
            AppVisibility.Set(true);
            Assert.Equal([false, true], seen);
            Assert.True(AppVisibility.Foreground);
        }
        finally
        {
            AppVisibility.Changed -= Handler;
            AppVisibility.Set(true);
        }
    }

    // ---- Drawing -------------------------------------------------------------------------------------

    private static (int Differing, int Total) Compare(byte[] a, byte[] b)
    {
        var differing = 0;
        for (var i = 0; i < a.Length; i += 4)
        {
            if (Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]) > 24)
                differing++;
        }

        return (differing, a.Length / 4);
    }

    private static int Brightest(byte[] bgra)
    {
        var best = 0;
        for (var i = 0; i < bgra.Length; i += 4)
            best = Math.Max(best, bgra[i] + bgra[i + 1] + bgra[i + 2]);
        return best;
    }

    private static (double R, double G, double B) Mean(byte[] bgra)
    {
        double r = 0, g = 0, b = 0;
        for (var i = 0; i < bgra.Length; i += 4)
        {
            b += bgra[i];
            g += bgra[i + 1];
            r += bgra[i + 2];
        }

        var n = bgra.Length / 4.0;
        return (r / n, g / n, b / n);
    }

    /// <summary>Drives one style for a few seconds of demo music at 30 fps; returns frames while it plays, then after it stopped.</summary>
    private static (byte[] Playing1, byte[] Playing2, byte[] Silent1, byte[] Silent2) DriveStyle(GlassVizStyle style, int w, int h)
    {
        double t = 0;
        var demo = new DemoTap(() => t);
        using var renderer = new VisualizationRenderer { Glass = true, GlassStyle = style, Source = demo, Clock = () => t };
        byte[] Frame() => LunaArt.Render(w, h, g => renderer.Render(g, new Rect(0, 0, w, h), 0, null, 1.0 / 30));
        byte[] playing1 = [], playing2 = [];
        for (var i = 0; i < 75; i++)
        {
            t += 1.0 / 30;
            var frame = Frame();
            if (i == 60)
                playing1 = frame;
            if (i == 68)
                playing2 = frame;
        }

        demo.Playing = false;
        byte[] silent1 = [], silent2 = [];
        for (var i = 0; i < 150; i++) // five seconds: levels, beads and ripples all settle
        {
            t += 1.0 / 30;
            var frame = Frame();
            if (i == 147)
                silent1 = frame;
            if (i == 149)
                silent2 = frame;
        }

        return (playing1, playing2, silent1, silent2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Every_glass_style_draws_lively_frames_in_portrait_and_landscape_and_freezes_when_the_music_stops(int styleIndex)
    {
        EnsurePlatform();
        var style = (GlassVizStyle)styleIndex;
        foreach (var (w, h) in new[] { (240, 300), (320, 150) })
        {
            var (p1, p2, s1, s2) = DriveStyle(style, w, h);
            Assert.True(Brightest(p1) > 450, $"{style} {w}x{h}: nothing bright is drawn");
            var (moved, total) = Compare(p1, p2);
            Assert.True(moved > total / 200, $"{style} {w}x{h}: frames 8 apart differ in only {moved} of {total} pixels (it does not move)");
            var (changed, _) = Compare(p1, s1);
            Assert.True(changed > total / 200, $"{style} {w}x{h}: the playing and the silent picture are alike");
            Assert.Equal(s1, s2); // frozen: byte for byte the same picture
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void A_tinted_skin_colour_re_colours_every_glass_style(int styleIndex)
    {
        EnsurePlatform();
        var style = (GlassVizStyle)styleIndex;
        GlassPalette.Current = GlassPalette.Tint.Royal;
        var (royal, _, _, _) = DriveStyle(style, 240, 300);
        GlassPalette.Current = new GlassPalette.Tint(150, 1, 1); // emerald
        var (green, _, _, _) = DriveStyle(style, 240, 300);
        Assert.True(Brightest(green) > 400, $"{style}: nothing bright in the tinted frame");
        var (r0, g0, b0) = Mean(royal);
        var (r1, g1, b1) = Mean(green);
        // Blue is the skin colour: in green the share of green against blue grows clearly.
        Assert.True(g1 / Math.Max(1, b1) > g0 / Math.Max(1, b0) + 0.15, $"{style}: royal g/b {g0 / b0:0.00}, emerald g/b {g1 / b1:0.00}");
        _ = (r0, r1);
    }

    [Fact]
    public void The_classic_presets_draw_on_the_pickers_thumbnails()
    {
        EnsurePlatform();
        double t = 0;
        var demo = new DemoTap(() => t);
        for (var index = 0; index < VisualizationCatalog.All.Count; index++)
        {
            using var renderer = new VisualizationRenderer { Source = demo, Preview = true };
            byte[] frame = [];
            for (var i = 0; i < 12; i++)
            {
                t += 1.0 / 30;
                frame = LunaArt.Render(150, 74, g => renderer.Render(g, new Rect(0, 0, 150, 74), index, null, 1.0 / 30));
            }

            Assert.True(Brightest(frame) > 90, $"{VisualizationCatalog.All[index].Title}: the thumbnail is black");
        }
    }

    [Fact]
    public void Chrome_and_live_bars_are_safe_under_the_persian_culture()
    {
        EnsurePlatform();
        var fa = CultureInfo.GetCultureInfo("fa-IR");
        CultureInfo.CurrentCulture = fa;
        CultureInfo.CurrentUICulture = fa;
        VizDirector.Reload();
        VizDirector.Advance(glass: true); // sets the style name that fades in
        var pixels = LunaArt.Render(300, 200, g =>
        {
            g.FillRectangle(Brushes.Black, new Rect(0, 0, 300, 200));
            VizChrome.DrawFlash(g, new Rect(0, 0, 300, 200), glass: true);
            VizChrome.DrawChip(g, new Rect(0, 0, 300, 200), glass: true);
            LiveBars.Draw(g, new Rect(10, 10, 50, 20), [0.2f, 0.6f, 1f], LiveBarsLook.PlayingRow);
        });
        Assert.True(Brightest(pixels) > 300);
        foreach (var style in Enum.GetValues<GlassVizStyle>())
        {
            var (a, _, _, _) = DriveStyle(style, 200, 160);
            Assert.True(Brightest(a) > 300, $"{style} under fa-IR");
        }
    }

    [Fact]
    public void Live_bars_follow_the_level_and_rest_low_and_flat()
    {
        EnsurePlatform();
        byte[] Draw(float[] levels, bool segmented)
        {
            var look = segmented
                ? new LiveBarsLook(Color.Parse("#35C6FF"), Color.Parse("#35C6FF"), Color.Parse("#9CEBFF"), Segmented: true, Rest: 0.2, Gap: 2)
                : LiveBarsLook.PlayingRow;
            return LunaArt.Render(60, 24, g => LiveBars.Draw(g, new Rect(4, 2, 47, 19), levels, look));
        }

        int Lit(byte[] bgra) => Enumerable.Range(0, bgra.Length / 4).Count(i => bgra[i * 4 + 3] > 0 && bgra[i * 4 + 1] > 60);

        foreach (var segmented in new[] { false, true })
        {
            var rest = Lit(Draw([0f, 0f, 0f], segmented));
            var mid = Lit(Draw([0.5f, 0.5f, 0.5f], segmented));
            var full = Lit(Draw([1f, 1f, 1f], segmented));
            Assert.True(rest > 0, "a paused glyph is still visible");
            Assert.True(rest < mid && mid < full, $"segmented={segmented}: rest {rest}, half {mid}, full {full}");
            Assert.Equal(Draw([0f, 0f, 0f], segmented), Draw([0.05f, 0.1f, 0f], segmented)); // below the rest level: the same flat row
        }
    }

    // ---- The Glass skin: the playing row's bars and the bottom bar's glyph ---------------------------------

    private sealed class SynthEngine : IPlaybackEngine
    {
        private int _calls;
        public bool Audible { get; set; }
        public bool IsOpen { get; private set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration { get; private set; }
        public float Volume { get; set; }
        public float Balance { get; set; }
        public bool Muted { get; set; }
        public EqualizerSettings Equalizer { get; } = new();
#pragma warning disable CS0067
        public event EventHandler? PlaybackEnded;
#pragma warning restore CS0067
        public void Open(string source)
        {
            IsOpen = true;
            Duration = TimeSpan.FromMinutes(3);
            Position = TimeSpan.Zero;
        }

        public void Close() => IsOpen = false;
        public void Play() => Audible = true;
        public void Pause() => Audible = false;
        public void Stop() => Audible = false;
        public (int Width, int Height) VideoSize => (0, 0);
        public VideoFrame? CurrentVideoFrame => null;
        public void Dispose() { }

        public bool GetSpectrum(Span<float> bins)
        {
            if (!Audible)
            {
                bins.Clear();
                return false;
            }

            _calls++;
            for (var i = 0; i < bins.Length; i++)
                bins[i] = 0.5f + 0.45f * MathF.Sin(_calls * 0.9f + i * 0.35f) * MathF.Cos(_calls * 0.37f);
            return true;
        }
    }

    private static byte[] Crop(byte[] bgra, int stride, int x, int y, int w, int h)
    {
        var result = new byte[w * h * 4];
        for (var row = 0; row < h; row++)
            Buffer.BlockCopy(bgra, ((y + row) * stride + x) * 4, result, row * w * 4, w * 4);
        return result;
    }

    [Fact]
    public void The_glass_playing_row_and_visualization_button_animate_while_playing_and_freeze_when_paused()
    {
        EnsurePlatform();
        var engine = new SynthEngine();
        var player = new MediaPlayer(engine, new FakeInfoReader());
        var session = new SkinSession(PhoneSkin.Build("glass-phone"), player, new FakeHost(), new AvaloniaImageDecoder(), new MemoryPreferences());
        session.Start();
        player.OpenFiles(["C:/music/a.mp3", "C:/music/b.mp3"]);
        const int W = 360, H = 720;
        var view = session.CurrentView!;
        view.Root.Width = W;
        view.Root.Height = H;
        session.Tick(TimeSpan.FromMilliseconds(30));
        var eq = (EqBarsElement)(view.FindById("eqViz") ?? throw new Xunit.Sdk.XunitException("no eqViz"));
        var list = view.FindById("ipl") ?? throw new Xunit.Sdk.XunitException("no ipl");
        var button = view.FindById("bViz")!;
        Assert.False(eq.HitTest(5, 5)); // a tap goes through to the button under it
        Assert.True(button.HitTest(10, 10));
        Assert.Equal(7, eq.Bars);

        var renderer = new SkinRenderer();
        byte[] Whole()
        {
            Thread.Sleep(45); // the feed reads the audio at most every ~33 ms of real time
            return LunaArt.Render(W, H, g => renderer.Render(g, view));
        }

        (byte[] Eq, byte[] Row) Frame()
        {
            var whole = Whole();
            var (ex, ey) = eq.AbsolutePosition;
            var (lx, ly) = list.AbsolutePosition;
            return (Crop(whole, W, (int)ex, (int)ey, (int)eq.Width, (int)eq.Height), Crop(whole, W, (int)lx, (int)ly, (int)list.Width, 30));
        }

        engine.Audible = true;
        player.Play();
        var playing = Enumerable.Range(0, 6).Select(_ => Frame()).ToList();
        Assert.Contains(Enumerable.Range(1, 5), i => Compare(playing[i - 1].Eq, playing[i].Eq).Differing > 5);
        Assert.Contains(Enumerable.Range(1, 5), i => Compare(playing[i - 1].Row, playing[i].Row).Differing > 5);

        // Paused: the bars sink and then do not move at all.
        player.Pause();
        engine.Audible = false;
        var feed = SpectrumFeed.For(player);
        for (var i = 0; i < 80 && !feed.Frozen; i++)
            Frame();
        Assert.True(feed.Frozen, "the levels did not settle");
        var a = Frame();
        var b = Frame();
        var c = Frame();
        Assert.Equal(a.Eq, b.Eq);
        Assert.Equal(b.Eq, c.Eq);
        Assert.Equal(a.Row, b.Row);
        Assert.Equal(b.Row, c.Row);
        Assert.True(Brightest(a.Eq) > 300, "the resting glyph is still visible");
        Assert.NotEqual(playing[^1].Eq, a.Eq);
    }
}
