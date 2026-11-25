using NAudio.Wave;
using Pardeh.Nava;

namespace Pardeh.Skinning.Tests;

/// <summary>Play speed: length changes with the rate, pitch does not.</summary>
public class TimeStretchTests
{
    private const int Rate = 44100;

    /// <summary>A stereo 440 Hz tone.</summary>
    private sealed class Tone(double seconds) : ISampleProvider
    {
        private long _frame;
        private readonly long _frames = (long)(seconds * Rate);

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            var n = 0;
            for (; n + 1 < count && _frame < _frames; n += 2, _frame++)
                buffer[offset + n] = buffer[offset + n + 1] = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * _frame / Rate));
            return n;
        }
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(2.0)]
    [InlineData(4.0)]
    public void Changes_length_but_keeps_pitch(double speed)
    {
        const double seconds = 4;
        var stretcher = new TimeStretcher(new Tone(seconds)) { Rate = speed };
        var output = new List<float>();
        var buffer = new float[8192];
        int read;
        while ((read = stretcher.Read(buffer, 0, buffer.Length)) > 0)
            output.AddRange(buffer.Take(read));

        var outSeconds = output.Count / 2.0 / Rate;
        Assert.InRange(outSeconds, seconds / speed * 0.95, seconds / speed * 1.05 + 0.15);

        // Rising zero crossings on the left channel → frequency.
        var crossings = 0;
        for (var i = 2; i < output.Count; i += 2)
            if (output[i - 2] < 0 && output[i] >= 0)
                crossings++;
        var frequency = crossings / outSeconds;
        Assert.InRange(frequency, 420, 460);
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(2.0)]
    [InlineData(0.5)]
    public void Changing_the_speed_while_playing_keeps_delivering_audio(double speed)
    {
        var stretcher = new TimeStretcher(new Tone(30));
        var buffer = new float[4410 * 2]; // what an output asks for
        for (var i = 0; i < 20; i++)
            Assert.True(stretcher.Read(buffer, 0, buffer.Length) > 0);
        stretcher.Rate = speed;
        var total = 0;
        for (var i = 0; i < 60; i++)
        {
            var read = stretcher.Read(buffer, 0, buffer.Length);
            Assert.True(read > 0, $"no audio after the speed change (read {i})");
            total += read;
        }

        Assert.True(total > 60 * buffer.Length * 0.9);
    }

    /// <summary>A source that hands out at most <c>chunk</c> frames per read, like a decoder (one codec frame at a time).</summary>
    private sealed class Chunked(ISampleProvider inner, int chunkFrames) : ISampleProvider
    {
        public WaveFormat WaveFormat => inner.WaveFormat;

        public int Read(float[] buffer, int offset, int count) => inner.Read(buffer, offset, Math.Min(count, chunkFrames * 2));
    }

    [Theory]
    [InlineData(1.5, 1152)]
    [InlineData(2.0, 1024)]
    [InlineData(0.5, 1536)]
    [InlineData(1.5, 480)]
    public void Changing_the_speed_works_with_a_decoder_that_returns_small_reads(double speed, int chunk)
    {
        var stretcher = new TimeStretcher(new Chunked(new Tone(30), chunk));
        var buffer = new float[4410 * 2];
        for (var i = 0; i < 20; i++)
            Assert.True(stretcher.Read(buffer, 0, buffer.Length) > 0);
        stretcher.Rate = speed;
        for (var i = 0; i < 80; i++)
            Assert.True(stretcher.Read(buffer, 0, buffer.Length) > 0, $"no audio after the speed change (read {i})");
    }
}
