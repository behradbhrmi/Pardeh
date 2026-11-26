using Pardeh.Engine;
using System.Diagnostics;
using Pardeh.Core.Playback;
using Pardeh.Nava;
using Pardeh.Ffmpeg;

namespace Pardeh.Skinning.Tests;

/// <summary>
/// The real engine against public test videos on the internet. Opt in with PARDEH_ONLINE_TESTS=1, or set PARDEH_ONLINE_URL to
/// try one address. Nothing personal is sent: the requests carry a generic client name.
/// </summary>
public sealed class OnlineVideoTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("large MP4 over https", "https://archive.org/download/BigBuckBunny_124/Content/big_buck_bunny_720p_surround.mp4", true)]
    [InlineData("HLS, several sizes", "https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8", true)]
    [InlineData("HLS, separate sound (fMP4)", "https://devstreaming-cdn.apple.com/videos/streaming/examples/img_bipbop_adv_example_fmp4/master.m3u8", true)]
    // DASH plays, but seeking in it does not resume: not asserted here.
    [InlineData("DASH", "https://dash.akamaized.net/dash264/TestCases/1a/netflix/exMPD_BIP_TC1.mpd", false)]
    public void A_public_video_opens_shows_a_picture_and_plays(string kind, string url, bool seek)
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("PARDEH_ONLINE_TESTS") != "1", "set PARDEH_ONLINE_TESTS=1 (uses the internet)");
        Try(kind, url, seek);
    }

    [Fact]
    public void The_given_address_opens_shows_a_picture_plays_and_seeks()
    {
        var url = Environment.GetEnvironmentVariable("PARDEH_ONLINE_URL");
        Assert.SkipWhen(url is null, "set PARDEH_ONLINE_URL to an address to try");
        Try("given address", url!, true);
    }

    private void Try(string kind, string url, bool seek)
    {
        Assert.SkipWhen(!FfmpegLibrary.IsAvailable, "needs FFmpeg");
        using var engine = new MediaEngine { Muted = true };
        var options = new NetworkOptions { UserAgent = "MediaClientTest/1.0" };
        var clock = Stopwatch.StartNew();
        using var prepared = engine.PrepareOpen(url, options, TestContext.Current.CancellationToken);
        var connected = clock.Elapsed;
        engine.OpenPrepared(prepared);
        output.WriteLine($"{kind}: connected in {connected.TotalSeconds:0.0} s, length {engine.Duration}, picture {engine.VideoSize}");

        Assert.True(Wait(() => engine.CurrentVideoFrame is not null, 30), "no picture");
        output.WriteLine($"  first picture {engine.CurrentVideoFrame!.Width}x{engine.CurrentVideoFrame.Height} after {clock.Elapsed.TotalSeconds:0.0} s");

        engine.Play();
        Assert.True(Wait(() => engine.Position > TimeSpan.FromSeconds(3), 30), $"did not play, at {engine.Position}");
        output.WriteLine($"  playing, at {engine.Position.TotalSeconds:0.0} s after {clock.Elapsed.TotalSeconds:0.0} s");

        if (seek && engine.Duration > TimeSpan.FromMinutes(1))
        {
            var target = TimeSpan.FromSeconds(engine.Duration.TotalSeconds * 0.6);
            clock.Restart();
            engine.Position = target;
            Assert.True(Wait(() => engine.Position > target + TimeSpan.FromSeconds(2) && !engine.IsBuffering, 40), $"seek did not resume, at {engine.Position}");
            output.WriteLine($"  seek to {target.TotalSeconds:0} s resumed in {clock.Elapsed.TotalSeconds:0.0} s; sound {engine.Position.TotalSeconds:0.0}, picture {engine.CurrentVideoFrame!.Time.TotalSeconds:0.0}");
            Assert.True(Wait(() => Math.Abs((engine.CurrentVideoFrame!.Time - engine.Position).TotalSeconds) < 1.5, 40),
                $"the picture did not catch up: sound {engine.Position}, picture {engine.CurrentVideoFrame!.Time}");
            output.WriteLine($"  picture caught up after {clock.Elapsed.TotalSeconds:0.0} s");
        }

        clock.Restart();
        engine.Close();
        output.WriteLine($"  closed in {clock.Elapsed.TotalSeconds:0.0} s");
    }

    private static bool Wait(Func<bool> done, int seconds)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            if (done())
                return true;
            Thread.Sleep(50);
        }

        return done();
    }
}
