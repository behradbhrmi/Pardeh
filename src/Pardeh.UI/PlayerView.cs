using Pardeh.Engine;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Pardeh.UI.FullMode;
using Pardeh.UI.Rendering;
using Pardeh.Core.Input;
using Pardeh.Core.Playback;
using Pardeh.Nava;
using Pardeh.Skinning.Imaging;
using Pardeh.Skinning.Runtime;
using Pardeh.Skinning.Wmp;

namespace Pardeh.UI;

/// <summary>
/// The player: hosts the skin runtime. A desktop head puts it in a borderless <see cref="Window"/> (shaped like the
/// current skin, with the window-only behaviour below applying when <see cref="Shell"/> is not null); a single-view
/// head (Android) puts it in its main view. OS-specific pieces come through <see cref="IPlatformServices"/>.
/// </summary>
internal sealed partial class PlayerView : UserControl, ISkinHost, Rendering.ISettingsHost, Rendering.IEmberHost
{
    private readonly SkinCanvas _canvas = new();
    private readonly LayoutTransformControl _scaler;
    private readonly MediaPlayer _player;
    private readonly MediaEngine _engine;
    private readonly IImageDecoder _decoder = new CompositeImageDecoder(new BmpDecoder(), new AvaloniaImageDecoder());
    private readonly IPreferenceStore _preferences = new JsonPreferenceStore(Path.Combine(AppPaths.DataDirectory, "SkinPreferences"));
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly StartupOptions _startup;
    private readonly Pardeh.Core.Library.MediaLibrary _library;
    private SkinSession? _session;
    private Pardeh.Skinning.Model.SkinPackage? _fullMode;
    private TimeSpan _lastTick;
    private PixelRect? _restoreBounds;

    // Window shape tracking.
    private SkinView? _shapedView;
    private int _shapedVersion = -1;
    private double _shapedScale;

    public PlayerView(StartupOptions startup)
    {
        _startup = startup;
        _scaler = new LayoutTransformControl { Child = new Panel { Children = { _canvas, _overlay } } };
        _canvas.FullScreenExitRequested += (_, _) => SetFullScreen(false);
        _canvas.Controls.ExitRequested += () => SetFullScreen(false);
        if (OperatingSystem.IsAndroid())
            InstallPageSwipe();
        _frame.Command += OnFrameCommand;
        _frame.MenuRequested += (name, title) =>
        {
            // Drops down from its title on the menu bar (Windows 11 flyouts leave a small gap).
            var menu = new ContextMenu
            {
                ItemsSource = MenuItems(name),
                Placement = PlacementMode.BottomEdgeAlignedLeft,
                PlacementRect = title,
                VerticalOffset = AppSettings.Current.ModernMenus ? 2 : 0,
            };
            OpenBarMenu(menu, _frame);
        };
        _frame.MenuDismissRequested += CloseBarMenu;
        if (OperatingSystem.IsAndroid())
        {
            // Touch-sized menu items (48 dp and up) and a readable size.
            Styles.Add(new Avalonia.Styling.Style(x => Avalonia.Styling.Selectors.OfType<MenuItem>(x))
            {
                Setters = { new Avalonia.Styling.Setter(MinHeightProperty, 48.0), new Avalonia.Styling.Setter(Avalonia.Controls.Primitives.TemplatedControl.FontSizeProperty, 15.0),
                    new Avalonia.Styling.Setter(PaddingProperty, new Thickness(14, 8)) },
            });
        }

        Content = _scaler;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, OnDrop);

        _engine = new MediaEngine();
        _engine.PlaybackFailed += (_, ex) => Dispatcher.UIThread.Post(() => Log($"Audio output failed: {ex}"));
        _player = new MediaPlayer(_engine, new TagMediaInfoReader());
        _player.Favorites = new Pardeh.Core.Library.FavoritesStore(Path.Combine(AppPaths.DataDirectory, "favorites.json"));
        _player.Bookmarks = AppBookmarks.Store;
        _player.Loudness = new Pardeh.Core.Playback.LoudnessService(Path.Combine(AppPaths.DataDirectory, "loudness.json"), NavaLoudness.Measure);
        _lyrics = Pardeh.UI.LyricsKit.LyricsController.Create(_player, AppSettings.Current, action => Dispatcher.UIThread.Post(action));
        _lyricsFeed = new Pardeh.UI.LyricsKit.LyricsFeed(_lyrics, AppSettings.Current);
        AudioSettingsStore.Attach(_player, _preferences);
        _player.CurrentMediaChanged += (_, _) => Log($"Opened {NetworkMedia.Redact(_player.CurrentMedia?.Source ?? string.Empty)} ({_engine.DecoderInfo})");
        _player.Error += (_, message) => Log($"Playback error: {message}");
        _library = new Pardeh.Core.Library.MediaLibrary(new TagMediaInfoReader(), Path.Combine(AppPaths.DataDirectory, "library.json"));
        _library.LoadSettings();
        if (Environment.GetEnvironmentVariable("PARDEH_LIBRARY_FOLDERS") is { Length: > 0 } folders)
        {
            _library.Folders.Clear();
            _library.Folders.AddRange(folders.Split(';', StringSplitOptions.RemoveEmptyEntries));
        }

#if DEBUG
        Rendering.VisualizationRenderer.Diagnostics = Log; // a periodic "viz ...: N fps, draw M ms" line while a visualization shows
#else
        if (Environment.GetEnvironmentVariable("PARDEH_DIAG") == "1")
            Rendering.VisualizationRenderer.Diagnostics = Log;
#endif
        if (Environment.GetEnvironmentVariable("PARDEH_MUTE") == "1")
            _player.Mute = true; // development aid: silent snapshot runs
        _player.Gapless = AppSettings.Current.Gapless;
        ApplyNetworkSettings();
        _player.VideoPositions = new Pardeh.Core.Library.VideoPositionStore(Path.Combine(AppPaths.DataDirectory, "video-positions.json"));
        _player.ResumeVideos = AppSettings.Current.ResumeVideos;
        AppVisibility.Changed += foreground =>
        {
            if (!foreground)
                Dispatcher.UIThread.Post(_player.SaveVideoPosition);
        };
        _player.ReplayGain = Enum.TryParse<Pardeh.Core.Playback.ReplayGainMode>(AppSettings.Current.ReplayGain, true, out var levelling) ? levelling : Pardeh.Core.Playback.ReplayGainMode.Off;
        _canvas.BackgroundPressed += (_, e) => OnSurfacePressed(e);
        _canvas.PointerMoved += OnSurfaceMoved;
        _canvas.PointerReleased += (_, _) => _pendingMove = null;
        if (Environment.GetEnvironmentVariable("PARDEH_DIAG") == "1")
            _canvas.PressLog = Log;
        _canvas.ContextRequestedOnSkin += (_, _) => ShowAppMenu();

        // ~60 Hz: smooth video and visualizations (30 ms beat against 24 / 30 fps video and looked jerky).
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => OnTick());
        // Android: with the screen off or the app in the background nothing is drawn, so nothing animates (live bars, visualizer): the
        // tick slows to 4 Hz (the player still needs it) and stops invalidating the canvas.
        AppVisibility.Changed += visible => Dispatcher.UIThread.Post(() => ApplyVisibility(visible));
    }

    private void ApplyVisibility(bool visible)
    {
        _timer.Interval = TimeSpan.FromMilliseconds(visible ? 15 : 250);
        if (visible)
            _canvas.InvalidateVisual();
    }

    // ---- The host window (desktop) or top level (single view) ------------------------------------

    private TopLevel? Top => TopLevel.GetTopLevel(this);

    /// <summary>The desktop window around this view, or null in a single-view head (Android).</summary>
    private Window? Shell => Top as Window;

    private double RenderScaling => Top?.RenderScaling ?? 1;

    private Size ClientSize => Top?.ClientSize ?? Bounds.Size;

    private IStorageProvider StorageProvider => Top!.StorageProvider;

    private Avalonia.Platform.Screen? CurrentScreen => Shell is { } shell ? Top?.Screens?.ScreenFromWindow(shell) ?? Top?.Screens?.Primary : null;

    private WindowState WindowState
    {
        get => Shell?.WindowState ?? WindowState.Normal;
        set
        {
            if (Shell is { } shell)
                shell.WindowState = value;
        }
    }

    private PixelPoint Position
    {
        get => Shell?.Position ?? default;
        set
        {
            if (Shell is { } shell)
                shell.Position = value;
        }
    }

    /// <summary>Always on top (desktop window only).</summary>
    internal bool Topmost
    {
        set
        {
            if (Shell is { } shell)
                shell.Topmost = value;
        }
    }

    private void Close() => Shell?.Close();

    private void Activate() => Shell?.Activate();

    private static IPlatformServices Platform => PlatformServices.Current;

    /// <summary>Physical pixels per skin pixel: a whole number so bitmaps stay crisp.</summary>
    private double PixelScale { get; set; } = 1;

    /// <summary>Layout scale (DIPs per skin pixel) that produces <see cref="PixelScale"/> physical pixels.</summary>
    private double LayoutScale => _fitScale ?? PixelScale / RenderScaling;

    /// <summary>Called by the head once the view is showing (a desktop window after Opened, a single view once attached).</summary>
    public void Start()
    {
        StartupMarks.Mark("PlayerView.Start begin");
        if (Top is { } top)
        {
            top.ScalingChanged += (_, _) => ApplyScale();
            if (OperatingSystem.IsAndroid())
            {
                SizeChanged += (_, _) => ApplyAndroidLayout();
                if (top.InsetsManager is { } insets)
                    insets.SafeAreaChanged += (_, _) => ApplyAndroidLayout();
            }

            if (top is Window shell)
            {
                shell.Activated += (_, _) => _frame.InvalidateVisual();
                shell.Deactivated += (_, _) => _frame.InvalidateVisual();
            }
        }

        ApplyScale();
        ApplySettings();
        AppSettings.Current.Changed += () => Dispatcher.UIThread.Post(ApplySettings);
        if (OperatingSystem.IsAndroid())
            RestorePlayModes();
        StartupMarks.Mark("settings applied");
        Platform.ListenForOpenRequests(args => Dispatcher.UIThread.Post(() => OpenArguments(args)));

        _library.Changed += (_, _) => Dispatcher.UIThread.Post(() => _session?.OnLibraryChanged());
        StartupMarks.Mark("library cache loading");
        _library.LoadCache();
        StartupMarks.Mark("library cache loaded");
        StartFileChanges(); // finishes an interrupted change before anything scans or restores the queue
        // The folder scan, the folder watchers and the system integration (media session, notification) are not needed for the
        // first frame: on Android they wait until the UI is idle, so the JIT-bound first script engine start is not slowed down.
        void StartBackgroundWork()
        {
            _ = _library.ScanAsync();
            _library.WatchFolders();
            _integration = Top is { } host ? Platform.StartSystemIntegration(host, _player, Log) : null;
            StartupMarks.Mark("background work started (scan, watchers, media session)");
        }

        if (OperatingSystem.IsAndroid())
            Dispatcher.UIThread.Post(StartBackgroundWork, DispatcherPriority.Background);
        else
            StartBackgroundWork();
        // Play counts and "last played" for the library (ratings, auto playlists).
        _player.CurrentMediaChanged += (_, _) =>
        {
            if (_player.CurrentMedia is { } played && !_player.IsResumingFile) // taken up again after a file change
                _library.RecordPlay(played.Source);
        };

        // Full mode unless a skin was asked for on the command line or skin mode was used last.
        // Tools › Options › Start in: the last mode, or always full / skin mode.
        var skinMode = _startup.SkinExplicit || AppSettings.Current.StartMode switch
        {
            "full" => false,
            "skin" => true,
            _ => SkinLibrary.LastMode == "skin",
        };
        if (!skinMode || _startup.SkinPath is null || !LoadSkin(_startup.SkinPath))
            LoadFullMode();

        if (_startup.MediaFiles.Count > 0)
            OpenGiven(_startup.MediaFiles);
        _player.Video.Aspect = AppSettings.Current.VideoAspect;
        StartQueueMemory(_startup.MediaFiles.Count == 0);

        // Development aid: PARDEH_ADD_FOLDER=<path> adds a library folder (for tests on a device).
        if (Environment.GetEnvironmentVariable("PARDEH_ADD_FOLDER") is { Length: > 0 } devFolder && Directory.Exists(devFolder))
            _library.AddFolder(devFolder);
        RestoreDesktopLyrics();        StartupMarks.Mark("PlayerView.Start end (full mode loaded)");
        _lastTick = _clock.Elapsed;
        _timer.Start();
    }

    private QueueStore? _queueStore;
    private DispatcherTimer? _queueTimer;

    /// <summary>
    /// The last session's queue and track come back paused at their old position (nothing starts by itself), and from now on the queue
    /// is saved when it changes, every few seconds while playing, and when the app leaves the screen or closes.
    /// </summary>
    private void StartQueueMemory(bool restore)
    {
        if (Environment.GetEnvironmentVariable("PARDEH_SNAPSHOT") is { Length: > 0 })
            return; // dev snapshots start clean
        _queueStore = new QueueStore(_player);
        if (restore)
            _queueStore.Restore();
        _queueTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => _queueStore.Save());
        _queueTimer.Start();
        _player.CurrentMediaChanged += (_, _) => _queueStore.Save();
        _player.PlayStateChanged += (_, state) =>
        {
            if (state is PlayState.Paused or PlayState.Stopped)
                _queueStore.Save();
        };
        AppVisibility.Changed += foreground =>
        {
            if (!foreground)
                _queueStore.Save();
        };
    }

    /// <summary>Android: shuffle and repeat (the phone looks' toggles, the menu, the keys) come back as they were left.</summary>
    private void RestorePlayModes()
    {
        var settings = AppSettings.Current;
        _player.Shuffle = settings.ShuffleOn;
        _player.Loop = settings.RepeatOn;
        _player.ModeChanged += (_, change) =>
        {
            switch (change.Mode)
            {
                case "shuffle": settings.ShuffleOn = change.Value; break;
                case "loop": settings.RepeatOn = change.Value; break;
                default: return;
            }

            settings.Save();
        };
    }

    private const string OriginalFullModeSource = "wmp9://mainappskin";

    private bool IsFullMode => _session?.Package.SourcePath is { } source
        && (source is FullModeSkin.SourcePath or OriginalFullModeSource or EmberSkin.SourcePath or TenSkin.SourcePath || PhoneSkin.IsPhoneSource(source) || FullModeDesign.All.Any(d => d.SourcePath == source));

    /// <summary>The full mode look (Tools › Options, View › Full Mode Look); PARDEH_FULLMODE overrides it.</summary>
    private Pardeh.Skinning.Model.SkinPackage BuildFullMode()
    {
        _loadedLook = AppSettings.Current.FullModeLook;
        _loadedBranding = AppSettings.Current.ClassicBranding;
        _loadedLogoFollow = AppSettings.Current.LogoFollowsSkinColor;
        var look = !_lookChosen && Environment.GetEnvironmentVariable("PARDEH_FULLMODE") is { Length: > 0 } forced ? forced : AppSettings.Current.FullModeLook;
        return BuildFullMode(look);
    }

    private const string FullModePrefix = "fullmode:";

    /// <summary>The full modes the Skin Chooser offers (the imported original only when present).</summary>
    private IEnumerable<(string Key, string Name)> BuiltInFullModes() =>
        FullModeLooks.Where(l => l.Key != "auto" && (l.Key != "wmp9" || _wmpResources is not null));

    /// <summary>The look actually showing (resolves "auto").</summary>
    private string CurrentFullModeKey() =>
        _session?.Package.SourcePath == OriginalFullModeSource ? "wmp9"
        : _session?.Package.SourcePath == EmberSkin.SourcePath ? "ember"
        : _session?.Package.SourcePath == TenSkin.SourcePath ? "aero10"
        : PhoneSkin.KeyOf(_session?.Package.SourcePath) is { } phoneKey ? phoneKey
        : FullModeDesign.All.FirstOrDefault(d => d.SourcePath == _session?.Package.SourcePath)?.Key ?? "pardeh";

    private Pardeh.Skinning.Model.SkinPackage BuildFullMode(string look)
    {
        if (look == "auto" && OperatingSystem.IsAndroid())
            look = "fluent-phone"; // the default on a phone
        if (look == "ember")
            return EmberSkin.Build(); // design adapted from the VLC Redesign by Juxtopposed (CC BY 4.0)
        if (look == "aero10")
            return TenSkin.Build(); // Pardeh Ten: the look of a WMP 10 on Luna, drawn in code
        if (PhoneSkin.IsPhoneKey(look))
        {
            // The art is drawn at the screen's whole-number density (PixelScale: 3 on a 1080 px wide phone) but laid out in the
            // same skin units. PARDEH_ART_SCALE=1..4 overrides it (a development aid, e.g. to compare with the old 1x art).
            PhoneSkin.ArtScale = int.TryParse(Environment.GetEnvironmentVariable("PARDEH_ART_SCALE"), out var forced) && forced is >= 1 and <= 4
                ? forced
                : (int)Math.Clamp(Platform.DisplayDensity is > 0 and var density ? Math.Round(density, MidpointRounding.AwayFromZero) : PixelScale, 1, 4);
            StartupMarks.Mark($"phone skin art building at {PhoneSkin.ArtScale}x");
            var phoneSkin = PhoneSkin.Build(look); // the portrait-first phone layouts
            StartupMarks.Mark($"phone skin art built at {PhoneSkin.ArtScale}x");
            return phoneSkin;
        }
        if (look == "horizon")
            return HorizonSkin.Build(); // its own layout, following the WMP 9 web clone
        if (FullModeDesign.All.FirstOrDefault(d => d.Key == look) is { } design)
            return FullModeDesigner.Build(design);
        return look == "pardeh"
            ? FullModeSkin.Build(SkinLibrary.FullModeAssetFolder())
            : BuildOriginalFullMode() ?? FullModeSkin.Build(SkinLibrary.FullModeAssetFolder()); // "auto" / "wmp9"
    }


    // ---- Settings page: real controls laid over the skin's settings pane ------------------------

    private readonly Canvas _overlay = new();
    private Control? _settingsPanel;
    private object? _settingsPanelSession;
    private string? _settingsPanelLook;

    private Rendering.QuickPanel? _quickPanel;
    private bool _emberViz; // Ember: visualization instead of the music glyph (its script reports it with emberViz)

    /// <summary>Ember's quick panel (settings, audio and captions buttons): opens on a tab; the same button closes it.</summary>
    private void ToggleQuickPanel(string tab)
    {
        if (_quickPanel is { IsVisible: true } && _quickPanel.Tab == tab)
        {
            CloseQuickPanel();
            return;
        }

        var glassLook = CurrentFullModeKey() == "glass-phone";
        if (_quickPanel is not null && _quickPanel.Glass != glassLook)
        {
            _overlay.Children.Remove(_quickPanel); // another look took over: build the panel again in its palette
            _quickPanel = null;
        }

        if (_quickPanel is null)
        {
            if (_queuePanel is not null)
                _queuePanel.IsVisible = false;
            _quickPanel = new Rendering.QuickPanel(_player, tab, LoadAudioFile, LoadSubtitleFile, () => { CloseQuickPanel(); ShowPane("skins"); },
                () => _emberViz, on => { _emberViz = on; _session?.CurrentView?.Script.Execute($"if (typeof SetViz == 'function') SetViz({(on ? "true" : "false")});", "viz"); },
                glass: CurrentFullModeKey() == "glass-phone", close: CloseQuickPanel);
            _overlay.Children.Add(_quickPanel);
        }
        else
        {
            _quickPanel.Show(tab);
        }

        _quickPanel.IsVisible = true;
        if (_queuePanel is not null)
            _queuePanel.IsVisible = false; // one card at a time on the right
        PlaceQuickPanel();
    }

    private Rendering.VolumePopup? _volumePopup;

    /// <summary>Pardeh Glass phone: the volume popup above the transport pill's speaker button (anchor in skin pixels); the same button again closes it.</summary>
    private void ToggleVolumePopup(double anchorX, double anchorY)
    {
        if (_session?.CurrentView?.Root is not { } root)
            return;
        if (_volumePopup is { IsVisible: true })
        {
            CloseVolumePopup();
            return;
        }

        if (_volumePopup is null)
        {
            _volumePopup = new Rendering.VolumePopup(_player, CloseVolumePopup);
            _overlay.Children.Add(_volumePopup);
        }

        Canvas.SetLeft(_volumePopup, 0);
        Canvas.SetTop(_volumePopup, 0);
        _volumePopup.Open(root.Width, root.Height, anchorX, anchorY);
    }

    private void CloseVolumePopup() => _volumePopup?.Close();

    // ---- Phone looks: the visualization picker ------------------------------------------------------

    private Rendering.VizPicker? _vizPicker;
    private string? _vizPickerLook;

    /// <summary>The style chip over a phone look's visualization: a sheet of live thumbnails; the chip again (or Back) closes it.</summary>
    private void ToggleVizPicker()
    {
        if (_vizPicker is { IsVisible: true })
        {
            CloseVizPicker();
            return;
        }

        var look = CurrentFullModeKey();
        if (!PhoneSkin.IsPhoneKey(look) || _session?.CurrentView?.Root is null)
            return;
        if (_vizPicker is not null && _vizPickerLook != look)
        {
            _overlay.Children.Remove(_vizPicker); // another look: build the sheet again in its colours
            _vizPicker = null;
        }

        if (_vizPicker is null)
        {
            _vizPicker = new Rendering.VizPicker(((Rendering.ISettingsHost)this).PhoneTheme, look == "glass-phone", CloseVizPicker) { IsVisible = false };
            _vizPickerLook = look;
            _overlay.Children.Add(_vizPicker);
        }

        _vizPicker.IsVisible = true;
        PlaceVizPicker();
    }

    private void CloseVizPicker()
    {
        if (_vizPicker is not null)
            _vizPicker.IsVisible = false;
    }

    /// <summary>A bottom sheet over the lower part of the window (the visualization stays visible above it); a right-hand panel in landscape.</summary>
    private void PlaceVizPicker()
    {
        if (_vizPicker is not { IsVisible: true } || _session?.CurrentView?.Root is not { } root)
            return;
        var land = root.Width > root.Height * 1.1;
        if (land)
        {
            Canvas.SetLeft(_vizPicker, root.Width * 0.5 + 2);
            Canvas.SetTop(_vizPicker, 8);
            _vizPicker.Width = root.Width * 0.5 - 10;
            _vizPicker.Height = Math.Max(120, root.Height - 16);
            _vizPicker.SetColumns(2);
        }
        else
        {
            var sheet = Math.Min(root.Height * 0.55, 470);
            Canvas.SetLeft(_vizPicker, 8);
            Canvas.SetTop(_vizPicker, root.Height - 60 - sheet);
            _vizPicker.Width = root.Width - 16;
            _vizPicker.Height = sheet;
            _vizPicker.SetColumns(root.Width > 560 ? 3 : 2);
        }
    }

    // ---- Phone looks: the playback tools sheet (sleep timer, A-B repeat, volume levelling) -------

    private Rendering.PhoneToolsSheet? _toolsSheet;

    private const string PhoneToolsItem = "Sleep Timer, A-B Repeat, Markers...";

    private void ShowPhoneTools() => ShowPhoneTools(markersFirst: false);

    private void ShowPhoneTools(bool markersFirst)
    {
        if (!PhoneSkin.IsPhoneKey(CurrentFullModeKey()) || _session?.CurrentView?.Root is null)
            return;
        CloseQuickPanel();
        if (_toolsSheet is not null)
            _overlay.Children.Remove(_toolsSheet);
        _toolsSheet = new Rendering.PhoneToolsSheet(_player, ((Rendering.ISettingsHost)this).PhoneTheme, CloseToolsSheet, async index =>
        {
            CloseToolsSheet(); // the name prompt takes the screen; the sheet comes back after it
            await NameMarker(index);
            ShowPhoneTools(markersFirst: true);
        }, markersFirst);
        _overlay.Children.Add(_toolsSheet); // added last: over the library and settings pages
        PlaceToolsSheet();
    }

    private void CloseToolsSheet()
    {
        if (_toolsSheet is not null)
            _overlay.Children.Remove(_toolsSheet);
        _toolsSheet = null;
    }

    private void PlaceToolsSheet()
    {
        if (_toolsSheet is null || _session?.CurrentView?.Root is not { } root)
            return;
        _toolsSheet.Width = root.Width;
        _toolsSheet.Height = root.Height;
        _toolsSheet.Fit(root.Height);
        _toolsSheet.ZIndex = 20;
    }

    private void CloseQuickPanel()
    {
        CloseVolumePopup();
        CloseVizPicker();
        CloseToolsSheet();
        if (_quickPanel is not null)
            _quickPanel.IsVisible = false;
        if (_queuePanel is not null)
            _queuePanel.IsVisible = false;
        if (_libraryPanel is not null)
            _libraryPanel.IsVisible = false;
    }

    // ---- Ember: library side panel and queue card ----------------------------------------------

    private Rendering.EmberLibraryPanel? _libraryPanel;
    private Rendering.EmberQueuePanel? _queuePanel;
    private double _sideWidthShown;

    /// <summary>The rail's pages (home, videos, music, network, playlists, recent): the same page again closes it.</summary>
    private bool ToggleLibraryPanel(string page)
    {
        if (_libraryPanel is null)
        {
            _libraryPanel = new Rendering.EmberLibraryPanel(this, page.Length == 0 ? "home" : page);
            _library.Changed += (_, _) => _libraryPanel.Refresh();
            _overlay.Children.Add(_libraryPanel);
        }
        else if (_libraryPanel.IsVisible && (page.Length == 0 || _libraryPanel.Page == page || _libraryPanel.Page.StartsWith(page + "/", StringComparison.Ordinal)))
        {
            _libraryPanel.IsVisible = false;
            return false;
        }
        else if (page.Length > 0)
        {
            _libraryPanel.Open(page);
        }

        _libraryPanel.IsVisible = true;
        PlaceEmberPanels();
        return true;
    }

    /// <summary>The bottom bar's queue button: Queue / Bookmarks / Chapters (the quick panel closes).</summary>
    private bool ToggleQueuePanel()
    {
        if (_queuePanel is { IsVisible: true })
        {
            _queuePanel.IsVisible = false;
            return false;
        }

        if (_quickPanel is not null)
            _quickPanel.IsVisible = false;
        if (_queuePanel is null)
        {
            _queuePanel = new Rendering.EmberQueuePanel(this);
            _overlay.Children.Add(_queuePanel);
        }

        _queuePanel.IsVisible = true;
        _queuePanel.Show(_queuePanel.Tab);
        PlaceEmberPanels();
        return true;
    }

    /// <summary>Ember's metrics (skin pixels): the side panel at the stage's left, the cards at its lower right.</summary>
    private void PlaceEmberPanels()
    {
        if (_session?.CurrentView?.Root is not { } root)
            return;
        var sideWidth = 0.0;
        if (_libraryPanel is { IsVisible: true })
        {
            sideWidth = Math.Round(Math.Min(380, Math.Max(260, root.Width * 0.36)));
            Canvas.SetLeft(_libraryPanel, 56);
            Canvas.SetTop(_libraryPanel, 40);
            _libraryPanel.Width = sideWidth;
            _libraryPanel.Height = Math.Max(100, root.Height - 40 - 62);
        }

        // As in the redesign, the stage moves aside for the side panel: tell the skin when that changes.
        if (sideWidth != _sideWidthShown)
        {
            _sideWidthShown = sideWidth;
            _session.CurrentView.Script.Execute(FormattableString.Invariant($"if (typeof OnSidePanel == 'function') OnSidePanel({sideWidth});"), "side");
        }

        if (_queuePanel is { IsVisible: true })
        {
            var width = Math.Min(400, root.Width - 120);
            var height = Math.Max(120, root.Height - 40 - 62 - 20);
            Canvas.SetLeft(_queuePanel, root.Width - 14 - width - 10);
            Canvas.SetTop(_queuePanel, 50);
            _queuePanel.Width = width;
            _queuePanel.Height = height;
        }
    }

    Pardeh.Core.Playback.MediaPlayer Rendering.IEmberHost.Player => _player;

    Pardeh.Core.Library.MediaLibrary Rendering.IEmberHost.Library => _library;

    Pardeh.Core.Library.PlaylistStore Rendering.IEmberHost.Playlists => _playlists;

    void Rendering.IEmberHost.OpenFiles() => OpenFileWithDialog();

    void Rendering.IEmberHost.OpenUrl() => OpenUrl();

    void Rendering.IEmberHost.AddFolder() => AddFolderToLibrary();

    void Rendering.IEmberHost.ShowPage(string pane)
    {
        if (_libraryPanel is not null)
            _libraryPanel.IsVisible = false;
        ShowPane(pane);
    }

    Task<string?> Rendering.IEmberHost.Ask(string question, string initial) => AskAsync(question, initial);

    /// <summary>Over the stage's lower right, above the bottom bar (skin pixels; Ember's metrics).</summary>
    private void PlaceQuickPanel()
    {
        if (_quickPanel is not { IsVisible: true } || _session?.CurrentView?.Root is not { } root)
            return;
        if (_quickPanel.Glass)
        {
            // Pardeh Glass phone: a sheet across the window, resting on the bottom bar.
            var sheetHeight = Math.Min(root.Height * 0.62, 460);
            Canvas.SetLeft(_quickPanel, 12);
            Canvas.SetTop(_quickPanel, root.Height - 54 - sheetHeight);
            _quickPanel.Width = root.Width - 24;
            _quickPanel.Height = sheetHeight;
            return;
        }

        var width = Math.Min(400, root.Width - 120);
        var height = Math.Min(480, root.Height - 40 - 62 - 24);
        Canvas.SetLeft(_quickPanel, root.Width - 14 - width - 10);
        Canvas.SetTop(_quickPanel, root.Height - 62 - height - 10);
        _quickPanel.Width = width;
        _quickPanel.Height = height;
    }

    private void OpenPhoneEqualizer()
    {
        if (_settingsPanel is Rendering.MobileSettingsView live && ReferenceEquals(_settingsPanelSession, _session))
            live.OpenEqualizer();
        else
            Rendering.MobileSettingsView.PendingPage = "Equalizer"; // the panel is built when the page first shows
        ShowPane("skins");
    }

    private Rendering.MobileLibraryView? _phoneLibrary;
    private object? _phoneLibrarySession;

    /// <summary>Phone skins: the touch library laid over the Library page (skin pixels), hidden on other pages.</summary>
    private void UpdatePhoneLibrary()
    {
        if (_canvas.LibraryHost is not { Width: > 0, Height: > 0 } area || _session is null)
        {
            if (_phoneLibrary is not null)
                _phoneLibrary.IsVisible = false;
            return;
        }

        if (_phoneLibrary is null || !ReferenceEquals(_phoneLibrarySession, _session))
        {
            if (_phoneLibrary is not null)
                _overlay.Children.Remove(_phoneLibrary);
            var host = new Rendering.MobileLibraryHost(() => Top, () => _session?.Playlists, () => ShowPane("now"), () => ShowPane("radio"),
                path => _session?.Host.RunCommand("editTags", [path]));
            _phoneLibrary = new Rendering.MobileLibraryView(_player, _library, host, ((Rendering.ISettingsHost)this).PhoneTheme);
            _phoneLibrarySession = _session;
            _overlay.Children.Add(_phoneLibrary);
        }

        Canvas.SetLeft(_phoneLibrary, area.X);
        Canvas.SetTop(_phoneLibrary, area.Y);
        _phoneLibrary.Width = area.Width;
        _phoneLibrary.Height = area.Height;
        _phoneLibrary.IsVisible = true;
    }

    /// <summary>Shows the settings controls where the skin drew its Settings page (skin pixels), else hides them.</summary>
    private void UpdateSettingsOverlay()
    {
        PlaceQuickPanel();
        PlaceVizPicker();
        PlaceToolsSheet();
        PlaceEmberPanels();
        UpdatePhoneLibrary();
        if (_canvas.SettingsHost is not { Width: > 0, Height: > 0 } area || _session is null)
        {
            if (_settingsPanel is not null)
                _settingsPanel.IsVisible = false;
            return;
        }

        // A new full mode (or session), or the settings' own look changed: build the panel again.
        var look = $"{AppSettings.Current.ModernDialogs}/{AppSettings.Current.ModernFont}/{((Rendering.ISettingsHost)this).DarkSurface}";
        if (_settingsPanel is null && ReferenceEquals(_settingsPanelSession, _session) && look == _settingsPanelLook)
            return; // building it failed (logged once): leave the page out
        if (_settingsPanel is null || !ReferenceEquals(_settingsPanelSession, _session) || look != _settingsPanelLook)
        {
            if (_settingsPanel is not null)
            {
                _overlay.Children.Remove(_settingsPanel);
                _settingsPanel = null;
            }

            try
            {
                _settingsPanel = Rendering.OptionsDialog.Panel(Top!, _player, _library, this);
            }
            catch (Exception ex)
            {
                // Real error path: a page that cannot be built is logged once and left out (not rebuilt on every frame).
                Log("Settings page failed: " + ex.Message);
                _settingsPanelSession = _session;
                _settingsPanelLook = look;
                return;
            }

            _settingsPanelSession = _session;
            _settingsPanelLook = look;
            _overlay.Children.Add(_settingsPanel);
        }

        Canvas.SetLeft(_settingsPanel, area.X);
        Canvas.SetTop(_settingsPanel, area.Y);
        _settingsPanel.Width = area.Width;
        _settingsPanel.Height = area.Height;
        _settingsPanel.IsVisible = true;
    }

    IReadOnlyList<(string Key, string Name)> Rendering.ISettingsHost.SkinEntries() =>
        BuiltInFullModes().Select(l => (FullModePrefix + l.Key, l.Name.Replace(" (imported)", string.Empty) + " (full mode)"))
            .Concat(SkinLibrary.Discover().Select(p => (p, Path.GetFileNameWithoutExtension(p))))
            .ToList();

    string? Rendering.ISettingsHost.CurrentSkinEntry() => IsFullMode ? FullModePrefix + CurrentFullModeKey() : SkinLibrary.LastUsed();

    void Rendering.ISettingsHost.ApplySkinEntry(string key) => Dispatcher.UIThread.Post(() => ApplySkinEntry(key));

    void Rendering.ISettingsHost.AddSkin() => ChooseSkinWithDialog();

    void Rendering.ISettingsHost.OpenSkinsFolder() => SkinLibrary.OpenUserFolder();

    Avalonia.Media.Imaging.Bitmap? Rendering.ISettingsHost.SkinPicture(string key) =>
        SkinPreviews.Get(key) is { } picture ? Rendering.BitmapBridge.ToAvalonia(picture) : null;

    WindowStyle Rendering.ISettingsHost.WindowStyle => CurrentWindowStyle;

    bool Rendering.ISettingsHost.DarkSurface => IsFullMode && CurrentFullModeKey() is "fluent" or "ember" or "fluent-phone" or "ember-phone" or "glass-phone" or "winamp-phone" or "groove-phone"
        || IsFullMode && PhoneSkin.RetroSettingsTheme(CurrentFullModeKey()) is { Dark: true }
        || IsFullMode && FullModeDesign.All.FirstOrDefault(d => d.Key == CurrentFullModeKey())?.Retro is { Dark: true };

    void Rendering.ISettingsHost.SetWindowStyle(WindowStyle style) => SetWindowStyle(style);

    /// <summary>Phone settings colours per look: dark for Fluent / Ember / Glass, light for the blue XP-era ones.</summary>
    Rendering.MobileTheme Rendering.ISettingsHost.PhoneTheme => CurrentFullModeKey() switch
    {
        var key when PhoneSkin.RetroSettingsTheme(key) is { } retro => retro,
        "ember-phone" => new(true, Color.Parse("#141414"), Color.Parse("#222222"), Color.Parse("#E5801F")),
        "winamp-phone" => new(true, Color.Parse("#0A0E16"), Color.Parse("#161D2A"), Color.Parse("#FF9D00"), Art: "winamp"),
        "groove-phone" => new(true, Color.Parse("#0B2E78"), Color.Parse("#1A4CA8"), Color.Parse("#7FD4FF"), Art: "groove"),
        "glass-phone" => Rendering.MobileTheme.ForGlass(),
        "luna-phone" or "luna-plus-phone" or "aero10-phone" => new(false, Color.Parse("#EEF3FC"), Color.Parse("#FAFCFF"), Color.Parse("#2E55C4"), Color.Parse("#7F9CE6"), Color.Parse("#3A5FCD"), Colors.White, Royal: true),
        "horizon-phone" => new(false, Color.Parse("#EDF3FB"), Color.Parse("#FFFFFF"), Color.Parse("#3B5996"), Color.Parse("#EFF4FB"), Color.Parse("#D6E3F4"), Color.Parse("#22356E")),
        _ => new(true, Color.Parse("#1C1C1C"), Color.Parse("#2B2B2B"), Color.Parse("#60CDFF")),
    };

    private Rendering.SkinPreview SkinPreviews => _skinPreview ??= new Rendering.SkinPreview(_decoder, () => _wmpResources, Log, path =>
        path.StartsWith(FullModePrefix, StringComparison.OrdinalIgnoreCase) ? BuildFullMode(path[FullModePrefix.Length..]) : null);

    /// <summary>Applies a Skins entry: a built-in full mode ("fullmode:&lt;look&gt;") or a .wmz skin (skin mode).</summary>
    private void ApplySkinEntry(string key)
    {
        if (key.StartsWith(FullModePrefix, StringComparison.OrdinalIgnoreCase))
        {
            SetFullModeLook(key[FullModePrefix.Length..]);
            if (!IsFullMode)
                LoadFullMode();
        }
        else
        {
            LoadSkin(key);
        }
    }

    private string? _loadedLook;
    private bool _loadedBranding = true;
    private bool _loadedLogoFollow;
    private bool _lookChosen;

    /// <summary>
    /// The settings in the Settings pane (full mode): section, name, current value, and a step to its next value
    /// (or an action). Player, full mode, the classic / Windows 11 appearance options, audio and library.
    /// </summary>
    private List<(string Section, string Name, Func<string> Value, Action Change)> AllSettings()
    {
        var s = AppSettings.Current;
        var eq = _player.Equalizer;
        (string, string, Func<string>, Action) Look(string name, Func<bool> get, Action<bool> set) =>
            ("Appearance", name, () => get() ? "Windows 11" : "Classic XP", () => { set(!get()); s.Save(); });
        (string, string, Func<string>, Action) OnOff(string section, string name, Func<bool> get, Action<bool> set, bool save = true) =>
            (section, name, () => get() ? "On" : "Off", () => { set(!get()); if (save) s.Save(); });
        string[] quiet = ["Off", "Medium difference", "Little difference"];
        string[] starts = ["last", "full", "skin"];
        string[] startNames = ["The mode I used last", "Full mode", "Skin mode"];

        List<(string Section, string Name, Func<string> Value, Action Change)> all =
        [
            ("Player", "Start in", () => startNames[Math.Max(0, Array.IndexOf(starts, s.StartMode))],
                () => { s.StartMode = starts[(Array.IndexOf(starts, s.StartMode) + 1) % starts.Length]; s.Save(); }),
            OnOff("Player", "One Pardeh at a time (open files in it)", () => s.SingleInstance, v => s.SingleInstance = v),
            OnOff("Player", "Always on top", () => s.AlwaysOnTop, v => s.AlwaysOnTop = v),
            ("Full mode", "Window style", () => WindowStyles[(int)CurrentWindowStyle].Name, NextWindowStyle),
            ("Full mode", "Full mode look", () => FullModeLooks.FirstOrDefault(l => l.Key == s.FullModeLook).Name ?? "Automatic", NextFullModeLook),
            Look("Fonts", () => s.ModernFont, v => s.ModernFont = v),
            Look("Title bar style", () => s.ModernTitleBar, v => s.ModernTitleBar = v),
            Look("Menu bar style", () => s.ModernMenuBar, v => s.ModernMenuBar = v),
            Look("Context menus and popups", () => s.ModernMenus, v => s.ModernMenus = v),
            Look("Dialogs", () => s.ModernDialogs, v => s.ModernDialogs = v),
            Look("Scrollbars and tooltips", () => s.ModernControls, v => s.ModernControls = v),
            Look("Task pane toolbars", () => s.ModernToolbars, v => s.ModernToolbars = v),
            Look("Full-screen control bar", () => s.ModernFullScreenBar, v => s.ModernFullScreenBar = v),
            OnOff("Appearance", "Rounded window corners", () => s.RoundedCorners, v => s.RoundedCorners = v),
            OnOff("Appearance", "Classic branding", () => s.ClassicBranding, v => s.ClassicBranding = v),
            OnOff("Audio", "Crossfading between tracks", () => eq.CrossFade, v => eq.CrossFade = v, save: false),
            ("Audio", "Crossfade overlap", () => $"{eq.CrossFadeSeconds:0} s", () => eq.CrossFadeSeconds = eq.CrossFadeSeconds >= 10 ? 1 : eq.CrossFadeSeconds + 1),
            OnOff("Audio", "Auto Volume Leveling", () => eq.AutoVolume, v => eq.AutoVolume = v, save: false),
            OnOff("Audio", "Bass and width enhancement (SRS WOW)", () => eq.Enhanced, v => eq.Enhanced = v, save: false),
            ("Audio", "Quiet Mode", () => quiet[Math.Clamp(eq.QuietMode, 0, 2)], () => eq.QuietMode = (eq.QuietMode + 1) % 3),
            ("Audio", "Play speed", () => _player.Rate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "×",
                () => _player.Rate = MediaPlayer.RatePresets[(Array.IndexOf(MediaPlayer.RatePresets, _player.Rate) + 1) % MediaPlayer.RatePresets.Length]),
            ("Library", "Monitored folders", () => $"{_library.Folders.Count} folders (change in Options)", () => _ = ShowOptions()),
            ("Library", "Search folders now", () => "Run", () => _ = _library.ScanAsync()),
        ];
        return OperatingSystem.IsAndroid()
            ? all.Where(a => a.Name is not ("Always on top" or "Window style" or "Title bar style" or "Menu bar style" or "Rounded window corners" or "Full-screen control bar" or "One Pardeh at a time (open files in it)")).ToList()
            : all;
    }

    /// <summary>Full mode's "Show menu bar" (Ctrl+M): Pardeh's own window frame (title bar and menu bar) on, or the compact skin.</summary>
    private void ToggleTitleBar() => SetWindowStyle(_framed ? WindowStyle.Compact : WindowStyle.TitleBar);

    private void NextFullModeLook()
    {
        var looks = FullModeLooks.Where(l => l.Key == "auto" || BuiltInFullModes().Any(b => b.Key == l.Key)).ToList();
        var index = looks.FindIndex(l => l.Key == AppSettings.Current.FullModeLook);
        SetFullModeLook(looks[(index + 1) % looks.Count].Key);
    }

    /// <summary>The full mode looks: setting key and name.</summary>
    internal static readonly (string Key, string Name)[] FullModeLooks =
    [
        ("auto", "Automatic"),
        ("wmp9", "Windows Media Player 9 (imported)"),
        ("pardeh", "Pardeh Classic"),
        ("horizon", "Horizon"),
        ("luna", "Luna"),
        ("fluent", "Fluent"),
        ("ember", "Ember"),
        ("aero10", "Pardeh Ten"),
        ("luna-phone", "Luna Phone"),
        ("fluent-phone", "Fluent Phone"),
        ("ember-phone", "Ember Phone"),
        ("horizon-phone", "Horizon Phone"),
        ("aero10-phone", "Pardeh Ten Phone"),
        ("luna-plus-phone", "Luna Phone Plus"),
        ("glass-phone", "Pardeh Glass"),
        ("winamp-phone", "Winamp Phone"),
        ("groove-phone", "Groove Phone"),
        .. PhoneSkin.RetroLookNames,
        .. PhoneSkin.DesktopRetro.Select(l => (l.Key, l.Name)),
    ];

    private MenuItem FullModeLookMenu() => new()
    {
        Header = "Full Mode Look",
        ItemsSource = FullModeLooks.Select(look =>
        {
            var item = Item(look.Name, () => SetFullModeLook(look.Key), check: AppSettings.Current.FullModeLook == look.Key);
            item.IsEnabled = look.Key != "wmp9" || _wmpResources is not null;
            return (object)item;
        }).ToArray(),
    };

    /// <summary>Switches the full mode look, reloading full mode if it is showing.</summary>
    private void SetFullModeLook(string look)
    {
        _lookChosen = true; // the user's choice beats the PARDEH_FULLMODE development override from now on
        AppSettings.Current.FullModeLook = look;
        AppSettings.Current.Save();
        _fullMode = null;
        if (IsFullMode)
            LoadFullMode();
    }

    /// <summary>The classic player's own resources, if imported (see WmpResources).</summary>
    private Pardeh.Skinning.Wmp.WmpResources? _wmpResources = Pardeh.Skinning.Wmp.WmpResources.Find();

    /// <summary>Shows full mode (the main window with the taskbar rail and Media Library).</summary>
    private void LoadFullMode()
    {

        StartupMarks.Mark("building the full mode");
        _fullMode ??= BuildFullMode();
        StartupMarks.Mark("full mode built; starting session");
        StartSession(_fullMode);
        // Development aid: PARDEH_START_TASK=RadioTuner opens a task pane (for snapshots).
        if (Environment.GetEnvironmentVariable("PARDEH_START_TASK") is { Length: > 0 } task)
        {
            if (_session?.Package.SourcePath == OriginalFullModeSource)
                _session.App.SetMember("currentTask", task);
            else
                ShowPane(task switch
                {
                    "MediaLibrary" => "library",
                    "RadioTuner" => "radio",
                    "SkinViewer" => "skins",
                    "PortableDevice" => "burn",
                    "InfoCenter" => "info",
                    "Queue" => "queue",
                    _ => "now",
                });
        }
        // Development aid: PARDEH_QUICK_PANEL=playback|video|audio|subtitles opens Ember's quick panel (for snapshots).
        if (Environment.GetEnvironmentVariable("PARDEH_QUICK_PANEL") is { Length: > 0 } quickTab)
            Dispatcher.UIThread.Post(() =>
            {
                if (quickTab == "tools")
                    ShowPhoneTools();
                else if (quickTab == "markers")
                    ShowPhoneTools(markersFirst: true);
                else if (quickTab == "lyrics")
                    _session?.CurrentView?.Script.Evaluate("typeof ToggleLyrics == 'function' ? ToggleLyrics() : 0");
                else
                    ToggleQuickPanel(quickTab);
            }, DispatcherPriority.Background);
        // PARDEH_LIBRARY_PANEL=home|videos|music|… and PARDEH_QUEUE_PANEL=1 open Ember's panels (for snapshots).
        if (Environment.GetEnvironmentVariable("PARDEH_LIBRARY_PANEL") is { Length: > 0 } sidePage)
            Dispatcher.UIThread.Post(() => ToggleLibraryPanel(sidePage), DispatcherPriority.Background);
        if (Environment.GetEnvironmentVariable("PARDEH_QUEUE_PANEL") is { Length: > 0 } queueTab)
            Dispatcher.UIThread.Post(() =>
            {
                ToggleQueuePanel();
                if (queueTab is "bookmarks" or "chapters")
                    _queuePanel?.Show(queueTab);
            }, DispatcherPriority.Background);
        if (Environment.GetEnvironmentVariable("PARDEH_START_MAXIMIZED") == "1")
            Dispatcher.UIThread.Post(ToggleMaximize, DispatcherPriority.Background);
        if (Environment.GetEnvironmentVariable("PARDEH_START_FULLSCREEN") == "1")
            Dispatcher.UIThread.Post(() => SetFullScreen(true), DispatcherPriority.Background);
        SetTitle("Pardeh");
        SkinLibrary.LastMode = "full";
    }

    /// <summary>
    /// The original 2003 full mode, decoded from the player's own compiled skin when the user has imported
    /// their copy of its resources. Null otherwise (Pardeh's own drawing is used).
    /// </summary>
    private Pardeh.Skinning.Model.SkinPackage? BuildOriginalFullMode()
    {
        if (Environment.GetEnvironmentVariable("PARDEH_FULLMODE") == "pardeh")
            return null;
        if (_wmpResources?.GetText("res://wmploc/RT_TEXT/MAINAPPSKIN.WSZ") is not { } compiled)
            return null;
        try
        {
            var theme = new Pardeh.Skinning.Wmp.WszDecoder(compiled, _wmpResources.GetString).Decode();
            return WmzLoader.FromNode(Pardeh.Skinning.Wmp.MainAppSkin.Adapt(theme), OriginalFullModeSource);
        }
        catch (Exception ex)
        {
            Log($"Cannot decode the original full mode: {ex.Message}");
            return null;
        }
    }

    /// <summary>Switches to skin mode with the last skin used (or the first one found).</summary>
    private void LoadSkinMode()
    {
        var path = SkinLibrary.LastUsed() ?? SkinLibrary.Discover().FirstOrDefault();
        if (path is null)
        {
            Platform.Inform(Top, "No skins are installed yet. Choose a .wmz skin to use skin mode.", "Pardeh");
            ChooseSkinWithDialog();
            return;
        }

        LoadSkin(path);
    }

    private bool LoadSkin(string path)
    {
        try
        {
            var package = Pardeh.Skinning.Model.SkinLoaders.Load(path); // .wmz / .wms / .wsz: the loader plug-in for the extension
            StartSession(package);
            SetTitle($"Pardeh - {package.Title}");
            SkinLibrary.RememberCurrent(path);
            SkinLibrary.LastMode = "skin";
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Log($"Cannot load skin '{path}': {ex.Message}");
            Platform.Inform(Top, $"Cannot load the skin:\n{path}\n\n{ex.Message}", "Pardeh");
            return false;
        }
    }

    private void StartSession(Pardeh.Skinning.Model.SkinPackage package)
    {
        CloseQuickPanel();
        _sideWidthShown = 0; // the new skin starts with its stage in place
        _session?.Dispose();
        // Phone layouts: touch-sized list rows in the library, queue and list panes.
        var phone = PhoneSkin.IsPhoneSource(package.SourcePath);
        Rendering.OptionsDialog.Compact = phone;
        Pardeh.Skinning.Runtime.Elements.LibraryElement.RowHeight = phone ? 30 : 15;
        Pardeh.Skinning.Runtime.Elements.PlaylistElement.RowHeight = phone ? (PhoneSkin.KeyOf(package.SourcePath) is { } key ? key switch { "glass-phone" => 30, "winamp-phone" => 40, "groove-phone" => 56, _ => 46 } : 30) : 15; // touch queue rows of two lines (title, artist); Glass: one line
        Pardeh.Skinning.Runtime.Panes.ListPaneElement.RowHeight = phone ? 30 : 16;
        _session = new SkinSession(package, _player, this, _decoder, _preferences) { Library = _library, Playlists = _playlists, Resources = _wmpResources, Lyrics = _lyricsFeed };
        StartupMarks.Mark("session created");
        _session.Start();
        StartupMarks.Mark("session started (script run)");
        // Only full mode has the menu bar frame.
        ApplyWindowStyle();
        Rendering.UiTheme.Apply(((Rendering.ISettingsHost)this).DarkSurface); // dark menus over dark full modes
    }

    private bool _firstTick;

    private void OnTick()
    {
        if (!_firstTick)
        {
            _firstTick = true;
            StartupMarks.Mark("first tick");
        }

        var now = _clock.Elapsed;
        var elapsed = now - _lastTick;
        _lastTick = now;
        if (elapsed > TimeSpan.FromMilliseconds(250))
            elapsed = TimeSpan.FromMilliseconds(250);

        _session?.Tick(elapsed);
        _lyrics.Tick();
        if (!AppVisibility.Foreground)
            return; // in the background: no drawing, no animation
        if (_fullScreen)
        {
            var cursor = _canvas.Controls.IsShown ? Cursor.Default : HiddenCursor;
            if (!ReferenceEquals(_canvas.Cursor, cursor))
                _canvas.Cursor = cursor;
        }
        UpdateShape();
        _canvas.InvalidateVisual();
        UpdateSettingsOverlay();
        UpdateDesktopLyrics(elapsed);
        UpdateNetworkBanner();
        TakeSnapshotIfRequested(now);
        if (Environment.GetEnvironmentVariable("PARDEH_DIAG") == "1" && now - _lastDiag > TimeSpan.FromSeconds(1))
        {
            _lastDiag = now;
            Log($"diag window={Shell?.Width}x{Shell?.Height} client={ClientSize} view={_session?.CurrentView?.Root.Width}x{_session?.CurrentView?.Root.Height} scaling={RenderScaling} pixelScale={PixelScale} layoutScale={LayoutScale} state={WindowState}");
        }
    }

    /// <summary>
    /// PARDEH_SNAPSHOT=path.png renders the skin to a file a few seconds after start (focus-independent),
    /// for visual checks and golden-image comparisons. PARDEH_SNAPSHOT_DELAY sets the delay in seconds.
    /// </summary>
    private void TakeSnapshotIfRequested(TimeSpan now)
    {
        if (_snapshotTaken || Environment.GetEnvironmentVariable("PARDEH_SNAPSHOT") is not { Length: > 0 } path)
            return;
        var delay = double.TryParse(Environment.GetEnvironmentVariable("PARDEH_SNAPSHOT_DELAY"), System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 3;
        if (now.TotalSeconds < delay || _session?.CurrentView is not { } view)
            return;

        _snapshotTaken = true;
        if (Environment.GetEnvironmentVariable("PARDEH_SNAPSHOT_MENU") is { Length: > 0 } menuSpec)
            SnapshotMenu(menuSpec, path);
        if (Environment.GetEnvironmentVariable("PARDEH_SNAPSHOT_DIALOG") is { Length: > 0 } dialogSpec)
            SnapshotDialog(dialogSpec, path);
        SnapshotLyricsWindow(path);
        if (_framed)
        {
            // With the menu bar shown: the whole frame (caption, menus, skin).
            var framedSize = new PixelSize((int)_frame.Bounds.Width, (int)_frame.Bounds.Height);
            using var framed = new Avalonia.Media.Imaging.RenderTargetBitmap(framedSize, new Vector(96, 96));
            framed.Render(_frame);
            framed.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            Log($"Snapshot (framed) {framedSize.Width}x{framedSize.Height} saved to {path}");
            return;
        }

        if (_barsOnly && !_fullScreen && Clip is Avalonia.Media.RectangleGeometry rounded)
        {
            // The whole view, so the rounded corners show in the picture.
            var rounds = new PixelSize((int)rounded.Rect.Width, (int)rounded.Rect.Height);
            using var clipped = new Avalonia.Media.Imaging.RenderTargetBitmap(rounds, new Vector(96, 96));
            clipped.Render(this);
            clipped.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            Log($"Snapshot (bars only) {rounds.Width}x{rounds.Height} saved to {path}");
            return;
        }

        var size = _fullScreen
            ? new PixelSize((int)_canvas.Bounds.Width, (int)_canvas.Bounds.Height)
            : new PixelSize((int)view.Root.Width, (int)view.Root.Height);
        using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96, 96));
        if (!_fullScreen && _scaler.Child is { } skinAndOverlay)
        {
            bitmap.Render(skinAndOverlay); // the skin and the Settings page controls over it
        }
        else
        {
            using var context = bitmap.CreateDrawingContext();
            _canvas.Render(context);
        }
        bitmap.Save(path, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        Log($"Snapshot {size.Width}x{size.Height} saved to {path}; window at {Position} size {ClientSize}, state {_player.PlayState}, video {_player.HasVideo}");
    }

    /// <summary>
    /// PARDEH_SNAPSHOT_MENU=View (or View/3: also open item 3's submenu) opens that menu-bar menu and renders it
    /// to &lt;snapshot&gt;-menu.png (and -submenu.png), to check menu styling without screen capture.
    /// </summary>
    private void SnapshotMenu(string spec, string path)
    {
        var parts = spec.Split('/');
        var menu = new ContextMenu { ItemsSource = MenuItems(parts[0]) };
        menu.Open(_framed ? _frame : _canvas);
        var stem = Path.Combine(Path.GetDirectoryName(path) ?? ".", Path.GetFileNameWithoutExtension(path));

        static void Save(Control control, string file)
        {
            var size = new PixelSize(Math.Max(1, (int)Math.Ceiling(control.Bounds.Width)), Math.Max(1, (int)Math.Ceiling(control.Bounds.Height)));
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96, 96));
            bitmap.Render(control);
            bitmap.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }

        DispatcherTimer.RunOnce(() =>
        {
            Save(menu, stem + "-menu.png");
            Log($"Menu snapshot {menu.Bounds.Size} saved to {stem}-menu.png");
            if (menu.ItemsSource is object[] { Length: > 0 } all && all[0] is MenuItem first)
                Log($"  menu font {menu.FontFamily} {menu.FontSize}; item font {first.FontFamily} {first.FontSize}; theme {menu.Theme?.TargetType} {menu.ActualThemeVariant}");
            if (parts.Length > 1 && int.TryParse(parts[1], out var index) && menu.ItemsSource is object[] items
                && index < items.Length && items[index] is MenuItem item)
            {
                item.IsSelected = true;
                item.IsSubMenuOpen = true;
                DispatcherTimer.RunOnce(() =>
                {
                    if (Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(item).OfType<Avalonia.Controls.Primitives.Popup>().FirstOrDefault()?.Child is Control sub)
                    {
                        Save(sub, stem + "-submenu.png");
                        Save(menu, stem + "-menu-selected.png");
                        Log($"Submenu snapshot saved to {stem}-submenu.png");
                    }
                }, TimeSpan.FromMilliseconds(600));
            }
        }, TimeSpan.FromMilliseconds(600));
    }

    /// <summary>
    /// PARDEH_SNAPSHOT_DIALOG=options/2 (Options on tab 2) or prompt: opens that dialog and renders its content
    /// to &lt;snapshot&gt;-dialog.png, to check the dialog look without screen capture.
    /// </summary>
    private void SnapshotDialog(string spec, string path)
    {
        var parts = spec.Split('/');
        if (parts[0] == "prompt")
            _ = AskAsync("Name of the new playlist:", "My playlist");
        else if (parts[0] == "about")
            ShowAbout();
        else
            _ = ShowOptions();
        var file = Path.Combine(Path.GetDirectoryName(path) ?? ".", Path.GetFileNameWithoutExtension(path) + "-dialog.png");
        DispatcherTimer.RunOnce(() =>
        {
            if (Application.Current?.ApplicationLifetime is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                || desktop.Windows.FirstOrDefault(w => w != Shell) is not { } dialog)
                return;
            if (parts.Length > 1 && int.TryParse(parts[1], out var tab)
                && Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(dialog).OfType<TabControl>().FirstOrDefault() is { } tabs)
                tabs.SelectedIndex = tab;
            DispatcherTimer.RunOnce(() =>
            {
                var size = new PixelSize((int)dialog.ClientSize.Width, (int)dialog.ClientSize.Height);
                using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96, 96));
                bitmap.Render(dialog);
                bitmap.Save(file, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                Log($"Dialog snapshot {size} saved to {file}");
            }, TimeSpan.FromMilliseconds(500));
        }, TimeSpan.FromMilliseconds(800));
    }

    private bool _snapshotTaken;
    private TimeSpan _lastDiag;
    private Rendering.SkinPreview? _skinPreview;
    private IDisposable? _integration;

    private void SetTitle(string title)
    {
        if (Shell is { } shell)
            shell.Title = title;
    }

    // Windows used for dialogs exist only on the desktop; a single-view head has its own (TODO) in-view dialogs.
    private Task<string?> AskAsync(string question, string initial) =>
        Shell is { } shell ? Rendering.PromptDialog.Ask(shell, question, initial) : Rendering.InViewPrompt.Ask(_overlay, _session?.CurrentView?.Root.Width ?? 360, question, initial);

    private Task ShowOptions() =>
        Shell is { } shell ? Rendering.OptionsDialog.Show(shell, _player, _library, ApplySettings, this) : Task.CompletedTask;

    private static IWmpImportService WmpImport => Platform.WmpImport!;
    private readonly Pardeh.Core.Library.PlaylistStore _playlists = new(Path.Combine(AppPaths.DataDirectory, "Playlists"));



    // ---- Menu bar frame -----------------------------------------------------------------------

    private readonly Rendering.LunaFrame _frame = new();
    private bool _framed;

    /// <summary>Extra window size (DIPs) taken by the Luna caption, menu row and border when the menu bar is shown.</summary>
    private (double Width, double Height) ChromeSize => _framed
        ? (_frame.Chrome.Left + _frame.Chrome.Right, _frame.Chrome.Top + _frame.Chrome.Bottom)
        : (0, 0);

    /// <summary>Windows 11 rounding for the framed window (Appearance option); never for the shaped skin, full screen or maximized.</summary>
    private void UpdateCorners()
    {
        Platform.SetRoundedCorners(Top, _framed && !_fullScreen && _restoreBounds is null && AppSettings.Current.RoundedCorners);
        UpdateBarsClip();
    }

    /// <summary>Full mode's "Show menu bar": a framed window with caption and menus, or the frameless shaped skin.</summary>
    private void SetFramed(bool framed)
    {
        if (framed == _framed)
            return;
        _framed = framed;
        if (framed)
        {
            Content = null;
            _frame.Child = _scaler;
            Content = _frame;
            Platform.ClearWindowShape(Top);
        }
        else
        {
            Content = null;
            _frame.Child = null;
            Content = _scaler;
            _shapedVersion = -1;
        }

        UpdateCorners();
        if (_session?.CurrentView is { } view)
            SizeToView(view.Root.Width, view.Root.Height);
    }

    private void OnFrameCommand(string command, PointerPressedEventArgs? e)
    {
        switch (command)
        {
            case "minimize":
                WindowState = WindowState.Minimized;
                break;
            case "maximize" or "toggleMaximize":
                ToggleMaximize();
                break;
            case "close":
                Close();
                break;
            case "drag" when e is not null:
                BeginWindowMove(e);
                break;
        }
    }
    // ---- Full screen --------------------------------------------------------------------------

    private bool _fullScreen;
    private static readonly Cursor HiddenCursor = new(StandardCursorType.None);

    /// <summary>Video / visualization over the whole screen (Now Playing's "View full screen"). Esc or double-click leaves.</summary>
    private void SetFullScreen(bool on)
    {
        if (on == _fullScreen)
            return;
        _fullScreen = on;
        _canvas.FullScreen = on;
        if (on)
        {
            Platform.ClearWindowShape(Top);
            _scaler.LayoutTransform = null;
            WindowState = WindowState.FullScreen;
            _canvas.Controls.Activity();
        }
        else
        {
            WindowState = WindowState.Normal;
            _canvas.Cursor = Cursor.Default;
            ApplyScale();
        }

        _canvas.InvalidateMeasure();
        UpdateCorners();
        _canvas.InvalidateVisual();
    }
    // ---- Scaling and shape --------------------------------------------------------------------

    private double _appliedZoom = 1;

    /// <summary>Makes the skin larger or smaller (the next / previous size of View > Skin Size).</summary>
    internal void StepSkinZoom(int direction)
    {
        var zooms = AppSettings.SkinZooms;
        var current = AppSettings.Current.SkinZoom;
        var index = direction == 0 ? Array.IndexOf(zooms, 1.0) : direction > 0 ? Array.FindIndex(zooms, z => z > current + 1e-6) : Array.FindLastIndex(zooms, z => z < current - 1e-6);
        if (index < 0)
            return;
        AppSettings.Current.SkinZoom = zooms[index];
        AppSettings.Current.Save();
    }

    private void ApplyScale()
    {
        var scaling = RenderScaling;
        PixelScale = Math.Max(1, Math.Round(scaling, MidpointRounding.AwayFromZero)) * (OperatingSystem.IsAndroid() ? 1 : AppSettings.Current.SkinZoom);
        _appliedZoom = AppSettings.Current.SkinZoom;
        // Bitmap skins at a whole-number size stay pixel-exact (crisp, like the original stretched); at a fraction (125 %, 150 % ...)
        // pixel-exact drawing doubles some pixels and not others, so they are smoothed instead.
        if (!OperatingSystem.IsAndroid())
            Rendering.SkinRenderer.SmoothScaling = Math.Abs(PixelScale - Math.Round(PixelScale)) > 0.01;
        // Skins drawn in code are drawn again at the enlarged size's density, so they stay sharp instead of being stretched.
        LunaArt.DefaultScale = !OperatingSystem.IsAndroid() && _appliedZoom > 1.001 ? (int)Math.Clamp(Math.Ceiling(PixelScale - 0.001), 1, 4) : 1;
        _scaler.LayoutTransform = new ScaleTransform(LayoutScale, LayoutScale);
        if (_session?.CurrentView is { } view)
            SizeToView(view.Root.Width, view.Root.Height);
        _shapedVersion = -1;
    }

    // ---- Android: fit to the screen, inside the system bars -------------------------------------

    private double? _fitScale;
    private bool _androidLayouting;

    /// <summary>
    /// Single-view Android layout. Content sits inside the system bars (padding from the safe area). A phone full mode
    /// fills the view: the skin is resized to the screen at whole-number density. Every other skin keeps its design size
    /// and is scaled uniformly to fit, centred and letterboxed.
    /// </summary>
    private void ApplyAndroidLayout()
    {
        if (!OperatingSystem.IsAndroid() || Top is not { } top || _androidLayouting)
            return;
        _androidLayouting = true;
        try
        {
            var insets = top.InsetsManager?.SafeAreaPadding ?? default;
            Padding = insets;
            var availW = Math.Max(1, top.ClientSize.Width - insets.Left - insets.Right);
            var availH = Math.Max(1, top.ClientSize.Height - insets.Top - insets.Bottom);
            var phoneKey = PhoneSkin.KeyOf(_session?.Package.SourcePath);
            var backdrop = PhoneSkin.Backdrop(phoneKey);
            if (top.InsetsManager is { } bars)
                bars.SystemBarColor = backdrop.Top;
            // Light icons on dark bars, dark icons on light ones (Avalonia's own pick does not always follow).
            static bool Dark(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) < 140;
            Platform.SetSystemBarIcons(Dark(backdrop.Top), Dark(backdrop.Bottom));
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                // Status bar band, then the page colour (so a landscape cutout gutter beside the content is one tone), then the gesture bar band.
                GradientStops = BackdropStops(backdrop, insets, top.ClientSize.Height),
            };
            if (_session?.CurrentView is not { } view)
                return;

            _scaler.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            _scaler.VerticalAlignment = phoneKey is not null ? Avalonia.Layout.VerticalAlignment.Top : Avalonia.Layout.VerticalAlignment.Center;
            if (phoneKey is not null)
            {
                _fitScale = null;
                Rendering.SkinRenderer.SmoothScaling = false;
                _scaler.LayoutTransform = new ScaleTransform(LayoutScale, LayoutScale);
                var width = Math.Floor(availW / LayoutScale);
                var height = Math.Floor(availH / LayoutScale);
                if (Math.Abs(view.Root.Width - width) > 0.5)
                    view.Root.Width = width;
                if (Math.Abs(view.Root.Height - height) > 0.5)
                    view.Root.Height = height;
            }
            else
            {
                var scale = Math.Min(availW / Math.Max(1, view.Root.Width), availH / Math.Max(1, view.Root.Height));
                _fitScale = scale;
                Rendering.SkinRenderer.SmoothScaling = true;
                _scaler.LayoutTransform = new ScaleTransform(scale, scale);
            }

            _canvas.InvalidateMeasure();
            _canvas.InvalidateVisual();
        }
        finally
        {
            _androidLayouting = false;
        }
    }

    private static GradientStops BackdropStops((Color Top, Color Middle, Color Bottom) backdrop, Thickness insets, double height)
    {
        var upper = Math.Clamp(insets.Top / Math.Max(1, height), 0, 0.5);
        var lower = Math.Clamp(1 - insets.Bottom / Math.Max(1, height), 0.5, 1);
        return
        [
            new GradientStop(backdrop.Top, 0), new GradientStop(backdrop.Top, upper),
            new GradientStop(backdrop.Middle, upper), new GradientStop(backdrop.Middle, lower),
            new GradientStop(backdrop.Bottom, lower), new GradientStop(backdrop.Bottom, 1),
        ];
    }

    private void SizeToView(double width, double height)
    {
        if (Shell is { } shell)
        {
            shell.Width = width * LayoutScale + ChromeSize.Width;
            shell.Height = height * LayoutScale + ChromeSize.Height;
        }
        else if (OperatingSystem.IsAndroid())
        {
            ApplyAndroidLayout();
        }

        _canvas.InvalidateMeasure();
    }

    private void UpdateShape()
    {
        if (_barsOnly)
            UpdateBarsClip(); // follows the view's size
        if (_session?.CurrentView is not { } view || _fullScreen || _framed || _barsOnly)
            return;
        if (view == _shapedView && view.LayoutVersion == _shapedVersion && PixelScale == _shapedScale)
            return;

        _shapedView = view;
        _shapedVersion = view.LayoutVersion;
        _shapedScale = PixelScale;
        Platform.ApplyWindowShape(Top, SkinMask.Compute(view), PixelScale);
    }

    // ---- Window events ------------------------------------------------------------------------

    /// <summary>The host window closed (or the app is ending): stop everything.</summary>
    public void Shutdown()
    {
        _queueTimer?.Stop();
        _queueStore?.Save();
        _player.SaveVideoPosition();
        _player.Loudness?.Dispose();
        _integration?.Dispose();
        _timer.Stop();
        ShutdownDesktopLyrics();
        CloseQuickPanel();
        _session?.Dispose();
        _lyrics.Dispose();
        _player.Dispose();
    }

    /// <summary>The host window was resized; <paramref name="byUser"/> is a drag on its edge.</summary>
    public void OnShellResized(bool byUser)
    {
        // A user resize (BeginResizeDrag) changes the window; push the new size to the skin.
        if (_session?.CurrentView is { } view && byUser)
        {
            var width = (ClientSize.Width - ChromeSize.Width) / LayoutScale;
            var height = (ClientSize.Height - ChromeSize.Height) / LayoutScale;
            if (Math.Abs(view.Root.Width - width) > 0.5)
                view.Root.Width = Math.Round(width);
            if (Math.Abs(view.Root.Height - height) > 0.5)
                view.Root.Height = Math.Round(height);
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles()?
            .Select(f => f.TryGetLocalPath())
            .OfType<string>()
            .ToList() ?? [];
        if (files.Count == 0)
        {
            // A link dragged from a browser arrives as text.
            if (NetworkMedia.FindAddress(e.DataTransfer.TryGetText()) is { } address)
                _ = OpenAddress(address);
            return;
        }

        var skin = files.FirstOrDefault(StartupOptions.IsSkinFile);
        if (skin is not null)
        {
            var installed = SkinLibrary.Install(skin);
            LoadSkin(installed ?? skin);
            return;
        }

        _player.OpenFiles(files);
    }

    // ---- Commands -----------------------------------------------------------------------------

    internal void OpenFileWithDialog()
    {
        var file = Platform.OpenMediaFile(Top);
        if (file is not null)
            _player.OpenFiles([file]);
    }

    internal async void ChooseSkinWithDialog()
    {
        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a skin",
            FileTypeFilter = [new FilePickerFileType("Skins (Windows Media Player, Winamp classic)") { Patterns = Pardeh.Skinning.Model.SkinLoaders.PickerPatterns }],
        });
        var path = result.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null)
            LoadSkin(SkinLibrary.Install(path) ?? path);
    }

    private void ShowAppMenu()
    {
        MenuItem Item(string header, Action action, string? command = null)
        {
            var item = new MenuItem { Header = header, InputGesture = OperatingSystem.IsAndroid() ? null : Keymaps.Hint(command) };
            item.Click += (_, _) => action();
            return item;
        }

        var playing = _player.PlayState == PlayState.Playing;
        var skinsMenu = new MenuItem { Header = "Skins" };
        var skinItems = new List<object>();
        foreach (var look in BuiltInFullModes())
        {
            var key = look.Key;
            skinItems.Add(Item(look.Name + " (full mode)", () => { SetFullModeLook(key); LoadFullMode(); }));
        }

        skinItems.Add(new Separator());
        foreach (var skin in SkinLibrary.Discover())
        {
            var path = skin;
            var item = Item(Path.GetFileNameWithoutExtension(path), () => LoadSkin(path));
            if (string.Equals(path, _session?.Package.SourcePath, StringComparison.OrdinalIgnoreCase))
                item.Icon = new TextBlock { Text = "●", FontSize = 8 };
            skinItems.Add(item);
        }

        if (skinItems.Count > 0)
            skinItems.Add(new Separator());
        skinItems.Add(Item("Add Skin...", ChooseSkinWithDialog));
        skinItems.Add(Item("Open Skins Folder", SkinLibrary.OpenUserFolder));
        skinsMenu.ItemsSource = skinItems;

        var items = new List<object>
        {
            Item("Open File...", OpenFileWithDialog, KeyCmd.OpenFile),
            new Separator(),
            Item(playing ? "Pause" : "Play", () => { if (playing) _player.Pause(); else _player.Play(); }, KeyCmd.PlayPause),
            Item("Stop", _player.Stop, KeyCmd.Stop),
            Item("Previous", _player.Previous, KeyCmd.Previous),
            Item("Next", _player.Next, KeyCmd.Next),
            Item(_player.Shuffle ? "Shuffle: On" : "Shuffle: Off", () => _player.Shuffle = !_player.Shuffle, KeyCmd.Shuffle),
            Item(_player.Loop ? "Repeat: On" : "Repeat: Off", () => _player.Loop = !_player.Loop, KeyCmd.Repeat),
            Item(_player.Mute ? "Unmute" : "Mute", () => _player.Mute = !_player.Mute, KeyCmd.Mute),
            new Separator(),
            skinsMenu,
            new Separator(),
            Item("Exit", Close, KeyCmd.Quit),
        };
        if (PhoneSkin.IsPhoneKey(CurrentFullModeKey()))
            items.Insert(items.IndexOf(skinsMenu), Item(PhoneToolsItem, ShowPhoneTools));
        if (UndoFileChangeEntry() is { } undo)
            items.Insert(items.IndexOf(skinsMenu), Item(undo.Header, undo.Run));
        // The compact window and the bars-only window have no menu bar: what it holds is reachable here too.
        if (!OperatingSystem.IsAndroid())
        {
            items.InsertRange(items.IndexOf(skinsMenu),
            [
                Item("Open URL...", OpenUrl, KeyCmd.OpenUrl), RecentAddressMenu(), new Separator(),
                PlaybackToolsMenu(), SubtitlesMenu(), LyricsMenu(), new Separator(),
                WindowStyleMenu(), Item("Options...", () => _ = ShowOptions(), KeyCmd.Preferences),
            ]);
        }

        var appItems = items.ToArray();
        var menu = new ContextMenu { ItemsSource = OperatingSystem.IsAndroid() ? WithoutDesktopItems(appItems) : appItems };
        OpenTracked(menu, _canvas);
    }

    private ContextMenu? _activeMenu;
    private long? _backLeftFullScreenAt;

    /// <summary>Opens a menu and remembers it, so the Back key can close it first.</summary>
    private void OpenTracked(ContextMenu menu, Control target)
    {
        _activeMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_activeMenu, menu))
                _activeMenu = null;
        };
        menu.Open(target);
    }

    /// <summary>
    /// Android Back key: closes an open menu or panel first, then a page's own inner level (library list to its
    /// tree), then returns to Now Playing from another page. False when nothing was left to do (the app may go to
    /// the background).
    /// </summary>
    public bool HandleBack()
    {
        // The Escape key event of this same Back press has just left full screen: that was the step back.
        if (OperatingSystem.IsAndroid() && _backLeftFullScreenAt is { } at)
        {
            _backLeftFullScreenAt = null;
            if (_clock.ElapsedMilliseconds - at < 400)
                return true;
        }

        if (_activeMenu is { } menu)
        {
            _activeMenu = null;
            menu.Close();
            return true;
        }

        if (_closeTagEditor is { } closeEditor)
        {
            _closeTagEditor = null;
            closeEditor();
            return true;
        }

        if (_barMenu is not null)
        {
            CloseBarMenu();
            return true;
        }

        if (_volumePopup is { IsVisible: true })
        {
            CloseVolumePopup();
            return true;
        }

        if (_toolsSheet is not null)
        {
            CloseToolsSheet();
            return true;
        }

        if (_vizPicker is { IsVisible: true })
        {
            CloseVizPicker(); // Back closes the style sheet first, then (next press) leaves the visualization
            return true;
        }

        if (_quickPanel is { IsVisible: true })
        {
            CloseQuickPanel();
            return true;
        }

        // A phone look's full screen (its title bar / bottom bar button): Back leaves it.
        if (_fullScreen && PhoneSkin.IsPhoneSource(_session?.Package.SourcePath))
        {
            SetFullScreen(false);
            return true;
        }

        // The mobile Settings: sheet, then sub-page, then the page itself falls through to "back to Now Playing".
        if (_settingsPanel is Rendering.MobileSettingsView { IsVisible: true } mobile && mobile.GoBack())
            return true;
        if (_phoneLibrary is { IsVisible: true } library && library.GoBack())
            return true;

        // The phone skins' script: library list -> library tree, any other page -> Now Playing.
        return _session?.CurrentView is { } view && PhoneSkin.IsPhoneSource(_session.Package.SourcePath)
            && Pardeh.Skinning.Runtime.SkinValue.ToDouble(view.Script.Evaluate("typeof OnBack == 'function' && OnBack() ? 1 : 0")) > 0;
    }

    // ---- Android: swipe between the bottom navigation pages -------------------------------------------

    /// <summary>A page swipe needs at least this much horizontal travel (skin pixels) and twice as much horizontal as vertical.</summary>
    private const double PageSwipeDistance = 80;

    private int _swipePointer = -1;
    private Point _swipeStart;
    private bool _swipeBlocked;

    /// <summary>
    /// Phone looks on Android only: a clear horizontal touch swipe moves to the next / previous page of the bottom
    /// navigation (the skin script's <c>SwipePage</c>). Watches touches on the way down (tunnel) so it works over the
    /// skin and the Library / Settings overlays alike; sliders, text boxes, the stage and cover while playing (their own
    /// swipe seeks) and anything with a sheet or menu open keep their gestures.
    /// </summary>
    private void InstallPageSwipe()
    {
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Pointer.Type != PointerType.Touch)
                return;
            _swipePointer = e.Pointer.Id;
            _swipeStart = e.GetPosition(_canvas);
            _swipeBlocked = !PageSwipeAllowed(e);
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        // Moves count too: once a scroll viewer's gesture recognizer takes the pointer (any small vertical drift), Avalonia
        // stops routing this touch's later events, so the swipe is decided as soon as it is clear, not on release.
        AddHandler(PointerMovedEvent, (_, e) => TrySwipe(e), Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, (_, e) =>
        {
            TrySwipe(e);
            if (e.Pointer.Id == _swipePointer)
                _swipePointer = -1;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void TrySwipe(PointerEventArgs e)
    {
        if (e.Pointer.Type != PointerType.Touch || e.Pointer.Id != _swipePointer || _swipeBlocked)
            return;
        var delta = e.GetPosition(_canvas) - _swipeStart;
        if (Math.Abs(delta.X) < PageSwipeDistance || Math.Abs(delta.X) < Math.Abs(delta.Y) * 2)
            return;
        // Once per touch. A horizontal swipe is never a tap on the row it started on.
        _swipeBlocked = true;
        _canvas.CancelInteraction(e);
        SwipePage(delta.X < 0 ? 1 : -1);
    }

    private bool PageSwipeAllowed(PointerPressedEventArgs e)
    {
        if (_fullScreen || !PhoneSkin.IsPhoneSource(_session?.Package.SourcePath))
            return false;
        if (_activeMenu is not null || _barMenu is not null || _volumePopup is { IsVisible: true } || _quickPanel is { IsVisible: true } || _vizPicker is { IsVisible: true })
            return false;
        if (_settingsPanel is Rendering.MobileSettingsView { IsVisible: true, SheetOpen: true }
            || _phoneLibrary is { IsVisible: true, SheetOpen: true })
            return false;

        if (ReferenceEquals(e.Source, _canvas))
            return !_canvas.OwnsHorizontalDrag(e.GetPosition(_canvas));
        for (var visual = e.Source as Avalonia.Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is Avalonia.Controls.Primitives.RangeBase or Avalonia.Controls.Primitives.Thumb or TextBox
                || visual is ScrollViewer { HorizontalScrollBarVisibility: not Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled })
                return false;
        }

        return true;
    }

    private bool SwipePage(int direction) =>
        _session?.CurrentView is { } view
        && Pardeh.Skinning.Runtime.SkinValue.ToDouble(view.Script.Evaluate($"typeof SwipePage == 'function' && SwipePage({direction}) ? 1 : 0")) > 0;

    // ---- ISkinHost ------------------------------------------------------------------------------

    private static readonly System.Text.RegularExpressions.Regex DesktopOnlyHandler = new(
        @"view\.(minimize|close|maximize|restore)|command\(\s*['""](maximize|togglemaximize|titlebar)['""]|command\(\s*['""]menu['""]\s*,\s*['""](file|view|play|tools|help)['""]",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Android: a skin's minimize / maximize / close buttons and its menu bar words mean nothing on a phone.</summary>
    private static void HideDesktopControls(SkinView view)
    {
        foreach (var element in view.Elements)
        {
            if (element.Handlers.Values.Any(h => DesktopOnlyHandler.IsMatch(h)))
                element.Visible = false;
        }
    }

    public void ViewChanged(SkinView view)
    {
        _canvas.View = view;
        if (OperatingSystem.IsAndroid())
            HideDesktopControls(view);
        SizeToView(view.Root.Width, view.Root.Height);
        _shapedVersion = -1;
        UpdateShape();
        _canvas.InvalidateVisual();
    }

    public void ResizeView(SkinView view, double width, double height)
    {
        if (Environment.GetEnvironmentVariable("PARDEH_DIAG") == "1")
            Log($"diag ResizeView {width}x{height} max={MaxViewSize} scaling={RenderScaling} pixelScale={PixelScale}");
        if (view != _session?.CurrentView)
            return;
        SizeToView(width, height);
        KeepOnScreen(width, height);
    }

    public (double Width, double Height) MaxViewSize
    {
        get
        {
            var screen = CurrentScreen;
            if (screen is null)
                return (double.MaxValue, double.MaxValue);
            var area = screen.WorkingArea; // physical pixels
            // Leave some desktop visible when a skin grows to fit a large video; a maximized window takes it all.
            var share = _restoreBounds is null ? 0.85 : 1.0;
            return (Math.Floor(area.Width * share / PixelScale), Math.Floor(area.Height * share / PixelScale));
        }
    }

    /// <summary>After the skin grows (e.g. to fit a video), move the window so it stays fully visible.</summary>
    private void KeepOnScreen(double viewWidth, double viewHeight)
    {
        var screen = CurrentScreen;
        if (screen is null)
            return;
        var area = screen.WorkingArea;
        var w = (int)Math.Ceiling(viewWidth * PixelScale);
        var h = (int)Math.Ceiling(viewHeight * PixelScale);
        var x = Math.Clamp(Position.X, area.X, Math.Max(area.X, area.Right - w));
        var y = Math.Clamp(Position.Y, area.Y, Math.Max(area.Y, area.Bottom - h));
        if (x != Position.X || y != Position.Y)
            Position = new PixelPoint(x, y);
    }

    public void MinimizeView(SkinView view) => WindowState = WindowState.Minimized;

    public void BeginResize(SkinView view, string edge)
    {
        if (_canvas.CurrentPress is not { } press)
            return;
        var windowEdge = edge.ToLowerInvariant() switch
        {
            "left" => WindowEdge.West,
            "right" => WindowEdge.East,
            "top" => WindowEdge.North,
            "bottom" => WindowEdge.South,
            "topleft" => WindowEdge.NorthWest,
            "topright" => WindowEdge.NorthEast,
            "bottomleft" => WindowEdge.SouthWest,
            _ => WindowEdge.SouthEast,
        };
        // The resize loop is modal; start it after the script handler that asked for it has returned.
        Dispatcher.UIThread.Post(() =>
        {
            _canvas.CancelInteraction(press);
            if (Shell is { } shell)
            {
                shell.CanResize = true;
                shell.BeginResizeDrag(windowEdge, press);
            }
        });
    }

    public void CloseSkin() => Close();

    // Mode switches are posted: the request comes from inside the current skin's script, which must finish first.
    public void ReturnToFullMode() => Dispatcher.UIThread.Post(LoadFullMode);

    public object? RunCommand(string name, IReadOnlyList<object?> args)
    {
        string Arg(int i) => i < args.Count ? args[i]?.ToString() ?? string.Empty : string.Empty;
        switch (name.ToLowerInvariant())
        {
            case "menu":
                ShowMenu(Arg(0));
                return null;
            case "skinmode":
                Dispatcher.UIThread.Post(LoadSkinMode);
                return null;
            case "fullmode":
                Dispatcher.UIThread.Post(LoadFullMode);
                return null;
            case "openfile":
                OpenFileWithDialog();
                return null;
            case "chooseskin":
                Dispatcher.UIThread.Post(ChooseSkinWithDialog);
                return null;
            case "skinpreview":
                return SkinPreviews.Get(Arg(0));
            case "fullscreen":
                Dispatcher.UIThread.Post(() => SetFullScreen(true));
                return null;
            case "titlebar":
                Dispatcher.UIThread.Post(ApplyWindowStyle); // the skin's titleBarVisible changed
                return null;
            case "libraryaddfolder":
                Dispatcher.UIThread.Post(AddFolderToLibrary);
                return null;
            case "openurl":
                Dispatcher.UIThread.Post(OpenUrl);
                return null;
            case "edittags":
                var tagPath = Arg(0);
                Dispatcher.UIThread.Post(() => _ = EditTags(tagPath));
                return null;
            case "libraryimportplaylist":
                Dispatcher.UIThread.Post(ImportPlaylist);
                return null;
            case "libraryaddfiles":
                Dispatcher.UIThread.Post(AddFilesToLibrary);
                return null;
            case "reveal":
                var revealPath = Arg(0);
                if (File.Exists(revealPath))
                    Platform.RevealInFileManager(revealPath);
                return null;
            case "prompt" when args.Count >= 3 && args[2] is Action<string> answer:
                var (question, initial) = (Arg(0), Arg(1));
                Dispatcher.UIThread.Post(async () =>
                {
                    if (await AskAsync(question, initial) is { } text)
                        answer(text);
                });
                return null;
            case "listskins":
                // Pardeh's built-in full modes first ("fullmode:<look>"), then the installed .wmz skins.
                return BuiltInFullModes().Select(look => FullModePrefix + look.Key).Concat(SkinLibrary.Discover()).ToList();
            case "emberviz":
                _emberViz = args.Count > 0 && SkinValue.ToBool(args[0]);
                return null;
            case "librarypanel":
                return ToggleLibraryPanel(Arg(0)); // true: now open (the rail lights that page)
            case "queuepanel":
                return ToggleQueuePanel();
            case "quickpanel":
                var quickTab = Arg(0);
                Dispatcher.UIThread.Post(() => ToggleQuickPanel(quickTab));
                return null;
            case "vizpicker":
                Dispatcher.UIThread.Post(ToggleVizPicker);
                return null;
            case "openequalizer": // the Winamp Phone's EQ key: the Settings page, on its Equalizer sub-page
                Dispatcher.UIThread.Post(OpenPhoneEqualizer);
                return null;
            case "vizstep":
                // A swipe on the visualization (vizSwipe): the next (1) or previous (-1) style, remembered; the picker, if open, follows.
                var vizDirection = args.Count > 0 && SkinValue.ToDouble(args[0]) < 0 ? -1 : 1;
                Dispatcher.UIThread.Post(() =>
                {
                    Rendering.VizDirector.Step(CurrentFullModeKey() == "glass-phone", vizDirection);
                    _vizPicker?.Refresh();
                });
                return null;
            case "volumepopup":
                var (volX, volY) =(SkinValue.ToDouble(args.Count > 0 ? args[0] : 0.0), SkinValue.ToDouble(args.Count > 1 ? args[1] : 0.0));
                Dispatcher.UIThread.Post(() => ToggleVolumePopup(volX, volY));
                return null;
            case "hiddenpages":
                return string.Join(",", AppSettings.Current.HiddenPages);
            case "lyrics": // lyrics <action>: paste, import, search, delete, refresh, enable (the LYRICS element's buttons)
                return LyricsCommand(args);
            case "togglefavorite":
                return _player.ToggleFavorite();
            case "isfavorite":
                return _player.CurrentIsFavorite;
            case "queuemenu":
                Dispatcher.UIThread.Post(() => { if (_session?.CurrentView is { } sortView) Pardeh.Skinning.Runtime.QueueMenus.ShowSortMenu(_session, sortView); });
                return null;
            case "queuefilter":
                Dispatcher.UIThread.Post(() => { if (_session?.CurrentView is { } filterView) Pardeh.Skinning.Runtime.QueueMenus.ShowFilterMenu(_session, filterView); });
                return null;
            case "toggletitlebar":
                Dispatcher.UIThread.Post(ToggleTitleBar);
                return null;
            case "settingslist":
                return AllSettings().Select(a => new[] { a.Section, a.Name, a.Value() }).ToList();
            case "changesetting":
                if (args.Count > 0 && Convert.ToInt32(args[0], System.Globalization.CultureInfo.InvariantCulture) is var row
                    && AllSettings() is var settings && row >= 0 && row < settings.Count)
                    Dispatcher.UIThread.Post(settings[row].Change);
                return null;
            case "options":
                Dispatcher.UIThread.Post(() => _ = ShowOptions());
                return null;
            case "skindisplayname":
                var entry = Arg(0);
                return FullModeLooks.FirstOrDefault(l => FullModePrefix + l.Key == entry).Name is { } lookName ? lookName.Replace(" (imported)", string.Empty) + " (full mode)" : null;
            case "currentskin":
                return IsFullMode ? FullModePrefix + CurrentFullModeKey() : SkinLibrary.LastUsed();
            case "applyskin":
                var skinPath = Arg(0);
                Dispatcher.UIThread.Post(() => ApplySkinEntry(skinPath));
                return null;
            case "copytodevice":
                var files = args.Skip(1).Select(a => a?.ToString()).OfType<string>().ToList();
                var destination = Arg(0);
                Dispatcher.UIThread.Post(() => CopyToDevice(files, destination));
                return null;
            case "openskinsfolder":
                SkinLibrary.OpenUserFolder();
                return null;
            case "movetobackground": // a phone skin's minimize button: Android goes to the background, the desktop minimizes
                Dispatcher.UIThread.Post(() =>
                {
                    if (!Platform.MoveToBackground() && Shell is not null)
                        WindowState = WindowState.Minimized;
                });
                return null;
            case "exitapp": // a phone skin's close button
                Dispatcher.UIThread.Post(() =>
                {
                    if (!Platform.ExitApp())
                        Close();
                });
                return null;
            case "maximize":
                Dispatcher.UIThread.Post(ToggleMaximize);
                return null;
            case "ismaximized":
                return _restoreBounds is not null;
            case "rescan":
                _ = _library.ScanAsync();
                return null;
            case "getrating": // the library's 0-5 star rating of the playing item (-1: not in the library)
                return _library.Items.FirstOrDefault(i => string.Equals(i.Path, _player.CurrentMedia?.Source, StringComparison.OrdinalIgnoreCase)) is { } rated ? (double)rated.Rating : -1.0;
            case "setrating": // setrating <stars>: tapping the star that is already the rating clears it
                if (_player.CurrentMedia?.Source is { } ratedPath && int.TryParse(Arg(0), out var stars))
                {
                    var current = _library.Items.FirstOrDefault(i => string.Equals(i.Path, ratedPath, StringComparison.OrdinalIgnoreCase));
                    if (current is not null)
                        _library.SetRating(current.Path, current.Rating == stars ? 0 : stars);
                }

                return null;
            default:
                Log($"Unknown command from skin: {name}");
                return null;
        }
    }

    /// <summary>Fills the screen's working area (or restores), resizing the skin rather than the OS window frame.</summary>
    /// <summary>
    /// A drag on the skin's background moves the window. When the window is maximized it is first restored to its normal size and put
    /// under the cursor (as Windows does), otherwise a window that fills the screen has nowhere to go.
    /// </summary>
    private void BeginWindowMove(PointerPressedEventArgs e)
    {
        if (Shell is not { } shell)
            return;
        if (_restoreBounds is { } restore)
        {
            var inShell = e.GetPosition(shell);
            var cursor = shell.PointToScreen(inShell);
            var fraction = Math.Clamp(inShell.X / Math.Max(1, shell.Bounds.Width), 0, 1);
            ToggleMaximize(); // back to the size and place it had
            var width = (int)Math.Round(restore.Width * PixelScale + ChromeSize.Width * RenderScaling);
            shell.Position = new PixelPoint(cursor.X - (int)(width * fraction), Math.Max(0, cursor.Y - 14));
        }

        var from = shell.Position;
        var pressed = e.GetPosition(_canvas);
        var under = _canvas.DescribeAt(pressed);
        shell.BeginMoveDrag(e);
        // Windows runs the move until the button is released; the log line says whether the window went anywhere.
        DispatcherTimer.RunOnce(
            () => Log($"Window move from a press at ({pressed.X:0},{pressed.Y:0}) on {under}: window {from} -> {shell.Position}, style {CurrentWindowStyle}"),
            TimeSpan.FromMilliseconds(400));
    }

    private void ToggleMaximize()
    {
        if (_session?.CurrentView is not { } view)
            return;
        if (_restoreBounds is { } restore)
        {
            _restoreBounds = null;
            _frame.Maximized = false;
            UpdateCorners();
            Position = restore.Position;
            view.Root.Width = restore.Width;
            view.Root.Height = restore.Height;
            return;
        }

        var screen = CurrentScreen;
        if (screen is null)
            return;
        _restoreBounds = new PixelRect(Position, new PixelSize((int)view.Root.Width, (int)view.Root.Height));
        _frame.Maximized = true;
        UpdateCorners();
        var area = screen.WorkingArea;
        Position = area.Position;
        // Working area in DIPs, less the frame when the menu bar is shown, in skin pixels.
        view.Root.Width = Math.Floor((area.Width / RenderScaling - ChromeSize.Width) / LayoutScale);
        view.Root.Height = Math.Floor((area.Height / RenderScaling - ChromeSize.Height) / LayoutScale);
    }

    /// <summary>View › Now Playing / Media Library / Skin Chooser: the full mode's task, or Pardeh's own full mode's ShowPane().</summary>
    private void ShowPane(string pane)
    {
        if (_session?.Package.SourcePath == OriginalFullModeSource)
        {
            _session.App.SetMember("currentTask", pane switch { "library" => "MediaLibrary", "skins" => "SkinViewer", _ => "NowPlaying" });
            return;
        }

        _session?.CurrentView?.Script.Execute($"if (typeof ShowPane == 'function') ShowPane('{pane}');", "menu");
    }

    private static TextBlock? Glyph(string glyph) => OperatingSystem.IsAndroid() ? null : // Segoe icon font: not on Android
        new() { Text = glyph, FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14 };

    private MenuItem GlyphItem(string glyph, string header, Action action, string? command = null)
    {
        var item = Item(header, action, command);
        item.Icon = Glyph(glyph);
        return item;
    }

    private MenuItem Item(string header, Action action, string? command = null, bool check = false)
    {
        var item = new MenuItem
        {
            Header = header,
            InputGesture = OperatingSystem.IsAndroid() ? null : Keymaps.Hint(command), // no desktop key hints on a phone
            Icon = check ? new TextBlock { Text = "✓", FontSize = 11 } : null,
        };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>The classic menu bar menus (File, View, Play, Tools, Help).</summary>
    private void ShowMenu(string name)
    {
        // No name: the whole menu bar (the full-mode "Show menu bar" button).
        object[] items = name.Length == 0
            ? new[] { "File", "View", "Play", "Tools", "Help" }.Select(menu => (object)new MenuItem { Header = menu, ItemsSource = MenuItems(menu) }).ToArray()
            : MenuItems(name);
        OpenBarMenu(new ContextMenu { ItemsSource = items }, _canvas);
    }

    private ContextMenu? _barMenu;

    /// <summary>
    /// Opens a menu-bar menu (from the frame or a skin's menu buttons). Only one is open at a time, like a real
    /// menu bar: opening another closes the current one first.
    /// </summary>
    private void OpenBarMenu(ContextMenu menu, Control target)
    {
        CloseBarMenu();
        _barMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_barMenu, menu))
            {
                _barMenu = null;
                _frame.MenuClosed();
            }
        };
        menu.Open(target);
    }

    private void CloseBarMenu()
    {
        var open = _barMenu;
        _barMenu = null;
        open?.Close();
        _frame.MenuClosed();
    }

    private object[] MenuItems(string name) => OperatingSystem.IsAndroid() ? WithoutDesktopItems(MenuItemsCore(name)) : MenuItemsCore(name);

    private static readonly string[] DesktopOnlyItems = ["Title Bar and Menu Bar", "Exit", "Always on top", "Minimize", "Maximize"];

    /// <summary>Android: drops the window-only items (title bar, exit …) and separators left dangling.</summary>
    private static object[] WithoutDesktopItems(object[] items)
    {
        var kept = new List<object>();
        foreach (var item in items.Where(i => i is not MenuItem { Header: string h } || !DesktopOnlyItems.Contains(h)))
        {
            if (item is Separator && (kept.Count == 0 || kept[^1] is Separator))
                continue;
            kept.Add(item);
        }

        if (kept.Count > 0 && kept[^1] is Separator)
            kept.RemoveAt(kept.Count - 1);
        return kept.ToArray();
    }

    private object[] MenuItemsCore(string name)
    {
        var playing = _player.PlayState == PlayState.Playing;
        return name.ToLowerInvariant() switch
        {
            // One menu with icons (Ember's "…" button, like the VLC redesign's): main actions, then the classic menus.
            "app" =>
            [
                GlyphItem("", "Open File(s)...", OpenFileWithDialog, KeyCmd.OpenFile),
                GlyphItem("", "Open URL...", OpenUrl, KeyCmd.OpenUrl),
                GlyphItem("", "Add Folder to Library...", AddFolderToLibrary),
                new Separator(),
                GlyphItem("", "Full Screen", () => SetFullScreen(true)),
                GlyphItem("", "Title Bar and Menu Bar", ToggleTitleBar, KeyCmd.TitleBar),
                .. OperatingSystem.IsAndroid() ? Array.Empty<object>() : [WindowStyleMenu()],
                GlyphItem("", "Settings", () => ShowPane("skins")),
                .. PhoneSkin.IsPhoneKey(CurrentFullModeKey()) ? new object[] { GlyphItem("", PhoneToolsItem, ShowPhoneTools) } : [],
                .. UndoFileChangeEntry() is { } undoChange ? new object[] { Item(undoChange.Header, undoChange.Run) } : [],
                new Separator(),
                new MenuItem { Header = "Play", Icon = Glyph(""), ItemsSource = MenuItems("play") },
                new MenuItem { Header = "View", Icon = Glyph(""), ItemsSource = MenuItems("view") },
                new MenuItem { Header = "Tools", Icon = Glyph(""), ItemsSource = MenuItems("tools") },
                new MenuItem { Header = "Help", Icon = Glyph(""), ItemsSource = MenuItems("help") },
                new Separator(),
                GlyphItem("", "Exit", Close, KeyCmd.Quit),
            ],
            "file" =>
            [
                Item("Open...", OpenFileWithDialog, KeyCmd.OpenFile),
                Item("Open URL...", OpenUrl, KeyCmd.OpenUrl),
                .. OperatingSystem.IsAndroid() ? Array.Empty<object>() : [RecentAddressMenu()],
                Item("Add Folder to Library...", AddFolderToLibrary),
                Item("Search Library Again", () => _ = _library.ScanAsync()),
                .. UndoFileChangeEntry() is { } undoFile ? new object[] { Item(undoFile.Header, undoFile.Run) } : [],
                new Separator(),
                .. ImportMenu(),
                Item("Exit", Close, KeyCmd.Quit),
            ],
            "view" =>
            [
                Item("Full Mode", LoadFullMode, KeyCmd.FullMode, check: IsFullMode),
                Item("Skin Mode", LoadSkinMode, KeyCmd.SkinMode, check: !IsFullMode),
                FullModeLookMenu(),
                WindowStyleMenu(),
                SkinSizeMenu(),
                AspectRatioMenu(),
                new Separator(),
                Item("Now Playing", () => ShowPane("now")),
                Item("Media Library", () => ShowPane("library")),
                Item("Settings", () => ShowPane("skins")),
                new Separator(),
                .. OperatingSystem.IsAndroid() ? Array.Empty<object>() : [LyricsMenu()],
                EnhancementsMenu(),
            ],
            "play" =>
            [
                Item(playing ? "Pause" : "Play", () => { if (playing) _player.Pause(); else _player.Play(); }, KeyCmd.PlayPause),
                Item("Stop", _player.Stop, KeyCmd.Stop),
                new Separator(),
                Item("Previous", _player.Previous, KeyCmd.Previous),
                Item("Next", _player.Next, KeyCmd.Next),
                new Separator(),
                Item("Shuffle", () => _player.Shuffle = !_player.Shuffle, KeyCmd.Shuffle, check: _player.Shuffle),
                Item("Repeat", () => _player.Loop = !_player.Loop, KeyCmd.Repeat, check: _player.Loop),
                new Separator(),
                PlaySpeedMenu(),
                PlaybackToolsMenu(),
                .. OperatingSystem.IsAndroid() ? Array.Empty<object>() : [LyricsMenu()],
                AudioTracksMenu(),
                SubtitlesMenu(),
                new Separator(),
                Item("Volume Up", () => _player.Volume += VolumeStep, KeyCmd.VolumeUp),
                Item("Volume Down", () => _player.Volume -= VolumeStep, KeyCmd.VolumeDown),
                Item("Mute", () => _player.Mute = !_player.Mute, KeyCmd.Mute, check: _player.Mute),
            ],
            "nowplayingoptions" => NowPlayingOptions(),
            "tools" => EqualizerItems(),
            _ =>
            [
                Item("About Pardeh", ShowAbout),
            ],
        };
    }


    /// <summary>Play › Captions and Subtitles: on / off, and a file chosen by hand.</summary>
    private MenuItem SubtitlesMenu()
    {
        var items = new List<object>
        {
            Item("Show", () => _player.ShowSubtitles = !_player.ShowSubtitles, KeyCmd.SubtitleToggle, check: _player.ShowSubtitles && _player.Subtitles is not null),
            new Separator(),
        };

        // The subtitle files lying next to the video (movie.srt, movie.fa.srt ...): one click each.
        var found = SubtitleFilesNextToMedia();
        foreach (var file in found)
        {
            var path = file;
            items.Add(Item(System.IO.Path.GetFileName(path), () =>
            {
                if (_player.LoadSubtitles(path))
                    _player.ShowSubtitles = true;
                else
                    ShowMessage("No subtitles could be read from that file.");
            }, check: string.Equals(_player.Subtitles?.Path, path, StringComparison.OrdinalIgnoreCase)));
        }

        // Subtitle tracks inside the file (Matroska, MP4 ...).
        var embedded = _player.SubtitleTracks;
        foreach (var embeddedTrack in embedded)
        {
            var track = embeddedTrack;
            items.Add(Item("Embedded: " + track.Label, async () =>
            {
                if (!await _player.SelectSubtitleTrackAsync(track))
                    ShowMessage("No subtitles could be read from that track.");
            }, check: _player.EmbeddedSubtitle == track));
        }

        if (found.Count == 0 && embedded.Count == 0)
            items.Add(new MenuItem { Header = _player.CurrentMedia is null ? "(open a video first)" : "(no subtitles in or next to the video)", IsEnabled = false });
        items.Add(new Separator());
        items.Add(SubtitleDelayMenu());
        items.Add(Item("Load Subtitle File...", LoadSubtitleFile));
        return new MenuItem { Header = "Captions and Subtitles", ItemsSource = items };
    }

    /// <summary>Subtitle timing: 0.1 s steps (keys G and H), and back to none.</summary>
    private MenuItem SubtitleDelayMenu() => new()
    {
        Header = $"Delay ({Pardeh.Core.Playback.MediaPlayer.DescribeDelay(_player.SubtitleDelay)})",
        IsEnabled = _player.Subtitles is not null,
        ItemsSource = new object[]
        {
            Item("Show Earlier by 0.1 s", () => _player.NudgeSubtitleDelay(-1), KeyCmd.SubtitleEarlier),
            Item("Show Later by 0.1 s", () => _player.NudgeSubtitleDelay(1), KeyCmd.SubtitleLater),
            Item("Show Earlier by 1 s", () => _player.NudgeSubtitleDelay(-10)),
            Item("Show Later by 1 s", () => _player.NudgeSubtitleDelay(10)),
            Item("No Delay", () => _player.SetSubtitleDelay(TimeSpan.Zero), check: _player.SubtitleDelay == TimeSpan.Zero),
        },
    };

    private List<string> SubtitleFilesNextToMedia()
    {
        try
        {
            if (_player.CurrentMedia?.Source is not { } source || source.Contains("://") || System.IO.Path.GetDirectoryName(source) is not { Length: > 0 } folder)
                return [];
            var name = System.IO.Path.GetFileNameWithoutExtension(source);
            return Directory.EnumerateFiles(folder, name + "*")
                .Where(f => Pardeh.Core.Playback.Subtitles.Extensions.Contains(System.IO.Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private async void LoadSubtitleFile()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a subtitle file",
            FileTypeFilter = [new FilePickerFileType("Subtitles") { Patterns = Pardeh.Core.Playback.Subtitles.Extensions.Select(e => "*" + e).ToList() }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path && !_player.LoadSubtitles(path))
            ShowMessage("No subtitles could be read from that file.");
    }

    /// <summary>Play › Audio and Language Tracks: the file's languages, separate dub files, and loading one.</summary>
    private MenuItem AudioTracksMenu()
    {
        var items = new List<object>();
        var tracks = _player.AudioTracks;
        for (var i = 0; i < tracks.Count; i++)
        {
            var index = i;
            items.Add(Item(tracks[i].Label, () => _player.SelectAudioTrack(index), check: i == _player.CurrentAudioTrack));
        }

        if (items.Count == 0)
            items.Add(new MenuItem { Header = "(no other audio tracks)", IsEnabled = false });
        items.Add(new Separator());
        items.Add(Item("Load Audio File...", LoadAudioFile));
        items.Add(Item("Next Audio Track", () => CycleAudioTrack(), KeyCmd.NextAudioTrack));
        return new MenuItem { Header = "Audio and Language Tracks", ItemsSource = items };
    }

    internal void CycleAudioTrack()
    {
        var count = _player.AudioTracks.Count;
        if (count > 1)
            _player.SelectAudioTrack((_player.CurrentAudioTrack + 1) % count);
    }

    /// <summary>A dub delivered as its own file (.mka, .mp3, .ac3 …): plays with the current video.</summary>
    private async void LoadAudioFile()
    {
        if (_player.CurrentMedia is null)
        {
            ShowMessage("Open the movie first, then load its audio file.");
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose an audio file for this video",
            FileTypeFilter = [new FilePickerFileType("Audio") { Patterns = ["*.mka", "*.mp3", "*.aac", "*.ac3", "*.eac3", "*.dts", "*.m4a", "*.flac", "*.ogg", "*.opus", "*.wav", "*.wma"] }],
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path && !_player.AddAudioFile(path))
            ShowMessage("That file has no audio Pardeh can play.");
    }
    /// <summary>Play › Play Speed: 0.25× – 4×, pitch kept; Ctrl+Shift+G / S / N step faster, slower, normal.</summary>
    /// <summary>View › Skin Size: every skin (and full mode) drawn larger or smaller, from 75 % to 400 %.</summary>
    private MenuItem SkinSizeMenu()
    {
        var items = new List<object>
        {
            Item("Larger", () => StepSkinZoom(1), KeyCmd.ZoomIn),
            Item("Smaller", () => StepSkinZoom(-1), KeyCmd.ZoomOut),
            Item("Actual size", () => StepSkinZoom(0), KeyCmd.ZoomReset),
            new Separator(),
        };
        foreach (var zoom in AppSettings.SkinZooms)
        {
            var value = zoom;
            items.Add(Item($"{value * 100:0}%", () => { AppSettings.Current.SkinZoom = value; AppSettings.Current.Save(); }, check: Math.Abs(AppSettings.Current.SkinZoom - value) < 1e-6));
        }

        return new MenuItem { Header = $"Skin Size ({AppSettings.Current.SkinZoom * 100:0}%)", ItemsSource = items };
    }

    /// <summary>View › Aspect Ratio: the picture's own shape, a fixed ratio, stretched or cropped to fill the pane (key A cycles).</summary>
    private MenuItem AspectRatioMenu()
    {
        var items = new List<object>();
        foreach (var (key, name, _) in VideoSettings.Aspects)
        {
            var chosen = key;
            items.Add(Item(name, () => SetAspect(chosen), check: _player.Video.Aspect == key));
        }

        items.Add(new Separator());
        items.Add(Item("Next Aspect Ratio", () => SetAspect(null), KeyCmd.AspectRatio));
        return new MenuItem { Header = "Aspect Ratio", ItemsSource = items };
    }

    /// <summary>Sets the picture's shape (null: the next one), remembers it and says which.</summary>
    internal bool HasVideoOpen => _player.HasVideo;

    internal void SetAspect(string? key)
    {
        if (key is null)
            _player.Video.StepAspect(1);
        else
            _player.Video.Aspect = key;
        AppSettings.Current.VideoAspect = _player.Video.Aspect;
        AppSettings.Current.Save();
    }

    /// <summary>Play › Sleep Timer, A-B Repeat and Volume Levelling.</summary>
    internal MenuItem PlaybackToolsMenu()
    {
        var items = new List<object>();
        var remaining = _player.SleepRemaining;
        var sleepHeader = _player.SleepAtEndOfTrack ? "Sleep Timer (end of track)" : remaining is { } left ? $"Sleep Timer ({Math.Ceiling(left.TotalMinutes):0} min left)" : "Sleep Timer";
        var sleep = new List<object>();
        foreach (var minutes in Pardeh.Core.Playback.MediaPlayer.SleepPresetMinutes)
        {
            var m = minutes;
            sleep.Add(Item($"{m} minutes", () => _player.StartSleepTimer(TimeSpan.FromMinutes(m))));
        }

        sleep.Add(Item("End of this track", () => _player.StartSleepAtEndOfTrack(), check: _player.SleepAtEndOfTrack));
        sleep.Add(new Separator());
        sleep.Add(Item("Off", () => _player.CancelSleepTimer(), check: !_player.SleepTimerActive));
        items.Add(new MenuItem { Header = sleepHeader, ItemsSource = sleep });

        var ab = _player.SectionLoopActive ? "A-B Repeat: clear" : _player.LoopStart is not null ? "A-B Repeat: set B here" : "A-B Repeat: set A here";
        items.Add(Item(ab, () => _player.CycleSectionLoop(), KeyCmd.AbRepeat, check: _player.SectionLoopActive));

        var level = new List<object>();
        foreach (var (mode, name) in new[] { (Pardeh.Core.Playback.ReplayGainMode.Off, "Off"), (Pardeh.Core.Playback.ReplayGainMode.Track, "Even out each track"), (Pardeh.Core.Playback.ReplayGainMode.Album, "Even out albums (needs album tags)") })
        {
            var chosen = mode;
            level.Add(Item(name, () =>
            {
                _player.ReplayGain = chosen;
                AppSettings.Current.ReplayGain = chosen.ToString();
                AppSettings.Current.Save();
            }, check: _player.ReplayGain == chosen));
        }

        items.Add(new MenuItem { Header = "Volume Levelling", ItemsSource = level });
        items.Add(Item("Gapless Playback", () =>
        {
            _player.Gapless = !_player.Gapless;
            AppSettings.Current.Gapless = _player.Gapless;
            AppSettings.Current.Save();
        }, check: _player.Gapless));
        items.Add(new MenuItem { Header = "Markers", ItemsSource = MarkerMenuItems() });
        items.Add(Item("Resume Videos Where Left", () =>
        {
            _player.ResumeVideos = !_player.ResumeVideos;
            AppSettings.Current.ResumeVideos = _player.ResumeVideos;
            AppSettings.Current.Save();
        }, check: _player.ResumeVideos));
        return new MenuItem { Header = "Playback Tools", ItemsSource = items };
    }

    // ---- Markers ("chorus 1:12") ---------------------------------------------------------------

    /// <summary>"Add marker..." and one submenu per marker of the playing track.</summary>
    private List<object> MarkerMenuItems()
    {
        var items = new List<object> { Item("Add Marker Here", () => _player.AddMarker(), KeyCmd.MarkerAdd), Item("Add Named Marker...", () => _ = NameMarker(null)) };
        var markers = _player.CurrentMarkers;
        if (markers.Count > 0)
            items.Add(new Separator());
        for (var i = 0; i < markers.Count; i++)
        {
            var index = i;
            var label = $"{Pardeh.Core.Playback.MediaPlayer.FormatTime(markers[i].Seconds)}  {Pardeh.Core.Playback.MediaPlayer.MarkerLabel(markers[i], i)}";
            items.Add(new MenuItem
            {
                Header = label.Replace("_", "__"), // an underscore in a name is not a menu access key
                ItemsSource = new object[]
                {
                    Item("Jump Here", () => _player.JumpToMarker(index)),
                    Item("Repeat This Section", () => _player.LoopMarkerSection(index)),
                    Item("Set as A", () => _player.SetSectionPointAtMarker(index, end: false)),
                    Item("Set as B", () => _player.SetSectionPointAtMarker(index, end: true)),
                    new Separator(),
                    Item("Rename...", () => _ = NameMarker(index)),
                    Item("Delete", () => _player.RemoveMarker(index)),
                },
            });
        }

        return items;
    }

    /// <summary>Shift+M: the list of markers (the Playback tools sheet on the phone looks, a menu elsewhere).</summary>
    internal void ShowMarkers()
    {
        if (PhoneSkin.IsPhoneKey(CurrentFullModeKey()) && IsFullMode)
        {
            ShowPhoneTools(markersFirst: true);
            return;
        }

        OpenTracked(new ContextMenu { ItemsSource = MarkerMenuItems() }, _canvas);
    }

    /// <summary>Names a new marker at the current position (index null) or renames one. True when the tools sheet should come back.</summary>
    private async Task NameMarker(int? index)
    {
        if (_player.CurrentMedia is not { } media)
            return;
        var at = _player.CurrentPosition; // where it was when asked, not when the name is typed
        var markers = _player.CurrentMarkers;
        var initial = index is { } i && i < markers.Count ? markers[i].Name : string.Empty;
        var question = index is null ? $"Name the marker at {Pardeh.Core.Playback.MediaPlayer.FormatTime(at)}" : "Marker name";
        var name = Shell is { } shell
            ? await Rendering.PromptDialog.Ask(shell, question, initial)
            : await Rendering.InViewPrompt.Ask(_overlay, _session?.CurrentView?.Root.Width ?? 360, question, initial);
        if (name is null || !ReferenceEquals(media, _player.CurrentMedia))
            return; // cancelled, or the song changed meanwhile
        if (index is { } existing)
            _player.RenameMarker(existing, name);
        else
            _player.AddMarker(name, at);
    }

    private MenuItem PlaySpeedMenu()
    {
        var items = new List<object>
        {
            Item("Faster", () => _player.StepRate(1), KeyCmd.Faster),
            Item("Normal", () => _player.Rate = 1, KeyCmd.NormalSpeed),
            Item("Slower", () => _player.StepRate(-1), KeyCmd.Slower),
            new Separator(),
        };
        foreach (var rate in Pardeh.Core.Playback.MediaPlayer.RatePresets)
        {
            var label = rate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "\u00D7";
            items.Add(Item(label, () => _player.Rate = rate, check: Math.Abs(_player.Rate - rate) < 1e-6));
        }

        var current = _player.Rate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        return new MenuItem { Header = $"Play Speed ({current}\u00D7)", ItemsSource = items };
    }

    /// <summary>The Now Playing options menu (the pane's "Select Now Playing options" button).</summary>
    private object[] NowPlayingOptions()
    {
        if (_session is not { } session)
            return [];
        var center = session.MediaCenter;
        bool On(string name) => SkinValue.ToBool(center.GetMember(name));
        object Toggle(string header, string name) => Item(header, () => center.SetMember(name, !On(name)), check: On(name));

        return
        [
            Toggle("Show Enhancements", "showSettings"),
            Toggle("Show Title", "showTitles"),
            Toggle("Show Media Information", "showMetadata"),
            Toggle("Show Playlist", "showPlaylist"),
            new Separator(),
            EnhancementsMenu(),
            new Separator(),
            PlaySpeedMenu(),
            PlaybackToolsMenu(),
            AudioTracksMenu(),
            AspectRatioMenu(),
            SubtitlesMenu(),
            Toggle("Maximize the Video and Visualization Pane", "quickHide"),
            Item("Full Screen", () => center.SetMember("fullScreenRequest", true), KeyCmd.FullScreen),
        ];
    }

    /// <summary>View › Enhancements: the original Now Playing panes (full mode with imported resources).</summary>
    private MenuItem EnhancementsMenu()
    {
        var available = IsFullMode && _session?.Package.SourcePath == OriginalFullModeSource;
        var shown = available && SkinValue.ToBool(_session!.MediaCenter.GetMember("showSettings"));
        object Enhancement(string header, string view) => Item(header, () =>
        {
            if (!IsFullMode)
                LoadFullMode();
            _session?.ShowEnhancement(view);
        });

        return new MenuItem
        {
            Header = "Enhancements",
            IsEnabled = available,
            ItemsSource = new object[]
            {
                Item("Show Enhancements", () => _session?.MediaCenter.SetMember("showSettings", !shown), check: shown),
                new Separator(),
                Enhancement("Color Chooser", "AppColorView"),
                Enhancement("Crossfading and Auto Volume Leveling", "CrossFadeView"),
                Enhancement("Graphic Equalizer", "EQView"),
                Enhancement("Quiet Mode", "AdvancedAudioView"),
                Enhancement("SRS WOW Effects", "SRSView"),
                Enhancement("Video Settings", "VideoSettingsView"),
            },
        };
    }

    /// <summary>Applies Tools › Options that act on the window.</summary>
    private void ApplySettings()
    {
        ApplyNetworkSettings();
        Topmost = AppSettings.Current.AlwaysOnTop;
        if (Math.Abs(_appliedZoom - AppSettings.Current.SkinZoom) > 1e-6)
        {
            var artBefore = LunaArt.DefaultScale;
            ApplyScale();
            if (artBefore != LunaArt.DefaultScale && _loadedLook is not null)
            {
                _fullMode = null; // the built-in looks are drawn again at the new density
                _skinPreview = null;
                if (IsFullMode)
                    LoadFullMode();
                else
                    _loadedLook = null;
            }
        }
        PlatformServices.Current.ApplyAppBranding(AppSettings.Current.ClassicBranding);

        // The Pardeh Glass skin colour changed (Settings > Appearance > Skin color): the look is built again in the new colour
        // (the phone views, popups and Skin Chooser pictures that carry the old one are dropped with it).
        var glassRebuild = false;
        if (Rendering.GlassPalette.Sync(AppSettings.Current))
        {
            _skinPreview = null;
            if (_fullMode is not null && PhoneSkin.KeyOf(_fullMode.SourcePath) == "glass-phone")
            {
                glassRebuild = true;
                if (_quickPanel is not null)
                {
                    _overlay.Children.Remove(_quickPanel);
                    _quickPanel = null;
                }

                CloseVolumePopup();
                if (_volumePopup is not null)
                {
                    _overlay.Children.Remove(_volumePopup);
                    _volumePopup = null;
                }

                if (_vizPicker is not null)
                {
                    _overlay.Children.Remove(_vizPicker); // its colours follow the skin colour: built again when next opened
                    _vizPicker = null;
                }
            }
        }

        // Full mode look changed (Options): rebuild, and show it now if full mode is up.
        if (_loadedLook is not null && (glassRebuild || _loadedLook != AppSettings.Current.FullModeLook || _loadedBranding != AppSettings.Current.ClassicBranding || _loadedLogoFollow != AppSettings.Current.LogoFollowsSkinColor))
        {
            _fullMode = null;
            if (IsFullMode)
                LoadFullMode();
            else
                _loadedLook = null;
        }

        // Pages: the full mode re-flows its task rail.
        if (IsFullMode)
            _session?.CurrentView?.Script.Execute("if (typeof ApplyPages == 'function') ApplyPages();", "pages");

        // Appearance: fonts and menus app-wide, the frame's caption / menu row (whose size may change).
        Rendering.UiTheme.Apply(((Rendering.ISettingsHost)this).DarkSurface);
        _frame.Refresh();
        UpdateCorners();
        if (_framed && _session?.CurrentView is { } view)
            SizeToView(view.Root.Width, view.Root.Height);
        _canvas.InvalidateVisual();
    }

    private object[] EqualizerItems()
    {
        var eq = _player.Equalizer;
        var presets = new MenuItem { Header = "Equalizer Presets" };
        presets.ItemsSource = Pardeh.Core.Playback.EqualizerSettings.Presets
            .Select((preset, index) => (object)Item(preset.Name, () => { eq.CurrentPreset = index; eq.Bypass = false; }, check: !eq.Bypass && eq.CurrentPreset == index))
            .ToList();
        return
        [
            Item("Equalizer On", () => eq.Bypass = !eq.Bypass, check: !eq.Bypass),
            presets,
            new Separator(),
            Item("Search Library Again", () => _ = _library.ScanAsync()),
            new Separator(),
            Item("Always on Top", () => { AppSettings.Current.AlwaysOnTop = !AppSettings.Current.AlwaysOnTop; AppSettings.Current.Save(); ApplySettings(); }, KeyCmd.AlwaysOnTop, check: AppSettings.Current.AlwaysOnTop),
            Item("Options...", () => _ = ShowOptions(), KeyCmd.Preferences),
            .. OperatingSystem.IsAndroid() ? Array.Empty<object>() : [Item("Keyboard Shortcuts...", () => ShowSettingsPage("Keyboard"))],
        ];
    }

    /// <summary>Library › Playlists › Import: copies a .m3u / .pls / .wpl / .asx playlist into My Playlists.</summary>
    private async void ImportPlaylist()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import a playlist",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Playlists") { Patterns = Pardeh.Core.Library.PlaylistFormats.Extensions.Select(e => "*" + e).ToList() }],
        });
        foreach (var path in files.Select(f => f.TryGetLocalPath()).OfType<string>())
        {
            var entries = Pardeh.Core.Library.PlaylistFormats.Read(path);
            if (entries.Count > 0)
                _playlists.Save(Path.GetFileNameWithoutExtension(path), entries);
        }

        _session?.OnLibraryChanged();
    }

    private async void AddFilesToLibrary()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Add files to the library", AllowMultiple = true });
        var paths = files.Select(f => Rendering.PickedPaths.Of(f)).OfType<string>().ToList();
        if (paths.Count > 0)
            _library.AddFiles(paths);
    }

    private async void AddFolderToLibrary()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Add a folder to the library" });
        var path = Rendering.PickedPaths.Of(folders.FirstOrDefault());
        if (path is null)
            return;
        _library.AddFolder(path);
    }



    /// <summary>Files handed over by a later launch (double-clicked in Explorer while Pardeh runs).</summary>
    private void OpenArguments(string[] args)
    {
        var options = StartupOptions.Parse(args);
        if (options.SkinExplicit && options.SkinPath is { } skin)
            LoadSkin(skin);
        if (options.MediaFiles.Count > 0)
            OpenGiven(options.MediaFiles);
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }
    /// <summary>File › Import from Windows XP…: brings in the original full mode from the user's own copy of XP.</summary>
    private object[] ImportMenu() => Platform.WmpImport is null ? [] : [Item("Import from Windows XP...", ImportFromWindowsXp), new Separator()];

    private async void ImportFromWindowsXp()
    {
        if (Platform.WmpImport is null)
            return;
        if (!Platform.Confirm(Top,
                "Pardeh can show Windows Media Player 9's original full mode, using the files from your own copy of Windows XP " +
                "(an installation ISO, the CD's I386 folder, WMPLOC.DL_ or wmploc.dll).\n\n" +
                "The files stay on this computer, in " + WmpImport.TargetDirectory + ".\n\nChoose the file now?", "Import from Windows XP"))
            return;

        var result = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a Windows XP ISO, WMPLOC.DL_ or wmploc.dll",
            FileTypeFilter = [new FilePickerFileType("Windows XP files") { Patterns = ["*.iso", "*.dl_", "wmploc.dll"] }],
        });
        if (result.FirstOrDefault()?.TryGetLocalPath() is not { } source)
            return;

        try
        {
            var summary = await Task.Run(() => WmpImport.Import(source, message => Log("Import: " + message)));
            Log("Imported Windows XP resources: " + summary);
            _wmpResources = Pardeh.Skinning.Wmp.WmpResources.Find();
            LoadFullMode();
            ShowMessage("Done. Full mode now uses the original Windows Media Player 9 look.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            ShowMessage("Import failed: " + ex.Message);
        }
    }

    /// <summary>Copy to CD or Device: copies files to a removable drive or a folder (empty destination = ask).</summary>
    private async void CopyToDevice(List<string> files, string destination)
    {
        if (files.Count == 0)
        {
            ShowMessage("There is nothing to copy. Add items to Now Playing first.");
            return;
        }

        if (destination.Length == 0)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Copy to a device or folder" });
            if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } picked)
                return;
            destination = picked;
        }

        try
        {
            var (copied, skipped) = await Task.Run(() =>
            {
                int done = 0, existing = 0;
                foreach (var file in files.Where(File.Exists))
                {
                    var target = Path.Combine(destination, Path.GetFileName(file));
                    if (File.Exists(target))
                    {
                        existing++;
                        continue;
                    }

                    File.Copy(file, target);
                    done++;
                }

                return (done, existing);
            });
            ShowMessage($"Copied {copied} item(s) to {destination}." + (skipped > 0 ? $" {skipped} were already there." : string.Empty));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage("Copying stopped: " + ex.Message);
        }
    }
    private void ShowAbout()
    {
        if (Shell is { } window)
            _ = Rendering.AboutDialog.Show(window);
        else
            Platform.Inform(Top, $"{AppInfo.Details()}\n\n{AppInfo.SourcePage}", "About Pardeh");
    }

    public string? ShowOpenFileDialog(string dialogType, string filter) => Platform.OpenMediaFile(Top);

    public void LaunchUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            Log($"Blocked URL from skin: {url}");
            return;
        }

        if (Platform.Confirm(Top, $"The skin wants to open this web page:\n\n{uri}\n\nOpen it?", "Pardeh"))
            Platform.OpenUrl(uri.ToString());
    }

    public void ShowPopup(SkinView view, IReadOnlyList<string> items, int selectedIndex, Action<int> onSelected)
    {
        var chosen = false;
        var menuItems = items.Select((text, index) =>
        {
            var item = new MenuItem
            {
                Header = text,
                Icon = index == selectedIndex ? new TextBlock { Text = "●", FontSize = 8 } : null,
            };
            item.Click += (_, _) => { chosen = true; onSelected(index); };
            return item;
        }).ToList();

        var menu = new ContextMenu { ItemsSource = menuItems };
        menu.Closed += (_, _) => { if (!chosen) onSelected(-1); };
        OpenTracked(menu, _canvas);
    }

    public int ShowPopupModal(SkinView view, IReadOnlyList<string> items)
    {
        // A nested message loop until the menu closes: scripts expect show() to return the choice.
        var result = -1;
        using var done = new CancellationTokenSource();
        ShowPopup(view, items, -1, index =>
        {
            result = index;
            done.Cancel();
        });
        Dispatcher.UIThread.MainLoop(done.Token);
        return result;
    }

    public double MeasureText(string text, string fontFamily, double sizePoints, bool bold)
    {
        if (text.Length == 0)
            return 0;
        var family = fontFamily.Equals("Ariel", StringComparison.OrdinalIgnoreCase) ? "Arial" : fontFamily;
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Parse(family + ", Tahoma, Arial"), FontStyle.Normal, bold ? FontWeight.Bold : FontWeight.Normal),
            sizePoints * 96 / 72, Brushes.Black);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    public void ShowMessage(string text) => Platform.Inform(Top, text, "Pardeh");

    public void Log(string message)
    {
        Debug.WriteLine(message);
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.AppendAllText(AppPaths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }
}
