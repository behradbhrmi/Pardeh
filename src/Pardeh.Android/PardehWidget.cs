using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;

namespace Pardeh.Android;

/// <summary>What the widgets show right now (pushed by the media session controller).</summary>
internal sealed record WidgetState(string? Title, string Artist, bool Playing, Bitmap? Cover, double Position, double Duration, bool Shuffle, bool Loop, bool Favorite);

/// <summary>
/// The home-screen widget, customizable per placed widget (<see cref="WidgetConfig"/>, set in <see cref="WidgetConfigActivity"/>):
/// theme, opacity, text size, and which parts show (cover, seekbar, skip, shuffle, repeat, favourite). The seekbar is drawn as a picture
/// with 20 tap zones over it (widgets cannot host a draggable slider): a tap jumps to that place.
/// </summary>
[BroadcastReceiver(Label = "Pardeh player", Exported = true)]
[IntentFilter(new[] { "android.appwidget.action.APPWIDGET_UPDATE" })]
[MetaData("android.appwidget.provider", Resource = "@xml/pardeh_widget_info")]
public sealed class PardehWidget : AppWidgetProvider
{
    private const int Zones = 20;
    private static WidgetState _state = new(null, string.Empty, false, null, 0, 0, false, false, false);
    private static long _pushedAt;

    /// <summary>Pushes the state to every placed widget. A position-only change is limited to one repaint every few seconds (each one carries pictures).</summary>
    internal static void Push(Context context, WidgetState state)
    {
        var changed = _state.Title != state.Title || _state.Playing != state.Playing || !ReferenceEquals(_state.Cover, state.Cover)
            || _state.Shuffle != state.Shuffle || _state.Loop != state.Loop || _state.Favorite != state.Favorite || _state.Artist != state.Artist
            || Math.Abs(_state.Duration - state.Duration) > 0.5;
        var now = SystemClock.ElapsedRealtime();
        // A jump of the position (a seek, a skip) repaints at once; the normal advance does not.
        var expected = _state.Position + (_state.Playing ? (now - _pushedAt) / 1000.0 : 0);
        changed |= Math.Abs(state.Position - expected) > 2.5;
        if (!changed && now - _pushedAt < 4500)
            return;
        _state = state;
        _pushedAt = now;
        try
        {
            var manager = AppWidgetManager.GetInstance(context);
            var ids = manager?.GetAppWidgetIds(new ComponentName(context, Java.Lang.Class.FromType(typeof(PardehWidget))));
            if (manager is null || ids is not { Length: > 0 })
                return;
            foreach (var id in ids)
                manager.UpdateAppWidget(id, Build(context, manager, id));
        }
        catch (Exception)
        {
            // A widget host that is not ready: the next push or update repaints it.
        }
    }

    /// <summary>Repaints one widget (after its settings changed).</summary>
    internal static void Refresh(Context context, int id)
    {
        var manager = AppWidgetManager.GetInstance(context);
        manager?.UpdateAppWidget(id, Build(context, manager, id));
    }

    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        if (context is null || appWidgetManager is null || appWidgetIds is null)
            return;
        foreach (var id in appWidgetIds)
            appWidgetManager.UpdateAppWidget(id, Build(context, appWidgetManager, id));
    }

    public override void OnAppWidgetOptionsChanged(Context? context, AppWidgetManager? appWidgetManager, int appWidgetId, Bundle? newOptions)
    {
        if (context is not null && appWidgetManager is not null)
            appWidgetManager.UpdateAppWidget(appWidgetId, Build(context, appWidgetManager, appWidgetId));
    }

    public override void OnDeleted(Context? context, int[]? appWidgetIds)
    {
        if (context is not null && appWidgetIds is not null)
            foreach (var id in appWidgetIds)
                WidgetConfig.Delete(context, id);
    }

    private static RemoteViews Build(Context context, AppWidgetManager manager, int widgetId)
    {
        var cfg = WidgetConfig.Load(context, widgetId);
        var s = _state;
        var pkg = context.PackageName!;
        var res = context.Resources!;
        int Id(string n) => res.GetIdentifier(n, "id", pkg);
        int Drawable(string n) => res.GetIdentifier(n, "drawable", pkg);
        var rv = new RemoteViews(pkg, res.GetIdentifier("widget_pardeh", "layout", pkg));
        var idle = string.IsNullOrEmpty(s.Title);

        // Size in dp (the launcher reports the current cell area): small widgets drop parts instead of clipping them.
        var options = manager.GetAppWidgetOptions(widgetId);
        var wDp = options?.GetInt(AppWidgetManager.OptionAppwidgetMinWidth, 250) ?? 250;
        var hDp = options?.GetInt(AppWidgetManager.OptionAppwidgetMinHeight, 110) ?? 110;
        var narrow = wDp < 230;
        var showSeek = cfg.Seekbar && hDp >= 128 && !idle;
        var showControls = hDp >= 88;
        var density = res.DisplayMetrics?.Density ?? 2f;

        // Colours follow the theme.
        var light = cfg.Theme == WidgetConfig.ThemeLight;
        var text = light ? unchecked((int)0xFF14213D) : unchecked((int)0xFFFFFFFF);
        var sub = light ? unchecked((int)0xFF4A5675) : unchecked((int)0xB3FFFFFF);
        var accent = MediaSessionController.AccentColor(true) ?? unchecked((int)0xFF2563D6);
        var barFill = cfg.Theme == WidgetConfig.ThemeAccent ? unchecked((int)0xFFFFFFFF) : light ? accent : unchecked((int)0xFF5CC8FF);

        // Background picture.
        var bg = DrawBackground(context, cfg, s, accent, (int)(wDp * density), (int)(hDp * density), density);
        rv.SetImageViewBitmap(Id("wd_bg"), bg);
        rv.SetInt(Id("wd_bg"), "setImageAlpha", (int)(255 * Math.Clamp(cfg.Opacity, 20, 100) / 100.0));

        // Text.
        var title = new[] { 13f, 15f, 18f }[Math.Clamp(cfg.TextSize, 0, 2)];
        rv.SetTextViewText(Id("wd_title"), idle ? "Pardeh" : s.Title);
        rv.SetTextViewText(Id("wd_artist"), idle ? "Tap to open" : s.Artist);
        rv.SetTextColor(Id("wd_title"), new Color(text));
        rv.SetTextColor(Id("wd_artist"), new Color(sub));
        rv.SetTextViewTextSize(Id("wd_title"), (int)ComplexUnitType.Sp, title);
        rv.SetTextViewTextSize(Id("wd_artist"), (int)ComplexUnitType.Sp, title - 2);
        rv.SetTextColor(Id("wd_pos"), new Color(sub));
        rv.SetTextColor(Id("wd_dur"), new Color(sub));

        // Cover.
        rv.SetViewVisibility(Id("wd_cover"), cfg.Cover ? ViewStates.Visible : ViewStates.Gone);
        if (cfg.Cover)
        {
            if (s.Cover is not null && !idle)
                rv.SetImageViewBitmap(Id("wd_cover"), s.Cover);
            else if (Drawable("pardeh_logo") is var logo and not 0)
                rv.SetImageViewResource(Id("wd_cover"), logo);
        }

        PendingIntent Open() => PendingIntent.GetActivity(context, 1, new Intent(context, typeof(MainActivity)).AddFlags(ActivityFlags.SingleTop | ActivityFlags.NewTask), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
        PendingIntent Action(string action) => idle
            ? Open()
            : PendingIntent.GetService(context, action.GetHashCode(), new Intent(context, typeof(PlaybackService)).SetAction(action), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent)!;
        rv.SetOnClickPendingIntent(Id("wd_cover"), Open());
        rv.SetOnClickPendingIntent(Id("wd_open"), Open());

        void Button(string view, string icon, string action, bool visible, int? tint = null)
        {
            var vid = Id(view);
            rv.SetViewVisibility(vid, visible ? ViewStates.Visible : ViewStates.Gone);
            if (!visible)
                return;
            if (Drawable(icon) is var d and not 0)
                rv.SetImageViewResource(vid, d);
            rv.SetInt(vid, "setColorFilter", tint ?? text);
            rv.SetOnClickPendingIntent(vid, Action(action));
        }

        rv.SetViewVisibility(Id("wd_controls"), showControls ? ViewStates.Visible : ViewStates.Gone);
        var dim = light ? unchecked((int)0x8014213D) : unchecked((int)0x80FFFFFF);
        Button("wd_shuffle", s.Shuffle ? "ic_notif_shuffle_on" : "ic_notif_shuffle_off", MediaSessionController.ActionShuffle, cfg.Shuffle && !narrow, s.Shuffle ? barFill : dim);
        Button("wd_back", "ic_notif_back10", MediaSessionController.ActionBack10, cfg.Skip && !narrow);
        Button("wd_prev", "ic_notif_prev", MediaSessionController.ActionPrevious, true);
        Button("wd_play", s.Playing ? "ic_notif_pause" : "ic_notif_play", s.Playing ? MediaSessionController.ActionPause : MediaSessionController.ActionPlay, true);
        Button("wd_next", "ic_notif_next", MediaSessionController.ActionNext, true);
        Button("wd_fwd", "ic_notif_fwd10", MediaSessionController.ActionForward10, cfg.Skip && !narrow);
        Button("wd_repeat", s.Loop ? "ic_notif_repeat_on" : "ic_notif_repeat_off", MediaSessionController.ActionRepeat, cfg.Repeat && !narrow, s.Loop ? barFill : dim);
        Button("wd_fav", s.Favorite ? "ic_notif_heart_on" : "ic_notif_heart_off", MediaSessionController.ActionFavorite, cfg.Favorite && !idle, s.Favorite ? unchecked((int)0xFFFF5C7A) : sub);

        // Seekbar: the bar picture, the times and the tap zones.
        rv.SetViewVisibility(Id("wd_seekrow"), showSeek ? ViewStates.Visible : ViewStates.Gone);
        if (showSeek)
        {
            var fraction = s.Duration > 0 ? Math.Clamp(s.Position / s.Duration, 0, 1) : 0;
            rv.SetImageViewBitmap(Id("wd_progress"), DrawBar(fraction, barFill, light ? unchecked((int)0x3314213D) : unchecked((int)0x40FFFFFF)));
            rv.SetTextViewText(Id("wd_pos"), Format(s.Position));
            rv.SetTextViewText(Id("wd_dur"), Format(s.Duration));
            for (var i = 0; i < Zones; i++)
                rv.SetOnClickPendingIntent(Id("wd_s" + i), Action(MediaSessionController.ActionSeekPrefix + (i * 1000 / Zones + 1000 / Zones / 2)));
        }

        return rv;
    }

    internal static string Format(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    internal static Bitmap DrawBar(double fraction, int fill, int track)
    {
        const int W = 480, H = 28;
        var bmp = Bitmap.CreateBitmap(W, H, Bitmap.Config.Argb8888!)!;
        using var canvas = new Canvas(bmp);
        using var paint = new Paint(PaintFlags.AntiAlias);
        paint.Color = new Color(track);
        canvas.DrawRoundRect(new RectF(8, 10, W - 8, 18), 4, 4, paint);
        paint.Color = new Color(fill);
        var x = 8 + (float)(W - 16) * (float)fraction;
        canvas.DrawRoundRect(new RectF(8, 10, Math.Max(x, 12), 18), 4, 4, paint);
        canvas.DrawCircle(Math.Clamp(x, 12, W - 12), H / 2f, 9, paint);
        return bmp;
    }

    /// <summary>The background picture for the theme (a rounded card; Transparent draws nothing; Blurred cover shrinks the cover hard and darkens it).</summary>
    private static Bitmap? _logoBackdrop;

    /// <summary>The Pardeh logo as a picture to blur, for a widget with no cover to show.</summary>
    private static Bitmap? LogoBackdrop(Context context)
    {
        if (_logoBackdrop is { IsRecycled: false } cached)
            return cached;
        try
        {
            var id = context.Resources?.GetIdentifier("pardeh_logo", "drawable", context.PackageName) ?? 0;
            if (id == 0)
                return null;
            return _logoBackdrop = BitmapFactory.DecodeResource(context.Resources, id, new BitmapFactory.Options { InPreferredConfig = Bitmap.Config.Argb8888 });
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Bitmap DrawBackground(Context context, WidgetConfig cfg, WidgetState s, int accent, int w, int h, float density)
    {
        // RemoteViews bitmaps share a size budget; 960 px on the long side stays under it and keeps the card edges crisp.
        var scale = Math.Min(1f, 960f / Math.Max(1, Math.Max(w, h)));
        w = Math.Max(64, (int)(w * scale));
        h = Math.Max(64, (int)(h * scale));
        var bmp = Bitmap.CreateBitmap(w, h, Bitmap.Config.Argb8888!)!;
        if (cfg.Theme == WidgetConfig.ThemeClear)
            return bmp;
        using var canvas = new Canvas(bmp);
        var radius = 22f * density * scale;
        using var clip = new global::Android.Graphics.Path();
        clip.AddRoundRect(new RectF(0, 0, w, h), radius, radius, global::Android.Graphics.Path.Direction.Cw!);
        canvas.ClipPath(clip);
        using var paint = new Paint(PaintFlags.AntiAlias | PaintFlags.FilterBitmap);
        switch (cfg.Theme)
        {
            case WidgetConfig.ThemeLight:
                paint.Color = new Color(unchecked((int)0xFFF1F4FB));
                canvas.DrawRect(0, 0, w, h, paint);
                break;
            case WidgetConfig.ThemeAccent:
                paint.SetShader(new LinearGradient(0, 0, w, h, new Color(accent), new Color(Darken(accent, 0.55f)), Shader.TileMode.Clamp!));
                canvas.DrawRect(0, 0, w, h, paint);
                break;
            case WidgetConfig.ThemeCover:
                // The cover of the playing track; with none (no picture in the file, nothing playing) the Pardeh logo is blurred instead,
                // so this theme never silently looks like the plain navy one.
                var art = s.Cover ?? LogoBackdrop(context);
                try
                {
                    if (art is not null)
                        canvas.DrawBitmap(CoverBlur.Render(art, w, h, 9), 0, 0, paint);
                    else
                        throw new InvalidOperationException("no picture to blur");
                }
                catch (Exception)
                {
                    paint.SetShader(new LinearGradient(0, 0, w, h, new Color(accent), new Color(Darken(accent, 0.45f)), Shader.TileMode.Clamp!));
                    canvas.DrawRect(0, 0, w, h, paint);
                    paint.SetShader(null);
                }

                // Depth: a dark veil that is stronger at the bottom (under the controls) and a faint highlight along the top edge.
                paint.SetShader(new LinearGradient(0, 0, 0, h, new[] { unchecked((int)0x55000000), unchecked((int)0x8C000000), unchecked((int)0xB8000000) }, new[] { 0f, 0.5f, 1f }, Shader.TileMode.Clamp!));
                canvas.DrawRect(0, 0, w, h, paint);
                // Color, not int: a plain int picks the constructor's "long color" overload, where it is read as a packed ColorLong
                // with a colour space number in its low bits ("Invalid ID: 63", which took the whole app down).
                paint.SetShader(new LinearGradient(0, 0, 0, h * 0.18f, new Color(unchecked((int)0x26FFFFFF)), new Color(unchecked((int)0x00FFFFFF)), Shader.TileMode.Clamp!));
                canvas.DrawRect(0, 0, w, h * 0.18f, paint);
                paint.SetShader(null);
                break;
            default:
                paint.SetShader(new LinearGradient(0, 0, w, h, new Color(unchecked((int)0xFF1B2A55)), new Color(unchecked((int)0xFF0B1020)), Shader.TileMode.Clamp!));
                canvas.DrawRect(0, 0, w, h, paint);
                break;
        }

        return bmp;
    }

    private static int Darken(int argb, float factor)
    {
        var c = new Color(argb);
        return Color.Argb(255, (int)(c.R * factor), (int)(c.G * factor), (int)(c.B * factor));
    }
}
