using System.Globalization;
using Pardeh.Core.Playback;

namespace Pardeh.Core.Notices;

/// <summary>Messages a caller asks for, the state that is compared, and how values are written.</summary>
public sealed partial class PlayerNotices
{
    // ---- What a command can ask for ------------------------------------------------------------------------

    /// <summary>A short answer to a command: "No subtitles", "Always on top: On".</summary>
    public void Say(string key, NoticeCategory category, NoticeIcon icon, string title, string? value = null, string? detail = null,
        double seconds = Notice.NormalSeconds) =>
        _center.Post(new Notice(key, category, icon, title) { Value = value, Detail = detail, Seconds = seconds });

    /// <summary>The volume as it is now (also after a command that could not change it).</summary>
    public void ShowVolume()
    {
        var volume = (int)Math.Round(_player.Volume);
        var silent = _player.Mute || volume == 0;
        _center.Post(new Notice(VolumeKey, NoticeCategory.Sound, silent ? NoticeIcon.Muted : NoticeIcon.Volume, _player.Mute ? "Muted" : "Volume")
        {
            Value = string.Create(CultureInfo.InvariantCulture, $"{volume}%"),
            Level = volume / 100.0,
            Seconds = 1.6,
        });
    }

    /// <summary>"Show position": elapsed, remaining, and when it will end at this speed.</summary>
    public bool ShowPosition(DateTime? clockNow = null)
    {
        if (_player.CurrentMedia is not { } media || _player.OpenState != OpenState.MediaOpen)
            return false;
        var timeline = Timeline();
        var ends = timeline.Duration > 0
            ? "Ends at " + (clockNow ?? DateTime.Now).AddSeconds(timeline.Remaining / _player.Rate).ToString("HH:mm", CultureInfo.InvariantCulture)
            : null;
        var chapter = ChapterAt(timeline.Position);
        _center.Post(new Notice(SeekKey, NoticeCategory.Position, NoticeIcon.Info, media.Name)
        { Timeline = timeline, Detail = chapter is null ? ends : ends is null ? chapter : $"{chapter} · {ends}", Seconds = 3 });
        _seekTotal = 0;
        _lastSeekKind = SeekKind.Silent;
        return true;
    }

    public void ShowAudioTrack()
    {
        var tracks = _player.AudioTracks;
        var index = _player.CurrentAudioTrack;
        if (index < 0 || index >= tracks.Count)
            return;
        _center.Post(new Notice("audioTrack", NoticeCategory.Tracks, NoticeIcon.AudioTrack, "Audio track")
        {
            Detail = tracks[index].Label,
            Value = tracks.Count > 1 ? string.Create(CultureInfo.InvariantCulture, $"{index + 1} of {tracks.Count}") : null,
            Seconds = 3,
        });
    }

    public void ShowSubtitleDelay() =>
        _center.Post(new Notice("subtitleDelay", NoticeCategory.Tracks, NoticeIcon.Subtitles, "Subtitle delay")
        { Value = MediaPlayer.DescribeDelay(_player.SubtitleDelay) });

    public void ShowAspect() =>
        _center.Post(new Notice("aspect", NoticeCategory.Picture, NoticeIcon.Picture, "Aspect ratio")
        { Value = VideoSettings.Aspects.FirstOrDefault(a => a.Key == _player.Video.Aspect).Name });

    /// <summary>A place being aimed at (a finger swiping on the picture); it stays until <see cref="EndAim"/>.</summary>
    public void Aim(double from, double to)
    {
        if (IsQuiet)
            return;
        var notice = new Notice(AimKey, NoticeCategory.Position, to < from ? NoticeIcon.Rewind : NoticeIcon.FastForward, Delta(to - from))
        { Timeline = new NoticeTimeline(to, _player.Duration), Follows = false, Held = true };
        if (_center.Showing(AimKey) is { } showing)
            _center.Refresh(notice with { Held = showing.Held });
        else
            _center.Post(notice);
    }

    public void EndAim() => _center.Release(AimKey);

    // ---- Writing values -----------------------------------------------------------------------------------

    /// <summary>"+10 s", "-1:05"</summary>
    public static string Delta(double seconds)
    {
        var sign = seconds < 0 ? "-" : "+";
        var size = Math.Abs(seconds);
        return size < 59.5
            ? sign + Math.Round(size).ToString("0", CultureInfo.InvariantCulture) + " s"
            : sign + Short(Math.Round(size));

        static string Short(double s)
        {
            var text = MediaPlayer.FormatTime(s);
            return text.StartsWith('0') ? text[1..] : text;
        }
    }

    public static string FormatRate(double rate) => string.Create(CultureInfo.InvariantCulture, $"{rate:0.##}×");

    private static string OnOff(bool on) => on ? "On" : "Off";

    private NoticeTimeline Timeline() => new(_player.CurrentPosition, _player.Duration, _player.LoopStart, _player.LoopEnd);

    private string? ChapterAt(double seconds)
    {
        var chapters = _player.Chapters;
        for (var i = chapters.Count - 1; i >= 0; i--)
        {
            if (chapters[i].Start.TotalSeconds <= seconds + 0.05)
                return string.IsNullOrWhiteSpace(chapters[i].Title) ? string.Create(CultureInfo.InvariantCulture, $"Chapter {i + 1}") : chapters[i].Title;
        }

        return null;
    }

    // ---- The state compared on each tick -------------------------------------------------------------------

    private readonly record struct State(
        PlayState? Play, bool Shuffle, bool Loop, double Rate, bool Gapless, ReplayGainMode ReplayGain, bool ResumeVideos,
        int Volume, bool Mute, double Balance,
        bool EqualizerOff, int Preset, bool CrossFade, double CrossFadeSeconds, bool AutoVolume, int Quiet, bool Enhanced,
        int AudioTrack, int AudioTrackCount, string? Subtitle, TimeSpan SubtitleDelay,
        double? LoopStart, double? LoopEnd, int QueueCount, bool Favorite,
        string Aspect, double Brightness, double Contrast, double Hue, double Saturation,
        TimeSpan? Sleep, bool SleepAtEnd);

    private State Capture()
    {
        var eq = _player.Equalizer;
        var video = _player.Video;
        var play = _player.PlayState is PlayState.Playing or PlayState.Paused or PlayState.Stopped or PlayState.Ready
            or PlayState.ScanForward or PlayState.ScanReverse ? _player.PlayState : (PlayState?)null;
        return new State(
            play, _player.Shuffle, _player.Loop, _player.Rate, _player.Gapless, _player.ReplayGain, _player.ResumeVideos,
            (int)Math.Round(_player.Volume), _player.Mute, Math.Round(_player.Balance),
            eq.Bypass, eq.CurrentPreset, eq.CrossFade, eq.CrossFadeSeconds, eq.AutoVolume, eq.QuietMode, eq.Enhanced,
            _player.CurrentAudioTrack, _player.AudioTracks.Count, ShownSubtitle(), _player.SubtitleDelay,
            _player.LoopStart, _player.LoopEnd, _player.CurrentPlaylist.Count, _player.CurrentIsFavorite,
            video.Aspect, video.Brightness, video.Contrast, video.Hue, video.Saturation,
            _player.SleepRemaining, _player.SleepAtEndOfTrack);
    }

    /// <summary>The name of the subtitles showing, or null when none show.</summary>
    private string? ShownSubtitle()
    {
        if (!_player.ShowSubtitles || _player.Subtitles is not { } subtitles)
            return null;
        if (_player.EmbeddedSubtitle is { } track)
            return track.Label;
        return Path.GetFileName(subtitles.Path) is { Length: > 0 } name ? name : subtitles.Path;
    }
}
