using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Runtime;
using Avalonia.Android;
using Pardeh.UI;
using AndroidUri = Android.Net.Uri;

namespace Pardeh.Android;

/// <summary>The Android application object: boots Avalonia with <see cref="AndroidApplication"/>.</summary>
[global::Android.App.Application]
public sealed class PardehAndroidApp(IntPtr handle, JniHandleOwnership transfer) : AvaloniaAndroidApplication<AndroidApplication>(handle, transfer)
{
}

[Activity(
    Name = "org.pardeh.player.MainActivity", // stable: the launcher aliases in AndroidManifest.xml target it
    Label = "${appLabel}",
    Theme = "@style/PardehTheme",
    Exported = true,
    // One activity only: the player view can sit in one window. With SingleTop, "open with" while Pardeh ran (started through a
    // launcher alias) made a second activity, which crashed taking the view; SingleTask hands the intent to OnNewIntent.
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
// Open with / Share to Pardeh: audio and video from other apps (content:// and file:// URIs, http streams), and a link
// shared as text.
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataMimeTypes = ["audio/*", "video/*", "application/ogg"])]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataSchemes = ["http", "https", "content", "file"],
    DataMimeTypes = ["audio/*", "video/*", "application/x-mpegURL", "application/vnd.apple.mpegurl"])]
[IntentFilter([Intent.ActionSend], Categories = [Intent.CategoryDefault], DataMimeTypes = ["audio/*", "video/*", "text/plain"])]
[IntentFilter([Intent.ActionSendMultiple], Categories = [Intent.CategoryDefault], DataMimeTypes = ["audio/*", "video/*"])]
public sealed class MainActivity : AvaloniaMainActivity
{
    private static readonly Dictionary<int, TaskCompletionSource<bool>> Requests = [];
    private static int _nextRequest = 100;

    /// <summary>The running activity (system bar icons, moving to the background).</summary>
    internal static MainActivity? Current { get; private set; }

    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        Current = this;
        base.OnCreate(savedInstanceState);
        VolumeControlStream = global::Android.Media.Stream.Music; // the volume keys set the media volume even when nothing plays
        AndroidPlatformServices.ApplyBranding(AppSettings.Current.ClassicBranding);
        HoldSplashUntilFirstSkinFrame();
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
            SplashScreen.SetOnExitAnimationListener(new SplashExit()); // no fade: the system splash window would still take touches while it fades out
        AndroidPlatformServices.Deliver(OpenedFrom(Intent));
    }

    private static volatile bool _skinReady;

    /// <summary>
    /// Keeps the launch splash (system splash on Android 12+, the windowBackground before) up until the first skin frame was
    /// drawn: the activity's first draw is held back by a pre-draw listener. Only the first activity of the process holds
    /// (rotation, resume and re-creation never see the splash again); a timeout keeps a failed start from hanging.
    /// </summary>
    private void HoldSplashUntilFirstSkinFrame()
    {
        if (_skinReady || Window?.DecorView?.ViewTreeObserver is not { } observer)
            return;
        var decor = Window.DecorView;
        var started = global::Android.OS.SystemClock.ElapsedRealtime();
        var listener = new SplashPreDraw(() => _skinReady || global::Android.OS.SystemClock.ElapsedRealtime() - started > 20000);
        observer.AddOnPreDrawListener(listener);
        void Release(string what)
        {
            if (what != "first skin frame drawn")
                return;
            _skinReady = true;
            StartupMarks.Marked -= Release;
            decor.Post(() => decor.Invalidate());
        }

        StartupMarks.Marked += Release;
        // Poll fallback for the time-out (the listener only runs when something invalidates).
        decor.PostDelayed(() => decor.Invalidate(), 20100);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android31.0")]
    private sealed class SplashExit : Java.Lang.Object, global::Android.Window.ISplashScreenOnExitAnimationListener
    {
        public void OnSplashScreenExit(global::Android.Window.SplashScreenView view) => view.Remove();
    }

    private sealed class SplashPreDraw(Func<bool> ready) : Java.Lang.Object, global::Android.Views.ViewTreeObserver.IOnPreDrawListener
    {
        private bool _done;

        public bool OnPreDraw()
        {
            if (_done)
                return true;
            if (!ready())
                return false;
            _done = true;
            return true;
        }
    }

    /// <summary>
    /// The hardware volume keys. The Avalonia view consumes every key event (the system never saw them: with the app in front the
    /// media volume did not move), so the keys adjust the media stream here, with the system volume panel, playing or not.
    /// </summary>
    public override bool DispatchKeyEvent(global::Android.Views.KeyEvent? e)
    {
        if (e is { KeyCode: global::Android.Views.Keycode.VolumeUp or global::Android.Views.Keycode.VolumeDown or global::Android.Views.Keycode.VolumeMute })
        {
            if (e.Action == global::Android.Views.KeyEventActions.Down && GetSystemService(AudioService) is global::Android.Media.AudioManager audio)
            {
                var direction = e.KeyCode switch
                {
                    global::Android.Views.Keycode.VolumeUp => global::Android.Media.Adjust.Raise,
                    global::Android.Views.Keycode.VolumeDown => global::Android.Media.Adjust.Lower,
                    _ => global::Android.Media.Adjust.ToggleMute,
                };
                audio.AdjustStreamVolume(global::Android.Media.Stream.Music, direction, global::Android.Media.VolumeNotificationFlags.ShowUi);
            }

            return true;
        }

        return base.DispatchKeyEvent(e);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        AndroidPlatformServices.Deliver(OpenedFrom(intent));
    }

    // Visible or not (started / stopped: screen off, home, another app in front): the view animates only while it is visible.
    protected override void OnStart()
    {
        base.OnStart();
        AppVisibility.Set(true);
    }

    protected override void OnStop()
    {
        AppVisibility.Set(false);
        base.OnStop();
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(Current, this))
            Current = null;
        base.OnDestroy();
    }

    /// <summary>Runtime permissions: true when all are granted (asks the user when they are not).</summary>
    internal Task<bool> RequestAsync(params string[] permissions)
    {
        if (permissions.All(p => CheckSelfPermission(p) == Permission.Granted))
            return Task.FromResult(true);
        var source = new TaskCompletionSource<bool>();
        var code = _nextRequest++;
        Requests[code] = source;
        RequestPermissions(permissions, code);
        return source.Task;
    }

    // Codes of their own, far from those Avalonia's pickers use.
    private static readonly Dictionary<int, TaskCompletionSource<(Result Result, Intent? Data)>> Answers = [];
    private static int _nextAnswer = 52_000;

    private Task<(Result Result, Intent? Data)> AnswerTo(Action<int> start)
    {
        var source = new TaskCompletionSource<(Result, Intent?)>();
        var code = _nextAnswer++;
        Answers[code] = source;
        try
        {
            start(code);
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or IntentSender.SendIntentException)
        {
            Answers.Remove(code);
            source.TrySetResult((Result.Canceled, null));
        }

        return source.Task;
    }

    /// <summary>Shows a system question about the user's files (change, move to trash). True when they agreed.</summary>
    internal async Task<bool> AgreeAsync(PendingIntent request) =>
        (await AnswerTo(code => StartIntentSenderForResult(request.IntentSender, code, null, 0, 0, 0))).Result == Result.Ok;

    /// <summary>Asks the user to pick a folder once more, to get write access to it. True when that same folder was picked.</summary>
    internal async Task<bool> PickAgainAsync(AndroidUri tree)
    {
        global::Android.Widget.Toast.MakeText(this, "To change files here, choose this folder again: " + Pardeh.Core.Library.MediaSources.FolderLabel(tree.ToString()!),
            global::Android.Widget.ToastLength.Long)?.Show();
        var pick = new Intent(Intent.ActionOpenDocumentTree);
        pick.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantPersistableUriPermission);
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            pick.PutExtra(global::Android.Provider.DocumentsContract.ExtraInitialUri,
                global::Android.Provider.DocumentsContract.BuildDocumentUriUsingTree(tree, global::Android.Provider.DocumentsContract.GetTreeDocumentId(tree)));
        var (result, data) = await AnswerTo(code => StartActivityForResult(pick, code));
        if (result != Result.Ok || data?.Data is not { } picked
            || global::Android.Provider.DocumentsContract.GetTreeDocumentId(picked) != global::Android.Provider.DocumentsContract.GetTreeDocumentId(tree)
            || picked.Authority != tree.Authority)
            return false;
        ContentResolver?.TakePersistableUriPermission(picked, ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        return true;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (Answers.Remove(requestCode, out var source))
            source.TrySetResult((resultCode, data));
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (Requests.Remove(requestCode, out var source))
            source.TrySetResult(grantResults.All(g => g == Permission.Granted));
    }

    /// <summary>The media a VIEW / SEND intent carries: content URIs, file paths or stream URLs.</summary>
    private static string[] OpenedFrom(Intent? intent)
    {
        var found = new List<AndroidUri>();
        if (intent?.Action is Intent.ActionView && intent.Data is { } data)
            found.Add(data);
        else if (intent?.Action is Intent.ActionSend or Intent.ActionSendMultiple)
        {
#pragma warning disable CA1422
            if (intent.GetParcelableExtra(Intent.ExtraStream) is AndroidUri one)
                found.Add(one);
            if (intent.GetParcelableArrayListExtra(Intent.ExtraStream) is { } many)
                found.AddRange(many.OfType<AndroidUri>());
#pragma warning restore CA1422
        }

        if (found.Count == 0 && intent?.ClipData is { } clip)
        {
            for (var i = 0; i < clip.ItemCount; i++)
                if (clip.GetItemAt(i)?.Uri is { } uri)
                    found.Add(uri);
        }

        // A link shared as text ("Share" in a browser): the address in it.
        if (found.Count == 0 && intent?.Action is Intent.ActionSend && intent.GetStringExtra(Intent.ExtraText) is { } text)
        {
            if (Pardeh.Core.Playback.NetworkMedia.FindAddress(text) is { } address)
                return [address];
            global::Android.Widget.Toast.MakeText(global::Android.App.Application.Context, "There is no link to play in that text.", global::Android.Widget.ToastLength.Long)?.Show();
        }

        return found.Select(uri => uri.Scheme == "file" ? uri.Path : uri.ToString()).OfType<string>().ToArray();
    }
}
