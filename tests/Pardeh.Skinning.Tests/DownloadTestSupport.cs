using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Pardeh.Core.Downloads;
using Pardeh.Nava;

namespace Pardeh.Skinning.Tests;

/// <summary>One request as the test server got it.</summary>
internal sealed record ServerRequest(string Path, IReadOnlyDictionary<string, string> Headers)
{
    public string? Header(string name) => Headers.GetValueOrDefault(name);

    /// <summary>The first byte of a "bytes=N-" range, or null.</summary>
    public long? From => Header("Range") is { } range && range.StartsWith("bytes=", StringComparison.Ordinal)
        ? long.Parse(range["bytes=".Length..].TrimEnd('-'), CultureInfo.InvariantCulture)
        : null;
}

/// <summary>What the test server answers.</summary>
internal sealed class ServerReply
{
    public int Status { get; init; } = 200;
    public byte[] Body { get; init; } = [];
    public string? Type { get; init; } = "audio/mpeg";
    public string? ETag { get; init; }
    public List<(string, string)> Headers { get; } = [];
    public bool NoLength { get; init; }

    /// <summary>The connection is closed after this many bytes of the body.</summary>
    public int? CutAfter { get; init; }

    /// <summary>Nothing more is sent after this many bytes until the test lets go.</summary>
    public int? HoldAfter { get; init; }

    public static ServerReply Redirect(string to) => new() { Status = 302, Type = null, Headers = { ("Location", to) } };

    public static ServerReply Text(string type, string text) => new() { Type = type, Body = Encoding.UTF8.GetBytes(text) };

    /// <summary>The file, or the part of it a range asks for when If-Range still matches.</summary>
    public static ServerReply File(ServerRequest request, byte[] file, string? etag = "\"v1\"", string type = "audio/mpeg", int? holdAfter = null, int? cutAfter = null)
    {
        if (request.From is { } from && etag is not null && request.Header("If-Range") == etag)
        {
            return new ServerReply
            {
                Status = 206, Body = file[(int)from..], Type = type, ETag = etag, HoldAfter = holdAfter, CutAfter = cutAfter,
                Headers = { ("Content-Range", $"bytes {from}-{file.Length - 1}/{file.Length}") },
            };
        }

        return new ServerReply { Body = file, Type = type, ETag = etag, HoldAfter = holdAfter, CutAfter = cutAfter };
    }
}

/// <summary>A small web server on this machine, for downloads. It speaks plain http.</summary>
internal sealed class TinyServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TinyServer()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(Accept);
    }

    public int Port { get; }

    public List<ServerRequest> Requests { get; } = [];

    public Func<ServerRequest, ServerReply> Answer { get; set; } = _ => new ServerReply { Status = 404 };

    public Uri Address(string path = "/song.mp3", string host = "127.0.0.1") => new($"http://{host}:{Port}{path}");

    /// <summary>Lets held answers go on.</summary>
    public void Release()
    {
        _release.TrySetResult();
        _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private async Task Accept()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => Serve(client));
        }
    }

    private async Task Serve(TcpClient client)
    {
        using var owned = client;
        try
        {
            var stream = client.GetStream();
            var head = new StringBuilder();
            var one = new byte[1];
            while (!head.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                if (await stream.ReadAsync(one, _stop.Token) == 0)
                    return;
                head.Append((char)one[0]);
            }

            var lines = head.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            var headers = lines.Skip(1).Select(l => l.Split(':', 2)).ToDictionary(p => p[0], p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);
            var request = new ServerRequest(lines[0].Split(' ')[1], headers);
            lock (Requests)
                Requests.Add(request);
            var held = _release.Task;
            var reply = Answer(request);

            var answer = new StringBuilder($"HTTP/1.1 {reply.Status} X\r\nConnection: close\r\n");
            if (reply.Type is not null)
                answer.Append("Content-Type: ").Append(reply.Type).Append("\r\n");
            if (reply.ETag is not null)
                answer.Append("ETag: ").Append(reply.ETag).Append("\r\n");
            if (!reply.NoLength)
                answer.Append("Content-Length: ").Append(reply.Body.Length).Append("\r\n");
            foreach (var (name, value) in reply.Headers)
                answer.Append(name).Append(": ").Append(value).Append("\r\n");
            answer.Append("\r\n");
            await stream.WriteAsync(Encoding.Latin1.GetBytes(answer.ToString()), _stop.Token);

            var first = Math.Min(reply.Body.Length, reply.CutAfter ?? reply.HoldAfter ?? reply.Body.Length);
            await stream.WriteAsync(reply.Body.AsMemory(0, first), _stop.Token);
            await stream.FlushAsync(_stop.Token);
            if (reply.CutAfter is not null)
                return;
            if (reply.HoldAfter is not null)
            {
                await held.WaitAsync(_stop.Token);
                await stream.WriteAsync(reply.Body.AsMemory(first), _stop.Token);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // The client went away.
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }
}

/// <summary>A download engine whose rules let it reach the test servers, and folders of its own.</summary>
internal sealed class DownloadFixture : IDisposable
{
    public DownloadFixture(params TinyServer[] servers)
    {
        Root = Directory.CreateTempSubdirectory("pardeh-download-").FullName;
        Rules = new DownloadRules
        {
            UseProxy = false,
            // Only these servers are let past; every other address is held to the real rules.
            TestAllows = address => Servers.Any(s => address.Port == s.Port && address.Host is "127.0.0.1" or "localhost" && address.UserInfo.Length == 0),
            Resolve = (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") }),
        };
        Servers.AddRange(servers);
        Engine = new DownloadEngine(Rules.CreateClient(), Rules, new AudioCheck(() => true, (path, cancel) => AudioDigest.Scan(path, 0, cancel)), new TagEditor(), Contact);
        Target = new FolderDownloadTarget(Path.Combine(Root, "Downloads"));
        Manager = NewManager();
    }

    public const string Contact = "Pardeh/9.9 (https://example.org/project)";

    public string Root { get; }
    public List<TinyServer> Servers { get; } = [];
    public DownloadRules Rules { get; }
    public DownloadEngine Engine { get; }
    public FolderDownloadTarget Target { get; }
    public DownloadManager Manager { get; private set; }
    public string Work => Path.Combine(Root, "work");

    /// <summary>As after a restart of the app: everything is read again from disk.</summary>
    public DownloadManager NewManager() => Manager = new DownloadManager(Engine, new DownloadStore(Work, Path.Combine(Root, "downloads.json")), Target);

    public string[] Added => Directory.Exists(Target.Folder) ? Directory.GetFiles(Target.Folder) : [];

    public string[] Left => Directory.Exists(Work) ? Directory.GetFiles(Work) : [];

    /// <summary>A real audio file, made once for each kind.</summary>
    public byte[] Audio(string extension = ".mp3", string codec = "libmp3lame", int seconds = 20)
    {
        var path = Path.Combine(Root, $"source-{codec}-{seconds}{extension}");
        if (!File.Exists(path))
        {
            string[] rate = codec == "libmp3lame" ? ["-b:a", "128k"] : [];
            FfmpegTool.Run(["-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}", "-c:a", codec, .. rate, "-metadata", "title=Test tone", path]);
        }

        return File.ReadAllBytes(path);
    }

    public Task<DownloadOffer> Look(Uri address, DownloadKind kind = DownloadKind.PastedLink) => Engine.LookAsync(address, kind, TestContext.Current.CancellationToken);

    /// <summary>Starts the job and waits for it to end.</summary>
    public async Task<DownloadStatus> Run(DownloadJob job)
    {
        Assert.True(Manager.Start(job));
        await Manager.Completion.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        return Manager.Status!;
    }

    /// <summary>Waits until at least this much has come.</summary>
    public async Task Until(long bytes)
    {
        for (var i = 0; i < 500; i++)
        {
            if (Manager.Status is { } status && File.Exists(Manager.Store.PartialPath(status.Job.Id)) && new FileInfo(Manager.Store.PartialPath(status.Job.Id)).Length >= bytes)
                return;
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Not {bytes} bytes yet: {Manager.Status}");
    }

    public void Dispose()
    {
        Manager.Pause();
        foreach (var server in Servers)
            server.Dispose();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
