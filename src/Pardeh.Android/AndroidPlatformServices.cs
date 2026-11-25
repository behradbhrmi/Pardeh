using Android.Content;
using Android.Graphics;
using Android.Widget;
using Avalonia.Controls;
using Pardeh.Core.Playback;
using Pardeh.UI;

namespace Pardeh.Android;

/// <summary>What the Android head provides to the shared UI. Mostly stubs for now; each TODO is a feature to build.</summary>
internal sealed class AndroidPlatformServices : IPlatformServices
{
    private static Context AppContext => global::Android.App.Application.Context;

    public IDisposable? StartSystemIntegration(TopLevel top, MediaPlayer player, Action<string> log)
    {
        // Android 13+: notifications (the media controls) need a runtime permission. Asked after the first frame.
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = MainActivity.Current?.RequestAsync("android.permission.POST_NOTIFICATIONS"), Avalonia.Threading.DispatcherPriority.Background);
        return new MediaSessionController(player, log);
    }

    public bool HasDeviceMedia => true;

    public byte[]? VideoThumbnail(string source, int size)
    {
        try
        {
            using var retriever = new global::Android.Media.MediaMetadataRetriever();
            if (source.StartsWith("content://", StringComparison.Ordinal))
                retriever.SetDataSource(AppContext, global::Android.Net.Uri.Parse(source));
            else
                retriever.SetDataSource(source);
            // A frame a little way in (the very first is often black), scaled down to the row's size.
            using var frame = retriever.GetFrameAtTime(1_000_000, global::Android.Media.Option.ClosestSync);
            if (frame is null)
                return null;
            var width = Math.Max(48, size);
            var height = Math.Max(1, frame.Height * width / Math.Max(1, frame.Width));
            using var scaled = Bitmap.CreateScaledBitmap(frame, width, height, true)!;
            using var stream = new MemoryStream();
            scaled.Compress(Bitmap.CompressFormat.Jpeg!, 80, stream);
            return stream.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public double DisplayDensity => AppContext.Resources?.DisplayMetrics?.Density ?? 0;

    public void ApplyAppBranding(bool classic) => ApplyBranding(classic);

    private static bool? _branding;

    /// <summary>
    /// The launcher entry follows the "Classic branding" switch: of the two activity aliases (see AndroidManifest.xml) exactly
    /// one is enabled, and the recents entry gets the matching title and icon. The launcher may take a few seconds to refresh.
    /// </summary>
    internal static void ApplyBranding(bool classic)
    {
        if (_branding == classic)
            return;
        _branding = classic;
        try
        {
            var pm = AppContext.PackageManager;
            var package = AppContext.PackageName;
            if (pm is not null && package is not null)
            {
                // Enable the wanted alias first, so there is never a moment without a launcher entry.
                var on = new ComponentName(package, classic ? "org.pardeh.player.LauncherClassic" : "org.pardeh.player.LauncherPardeh");
                var off = new ComponentName(package, classic ? "org.pardeh.player.LauncherPardeh" : "org.pardeh.player.LauncherClassic");
                if (pm.GetComponentEnabledSetting(on) != global::Android.Content.PM.ComponentEnabledState.Enabled)
                    pm.SetComponentEnabledSetting(on, global::Android.Content.PM.ComponentEnabledState.Enabled, global::Android.Content.PM.ComponentEnableOption.DontKillApp);
                if (pm.GetComponentEnabledSetting(off) != global::Android.Content.PM.ComponentEnabledState.Disabled)
                    pm.SetComponentEnabledSetting(off, global::Android.Content.PM.ComponentEnabledState.Disabled, global::Android.Content.PM.ComponentEnableOption.DontKillApp);
            }

            if (MainActivity.Current is { } activity)
            {
                var icon = AppContext.Resources?.GetIdentifier(classic ? "ic_launcher_classic_foreground" : "pardeh_logo", "drawable", package) ?? 0;
                Bitmap? bitmap = null;
                if (icon != 0 && activity.GetDrawable(icon) is { } drawable)
                {
                    bitmap = Bitmap.CreateBitmap(192, 192, Bitmap.Config.Argb8888!)!;
                    var canvas = new global::Android.Graphics.Canvas(bitmap);
                    if (classic && AppContext.Resources?.GetIdentifier("ic_launcher_classic_background", "drawable", package) is > 0 and var back && activity.GetDrawable(back) is { } tile)
                    {
                        tile.SetBounds(0, 0, 192, 192);
                        tile.Draw(canvas);
                    }

                    drawable.SetBounds(0, 0, 192, 192);
                    drawable.Draw(canvas);
                }

#pragma warning disable CA1422
                activity.SetTaskDescription(new global::Android.App.ActivityManager.TaskDescription(classic ? "Windows Media Player" : "Pardeh", bitmap));
#pragma warning restore CA1422
            }
        }
        catch (Exception)
        {
            // Branding is cosmetic: never break the start.
        }
    }

    public Task<bool> RequestMediaAccess() =>
        MainActivity.Current is not { } activity ? Task.FromResult(false)
        : activity.RequestAsync(OperatingSystem.IsAndroidVersionAtLeast(33)
            ? ["android.permission.READ_MEDIA_AUDIO", "android.permission.READ_MEDIA_VIDEO"]
            : ["android.permission.READ_EXTERNAL_STORAGE"]);

    // VIEW / SEND intents (and OnNewIntent) arrive here; the view subscribes once it started.
    private static Action<string[]>? _openHandler;
    private static string[]? _pendingOpen;

    internal static void Deliver(string[] sources)
    {
        if (sources.Length == 0)
            return;
        if (_openHandler is { } handler)
            handler(sources);
        else
            _pendingOpen = sources;
    }

    public void ListenForOpenRequests(Action<string[]> received)
    {
        _openHandler = received;
        if (_pendingOpen is { } pending)
        {
            _pendingOpen = null;
            received(pending);
        }
    }

    // A synchronous picker cannot exist on Android: skins calling openDialog get null.
    // The async File > Open already uses the Storage Access Framework through StorageProvider.
    public string? OpenMediaFile(TopLevel? owner) => null;

    public void Inform(TopLevel? owner, string text, string caption) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Toast.MakeText(AppContext, text, ToastLength.Long)?.Show());

    // TODO(android): an in-view confirmation dialog; until then "no" (a skin's web page prompt stays blocked).
    public bool Confirm(TopLevel? owner, string text, string caption) => false;

    public void SetSystemBarIcons(bool lightStatusIcons, bool lightNavigationIcons)
    {
        if (MainActivity.Current?.Window is not { } window)
            return;
        // "Light bars" in Android means dark icons; light icons (for a dark skin) clear the flag.
        const int StatusFlag = 0x2000, NavFlag = 0x10; // LIGHT_STATUS_BAR / LIGHT_NAVIGATION_BAR (also the WindowInsetsController values 8 / 16)
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            var controller = window.InsetsController;
            controller?.SetSystemBarsAppearance(lightStatusIcons ? 0 : 8, 8);
            controller?.SetSystemBarsAppearance(lightNavigationIcons ? 0 : 16, 16);
        }
        else if (window.DecorView is { } decor)
        {
#pragma warning disable CA1416, CS0618
            var flags = (int)decor.SystemUiVisibility;
            flags = lightStatusIcons ? flags & ~StatusFlag : flags | StatusFlag;
            flags = lightNavigationIcons ? flags & ~NavFlag : flags | NavFlag;
            decor.SystemUiVisibility = (global::Android.Views.StatusBarVisibility)flags;
#pragma warning restore CA1416, CS0618
        }
    }

    public bool MoveToBackground() => MainActivity.Current?.MoveTaskToBack(true) == true;

    public bool ExitApp()
    {
        if (MainActivity.Current is not { } activity)
            return false;
        activity.FinishAndRemoveTask();
        return true;
    }

    public void OpenUrl(string url)
    {
        var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(url));
        intent.AddFlags(ActivityFlags.NewTask);
        AppContext.StartActivity(intent);
    }
}
