using Pardeh.Nava;
using System.Net.Http;
using System.Text.Json;
using Pardeh.Ffmpeg;

namespace Pardeh.Skinning.Tests;

/// <summary>Internet radio through FFmpeg. Needs the network: runs only with PARDEH_NET_TESTS=1.</summary>
public sealed class RadioTests
{
    [Fact]
    public async Task Decodes_a_live_station_from_the_directory()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("PARDEH_NET_TESTS") != "1", "network tests are off (PARDEH_NET_TESTS=1)");
        Assert.SkipWhen(!FfmpegLibrary.IsAvailable, "FFmpeg not available");

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Pardeh/0.1");
        var json = await http.GetStringAsync("https://all.api.radio-browser.info/json/stations/topclick/5?hidebroken=true&codec=MP3", TestContext.Current.CancellationToken);
        var urls = JsonDocument.Parse(json).RootElement.EnumerateArray().Select(e => e.GetProperty("url_resolved").GetString()!).ToList();

        Exception? last = null;
        foreach (var url in urls)
        {
            try
            {
                using var source = new FfmpegAudioSource(url);
                var buffer = new float[source.WaveFormat.SampleRate * source.WaveFormat.Channels];
                var total = 0;
                for (var i = 0; i < 3 && total < buffer.Length * 2; i++)
                    total += source.Read(buffer, 0, buffer.Length);
                Assert.True(total > 0, "no samples from " + url);
                return;
            }
            catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
            {
                last = ex;
            }
        }

        throw new Exception("No station could be decoded", last);
    }
}
