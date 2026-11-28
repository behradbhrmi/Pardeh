namespace Pardeh.Core.Input;

/// <summary>
/// Something a key can be bound to. <see cref="Id"/> is what keymap files store, so it never changes once shipped.
/// A command that is not <see cref="Supported"/> exists in VLC but Pardeh cannot do it yet: its keys are kept (and shown
/// in the editor), and pressing them does nothing.
/// </summary>
public sealed record KeyCommand(string Id, string Category, string Name, bool Supported = true);

/// <summary>The command ids the code refers to.</summary>
public static class KeyCmd
{
    public const string PlayPause = "playback.playPause";
    public const string Play = "playback.play";
    public const string Pause = "playback.pause";
    public const string Stop = "playback.stop";
    public const string Next = "playback.next";
    public const string Previous = "playback.previous";
    public const string FastForward = "playback.fastForward";
    public const string Rewind = "playback.rewind";
    public const string Shuffle = "playback.shuffle";
    public const string Repeat = "playback.repeat";
    public const string AbRepeat = "playback.abRepeat";
    public const string FrameStep = "playback.frameStep";
    public const string Record = "playback.record";

    public const string Faster = "speed.faster";
    public const string Slower = "speed.slower";
    public const string NormalSpeed = "speed.normal";
    public const string FasterFine = "speed.fasterFine";
    public const string SlowerFine = "speed.slowerFine";

    public const string BackExtraShort = "seek.backExtraShort";
    public const string ForwardExtraShort = "seek.forwardExtraShort";
    public const string BackShort = "seek.backShort";
    public const string ForwardShort = "seek.forwardShort";
    public const string BackMedium = "seek.backMedium";
    public const string ForwardMedium = "seek.forwardMedium";
    public const string BackLong = "seek.backLong";
    public const string ForwardLong = "seek.forwardLong";
    public const string Back5 = "seek.back5";
    public const string Forward5 = "seek.forward5";
    public const string Back30 = "seek.back30";
    public const string Forward30 = "seek.forward30";
    public const string JumpToTime = "seek.jumpToTime";
    public const string ShowPosition = "seek.showPosition";

    public const string VolumeUp = "audio.volumeUp";
    public const string VolumeDown = "audio.volumeDown";
    public const string Mute = "audio.mute";
    public const string NextAudioTrack = "audio.nextTrack";
    public const string AudioDevice = "audio.nextDevice";
    public const string AudioDelayUp = "audio.delayUp";
    public const string AudioDelayDown = "audio.delayDown";

    public const string SubtitleToggle = "subtitle.toggle";
    public const string SubtitleNext = "subtitle.nextTrack";
    public const string SubtitlePrevious = "subtitle.previousTrack";
    public const string SubtitleLater = "subtitle.delayUp";
    public const string SubtitleEarlier = "subtitle.delayDown";

    public const string FullScreen = "video.fullScreen";
    public const string LeaveFullScreen = "video.leaveFullScreen";
    public const string AspectRatio = "video.nextAspect";
    public const string Crop = "video.crop";
    public const string Snapshot = "video.snapshot";

    public const string MarkerAdd = "marker.add";
    public const string MarkerList = "marker.list";
    public const string MarkerPrevious = "marker.previous";
    public const string MarkerNext = "marker.next";

    public const string OpenFile = "file.open";
    public const string OpenFiles = "file.openMultiple";
    public const string OpenFolder = "file.openFolder";
    public const string OpenUrl = "file.openUrl";
    public const string PasteLocation = "file.pasteLocation";
    public const string SavePlaylist = "file.savePlaylist";
    public const string MediaInfo = "file.mediaInfo";
    public const string Quit = "app.quit";
    public const string Preferences = "app.preferences";
    public const string About = "app.about";

    public const string Library = "view.library";
    public const string Lyrics = "view.lyrics";
    public const string Effects = "view.effects";
    public const string ShowControls = "view.showControls";
    public const string FullMode = "view.fullMode";
    public const string SkinMode = "view.skinMode";
    public const string TitleBar = "view.titleBar";
    public const string BarsStyle = "view.barsStyle";
    public const string Maximize = "view.maximize";
    public const string AlwaysOnTop = "view.alwaysOnTop";
    public const string MiniWidget = "view.miniWidget";
    public const string ZoomIn = "view.zoomIn";
    public const string ZoomOut = "view.zoomOut";
    public const string ZoomReset = "view.zoomReset";

    /// <summary>Playlist bookmarks: VLC's F1 … F10 (go to) and Ctrl+F1 … Ctrl+F10 (set).</summary>
    public const int BookmarkCount = 10;

    public static string SetBookmark(int number) => $"bookmark.set{number}";

    public static string PlayBookmark(int number) => $"bookmark.play{number}";
}

/// <summary>Every command, in the order the editor lists them.</summary>
public static class KeyCommandCatalog
{
    public static IReadOnlyList<KeyCommand> All { get; } = Build();

    private static readonly Dictionary<string, KeyCommand> ById = All.ToDictionary(c => c.Id);

    public static KeyCommand? Find(string id) => ById.GetValueOrDefault(id);

    private static List<KeyCommand> Build()
    {
        var all = new List<KeyCommand>();
        string category = "";
        void Group(string name) => category = name;
        void Add(string id, string name) => all.Add(new KeyCommand(id, category, name));
        void Missing(string id, string name) => all.Add(new KeyCommand(id, category, name, Supported: false));

        Group("Playback");
        Add(KeyCmd.PlayPause, "Play / pause");
        Add(KeyCmd.Play, "Play");
        Add(KeyCmd.Pause, "Pause");
        Add(KeyCmd.Stop, "Stop");
        Add(KeyCmd.Next, "Next item");
        Add(KeyCmd.Previous, "Previous item");
        Add(KeyCmd.FastForward, "Fast forward");
        Add(KeyCmd.Rewind, "Rewind");
        Add(KeyCmd.Shuffle, "Shuffle on / off");
        Add(KeyCmd.Repeat, "Repeat on / off");
        Add(KeyCmd.AbRepeat, "A-B repeat: set A, set B, clear");
        Missing(KeyCmd.FrameStep, "Next frame");
        Missing(KeyCmd.Record, "Record");

        Group("Speed");
        Add(KeyCmd.Faster, "Faster");
        Add(KeyCmd.Slower, "Slower");
        Add(KeyCmd.NormalSpeed, "Normal speed");
        Add(KeyCmd.FasterFine, "Faster (fine)");
        Add(KeyCmd.SlowerFine, "Slower (fine)");

        Group("Seeking");
        Add(KeyCmd.BackExtraShort, "Back 3 seconds");
        Add(KeyCmd.ForwardExtraShort, "Forward 3 seconds");
        Add(KeyCmd.BackShort, "Back 10 seconds");
        Add(KeyCmd.ForwardShort, "Forward 10 seconds");
        Add(KeyCmd.BackMedium, "Back 1 minute");
        Add(KeyCmd.ForwardMedium, "Forward 1 minute");
        Add(KeyCmd.BackLong, "Back 5 minutes");
        Add(KeyCmd.ForwardLong, "Forward 5 minutes");
        Add(KeyCmd.Back5, "Back 5 seconds");
        Add(KeyCmd.Forward5, "Forward 5 seconds");
        Add(KeyCmd.Back30, "Back 30 seconds");
        Add(KeyCmd.Forward30, "Forward 30 seconds");
        Add(KeyCmd.JumpToTime, "Jump to time");
        Add(KeyCmd.ShowPosition, "Show the position");

        Group("Audio");
        Add(KeyCmd.VolumeUp, "Volume up");
        Add(KeyCmd.VolumeDown, "Volume down");
        Add(KeyCmd.Mute, "Mute");
        Add(KeyCmd.NextAudioTrack, "Next audio track");
        Missing(KeyCmd.AudioDevice, "Next audio device");
        Missing(KeyCmd.AudioDelayUp, "Audio delay up");
        Missing(KeyCmd.AudioDelayDown, "Audio delay down");

        Group("Subtitles");
        Add(KeyCmd.SubtitleToggle, "Show / hide subtitles");
        Add(KeyCmd.SubtitleNext, "Next subtitle track");
        Add(KeyCmd.SubtitlePrevious, "Previous subtitle track");
        Add(KeyCmd.SubtitleEarlier, "Subtitles 0.1 s earlier");
        Add(KeyCmd.SubtitleLater, "Subtitles 0.1 s later");
        Missing("subtitle.markAudio", "Subtitle sync: mark the audio");
        Missing("subtitle.markSubtitle", "Subtitle sync: mark the subtitle");
        Missing("subtitle.applySync", "Subtitle sync: apply");
        Missing("subtitle.resetSync", "Subtitle sync: reset");

        Group("Video");
        Add(KeyCmd.FullScreen, "Full screen on / off");
        Add(KeyCmd.LeaveFullScreen, "Leave full screen");
        Add(KeyCmd.AspectRatio, "Next aspect ratio");
        Add(KeyCmd.Crop, "Crop to fill on / off");
        Add(KeyCmd.Snapshot, "Save a picture of the video");
        Missing("video.zoom", "Next zoom mode");
        Missing("video.unzoom", "Previous zoom mode");
        Missing("video.zoomQuarter", "Zoom 1:4");
        Missing("video.zoomHalf", "Zoom 1:2");
        Missing("video.zoomOriginal", "Zoom 1:1");
        Missing("video.zoomDouble", "Zoom 2:1");
        Missing("video.autoscale", "Auto scale on / off");
        Missing("video.scaleUp", "Increase the scale factor");
        Missing("video.scaleDown", "Decrease the scale factor");
        Missing("video.deinterlace", "Deinterlace on / off");
        Missing("video.deinterlaceMode", "Next deinterlace mode");
        Missing("video.wallpaper", "Wallpaper mode");
        Missing("video.cropTop", "Crop one pixel from the top");
        Missing("video.uncropTop", "Uncrop one pixel from the top");
        Missing("video.cropLeft", "Crop one pixel from the left");
        Missing("video.uncropLeft", "Uncrop one pixel from the left");
        Missing("video.cropBottom", "Crop one pixel from the bottom");
        Missing("video.uncropBottom", "Uncrop one pixel from the bottom");
        Missing("video.cropRight", "Crop one pixel from the right");
        Missing("video.uncropRight", "Uncrop one pixel from the right");
        Missing("video.fovIn", "360° video: zoom in");
        Missing("video.fovOut", "360° video: zoom out");
        Missing("video.nextProgram", "Next program");
        Missing("video.previousProgram", "Previous program");

        Group("Discs and chapters");
        Missing("disc.menu", "Disc menu");
        Missing("disc.previousTitle", "Previous title");
        Missing("disc.nextTitle", "Next title");
        Missing("disc.previousChapter", "Previous chapter");
        Missing("disc.nextChapter", "Next chapter");
        Missing("disc.navigateUp", "Disc menu: up");
        Missing("disc.navigateDown", "Disc menu: down");
        Missing("disc.navigateLeft", "Disc menu: left");
        Missing("disc.navigateRight", "Disc menu: right");
        Missing("disc.activate", "Disc menu: choose");

        Group("Markers");
        Add(KeyCmd.MarkerAdd, "Add a marker here");
        Add(KeyCmd.MarkerList, "List the markers");
        Add(KeyCmd.MarkerPrevious, "Previous marker");
        Add(KeyCmd.MarkerNext, "Next marker");

        Group("Playlist bookmarks");
        for (var i = 1; i <= KeyCmd.BookmarkCount; i++)
            Add(KeyCmd.PlayBookmark(i), $"Go to playlist bookmark {i}");
        for (var i = 1; i <= KeyCmd.BookmarkCount; i++)
            Add(KeyCmd.SetBookmark(i), $"Set playlist bookmark {i}");

        Group("Files");
        Add(KeyCmd.OpenFile, "Open a file");
        Add(KeyCmd.OpenFiles, "Open several files");
        Add(KeyCmd.OpenFolder, "Open a folder");
        Add(KeyCmd.OpenUrl, "Open a network address");
        Add(KeyCmd.PasteLocation, "Open the address or path on the clipboard");
        Add(KeyCmd.SavePlaylist, "Save the playlist");
        Add(KeyCmd.MediaInfo, "Media information (tags)");
        Missing("file.codecInfo", "Codec information");
        Missing("file.openDisc", "Open a disc");
        Missing("file.openCapture", "Open a capture device");
        Missing("file.convert", "Convert / save");
        Missing("file.stream", "Stream");

        Group("View and window");
        Add(KeyCmd.Library, "Media library and playlists");
        Add(KeyCmd.Lyrics, "Lyrics and chords");
        Add(KeyCmd.Effects, "Equalizer and effects");
        Add(KeyCmd.ShowControls, "Show the full-screen controls");
        Add(KeyCmd.FullMode, "Full mode");
        Add(KeyCmd.SkinMode, "Skin mode");
        Add(KeyCmd.TitleBar, "Title bar and menu bar (minimal view)");
        Add(KeyCmd.BarsStyle, "Skin bars only, without a title bar");
        Add(KeyCmd.Maximize, "Maximize the window");
        Add(KeyCmd.AlwaysOnTop, "Always on top");
        Add(KeyCmd.MiniWidget, "Desktop widget (mini player)");
        Add(KeyCmd.ZoomIn, "Larger skin");
        Add(KeyCmd.ZoomOut, "Smaller skin");
        Add(KeyCmd.ZoomReset, "Skin at its actual size");

        Group("Application");
        Add(KeyCmd.Preferences, "Options");
        Add(KeyCmd.About, "About");
        Add(KeyCmd.Quit, "Quit");
        Missing("app.messages", "Messages log");
        Missing("app.streamManager", "Stream manager (VLM)");
        return all;
    }
}
