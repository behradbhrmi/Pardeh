using System.Net;
using System.Net.Sockets;
using System.Text;
using Pardeh.Nava;

namespace Pardeh.Skinning.Tests;

/// <summary>The relay against a web server in this process (plain http: what is tested is the handing on, not TLS).</summary>
public sealed class HttpsRelayTests
{
    private sealed class Server : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();

        public Server()
        {
            _listener.Start();
            Home = $"127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                        var stream = client.GetStream();
                        var request = new StringBuilder();
                        var one = new byte[1];
                        while (!request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal) && await stream.ReadAsync(one, _stop.Token) > 0)
                            request.Append((char)one[0]);
                        Requests.Add(request.ToString());
                        var list = request.ToString().Contains(".m3u8", StringComparison.Ordinal);
                        var body = list ? "#EXTM3U\n#EXTINF:4,\nseg1.ts\n#EXTINF:4,\n/root/seg2.ts\n" : "0123456789";
                        var ranged = request.ToString().Contains("Range: bytes=4-", StringComparison.Ordinal);
                        var head = ranged
                            ? "HTTP/1.1 206 Partial Content\r\nContent-Range: bytes 4-9/10\r\nContent-Length: 6\r\n"
                            : $"HTTP/1.1 200 OK\r\nAccept-Ranges: bytes\r\nContent-Length: {body.Length}\r\n";
                        var type = list ? "application/vnd.apple.mpegurl" : "video/mp4";
                        await stream.WriteAsync(Encoding.ASCII.GetBytes($"{head}Content-Type: {type}\r\nConnection: close\r\n\r\n{(ranged ? body[4..] : body)}"), _stop.Token);
                    }
                }
                catch (Exception)
                {
                    // Stopped.
                }
            });
        }

        public string Home { get; }

        public List<string> Requests { get; } = [];

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_part_of_a_file_comes_through_with_its_headers()
    {
        using var server = new Server();
        using var relay = new HttpsRelay();
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, relay.Local($"http://me:pw@{server.Home}/a.mp4?id=7"));
        request.Headers.TryAddWithoutValidation("Range", "bytes=4-");
        request.Headers.TryAddWithoutValidation("User-Agent", "Test/1");
        using var response = await http.SendAsync(request, Cancel);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("bytes 4-9/10", response.Content.Headers.ContentRange!.ToString());
        Assert.Equal("456789", await response.Content.ReadAsStringAsync(Cancel));
        var seen = Assert.Single(server.Requests);
        Assert.StartsWith("GET /a.mp4?id=7 ", seen);
        Assert.Contains("User-Agent: Test/1", seen);
        Assert.Contains("Authorization: Basic " + Convert.ToBase64String("me:pw"u8.ToArray()), seen);
    }

    [Fact]
    public async Task The_pieces_a_playlist_names_come_through_the_relay_too()
    {
        using var server = new Server();
        using var relay = new HttpsRelay();
        using var http = new HttpClient();
        var list = await http.GetStringAsync(relay.Local($"http://{server.Home}/v/list.m3u8"), Cancel);
        var pieces = list.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => l[0] != '#').ToList();
        Assert.Equal([relay.Local($"http://{server.Home}/v/seg1.ts"), relay.Local($"http://{server.Home}/root/seg2.ts")], pieces);
        Assert.Equal("0123456789", await http.GetStringAsync(pieces[1], Cancel));
    }

    [Fact]
    public void Https_entries_and_keys_are_rewritten_and_plain_http_ones_left()
    {
        using var relay = new HttpsRelay();
        var text = relay.Rewrite("#EXTM3U\r\n#EXT-X-KEY:METHOD=AES-128,URI=\"https://keys.example/k\",IV=0x1\r\n"
            + "https://cdn.example/a.ts\r\nhttp://plain.example/b.ts\r\n../c.ts\r\n", new Uri("https://host.example/x/y/list.m3u8"));
        Assert.Contains($"URI=\"{relay.Local("https://keys.example/k")}\",IV=0x1", text);
        Assert.Contains(relay.Local("https://cdn.example/a.ts") + "\n", text);
        Assert.Contains("\nhttp://plain.example/b.ts\n", text);
        Assert.Contains(relay.Local("https://host.example/x/c.ts"), text);
        Assert.EndsWith("/https/host.example/?a=1", relay.Local("https://host.example?a=1"));
    }

    [Fact]
    public async Task Without_the_secret_nothing_is_fetched()
    {
        using var server = new Server();
        using var relay = new HttpsRelay();
        using var http = new HttpClient();
        var guess = new Uri(relay.Local($"http://{server.Home}/a.mp4"));
        var wrong = $"http://127.0.0.1:{guess.Port}/{new string('0', 32)}/http/{server.Home}/a.mp4";
        using var response = await http.GetAsync(wrong, Cancel);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task A_server_that_cannot_be_reached_is_a_bad_gateway()
    {
        using var relay = new HttpsRelay();
        using var http = new HttpClient();
        using var response = await http.GetAsync(relay.Local("https://nowhere.invalid/a.mp4"), Cancel);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }
}
