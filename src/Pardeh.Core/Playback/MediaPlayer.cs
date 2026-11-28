using System.Globalization;

namespace Pardeh.Core.Playback;

/// <summary>
/// The player: playlist, transport state machine and settings on top of an <see cref="IPlaybackEngine"/>.
/// All members must be used from one (UI) thread; engine callbacks are marshalled to it.
/// </summary>
public sealed partial class MediaPlayer : IDisposable
{
    private const double ScanSpeed = 5.0; // seconds of media per second while scanning

    private readonly IPlaybackEngine _engine;
    private readonly IMediaInfoReader _infoReader;
    private readonly SynchronizationContext? _context;

    private PlayState _playState = PlayState.Undefined;
    private OpenState _openState = OpenState.Undefined;
    private string _status = string.Empty;
    private double _volume = 50;
    private double _balance;
    private bool _mute;
    private PlayState _stateBeforeScan;

    public MediaPlayer(IPlaybackEngine engine, IMediaInfoReader infoReader)
    {
        _engine = engine;
        _infoReader = infoReader;
        _context = SynchronizationContext.Current;
        _engine.PlaybackEnded += OnEnginePlaybackEnded;
        _engine.AdvancedToNext += OnEngineAdvanced;
        _engine.Volume = (float)(_volume / 100);
        CurrentPlaylist.Changed += (_, _) => PlaylistChanged?.Invoke(this, EventArgs.Empty);
        SetStatus("Ready");
        SetPlayState(PlayState.Ready);
    }

    public Playlist CurrentPlaylist { get; } = new("Now Playing");

    public MediaItem? CurrentMedia { get; private set; }

    public EqualizerSettings Equalizer => _engine.Equalizer;

    /// <summary>Video Settings: brightness, contrast, hue, saturation (applied when drawing frames).</summary>
    public VideoSettings Video { get; } = new();

    public PlayState PlayState => _playState;

    public OpenState OpenState => _openState;

    public string Status => _status;

    public event EventHandler<PlayState>? PlayStateChanged;

    public event EventHandler<OpenState>? OpenStateChanged;

    public event EventHandler<string>? StatusChanged;

    public event EventHandler<(string Mode, bool Value)>? ModeChanged;

    public event EventHandler? CurrentMediaChanged;

    public event EventHandler? PlaylistChanged;

    public event EventHandler<string>? Error;

    /// <summary>Subtitles of the current item (a sidecar .srt / .vtt / .ass, or one loaded by hand), or null.</summary>
    public Subtitles? Subtitles { get; private set; }

    /// <summary>Play › Captions and Subtitles: show them when there are any.</summary>
    public bool ShowSubtitles { get; set; } = true;

    /// <summary>The subtitle text to show now (after a delay change also the new delay, for a moment), or null.</summary>
    public string? CurrentSubtitle => WithDelayNotice(
        ShowSubtitles && Subtitles is { } subtitles ? subtitles.TextAt(TimeSpan.FromSeconds(CurrentPosition) - SubtitleDelay) : null);

    /// <summary>Uses a subtitle file for the current item. False when it has no usable cues.</summary>
    public bool LoadSubtitles(string path)
    {
        if (Subtitles.Load(path) is not { } loaded)
            return false;
        Subtitles = loaded;
        EmbeddedSubtitle = null;
        ShowSubtitles = true;
        return true;
    }

    /// <summary>The subtitle tracks inside the current video (they appear a moment after it opens).</summary>
    public IReadOnlyList<SubtitleTrack> SubtitleTracks => _engine.SubtitleTracks;

    /// <summary>The embedded track whose cues are showing, or null (none, or a subtitle file).</summary>
    public SubtitleTrack? EmbeddedSubtitle { get; private set; }

    private CancellationTokenSource? _subtitleLoad;

    /// <summary>Reads an embedded subtitle track (in the background) and shows it. False when it has no usable cues or the item changed meanwhile.</summary>
    public async Task<bool> SelectSubtitleTrackAsync(SubtitleTrack track)
    {
        _subtitleLoad?.Cancel();
        var load = _subtitleLoad = new CancellationTokenSource();
        var item = CurrentMedia;
        IReadOnlyList<SubtitleCue>? cues;
        try
        {
            cues = await Task.Run(() => _engine.ReadSubtitleTrack(track, load.Token));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }

        if (load.IsCancellationRequested || !ReferenceEquals(item, CurrentMedia) || cues is null || Playback.Subtitles.FromCues(track.Label, cues) is not { } loaded)
            return false;
        Subtitles = loaded;
        EmbeddedSubtitle = track;
        ShowSubtitles = true;
        return true;
    }

    /// <summary>Audio tracks of the current item: languages inside the file, plus separate audio files (dubs).</summary>
    public IReadOnlyList<AudioTrack> AudioTracks => _engine.AudioTracks;

    public int CurrentAudioTrack => _engine.CurrentAudioTrack;

    /// <summary>Chapters of the current item (valid once <see cref="CurrentMediaChanged"/> has fired).</summary>
    public IReadOnlyList<Chapter> Chapters => _engine.Chapters;

    /// <summary>The language chosen last ("Persian"); later items (the next episode) start with it when they have it.</summary>
    private string? _preferredAudio;

    public void SelectAudioTrack(int index)
    {
        _engine.SelectAudioTrack(index);
        if (index >= 0 && index < AudioTracks.Count)
            _preferredAudio = LanguageOf(AudioTracks[index].Label);
    }

    /// <summary>Plays a separate audio file (a dub) with the current video, from the same position.</summary>
    public bool AddAudioFile(string path) => _engine.AddAudioFile(path);

    private static string LanguageOf(string label)
    {
        var end = label.IndexOfAny(['–', '(']);
        return (end > 0 ? label[..end] : label).Trim();
    }

    private void ApplyPreferredAudio()
    {
        if (_preferredAudio is not { Length: > 0 } preferred || AudioTracks.Count < 2)
            return;
        var tracks = AudioTracks;
        for (var i = 0; i < tracks.Count; i++)
        {
            if (tracks[i].Stream >= 0 && LanguageOf(tracks[i].Label).Equals(preferred, StringComparison.OrdinalIgnoreCase))
            {
                if (i != CurrentAudioTrack)
                    _engine.SelectAudioTrack(i);
                return;
            }
        }
    }

    /// <summary>The play speeds offered in menus (Play › Play Speed).</summary>
    public static readonly double[] RatePresets = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 2.5, 3, 4];

    private double _rate = 1;

    public event EventHandler? RateChanged;

    /// <summary>Play speed: 0.25 – 4 (1 = normal), pitch kept. Stays for the following items, as in the classic player.</summary>
    public double Rate
    {
        get => _rate;
        set
        {
            var rate = Math.Round(Math.Clamp(value, 0.25, 4), 3);
            if (Math.Abs(rate - _rate) < 1e-6)
                return;
            _rate = rate;
            _engine.Rate = rate;
            SetStatus(FormattableString.Invariant($"Play speed {rate:0.##}×"));
            RateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The next faster (+1) or slower (-1) preset speed.</summary>
    public void StepRate(int direction)
    {
        var next = direction > 0
            ? RatePresets.FirstOrDefault(r => r > _rate + 1e-6, RatePresets[^1])
            : RatePresets.LastOrDefault(r => r < _rate - 1e-6, RatePresets[0]);
        Rate = next;
    }

    /// <summary>0 – 100.</summary>
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 100);
            _engine.Volume = (float)(_volume / 100);
        }
    }

    /// <summary>-100 (left) – 100 (right).</summary>
    public double Balance
    {
        get => _balance;
        set
        {
            _balance = Math.Clamp(value, -100, 100);
            _engine.Balance = (float)(_balance / 100);
        }
    }

    public bool Mute
    {
        get => _mute;
        set
        {
            _mute = value;
            ApplyMute();
        }
    }

    public bool Shuffle
    {
        get => CurrentPlaylist.Shuffle;
        set
        {
            if (CurrentPlaylist.Shuffle == value)
                return;
            CurrentPlaylist.Shuffle = value;
            ModeChanged?.Invoke(this, ("shuffle", value));
        }
    }

    public bool Loop
    {
        get => CurrentPlaylist.Loop;
        set
        {
            if (CurrentPlaylist.Loop == value)
                return;
            CurrentPlaylist.Loop = value;
            ModeChanged?.Invoke(this, ("loop", value));
        }
    }

    /// <summary>Current position in seconds.</summary>
    public double CurrentPosition
    {
        get => _engine.IsOpen ? _engine.Position.TotalSeconds : 0;
        set
        {
            if (!_engine.IsOpen)
                return;
            _engine.Position = TimeSpan.FromSeconds(Math.Clamp(value, 0, Math.Max(0, Duration)));
            Seeked?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised after the position was set directly (a seek, a skip, a scan step), so that mirrors such as the Android media session can follow.</summary>
    public event EventHandler? Seeked;

    /// <summary>
    /// The position a relative seek of <paramref name="delta"/> seconds from <paramref name="position"/> lands on, in [0, duration].
    /// Going forward never lands on the very end (the item would finish and the next one start): it stops 0.5 s before it,
    /// or at the end of a very short item.
    /// </summary>
    public static double ClampSeek(double position, double delta, double duration)
    {
        if (double.IsNaN(position) || position < 0)
            position = 0;
        var target = position + delta;
        var end = duration > 1 ? duration - 0.5 : Math.Max(0, duration);
        return Math.Clamp(target, 0, Math.Max(0, end));
    }

    /// <summary>Skips <paramref name="seconds"/> (negative = back) from the current position, clamped to the item. Does nothing when nothing is open.</summary>
    public void SeekBy(double seconds)
    {
        if (!_engine.IsOpen)
            return;
        CurrentPosition = ClampSeek(CurrentPosition, seconds, Duration);
    }

    /// <summary>Duration of the current media in seconds.</summary>
    public double Duration => _engine.IsOpen ? _engine.Duration.TotalSeconds : 0;

    /// <summary>"mm:ss" while media is open, else empty.</summary>
    public string CurrentPositionString =>
        _openState == OpenState.MediaOpen && _playState is not (PlayState.Stopped or PlayState.Ready)
            ? FormatTime(CurrentPosition)
            : string.Empty;

    public string DurationString => _engine.IsOpen ? FormatTime(Duration) : string.Empty;

    public static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0)
            seconds = 0;
        var t = TimeSpan.FromSeconds(Math.Floor(seconds));
        return t.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", t.Minutes, t.Seconds);
    }

    /// <summary>Replaces the playlist with the given sources and starts playing the first.</summary>
    public void OpenFiles(IEnumerable<string> sources, bool autoPlay = true)
    {
        // Playlist files (.m3u, .wpl, .asx …) open as their entries.
        var list = Library.PlaylistFormats.Expand(sources).ToList();
        if (list.Count == 0)
            return;

        StopInternal(PlayState.Stopped);
        CurrentPlaylist.Clear();
        foreach (var source in list)
            CurrentPlaylist.Add(ReadInfo(source));

        CurrentPlaylist.SetCurrent(0);
        if (autoPlay)
            OpenCurrentAndPlay();
    }

    /// <summary>Replaces the playlist with already-known items (e.g. from the library) and plays one.</summary>
    public void OpenItems(IEnumerable<MediaItem> items, int startIndex)
    {
        var list = items.ToList();
        if (list.Count == 0)
            return;

        StopInternal(PlayState.Stopped);
        CurrentPlaylist.Clear();
        foreach (var item in list)
            CurrentPlaylist.Add(item);
        CurrentPlaylist.SetCurrent(Math.Clamp(startIndex, 0, list.Count - 1));
        OpenCurrentAndPlay();
    }

    /// <summary>Adds sources to the end of the playlist.</summary>
    public void Enqueue(IEnumerable<string> sources)
    {
        foreach (var source in sources)
            CurrentPlaylist.Add(ReadInfo(source));
    }

    /// <summary>Equivalent of WMP's player.URL setter.</summary>
    public string Url
    {
        get => CurrentMedia?.Source ?? string.Empty;
        set => OpenFiles([value], autoPlay: false);
    }

    public void PlayItem(int index)
    {
        CurrentPlaylist.SetCurrent(index);
        OpenCurrentAndPlay();
    }

    public void Play()
    {
        if (IsConnecting)
        {
            _connectPlay = true; // it starts when the connection is there
            return;
        }

        switch (_playState)
        {
            case PlayState.Playing:
                return;
            case PlayState.Paused:
                _engine.Play();
                SetPlayState(PlayState.Playing);
                SetStatus("Playing");
                return;
            case PlayState.ScanForward or PlayState.ScanReverse:
                EndScan();
                return;
        }

        if (CurrentPlaylist.Count == 0)
            return;
        if (CurrentPlaylist.CurrentIndex < 0)
            CurrentPlaylist.SetCurrent(0);

        if (_engine.IsOpen && CurrentMedia == CurrentPlaylist.Current)
        {
            _engine.Play();
            SetPlayState(PlayState.Playing);
            SetStatus("Playing");
        }
        else
        {
            OpenCurrentAndPlay();
        }
    }

    public void Pause()
    {
        if (IsConnecting)
        {
            _connectPlay = false;
            return;
        }

        if(_playState is not (PlayState.Playing or PlayState.ScanForward or PlayState.ScanReverse))
            return;
        if (_playState != PlayState.Playing)
            EndScan(resume: false);
        _engine.Pause();
        SetPlayState(PlayState.Paused);
        SetStatus("Paused");
        SaveVideoPosition();
    }

    public void Stop()
    {
        if (IsConnecting)
        {
            CancelConnect();
            return;
        }

        StopInternal(PlayState.Stopped);
        // A live stream cannot be rewound: Play connects to it again.
        if (_engine.IsOpen && CurrentMedia is { } item && NetworkMedia.IsNetwork(item.Source) && _engine.Duration <= TimeSpan.Zero)
            _engine.Close();
        SetNetwork(NetworkPhase.None);
    }

    public void Next()
    {
        if (!CurrentPlaylist.MoveNext())
            return;
        OpenCurrentAndPlay();
    }

    public void Previous()
    {
        // Like WMP 9: always the previous item; at the first one, back to its start.
        if (!CurrentPlaylist.MovePrevious())
        {
            CurrentPosition = 0;
            return;
        }

        OpenCurrentAndPlay();
    }

    public void FastForward() => BeginScan(PlayState.ScanForward);

    public void FastReverse() => BeginScan(PlayState.ScanReverse);

    private static readonly string[] FolderArtNames = ["folder.jpg", "AlbumArtSmall.jpg", "cover.jpg", "front.jpg", "folder.png", "cover.png"];

    /// <summary>
    /// The current item's cover: the picture embedded in the file, else a folder image (folder.jpg, cover.jpg …).
    /// Null when there is none.
    /// </summary>
    public byte[]? GetAlbumArt(MediaItem item)
    {
        if (_infoReader.ReadPicture(item.Source) is { Length: > 0 } embedded)
            return embedded;
        try
        {
            if (Path.GetDirectoryName(item.Source) is { Length: > 0 } folder && Directory.Exists(folder))
            {
                foreach (var name in FolderArtNames)
                {
                    var path = Path.Combine(folder, name);
                    if (File.Exists(path))
                        return File.ReadAllBytes(path);
                }

                // AlbumArt_{GUID}_Large.jpg, as the classic player saved them.
                if (Directory.EnumerateFiles(folder, "AlbumArt_*_Large.jpg").FirstOrDefault() is { } classic)
                    return File.ReadAllBytes(classic);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        return null;
    }
    /// <summary>WMP's controls.isAvailable(name).</summary>
    public bool IsAvailable(string name)
    {
        var open = _engine.IsOpen;
        var hasItems = CurrentPlaylist.Count > 0;
        return name.ToLowerInvariant() switch
        {
            "play" => hasItems && _playState != PlayState.Playing,
            "pause" => _playState is PlayState.Playing or PlayState.ScanForward or PlayState.ScanReverse,
            "stop" => _playState is PlayState.Playing or PlayState.Paused or PlayState.ScanForward or PlayState.ScanReverse,
            "next" => CurrentPlaylist.HasNext,
            "previous" => hasItems,
            "fastforward" or "fastreverse" => open && _playState is PlayState.Playing or PlayState.ScanForward or PlayState.ScanReverse,
            "currentposition" or "seek" => open,
            "currentitem" => hasItems,
            _ => false,
        };
    }


    private MediaItem? _crossfadedFrom;

    /// <summary>Crossfading: near the end of an audio track, start the next one while this one fades out.</summary>
    private void CheckCrossfade()
    {
        var eq = Equalizer;
        if (!eq.CrossFade || eq.CrossFadeSeconds <= 0 || _playState != PlayState.Playing || HasVideo || _sleepAtEndOfTrack || SectionLoopActive
            || CurrentMedia is not { } current || ReferenceEquals(current, _crossfadedFrom) || !CurrentPlaylist.HasNext)
            return;

        var duration = Duration;
        var window = eq.CrossFadeSeconds;
        if (duration < window * 2 || CurrentPosition < duration - window)
            return;

        _crossfadedFrom = current;
        if (!CurrentPlaylist.MoveNext())
            return;
        var next = CurrentPlaylist.Current;
        if (next is null || NetworkMedia.IsNetwork(next.Source) || Library.MediaLibrary.VideoExtensions.Contains(Path.GetExtension(next.Source)))
        {
            OpenCurrentAndPlay();
            return;
        }

        OpenCurrentAndPlay(window);
    }
    /// <summary>Call regularly (e.g. every 30 ms) from the UI thread.</summary>
    public void Tick(TimeSpan elapsed)
    {
        TickSleepTimer(elapsed);
        TickVideoPosition(elapsed);
        TickSectionLoop();
        CheckCrossfade();
        CheckGapless();
        TickNetwork(elapsed);

        if (_playState is PlayState.ScanForward or PlayState.ScanReverse)
        {
            var delta = elapsed.TotalSeconds * ScanSpeed * (_playState == PlayState.ScanForward ? 1 : -1);
            var target = CurrentPosition + delta;
            if (target <= 0)
            {
                CurrentPosition = 0;
                EndScan();
            }
            else if (target >= Duration)
            {
                EndScan(resume: false);
                OnEnginePlaybackEndedCore();
            }
            else
            {
                CurrentPosition = target;
            }
        }
    }

    public bool GetSpectrum(Span<float> bins) => _engine.GetSpectrum(bins);

    public bool GetWaveform(Span<float> samples) => _engine.GetWaveform(samples);

    /// <summary>True when the open media has a picture.</summary>
    public bool HasVideo => _engine.IsOpen && _engine.VideoSize.Width > 0;

    /// <summary>The video frame to show now, or null.</summary>
    public VideoFrame? CurrentVideoFrame => _engine.CurrentVideoFrame;

    public void Dispose()
    {
        AbandonConnect();
        _engine.PlaybackEnded -= OnEnginePlaybackEnded;
        _engine.AdvancedToNext -= OnEngineAdvanced;
        _engine.Dispose();
    }

    private MediaItem ReadInfo(string source)
    {
        try
        {
            return _infoReader.Read(source);
        }
        catch
        {
            return new MediaItem(source);
        }
    }

    /// <summary>Makes <paramref name="item"/> the current one: what belonged to the track before it is let go.</summary>
    private void BeginItem(MediaItem item)
    {
        CurrentMedia = item;
        SubtitleDelay = TimeSpan.Zero;
        if (LoopStart is not null || LoopEnd is not null)
        {
            LoopStart = LoopEnd = null; // a section belongs to one track
            SectionLoopChanged?.Invoke(this, EventArgs.Empty);
        }

        _subtitleLoad?.Cancel();
        EmbeddedSubtitle = null;
        Subtitles = Playback.Subtitles.FindFor(item.Source) is { } subtitleFile ? Playback.Subtitles.Load(subtitleFile) : null;
    }

    private void OpenCurrentAndPlay(double crossfadeSeconds = 0, bool play = true, double startAt = 0)
    {
        var item = CurrentPlaylist.Current;
        if (item is null)
            return;

        SaveVideoPosition(); // where the video being left was
        AbandonConnect();
        _reconnects = 0;
        SetNetwork(NetworkPhase.None);
        if (crossfadeSeconds <= 0)
            _engine.Stop();
        SetOpenState(OpenState.MediaChanging);
        SetPlayState(PlayState.Transitioning);
        SetStatus("Opening media...");

        DropPrepared();
        BeginItem(item);
        SetOpenState(OpenState.MediaOpening);

        if (NetworkMedia.IsNetwork(item.Source) && _engine.OpensInBackground(item.Source))
        {
            _engine.Close();
            StartConnect(item, play, startAt, TimeSpan.Zero);
            return;
        }

        try
        {
            if (crossfadeSeconds > 0)
                _engine.OpenCrossfade(item.Source, crossfadeSeconds);
            else
                _engine.Open(item.Source);
            ApplyReplayGain();
        }
        catch (Exception ex)
        {
            CurrentMedia = null;
            SetOpenState(OpenState.Undefined);
            SetPlayState(PlayState.Stopped);
            SetStatus("Cannot play the file");
            Error?.Invoke(this, $"{item.Source}: {ex.Message}");
            return;
        }

        FinishOpen(item, play, startAt);
    }

    /// <summary>The rest of opening, once the engine has the media.</summary>
    private void FinishOpen(MediaItem item, bool play, double startAt)
    {
        if (item.Duration == TimeSpan.Zero)
            item.Duration = _engine.Duration;
        var (videoWidth, videoHeight) = _engine.VideoSize;
        if (videoWidth > 0)
        {
            item.VideoWidth = videoWidth;
            item.VideoHeight = videoHeight;
        }

        ApplyPreferredAudio();
        _resumedAt = null;
        if (play)
            ResumeVideo(item);
        CurrentMediaChanged?.Invoke(this, EventArgs.Empty);
        SetOpenState(OpenState.MediaOpen);
        ApplyMute();
        if (!play)
        {
            // Restored from the last session: loaded and waiting at its old position, paused.
            if (startAt > 0.5 && startAt < _engine.Duration.TotalSeconds - 1)
                _engine.Position = TimeSpan.FromSeconds(startAt);
            SetPlayState(PlayState.Paused);
            SetStatus("Paused");
            return;
        }

        // A file that was connected again after a drop carries on where it was.
        if (startAt > 0.5 && startAt < _engine.Duration.TotalSeconds - 1)
            _engine.Position = TimeSpan.FromSeconds(startAt);
        _engine.Play();
        SetPlayState(PlayState.Playing);
        SetStatus(_resumedAt is { } resumed ? $"Playing (resumed at {FormatTime(resumed)})" : "Playing");
    }

    /// <summary>
    /// Puts back the queue of the last session: the items, the current one loaded and paused at <paramref name="positionSeconds"/>.
    /// Nothing plays until the user presses Play.
    /// </summary>
    public void RestoreQueue(IEnumerable<MediaItem> items, int index, double positionSeconds)
    {
        var list = items.ToList();
        if (list.Count == 0 || CurrentMedia is not null)
            return;
        CurrentPlaylist.Clear();
        foreach (var item in list)
            CurrentPlaylist.Add(item);
        CurrentPlaylist.SetCurrent(Math.Clamp(index, 0, list.Count - 1));
        if (CurrentPlaylist.Current is { } restored && NetworkMedia.IsNetwork(restored.Source))
            return; // nothing is fetched from the network at start: Play connects
        OpenCurrentAndPlay(0, play: false, startAt: positionSeconds);
    }

    private void StopInternal(PlayState finalState)
    {
        AbandonConnect();
        if(_playState is PlayState.ScanForward or PlayState.ScanReverse)
            EndScan(resume: false);
        SaveVideoPosition();
        if (_engine.IsOpen)
        {
            _engine.Stop();
            _engine.Position = TimeSpan.Zero;
        }

        if (_playState != finalState)
        {
            SetPlayState(finalState);
            SetStatus(finalState == PlayState.Stopped ? "Stopped" : "Ready");
        }
    }

    private void BeginScan(PlayState scanState)
    {
        if (!_engine.IsOpen || _playState is not (PlayState.Playing or PlayState.ScanForward or PlayState.ScanReverse))
            return;
        if (_playState is PlayState.Playing)
            _stateBeforeScan = PlayState.Playing;
        SetPlayState(scanState);
        SetStatus(scanState == PlayState.ScanForward ? "Fast Forward" : "Rewind");
        ApplyMute();
    }

    private void EndScan(bool resume = true)
    {
        if (_playState is not (PlayState.ScanForward or PlayState.ScanReverse))
            return;
        SetPlayState(resume ? _stateBeforeScan : PlayState.Playing);
        ApplyMute();
        if (resume)
            SetStatus("Playing");
    }

    private void ApplyMute() =>
        _engine.Muted = _mute || _playState is PlayState.ScanForward or PlayState.ScanReverse;

    private void OnEnginePlaybackEnded(object? sender, EventArgs e)
    {
        if (_context is null)
            OnEnginePlaybackEndedCore();
        else
            _context.Post(_ => OnEnginePlaybackEndedCore(), null);
    }

    private void OnEnginePlaybackEndedCore()
    {
        if (_playState is PlayState.Stopped or PlayState.Paused)
            return;

        if (TryReconnect())
            return;

        if (CurrentMedia is { } finished && HasVideo)
            VideoPositions?.Set(finished.Source, null); // seen to the end: next time from the start
        SetPlayState(PlayState.MediaEnded);
        if (_sleepAtEndOfTrack)
        {
            // The sleep timer waits for this track's end: the next track is loaded but not played.
            _sleepAtEndOfTrack = false;
            SleepTimerChanged?.Invoke(this, EventArgs.Empty);
            if (CurrentPlaylist.MoveNext())
                OpenCurrentAndPlay(0, play: false);
            else
                StopInternal(PlayState.Stopped);
            return;
        }

        if (CurrentPlaylist.MoveNext())
        {
            OpenCurrentAndPlay();
        }
        else
        {
            StopInternal(PlayState.Stopped);
        }
    }

    private void SetPlayState(PlayState state)
    {
        if (_playState == state)
            return;
        _playState = state;
        PlayStateChanged?.Invoke(this, state);
    }

    private void SetOpenState(OpenState state)
    {
        if (_openState == state)
            return;
        _openState = state;
        OpenStateChanged?.Invoke(this, state);
    }

    /// <summary>Writes the head's own message ("Picture saved to …") on the status line.</summary>
    public void Announce(string message) => SetStatus(message);

    private void SetStatus(string status)
    {
        if (_status == status)
            return;
        _status = status;
        StatusChanged?.Invoke(this, status);
    }
}
