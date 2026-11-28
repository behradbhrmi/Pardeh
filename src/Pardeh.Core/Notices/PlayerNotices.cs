using System.Globalization;
using Pardeh.Core.Playback;

namespace Pardeh.Core.Notices;

/// <summary>When a new track's title is shown.</summary>
public enum SongTitleNotices
{
    /// <summary>In full screen, and with a video, where the skin shows no title.</summary>
    FullScreenAndVideo,
    Always,
    Never,
}

/// <summary>
/// Turns what happens to the player into on-screen messages. It compares the player's state on every tick instead of being
/// told by each button, so a key, a skin button, a skin script, a menu, a settings page and a headset button all show the
/// same message. What can only be seen as it happens (a seek, an error) comes from the player's events.
/// <para>A command that changes nothing (volume up at 100 %, no subtitles to show) is answered by the caller, with the
/// <c>Show…</c> methods and <see cref="Say"/>. Used from the player's thread.</para>
/// </summary>
public sealed partial class PlayerNotices : IDisposable
{
    public const string VolumeKey = "volume";
    public const string SeekKey = "seek";
    public const string StateKey = "state";
    public const string ScanKey = "scan";
    public const string AimKey = "aim";

    /// <summary>Seeks this close together are one drag of the seek bar.</summary>
    private const long DragGapMs = 900;

    private const double SleepWarningSeconds = 60;

    private readonly MediaPlayer _player;
    private readonly NoticeCenter _center;
    private readonly Func<long> _clock;
    private State _state;
    private long _quietUntil;
    private bool _newItem, _ended, _failed, _markerJumped;
    private double _seekTotal, _dragFrom, _scanFrom;
    private long _lastSeek = long.MinValue / 2;
    private SeekKind _lastSeekKind;

    public PlayerNotices(MediaPlayer player, NoticeCenter center, Func<long>? clock = null)
    {
        _player = player;
        _center = center;
        _clock = clock ?? (static () => Environment.TickCount64);
        _state = Capture();
        _player.Seeked += OnSeeked;
        _player.CurrentMediaChanged += OnCurrentMediaChanged;
        _player.PlayStateChanged += OnPlayStateChanged;
        _player.Error += OnError;
        _player.Announced += OnAnnounced;
        _player.MarkerAdded += OnMarkerAdded;
    }

    public NoticeCenter Center => _center;

    /// <summary>Whether the player fills the screen (for <see cref="SongTitle"/>).</summary>
    public Func<bool> FullScreen { get; set; } = static () => false;

    public SongTitleNotices SongTitle { get; set; } = SongTitleNotices.FullScreenAndVideo;

    private bool IsQuiet => _clock() < _quietUntil;

    /// <summary>
    /// No messages for a while, and whatever changes meanwhile is taken as it is: at the start, while a skin loads (its script
    /// sets the volume and the modes), while the window is hidden.
    /// </summary>
    public void Hush(double seconds)
    {
        _quietUntil = Math.Max(_quietUntil, _clock() + (long)(seconds * 1000));
        Forget();
    }

    private void Forget()
    {
        _state = Capture();
        _newItem = _ended = _failed = _markerJumped = false;
    }

    // ---- Each tick ----------------------------------------------------------------------------------------

    /// <summary>Call after the player's own tick.</summary>
    public void Update()
    {
        if (IsQuiet)
        {
            Forget();
            return;
        }

        var old = _state;
        var now = Capture();
        // Opening is not over (a stream is connecting), or a file is let go while it is changed: the play state in between
        // says nothing.
        if (now.Play is null || _player.IsFileReleased)
            now = now with { Play = old.Play };
        _state = now;

        var newItem = _newItem;
        var ended = _ended;
        var failed = _failed;
        var markerJumped = _markerJumped;
        _newItem = _markerJumped = false;
        if (now.Play is PlayState.Stopped or PlayState.Ready or PlayState.Playing or PlayState.Paused)
            _ended = _failed = false;

        // The order matters: when several things change at once, the last message posted is the one that stays.
        if (!failed)
        {
            if (newItem)
                NewItem();
            else
                CompareTransport(old, now, ended);
        }

        CompareModes(old, now);
        CompareSound(old, now);
        if (!newItem && !_player.IsConnecting)
        {
            // What a new track resets or picks for itself is not news.
            CompareTracks(old, now);
            CompareSection(old, now, markerJumped);
            CompareLibrary(old, now);
        }

        ComparePicture(old, now);
        CompareSleep(old, now, newItem, ended);
        FollowTimeline();
    }

    private void CompareTransport(State old, State now, bool ended)
    {
        if (old.Play == now.Play)
            return;
        var scanning = now.Play is PlayState.ScanForward or PlayState.ScanReverse;
        if (old.Play is PlayState.ScanForward or PlayState.ScanReverse && !scanning)
            _center.Release(ScanKey);

        switch (now.Play)
        {
            case PlayState.ScanForward or PlayState.ScanReverse:
                _scanFrom = _player.CurrentPosition;
                var forward = now.Play == PlayState.ScanForward;
                _center.Post(new Notice(ScanKey, NoticeCategory.Playback, forward ? NoticeIcon.FastForward : NoticeIcon.Rewind,
                    forward ? "Fast forward 5×" : "Rewind 5×") { Timeline = Timeline(), Held = true });
                break;
            case PlayState.Playing when old.Play is PlayState.ScanForward or PlayState.ScanReverse:
                _center.Post(StateNotice("Play", NoticeIcon.Play, Delta(_player.CurrentPosition - _scanFrom)));
                break;
            case PlayState.Playing:
                _center.Post(StateNotice("Play", NoticeIcon.Play));
                break;
            case PlayState.Paused:
                _center.Post(StateNotice("Pause", NoticeIcon.Pause));
                break;
            case PlayState.Stopped when old.Play is not (null or PlayState.Ready):
                _center.Post(ended
                    ? new Notice(StateKey, NoticeCategory.Playback, NoticeIcon.Stop, "End of the queue") { Seconds = 3, Automatic = true }
                    : new Notice(StateKey, NoticeCategory.Playback, NoticeIcon.Stop, "Stop"));
                break;
        }
    }

    private Notice StateNotice(string title, NoticeIcon icon, string? value = null) =>
        new(StateKey, NoticeCategory.Playback, icon, title) { Timeline = Timeline(), Value = value };

    private void NewItem()
    {
        if (_player.CurrentMedia is not { } item)
            return;
        var resumed = _player.ResumedAt;
        if (SongTitle != SongTitleNotices.Never && (SongTitle == SongTitleNotices.Always || FullScreen() || _player.HasVideo))
        {
            var details = new[] { item.GetAttribute("Author"), item.GetAttribute("Album") }.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
            var count = _player.CurrentPlaylist.Count;
            if (_center.Post(new Notice("title", NoticeCategory.SongTitle, _player.HasVideo ? NoticeIcon.Video : NoticeIcon.Music, item.Name)
            {
                Detail = resumed is { } at ? $"Resumed at {MediaPlayer.FormatTime(at)}" : details.Count > 0 ? string.Join(" · ", details) : null,
                Value = count > 1 && _player.CurrentPlaylist.CurrentIndex >= 0 ? $"{_player.CurrentPlaylist.CurrentIndex + 1} of {count}" : null,
                Seconds = 3.5,
                Automatic = true,
            }))
                return;
        }

        if (resumed is { } place)
        {
            _center.Post(new Notice(SeekKey, NoticeCategory.Position, NoticeIcon.Play, $"Resumed at {MediaPlayer.FormatTime(place)}")
            { Timeline = Timeline(), Seconds = 3, Automatic = true });
        }
    }

    private void CompareModes(State old, State now)
    {
        if (old.Shuffle != now.Shuffle)
            Mode("shuffle", NoticeIcon.Shuffle, "Shuffle", OnOff(now.Shuffle));
        if (old.Loop != now.Loop)
            Mode("repeat", NoticeIcon.Repeat, "Repeat", OnOff(now.Loop));
        if (old.Rate != now.Rate)
            Mode("speed", NoticeIcon.Speed, "Speed", FormatRate(now.Rate));
        if (old.Gapless != now.Gapless)
            Mode("gapless", NoticeIcon.Music, "Gapless playback", OnOff(now.Gapless));
        if (old.ReplayGain != now.ReplayGain)
            Mode("levelling", NoticeIcon.Volume, "Volume levelling", now.ReplayGain switch
            {
                ReplayGainMode.Track => "Each track",
                ReplayGainMode.Album => "Albums",
                _ => "Off",
            });
        if (old.ResumeVideos != now.ResumeVideos)
            Mode("resume", NoticeIcon.Video, "Resume videos", OnOff(now.ResumeVideos));
    }

    private void Mode(string key, NoticeIcon icon, string title, string value) =>
        _center.Post(new Notice(key, NoticeCategory.Playback, icon, title) { Value = value });

    private void CompareSound(State old, State now)
    {
        if (old.Volume != now.Volume || old.Mute != now.Mute)
            ShowVolume();
        if (old.Balance != now.Balance)
        {
            _center.Post(new Notice("balance", NoticeCategory.Sound, NoticeIcon.Balance, "Balance")
            {
                Value = Math.Abs(now.Balance) < 0.5 ? "Centre" : string.Create(CultureInfo.InvariantCulture, $"{Math.Abs(now.Balance):0} {(now.Balance < 0 ? "left" : "right")}"),
                Level = (now.Balance + 100) / 200,
                Centered = true,
                Seconds = 1.6,
            });
        }

        // Choosing a preset also turns the equalizer on: the preset is the news then.
        if (old.Preset != now.Preset)
            Sound("equalizer", "Equalizer", EqualizerSettings.Presets[now.Preset].Name);
        else if (old.EqualizerOff != now.EqualizerOff)
            Sound("equalizer", "Equalizer", OnOff(!now.EqualizerOff));
        if (old.CrossFade != now.CrossFade)
            Sound("crossfade", "Crossfading", OnOff(now.CrossFade));
        else if (old.CrossFadeSeconds != now.CrossFadeSeconds && now.CrossFade)
            Sound("crossfade", "Crossfade overlap", string.Create(CultureInfo.InvariantCulture, $"{now.CrossFadeSeconds:0} s"));
        if (old.AutoVolume != now.AutoVolume)
            Sound("autoVolume", "Auto volume leveling", OnOff(now.AutoVolume));
        if (old.Quiet != now.Quiet)
            Sound("quietMode", "Quiet mode", now.Quiet switch { 1 => "Medium difference", 2 => "Little difference", _ => "Off" });
        if (old.Enhanced != now.Enhanced)
            Sound("enhanced", "Bass and width", OnOff(now.Enhanced));
    }

    private void Sound(string key, string title, string value) =>
        _center.Post(new Notice(key, NoticeCategory.Sound, NoticeIcon.Equalizer, title) { Value = value });

    private void CompareTracks(State old, State now)
    {
        // Tracks listed late (found in the background) are not a choice; a change between known tracks is.
        if (old.AudioTrack >= 0 && now.AudioTrack >= 0 && (old.AudioTrack != now.AudioTrack || now.AudioTrackCount > old.AudioTrackCount && old.AudioTrackCount > 0))
            ShowAudioTrack();
        if (old.Subtitle != now.Subtitle)
        {
            _center.Release("subtitleLoading");
            _center.Post(new Notice("subtitles", NoticeCategory.Tracks, NoticeIcon.Subtitles, now.Subtitle is null ? "Subtitles off" : "Subtitles")
            { Detail = now.Subtitle, Seconds = now.Subtitle is null ? Notice.NormalSeconds : 3 });
        }

        if (old.SubtitleDelay != now.SubtitleDelay)
            ShowSubtitleDelay();
    }

    private void CompareSection(State old, State now, bool markerJumped)
    {
        if (old.LoopStart == now.LoopStart && old.LoopEnd == now.LoopEnd)
            return;
        // A jump out of the section ends it: the marker jumped to is the news.
        if (markerJumped && now.LoopStart is null)
            return;
        var title = now switch
        {
            { LoopStart: null } => "A-B repeat off",
            { LoopEnd: null } => $"A-B repeat: A at {MediaPlayer.FormatTime(now.LoopStart!.Value)}",
            _ => $"Repeating {MediaPlayer.FormatTime(now.LoopStart!.Value)} to {MediaPlayer.FormatTime(now.LoopEnd!.Value)}",
        };
        _center.Post(new Notice("abRepeat", NoticeCategory.Playback, NoticeIcon.Repeat, title) { Timeline = Timeline(), Seconds = 2.5 });
    }

    private void CompareLibrary(State old, State now)
    {
        var added = now.QueueCount - old.QueueCount;
        if (added > 0 && old.QueueCount > 0)
        {
            var last = _player.CurrentPlaylist.Items[^1];
            _center.Post(new Notice("queue", NoticeCategory.Library, NoticeIcon.Queue,
                added == 1 ? "Added to the queue" : string.Create(CultureInfo.InvariantCulture, $"{added} added to the queue"))
            { Detail = added == 1 ? last.Name : null, Value = string.Create(CultureInfo.InvariantCulture, $"{now.QueueCount} in all"), Seconds = 2.5 });
        }

        if (old.Favorite != now.Favorite)
        {
            _center.Post(new Notice("favorite", NoticeCategory.Library, NoticeIcon.Heart, now.Favorite ? "Added to favourites" : "Removed from favourites")
            { Detail = _player.CurrentMedia?.Name });
        }
    }

    private void ComparePicture(State old, State now)
    {
        if (old.Aspect != now.Aspect)
            ShowAspect();
        var changed = new List<(string Name, double Value)>(4);
        if (old.Brightness != now.Brightness)
            changed.Add(("Brightness", now.Brightness));
        if (old.Contrast != now.Contrast)
            changed.Add(("Contrast", now.Contrast));
        if (old.Hue != now.Hue)
            changed.Add(("Hue", now.Hue));
        if (old.Saturation != now.Saturation)
            changed.Add(("Saturation", now.Saturation));
        if (changed.Count > 1 && _player.Video.IsDefault)
        {
            _center.Post(new Notice("adjust", NoticeCategory.Picture, NoticeIcon.Picture, "Picture reset"));
        }
        else if (changed.Count > 0)
        {
            var (name, value) = changed[0];
            _center.Post(new Notice("adjust", NoticeCategory.Picture, NoticeIcon.Picture, name)
            {
                Value = value.ToString("+0;-0;0", CultureInfo.InvariantCulture),
                Level = (value + 127) / 254,
                Centered = true,
                Seconds = 1.6,
            });
        }
    }

    private void CompareSleep(State old, State now, bool newItem, bool ended)
    {
        var message = default(string);
        var automatic = false;
        if (now.SleepAtEnd && !old.SleepAtEnd)
            message = "Sleep timer: end of this track";
        else if (now.Sleep is { } left && (old.Sleep is not { } before || left > before + TimeSpan.FromSeconds(1)))
            message = string.Create(CultureInfo.InvariantCulture, $"Sleep timer: {Math.Ceiling(left.TotalMinutes):0} min");
        else if (now.Sleep is { TotalSeconds: <= SleepWarningSeconds } && old.Sleep is { TotalSeconds: > SleepWarningSeconds })
            (message, automatic) = ("Sleep timer: pausing in 1 minute", true);
        else if (old.SleepAtEnd && ended)
        {
            // The track it waited for has ended: the next one is loaded and paused (after the last one, the queue's end is told).
            (message, automatic) = (newItem && now.Play == PlayState.Paused ? "Sleep timer: paused" : null, true);
        }
        else if ((old.Sleep is not null || old.SleepAtEnd) && now.Sleep is null && !now.SleepAtEnd)
        {
            // It ran out when little was left and playback is paused; otherwise it was turned off.
            var ranOut = now.Play == PlayState.Paused && old.Sleep is { TotalSeconds: <= 1 };
            (message, automatic) = ranOut ? ("Sleep timer: paused", true) : ("Sleep timer off", false);
        }

        if (message is not null)
            _center.Post(new Notice("sleep", NoticeCategory.Playback, NoticeIcon.Sleep, message) { Seconds = automatic ? 4 : 2.5, Automatic = automatic });
    }

    /// <summary>The time and bar of the message showing keep up with playback.</summary>
    private void FollowTimeline()
    {
        if (_center.Current is { Timeline: not null, Follows: true } showing && _state.Play is PlayState.Playing or PlayState.Paused
            or PlayState.ScanForward or PlayState.ScanReverse)
            _center.Refresh(showing with { Timeline = Timeline() });
    }

    // ---- Events -------------------------------------------------------------------------------------------

    private void OnSeeked(object? sender, EventArgs e)
    {
        var seek = _player.LastSeek;
        if (IsQuiet || seek.Kind == SeekKind.Silent)
            return;
        var now = _clock();
        var showing = _center.Showing(SeekKey) is not null;
        double moved;
        if (seek.Kind == SeekKind.Relative)
        {
            // Presses in the same direction add up while the message stands; the player's clamp can make a step shorter.
            var step = seek.To - seek.From;
            _seekTotal = showing && _lastSeekKind == SeekKind.Relative && Math.Sign(_seekTotal) == Math.Sign(seek.Requested)
                ? _seekTotal + step : step;
            moved = _seekTotal;
        }
        else
        {
            if (!showing || _lastSeekKind != SeekKind.Direct || now - _lastSeek > DragGapMs)
                _dragFrom = seek.From;
            moved = seek.To - _dragFrom;
        }

        _lastSeek = now;
        _lastSeekKind = seek.Kind;
        _markerJumped |= seek.Kind == SeekKind.Marker;

        var back = seek.Kind == SeekKind.Relative ? seek.Requested < 0 : moved < 0;
        var notice = new Notice(SeekKey, NoticeCategory.Position,
            seek.Kind == SeekKind.Marker ? NoticeIcon.Marker : back ? NoticeIcon.Rewind : NoticeIcon.FastForward,
            seek.Label ?? Delta(moved))
        {
            Timeline = Timeline(),
            Detail = ChapterAt(seek.To),
        };
        _center.Post(notice);
    }

    private void OnCurrentMediaChanged(object? sender, EventArgs e)
    {
        // The same track, taken up again after a change to its file, is not a new one.
        if (!_player.IsResumingFile)
            _newItem = true;
    }

    private void OnPlayStateChanged(object? sender, PlayState state)
    {
        if (state == PlayState.MediaEnded)
            _ended = true;
    }

    private void OnError(object? sender, string message)
    {
        if (IsQuiet)
            return;
        _failed = true;
        // A network address that fails is reported by the network banner, with its button.
        var item = _player.CurrentPlaylist.Current;
        if (item is not null && NetworkMedia.IsNetwork(item.Source))
            return;
        _center.Post(new Notice("error", NoticeCategory.Problem, NoticeIcon.Warning, "Can't play this file") { Detail = item?.Name, Seconds = 5 });
    }

    private void OnAnnounced(object? sender, string message)
    {
        if (!IsQuiet)
            _center.Post(new Notice("announce", NoticeCategory.Library, NoticeIcon.Done, message) { Seconds = 3.5 });
    }

    private void OnMarkerAdded(object? sender, int index)
    {
        var markers = _player.CurrentMarkers;
        if (IsQuiet || index < 0 || index >= markers.Count)
            return;
        _center.Post(new Notice("marker", NoticeCategory.Library, NoticeIcon.Marker, "Marker added")
        { Detail = $"{MediaPlayer.MarkerLabel(markers[index], index)} at {MediaPlayer.FormatTime(markers[index].Seconds)}", Seconds = 2.5 });
    }

    public void Dispose()
    {
        _player.Seeked -= OnSeeked;
        _player.CurrentMediaChanged -= OnCurrentMediaChanged;
        _player.PlayStateChanged -= OnPlayStateChanged;
        _player.Error -= OnError;
        _player.Announced -= OnAnnounced;
        _player.MarkerAdded -= OnMarkerAdded;
    }
}
