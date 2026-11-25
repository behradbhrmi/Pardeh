using Pardeh.UI;
using System.IO.Pipes;
using System.Text;

namespace Pardeh.App.Platform;

/// <summary>
/// One Pardeh at a time: opening files while it runs (Explorer, "Open with", the command line) hands them to the
/// running window over a named pipe instead of starting a second player.
/// </summary>
internal static class SingleInstance
{
    private static readonly string Name = "Pardeh." + Environment.UserName;
    private static Mutex? _mutex;

    /// <summary>
    /// True when this process should run the player. False when another Pardeh took the arguments (exit then).
    /// Development runs that render snapshots always run on their own.
    /// </summary>
    public static bool Claim(string[] args)
    {
        if (Environment.GetEnvironmentVariable("PARDEH_SNAPSHOT") is { Length: > 0 }
            || Environment.GetEnvironmentVariable("PARDEH_MULTI_INSTANCE") == "1"
            || !AppSettings.Current.SingleInstance)
            return true;

        _mutex = new Mutex(initiallyOwned: true, "Local\\" + Name, out var first);
        if (first)
            return true;

        try
        {
            using var pipe = new NamedPipeClientStream(".", Name, PipeDirection.Out);
            pipe.Connect(2000);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false));
            foreach (var arg in args)
                writer.WriteLine(Path.IsPathFullyQualified(arg) || arg.Contains("://") ? arg : Path.GetFullPath(arg));
            return false;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            return true; // the other one isn't answering: run anyway
        }
    }

    /// <summary>Receives the arguments of later launches (called on a background thread).</summary>
    public static void Listen(Action<string[]> received)
    {
        if (_mutex is null)
            return;
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(Name, PipeDirection.In, 1);
                    pipe.WaitForConnection();
                    using var reader = new StreamReader(pipe, Encoding.UTF8);
                    var lines = new List<string>();
                    while (reader.ReadLine() is { } line)
                    {
                        if (line.Length > 0)
                            lines.Add(line);
                    }

                    received(lines.ToArray());
                }
                catch (IOException)
                {
                    Thread.Sleep(200);
                }
            }
        }) { IsBackground = true, Name = "Pardeh single instance" };
        thread.Start();
    }
}
