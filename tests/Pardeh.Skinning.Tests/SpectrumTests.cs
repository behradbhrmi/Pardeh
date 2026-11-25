using Pardeh.Nava;

namespace Pardeh.Skinning.Tests;

public class SpectrumTests
{
    [Fact]
    public void A_sine_wave_shows_up_in_its_band()
    {
        var analyzer = new SpectrumAnalyzer(44100);
        var samples = new float[4096 * 2];
        for (var i = 0; i < 4096; i++)
        {
            var v = (float)(0.05 * Math.Sin(2 * Math.PI * 220 * i / 44100.0));
            samples[i * 2] = v;
            samples[i * 2 + 1] = v;
        }

        analyzer.Write(samples, 2);
        var bands = new float[40];
        analyzer.Compute(bands);

        Assert.True(bands.Max() > 0.3f, $"max band {bands.Max()}");
        Assert.True(bands[^1] < 0.2f, $"top band {bands[^1]}");
    }
}
