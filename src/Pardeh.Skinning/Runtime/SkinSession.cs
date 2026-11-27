using Pardeh.Core.Playback;
using Pardeh.Skinning.Imaging;
using Pardeh.Skinning.Model;
using Pardeh.Skinning.Runtime.Scripting;

namespace Pardeh.Skinning.Runtime;

/// <summary>
/// A skin applied to the player: owns the current view, the shared script objects and the image cache,
/// and forwards player events to the view's scripts.
/// </summary>
public sealed class SkinSession : IDisposable
{
    private string? _pendingViewId;
    private bool _closeRequested;
    private readonly IImageDecoder _decoder;

    /// <summary>Decodes images that aren't part of the skin (album art, skin previews).</summary>
    internal IImageDecoder Decoder => _decoder;

    public SkinSession(SkinPackage package, MediaPlayer player, ISkinHost host, IImageDecoder decoder, IPreferenceStore preferences)
    {
        Package = package;
        Player = player;
        Host = host;
        Preferences = preferences;
        _decoder = decoder;
        Images = new SkinImageCache(package, decoder, Log, () => Resources);
        PlayerObject = new PlayerObject(this);
        ThemeObject = new ThemeObject(this);
        MediaCenter = new MediaCenterObject();
        RestoreMediaCenter();
        Pardeh = new PardehObject(this);
        App = new AppObject(this);
        Helper = new HelperObject(this);

        Player.PlayStateChanged += OnPlayStateChanged;
        Player.OpenStateChanged += OnOpenStateChanged;
        Player.StatusChanged += OnStatusChanged;
        Player.ModeChanged += OnModeChanged;
        Player.CurrentMediaChanged += OnMediaChanged;
    }

    /// <summary>
    /// The player-wide UI state the classic player kept between runs (colour chooser hue / saturation, which
    /// Now Playing panels show). Stored under the "mediacenter" preference id, shared by every skin.
    /// </summary>
    private static readonly string[] PersistedMediaCenter =
    [
        "backHueShift", "backSaturation", "buttonHueShift", "buttonSaturation",
        "showPlaylist", "showMetadata", "showSettings", "showCaptions", "showTitles",
        "settingsView", "settingsPreset", "metadataView", "metadataPreset", "playlistWidth", "effectType", "effectPreset",
    ];

    private const string MediaCenterPreferences = "mediacenter";

    private void RestoreMediaCenter()
    {
        // Quiet Mode lives on the player (it outlasts skins); mediacenter mirrors it.
        MediaCenter.SetMember("dynamicRangeControl", (double)Player.Equalizer.QuietMode);

        foreach (var name in PersistedMediaCenter)
        {
            if (Preferences.Load(MediaCenterPreferences, name) is { } stored)
                MediaCenter.SetMember(name, SkinValue.ParseLiteral(stored));
        }

        MediaCenter.MemberChanged += name =>
        {
            if (name.Equals("dynamicRangeControl", StringComparison.OrdinalIgnoreCase))
                Player.Equalizer.QuietMode = (int)SkinValue.ToDouble(MediaCenter.GetMember(name));

            // The Now Playing arrows: 1 / 2 previous / next preset, 3 / 4 (with Shift) previous / next family. One-shot.
            if (name.Equals("vizRequest", StringComparison.OrdinalIgnoreCase) && (int)SkinValue.ToDouble(MediaCenter.GetMember(name)) is var request and >= 1 and <= 4)
            {
                var current = CurrentVisualization;
                ShowVisualization(request switch
                {
                    1 => current - 1,
                    2 => current + 1,
                    3 => VisualizationCatalog.StepType(current, -1),
                    _ => VisualizationCatalog.StepType(current, 1),
                });
                MediaCenter.SetMember(name, 0.0);
                return;
            }


            // "View full screen": the host shows the video / visualization full screen; the request is one-shot.
            if (name.Equals("fullScreenRequest", StringComparison.OrdinalIgnoreCase) && SkinValue.ToBool(MediaCenter.GetMember(name)))
            {
                MediaCenter.SetMember(name, false);
                Host.RunCommand("fullScreen", []);
                return;
            }

            // Panels hidden by quickHide come back on their own; keep the saved choice.
            if (PersistedMediaCenter.Contains(name, StringComparer.OrdinalIgnoreCase) && !MediaCenter.QuickHidden)
                Preferences.Save(MediaCenterPreferences, name, SkinValue.ToText(MediaCenter.GetMember(name)));
        };
    }

    /// <summary>The full mode's visualization (index into <see cref="VisualizationCatalog.All"/>).</summary>
    public int CurrentVisualization =>
        VisualizationCatalog.IndexOf(SkinValue.ToText(MediaCenter.GetMember("effectType")), (int)SkinValue.ToDouble(MediaCenter.GetMember("effectPreset")));

    public void ShowVisualization(int index)
    {
        index = VisualizationCatalog.Wrap(index);
        MediaCenter.SetMember("effectType", VisualizationCatalog.All[index].Type);
        MediaCenter.SetMember("effectPreset", (double)VisualizationCatalog.PresetOf(index));
    }

    /// <summary>Opens an enhancement pane in Now Playing (AppColorView = the colour chooser, EQView …).</summary>
    public void ShowEnhancement(string view)
    {
        App.SetMember("currentTask", "NowPlaying");
        MediaCenter.SetMember("settingsView", view);
        MediaCenter.SetMember("showSettings", true);
    }

    public SkinPackage Package { get; }

    public MediaPlayer Player { get; }

    public ISkinHost Host { get; }

    public IPreferenceStore Preferences { get; }

    public SkinImageCache Images { get; }

    public PlayerObject PlayerObject { get; }

    public ThemeObject ThemeObject { get; }

    public MediaCenterObject MediaCenter { get; }

    public PardehObject Pardeh { get; }

    public AppObject App { get; }

    public HelperObject Helper { get; }

    /// <summary>The classic player's own resources, if the user imported them (scripts, bitmaps, strings).</summary>
    public Pardeh.Skinning.Wmp.WmpResources? Resources { get; set; }

    /// <summary>The media library (shown by LIBRARY elements), if the host has one.</summary>
    public Pardeh.Core.Library.MediaLibrary? Library { get; set; }

    /// <summary>"My Playlists" (saved .m3u8 playlists), when the host provides them.</summary>
    public Pardeh.Core.Library.PlaylistStore? Playlists { get; set; }

    /// <summary>True for a library song with something to clean up: lists mark it. Null: no marks.</summary>
    public Func<string, bool>? NeedsCleanup { get; set; }

    /// <summary>True for a file whose details the host can fix (not a stream). Null: no "Fix Info" entries.</summary>
    public Func<string, bool>? CanFixInfo { get; set; }

    /// <summary>The lyrics of the playing track for LYRICS elements, when the host has them (the phone looks).</summary>
    public ILyricsFeed? Lyrics { get; set; }

    /// <summary>Call after the library changed (from the UI thread).</summary>
    public void OnLibraryChanged()
    {
        foreach (var element in CurrentView?.Elements.OfType<Elements.LibraryElement>() ?? [])
            element.Invalidate();
    }

    public SkinView? CurrentView { get; private set; }

    /// <summary>Opens the first view (or the one with the given id).</summary>
    public SkinView Start(string? viewId = null)
    {
        OpenView(viewId);
        return CurrentView!;
    }

    /// <summary>True while a modal host operation (dialog) runs on behalf of a script.</summary>
    public bool IsModal { get; private set; }

    /// <summary>
    /// Runs a modal host operation for a script: pauses the script time limit and the skin's ticks,
    /// so nothing re-enters the script engine while the user is in a dialog.
    /// </summary>
    public T RunModal<T>(Func<T> operation)
    {
        var wasModal = IsModal;
        IsModal = true;
        using var suspended = CurrentView?.Script.SuspendLimits();
        try
        {
            return operation();
        }
        finally
        {
            IsModal = wasModal;
        }
    }


    private MediaItem? _artItem;
    private SkinBitmap? _artSource;
    private (int W, int H, bool Fill, SkinBitmap? Bitmap, double Radius) _artFitted;
    private bool _artIsReal;

    /// <summary>
    /// The current item's real cover, cropped square and rounded to a disc of <paramref name="size"/> skin units (<paramref name="density"/> pixels
    /// per unit), or null when it has none (the stock note / film pictures do not count). The centre of the Glass circular visualization.
    /// Shares the one-picture cache of <see cref="PlayerImage"/>, so ask for it only while no other cover picture is drawn.
    /// </summary>
    public SkinBitmap? CoverDisc(double size, int density = 1)
    {
        var disc = PlayerImage("WMPImage_AlbumArtLarge", size, size, fill: true, radius: size / 2, density);
        return _artIsReal ? disc : null;
    }

    /// <summary>
    /// Images the player itself supplies ("WMPImage_AlbumArtLarge" / "…Small"): the current item's cover, else the
    /// classic default art (note for audio, film for video), shrunk to fit and centred in a <paramref name="width"/> ×
    /// <paramref name="height"/> box.
    /// </summary>
    internal SkinBitmap? PlayerImage(string name, double width, double height, bool fill = false, double radius = 0, int density = 1)
    {
        if (!name.StartsWith("WMPImage_AlbumArt", StringComparison.OrdinalIgnoreCase) || Player.CurrentMedia is not { } item)
            return null;

        if (!ReferenceEquals(item, _artItem))
        {
            _artItem = item;
            _artFitted = default;
            _artSource = null;
            _artIsReal = false;
            if (Player.GetAlbumArt(item) is { } bytes)
            {
                try
                {
                    _artSource = _decoder.Decode(bytes);
                    _artIsReal = _artSource is not null;
                }
                catch (Exception ex)
                {
                    Log($"Cannot decode album art: {ex.Message}");
                }
            }

            _artSource ??= Images.Get(item.VideoWidth > 0 || Player.HasVideo ? "res://wmploc/#528" : "res://wmploc/#526", null);
        }

        if (_artSource is not { } source)
            return null;

        // coverScale (opt-in): a real cover is fitted at that many pixels per skin unit, so it is sharp on a dense screen.
        // The stock pictures are fixed-size art and keep density 1.
        density = _artIsReal ? Math.Clamp(density, 1, 8) : 1;
        int w = Math.Max(1, (int)width) * density, h = Math.Max(1, (int)height) * density;
        fill &= _artIsReal; // the stock note / film pictures keep their size
        radius = fill ? radius * density : 0; // coverRadius (opt-in) rounds the corners of a coverFill picture
        if (_artFitted.Bitmap is not null && _artFitted.W == w && _artFitted.H == h && _artFitted.Fill == fill && _artFitted.Radius == radius)
            return _artFitted.Bitmap;

        if (fill)
        {
            // coverFill: scaled up or down to cover the whole box, the overflow of the longer side cropped evenly.
            var cover = Math.Max((double)w / source.Width, (double)h / source.Height);
            var big = source.Resized(Math.Max(w, (int)Math.Round(source.Width * cover)), Math.Max(h, (int)Math.Round(source.Height * cover)));
            var pixels = new byte[w * h * 4];
            int cx = (big.Width - w) / 2, cy = (big.Height - h) / 2;
            for (var y = 0; y < h; y++)
                Buffer.BlockCopy(big.Pixels, (cy + y) * big.Stride + cx * 4, pixels, y * w * 4, w * 4);
            if (radius > 0)
                RoundCorners(pixels, w, h, radius);
            _artFitted = (w, h, true, new SkinBitmap(w, h, pixels, density), radius);
            return _artFitted.Bitmap;
        }

        var scale = Math.Min(1, Math.Min((double)w / source.Width, (double)h / source.Height));
        var art = source.Resized((int)Math.Round(source.Width * scale), (int)Math.Round(source.Height * scale));
        var canvas = new byte[w * h * 4];
        int ox = (w - art.Width) / 2, oy = (h - art.Height) / 2;
        for (var y = 0; y < art.Height; y++)
            Buffer.BlockCopy(art.Pixels, y * art.Stride, canvas, ((oy + y) * w + ox) * 4, art.Stride);
        _artFitted = (w, h, false, new SkinBitmap(w, h, canvas, density), 0);
        return _artFitted.Bitmap;
    }

    /// <summary>Makes the corners of a premultiplied-or-straight BGRA picture transparent with a one pixel soft edge.</summary>
    private static void RoundCorners(byte[] pixels, int w, int h, double radius)
    {
        radius = Math.Min(radius, Math.Min(w, h) / 2.0);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                double dx = Math.Max(0, Math.Max(radius - (x + 0.5), x + 0.5 - (w - radius)));
                double dy = Math.Max(0, Math.Max(radius - (y + 0.5), y + 0.5 - (h - radius)));
                if (dx == 0 || dy == 0)
                    continue;
                var coverage = Math.Clamp(radius - Math.Sqrt(dx * dx + dy * dy) + 0.5, 0, 1);
                if (coverage >= 1)
                    continue;
                var i = (y * w + x) * 4;
                for (var c = 0; c < 4; c++)
                    pixels[i + c] = (byte)(pixels[i + c] * coverage);
            }
        }
    }
    private readonly Dictionary<string, SkinNode?> _paneCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The pane a TASKCENTER shows for a task: the player's own compiled pane when imported (Now Playing),
    /// Pardeh's library for Media Library, otherwise a short "not available" pane.
    /// </summary>
    internal SkinView? CreatePaneView(string task, Elements.HostAreaElement host)
    {
        var node = task.ToLowerInvariant() switch
        {
            "nowplaying" => CompiledPane("NOWPLAYING.WSZ") ?? MessagePane("Now Playing"),
            "medialibrary" => SyntheticPane("""
                <THEME><VIEW id="vLibrary" backgroundColor="#1F2A5C">
                  <LIBRARY id="library" left="0" top="0" width="wmpprop:view.width" height="wmpprop:view.height"/>
                </VIEW></THEME>
                """),
            "mediaguide" => MessagePane("The Media Guide service closed years ago."),
            "services" or "subscriptions" or "premiumservices" => MessagePane("Premium Services are no longer offered."),
            "cdaudio" => MessagePane("Copy from CD is coming in a later version of Pardeh."),
            "radiotuner" => ListPane("radio"),
            "portabledevice" => ListPane("device"),
            "skinviewer" or "skinchooser" => SettingsPane(),
            _ => MessagePane(task),
        };

        return node is null ? null : Embed(node, host);
    }

    /// <summary>The player's plug-in panes (PLUGINHOST objectID → compiled pane in the imported resources).</summary>
    private static readonly Dictionary<string, string> PluginPanes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AlbumArtView"] = "ALBUMART.WSZ",
        ["MediaInfoView"] = "MEDIAINFO.WSZ",
        ["AppColorView"] = "APPCOLORCHOOSER.WSZ",
        ["EQView"] = "EQUALIZER.WSZ",
        ["CrossFadeView"] = "CROSSFADE.WSZ",
        ["MediaLinkView"] = "MEDIALINK.WSZ",
        ["VideoSettingsView"] = "VSETTINGS.WSZ",
        ["AdvancedAudioView"] = "ADVANCEDAUDIO.WSZ",
        ["SRSView"] = "SRSWOW.WSZ",
        ["CaptionsView"] = "CAPTIONS.WSZ",
    };

    /// <summary>True when a PLUGINHOST can show <paramref name="objectId"/> as an embedded pane.</summary>
    internal bool HasPluginPane(string objectId) =>
        PluginPanes.TryGetValue(objectId, out var resource) && Resources?.GetText("res://wmploc/RT_TEXT/" + resource) is not null;

    internal SkinView? CreatePluginView(string objectId, Elements.HostAreaElement host) =>
        PluginPanes.TryGetValue(objectId, out var resource) && CompiledPane(resource) is { } node ? Embed(node, host) : null;

    private SkinView Embed(SkinNode node, Elements.HostAreaElement host)
    {
        var view = new SkinView(this, node) { EmbeddedIn = host };
        view.Root.SetInitial("width", host.Width);
        view.Root.SetInitial("height", host.Height);
        view.Load();
        return view;
    }

    private SkinNode? CompiledPane(string resource)
    {
        if (_paneCache.TryGetValue(resource, out var cached))
            return cached;
        SkinNode? view = null;
        if (Resources?.GetText("res://wmploc/RT_TEXT/" + resource) is { } compiled)
        {
            try
            {
                view = new Wmp.WszDecoder(compiled, Resources.GetString).Decode().Children.FirstOrDefault(n => n.Tag == "VIEW");
            }
            catch (Exception ex)
            {
                Log($"Cannot decode {resource}: {ex.Message}");
            }
        }

        _paneCache[resource] = view;
        return view;
    }

    private static SkinNode? SyntheticPane(string wms) =>
        LenientXmlParser.Parse(wms).Children.FirstOrDefault(n => n.Tag == "THEME")?.Children.FirstOrDefault(n => n.Tag == "VIEW");


    private static SkinNode? ListPane(string kind) => SyntheticPane($"""
        <THEME><VIEW backgroundColor="#1F2A5C">
          <LISTPANE kind="{kind}" left="0" top="0" width="wmpprop:view.width" height="wmpprop:view.height"/>
        </VIEW></THEME>
        """);
    /// <summary>Settings: the skins (and built-in full modes) above, all settings below.</summary>
    private static SkinNode? SettingsPane() => SyntheticPane("""
        <THEME><VIEW backgroundColor="#1F2A5C">
          <LISTPANE kind="settings" left="0" top="0" width="wmpprop:view.width" height="wmpprop:view.height"/>
        </VIEW></THEME>
        """);

    private static SkinNode? MessagePane(string text) => SyntheticPane($"""
        <THEME><VIEW backgroundColor="#212741">
          <TEXT left="24" top="24" width="420" value="{System.Security.SecurityElement.Escape(text)}" fontType="Tahoma" fontSize="9" fontStyle="bold" foregroundColor="#FFFFFF"/>
        </VIEW></THEME>
        """);

    public void RequestViewSwitch(string viewId) => _pendingViewId = viewId;

    public void RequestClose() => _closeRequested = true;

    public void Tick(TimeSpan elapsed)
    {
        if (IsModal)
            return;
        Player.Tick(elapsed);
        CurrentView?.Tick(elapsed);

        if (_pendingViewId is { } id)
        {
            _pendingViewId = null;
            OpenView(id);
        }

        if (_closeRequested)
        {
            _closeRequested = false;
            CurrentView?.Close();
            Host.CloseSkin();
        }
    }

    public void Log(string message) => Host.Log($"[{Package.Id}] {message}");

    public void Dispose()
    {
        Player.PlayStateChanged -= OnPlayStateChanged;
        Player.OpenStateChanged -= OnOpenStateChanged;
        Player.StatusChanged -= OnStatusChanged;
        Player.ModeChanged -= OnModeChanged;
        Player.CurrentMediaChanged -= OnMediaChanged;
        CurrentView?.Close();
    }

    private void OpenView(string? viewId)
    {
        SkinView.Trace?.Invoke("session: finding view");
        var node = Package.FindView(viewId);
        SkinView.Trace?.Invoke("session: view found");
        if (node is null)
        {
            Log($"View '{viewId}' not found.");
            if (CurrentView is not null)
                return;
            node = Package.Views.First();
        }

        CurrentView?.Close();
        var view = new SkinView(this, node);
        SkinView.Trace?.Invoke("session: SkinView constructed");
        CurrentView = view;
        view.Load();
        Host.ViewChanged(view);
    }

    private void OnPlayStateChanged(object? sender, PlayState state) => CurrentView?.OnPlayStateChanged(state);

    private void OnOpenStateChanged(object? sender, OpenState state) => CurrentView?.OnOpenStateChanged(state);

    private void OnStatusChanged(object? sender, string status) => CurrentView?.OnStatusChanged();

    private void OnModeChanged(object? sender, (string Mode, bool Value) e) => CurrentView?.OnModeChanged(e.Mode, e.Value);

    private void OnMediaChanged(object? sender, EventArgs e) => CurrentView?.OnMediaChanged();
}
