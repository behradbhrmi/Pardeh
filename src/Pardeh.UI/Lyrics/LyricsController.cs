using System.Net;
using System.Net.Http;
using Pardeh.Core.LyricsKit;
using Pardeh.Core.Playback;
using Pardeh.Nava;

namespace Pardeh.UI.LyricsKit;

/// <summary>What the controller needs from the player (an adapter over <see cref="MediaPlayer"/>; tests use a fake clock).</summary>
public interface ILyricsPlayback
{
    MediaItem? CurrentMedia { get; }

    /// <summary>The current playback position.</summary>
    TimeSpan Position { get; }

    void Seek(TimeSpan position);

    /// <summary>Raised when the playing item changes (any thread).</summary>
    event EventHandler? CurrentMediaChanged;
}

/// <summary><see cref="ILyricsPlayback"/> over the app's <see cref="MediaPlayer"/>.</summary>
public sealed class MediaPlayerLyricsPlayback(MediaPlayer player) : ILyricsPlayback
{
    public MediaItem? CurrentMedia => player.CurrentMedia;

    public TimeSpan Position => TimeSpan.FromSeconds(player.CurrentPosition);

    public void Seek(TimeSpan position) => player.CurrentPosition = position.TotalSeconds;

    public event EventHandler? CurrentMediaChanged
    {
        add => player.CurrentMediaChanged += value;
        remove => player.CurrentMediaChanged -= value;
    }
}

/// <summary>What changed in a <see cref="LyricsController"/>.</summary>
public enum LyricsChangeKind
{
    /// <summary>Other lyrics (or none, or loading started): read <see cref="LyricsController.Current"/> again.</summary>
    Lyrics,

    /// <summary>Playback reached another line: <see cref="LyricsController.CurrentIndex"/>.</summary>
    Index,

    /// <summary>The timing correction changed (the index may have moved too).</summary>
    Offset,
}

public sealed class LyricsChangedEventArgs(LyricsChangeKind kind) : EventArgs
{
    public LyricsChangeKind Kind { get; } = kind;
}

/// <summary>
/// The lyrics of the playing track for any skin view: it loads them in the background when the track changes
/// (<see cref="LyricsService"/>), follows the playback position and tells the view when to repaint. It holds no Avalonia types.
/// Call <see cref="Tick"/> from the UI's regular timer; read <see cref="Current"/> / <see cref="CurrentIndex"/> from the UI thread
/// in the <see cref="Changed"/> handler.
/// </summary>
public sealed class LyricsController : IDisposable
{
    private readonly ILyricsPlayback _playback;
    private readonly LyricsService _service;
    private readonly Func<bool> _enabled;
    private readonly Action<Action> _post;
    private readonly object _gate = new();
    private readonly Action? _detach;
    private CancellationTokenSource? _cancel;
    private MediaItem? _item;
    private Lyrics? _lyrics;
    private int _index = -1;
    private int _generation;
    private bool _loading;
    private bool _disposed;
    private TimeSpan? _lastPosition;
    private Task _persist = Task.CompletedTask;

    /// <param name="playback">The player.</param>
    /// <param name="service">Finds the lyrics.</param>
    /// <param name="enabled">The "lyrics enabled" switch, read on every track change and <see cref="Refresh"/>.</param>
    /// <param name="post">Runs an action on the UI thread (Avalonia: <c>Dispatcher.UIThread.Post</c>). Null: runs it where it is called.</param>
    /// <param name="detach">Called on <see cref="Dispose"/> (to unsubscribe from settings).</param>
    public LyricsController(ILyricsPlayback playback, LyricsService service, Func<bool>? enabled = null, Action<Action>? post = null, Action? detach = null)
    {
        _playback = playback;
        _service = service;
        _enabled = enabled ?? (() => true);
        _post = post ?? (a => a());
        _detach = detach;
        _playback.CurrentMediaChanged += OnMediaChanged;
        OnMediaChanged(this, EventArgs.Empty);
    }

    /// <summary>
    /// The controller the app uses: lyrics from the tags and files next to the tracks, user lyrics in <see cref="AppPaths.DataDirectory"/>/lyrics,
    /// and (only when <see cref="AppSettings.LyricsOnline"/> is on) LRCLIB. Follows the settings.
    /// </summary>
    public static LyricsController Create(MediaPlayer player, AppSettings settings, Action<Action>? post = null)
    {
        var handler = new SocketsHttpHandler { UseCookies = false, AutomaticDecompression = DecompressionMethods.All, ConnectTimeout = TimeSpan.FromSeconds(8) };
        var http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(30) };
        var service = new LyricsService(
            new LyricsStore(Path.Combine(AppPaths.DataDirectory, "lyrics")),
            [new TagLyricsSource(), new SidecarLyricsSource()],
            [new LrcLibProvider(http), new CustomLyricsProvider(http, () => settings.LyricsSources)],
            () => new LyricsOptions(settings.LyricsEnabled, settings.LyricsOnline, settings.LyricsSaveFetched))
        {
            ChordSource = new SidecarLyricsSource.Chords(),
            TabSource = (item, cancel) => SidecarLyricsSource.ReadTab(item.Source, cancel),
        };

        LyricsController? controller = null;
        var last = (settings.LyricsEnabled, settings.LyricsOnline);
        void OnSettings()
        {
            var now = (settings.LyricsEnabled, settings.LyricsOnline);
            if (now == last)
                return;
            last = now;
            controller?.Refresh();
        }

        settings.Changed += OnSettings;
        controller = new LyricsController(new MediaPlayerLyricsPlayback(player), service, () => settings.LyricsEnabled, post,
            detach: () =>
            {
                settings.Changed -= OnSettings;
                http.Dispose();
            });
        return controller;
    }

    public LyricsService Service => _service;

    /// <summary>The lyrics of the playing track, or null (none found, still loading, or lyrics are off).</summary>
    public Lyrics? Current
    {
        get
        {
            lock (_gate)
                return _lyrics;
        }
    }

    /// <summary>The line to highlight: -1 before the first line, for unsynchronised lyrics and when there are none.</summary>
    public int CurrentIndex
    {
        get
        {
            lock (_gate)
                return _index;
        }
    }

    /// <summary>True from a track change until its lyrics have been looked up.</summary>
    public bool IsLoading
    {
        get
        {
            lock (_gate)
                return _loading;
        }
    }

    /// <summary>The track the lyrics are for.</summary>
    public MediaItem? Item
    {
        get
        {
            lock (_gate)
                return _item;
        }
    }

    /// <summary>The user's timing correction in milliseconds for the current lyrics (0 when there are none).</summary>
    public int OffsetMs => Current?.OffsetMs ?? 0;

    /// <summary>Raised (on the UI thread when <c>post</c> was given) when the lyrics, the highlighted line or the offset change.</summary>
    public event EventHandler<LyricsChangedEventArgs>? Changed;

    private void Raise(LyricsChangeKind kind) => _post(() =>
    {
        if (!_disposed)
            Changed?.Invoke(this, new LyricsChangedEventArgs(kind));
    });

    /// <summary>Call from the UI timer (every frame is fine): moves <see cref="CurrentIndex"/> with the playback position.</summary>
    public void Tick()
    {
        bool changed;
        lock (_gate)
        {
            if (_lyrics is not { Synced: true } lyrics)
                return;
            var position = _playback.Position;
            if (_lastPosition == position)
                return;
            _lastPosition = position;
            var index = lyrics.IndexAt(position);
            changed = index != _index;
            _index = index;
        }

        if (changed)
            Changed?.Invoke(this, new LyricsChangedEventArgs(LyricsChangeKind.Index));
    }

    /// <summary>Seeks the player to the start of line <paramref name="index"/> (synchronised lyrics only). False when not possible.</summary>
    public bool SeekToLine(int index)
    {
        TimeSpan target;
        lock (_gate)
        {
            if (_lyrics is not { Synced: true } lyrics || index < 0 || index >= lyrics.Lines.Count)
                return false;
            // A few ms past the stamp, so that the engine landing slightly early still shows this line.
            target = lyrics.TimeOf(index) + TimeSpan.FromMilliseconds(10);
            _lastPosition = null;
        }

        _playback.Seek(target);
        return true;
    }

    /// <summary>Sets the timing correction (milliseconds; positive delays the lyrics) and saves it for the track.</summary>
    public void SetOffset(int offsetMs)
    {
        MediaItem? item;
        lock (_gate)
        {
            if (_lyrics is not { } lyrics)
                return;
            offsetMs = Math.Clamp(offsetMs, -600_000, 600_000);
            if (lyrics.OffsetMs == offsetMs)
                return;
            lyrics.OffsetMs = offsetMs;
            _lastPosition = null;
            item = _item;
        }

        if (item is not null)
        {
            // One write at a time, in order, off the UI thread.
            lock (_gate)
                _persist = _persist.ContinueWith(_ => _service.SetOffset(item, offsetMs), TaskScheduler.Default);
        }

        Tick();
        Raise(LyricsChangeKind.Offset);
    }

    /// <summary>Moves the timing correction by <paramref name="deltaMs"/>.</summary>
    public void NudgeOffset(int deltaMs) => SetOffset(OffsetMs + deltaMs);

    /// <summary>Transposes the chords (semitones, wrapped into -11 to +11) and saves that for the track.</summary>
    public void SetTranspose(int semitones)
    {
        semitones %= 12;
        MediaItem? item;
        lock (_gate)
        {
            if (_lyrics is not { } lyrics || lyrics.Transpose == semitones)
                return;
            lyrics.Transpose = semitones;
            item = _item;
            if (item is not null)
                _persist = _persist.ContinueWith(_ => _service.SetTranspose(item, semitones), TaskScheduler.Default);
        }

        Raise(LyricsChangeKind.Offset);
    }

    /// <summary>The user's own chords text for the playing track, or null.</summary>
    public string? ChordsText => Item is { } item ? _service.GetChordsText(item) : null;

    /// <summary>Saves the user's own chords for the playing track and shows them (empty text removes them). Throws IOException when they cannot be saved.</summary>
    public void SaveChords(string text)
    {
        if (Item is not { } item)
            return;
        _service.SaveChordsText(item, text);
        Reload();
    }

    public void DeleteChords()
    {
        if (Item is { } item && _service.DeleteChordsText(item))
            Reload();
    }

    /// <summary>Saves the user's own lyrics for the playing track and shows them. Throws IOException when they cannot be saved.</summary>
    public void SaveOverride(Lyrics lyrics)
    {
        MediaItem? item;
        lock (_gate)
            item = _item;
        if (item is null)
            return;
        _service.SaveOverride(item, lyrics);
        Reload();
    }

    /// <summary>Removes the user's own lyrics for the playing track and looks for the others again.</summary>
    public void DeleteOverride()
    {
        MediaItem? item;
        lock (_gate)
            item = _item;
        if (item is not null && _service.DeleteOverride(item))
            Reload();
    }

    /// <summary>Looks the playing track up again (after the settings or the files changed).</summary>
    public void Refresh()
    {
        lock (_gate)
            if (_item is { } item)
                _service.Invalidate(item);
        Reload();
    }

    /// <summary>The user asked to search again: forgets an earlier online miss, then looks the track up again.</summary>
    public void SearchAgain()
    {
        lock (_gate)
            if (_item is { } item)
                _service.ForgetMiss(item);
        Refresh();
    }

    private void Reload()
    {
        lock (_gate)
            _item = null; // makes OnMediaChanged treat the item as new
        OnMediaChanged(this, EventArgs.Empty);
    }

    private void OnMediaChanged(object? sender, EventArgs e)
    {
        var item = _playback.CurrentMedia;
        var token = CancellationToken.None;
        int generation;
        bool hadLyrics;
        lock (_gate)
        {
            if (_disposed || (ReferenceEquals(item, _item) && item is not null))
                return;
            _cancel?.Cancel();
            _cancel?.Dispose();
            _cancel = null;
            hadLyrics = _lyrics is not null || _loading;
            _item = item;
            _lyrics = null;
            _index = -1;
            _lastPosition = null;
            generation = ++_generation;
            _loading = item is not null && _enabled();
            if (_loading)
            {
                _cancel = new CancellationTokenSource();
                token = _cancel.Token;
            }
        }

        if (hadLyrics)
            Raise(LyricsChangeKind.Lyrics);
        if (token.CanBeCanceled)
            _ = LoadAsync(item!, generation, token);
    }

    private async Task LoadAsync(MediaItem item, int generation, CancellationToken cancel)
    {
        Lyrics? lyrics = null;
        try
        {
            lyrics = await _service.GetAsync(item, cancel).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            // Whatever went wrong while looking: no lyrics.
        }

        lock (_gate)
        {
            if (_disposed || generation != _generation)
                return;
            _lyrics = lyrics;
            _loading = false;
            _lastPosition = null;
            _index = lyrics is { Synced: true } ? lyrics.IndexAt(_playback.Position) : -1;
        }

        Raise(LyricsChangeKind.Lyrics);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _cancel?.Cancel();
            _cancel?.Dispose();
            _cancel = null;
        }

        _playback.CurrentMediaChanged -= OnMediaChanged;
        _detach?.Invoke();
    }
}
