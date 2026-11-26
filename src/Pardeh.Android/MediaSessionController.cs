using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Media;
using Android.Media.Session;
using Android.OS;
using Avalonia.Threading;
using MediaPlayer = Pardeh.Core.Playback.MediaPlayer;
using MediaItem = Pardeh.Core.Playback.MediaItem;
using PlayState = Pardeh.Core.Playback.PlayState;

namespace Pardeh.Android;

/// <summary>
/// Hooks the player to Android: a MediaSession (lock screen, headset and Bluetooth buttons, the system media controls), a
/// media-style notification and a foreground service that keeps playback alive with the screen off or the app in the background.
/// Also takes audio focus and holds a partial wake lock while playing.
///
/// What the system gets (the media card of Android 13+ is built from the session, the notification is what older versions and
/// other shells show): title, artist, album, duration and the cover art as session metadata; the playback state with position,
/// speed and update time (the card's seek bar and the elapsed / remaining times), the actions play / pause / previous / next /
/// seek / stop and the two custom actions "back 10 s" and "forward 10 s" (Android 13+ puts them on both sides of previous / next,
/// as VLC's card shows them); and a notification with the same five buttons, the cover as its large picture, the album as sub
/// text and a colour taken from the cover.
/// </summary>
internal sealed class MediaSessionController : IDisposable
{
    public const string ChannelId = "pardeh.playback";
    public const int NotificationId = 4711;
    public const string ActionPlay = "org.pardeh.player.PLAY", ActionPause = "org.pardeh.player.PAUSE", ActionNext = "org.pardeh.player.NEXT",
        ActionPrevious = "org.pardeh.player.PREVIOUS", ActionStop = "org.pardeh.player.STOP",
        ActionBack10 = "org.pardeh.player.BACK10", ActionForward10 = "org.pardeh.player.FORWARD10",
        ActionShuffle = "org.pardeh.player.SHUFFLE", ActionRepeat = "org.pardeh.player.REPEAT", ActionFavorite = "org.pardeh.player.FAVORITE",
        ActionSeekPrefix = "org.pardeh.player.SEEK:";

    /// <summary>Seconds the back / forward buttons (and a headset's rewind / fast-forward keys) skip.</summary>
    public const double SkipSeconds = 10;

    /// <summary>Longest side of the cover bitmap handed to the system (the card scales it down further).</summary>
    private const int ArtSize = 512;

    private static MediaSessionController? _current;
    private readonly MediaPlayer _player;
    private readonly Action<string> _log;
    private readonly MediaSession _session;
    private readonly NotificationManager _notifications;
    private readonly AudioManager? _audio;
    private readonly PowerManager.WakeLock? _wake;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private AudioFocusRequestClass? _focus;
    private Notification? _notification;
    private bool _foreground;
    private bool _resumeOnFocus;
    private string? _artFor;
    private Bitmap? _art;
    private int? _artColor;
    private string? _metadataKey;
    private long _stateAt;          // SystemClock.ElapsedRealtime() of the last SetPlaybackState
    private double _statePosition;  // seconds
    private double _stateSpeed;     // 0 when paused
    private int _cardColor;         // average colour of the cover, darkened: the notification card colour of the Pardeh card
    private long _cardRefreshedAt;  // SystemClock.ElapsedRealtime() of the last periodic repaint of the Pardeh notification card

    private static Context AppContext => global::Android.App.Application.Context;

    public MediaSessionController(MediaPlayer player, Action<string> log)
    {
        _player = player;
        _log = log;
        _current = this;
        _notifications = (NotificationManager)AppContext.GetSystemService(Context.NotificationService)!;
        _audio = AppContext.GetSystemService(Context.AudioService) as AudioManager;
        _wake = (AppContext.GetSystemService(Context.PowerService) as PowerManager)?.NewWakeLock(WakeLockFlags.Partial, "Pardeh:playback");
        _wake?.SetReferenceCounted(false);
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            _notifications.CreateNotificationChannel(new NotificationChannel(ChannelId, "Playback", NotificationImportance.Low) { Description = "Now playing controls" });

        _session = new MediaSession(AppContext, "Pardeh");
        _session.SetCallback(new SessionCallback(this));
        _session.SetSessionActivity(PendingIntent.GetActivity(AppContext, 0, new Intent(AppContext, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop | ActivityFlags.NewTask), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent));
        _session.SetRatingType(RatingStyle.Heart); // other controllers can show the favourite heart and send it back (OnSetRating)
        _session.Active = true;
        try
        {
            var noisy = new IntentFilter(AudioManager.ActionAudioBecomingNoisy);
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
                AppContext.RegisterReceiver(_noisy, noisy, ReceiverFlags.NotExported);
            else
                AppContext.RegisterReceiver(_noisy, noisy);
        }
        catch (Exception ex)
        {
            log("Cannot watch for unplugged headphones: " + ex.Message);
        }

        _player.PlayStateChanged += OnChanged;
        _player.CurrentMediaChanged += OnChanged;
        _player.FavoritesChanged += OnChanged;
        _player.Seeked += OnSeeked;
        _timer.Tick += (_, _) => Heartbeat();
        Refresh();
        _log("Android media session started.");
    }

    private void OnChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Refresh);

    private void OnChanged(object? sender, PlayState state) => Dispatcher.UIThread.Post(Refresh);

    /// <summary>The position was set (a seek, a skip, a scan step): the card's seek bar follows when it is no longer where the system extrapolates it.</summary>
    private void OnSeeked(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
            SyncPosition(0.75);
        else
            Dispatcher.UIThread.Post(() => SyncPosition(0.75));
    }

    /// <summary>Called by the notification buttons (through the service) and the session callback.</summary>
    public static void Handle(string? action) => Dispatcher.UIThread.Post(() => _current?.Run(action));

    private void Run(string? action)
    {
        switch (action)
        {
            case ActionPlay:
                _player.Play();
                break;
            case ActionPause:
                _player.Pause();
                break;
            case ActionNext:
                _player.Next();
                break;
            case ActionPrevious:
                _player.Previous();
                break;
            case ActionStop:
                _player.Stop();
                break;
            case { } seek when seek.StartsWith(ActionSeekPrefix, StringComparison.Ordinal):
                if (int.TryParse(seek.AsSpan(ActionSeekPrefix.Length), out var permille) && _player.Duration > 0)
                    _player.CurrentPosition = _player.Duration * Math.Clamp(permille, 0, 1000) / 1000.0;
                break;
            case ActionBack10:
                _player.SeekBy(-SkipSeconds);
                break;
            case ActionForward10:
                _player.SeekBy(SkipSeconds);
                break;
            case ActionShuffle:
                _player.Shuffle = !_player.Shuffle;
                break;
            case ActionFavorite:
                _player.ToggleFavorite();
                break;
            case ActionRepeat:
                _player.Loop = !_player.Loop;
                break;
        }

        Refresh();
    }

    private bool IsPlaying => _player.PlayState is PlayState.Playing or PlayState.ScanForward or PlayState.ScanReverse;

    private void Refresh()
    {
        RefreshCore();
        PushWidget();
    }

    /// <summary>Pushes the current track and position to the home-screen widgets.</summary>
    private void PushWidget()
    {
        var media = _player.CurrentMedia;
        var active = media is not null && (IsPlaying || _player.PlayState == PlayState.Paused);
        var artist = active ? media!.GetAttribute("Author") : string.Empty;
        if (active && artist.Length == 0)
            artist = media!.GetAttribute("Album");
        var cover = active && _art is not null && _artFor == media!.Source ? _art : null;
        PardehWidget.Push(AppContext, new WidgetState(active ? media!.Name : null, artist, IsPlaying, cover,
            active ? _player.CurrentPosition : 0, active ? _player.Duration : 0, _player.Shuffle, _player.Loop, active && _player.IsFavorite(media)));
    }

    private void RefreshCore()
    {
        var media = _player.CurrentMedia;
        var playing = IsPlaying;
        var paused = _player.PlayState == PlayState.Paused;
        if (media is null || !(playing || paused))
        {
            Release();
            _session.SetPlaybackState(new PlaybackState.Builder().SetState(PlaybackStateCode.Stopped, 0, 0)!.Build());
            _metadataKey = null;
            return;
        }

        var title = media.Name;
        var artist = media.GetAttribute("Author");
        var album = media.GetAttribute("Album");
        UpdateMetadata(media, title, artist, album);
        UpdateState(playing);
        if (_art is null || _artFor != media.Source)
            LoadArt(media);

        _notification = BuildNotification(title, artist.Length > 0 ? artist : album, artist.Length > 0 ? album : string.Empty, playing);
        if (playing)
        {
            TakeFocus();
            if (_wake is { IsHeld: false })
                _wake.Acquire();
            if (!_timer.IsEnabled)
                _timer.Start();
            if (!_foreground)
            {
                _foreground = true;
                try
                {
                    var intent = new Intent(AppContext, typeof(PlaybackService));
                    if (OperatingSystem.IsAndroidVersionAtLeast(26))
                        AppContext.StartForegroundService(intent);
                    else
                        AppContext.StartService(intent);
                }
                catch (Exception ex)
                {
                    _foreground = false;
                    _log("Cannot start the playback service: " + ex.Message);
                }
            }
            else
            {
                _notifications.Notify(NotificationId, _notification);
            }
        }
        else
        {
            _timer.Stop();
            if (_wake is { IsHeld: true })
                _wake.Release();
            PlaybackService.Current?.Detach(); // paused: the notification stays but can be swiped away
            _foreground = false;
            _notifications.Notify(NotificationId, _notification);
        }
    }

    /// <summary>The session's metadata: everything a media card shows (title, artist, album, duration, cover); only rewritten when something in it changed.</summary>
    private void UpdateMetadata(MediaItem media, string title, string artist, string album)
    {
        var duration = _player.Duration > 0 ? _player.Duration : media.Duration.TotalSeconds;
        var haveArt = _art is not null && _artFor == media.Source;
        var favorite = _player.IsFavorite(media);
        var key = $"{media.Source}|{title}|{artist}|{album}|{(long)(duration * 1000)}|{haveArt}|{favorite}";
        if (key == _metadataKey)
            return;
        _metadataKey = key;
        var subtitle = artist.Length > 0 && album.Length > 0 ? artist + " - " + album : artist.Length > 0 ? artist : album;
        var metadata = new MediaMetadata.Builder();
        metadata.PutString(MediaMetadata.MetadataKeyTitle, title);
        metadata.PutString(MediaMetadata.MetadataKeyArtist, artist);
        metadata.PutString(MediaMetadata.MetadataKeyAlbum, album);
        metadata.PutString(MediaMetadata.MetadataKeyDisplayTitle, title);
        metadata.PutString(MediaMetadata.MetadataKeyDisplaySubtitle, subtitle);
        metadata.PutString(MediaMetadata.MetadataKeyDisplayDescription, album);
        metadata.PutString(MediaMetadata.MetadataKeyMediaId, media.Source);
        metadata.PutLong(MediaMetadata.MetadataKeyDuration, (long)(duration * 1000));
        if (_player.Favorites is not null)
            metadata.PutRating(MediaMetadata.MetadataKeyUserRating, Rating.NewHeartRating(favorite)); // the heart of the favourite state
        if (media.GetAttribute("Genre") is { Length: > 0 } genre)
            metadata.PutString(MediaMetadata.MetadataKeyGenre, genre);
        if (long.TryParse(media.GetAttribute("TrackNumber"), out var track) && track > 0)
            metadata.PutLong(MediaMetadata.MetadataKeyTrackNumber, track);
        if (long.TryParse(media.GetAttribute("Year"), out var year) && year > 0)
            metadata.PutLong(MediaMetadata.MetadataKeyYear, year);
        if (haveArt)
        {
            // The three keys systems look at: the media card, the lock screen and the notification shade read different ones.
            metadata.PutBitmap(MediaMetadata.MetadataKeyAlbumArt, _art);
            metadata.PutBitmap(MediaMetadata.MetadataKeyArt, _art);
            metadata.PutBitmap(MediaMetadata.MetadataKeyDisplayIcon, _art);
        }

        _session.SetMetadata(metadata.Build());
    }

    /// <summary>The playback state: position, speed (so the card's seek bar moves by itself), update time, actions and the two skip actions.</summary>
    private void UpdateState(bool playing)
    {
        const long Actions = PlaybackState.ActionPlay | PlaybackState.ActionPause | PlaybackState.ActionPlayPause | PlaybackState.ActionSkipToNext
            | PlaybackState.ActionSkipToPrevious | PlaybackState.ActionSeekTo | PlaybackState.ActionStop
            | PlaybackState.ActionRewind | PlaybackState.ActionFastForward // the headset's rewind / fast-forward keys skip 10 s too
            | PlaybackState.ActionSetRating;                                // a controller may heart / un-heart the track
        _statePosition = _player.CurrentPosition;
        _stateSpeed = playing ? _player.PlayState == PlayState.Playing ? _player.Rate : 1 : 0;
        var builder = new PlaybackState.Builder();
        builder.SetActions(Actions);
        builder.SetState(playing ? PlaybackStateCode.Playing : PlaybackStateCode.Paused, (long)(_statePosition * 1000), (float)_stateSpeed);
        AddSkipAction(builder, ActionBack10, "Back 10 seconds", "ic_notif_back10");
        AddSkipAction(builder, ActionForward10, "Forward 10 seconds", "ic_notif_fwd10");
        // Shuffle and repeat: the card shows them where it has room (the 5-button notification below is full); dimmed when off.
        AddSkipAction(builder, ActionShuffle, _player.Shuffle ? "Shuffle on" : "Shuffle off", _player.Shuffle ? "ic_notif_shuffle_on" : "ic_notif_shuffle_off");
        AddSkipAction(builder, ActionRepeat, _player.Loop ? "Repeat on" : "Repeat off", _player.Loop ? "ic_notif_repeat_on" : "ic_notif_repeat_off");
        _session.SetPlaybackState(builder.Build());
        _stateAt = SystemClock.ElapsedRealtime();
    }

    private static void AddSkipAction(PlaybackState.Builder builder, string action, string name, string icon)
    {
        if (DrawableId(icon) is var id and not 0)
            builder.AddCustomAction(new PlaybackState.CustomAction.Builder(action, name, id).Build());
    }

    private static int DrawableId(string name) => AppContext.Resources?.GetIdentifier(name, "drawable", AppContext.PackageName) ?? 0;

    /// <summary>Rewrites the playback state when the real position is more than <paramref name="tolerance"/> seconds away from where the system extrapolates it to.</summary>
    private void SyncPosition(double tolerance)
    {
        if (_player.CurrentMedia is null || !(IsPlaying || _player.PlayState == PlayState.Paused))
            return;
        var expected = _statePosition + (SystemClock.ElapsedRealtime() - _stateAt) / 1000.0 * _stateSpeed;
        if (Math.Abs(_player.CurrentPosition - expected) > tolerance)
            UpdateState(IsPlaying);
    }

    /// <summary>Every two seconds while playing: the duration may only be known once the file is open, and the position may have drifted (a long pause of the app, a seek by a skin).</summary>
    private void Heartbeat()
    {
        if (_player.CurrentMedia is not { } media || !IsPlaying)
            return;
        if (_player.Duration > 0 && !(_metadataKey ?? string.Empty).Contains($"|{(long)(_player.Duration * 1000)}|", StringComparison.Ordinal))
            UpdateMetadata(media, media.Name, media.GetAttribute("Author"), media.GetAttribute("Album"));
        SyncPosition(1.5);
        PushWidget(); // the widgets' seekbar (limited to one repaint every few seconds inside)
        if (Pardeh.UI.AppSettings.Current.NotificationCustomCard && SystemClock.ElapsedRealtime() - _cardRefreshedAt > 5000)
        {
            _cardRefreshedAt = SystemClock.ElapsedRealtime();
            Refresh(); // the Pardeh notification card's seekbar and times (a custom notification does not move by itself)
        }

    }

    /// <summary>The notification the service shows while it runs in the foreground.</summary>
    public static Notification? CurrentNotification => _current?._notification;

    private Notification BuildNotification(string title, string text, string album, bool playing)
    {
        PendingIntent Button(string action) => PendingIntent.GetService(AppContext, action.GetHashCode(), new Intent(AppContext, typeof(PlaybackService)).SetAction(action), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
        Notification.Action Act(string icon, int fallback, string label, string action) =>
            new Notification.Action.Builder(Icon.CreateWithResource(AppContext, DrawableId(icon) is var id and not 0 ? id : fallback), new Java.Lang.String(label), Button(action)).Build();
        var builder = OperatingSystem.IsAndroidVersionAtLeast(26) ? new Notification.Builder(AppContext, ChannelId) : new Notification.Builder(AppContext);
        var small = AppContext.Resources?.GetIdentifier("ic_stat_pardeh", "drawable", AppContext.PackageName) ?? 0;
        builder.SetSmallIcon(small != 0 ? small : global::Android.Resource.Drawable.IcMediaPlay)
            .SetContentTitle(title)
            .SetContentText(text)
            .SetOngoing(playing)
            .SetShowWhen(false)
            .SetOnlyAlertOnce(true)
            .SetCategory(Notification.CategoryTransport)
            .SetVisibility(NotificationVisibility.Public)
            .SetContentIntent(_session.Controller.SessionActivity)
            .SetDeleteIntent(Button(ActionStop))
            // Five buttons, as the expanded card of other players shows them; the collapsed one keeps previous, play / pause and next.
            .AddAction(Act("ic_notif_back10", global::Android.Resource.Drawable.IcMediaRew, "Back 10 seconds", ActionBack10))
            .AddAction(Act("ic_notif_prev", global::Android.Resource.Drawable.IcMediaPrevious, "Previous", ActionPrevious))
            .AddAction(playing
                ? Act("ic_notif_pause", global::Android.Resource.Drawable.IcMediaPause, "Pause", ActionPause)
                : Act("ic_notif_play", global::Android.Resource.Drawable.IcMediaPlay, "Play", ActionPlay))
            .AddAction(Act("ic_notif_next", global::Android.Resource.Drawable.IcMediaNext, "Next", ActionNext))
            .AddAction(Act("ic_notif_fwd10", global::Android.Resource.Drawable.IcMediaFf, "Forward 10 seconds", ActionForward10))
            .SetStyle(new Notification.MediaStyle().SetMediaSession(_session.SessionToken)!.SetShowActionsInCompactView(1, 2, 3));
        var customCard = false;
        if (Pardeh.UI.AppSettings.Current.NotificationCustomCard && BuildCustomCard(title, text, playing, Button) is { } card)
        {
            // Own design. Android draws the media card of a MediaStyle notification itself and ignores custom layouts there, so this
            // notification is a plain custom one (collapsed + expanded layout, its own buttons); the media session stays active
            // (lock screen metadata, headset and car keys). The system player card does not appear for it.
            customCard = true;
            builder.SetActions(Array.Empty<Notification.Action>());
            builder.SetStyle(new Notification.DecoratedCustomViewStyle());
            builder.SetCustomBigContentView(card);
            builder.SetCustomContentView(card); // full size from the start: the same card collapsed and expanded
            // The whole card (also around the picture and behind the header row) takes the cover's colour; the blurred picture fades into it.
            if (_cardColor != 0 && OperatingSystem.IsAndroidVersionAtLeast(26))
                builder.SetColor(_cardColor).SetColorized(true);
        }

        if (album.Length > 0)
            builder.SetSubText(album);
        if (customCard)
        {
            // The custom card draws the cover itself; no large icon (it would take width from the card).
        }
        else if (_art is not null && _artFor == _player.CurrentMedia?.Source)
        {
            builder.SetLargeIcon(_art); // the cover
            if ((AccentColor() ?? _artColor) is { } color && OperatingSystem.IsAndroidVersionAtLeast(26))
                builder.SetColor(color).SetColorized(true);
        }
        else if (LogoBitmap() is { } logo)
        {
            builder.SetLargeIcon(logo); // no cover: the app logo
        }

        return builder.Build()!;
    }

    /// <summary>The blurred cover for the card background: the cover shrunk hard and stretched back, with its edges fading out so it melts into the card colour around it.</summary>
    private static Bitmap FeatheredBlur(Bitmap cover, int width, int height, float feather)
    {
        var bmp = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!)!;
        using var canvas = new Canvas(bmp);
        canvas.DrawBitmap(CoverBlur.Render(cover, width, height, 8), 0, 0, null);
        // Two passes of a destination-in mask: horizontal and vertical fades (transparent at the edge, opaque inside).
        using var horizontal = new Paint { AntiAlias = true };
        horizontal.SetXfermode(new PorterDuffXfermode(PorterDuff.Mode.DstIn!));
        var fx = feather;
        horizontal.SetShader(new LinearGradient(0, 0, width, 0, new[] { unchecked((int)0x00FFFFFF), unchecked((int)0xFFFFFFFF), unchecked((int)0xFFFFFFFF), unchecked((int)0x00FFFFFF) }, new[] { 0f, fx, 1f - fx, 1f }, Shader.TileMode.Clamp!));
        canvas.DrawRect(0, 0, width, height, horizontal);
        using var vertical = new Paint { AntiAlias = true };
        vertical.SetXfermode(new PorterDuffXfermode(PorterDuff.Mode.DstIn!));
        var fy = Math.Min(0.42f, feather * 0.9f * width / Math.Max(1f, height));
        vertical.SetShader(new LinearGradient(0, 0, 0, height, new[] { unchecked((int)0x00FFFFFF), unchecked((int)0xFFFFFFFF), unchecked((int)0xFFFFFFFF), unchecked((int)0x00FFFFFF) }, new[] { 0f, fy, 1f - fy, 1f }, Shader.TileMode.Clamp!));
        canvas.DrawRect(0, 0, width, height, vertical);
        return bmp;
    }

    /// <summary>The cover's average colour, darkened: the colour of the whole notification card behind the blurred picture.</summary>
    private static int CardColor(Bitmap tiny)
    {
        using var one = Bitmap.CreateScaledBitmap(tiny, 1, 1, true)!;
        var hsv = new float[3];
        Color.ColorToHSV(new Color(one.GetPixel(0, 0)), hsv);
        hsv[1] = Math.Min(1f, hsv[1] * 1.1f);
        hsv[2] = Math.Clamp(hsv[2] * 0.62f, 0.14f, 0.6f);
        return Color.HSVToColor(hsv).ToArgb();
    }

    /// <summary>The optional "Pardeh" shade card: a blurred cover behind the title, artist and a seven-button row. Only this app's notification uses it.</summary>
    private RemoteViews? BuildCustomCard(string title, string artist, bool playing, Func<string, PendingIntent> button, string layoutName = "notification_pardeh")
    {
        var pkg = AppContext.PackageName;
        var layout = AppContext.Resources?.GetIdentifier(layoutName, "layout", pkg) ?? 0;
        if (layout == 0)
            return null;
        int Id(string n) => AppContext.Resources?.GetIdentifier(n, "id", pkg) ?? 0;
        var rv = new RemoteViews(pkg, layout);
        rv.SetTextViewText(Id("pd_title"), title);
        rv.SetTextViewText(Id("pd_artist"), artist);
        var hasArt = _art is not null && _artFor == _player.CurrentMedia?.Source;
        var cover = hasArt ? _art : LogoBitmap();
        if (cover is not null)
        {
            rv.SetImageViewBitmap(Id("pd_cover"), cover);
            // Blur: shrink hard, then stretch back with filtering.
            using var tiny = Bitmap.CreateScaledBitmap(cover, 12, 12, true)!;
            // The picture's own height sets the card's height (the layout is wrap_content), so it is made the card's size.
            var density = AppContext.Resources?.DisplayMetrics?.Density ?? 2f;
            var heightDp = layoutName.EndsWith("_small", StringComparison.Ordinal) ? 52 : 162;
            rv.SetImageViewBitmap(Id("pd_bg"), FeatheredBlur(cover, (int)(360 * density / 2), (int)(heightDp * density), 0.26f));
            _cardColor = CardColor(tiny);
        }
        void Btn(string id, string icon, string action)
        {
            if (Id(id) == 0)
                return; // the collapsed layout has fewer buttons
            if (DrawableId(icon) is var d and not 0)
                rv.SetImageViewResource(Id(id), d);
            rv.SetOnClickPendingIntent(Id(id), button(action));
        }
        Btn("pd_shuffle", _player.Shuffle ? "ic_notif_shuffle_on" : "ic_notif_shuffle_off", ActionShuffle);
        Btn("pd_back", "ic_notif_back10", ActionBack10);
        Btn("pd_prev", "ic_notif_prev", ActionPrevious);
        Btn("pd_play", playing ? "ic_notif_pause" : "ic_notif_play", playing ? ActionPause : ActionPlay);
        Btn("pd_next", "ic_notif_next", ActionNext);
        Btn("pd_fwd", "ic_notif_fwd10", ActionForward10);
        Btn("pd_repeat", _player.Loop ? "ic_notif_repeat_on" : "ic_notif_repeat_off", ActionRepeat);
        if (Id("pd_progress") != 0 && _player.Duration > 0)
        {
            // Seekbar: a picture of the bar and 20 tap zones over it (a notification cannot host a slider); a tap jumps there.
            var position = _player.CurrentPosition;
            var duration = _player.Duration;
            rv.SetImageViewBitmap(Id("pd_progress"), PardehWidget.DrawBar(Math.Clamp(position / duration, 0, 1), unchecked((int)0xFF5CC8FF), unchecked((int)0x55FFFFFF)));
            rv.SetTextViewText(Id("pd_pos"), PardehWidget.Format(position));
            rv.SetTextViewText(Id("pd_dur"), PardehWidget.Format(duration));
            for (var i = 0; i < 20; i++)
                rv.SetOnClickPendingIntent(Id("pd_s" + i), button(ActionSeekPrefix + (i * 50 + 25)));
        }

        return rv;
    }

    /// <summary>The skin colour (Glass tint) as an ARGB int when "Colour the notification" is on, otherwise null (the cover colour is used).</summary>
    internal static int? AccentColor(bool force = false)
    {
        var s = Pardeh.UI.AppSettings.Current;
        if (!force && !s.NotificationUsesAccent)
            return null;
        var hsv = new float[] { (float)(((s.GlassTintHue % 360) + 360) % 360), (float)Math.Clamp(s.GlassTintSaturation * 0.8, 0, 1), (float)Math.Clamp(s.GlassTintBrightness * 0.6, 0.25, 0.9) };
        return Color.HSVToColor(hsv).ToArgb();
    }

    private static Bitmap? _logo;
    private static bool _logoClassic;

    private static Bitmap? LogoBitmap()
    {
        var classic = Pardeh.UI.AppSettings.Current.ClassicBranding;
        if (_logo is not null && _logoClassic == classic)
            return _logo;
        _logoClassic = classic;
        try
        {
            var id = AppContext.Resources?.GetIdentifier(classic ? "classic_flag" : "pardeh_logo", "drawable", AppContext.PackageName) ?? 0;
            if (id == 0 || AppContext.GetDrawable(id) is not { } drawable)
                return null;
            var bitmap = Bitmap.CreateBitmap(256, 256, Bitmap.Config.Argb8888!)!;
            drawable.SetBounds(0, 0, 256, 256);
            drawable.Draw(new Canvas(bitmap));
            return _logo = bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void LoadArt(MediaItem media)
    {
        if (_artFor == media.Source)
            return;
        _artFor = media.Source;
        _art = null;
        _artColor = null;
        _ = Task.Run(() =>
        {
            try
            {
                var bitmap = DecodeCover(FindCover(media));
                if (bitmap is null)
                {
                    _log("Media session: no cover art for " + media.Name);
                    return;
                }

                var color = AverageColor(bitmap);
                Dispatcher.UIThread.Post(() =>
                {
                    if (_artFor == media.Source)
                    {
                        _art = bitmap;
                        _artColor = color;
                        _log($"Media session: cover art {bitmap.Width}x{bitmap.Height} for {media.Name}");
                        Refresh();
                    }
                });
            }
            catch (Exception ex)
            {
                _log("Media session: cover art failed: " + ex.Message); // no picture: the notification has the logo
            }
        });
    }

    /// <summary>
    /// The cover of an item, as image bytes: the picture embedded in the file or a folder image (what the player itself shows), else what
    /// Android can find: the embedded picture read by MediaMetadataRetriever (more formats than TagLib), then the MediaStore thumbnail of the
    /// file (the album art the system's own scanner knows, e.g. a folder.jpg a Device music file sits next to).
    /// </summary>
    private byte[]? FindCover(MediaItem media)
    {
        try
        {
            if (_player.GetAlbumArt(media) is { Length: > 0 } own)
                return own;
        }
        catch (Exception ex)
        {
            _log("Media session: tag cover failed: " + ex.Message);
        }

        try
        {
            using var retriever = new MediaMetadataRetriever();
            if (media.Source.StartsWith("content://", StringComparison.Ordinal))
                retriever.SetDataSource(AppContext, global::Android.Net.Uri.Parse(media.Source)!);
            else if (File.Exists(media.Source))
                retriever.SetDataSource(media.Source);
            else
                return null;
            if (retriever.GetEmbeddedPicture() is { Length: > 0 } embedded)
                return embedded;
        }
        catch (Exception)
        {
            // No picture that way.
        }

        return StoreThumbnail(media.Source);
    }

    /// <summary>MediaStore's thumbnail of an audio file (API 29+), as PNG bytes; null when the system knows none.</summary>
    private static byte[]? StoreThumbnail(string source)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
            return null;
        try
        {
            global::Android.Net.Uri? uri = null;
            if (source.StartsWith("content://media/", StringComparison.Ordinal))
            {
                uri = global::Android.Net.Uri.Parse(source);
            }
            else if (!source.StartsWith("content://", StringComparison.Ordinal) && AppContext.ContentResolver is { } resolver)
            {
                using var cursor = resolver.Query(global::Android.Provider.MediaStore.Audio.Media.ExternalContentUri!, ["_id"], "_data=?", [source], null);
                if (cursor is not null && cursor.MoveToFirst())
                    uri = ContentUris.WithAppendedId(global::Android.Provider.MediaStore.Audio.Media.ExternalContentUri!, cursor.GetLong(0));
            }

            if (uri is null || AppContext.ContentResolver is not { } contentResolver)
                return null;
            using var thumbnail = contentResolver.LoadThumbnail(uri, new global::Android.Util.Size(ArtSize, ArtSize), null);
            if (thumbnail is null)
                return null;
            using var stream = new MemoryStream();
            thumbnail.Compress(Bitmap.CompressFormat.Png!, 100, stream);
            return stream.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Decodes image bytes to a bitmap whose longest side is at most <see cref="ArtSize"/>, keeping the proportions.</summary>
    private static Bitmap? DecodeCover(byte[]? data)
    {
        if (data is not { Length: > 0 })
            return null;
        var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeByteArray(data, 0, data.Length, bounds);
        var sample = 1;
        while (bounds.OutWidth / (sample * 2) >= ArtSize && bounds.OutHeight / (sample * 2) >= ArtSize)
            sample *= 2;
        var decoded = BitmapFactory.DecodeByteArray(data, 0, data.Length, new BitmapFactory.Options { InSampleSize = sample });
        if (decoded is null)
            return null;
        var longest = Math.Max(decoded.Width, decoded.Height);
        if (longest <= ArtSize)
            return decoded;
        var k = (double)ArtSize / longest;
        return Bitmap.CreateScaledBitmap(decoded, Math.Max(1, (int)Math.Round(decoded.Width * k)), Math.Max(1, (int)Math.Round(decoded.Height * k)), true);
    }

    /// <summary>
    /// A colour for the notification taken from the cover: the average of a 12 x 12 version weighted towards saturated pixels, then darkened
    /// so that white text on it stays readable (a grey cover gives a neutral dark grey).
    /// </summary>
    private static int? AverageColor(Bitmap art)
    {
        try
        {
            using var tiny = Bitmap.CreateScaledBitmap(art, 12, 12, true)!;
            double r = 0, g = 0, b = 0, weight = 0;
            for (var y = 0; y < 12; y++)
            {
                for (var x = 0; x < 12; x++)
                {
                    var px = tiny.GetPixel(x, y);
                    int pr = (px >> 16) & 0xFF, pg = (px >> 8) & 0xFF, pb = px & 0xFF;
                    var max = Math.Max(pr, Math.Max(pg, pb));
                    var min = Math.Min(pr, Math.Min(pg, pb));
                    var s = max == 0 ? 0 : (max - min) / (double)max;
                    var w = 0.05 + s * s;
                    r += pr * w;
                    g += pg * w;
                    b += pb * w;
                    weight += w;
                }
            }

            var hsv = new float[3];
            Color.RGBToHSV((int)(r / weight), (int)(g / weight), (int)(b / weight), hsv);
            hsv[1] = Math.Min(hsv[1], 0.75f);
            hsv[2] = Math.Clamp(hsv[2], 0.18f, 0.42f);
            return Color.HSVToColor(hsv);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void TakeFocus()
    {
        if (_audio is null || _focus is not null || !OperatingSystem.IsAndroidVersionAtLeast(26))
            return;
        _focus = new AudioFocusRequestClass.Builder(AudioFocus.Gain)
            .SetAudioAttributes(new AudioAttributes.Builder()!.SetUsage(AudioUsageKind.Media)!.SetContentType(AudioContentType.Music)!.Build()!)
            .SetOnAudioFocusChangeListener(new FocusListener(this))!
            .Build()!;
        _audio.RequestAudioFocus(_focus);
    }

    private void OnFocus(AudioFocus change)
    {
        switch (change)
        {
            case AudioFocus.Loss:
                _resumeOnFocus = false;
                _player.Pause();
                break;
            case AudioFocus.LossTransientCanDuck:
                // A navigation prompt or a notification sound: keep playing, quieter.
                if (_duckedFrom is null && _player.PlayState == PlayState.Playing)
                {
                    _duckedFrom = _player.Volume;
                    _player.Volume = _duckedFrom.Value * 0.3;
                }

                break;
            case AudioFocus.LossTransient:
                _resumeOnFocus = _player.PlayState == PlayState.Playing;
                _player.Pause();
                break;
            case AudioFocus.Gain:
                if (_duckedFrom is { } volume)
                {
                    _player.Volume = volume;
                    _duckedFrom = null;
                }

                if (_resumeOnFocus)
                {
                    _resumeOnFocus = false;
                    _player.Play();
                }

                break;
        }
    }

    private double? _duckedFrom;

    /// <summary>Headphones pulled out or Bluetooth lost: pause instead of playing through the speaker.</summary>
    private sealed class NoisyReceiver : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action == AudioManager.ActionAudioBecomingNoisy)
                Handle(ActionPause);
        }
    }

    private readonly NoisyReceiver _noisy = new();

    private void Release()
    {
        _timer.Stop();
        if (_wake is { IsHeld: true })
            _wake.Release();
        if (_focus is not null && _audio is not null && OperatingSystem.IsAndroidVersionAtLeast(26))
            _audio.AbandonAudioFocusRequest(_focus);
        _focus = null;
        _foreground = false;
        _notification = null;
        PlaybackService.Current?.Quit();
        _notifications.Cancel(NotificationId);
    }

    public void Dispose()
    {
        _player.PlayStateChanged -= OnChanged;
        _player.CurrentMediaChanged -= OnChanged;
        _player.FavoritesChanged -= OnChanged;
        _player.Seeked -= OnSeeked;
        try
        {
            AppContext.UnregisterReceiver(_noisy);
        }
        catch (Exception)
        {
            // Not registered.
        }

        Release();
        _session.Active = false;
        _session.Release();
        if (ReferenceEquals(_current, this))
            _current = null;
    }

    private sealed class SessionCallback(MediaSessionController owner) : MediaSession.Callback
    {
        public override void OnPlay() => Handle(ActionPlay);

        public override void OnPause() => Handle(ActionPause);

        public override void OnSkipToNext() => Handle(ActionNext);

        public override void OnSkipToPrevious() => Handle(ActionPrevious);

        public override void OnStop() => Handle(ActionStop);

        public override void OnRewind() => Handle(ActionBack10);

        public override void OnFastForward() => Handle(ActionForward10);

        public override void OnCustomAction(string? action, Bundle? extras) => Handle(action);

        /// <summary>A controller (a car, a watch, another player's UI) sets the heart of the playing track.</summary>
        public override void OnSetRating(Rating? rating) => Dispatcher.UIThread.Post(() =>
        {
            if (rating is not null && rating.RatingStyle == RatingStyle.Heart)
                owner._player.SetFavorite(rating.HasHeart);
            owner.Refresh();
        });

        public override void OnSeekTo(long pos) => Dispatcher.UIThread.Post(() =>
        {
            owner._player.CurrentPosition = pos / 1000.0;
            owner.Refresh();
        });
    }

    private sealed class FocusListener(MediaSessionController owner) : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
    {
        public void OnAudioFocusChange(AudioFocus focusChange) => Dispatcher.UIThread.Post(() => owner.OnFocus(focusChange));
    }
}

/// <summary>The foreground service behind background playback: it shows the media notification and relays its buttons.</summary>
[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
public sealed class PlaybackService : Service
{
    internal static PlaybackService? Current { get; private set; }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        Current = this;
        if (intent?.Action is { } action)
            MediaSessionController.Handle(action);
        if (MediaSessionController.CurrentNotification is { } notification)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
                StartForeground(MediaSessionController.NotificationId, notification, ForegroundService.TypeMediaPlayback);
            else
                StartForeground(MediaSessionController.NotificationId, notification);
        }

        return StartCommandResult.NotSticky;
    }

    /// <summary>Leaves the foreground but keeps the notification (paused).</summary>
    internal void Detach() => StopForeground(StopForegroundFlags.Detach);

    internal void Quit()
    {
        StopForeground(StopForegroundFlags.Remove);
        StopSelf();
    }

    public override void OnDestroy()
    {
        if (ReferenceEquals(Current, this))
            Current = null;
        base.OnDestroy();
    }
}
