using System.Diagnostics;

namespace Pardeh.UI;

/// <summary>Start-up profiling: a line in the log with the milliseconds since the process started (kept: it is cheap).</summary>
public static class StartupMarks
{
    private static readonly DateTime ProcessStart = Process.GetCurrentProcess().StartTime;

    /// <summary>Raised (on the calling thread) after each mark; the Android head waits for "first skin frame drawn" to drop its splash.</summary>
    public static event Action<string>? Marked;

    public static void Mark(string what)
    {
        Marked?.Invoke(what);
        Console.WriteLine($"[startup +{(DateTime.Now - ProcessStart).TotalMilliseconds:0} ms] {what}"); // logcat (mono-stdout) on Android
        try
        {
            File.AppendAllText(AppPaths.LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [startup +{(DateTime.Now - ProcessStart).TotalMilliseconds:0} ms] {what}{Environment.NewLine}");
        }
        catch (Exception)
        {
        }
    }
}
