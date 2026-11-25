using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Pardeh.Nava;

/// <summary>
/// For an FFmpeg built without TLS (the Android libraries): https is fetched by .NET, with the system's certificate checks, and
/// handed to FFmpeg over a connection that never leaves the device. FFmpeg asks for
/// <c>http://127.0.0.1:port/secret/https/host/path</c>; the secret keeps other apps on the device from using the relay.
/// Playlists are rewritten on the way through, so that the pieces they name come the same way.
/// </summary>
internal sealed partial class HttpsRelay : IDisposable
{
    private const int MaxPlaylistBytes = 8 * 1024 * 1024;
    private const int MaxRequestBytes = 32 * 1024;
    private static readonly TimeSpan AnswerWithin = TimeSpan.FromSeconds(20);
    private static HttpsRelay? _shared;
    private static readonly Lock SharedGate = new();

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly string _secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    private readonly HttpClient _http = new(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.None })
    {
        Timeout = Timeout.InfiniteTimeSpan, // a film or a radio station takes as long as it takes
    };

    private readonly string _home;

    public HttpsRelay()
    {
        _listener.Start();
        _home = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture)}/{_secret}/";
        _ = Task.Run(Accept);
    }

    /// <summary>The one relay of the app, started when it is first needed.</summary>
    public static HttpsRelay Shared
    {
        get
        {
            lock (SharedGate)
                return _shared ??= new HttpsRelay();
        }
    }

    /// <summary>The address FFmpeg is to ask for in place of a web address.</summary>
    public string Local(string address)
    {
        var at = address.IndexOf("://", StringComparison.Ordinal);
        var rest = address[(at + 3)..];
        var fragment = rest.IndexOf('#');
        if (fragment >= 0)
            rest = rest[..fragment];
        // "host?query" has no path of its own.
        var slash = rest.IndexOf('/');
        var query = rest.IndexOf('?');
        if (query >= 0 && (slash < 0 || query < slash))
            rest = rest.Insert(query, "/");
        else if (slash < 0)
            rest += "/";
        return _home + address[..at].ToLowerInvariant() + "/" + rest;
    }

    /// <summary>
    /// A playlist whose entries lead through the relay again: every https entry, and every entry written relative to the
    /// playlist. Plain http entries are left to FFmpeg.
    /// </summary>
    public string Rewrite(string playlist, Uri from)
    {
        var lines = playlist.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0)
                continue;
            lines[i] = line[0] == '#'
                ? UriAttribute().Replace(line, m => "URI=\"" + Mapped(m.Groups[1].Value, from) + "\"")
                : Mapped(line.Trim(), from);
        }

        return string.Join('\n', lines);
    }

    [GeneratedRegex("URI=\"([^\"]*)\"")]
    private static partial Regex UriAttribute();

    private string Mapped(string entry, Uri from)
    {
        if (!Uri.TryCreate(from, entry, out var uri))
            return entry;
        return uri.Scheme == "https" || uri.Scheme == "http" && !entry.Contains("://", StringComparison.Ordinal)
            ? Local(uri.AbsoluteUri)
            : entry; // data:, skd:, or an http address given in full
    }

    private async Task Accept()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = Task.Run(() => Serve(client));
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            // Stopped.
        }
    }

    private async Task Serve(TcpClient client)
    {
        using var connection = client;
        try
        {
            var stream = client.GetStream();
            if (await ReadRequest(stream) is not { } head)
                return;
            var lines = head.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            var first = lines[0].Split(' ');
            if (first.Length < 3 || first[0] is not ("GET" or "HEAD") || Target(first[1]) is not { } target)
            {
                await Reply(stream, "403 Forbidden");
                return;
            }

            await Forward(stream, first[0] == "HEAD", target, lines);
        }
        catch (Exception ex) when (ex is IOException or SocketException or HttpRequestException or OperationCanceledException or ObjectDisposedException
            or UriFormatException or InvalidOperationException)
        {
            // FFmpeg hung up (a seek, a stop), or the server did: closing the connection says so.
        }
    }

    private async Task<string?> ReadRequest(NetworkStream stream)
    {
        var buffer = new byte[MaxRequestBytes];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), _stop.Token);
            if (read == 0)
                return null;
            length += read;
            if (buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8) >= 0)
                return Encoding.UTF8.GetString(buffer, 0, length);
        }

        return null;
    }

    /// <summary>"/secret/https/host/path" back to "https://host/path"; null for anything without the secret.</summary>
    private Uri? Target(string path)
    {
        var parts = path.Split('/', 4);
        if (parts.Length < 4 || parts[0].Length > 0 || parts[2] is not ("https" or "http")
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(parts[1]), Encoding.UTF8.GetBytes(_secret)))
        {
            return null;
        }

        return Uri.TryCreate(parts[2] + "://" + parts[3], UriKind.Absolute, out var uri) ? uri : null;
    }

    private async Task Forward(NetworkStream stream, bool headOnly, Uri target, string[] lines)
    {
        string? Header(string name) => lines.Skip(1)
            .FirstOrDefault(l => l.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))?[(name.Length + 1)..].Trim();

        using var request = new HttpRequestMessage(headOnly ? HttpMethod.Head : HttpMethod.Get, target);
        var namesPlaylist = target.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase);
        // A playlist is rewritten, so it is always asked for whole.
        if (Header("Range") is { } range && !namesPlaylist)
            request.Headers.TryAddWithoutValidation("Range", range);
        if (Header("User-Agent") is { } agent)
            request.Headers.TryAddWithoutValidation("User-Agent", agent);
        if (target.UserInfo.Length > 0)
        {
            var login = Convert.ToBase64String(Encoding.UTF8.GetBytes(Uri.UnescapeDataString(target.UserInfo)));
            request.Headers.TryAddWithoutValidation("Authorization", "Basic " + login);
        }

        HttpResponseMessage response;
        using (var limit = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            limit.CancelAfter(AnswerWithin);
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, limit.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                await Reply(stream, "502 Bad Gateway"); // no such server, a bad certificate, or no answer
                return;
            }
        }

        using (response)
        {
            var type = response.Content.Headers.ContentType?.ToString();
            var status = $"{(int)response.StatusCode} {response.ReasonPhrase}";
            var from = response.RequestMessage?.RequestUri ?? target; // after redirects
            var playlist = response.StatusCode == HttpStatusCode.OK && !headOnly
                && (namesPlaylist || from.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
                    || type?.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) == true);
            if (playlist)
            {
                var body = await ReadPlaylist(response);
                var bytes = Encoding.UTF8.GetBytes(Rewrite(body, from));
                await Reply(stream, status, $"Content-Type: {type ?? "application/vnd.apple.mpegurl"}\r\n", bytes.Length);
                await stream.WriteAsync(bytes, _stop.Token);
                return;
            }

            var headers = new StringBuilder();
            if (type is not null)
                headers.Append("Content-Type: ").Append(type).Append("\r\n");
            if (response.Content.Headers.ContentRange is { } given)
                headers.Append("Content-Range: ").Append(given).Append("\r\n");
            if (response.Headers.AcceptRanges.Count > 0)
                headers.Append("Accept-Ranges: ").Append(string.Join(", ", response.Headers.AcceptRanges)).Append("\r\n");
            await Reply(stream, status, headers.ToString(), response.Content.Headers.ContentLength);
            if (headOnly)
                return;
            await using var source = await response.Content.ReadAsStreamAsync(_stop.Token);
            await source.CopyToAsync(stream, 64 * 1024, _stop.Token);
        }
    }

    private async Task<string> ReadPlaylist(HttpResponseMessage response)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        limit.CancelAfter(AnswerWithin);
        await using var source = await response.Content.ReadAsStreamAsync(limit.Token);
        using var all = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await source.ReadAsync(chunk, limit.Token)) > 0)
        {
            all.Write(chunk, 0, read);
            if (all.Length > MaxPlaylistBytes)
                throw new IOException("The playlist is too long.");
        }

        return Encoding.UTF8.GetString(all.ToArray());
    }

    private async Task Reply(NetworkStream stream, string status, string headers = "", long? length = 0)
    {
        var size = length is { } known ? "Content-Length: " + known.ToString(CultureInfo.InvariantCulture) + "\r\n" : string.Empty;
        await stream.WriteAsync(Encoding.UTF8.GetBytes($"HTTP/1.1 {status}\r\n{headers}{size}Connection: close\r\n\r\n"), _stop.Token);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _http.Dispose();
    }
}
