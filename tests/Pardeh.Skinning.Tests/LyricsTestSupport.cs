using System.Diagnostics;
using System.IO.Compression;
using Pardeh.Core.LyricsKit;
using Pardeh.Core.Playback;

namespace Pardeh.Skinning.Tests;

/// <summary>A lyrics source that answers from a function and counts its calls.</summary>
internal sealed class FakeLyricsSource(string name, Func<MediaItem, Lyrics?>? read = null) : ILyricsSource
{
    private int _calls;

    public string Name => name;

    public int Calls => _calls;

    public Lyrics? Read(MediaItem item, CancellationToken cancel)
    {
        Interlocked.Increment(ref _calls);
        return read?.Invoke(item);
    }
}

/// <summary>An online provider that answers from a function, records the queries and can fail or wait.</summary>
internal sealed class FakeLyricsProvider(Func<LyricsQuery, Lyrics?>? find = null) : ILyricsProvider
{
    public List<LyricsQuery> Queries { get; } = [];

    public Task? Gate { get; set; }

    public string Name => "Fake";

    public async Task<Lyrics?> FindAsync(LyricsQuery query, CancellationToken cancel)
    {
        lock (Queries)
            Queries.Add(query);
        if (Gate is { } gate)
            await gate.WaitAsync(cancel);
        return find?.Invoke(query);
    }
}

internal static class LyricsTestData
{
    public static MediaItem Item(string path, string title = "Song", string artist = "Artist", string album = "Album", int seconds = 200)
    {
        var item = new MediaItem(path) { Name = title, Duration = TimeSpan.FromSeconds(seconds) };
        if (title.Length > 0)
            item.Attributes["Title"] = title;
        if (artist.Length > 0)
            item.Attributes["Author"] = artist;
        if (album.Length > 0)
            item.Attributes["Album"] = album;
        return item;
    }

    public static Lyrics Synced(params string[] texts) =>
        new(texts.Select((t, i) => new LyricLine(TimeSpan.FromSeconds(i * 10 + 5), t)), synced: true);

    public static Lyrics Plain(params string[] texts) => new(texts.Select(t => new LyricLine(TimeSpan.Zero, t)), synced: false);

    public static string NewTempFolder(string prefix) => Directory.CreateTempSubdirectory(prefix).FullName;
}

/// <summary>Locates an ffmpeg.exe for making real test files (never committed): tools/ffmpeg, or the one inside the downloaded zip.</summary>
internal static class FfmpegTool
{
    private static readonly Lazy<(string Exe, string DllFolder)?> Found = new(() => Locate());

    public static (string Exe, string DllFolder)? Tool => Found.Value;

    // Looks upwards from the test binaries and from this source file (a build to a scratch folder outside the repo still finds tools/).
    private static (string, string)? Locate([System.Runtime.CompilerServices.CallerFilePath] string? here = null)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        var starts = new List<string> { AppContext.BaseDirectory };
        if (here is not null && Path.GetDirectoryName(here) is { } sourceFolder)
            starts.Add(sourceFolder);
        foreach (var start in starts)
            if (LocateFrom(start) is { } found)
                return found;
        return null;
    }

    private static (string, string)? LocateFrom(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var tools = Path.Combine(dir.FullName, "tools", "ffmpeg");
            var dlls = Path.Combine(tools, "win-x64");
            if (!Directory.Exists(dlls))
                continue;
            var exe = Path.Combine(dlls, "ffmpeg.exe");
            if (File.Exists(exe))
                return (exe, dlls);
            if (Directory.GetFiles(tools, "ffmpeg-*-win64-lgpl-shared.zip").FirstOrDefault() is { } zip)
            {
                var cache = Path.Combine(Path.GetTempPath(), "pardeh-test-ffmpeg-exe");
                var target = Path.Combine(cache, "ffmpeg.exe");
                try
                {
                    if (!File.Exists(target))
                    {
                        Directory.CreateDirectory(cache);
                        using var archive = ZipFile.OpenRead(zip);
                        var entry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("/bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase));
                        if (entry is null)
                            return null;
                        entry.ExtractToFile(target + ".part", overwrite: true);
                        File.Move(target + ".part", target, overwrite: true);
                    }

                    return (target, dlls);
                }
                catch (IOException)
                {
                    return File.Exists(target) ? (target, dlls) : null;
                }
            }
        }

        return null;
    }

    /// <summary>Makes a small real JPEG of one colour (picked by <paramref name="seed"/>) and returns its bytes.</summary>
    public static byte[] MakeJpeg(string path, int seed)
    {
        var colour = new[] { "red", "green", "blue", "orange", "purple", "teal", "navy", "olive" }[seed % 8];
        Run("-f", "lavfi", "-i", $"color=c={colour}:s={160 + seed}x160:d=1", "-frames:v", "1", path);
        return File.ReadAllBytes(path);
    }

    public static void Run(params string[] arguments)
    {
        var (exe, dlls) = Tool ?? throw new InvalidOperationException("ffmpeg not available");
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        start.Environment["PATH"] = dlls + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(arguments))
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg failed ({process.ExitCode}): {error}");
    }

    /// <summary>Makes a one second tone in <paramref name="path"/> (the extension picks the container) with the given tags.</summary>
    public static void MakeAudio(string path, string codec, IReadOnlyDictionary<string, string>? metadata = null)
    {
        var (exe, dlls) = Tool ?? throw new InvalidOperationException("ffmpeg not available");
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
        start.Environment["PATH"] = dlls + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:duration=1", "-c:a", codec })
            start.ArgumentList.Add(arg);
        foreach (var (key, value) in metadata ?? new Dictionary<string, string>())
        {
            start.ArgumentList.Add("-metadata");
            start.ArgumentList.Add(key + "=" + value);
        }

        start.ArgumentList.Add(path);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 || !File.Exists(path))
            throw new InvalidOperationException($"ffmpeg failed ({process.ExitCode}): {error}");
    }
}
