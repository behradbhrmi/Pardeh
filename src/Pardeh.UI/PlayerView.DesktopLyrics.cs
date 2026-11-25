using Pardeh.Core.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Pardeh.Skinning.Runtime;
using Pardeh.Skinning.Runtime.Elements;
using Pardeh.UI.FullMode;
using Pardeh.UI.Rendering;

namespace Pardeh.UI;

/// <summary>
/// Lyrics and chords on the desktop: over the stage of a full mode (the video / visualization area), or in a window of
/// their own, which also serves skin mode. The phone looks keep their own lyrics view.
/// </summary>
internal sealed partial class PlayerView
{
    private LyricsPanel? _stageLyrics;
    private SkinSession? _stageLyricsFor;
    private Window? _lyricsWindow;
    private LyricsPanel? _windowLyrics;
    private bool _shuttingDown;

    private bool PhoneLookShowing => IsFullMode && PhoneSkin.IsPhoneKey(CurrentFullModeKey());

    /// <summary>Whether the lyrics show over the stage (full modes) or their window is open.</summary>
    internal bool DesktopLyricsShown => _lyricsWindow is not null || IsFullMode && !PhoneLookShowing && AppSettings.Current.LyricsOnStage;

    private LyricsPanelTheme PanelTheme() => (IsFullMode ? CurrentFullModeKey() : string.Empty) switch
    {
        "ember" => new("#141414", "#222222", "#FFFFFF", "#E5801F", true, "Segoe UI"),
        "fluent" => new("#1C1C1C", "#2B2B2B", "#FFFFFF", "#60CDFF", true, "Segoe UI"),
        "aero10" => new("#0D1C57", "#2E55C4", "#FFFFFF", "#9FE870", false, "Segoe UI"),
        var key when RetroDesktopLyricsTheme(key) is { } retro => retro,
        _ => new("#1D2554", "#3A4A8C", "#FFFFFF", "#7FFF00", false, "Tahoma"),
    };

    /// <summary>The panel colours of the retro PC looks, or null for other looks.</summary>
    private static LyricsPanelTheme? RetroDesktopLyricsTheme(string key) => PhoneSkin.DesktopLyricsTheme(key);

    /// <summary>Ctrl+L and View > Lyrics and Chords: the lyrics on the stage, in their window (skin mode), or the phone look's view.</summary>
    internal void ToggleLyricsView()
    {
        if (OperatingSystem.IsAndroid() || PhoneLookShowing)
        {
            _session?.CurrentView?.Script.Execute("if (typeof ShowPane == 'function') ShowPane('now'); if (typeof ToggleLyrics == 'function') ToggleLyrics();", "lyrics");
            return;
        }

        if (_lyricsWindow is not null)
        {
            CloseLyricsWindow();
            return;
        }

        if (!IsFullMode)
        {
            ShowLyricsWindow();
            return;
        }

        var on = !AppSettings.Current.LyricsOnStage;
        AppSettings.Current.LyricsOnStage = on;
        AppSettings.Current.Save();
        if (on)
            ShowPane("now");
    }

    private void OnLyricsPanelCommand(string action)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (action == "window")
            {
                AppSettings.Current.LyricsOnStage = false;
                ShowLyricsWindow();
            }
            else if (action == "close")
            {
                AppSettings.Current.LyricsOnStage = false;
                AppSettings.Current.Save();
            }
        });
    }

    // ---- Over the stage ---------------------------------------------------------------------------

    /// <summary>Where the full mode shows its video or visualization now, in skin units; null when that area is not on screen.</summary>
    private Rect? StageArea()
    {
        if (_session?.CurrentView is not { } view)
            return null;
        Rect? best = null;
        void Consider(SkinElement element)
        {
            for (SkinElement? e = element; e is not null; e = e.Parent)
            {
                if (!e.Visible)
                    return;
            }

            var (x, y) = element.AbsolutePosition;
            var area = new Rect(x, y, element.Width, element.Height)
                .Intersect(new Rect(0, 0, view.Root.Width, view.Root.Height));
            if (area.Width >= 160 && area.Height >= 100 && (best is not { } b || area.Width * area.Height > b.Width * b.Height))
                best = area;
        }

        foreach (var element in view.Elements)
        {
            if (element is EffectsElement or VideoElement)
            {
                Consider(element);
            }
            else if (element is HostAreaElement { Visible: true, Embedded: { } pane })
            {
                // The original full mode keeps Now Playing in an embedded pane (its elements report window positions).
                foreach (var inner in pane.Elements.Where(i => i is EffectsElement or VideoElement))
                    Consider(inner);
            }
        }

        // Ember shows neither while it is idle or showing the cover: its stage box is the place.
        if (best is null && view.FindById("svStage") is { } stage)
            Consider(stage);
        return best;
    }

    private void UpdateDesktopLyrics(TimeSpan elapsed)
    {
        if (OperatingSystem.IsAndroid())
            return;
        _windowLyrics?.Tick(elapsed);

        var wanted = AppSettings.Current.LyricsOnStage && _lyricsWindow is null && IsFullMode && !PhoneLookShowing && !_fullScreen;
        if (!wanted || StageArea() is not { } area)
        {
            if (_stageLyrics is not null)
                _stageLyrics.IsVisible = false;
            if (!wanted)
                RemoveStageLyrics();
            return;
        }

        if (_stageLyrics is null || !ReferenceEquals(_stageLyricsFor, _session) || _stageLyrics.Colors != PanelTheme())
        {
            RemoveStageLyrics();
            _stageLyrics = new LyricsPanel(_player, this, _lyricsFeed, PanelTheme(), inWindow: false, area.Width, area.Height, OnLyricsPanelCommand);
            _stageLyricsFor = _session;
            _overlay.Children.Insert(0, _stageLyrics); // popups and sheets stay above it
        }

        Canvas.SetLeft(_stageLyrics, area.X);
        Canvas.SetTop(_stageLyrics, area.Y);
        _stageLyrics.Width = area.Width;
        _stageLyrics.Height = area.Height;
        _stageLyrics.Resize(area.Width, area.Height);
        _stageLyrics.IsVisible = true;
        _stageLyrics.Tick(elapsed);
    }

    private void RemoveStageLyrics()
    {
        if (_stageLyrics is null)
            return;
        _overlay.Children.Remove(_stageLyrics);
        _stageLyrics.Dispose();
        _stageLyrics = null;
        _stageLyricsFor = null;
    }

    // ---- A window of their own --------------------------------------------------------------------

    internal void ShowLyricsWindow()
    {
        if (Shell is null)
            return;
        if (_lyricsWindow is { } open)
        {
            open.Activate();
            return;
        }

        var settings = AppSettings.Current;
        var zoom = Math.Max(1, settings.SkinZoom);
        var theme = PanelTheme();
        var width = Math.Clamp(settings.LyricsWindowWidth, 240, 2000);
        var height = Math.Clamp(settings.LyricsWindowHeight, 220, 2000);
        var panel = new LyricsPanel(_player, this, _lyricsFeed, theme, inWindow: true, width / zoom, height / zoom, OnLyricsPanelCommand);
        var window = new Window
        {
            Title = "Pardeh Lyrics",
            Width = width,
            Height = height,
            MinWidth = 240,
            MinHeight = 220,
            Background = new SolidColorBrush(Color.Parse(theme.Back)),
            Content = new LayoutTransformControl { LayoutTransform = new ScaleTransform(zoom, zoom), Child = panel },
            Icon = Shell.Icon,
            Topmost = settings.AlwaysOnTop,
        };
        if (settings.LyricsWindowX is { } left && settings.LyricsWindowY is { } top
            && Shell.Screens.All.Any(s => s.WorkingArea.Contains(new PixelPoint(left + 40, top + 20))))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = new PixelPoint(left, top);
        }

        window.SizeChanged += (_, e) => panel.Resize(e.NewSize.Width / zoom, e.NewSize.Height / zoom);
        window.KeyDown += (_, e) => HandleKeyDown(e);
        window.Closing += (_, _) =>
        {
            settings.LyricsWindowWidth = (int)window.ClientSize.Width;
            settings.LyricsWindowHeight = (int)window.ClientSize.Height;
            settings.LyricsWindowX = window.Position.X;
            settings.LyricsWindowY = window.Position.Y;
            if (!_shuttingDown)
                settings.LyricsWindowOpen = false; // closed by the user, not with the player
            settings.Save();
        };
        window.Closed += (_, _) =>
        {
            panel.Dispose();
            if (ReferenceEquals(_lyricsWindow, window))
                (_lyricsWindow, _windowLyrics) = (null, null);
        };
        (_lyricsWindow, _windowLyrics) = (window, panel);
        settings.LyricsWindowOpen = true;
        settings.Save();
        window.Show();
    }

    private void CloseLyricsWindow() => _lyricsWindow?.Close();

    /// <summary>At start: the lyrics window comes back if it was open. PARDEH_LYRICS=stage|window is a development aid.</summary>
    private void RestoreDesktopLyrics()
    {
        if (OperatingSystem.IsAndroid() || Shell is null)
            return;
        var forced = Environment.GetEnvironmentVariable("PARDEH_LYRICS");
        if (forced == "stage")
            AppSettings.Current.LyricsOnStage = true;
        if (forced == "window" || forced is null && AppSettings.Current.LyricsWindowOpen)
            Avalonia.Threading.Dispatcher.UIThread.Post(ShowLyricsWindow, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void ShutdownDesktopLyrics()
    {
        _shuttingDown = true;
        CloseLyricsWindow();
        RemoveStageLyrics();
    }

    /// <summary>PARDEH_SNAPSHOT with a lyrics window open also saves &lt;snapshot&gt;-lyrics.png.</summary>
    private void SnapshotLyricsWindow(string path)
    {
        if (_lyricsWindow is not { } window)
            return;
        var size = new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height);
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96, 96));
        bitmap.Render(window);
        var file = Path.Combine(Path.GetDirectoryName(path) ?? ".", Path.GetFileNameWithoutExtension(path) + "-lyrics.png");
        bitmap.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        Log($"Lyrics window snapshot {size} saved to {file}");
    }

    private object[] LyricsMenuItems()
    {
        var phone = PhoneLookShowing;
        var items = new List<object>
        {
            Item(IsFullMode && !phone ? "Show Over the Video Area" : "Show Lyrics", ToggleLyricsView, KeyCmd.Lyrics,
                check: IsFullMode && !phone && AppSettings.Current.LyricsOnStage),
        };
        if (!phone)
        {
            items.Add(Item("Show in Own Window", () =>
            {
                if (_lyricsWindow is not null)
                {
                    CloseLyricsWindow();
                    return;
                }

                AppSettings.Current.LyricsOnStage = false;
                ShowLyricsWindow();
            }, check: _lyricsWindow is not null));
        }

        items.Add(new Separator());
        foreach (var (name, action) in new[]
                 {
                     ("Paste or Edit Lyrics...", "paste"), ("Import Lyrics or Chords File...", "import"),
                     ("Add or Edit My Chords...", "chords"), ("Search Online...", "search"), ("Refresh", "refresh"),
                 })
        {
            var item = Item(name, () => LyricsCommand([action]));
            item.IsEnabled = _player.CurrentMedia is not null;
            items.Add(item);
        }

        return [.. items];
    }

    internal MenuItem LyricsMenu() => new() { Header = "Lyrics and Chords", ItemsSource = LyricsMenuItems() };
}
