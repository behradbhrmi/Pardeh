using Avalonia.Controls.ApplicationLifetimes;
using Pardeh.UI;

namespace Pardeh.Android;

/// <summary>The Android app: the shared theme plus a single-view lifetime that hosts the player view.</summary>
public sealed class AndroidApplication : PardehApplicationBase
{
    public override void Initialize()
    {
        // Before anything touches settings or the library: the app's private files dir instead of %APPDATA%.
        if (global::Android.App.Application.Context.FilesDir?.AbsolutePath is { Length: > 0 } files)
            AppPaths.DataDirectory = files;
        StartupMarks.Mark("AndroidApplication.Initialize");
        Pardeh.Skinning.Runtime.SkinView.Trace ??= StartupMarks.Mark;

        // The first script engine is JIT-bound (seconds): start building a throw-away one now so that cost overlaps the
        // rest of the start-up instead of all following it.
        _ = Pardeh.Skinning.Runtime.Scripting.ScriptHost.Prewarm();

        ApplyDevEnvironment();
        AndroidMediaSources.Install();
        PlatformServices.Current = new AndroidPlatformServices();
        base.Initialize();
    }

    /// <summary>
    /// Development aid: KEY=VALUE lines in the app's files dir (dev.env) become environment variables, like on
    /// Windows (PARDEH_FULLMODE=luna-phone, PARDEH_START_TASK=MediaLibrary ...). Written with adb run-as.
    /// </summary>
    private static void ApplyDevEnvironment()
    {
        try
        {
            var file = Path.Combine(AppPaths.DataDirectory, "dev.env");
            if (!File.Exists(file))
                return;
            foreach (var line in File.ReadAllLines(file))
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2 && parts[0].Trim().Length > 0)
                    Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
            }
        }
        catch (IOException)
        {
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime single)
            single.MainView = AndroidHost.CreateMainView();
        StartupMarks.Mark("main view created");

        base.OnFrameworkInitializationCompleted();
    }
}
