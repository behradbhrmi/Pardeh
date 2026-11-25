using System.Net;
using System.Net.Sockets;
using System.Text;
using Pardeh.Core.Playback;
using Pardeh.Nava;
using Pardeh.Nava.Ffmpeg;

namespace Pardeh.Skinning.Tests;

public sealed class PageVideoTests
{
    private const string Page = "https://site.example/watch/42";

    [Theory]
    // The description for search engines, also inside a list or a graph.
    [InlineData("""<script type="application/ld+json">{"@graph":[{"@type":"WebPage"},{"@type":"VideoObject","contentUrl":"https://cdn.example/v/42.mp4?x=1"}]}</script>""",
        "https://cdn.example/v/42.mp4?x=1")]
    // The link preview, attributes in either order, entities decoded.
    [InlineData("""<meta content="https://cdn.example/a.mp4?a=1&amp;b=2" property="og:video:secure_url">""", "https://cdn.example/a.mp4?a=1&b=2")]
    [InlineData("""<meta property="og:video" content="https://cdn.example/stream/42"><meta property="og:video:type" content="video/mp4">""", "https://cdn.example/stream/42")]
    [InlineData("""<meta name="twitter:player:stream" content="/media/b.m3u8">""", "https://site.example/media/b.m3u8")]
    // Video tags: relative addresses, and the first source that is video.
    [InlineData("""<video controls src='clip.webm'></video>""", "https://site.example/watch/clip.webm")]
    [InlineData("""<video poster="p.jpg"><source src="a.txt" type="text/plain"><source src="//cdn.example/c.mp4" type="video/mp4"></video>""", "https://cdn.example/c.mp4")]
    [InlineData("""<base href="https://files.example/dir/"><video src="d.mp4">""", "https://files.example/dir/d.mp4")]
    public void The_address_a_page_publishes_is_found(string html, string expected) => Assert.Equal(expected, PageVideo.Find(Page, html));

    [Theory]
    [InlineData("<html><body>No video here.</body></html>")]
    // A link preview that names a player page, as the big video sites do.
    [InlineData("""<meta property="og:video:url" content="https://video.example/embed/abc"><meta property="og:video:type" content="text/html">""")]
    [InlineData("""<meta property="og:video" content="https://video.example/player.swf">""")]
    // Not on the page: in a comment, or text inside a script.
    [InlineData("""<!-- <video src="old.mp4"> --><script>var t = '<video src="ad.mp4">';</script>""")]
    // Nothing but web addresses is ever taken from a page.
    [InlineData("""<video src="file:///C:/secret.mp4"></video>""")]
    [InlineData("""<video src="blob:https://site.example/1234"></video>""")]
    [InlineData("""<video src="data:video/mp4;base64,AAAA"></video>""")]
    // A page on the internet does not send the player into the home network or to this device.
    [InlineData("""<video src="http://127.0.0.1:8080/x.mp4"></video>""")]
    [InlineData("""<video src="http://192.168.1.1/x.mp4"></video>""")]
    [InlineData("""<video src="http://[::1]/x.mp4"></video>""")]
    [InlineData("""<video src="http://router/x.mp4"></video>""")]
    // A source tag that belongs to a sound or a picture, not to a video.
    [InlineData("""<audio><source src="a.mp4"></audio><picture><source src="b.mp4"></picture>""")]
    [InlineData("""<script type="application/ld+json">{ broken</script>""")]
    public void Anything_else_gives_nothing(string html) => Assert.Null(PageVideo.Find(Page, html));

    [Fact]
    public void A_page_in_the_home_network_may_name_a_video_there() =>
        Assert.Equal("http://192.168.1.5/v.mp4", PageVideo.Find("http://192.168.1.5/index", """<video src="v.mp4">"""));

    [Fact]
    public void The_pages_own_video_wins_over_a_decoration_and_DASH_comes_last()
    {
        const string html = """
            <video autoplay muted loop src="background.mp4"></video>
            <meta property="og:video" content="https://cdn.example/main.mp4">
            <script type="application/ld+json">{"@type":"VideoObject","contentUrl":"https://cdn.example/main.mpd"}</script>
            """;
        Assert.Equal("https://cdn.example/main.mp4", PageVideo.Find(Page, html));
        Assert.Equal("https://cdn.example/only.mpd", PageVideo.Find(Page, """<video src="https://cdn.example/only.mpd">"""));
    }
}

/// <summary>The real engine against a web server in this process: a page that publishes a sound file, and one that does not.</summary>
public sealed class PageOpenTests
{
    private sealed class Site : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();

        public Site()
        {
            _listener.Start();
            Home = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            var wav = Tone();
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                        _ = Task.Run(() => Serve(client, wav));
                    }
                }
                catch (Exception)
                {
                    // Stopped.
                }
            });
        }

        public string Home { get; }

        private async Task Serve(TcpClient client, byte[] wav)
        {
            using var connection = client;
            try
            {
                var stream = client.GetStream();
                var request = new StringBuilder();
                var one = new byte[1];
                while (!request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal) && await stream.ReadAsync(one, _stop.Token) > 0)
                    request.Append((char)one[0]);
                var path = request.ToString().Split(' ')[1];
                var (type, body) = path switch
                {
                    "/tone.wav" => ("audio/wav", wav),
                    // The video tag names a file of sound only: enough to show the address is taken and opened.
                    "/watch" => ("text/html; charset=utf-8", "<html><body><video src=\"/tone.wav\"></video></body></html>"u8.ToArray()),
                    "/broken" => ("text/html", "<html><video src=\"/gone.mp4\"></video></html>"u8.ToArray()),
                    "/article" => ("text/html", "<html><body>Only words.</body></html>"u8.ToArray()),
                    _ => ("text/plain", null!),
                };
                if (body is null)
                {
                    await stream.WriteAsync("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), _stop.Token);
                    return;
                }

                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"), _stop.Token);
                await stream.WriteAsync(body, _stop.Token);
            }
            catch (Exception)
            {
                // The player hung up.
            }
        }

        /// <summary>Two seconds of a quiet tone, 16 bit mono.</summary>
        private static byte[] Tone()
        {
            const int Rate = 8000, Samples = Rate * 2;
            using var all = new MemoryStream();
            using var w = new BinaryWriter(all);
            w.Write("RIFF"u8);
            w.Write(36 + Samples * 2);
            w.Write("WAVEfmt "u8);
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(Rate);
            w.Write(Rate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write("data"u8);
            w.Write(Samples * 2);
            for (var i = 0; i < Samples; i++)
                w.Write((short)(1000 * Math.Sin(i * 0.3)));
            return all.ToArray();
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public void A_page_that_publishes_its_media_plays_it()
    {
        Assert.SkipWhen(!FfmpegLibrary.IsAvailable, "needs FFmpeg");
        using var site = new Site();
        using var engine = new NavaEngine { Muted = true };
        using var prepared = engine.PrepareOpen(site.Home + "/watch", new NetworkOptions(), Cancel);
        try
        {
            engine.OpenPrepared(prepared);
        }
        catch (System.Runtime.InteropServices.COMException e) when (e.HResult == unchecked((int)0x80070490))
        {
            Assert.Skip("needs an audio output device"); // "element not found": a build server has none
        }

        Assert.Equal(2, engine.Duration.TotalSeconds, 0.1);
    }

    [Theory]
    [InlineData("/article", "does not give the address")]
    [InlineData("/broken", "could not play it")]
    [InlineData("/missing", "404")] // not a page at all: the first failure is reported
    public void Otherwise_the_reason_is_said(string path, string reason)
    {
        Assert.SkipWhen(!FfmpegLibrary.IsAvailable, "needs FFmpeg");
        using var site = new Site();
        using var engine = new NavaEngine { Muted = true };
        var error = Assert.ThrowsAny<Exception>(() => engine.PrepareOpen(site.Home + path, new NetworkOptions(), Cancel));
        Assert.Contains(reason, MediaPlayer.FriendlyReason(error.Message));
    }
}
