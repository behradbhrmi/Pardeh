namespace Pardeh.Core.Input;

/// <summary>
/// The keymaps that ship with Pardeh. They are read-only: editing one makes a copy.
/// A key belongs to one command only (a test checks it).
/// </summary>
public static class BuiltInKeymaps
{
    public const string VlcId = "vlc";
    public const string ClassicId = "classic";

    public static IReadOnlyList<KeymapProfile> All { get; } =
    [
        new KeymapProfile(VlcId, "VLC", null, builtIn: true, Vlc()),
        new KeymapProfile(ClassicId, "Pardeh Classic", null, builtIn: true, Classic()),
    ];

    /// <summary>
    /// VLC's default hotkeys on Windows and Linux: the ones from its Hotkeys preferences and the ones its menus use.
    /// The section at the end holds what only Pardeh has, on keys VLC leaves free or uses for the nearest thing.
    /// </summary>
    private static Dictionary<string, List<string>> Vlc()
    {
        var map = new Map
        {
            // ---- Playback ----
            { KeyCmd.PlayPause, "Space", "MediaPlayPause" },
            { KeyCmd.Stop, "S", "MediaStop" },
            { KeyCmd.Next, "N", "MediaNextTrack" },
            { KeyCmd.Previous, "P", "MediaPreviousTrack" },
            { KeyCmd.Shuffle, "R" },
            { KeyCmd.Repeat, "L" },
            { KeyCmd.FrameStep, "E" },
            { KeyCmd.Record, "Shift+R" },

            // ---- Speed ----
            { KeyCmd.Faster, "Plus" },
            { KeyCmd.Slower, "Minus" },
            { KeyCmd.NormalSpeed, "Equals" },
            { KeyCmd.FasterFine, "]" },
            { KeyCmd.SlowerFine, "[" },

            // ---- Seeking ----
            // VLC keeps the plain arrows for disc menus; Pardeh has none, so they seek (and set the volume, below).
            { KeyCmd.Back5, "Left" },
            { KeyCmd.Forward5, "Right" },
            { KeyCmd.BackExtraShort, "Shift+Left" },
            { KeyCmd.ForwardExtraShort, "Shift+Right" },
            { KeyCmd.BackShort, "Alt+Left" },
            { KeyCmd.ForwardShort, "Alt+Right" },
            { KeyCmd.BackMedium, "Ctrl+Left" },
            { KeyCmd.ForwardMedium, "Ctrl+Right" },
            { KeyCmd.BackLong, "Ctrl+Alt+Left" },
            { KeyCmd.ForwardLong, "Ctrl+Alt+Right" },
            { KeyCmd.JumpToTime, "Ctrl+T" },
            { KeyCmd.ShowPosition, "T" },

            // ---- Audio ----
            { KeyCmd.VolumeUp, "Ctrl+Up", "Up", "VolumeUp" },
            { KeyCmd.VolumeDown, "Ctrl+Down", "Down", "VolumeDown" },
            { KeyCmd.Mute, "M", "VolumeMute" },
            { KeyCmd.NextAudioTrack, "B" },
            { KeyCmd.AudioDevice, "Shift+A" },
            { KeyCmd.AudioDelayDown, "J" },
            { KeyCmd.AudioDelayUp, "K" },

            // ---- Subtitles ----
            { KeyCmd.SubtitleToggle, "Shift+V" },
            { KeyCmd.SubtitleNext, "V" },
            { KeyCmd.SubtitlePrevious, "Alt+V" },
            { KeyCmd.SubtitleEarlier, "G" },
            { KeyCmd.SubtitleLater, "H" },
            { "subtitle.markAudio", "Shift+H" },
            { "subtitle.markSubtitle", "Shift+J" },
            { "subtitle.applySync", "Shift+K" },
            { "subtitle.resetSync", "Ctrl+Shift+K" },

            // ---- Video ----
            { KeyCmd.FullScreen, "F" },
            { KeyCmd.LeaveFullScreen, "Escape" },
            { KeyCmd.AspectRatio, "A" },
            { KeyCmd.Crop, "C" },
            { KeyCmd.Snapshot, "Shift+S" },
            { "video.zoom", "Z" },
            { "video.unzoom", "Shift+Z" },
            { "video.zoomQuarter", "Alt+1" },
            { "video.zoomHalf", "Alt+2" },
            { "video.zoomOriginal", "Alt+3" },
            { "video.zoomDouble", "Alt+4" },
            { "video.autoscale", "O" },
            { "video.scaleUp", "Alt+O" },
            { "video.scaleDown", "Alt+Shift+O" },
            { "video.deinterlace", "D" },
            { "video.deinterlaceMode", "Shift+D" },
            { "video.wallpaper", "W" },
            { "video.cropTop", "Alt+R" },
            { "video.uncropTop", "Alt+Shift+R" },
            { "video.cropLeft", "Alt+D" },
            { "video.uncropLeft", "Alt+Shift+D" },
            { "video.cropBottom", "Alt+C" },
            { "video.uncropBottom", "Alt+Shift+C" },
            { "video.cropRight", "Alt+F" },
            { "video.uncropRight", "Alt+Shift+F" },
            { "video.fovIn", "PageUp" },
            { "video.fovOut", "PageDown" },
            { "video.nextProgram", "X" },
            { "video.previousProgram", "Shift+X" },

            // ---- Discs and chapters ----
            { "disc.menu", "Shift+M" },
            { "disc.previousTitle", "Shift+O" },
            { "disc.nextTitle", "Shift+B" },
            { "disc.previousChapter", "Shift+P" },
            { "disc.nextChapter", "Shift+N" },
            { "disc.activate", "Enter" },

            // ---- Files (the menu shortcuts) ----
            { KeyCmd.OpenFile, "Ctrl+O" },
            { KeyCmd.OpenFiles, "Ctrl+Shift+O" },
            { KeyCmd.OpenFolder, "Ctrl+F" },
            { KeyCmd.OpenUrl, "Ctrl+N" },
            { KeyCmd.PasteLocation, "Ctrl+V" },
            { KeyCmd.SavePlaylist, "Ctrl+Y" },
            { KeyCmd.MediaInfo, "Ctrl+I" },
            { "file.codecInfo", "Ctrl+J" },
            { "file.openDisc", "Ctrl+D" },
            { "file.openCapture", "Ctrl+C" },
            { "file.convert", "Ctrl+R" },
            { "file.stream", "Ctrl+S" },

            // ---- View and application ----
            { KeyCmd.Library, "Ctrl+L" },        // VLC: playlist
            { KeyCmd.Effects, "Ctrl+E" },
            { KeyCmd.ShowControls, "I" },
            { KeyCmd.TitleBar, "Ctrl+H" },       // VLC: minimal interface
            { KeyCmd.Maximize, "F11" },          // VLC: full screen interface
            { KeyCmd.Preferences, "Ctrl+P" },
            { KeyCmd.About, "Shift+F1" },
            { KeyCmd.Quit, "Ctrl+Q" },
            { "app.messages", "Ctrl+M" },

            // ---- Only in Pardeh ----
            { KeyCmd.Lyrics, "Ctrl+Shift+L" },
            { KeyCmd.AbRepeat, "Shift+L" },
            { KeyCmd.MarkerList, "Ctrl+B" },     // VLC: custom bookmarks
            { KeyCmd.MarkerAdd, "Ctrl+Shift+B" },
            { KeyCmd.MarkerPrevious, "Ctrl+Shift+Left" },
            { KeyCmd.MarkerNext, "Ctrl+Shift+Right" },
            { KeyCmd.FullMode, "Ctrl+1" },
            { KeyCmd.SkinMode, "Ctrl+2" },
            { KeyCmd.BarsStyle, "Ctrl+Shift+M" },
            { KeyCmd.AlwaysOnTop, "Ctrl+Shift+A" },
            { KeyCmd.MiniWidget, "Ctrl+Shift+W" },
            { KeyCmd.ZoomIn, "Ctrl+Plus", "Ctrl+Equals" },
            { KeyCmd.ZoomOut, "Ctrl+Minus" },
            { KeyCmd.ZoomReset, "Ctrl+0", "Ctrl+NumPad0" },
        };

        for (var i = 1; i <= KeyCmd.BookmarkCount; i++)
        {
            map.Add(KeyCmd.PlayBookmark(i), $"F{i}");
            map.Add(KeyCmd.SetBookmark(i), $"Ctrl+F{i}");
        }

        return map.Bindings;
    }

    /// <summary>The keys Pardeh had before keymaps: the classic Windows Media Player ones and a few additions.</summary>
    private static Dictionary<string, List<string>> Classic() => new Map
    {
        { KeyCmd.PlayPause, "Ctrl+P", "Space", "MediaPlayPause" },
        { KeyCmd.Stop, "Ctrl+S", "MediaStop" },
        { KeyCmd.Previous, "Ctrl+B", "PageUp", "MediaPreviousTrack" },
        { KeyCmd.Next, "Ctrl+F", "PageDown", "MediaNextTrack" },
        { KeyCmd.Rewind, "Ctrl+Shift+B" },
        { KeyCmd.FastForward, "Ctrl+Shift+F" },
        { KeyCmd.Shuffle, "Ctrl+H" },
        { KeyCmd.Repeat, "Ctrl+T" },
        { KeyCmd.AbRepeat, "Ctrl+R" },

        { KeyCmd.Faster, "Ctrl+Shift+G", "]" },
        { KeyCmd.Slower, "Ctrl+Shift+S", "[" },
        { KeyCmd.NormalSpeed, "Ctrl+Shift+N" },

        { KeyCmd.Back5, "Left" },
        { KeyCmd.Forward5, "Right" },
        { KeyCmd.Back30, "Shift+Left" },
        { KeyCmd.Forward30, "Shift+Right" },

        { KeyCmd.VolumeUp, "F9", "Up", "VolumeUp" },
        { KeyCmd.VolumeDown, "F8", "Down", "VolumeDown" },
        { KeyCmd.Mute, "F7", "VolumeMute" },
        { KeyCmd.NextAudioTrack, "Ctrl+Shift+L" },

        { KeyCmd.SubtitleToggle, "Ctrl+Shift+C" },
        { KeyCmd.SubtitleEarlier, "G" },
        { KeyCmd.SubtitleLater, "H" },

        { KeyCmd.FullScreen, "Alt+Enter" },
        { KeyCmd.LeaveFullScreen, "Escape" },
        { KeyCmd.AspectRatio, "A" },

        { KeyCmd.MarkerAdd, "M" },
        { KeyCmd.MarkerList, "Shift+M" },
        { KeyCmd.MarkerPrevious, "Ctrl+Left" },
        { KeyCmd.MarkerNext, "Ctrl+Right" },

        { KeyCmd.OpenFile, "Ctrl+O" },
        { KeyCmd.OpenUrl, "Ctrl+U" },
        { KeyCmd.Quit, "Alt+F4" },

        { KeyCmd.Lyrics, "Ctrl+L" },
        { KeyCmd.FullMode, "Ctrl+1" },
        { KeyCmd.SkinMode, "Ctrl+2" },
        { KeyCmd.TitleBar, "Ctrl+M" },
        { KeyCmd.BarsStyle, "Ctrl+Shift+M" },
        { KeyCmd.AlwaysOnTop, "Ctrl+Shift+A" },
        { KeyCmd.MiniWidget, "Ctrl+Shift+W" },
        { KeyCmd.ZoomIn, "Ctrl+Plus", "Ctrl+Equals" },
        { KeyCmd.ZoomOut, "Ctrl+Minus" },
        { KeyCmd.ZoomReset, "Ctrl+0", "Ctrl+NumPad0" },
    }.Bindings;

    private sealed class Map : System.Collections.IEnumerable
    {
        public Dictionary<string, List<string>> Bindings { get; } = [];

        public void Add(string command, params string[] keys) => Bindings.Add(command, [.. keys]);

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => Bindings.GetEnumerator();
    }
}
