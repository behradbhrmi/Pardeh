using System.Text.Json;

namespace Pardeh.UI;

/// <summary>Pardeh's own options (Tools › Options), saved as JSON in the data folder.</summary>
public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(AppPaths.DataDirectory, "settings.json");

    /// <summary>Keep the player above other windows.</summary>
    public bool AlwaysOnTop { get; set; }

    /// <summary>The picture's shape (a key of <see cref="Core.Playback.VideoSettings.Aspects"/>): "auto", "16:9", "4:3", "fill", "crop" ...</summary>
    public string VideoAspect { get; set; } = "auto";

    /// <summary>Desktop: how large skins are drawn, as a multiple of their own size (1 = the skin's real pixels, 1.5, 2 ...). View > Skin Size, Ctrl+Plus / Ctrl+Minus.</summary>
    public double SkinZoom
    {
        get => _skinZoom;
        set => _skinZoom = double.IsFinite(value) ? Math.Clamp(Math.Round(value * 100) / 100, 0.75, 4) : 1;
    }

    private double _skinZoom = double.TryParse(Environment.GetEnvironmentVariable("PARDEH_SKIN_ZOOM"), System.Globalization.CultureInfo.InvariantCulture, out var devZoom) ? Math.Clamp(devZoom, 0.75, 4) : 1; // dev aid for snapshots

    public static readonly double[] SkinZooms = [0.75, 1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4];

    // On-screen messages (the short notes over the player after a command).

    public bool OnScreenMessages { get; set; } = true;

    /// <summary>"Top", "TopLeft", "TopRight", "Centre" or "Bottom".</summary>
    public string NoticePlace { get; set; } = "Top";

    /// <summary>"Short", "Normal" or "Long".</summary>
    public string NoticeTime { get; set; } = "Normal";

    /// <summary>The title of a new track: "FullScreenAndVideo", "Always" or "Never" (<see cref="Core.Notices.SongTitleNotices"/>).</summary>
    public string NoticeSongTitle { get; set; } = "FullScreenAndVideo";

    /// <summary>Groups of messages turned off (<see cref="Core.Notices.NoticeCategory"/> names).</summary>
    public List<string> HiddenNotices { get; set; } = [];

    /// <summary>A video carries on where it was left.</summary>
    public bool ResumeVideos { get; set; } = true;

    /// <summary>Consecutive audio tracks play without a gap.</summary>
    public bool Gapless { get; set; } = true;

    /// <summary>Volume levelling from the ReplayGain tags: "Off", "Track" or "Album".</summary>
    public string ReplayGain { get; set; } = "Off";

    /// <summary>Open files in the running Pardeh instead of starting another one.</summary>
    public bool SingleInstance { get; set; } = true;

    /// <summary>Desktop full mode: while the skin shows its bars, leave out Pardeh's title bar and menu bar (rounded corners instead).</summary>
    public bool BarsWithoutTitleBar { get; set; }

    /// <summary>Network media: seconds of sound read ahead.</summary>
    public int NetworkBufferSeconds { get; set; } = 10;

    /// <summary>Network media: connect again when a connection drops.</summary>
    public bool NetworkReconnect { get; set; } = true;

    /// <summary>Online video that comes in several sizes: the tallest picture to take (more needs a faster connection).</summary>
    public int NetworkVideoHeight { get; set; } = Pardeh.Core.Playback.NetworkOptions.DefaultVideoHeight;

    /// <summary>The network settings as the player takes them.</summary>
    public Pardeh.Core.Playback.NetworkOptions ToNetworkOptions() =>
        new(NetworkBufferSeconds, NetworkReconnect) { MaxVideoHeight = NetworkVideoHeight };

    /// <summary>Addresses opened lately, newest first (never one that carries a password or token).</summary>
    public List<string> RecentAddresses { get; set; } = [];

    /// <summary>The lyrics view shows the track's guitar tab instead of the words, when it has one.</summary>
    public bool LyricsShowTab { get; set; }

    /// <summary>Desktop full modes: lyrics and chords shown over the video / visualization area (Ctrl+L).</summary>
    public bool LyricsOnStage { get; set; }

    /// <summary>Desktop: the separate lyrics window, its size (DIPs) and last place.</summary>
    public bool LyricsWindowOpen { get; set; }

    public int LyricsWindowWidth { get; set; } = 400;

    public int LyricsWindowHeight { get; set; } = 560;

    public int? LyricsWindowX { get; set; }

    public int? LyricsWindowY { get; set; }

    /// <summary>Playlist bookmarks 1 to 10 (Ctrl+F1 sets, F1 plays, in the VLC keymap): number to media path or address.</summary>
    public Dictionary<int, string> PlaylistBookmarks { get; set; } = [];

    /// <summary>"last", "full" or "skin": the mode Pardeh starts in.</summary>
    public string StartMode { get; set; } = "last";

    /// <summary>
    /// Full mode's look: "auto" (the original WMP 9 one when imported, else Pardeh Classic), "wmp9", "pardeh"
    /// (Pardeh Classic), "horizon", "luna", "fluent", "ember" or "aero10" (Pardeh Ten). New default skins are added here, never by changing an existing one.
    /// </summary>
    public string FullModeLook { get; set; } = "auto";

    /// <summary>Full mode pages hidden from the task rail (keys from <see cref="Pages"/>).</summary>
    public List<string> HiddenPages { get; set; } = [];

    /// <summary>The full mode pages that can be hidden (Now Playing and Settings always show).</summary>
    public static readonly (string Key, string Name)[] Pages =
    [
        ("library", "Media Library"),
        ("cdrip", "Copy from CD"),
        ("burn", "Copy to CD or Device"),
        ("radio", "Radio Tuner"),
        ("info", "Info Center"),
    ];

    // Appearance (Tools › Options › Appearance): each item is classic XP (false, default) or modern Windows 11 (true).

    /// <summary>Segoe UI in Pardeh-drawn UI, menus and dialogs instead of Tahoma / Trebuchet MS.</summary>
    public bool ModernFont { get; set; }

    /// <summary>A flat Windows 11 caption instead of the Luna one (when the menu bar is shown).</summary>
    public bool ModernTitleBar { get; set; }

    /// <summary>A flat Windows 11 menu bar instead of the classic File / View / Play / Tools / Help row.</summary>
    public bool ModernMenuBar { get; set; }

    /// <summary>Fluent context menus and popups instead of XP-style ones.</summary>
    public bool ModernMenus { get; set; }

    /// <summary>Fluent dialogs (Options, Prompt, Edit Info) on a light Windows 11 surface instead of XP buttons, fields and tabs.</summary>
    public bool ModernDialogs { get; set; }

    /// <summary>Fluent scrollbars and tooltips instead of XP Luna ones.</summary>
    public bool ModernControls { get; set; }

    /// <summary>Flat task pane toolbars (Pardeh-drawn panes) instead of the bordered classic buttons.</summary>
    public bool ModernToolbars { get; set; }

    /// <summary>A flat dark Windows 11 full-screen control bar instead of the Luna one.</summary>
    public bool ModernFullScreenBar { get; set; }

    /// <summary>Windows 11 rounded corners on the framed window (menu bar shown).</summary>
    public bool RoundedCorners { get; set; }

    /// <summary>The Pardeh Glass look shows the classic branding of the design it is drawn after (a flag logo, "Windows Media Player"), as in the mockups; off: Pardeh's own logo and name.</summary>
    public bool ClassicBranding { get; set; }

    /// <summary>
    /// The colour of the Pardeh Glass look (Settings > Appearance > Skin color): the hue (degrees) its royal blue (220, the default) is rotated to,
    /// and saturation / brightness scales (1 = as drawn). The defaults draw Glass exactly as before. See <see cref="Rendering.GlassPalette"/>.
    /// </summary>
    public double GlassTintHue { get; set; } = 220;

    /// <summary>The Pardeh logo takes the skin colour (its teal / blue parts are re-coloured, the yellow dot stays) instead of its own colours.</summary>
    public bool LogoFollowsSkinColor { get; set; }

    public double GlassTintSaturation { get; set; } = 1;

    public double GlassTintBrightness { get; set; } = 1;

    /// <summary>Pardeh Glass Library: the Music tab shows the Browse Media icon grid instead of the list (the bottom bar's list / grid pill).</summary>
    public bool BrowseGrid { get; set; }

    /// <summary>Android: the shuffle and repeat toggles of the phone looks are remembered across restarts (the desktop keeps them for the session only).</summary>
    public bool ShuffleOn { get; set; }

    public bool RepeatOn { get; set; }

    // Song details from online databases (Fix Info).

    /// <summary>
    /// The user agreed to Fix Info looking songs up on MusicBrainz and the Cover Art Archive. Off by default; a look-up still only
    /// happens when asked for.
    /// </summary>
    public bool FixInfoOnline { get; set; }

    /// <summary>Also look the playing song up by itself, once. Off by default, and only with <see cref="FixInfoOnline"/>.</summary>
    public bool FixInfoCheckPlaying { get; set; }

    /// <summary>Keep a history of look-ups on this device (Looked-Up Songs). Turning it off deletes it.</summary>
    public bool KeepLookupHistory { get; set; } = true;

    /// <summary>The user turned downloads on. Its own switch, off by default; each download is still asked for.</summary>
    public bool Downloads { get; set; }

    /// <summary>Suggest the database's script for names, for example Latin in place of Persian. Off: what the song uses stays.</summary>
    public bool FixInfoDatabaseScript { get; set; }

    // Lyrics. The engine reads these; a skin may show them.

    /// <summary>Look for lyrics of the playing track (tags, a .lrc or .txt next to it, your own saved text).</summary>
    public bool LyricsEnabled { get; set; } = true;

    /// <summary>
    /// Also look the track up online (LRCLIB) when it has no lyrics of its own. Off by default: a lookup sends the track's title, artist,
    /// album and length to lrclib.net.
    /// </summary>
    public bool LyricsOnline { get; set; }

    /// <summary>Extra lyrics services the user added (an address with {title} / {artist} / {album} / {duration}); asked after LRCLIB when online look-up is on.</summary>
    public List<Core.LyricsKit.LyricsEndpoint> LyricsSources { get; set; } = [];

    /// <summary>Keep lyrics fetched online in Pardeh's data folder, so they show again offline and are not fetched twice.</summary>
    public bool LyricsSaveFetched { get; set; } = true;

    /// <summary>Lyrics text size factor, 0.8 to 1.6 (1 = the skin's normal size). A skin applies it.</summary>
    public double LyricsTextSize
    {
        get => _lyricsTextSize;
        set => _lyricsTextSize = double.IsFinite(value) ? Math.Clamp(value, MinLyricsTextSize, MaxLyricsTextSize) : 1;
    }

    public const double MinLyricsTextSize = 0.8, MaxLyricsTextSize = 1.6;

    private double _lyricsTextSize = 1;

    /// <summary>Synchronised lyrics scroll to keep the current line in view (a skin applies it).</summary>
    public bool LyricsFollow { get; set; } = true;

    /// <summary>Chords are shown above the words when the lyrics have them.</summary>
    public bool LyricsShowChords { get; set; } = true;

    /// <summary>How transposed chords are spelled: "Auto" (as the sheet is), "Sharps" or "Flats".</summary>
    public string ChordSpelling { get; set; } = "Auto";

    /// <summary>Android notification / lock-screen card colour: false = taken from the cover (the system default), true = the skin colour (Glass tint).</summary>
    public bool NotificationUsesAccent { get; set; }

    /// <summary>Android: the expanded notification uses Pardeh's own card (blurred cover, shuffle / repeat row) instead of the system media card.</summary>
    public bool NotificationCustomCard { get; set; }

    // Windows desktop widget (a small always-on-top mini player; Pardeh.App/Platform/MiniWidget.cs). Android has its own widgets.
    public bool MiniWidgetOpen { get; set; }

    /// <summary>0 = mini player strip in the style of the Windows Media Player 9 taskbar mini player (default), 1 = the card.</summary>
    public int MiniWidgetStyle { get; set; }

    /// <summary>0 Navy, 1 skin colour, 2 blurred cover, 3 light, 4 transparent.</summary>
    public int MiniWidgetTheme { get; set; }

    /// <summary>Background opacity in percent, 20..100.</summary>
    public int MiniWidgetOpacity { get; set; } = 100;

    public bool MiniWidgetSeek { get; set; } = true;
    public bool MiniWidgetSkip { get; set; } = true;
    public bool MiniWidgetShuffle { get; set; } = true;
    public bool MiniWidgetRepeat { get; set; } = true;
    public bool MiniWidgetFavorite { get; set; } = true;
    public bool MiniWidgetTopmost { get; set; } = true;

    /// <summary>Last position of the widget on screen (-1: not placed yet).</summary>
    public double MiniWidgetX { get; set; } = -1;

    public double MiniWidgetY { get; set; } = -1;

    /// <summary>Phone Library: the sort choice of each level ("songs", "videos", "favorites", "artists" ...), as "Title:asc" / "Year:desc" (see <see cref="Core.Library.SortSpec"/>).</summary>
    public Dictionary<string, string> LibrarySorts { get; set; } = [];

    /// <summary>The remembered sort of a Library level, or <paramref name="fallback"/>.</summary>
    public Core.Library.SortSpec SortOf(string level, Core.Library.SortSpec fallback) =>
        LibrarySorts.TryGetValue(level, out var text) ? Core.Library.SortSpec.Parse(text, fallback) : fallback;

    public void RememberSort(string level, Core.Library.SortSpec sort)
    {
        LibrarySorts[level] = sort.ToString();
        Save();
    }

    /// <summary>Phone looks: the visualization style of Pardeh Glass ("bars", "mirror", "circular", "ribbon", "particles", "rings", "aurora", "led"); see <see cref="Rendering.VizStyles"/>.</summary>
    public string GlassVizStyle { get; set; } = "bars";

    /// <summary>Phone looks other than Glass: the classic visualization ("Ambience : Water" ...) the stage shows; empty = the player's default.</summary>
    public string PhoneVizPreset { get; set; } = string.Empty;

    /// <summary>Phone looks: "off", "track" (another style on every new track) or "cycle" (another style every 15 seconds).</summary>
    public string VizAuto { get; set; } = "off";

    public event Action? Changed;

    public static AppSettings Current { get; } = Load();

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) is { } loaded)
                return loaded;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.DataDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        Changed?.Invoke();
    }
}
