using Pardeh.Engine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Pardeh.Core.Playback;
using Pardeh.Nava;
using Pardeh.Ffmpeg;

namespace Pardeh.Skinning.Tests;

public sealed class NetworkAddressTests
{
    [Theory]
    [InlineData("http://a/b.mp3", true)]
    [InlineData("HTTPS://a/b", true)]
    [InlineData("rtsp://cam/1", true)]
    [InlineData("C:\\music\\a.mp3", false)]
    [InlineData("content://media/1", false)]
    [InlineData("file:///c/a.mp3", false)]
    public void Network_addresses_are_told_from_files(string source, bool network) => Assert.Equal(network, NetworkMedia.IsNetwork(source));

    [Fact]
    public void Secrets_are_found_hidden_and_never_remembered()
    {
        Assert.True(NetworkMedia.HasSecret("http://me:hunter2@host/a.mp3"));
        Assert.True(NetworkMedia.HasSecret("https://host/a.mp3?token=abc"));
        Assert.True(NetworkMedia.HasSecret("https://bucket.example/a.mp3?X-Amz-Signature=abc&X-Amz-Date=1"));
        Assert.False(NetworkMedia.HasSecret("https://host/a.mp3?id=4"));
        Assert.Equal("https://host/a.mp3?...", NetworkMedia.Redact("https://me:hunter2@host/a.mp3?token=abc"));
        Assert.DoesNotContain("hunter2", NetworkMedia.Redact("Could not open http://me:hunter2@host/x: refused", "http://me:hunter2@host/x"));

        var recent = new List<string>();
        NetworkMedia.Remember(recent, "http://a/1");
        NetworkMedia.Remember(recent, "http://a/2");
        NetworkMedia.Remember(recent, "http://a/1");
        NetworkMedia.Remember(recent, "http://me:pw@a/3");
        NetworkMedia.Remember(recent, "C:\\a.mp3");
        Assert.Equal(["http://a/1", "http://a/2"], recent);
        for (var i = 0; i < 40; i++)
            NetworkMedia.Remember(recent, $"http://a/{i}");
        Assert.Equal(15, recent.Count);
    }
}

/// <summary>The player's side of network items, with a scripted engine: connect off the thread, cancel, reconnect.</summary>
public sealed class NetworkPlayerTests
{
    private sealed class NetEngine : IPlaybackEngine
    {
        public sealed class Media(string source) : IDisposable
        {
            public string Source { get; } = source;
            public bool Disposed { get; private set; }
            public void Dispose() => Disposed = true;
        }

        public ManualResetEventSlim Gate { get; } = new(true);
        public List<Media> Prepared { get; } = [];
        public string? Failure { get; set; }
        public int Attempts;
        public bool IsOpen { get; private set; }
        public bool Playing { get; private set; }
        public string? Opened { get; private set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Length { get; set; } = TimeSpan.FromMinutes(3);
        public TimeSpan Duration => IsOpen ? Length : TimeSpan.Zero;
        public float Volume { get; set; }
        public float Balance { get; set; }
        public bool Muted { get; set; }
        public bool IsBuffering { get; set; }
        public int BufferingPercent => 40;
        public EqualizerSettings Equalizer { get; } = new();
        public event EventHandler? PlaybackEnded;
        public bool OpensInBackground(string source) => NetworkMedia.IsNetwork(source);

        public IDisposable PrepareOpen(string source, NetworkOptions options, CancellationToken cancel)
        {
            Interlocked.Increment(ref Attempts);
            Gate.Wait(cancel);
            if (Failure is { } why)
                throw new IOException(why);
            var media = new Media(source);
            lock (Prepared)
                Prepared.Add(media);
            return media;
        }

        public void OpenPrepared(IDisposable prepared)
        {
            Opened = ((Media)prepared).Source;
            IsOpen = true;
            Position = TimeSpan.Zero;
        }

        public void Open(string source)
        {
            Opened = source;
            IsOpen = true;
        }

        public void Close() => IsOpen = false;
        public void Play() => Playing = true;
        public void Pause() => Playing = false;
        public void Stop() => Playing = false;
        public bool GetSpectrum(Span<float> bins) => false;
        public (int Width, int Height) VideoSize => default;
        public VideoFrame? CurrentVideoFrame => null;
        public void Dispose() { }
        public void End() => PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    private static (MediaPlayer Player, NetEngine Engine) Start()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        var engine = new NetEngine();
        var player = new MediaPlayer(engine, new FakeInfoReader()) { ReconnectWaits = [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero] };
        return (player, engine);
    }

    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 500 && !done(); i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(done());
    }

    [Fact]
    public async Task A_network_item_connects_without_blocking_and_then_plays()
    {
        var (player, engine) = Start();
        engine.Gate.Reset();
        player.OpenFiles(["http://host/a.mp3"]); // returns although the server has not answered
        Assert.True(player.IsConnecting);
        Assert.Equal(NetworkPhase.Connecting, player.NetworkPhase);
        Assert.Equal(PlayState.Transitioning, player.PlayState);
        engine.Gate.Set();
        await Until(() => player.PlayState == PlayState.Playing);
        Assert.True(engine.Playing);
        Assert.Equal(NetworkPhase.None, player.NetworkPhase);
    }

    [Fact]
    public async Task Stop_cancels_connecting()
    {
        var (player, engine) = Start();
        engine.Gate.Reset();
        player.OpenFiles(["http://host/a.mp3"]);
        player.Stop();
        Assert.False(player.IsConnecting);
        Assert.Equal(PlayState.Stopped, player.PlayState);
        await player.PendingOpen;
        Assert.False(engine.IsOpen);
        Assert.Equal(NetworkPhase.None, player.NetworkPhase);
    }

    [Fact]
    public async Task A_newer_open_wins_and_the_older_result_is_disposed()
    {
        var (player, engine) = Start();
        engine.Gate.Reset();
        player.OpenFiles(["http://host/slow.mp3"]);
        var first = player.PendingOpen;
        player.OpenFiles(["http://host/second.mp3"]);
        engine.Gate.Set();
        await first;
        await player.PendingOpen;
        await Until(() => player.PlayState == PlayState.Playing);
        Assert.Equal("http://host/second.mp3", engine.Opened);
        Assert.All(engine.Prepared.Where(m => m.Source.Contains("slow")), m => Assert.True(m.Disposed));
    }

    [Fact]
    public async Task Pause_while_connecting_opens_the_item_paused()
    {
        var (player, engine) = Start();
        engine.Gate.Reset();
        player.OpenFiles(["http://host/a.mp3"]);
        player.Pause();
        engine.Gate.Set();
        await Until(() => player.PlayState == PlayState.Paused);
        Assert.False(engine.Playing);
    }

    [Fact]
    public async Task A_failure_is_reported_without_the_password()
    {
        var (player, engine) = Start();
        engine.Failure = "Connection refused for http://me:hunter2@host/a.mp3";
        string? error = null;
        player.Error += (_, message) => error = message;
        player.OpenFiles(["http://me:hunter2@host/a.mp3"]);
        await player.PendingOpen;
        Assert.Equal(NetworkPhase.Failed, player.NetworkPhase);
        Assert.Contains("Connection refused", player.NetworkMessage);
        Assert.DoesNotContain("hunter2", player.NetworkMessage + error);
        Assert.Equal(PlayState.Stopped, player.PlayState);
    }

    [Fact]
    public async Task A_live_stream_that_ends_is_connected_again_a_few_times()
    {
        var (player, engine) = Start();
        engine.Length = TimeSpan.Zero; // live
        player.OpenFiles(["http://host/radio"]);
        await Until(() => player.PlayState == PlayState.Playing);
        engine.End();
        await Until(() => player.PlayState == PlayState.Playing && engine.Attempts == 2);

        engine.Failure = "gone";
        engine.End();
        await Until(() => player.NetworkPhase == NetworkPhase.Failed);
        Assert.Equal(2 + 2, engine.Attempts); // the waits left: three in all
        Assert.Equal(PlayState.Stopped, player.PlayState);
    }

    [Fact]
    public async Task A_file_that_stops_early_carries_on_where_it_was_and_one_that_finishes_does_not_reconnect()
    {
        var (player, engine) = Start();
        player.OpenFiles(["http://host/a.mp3"]);
        await Until(() => player.PlayState == PlayState.Playing);
        engine.Position = TimeSpan.FromSeconds(60);
        engine.End();
        await Until(() => engine.Attempts == 2 && player.PlayState == PlayState.Playing);
        Assert.Equal(60, engine.Position.TotalSeconds, 1);

        engine.Position = engine.Length;
        engine.End();
        Assert.Equal(2, engine.Attempts);
        Assert.Equal(PlayState.Stopped, player.PlayState);
    }

    [Fact]
    public async Task Buffering_is_reported_while_playing()
    {
        var (player, engine) = Start();
        player.OpenFiles(["http://host/a.mp3"]);
        await Until(() => player.PlayState == PlayState.Playing);
        engine.IsBuffering = true;
        player.Tick(TimeSpan.FromMilliseconds(30));
        Assert.Equal("Buffering 40%", player.NetworkMessage);
        engine.IsBuffering = false;
        player.Tick(TimeSpan.FromMilliseconds(30));
        Assert.Equal(NetworkPhase.None, player.NetworkPhase);
    }

    [Fact]
    public void A_restored_network_queue_waits_for_Play()
    {
        var (player, engine) = Start();
        player.RestoreQueue([new MediaItem("http://host/a.mp3")], 0, 10);
        Assert.Equal(0, engine.Attempts);
        Assert.False(player.IsConnecting);
    }
}

/// <summary>The real engine against a web server in this process (no outside service): FFmpeg, the read-ahead buffer, cancel.</summary>
public sealed class NetworkEngineTests
{
    /// <summary>A tiny HTTP server on the loopback address: serves one WAV, with byte ranges; or accepts and says nothing.</summary>
    private sealed class Server : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly byte[] _body;
        private readonly List<TcpClient> _held = [];

        public Server(byte[] body, bool silent = false)
        {
            _body = body;
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/tone.wav";
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                        if (silent)
                            _held.Add(client); // kept alive, or the collector would close the connection
                        else
                            _ = Task.Run(() => Serve(client));
                    }
                }
                catch (Exception)
                {
                    // Stopped.
                }
            });
        }

        public string Url { get; }

        public List<string> Requests { get; } = [];

        private async Task Serve(TcpClient client)
        {
            using var connection = client;
            try
            {
                var stream = client.GetStream();
                var request = new StringBuilder();
                var one = new byte[1];
                while (!request.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    if (await stream.ReadAsync(one, _stop.Token) == 0)
                        return;
                    request.Append((char)one[0]);
                }

                lock (Requests)
                    Requests.Add(request.ToString());
                if (request.ToString().Contains("missing", StringComparison.Ordinal))
                {
                    await stream.WriteAsync("HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), _stop.Token);
                    return;
                }

                var from = 0L;
                var range = System.Text.RegularExpressions.Regex.Match(request.ToString(), @"Range: bytes=(\d+)-");
                if (range.Success)
                    from = Math.Min(_body.Length, long.Parse(range.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
                var head = (range.Success ? $"HTTP/1.1 206 Partial Content\r\nContent-Range: bytes {from}-{_body.Length - 1}/{_body.Length}\r\n" : "HTTP/1.1 200 OK\r\n")
                    + $"Content-Type: audio/wav\r\nAccept-Ranges: bytes\r\nContent-Length: {_body.Length - from}\r\nConnection: close\r\n\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head), _stop.Token);
                await stream.WriteAsync(_body.AsMemory((int)from), _stop.Token);
            }
            catch (Exception)
            {
                // The player hung up.
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            foreach (var client in _held)
                client.Dispose();
        }
    }

    // Twelve seconds of 8 kHz mono: quiet for six, then a constant level, so a seek can be told from the sound.
    private static byte[] Wav()
    {
        const int rate = 8000, seconds = 12;
        var memory = new MemoryStream();
        var writer = new BinaryWriter(memory);
        var bytes = rate * seconds * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + bytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(bytes);
        for (var i = 0; i < rate * seconds; i++)
            writer.Write((short)(i < rate * 6 ? 0 : 16000));
        return memory.ToArray();
    }

    private static void NeedFfmpeg() => Assert.SkipWhen(!FfmpegLibrary.IsAvailable, "FFmpeg is not available");

    [Fact]
    public void Sound_over_http_is_buffered_and_can_seek()
    {
        NeedFfmpeg();
        using var server = new Server(Wav());
        var abort = new NetworkAbort();
        var decoder = new FfmpegAudioSource(server.Url, -1, new NetworkInput(new NetworkOptions(), abort));
        Assert.False(decoder.HasVideo);
        using var source = new ReadAheadSource(decoder, 3, abort.Cancel);
        Assert.Equal(12, source.TotalTime.TotalSeconds, 1);
        Assert.True(source.WaitUntilReady(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        var samples = new float[4000];
        Assert.Equal(4000, source.Read(samples, 0, samples.Length));
        Assert.Equal(0f, samples.Max(Math.Abs));
        Assert.Equal(0.25, source.CurrentTime.TotalSeconds, 2);

        source.CurrentTime = TimeSpan.FromSeconds(8);
        Assert.Equal(8, source.CurrentTime.TotalSeconds, 2); // reads as the target at once
        Assert.True(source.WaitUntilReady(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        source.Read(samples, 0, samples.Length);
        Assert.InRange(samples.Max(), 0.3f, 0.4f); // 0.49 mono, 3 dB down on each of the two channels
    }

    [Fact]
    public void A_dry_buffer_gives_silence_and_says_it_is_buffering_and_the_end_is_the_end()
    {
        NeedFfmpeg();
        using var server = new Server(Wav());
        var abort = new NetworkAbort();
        using var source = new ReadAheadSource(new FfmpegAudioSource(server.Url, -1, new NetworkInput(new NetworkOptions(), abort)), 1, abort.Cancel);
        var samples = new float[2048];
        var total = 0L;
        var deadline = Environment.TickCount64 + 20_000;
        int read;
        while ((read = source.Read(samples, 0, samples.Length)) > 0 && Environment.TickCount64 < deadline)
        {
            if (source.IsBuffering)
                Thread.Sleep(5);
            else
                total += read;
        }

        Assert.Equal(0, read);
        Assert.InRange(total, 8000 * 2 * 11, 8000 * 2 * 13);
    }

    [Fact]
    public void The_engine_prepares_off_thread_and_a_bad_address_fails_with_a_reason()
    {
        NeedFfmpeg();
        using var server = new Server(Wav());
        using var engine = new MediaEngine();
        var missing = server.Url.Replace("tone", "missing");
        var error = Assert.ThrowsAny<Exception>(() => engine.PrepareOpen(missing, new NetworkOptions(Reconnect: false), TestContext.Current.CancellationToken));
        Assert.Contains("404", error.Message);
        using var prepared = engine.PrepareOpen(server.Url, new NetworkOptions(BufferSeconds: 3), TestContext.Current.CancellationToken);
        Assert.NotNull(prepared);
        Assert.Contains(server.Requests, r => r.Contains("User-Agent: Pardeh", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Video_over_http_has_a_picture()
    {
        NeedFfmpeg();
        var path = Path.Combine(Path.GetTempPath(), $"pardeh-net-{Guid.NewGuid():N}.mkv");
        MkvSubtitleTests.WriteMkv(path);
        try
        {
            using var server = new Server(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            using var engine = new MediaEngine();
            var prepared = engine.PrepareOpen(server.Url, new NetworkOptions(), TestContext.Current.CancellationToken);
            engine.OpenPrepared(prepared);
            Assert.Equal((32, 24), engine.VideoSize);
            Assert.Equal(6, engine.Duration.TotalSeconds, 0.5);
            engine.Play();
            for (var i = 0; i < 100 && engine.CurrentVideoFrame is null; i++)
                await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.NotNull(engine.CurrentVideoFrame);
            engine.Close();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Failures_given_as_bare_numbers_are_said_in_words()
    {
        Assert.Equal("The server did not answer in time.", MediaPlayer.FriendlyReason("Connecting failed: Error number -138 occurred"));
        Assert.Equal("Connecting failed: Server returned 404 Not Found", MediaPlayer.FriendlyReason("Connecting failed: Server returned 404 Not Found"));
    }

    [Fact]
    public void A_downloaded_playlist_may_only_name_network_addresses()
    {
        var entries = Pardeh.Core.Library.PlaylistFormats.ParseRemote("http://host/lists/radio.m3u",
            "#EXTM3U\nstream.mp3\nhttps://other/live\nC:\\Windows\\secret.wav\nfile:///c/x.mp3\n/abs.mp3\n");
        Assert.Equal(["http://host/lists/stream.mp3", "https://other/live", "http://host/abs.mp3"], entries);
        Assert.Equal(["http://r/a"], Pardeh.Core.Library.PlaylistFormats.ParseRemote("http://h/x.pls", "[playlist]\nFile1=http://r/a\nTitle1=A\n"));
        Assert.Null(Pardeh.Core.Library.PlaylistFormats.ParseRemote("http://h/live.m3u", "#EXTM3U\n#EXT-X-VERSION:3\nseg1.ts\n"));
    }

    [Fact]
    public async Task A_server_that_says_nothing_can_be_cancelled_quickly()
    {
        NeedFfmpeg();
        using var server = new Server([], silent: true);
        using var engine = new MediaEngine();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
        var started = Environment.TickCount64;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task.Run(() => engine.PrepareOpen(server.Url, new NetworkOptions(), cancel.Token)));
        Assert.InRange(Environment.TickCount64 - started, 0, 5000);
    }

    [Fact]
    public void The_connect_timeout_gives_up_on_a_silent_server()
    {
        NeedFfmpeg();
        using var server = new Server([], silent: true);
        using var engine = new MediaEngine();
        var started = Environment.TickCount64;
        Assert.ThrowsAny<Exception>(() => engine.PrepareOpen(server.Url, new NetworkOptions(Reconnect: false) { ConnectTimeout = TimeSpan.FromSeconds(2) }, TestContext.Current.CancellationToken));
        Assert.InRange(Environment.TickCount64 - started, 1500, 8000);
    }
}
