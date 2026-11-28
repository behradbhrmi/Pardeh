using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Pardeh.Core.Input;
using Pardeh.Core.Library;
using Pardeh.Core.Playback;

namespace Pardeh.UI;

/// <summary>
/// The keyboard: a key press is looked up in the keymap in use (<see cref="Keymaps"/>) and the command it names runs here.
/// Which key does what is never decided in this file.
/// </summary>
internal sealed partial class PlayerView
{
    private const double VolumeStep = 5;
    private const double FineRateStep = 0.1;

    /// <summary>The desktop widget belongs to the Windows head, which sets this.</summary>
    internal Action? MiniWidgetToggle { get; set; }

    /// <summary>Key presses reaching the host (window or top level): the player's shortcuts.</summary>
    public void HandleKeyDown(KeyEventArgs e)
    {
        // Typing in a text box (search, tag editor, prompts) keeps its arrows and letters.
        if (e.Handled || TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox)
            return;

        // Android's Back key reaches us as Escape first and then as BackRequested: it leaves full screen whatever the keymap
        // says, and that press is remembered so it does no more.
        if (OperatingSystem.IsAndroid() && _fullScreen && e.Key == Key.Escape)
        {
            _backLeftFullScreenAt = _clock.ElapsedMilliseconds;
            SetFullScreen(false);
            e.Handled = true;
            return;
        }

        if (Keymaps.ChordOf(e) is { } chord && Keymaps.Current.CommandFor(chord) is { } command && RunKeyCommand(command))
            e.Handled = true;
    }

    /// <summary>
    /// Runs a keymap command. False when it does not apply right now (no subtitles to shift, nothing to leave) or Pardeh
    /// cannot do it yet; the key then goes on to whatever else wants it.
    /// </summary>
    internal bool RunKeyCommand(string command)
    {
        var waitingForB = _player.LoopStart is not null && _player.LoopEnd is null;
        AnswerBefore(command);
        var ran = PerformKeyCommand(command);
        AnswerAfter(command, ran, waitingForB);
        return ran;
    }

    private bool PerformKeyCommand(string command)
    {
        switch (command)
        {
            // ---- Playback ----
            case KeyCmd.PlayPause:
                if (_player.PlayState == PlayState.Playing)
                    _player.Pause();
                else
                    _player.Play();
                return true;
            case KeyCmd.Play:
                _player.Play();
                return true;
            case KeyCmd.Pause:
                _player.Pause();
                return true;
            case KeyCmd.Stop:
                _player.Stop();
                return true;
            case KeyCmd.Next:
                _player.Next();
                return true;
            case KeyCmd.Previous:
                _player.Previous();
                return true;
            case KeyCmd.FastForward:
                _player.FastForward();
                return true;
            case KeyCmd.Rewind:
                _player.FastReverse();
                return true;
            case KeyCmd.Shuffle:
                _player.Shuffle = !_player.Shuffle;
                return true;
            case KeyCmd.Repeat:
                _player.Loop = !_player.Loop;
                return true;
            case KeyCmd.AbRepeat:
                _player.CycleSectionLoop();
                return true;

            // ---- Speed ----
            case KeyCmd.Faster:
                _player.StepRate(1);
                return true;
            case KeyCmd.Slower:
                _player.StepRate(-1);
                return true;
            case KeyCmd.NormalSpeed:
                _player.Rate = 1;
                return true;
            case KeyCmd.FasterFine:
                _player.Rate += FineRateStep;
                return true;
            case KeyCmd.SlowerFine:
                _player.Rate -= FineRateStep;
                return true;

            // ---- Seeking ----
            case KeyCmd.BackExtraShort:
                return Seek(-3);
            case KeyCmd.ForwardExtraShort:
                return Seek(3);
            case KeyCmd.BackShort:
                return Seek(-10);
            case KeyCmd.ForwardShort:
                return Seek(10);
            case KeyCmd.BackMedium:
                return Seek(-60);
            case KeyCmd.ForwardMedium:
                return Seek(60);
            case KeyCmd.BackLong:
                return Seek(-300);
            case KeyCmd.ForwardLong:
                return Seek(300);
            case KeyCmd.Back5:
                return Seek(-5);
            case KeyCmd.Forward5:
                return Seek(5);
            case KeyCmd.Back30:
                return Seek(-30);
            case KeyCmd.Forward30:
                return Seek(30);
            case KeyCmd.ShowPosition:
                return _notices?.ShowPosition() == true;
            case KeyCmd.JumpToTime:
                if (_player.CurrentMedia is null)
                    return false;
                _ = AskJumpToTime();
                return true;

            // ---- Audio and subtitles ----
            case KeyCmd.VolumeUp:
                _player.Volume += VolumeStep;
                return true;
            case KeyCmd.VolumeDown:
                _player.Volume -= VolumeStep;
                return true;
            case KeyCmd.Mute:
                _player.Mute = !_player.Mute;
                return true;
            case KeyCmd.NextAudioTrack:
                CycleAudioTrack();
                return true;
            case KeyCmd.SubtitleToggle:
                _player.ShowSubtitles = !_player.ShowSubtitles;
                return true;
            case KeyCmd.SubtitleNext:
                return CycleSubtitle(1);
            case KeyCmd.SubtitlePrevious:
                return CycleSubtitle(-1);
            case KeyCmd.SubtitleEarlier or KeyCmd.SubtitleLater:
                if (_player.Subtitles is null)
                    return false;
                _player.NudgeSubtitleDelay(command == KeyCmd.SubtitleEarlier ? -1 : 1);
                return true;

            // ---- Video ----
            case KeyCmd.FullScreen:
                SetFullScreen(!_fullScreen);
                return true;
            case KeyCmd.LeaveFullScreen:
                if (!_fullScreen)
                    return false;
                SetFullScreen(false);
                return true;
            case KeyCmd.AspectRatio:
                if (!_player.HasVideo)
                    return false;
                SetAspect(null);
                return true;
            case KeyCmd.Crop:
                if (!_player.HasVideo)
                    return false;
                SetAspect(_player.Video.Aspect == "crop" ? "auto" : "crop");
                return true;
            case KeyCmd.Snapshot:
                return SaveVideoPicture();

            // ---- Markers ----
            case KeyCmd.MarkerAdd:
                _player.AddMarker();
                return true;
            case KeyCmd.MarkerList:
                ShowMarkers();
                return true;
            case KeyCmd.MarkerPrevious or KeyCmd.MarkerNext:
                if (_player.CurrentMarkers.Count == 0)
                    return false;
                _player.StepMarker(command == KeyCmd.MarkerPrevious ? -1 : 1);
                return true;

            // ---- Files ----
            case KeyCmd.OpenFile:
                OpenFileWithDialog();
                return true;
            case KeyCmd.OpenFiles:
                _ = OpenSeveralFiles();
                return true;
            case KeyCmd.OpenFolder:
                _ = OpenFolderToPlay();
                return true;
            case KeyCmd.OpenUrl:
                OpenUrl();
                return true;
            case KeyCmd.PasteLocation:
                _ = OpenFromClipboard();
                return true;
            case KeyCmd.SavePlaylist:
                if (_player.CurrentPlaylist.Count == 0)
                    return false;
                _ = SaveCurrentPlaylist();
                return true;
            case KeyCmd.MediaInfo:
                if (_player.CurrentMedia is not { } media || !File.Exists(media.Source) || Shell is null)
                    return false;
                _ = EditTags(media.Source);
                return true;

            // ---- View and window ----
            case KeyCmd.Library:
                if (!IsFullMode)
                    LoadFullMode();
                ShowPane("library");
                return true;
            case KeyCmd.Lyrics:
                ToggleLyricsView();
                return true;
            case KeyCmd.Effects:
                ShowEffects();
                return true;
            case KeyCmd.ShowControls:
                if (!_fullScreen)
                    return false;
                _canvas.Controls.Activity();
                return true;
            case KeyCmd.FullMode:
                RunCommand("fullMode", []);
                return true;
            case KeyCmd.SkinMode:
                RunCommand("skinMode", []);
                return true;
            case KeyCmd.TitleBar:
                RunCommand("toggleTitleBar", []);
                return true;
            case KeyCmd.BarsStyle:
                ToggleBarsStyle();
                return true;
            case KeyCmd.Maximize:
                if (Shell is null || _fullScreen)
                    return false;
                ToggleMaximize();
                return true;
            case KeyCmd.AlwaysOnTop:
                AppSettings.Current.AlwaysOnTop = !AppSettings.Current.AlwaysOnTop;
                AppSettings.Current.Save();
                Topmost = AppSettings.Current.AlwaysOnTop;
                return true;
            case KeyCmd.MiniWidget:
                if (MiniWidgetToggle is not { } toggle)
                    return false;
                toggle();
                return true;
            case KeyCmd.ZoomIn:
                StepSkinZoom(1);
                return true;
            case KeyCmd.ZoomOut:
                StepSkinZoom(-1);
                return true;
            case KeyCmd.ZoomReset:
                StepSkinZoom(0);
                return true;

            // ---- Application ----
            case KeyCmd.Preferences:
                ShowSettingsPage(null);
                return true;
            case KeyCmd.About:
                ShowAbout();
                return true;
            case KeyCmd.Quit:
                if (Shell is null)
                    return false;
                Close();
                return true;

            default:
                return RunBookmarkCommand(command);
        }
    }

    private bool Seek(double seconds)
    {
        _player.SeekBy(seconds);
        return true;
    }

    /// <summary>The Options dialog (or the phone looks' settings page), opened on <paramref name="page"/> when it has one.</summary>
    private void ShowSettingsPage(string? page)
    {
        if (Shell is null)
        {
            ShowPane("skins");
            return;
        }

        Rendering.OptionsDialog.StartPage = page;
        _ = ShowOptions();
    }

    /// <summary>Ctrl+E: the equalizer, wherever the look showing keeps it.</summary>
    private void ShowEffects()
    {
        if (PhoneLookShowing && IsFullMode)
            ShowPhoneTools();
        else if (IsFullMode && _session?.Package.SourcePath == OriginalFullModeSource)
            _session?.ShowEnhancement("EQView");
        else
            ShowSettingsPage("Playback");
    }

    // ---- Subtitles --------------------------------------------------------------------------------

    /// <summary>The next or previous subtitles: off, each file next to the video, each track inside it, and round again.</summary>
    private bool CycleSubtitle(int direction)
    {
        var files = SubtitleFilesNextToMedia();
        var embedded = _player.SubtitleTracks;
        var count = 1 + files.Count + embedded.Count; // 0 is "off"
        if (count == 1)
            return false;

        var current = 0;
        if (_player.ShowSubtitles && _player.Subtitles is { } shown)
        {
            var file = files.FindIndex(f => string.Equals(f, shown.Path, StringComparison.OrdinalIgnoreCase));
            if (file >= 0)
                current = 1 + file;
            for (var i = 0; i < embedded.Count; i++)
            {
                if (_player.EmbeddedSubtitle == embedded[i])
                    current = 1 + files.Count + i;
            }
        }

        var next = (current + direction + count) % count;
        if (next == 0)
            _player.ShowSubtitles = false;
        else if (next <= files.Count)
        {
            if (_player.LoadSubtitles(files[next - 1]))
                _player.ShowSubtitles = true;
        }
        else
        {
            _ = SelectSubtitleTrack(embedded[next - 1 - files.Count]);
        }

        return true;
    }

    // ---- Playlist bookmarks -----------------------------------------------------------------------

    /// <summary>VLC's playlist bookmarks: Ctrl+F1 remembers the playing item, F1 plays it again (kept between runs).</summary>
    private bool RunBookmarkCommand(string command)
    {
        for (var number = 1; number <= KeyCmd.BookmarkCount; number++)
        {
            if (command == KeyCmd.SetBookmark(number))
            {
                if (_player.CurrentMedia is not { } media)
                    return false;
                AppSettings.Current.PlaylistBookmarks[number] = media.Source;
                AppSettings.Current.Save();
                NoticeBookmark(number, set: true);
                return true;
            }

            if (command != KeyCmd.PlayBookmark(number))
                continue;
            if (!AppSettings.Current.PlaylistBookmarks.TryGetValue(number, out var source))
            {
                NoticeBookmark(number, set: false);
                return false;
            }

            var items = _player.CurrentPlaylist.Items;
            for (var i = 0; i < items.Count; i++)
            {
                if (string.Equals(items[i].Source, source, StringComparison.OrdinalIgnoreCase))
                {
                    _player.PlayItem(i);
                    return true;
                }
            }

            _player.OpenFiles([source]);
            return true;
        }

        return false;
    }

    // ---- Commands that ask or pick ----------------------------------------------------------------

    private async Task AskJumpToTime()
    {
        var media = _player.CurrentMedia;
        var answer = await AskAsync("Jump to time (1:30, 1:02:03 or seconds)", MediaPlayer.FormatTime(_player.CurrentPosition));
        if (answer is not null && ReferenceEquals(media, _player.CurrentMedia) && JumpTime.TryParse(answer, out var seconds))
            _player.CurrentPosition = seconds;
    }

    private async Task OpenSeveralFiles()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open files", AllowMultiple = true });
        var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0)
            _player.OpenFiles(paths);
    }

    private async Task OpenFolderToPlay()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Play a folder" });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } folder)
            return;
        List<string> media;
        try
        {
            media = await Task.Run(() => Directory
                .EnumerateFiles(folder, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Where(f => MediaLibrary.AudioExtensions.Contains(Path.GetExtension(f)) || MediaLibrary.VideoExtensions.Contains(Path.GetExtension(f)))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage("That folder could not be read.");
            return;
        }

        if (media.Count > 0)
            _player.OpenFiles(media);
        else
            ShowMessage("That folder has no music or video.");
    }

    private async Task OpenFromClipboard()
    {
        var text = (await ReadClipboard())?.Trim().Trim('"');
        if (text is not { Length: > 0 } || text.Contains('\n'))
            return;
        // A network address goes the way of Open URL: it is remembered, and a playlist address is downloaded.
        if (NetworkMedia.IsNetwork(text))
            await OpenAddress(text);
        else if (text.Contains("://") || File.Exists(text))
            _player.OpenFiles([text]);
    }

    private async Task SaveCurrentPlaylist()
    {
        var sources = _player.CurrentPlaylist.Items.Select(i => i.Source).ToList();
        if ((await AskAsync("Save the playlist as", "My playlist"))?.Trim() is not { Length: > 0 } name)
            return;
        try
        {
            _playlists.Save(name, sources);
            _session?.OnLibraryChanged();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage("The playlist could not be saved.");
        }
    }

    // ---- A picture of the video -------------------------------------------------------------------

    /// <summary>Shift+S: the frame showing, as a PNG in Pictures\Pardeh (the decoded picture, without subtitles or colour settings).</summary>
    private bool SaveVideoPicture()
    {
        if (!_player.HasVideo || _player.CurrentVideoFrame is not { } frame)
            return false;
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (pictures.Length == 0)
            return false;
        try
        {
            var folder = Path.Combine(pictures, "Pardeh");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, $"pardeh-{DateTime.Now:yyyy-MM-dd-HHmmss-fff}.png");
            using var bitmap = new WriteableBitmap(new PixelSize(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var buffer = bitmap.Lock())
            {
                for (var y = 0; y < frame.Height; y++)
                    Marshal.Copy(frame.Pixels, y * frame.Width * 4, buffer.Address + y * buffer.RowBytes, frame.Width * 4);
            }

            bitmap.Save(file, PngBitmapEncoderOptions.Default);
            _player.Announce("Picture saved to " + file);
            Log("Video picture saved to " + file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage("The picture could not be saved.");
        }

        return true;
    }
}
