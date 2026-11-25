using Pardeh.Core.Playback;

namespace Pardeh.Skinning.Runtime.Scripting;

/// <summary>
/// The <c>app</c> object full-mode skins script against: which task pane is showing, title bar and taskbar
/// visibility, maximized state, and a few helpers.
/// </summary>
public sealed class AppObject : ScriptableObject
{
    private string _currentTask = "NowPlaying";
    private bool _taskbarVisible = true;
    // Pardeh draws full mode frameless (no XP caption), as WMP did with the menu bar hidden.
    private bool _titleBarVisible;
    private bool _titleBarAutoHide;

    public AppObject(SkinSession session)
    {
        Property("currentTask", () => _currentTask, v => _currentTask = SkinValue.ToText(v));
        Property("taskbarVisible", () => _taskbarVisible, v => SetTaskbar(SkinValue.ToBool(v)));
        Property("taskBarVisible", () => _taskbarVisible, v => SetTaskbar(SkinValue.ToBool(v)));
        // "Show menu bar": the host frames the window with a caption and menu row. Remembered between runs.
        _titleBarVisible = SkinValue.ToBool(session.Preferences.Load("app", "titleBarVisible"));
        Property("titleBarVisible", () => _titleBarVisible, v =>
        {
            var visible = SkinValue.ToBool(v);
            if (visible == _titleBarVisible)
                return;
            _titleBarVisible = visible;
            session.Preferences.Save("app", "titleBarVisible", visible ? "true" : "false");
            session.Host.RunCommand("titleBar", [visible]);
        });
        Property("titleBarAutoHide", () => _titleBarAutoHide, v => _titleBarAutoHide = SkinValue.ToBool(v));
        Property("maximized", () => SkinValue.ToBool(session.Host.RunCommand("isMaximized", [])));
        Property("isXPOrBetter", () => true);
        Property("bgPluginRunning", () => false);
        Property("hasDisplay", () => true);
        Method("gotoSkinMode", () => session.Host.RunCommand("skinMode", []));
        Method("getTimeString", args => MediaPlayer.FormatTime(SkinValue.ToDouble(Arg(args, 0))));
        Method("configurePlugins", () => { });
        Method("adjustLeft", args => Arg(args, 0));
    }

    public string CurrentTask => _currentTask;

    private void SetTaskbar(bool visible)
    {
        if (_taskbarVisible == visible)
            return;
        _taskbarVisible = visible;
        // Both spellings are used by skins; notify the other one too.
        NotifyChanged("taskbarVisible");
    }
}

/// <summary>The <c>event</c> object handlers read (button, position, modifier keys), filled from real input.</summary>
public sealed class EventObject : ScriptableObject
{
    public EventObject()
    {
        Property("button", () => (double)Button, v => Button = (int)SkinValue.ToDouble(v));
        Property("clientX", () => X);
        Property("clientY", () => Y);
        Property("x", () => X);
        Property("y", () => Y);
        Property("offsetX", () => X);
        Property("offsetY", () => Y);
        Property("screenX", () => X);
        Property("screenY", () => Y);
        Property("shiftKey", () => Shift);
        Property("ctrlKey", () => Ctrl);
        Property("altKey", () => Alt);
        Property("keyCode", () => (double)KeyCode, v => KeyCode = (int)SkinValue.ToDouble(v));
        Property("returnValue", () => ReturnValue, v => ReturnValue = SkinValue.ToBool(v, true));
        Property("cancelBubble", () => false, _ => { });
    }

    /// <summary>1 = left, 2 = right, 4 = middle (as in the classic object model).</summary>
    public int Button { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public bool Shift { get; set; }

    public bool Ctrl { get; set; }

    public bool Alt { get; set; }

    public int KeyCode { get; set; }

    public bool ReturnValue { get; set; } = true;
}

/// <summary>
/// The <c>helper</c> object the classic Now Playing pane uses for its layout (which areas show and how big) and
/// visualization names. Sizes follow the original defaults: a 190 px sidebar with a 160 px album-art block.
/// </summary>
public sealed class HelperObject : ScriptableObject
{
    private const int ControlBar = 20, Separator = 2, Sidebar = 190, Metadata = 160, Banner = 36, MinDisplay = 60;
    private readonly int[] _sizes = new int[8];

    public HelperObject(SkinSession session)
    {
        // Now Playing's panel sizes, from the window size and what mediacenter says to show.
        Method("calcLayout", args =>
        {
            var width = (int)SkinValue.ToDouble(Arg(args, 0));
            var height = (int)SkinValue.ToDouble(Arg(args, 1));
            var captions = SkinValue.ToBool(Arg(args, 2));
            var banner = SkinValue.ToBool(Arg(args, 3));
            var center = session.MediaCenter;
            bool Show(string name) => SkinValue.ToBool(center.GetMember(name), true);
            int Size(string name, int fallback) => (int)SkinValue.ToDouble(center.GetMember(name), fallback);

            var settingsHeight = Size("settingsHeight", 120);
            _sizes[1] = Show("showSettings") && height - ControlBar - settingsHeight - Separator >= MinDisplay ? settingsHeight : 0;
            var media = Math.Max(0, height - ControlBar - (_sizes[1] > 0 ? _sizes[1] + Separator : 0));

            // The saved width, shrunk to leave the display its 200 px when the window is smaller (min 100).
            var sidebar = Math.Min(Size("playlistWidth", Sidebar), width - 200 - Separator);
            _sizes[2] = (Show("showPlaylist") || Show("showMetadata")) && sidebar >= 100 ? sidebar : 0;
            var metadata = Size("metadataHeight", Metadata);
            _sizes[3] = _sizes[2] > 0 && Show("showMetadata") && height - ControlBar >= metadata + MinDisplay ? metadata : 0;
            _sizes[4] = _sizes[2] > 0 && Show("showPlaylist")
                ? Math.Max(0, height - ControlBar - (_sizes[3] > 0 ? _sizes[3] + Separator : 0))
                : 0;
            if (_sizes[4] == 0 && _sizes[3] > 0)
                _sizes[3] = height - ControlBar;                                  // metadata fills the sidebar
            _sizes[6] = captions ? Size("captionsHeight", 60) : 0;
            _sizes[7] = banner ? Banner : 0;
            _sizes[5] = Math.Max(0, media - _sizes[6] - _sizes[7]);           // video / visualization
            return null;
        });
        Method("getLayoutSize", args =>
        {
            var index = (int)SkinValue.ToDouble(Arg(args, 0));
            return index is > 0 and < 8 ? (double)_sizes[index] : 0.0;
        });
        Property("captionsAvailable", () => false);
        Property("linkRequest", () => string.Empty);
        Property("linkRequestParams", () => string.Empty);
        Method("resolveDisplayView", _ => "VizView");
        Method("viewFriendlyName", args => SkinValue.ToText(Arg(args, 0)) switch
        {
            "AppColorView" => "Color Chooser",
            "EQView" => "Graphic Equalizer",
            "CrossFadeView" => "Crossfading and Auto Volume Leveling",
            "AdvancedAudioView" => "Quiet Mode",
            "SRSView" => "SRS WOW Effects",
            "VideoSettingsView" => "Video Settings",
            "AdvancedVideoSettingsView" => "Video Settings",
            "MediaLinkView" => "Media Link for E-Mail",
            "AlbumArtView" => "Album Art",
            "MediaInfoView" => "Media Information",
            "VideoView" => "Video",
            _ => "Visualization",
        });
        Method("viewPresetName", _ => string.Empty);
        Method("viewPresetCount", _ => 3.0);
        Method("effectFriendlyName", args => VisualizationCatalog.All[VisualizationCatalog.IndexOf(SkinValue.ToText(Arg(args, 0)), 0)].Type);
        Method("effectPresetName", args =>
            VisualizationCatalog.All[VisualizationCatalog.IndexOf(SkinValue.ToText(Arg(args, 0)), (int)SkinValue.ToDouble(Arg(args, 1)))].Preset);
        Method("getSkinFile", _ => string.Empty);
        Property("linkAvailable", () => 0.0);
        Method("getRootPlaylist", args => Arg(args, 0));
        // The default cover art bitmaps: mixed (nothing open), note (audio), film (video); small or large.
        Method("getCurrentArtID", args =>
        {
            var small = SkinValue.ToBool(Arg(args, 0));
            var player = session.Player;
            var id = player.CurrentMedia is not { } item ? 520
                : item.VideoWidth > 0 || player.HasVideo ? 527
                : 525;
            return (double)(small ? id : id + 1);
        });
        Method("getTimeString", args => Pardeh.Core.Playback.MediaPlayer.FormatTime(SkinValue.ToDouble(Arg(args, 0))));
    }
}
