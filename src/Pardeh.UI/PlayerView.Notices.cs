using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Pardeh.Core.Input;
using Pardeh.Core.Notices;
using Pardeh.Core.Playback;
using Pardeh.Skinning.Runtime;
using Pardeh.Skinning.Runtime.Elements;
using Pardeh.UI.Rendering;

namespace Pardeh.UI;

/// <summary>
/// On-screen messages: <see cref="PlayerNotices"/> watches the player; this file adds what only the window knows (full
/// screen, always on top, skin size, lyrics timing, a swipe), answers commands that change nothing, and places the card.
/// </summary>
internal sealed partial class PlayerView
{
    private readonly NoticeCenter _noticeCenter = new();
    private readonly NoticeLayer _noticeLayer = new();
    private PlayerNotices? _notices;
    private WindowFacts _facts;
    private TimeSpan? _snapshotKeysAt;
    private int _noticeVersion;

    /// <summary>What the window shows messages about.</summary>
    private readonly record struct WindowFacts(bool FullScreen, bool OnTop, double Zoom, object? Lyrics, int LyricsOffset, int Transpose);

    private void StartNotices()
    {
        _notices = new PlayerNotices(_player, _noticeCenter) { FullScreen = () => _fullScreen };
        _noticeCenter.Allowed = category =>
        {
            var settings = AppSettings.Current;
            return settings.OnScreenMessages
                && (category is NoticeCategory.Problem or NoticeCategory.SongTitle || !settings.HiddenNotices.Contains(category.ToString()));
        };
        _facts = ReadWindowFacts();
        HushNotices(1.5);
    }

    /// <summary>The start and a new skin set the volume and the modes by themselves: that is not news.</summary>
    private void HushNotices(double seconds)
    {
        // The card showing stays, and what the window itself was asked to do is still told: a new skin size loads the skin again.
        _notices?.Hush(seconds);
    }

    private WindowFacts ReadWindowFacts() => new(_fullScreen, AppSettings.Current.AlwaysOnTop, AppSettings.Current.SkinZoom,
        _lyrics.Current, _lyrics.OffsetMs, _lyrics.Current?.Transpose ?? 0);

    /// <summary>Each tick, after the player's.</summary>
    private void UpdateNotices(TimeSpan now)
    {
        if (_notices is null)
            return;
        // Nobody sees a hidden or minimized window; what changes meanwhile must not show up late.
        if (!AppVisibility.Foreground || Shell is { WindowState: WindowState.Minimized })
        {
            HushNotices(0.3);
            _noticeCenter.Clear();
            _noticeLayer.Update(null, 0, default);
            return;
        }

        var settings = AppSettings.Current;
        if (!settings.OnScreenMessages)
            _noticeCenter.Clear();
        _noticeCenter.DurationScale = settings.NoticeTime switch { "Short" => 0.6, "Long" => 2, _ => 1 };
        _notices.SongTitle = Enum.TryParse<SongTitleNotices>(settings.NoticeSongTitle, out var when) ? when : SongTitleNotices.FullScreenAndVideo;
        RunSnapshotKeys(now); // before the comparisons, as a real key press comes between ticks

        // Window messages come first: whatever the player has to say at the same time is the one that stays.
        CompareWindowFacts(somethingSaid: _noticeCenter.Version != _noticeVersion);
        _notices.Update();
        if (_canvas.SwipeAim is { } aim)
            _notices.Aim(aim.From, aim.To);
        else
            _notices.EndAim();

        var notice = _noticeCenter.Current;
        _noticeVersion = _noticeCenter.Version;
        _noticeLayer.Update(notice, _noticeCenter.Opacity, notice is null ? default : NoticeFrameNow());
    }

    private void CompareWindowFacts(bool somethingSaid)
    {
        var old = _facts;
        var now = _facts = ReadWindowFacts();
        if (old.FullScreen != now.FullScreen)
        {
            if (now.FullScreen)
            {
                // A hint only: it gives way to what was said since the last tick.
                if (!somethingSaid && FullScreenHint() is { } hint)
                    _notices!.Say("fullScreen", NoticeCategory.Picture, NoticeIcon.FullScreen, "Full screen", detail: hint, seconds: 2.5);
            }
            else if (_noticeCenter.Showing("fullScreen") is not null)
            {
                _noticeCenter.Clear();
            }
        }

        if (old.OnTop != now.OnTop)
            _notices!.Say("onTop", NoticeCategory.Picture, NoticeIcon.Window, "Always on top", now.OnTop ? "On" : "Off");
        if (Math.Abs(old.Zoom - now.Zoom) > 1e-6)
            _notices!.Say("zoom", NoticeCategory.Picture, NoticeIcon.Window, "Skin size", string.Create(CultureInfo.InvariantCulture, $"{now.Zoom * 100:0}%"));

        // Only a change to the same lyrics is the user's; new lyrics bring their own saved values.
        if (now.Lyrics is null || !ReferenceEquals(old.Lyrics, now.Lyrics))
            return;
        if (old.LyricsOffset != now.LyricsOffset)
            _notices!.Say("lyricsTiming", NoticeCategory.Tracks, NoticeIcon.Lyrics, "Lyrics timing",
                MediaPlayer.DescribeDelay(TimeSpan.FromMilliseconds(now.LyricsOffset)));
        if (old.Transpose != now.Transpose)
            _notices!.Say("transpose", NoticeCategory.Tracks, NoticeIcon.Lyrics, "Chords transposed",
                now.Transpose.ToString("+0;-0;0", CultureInfo.InvariantCulture));
    }

    private static string? FullScreenHint()
    {
        if (OperatingSystem.IsAndroid())
            return "Back leaves full screen";
        var keys = Keymaps.Current.ChordsFor(KeyCmd.LeaveFullScreen);
        if (keys.Count == 0)
            keys = Keymaps.Current.ChordsFor(KeyCmd.FullScreen);
        return keys.Count > 0 ? $"Press {keys[0]} to leave" : null;
    }

    // ---- Commands that need an answer ---------------------------------------------------------------------

    /// <summary>What there is to say before a command runs: it is about to find nothing to do.</summary>
    private void AnswerBefore(string command)
    {
        if (_notices is null || _player.CurrentMedia is null)
            return;
        switch (command)
        {
            case KeyCmd.Next when !_player.CurrentPlaylist.HasNext:
                _notices.Say("noNext", NoticeCategory.Playback, NoticeIcon.Next, "No next item");
                break;
            case KeyCmd.NextAudioTrack when _player.AudioTracks.Count < 2:
                _notices.Say("audioTrack", NoticeCategory.Tracks, NoticeIcon.AudioTrack, "Only one audio track");
                break;
            case KeyCmd.SubtitleToggle when _player.Subtitles is null:
                NoSubtitles();
                break;
            case KeyCmd.AspectRatio or KeyCmd.Crop or KeyCmd.Snapshot when !_player.HasVideo:
                _notices.Say("noVideo", NoticeCategory.Picture, NoticeIcon.Video, "No video");
                break;
            case KeyCmd.MarkerPrevious or KeyCmd.MarkerNext when _player.CurrentMarkers.Count == 0:
                _notices.Say("noMarker", NoticeCategory.Library, NoticeIcon.Marker, "No markers");
                break;
        }
    }

    /// <summary>What there is to say after a command ran, or could not.</summary>
    private void AnswerAfter(string command, bool ran, bool wasWaitingForB)
    {
        if (_notices is null)
            return;
        switch (command)
        {
            // Also at 0 % and 100 %, where nothing changes.
            case KeyCmd.VolumeUp or KeyCmd.VolumeDown:
                _notices.ShowVolume();
                break;
            case KeyCmd.Faster or KeyCmd.Slower or KeyCmd.NormalSpeed or KeyCmd.FasterFine or KeyCmd.SlowerFine:
                _notices.Say("speed", NoticeCategory.Playback, NoticeIcon.Speed, "Speed", PlayerNotices.FormatRate(_player.Rate));
                break;
            case KeyCmd.SubtitleNext or KeyCmd.SubtitlePrevious or KeyCmd.SubtitleEarlier or KeyCmd.SubtitleLater when !ran:
                NoSubtitles();
                break;
            case KeyCmd.MarkerPrevious or KeyCmd.MarkerNext when !ran && _player.CurrentMarkers.Count > 0:
                _notices.Say("noMarker", NoticeCategory.Library, NoticeIcon.Marker,
                    command == KeyCmd.MarkerNext ? "No later marker" : "No earlier marker");
                break;
            case KeyCmd.AbRepeat when wasWaitingForB && _player.LoopStart is not null && _player.LoopEnd is null:
                _notices.Say("abRepeat", NoticeCategory.Playback, NoticeIcon.Repeat, "A-B repeat: B must come after A");
                break;
            default:
                // Disc keys are left out: Enter is one, and it is pressed for many things.
                if (!ran && _player.CurrentMedia is not null && !command.StartsWith("disc.", StringComparison.Ordinal)
                    && KeyCommandCatalog.Find(command) is { Supported: false } missing)
                    _notices.Say("notYet", NoticeCategory.Playback, NoticeIcon.Info, "Not in Pardeh yet", detail: missing.Name, seconds: 2.5);
                break;
        }
    }

    private void NoSubtitles()
    {
        if (_player.CurrentMedia is not null)
            _notices?.Say("subtitles", NoticeCategory.Tracks, NoticeIcon.Subtitles, "No subtitles");
    }

    private void NoticeBookmark(int number, bool set) => _notices?.Say("bookmark", NoticeCategory.Library, NoticeIcon.Marker,
        string.Create(CultureInfo.InvariantCulture, $"Playlist bookmark {number} {(set ? "set" : "is empty")}"),
        detail: set ? _player.CurrentMedia?.Name : null, seconds: 2.5);

    /// <summary>Reads an embedded subtitle track; that takes a pass over the file, so it is said while it happens.</summary>
    private async Task<bool> SelectSubtitleTrack(SubtitleTrack track)
    {
        const string key = "subtitleLoading";
        var media = _player.CurrentMedia;
        _noticeCenter.Post(new Notice(key, NoticeCategory.Tracks, NoticeIcon.Subtitles, "Loading subtitles…") { Detail = track.Label, Held = true });
        var loaded = await _player.SelectSubtitleTrackAsync(track);
        // A later choice has its own message under the same key: only the last one to finish lets it go.
        _noticeCenter.Release(key);
        if (!loaded && ReferenceEquals(media, _player.CurrentMedia))
            _notices?.Say("subtitles", NoticeCategory.Tracks, NoticeIcon.Subtitles, "No subtitles in that track", detail: track.Label);
        return loaded;
    }

    // ---- Where the card goes ------------------------------------------------------------------------------

    private NoticeFrame NoticeFrameNow()
    {
        var settings = AppSettings.Current;
        var place = Enum.TryParse<NoticePlace>(settings.NoticePlace, out var chosen) ? chosen : NoticePlace.Top;
        var accent = IsFullMode && CurrentFullModeKey().StartsWith("ember", StringComparison.Ordinal)
            ? Color.FromRgb(229, 128, 31)
            : Color.FromRgb(76, 194, 255);
        var whole = new Rect(_noticeLayer.Bounds.Size);
        if (_fullScreen || _session?.CurrentView is not { } view)
        {
            var controls = _fullScreen && _canvas.Controls.IsShown ? 72 : 0;
            return new NoticeFrame(whole, place, accent, Math.Clamp(whole.Height / 720, 1, 2), 0, controls);
        }

        var root = new Rect(0, 0, view.Root.Width, view.Root.Height);
        var picture = PictureArea(view).Intersect(root);
        var area = picture is { Width: >= 200, Height: >= 100 } ? picture : root;
        // A skin's title bar is along its top edge.
        var top = area.Height > 220 ? Math.Max(0, 30 - area.Y) : 0;
        return new NoticeFrame(area, place, accent, 1, top);
    }

    /// <summary>The largest video or visualization showing, in the view's units (empty when there is none).</summary>
    private static Rect PictureArea(SkinView view)
    {
        var best = default(Rect);
        Look(view);
        return best;

        void Look(SkinView inner)
        {
            foreach (var element in inner.Elements)
            {
                if (element is HostAreaElement { Embedded: { } pane } && Shown(element))
                    Look(pane);
                if (element is not (VideoElement or EffectsElement) || element.Width * element.Height <= best.Width * best.Height || !Shown(element))
                    continue;
                var (x, y) = element.AbsolutePosition;
                best = new Rect(x, y, element.Width, element.Height);
            }
        }

        static bool Shown(SkinElement element)
        {
            for (var e = element; e is not null; e = e.Parent)
            {
                if (!e.Visible)
                    return false;
            }

            return true;
        }
    }

    /// <summary>The picture waits for the commands of PARDEH_SNAPSHOT_KEYS and for their message to have appeared.</summary>
    private bool AwaitingSnapshotKeys(TimeSpan now) => Environment.GetEnvironmentVariable("PARDEH_SNAPSHOT_KEYS") is { Length: > 0 }
        && (_snapshotKeysAt is not { } ran || now - ran < TimeSpan.FromMilliseconds(500));

    /// <summary>
    /// Development aid for snapshots: PARDEH_SNAPSHOT_KEYS="audio.volumeUp,seek.forward30" runs those keymap commands half a
    /// second before the picture is taken, so that it shows their message.
    /// </summary>
    private void RunSnapshotKeys(TimeSpan now)
    {
        if (_snapshotKeysAt is not null || Environment.GetEnvironmentVariable("PARDEH_SNAPSHOT_KEYS") is not { Length: > 0 } keys)
            return;
        var delay = double.TryParse(Environment.GetEnvironmentVariable("PARDEH_SNAPSHOT_DELAY"), CultureInfo.InvariantCulture, out var d) ? d : 3;
        if (now.TotalSeconds < delay - 0.5)
            return;
        _snapshotKeysAt = now;
        foreach (var key in keys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            RunKeyCommand(key);
    }
}
