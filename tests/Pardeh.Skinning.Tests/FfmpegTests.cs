using Pardeh.Nava;
using Pardeh.Ffmpeg;

namespace Pardeh.Skinning.Tests;

/// <summary>FFmpeg decoding. Skipped when the FFmpeg libraries aren't installed (scripts/get-ffmpeg.ps1).</summary>
public sealed class FfmpegTests : IDisposable
{
    private const int Rate = 44100;
    private readonly string _wav = Path.Combine(Path.GetTempPath(), $"pardeh-test-{Guid.NewGuid():N}.wav");

    public FfmpegTests()
    {
        Assert.SkipWhen(!FfmpegLibrary.IsAvailable, $"FFmpeg not available: {FfmpegLibrary.LoadError}");
        WriteTone(_wav, seconds: 4, frequency: 440);
    }

    public void Dispose() => File.Delete(_wav);

    [Fact]
    public void Decodes_a_wav_to_float_stereo()
    {
        using var source = new FfmpegAudioSource(_wav);

        Assert.Equal(Rate, source.WaveFormat.SampleRate);
        Assert.Equal(2, source.WaveFormat.Channels);
        Assert.InRange(source.TotalTime.TotalSeconds, 3.99, 4.01);

        var buffer = new float[Rate * 2];
        var total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            total += read;

        Assert.InRange(total / 2, Rate * 4 - 10, Rate * 4 + 10);
    }

    [Fact]
    public void Seeks_precisely()
    {
        using var source = new FfmpegAudioSource(_wav);

        source.CurrentTime = TimeSpan.FromSeconds(2.5);
        var buffer = new float[2048];
        var read = source.Read(buffer, 0, buffer.Length);

        Assert.True(read > 0);
        // The tone's phase at exactly 2.5 s: sin(2π·440·2.5) = 0, rising.
        Assert.InRange(buffer[0], -0.02f, 0.02f);
        Assert.InRange(source.CurrentTime.TotalSeconds, 2.5, 2.53);
    }

    [Fact]
    public void Reads_into_naudio_aliased_buffers()
    {
        // WASAPI output hands us a byte[] viewed as float[] (NAudio WaveBuffer); Array.Copy would throw.
        using var source = new FfmpegAudioSource(_wav);
        var aliased = new NAudio.Wave.WaveBuffer(new byte[4096 * 4]).FloatBuffer;

        var read = source.Read(aliased, 0, 4096);

        Assert.Equal(4096, read);
    }

    private static void WriteTone(string path, int seconds, double frequency)
    {
        var frames = Rate * seconds;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + frames * 4);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)2);
        writer.Write(Rate);
        writer.Write(Rate * 4);
        writer.Write((short)4);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(frames * 4);
        for (var i = 0; i < frames; i++)
        {
            var sample = (short)(Math.Sin(2 * Math.PI * frequency * i / Rate) * 16000);
            writer.Write(sample);
            writer.Write(sample);
        }
    }
}
