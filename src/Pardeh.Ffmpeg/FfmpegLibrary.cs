using FFmpeg.AutoGen;

namespace Pardeh.Ffmpeg;

/// <summary>
/// Finds and loads the FFmpeg shared libraries. Without them Pardeh falls back to Media Foundation (Windows);
/// on Android they come from the APK's native library folder and there is no fallback yet.
/// </summary>
public static class FfmpegLibrary
{
    private static readonly Lock Gate = new();
    private static bool? _available;

    /// <summary>"ffmpeg 8.1…" when loaded, else null.</summary>
    public static string? Version { get; private set; }

    public static string? LoadError { get; private set; }

    public static bool IsAvailable
    {
        get
        {
            lock (Gate)
            {
                _available ??= TryLoad();
                return _available.Value;
            }
        }
    }

    private static bool TryLoad()
    {
        if (Environment.GetEnvironmentVariable("PARDEH_NO_FFMPEG") == "1")
        {
            LoadError = "Disabled by PARDEH_NO_FFMPEG.";
            return false;
        }

        foreach (var directory in CandidateDirectories())
        {
            if (!Directory.Exists(directory) || !Directory.EnumerateFiles(directory, "*avcodec*").Any())
                continue;
            try
            {
                ffmpeg.RootPath = directory;
#if ANDROID
                DynamicallyLoadedBindings.FunctionResolver = new AndroidFfmpegResolver(directory);
                DynamicallyLoadedBindings.Initialize();
#endif
                Version = ffmpeg.av_version_info();
                ffmpeg.av_log_set_level(ffmpeg.AV_LOG_ERROR);
                return true;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException or NotSupportedException)
            {
                LoadError = $"{directory}: {ex.Message}";
            }
        }

        LoadError ??= "FFmpeg libraries not found.";
        return false;
    }

    private static IEnumerable<string> CandidateDirectories()
    {
        if (Environment.GetEnvironmentVariable("PARDEH_FFMPEG_DIR") is { Length: > 0 } custom)
            yield return custom;

#if ANDROID
        // The APK's native library folder (src/Pardeh.Android/lib/<abi>/libav*.so ends up here).
        if (Android.App.Application.Context.ApplicationInfo?.NativeLibraryDir is { Length: > 0 } native)
            yield return native;
        yield break;
#else

        yield return Path.Combine(AppContext.BaseDirectory, "ffmpeg");
        yield return AppContext.BaseDirectory;

        // Development: the repo's tools/ffmpeg/win-x64 (filled by scripts/get-ffmpeg.ps1).
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var tools = Path.Combine(dir.FullName, "tools", "ffmpeg", "win-x64");
            if (Directory.Exists(tools))
            {
                yield return tools;
                yield break;
            }
        }
#endif
    }

    internal const int EAGAIN = -11;

    internal static unsafe string ErrorText(int error)
    {
        const int size = 256;
        var buffer = stackalloc byte[size];
        ffmpeg.av_strerror(error, buffer, size);
        return System.Runtime.InteropServices.Marshal.PtrToStringUTF8((IntPtr)buffer) ?? $"error {error}";
    }

    internal static void Check(int result, string operation)
    {
        if (result < 0)
            throw new InvalidDataException($"{operation} failed: {ErrorText(result)}");
    }
}
