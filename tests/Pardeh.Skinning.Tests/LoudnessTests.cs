using NAudio.Wave;
using Pardeh.Core.Playback;
using Pardeh.Nava;

namespace Pardeh.Skinning.Tests;

public sealed class LoudnessTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("pardeh-loud-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static float[] Sine(int rate, double seconds, double dbfs, double frequency = 997)
    {
        var amplitude = Math.Pow(10, dbfs / 20);
        var samples = new float[(int)(rate * seconds) * 2];
        for (var i = 0; i < samples.Length / 2; i++)
            samples[i * 2] = samples[i * 2 + 1] = (float)(amplitude * Math.Sin(2 * Math.PI * frequency * i / rate));
        return samples;
    }

    private static LoudnessResult Measure(int rate, params float[][] parts)
    {
        var meter = new LoudnessMeter(rate, 2);
        foreach (var part in parts)
        {
            for (var at = 0; at < part.Length; at += 3000) // any block size gives the same answer
                meter.Add(part.AsSpan(at, Math.Min(3000, part.Length - at)));
        }

        return meter.Result();
    }

    [Theory] // EBU Tech 3341: a stereo 1 kHz sine at -23 dBFS reads -23 LUFS
    [InlineData(48000, -23)]
    [InlineData(44100, -23)]
    [InlineData(22050, -33)]
    [InlineData(96000, -18)]
    public void A_reference_tone_reads_its_level(int rate, double dbfs)
    {
        var result = Measure(rate, Sine(rate, 5, dbfs));
        Assert.InRange(result.Lufs, dbfs - 0.15, dbfs + 0.15);
        Assert.InRange(result.Peak, Math.Pow(10, dbfs / 20) * 0.99, Math.Pow(10, dbfs / 20) * 1.001);
    }

    [Fact]
    public void Silence_and_very_quiet_stretches_are_gated_out()
    {
        const int rate = 48000;
        var gated = Measure(rate, Sine(rate, 5, -23), new float[rate * 2 * 20], Sine(rate, 5, -60));
        Assert.InRange(gated.Lufs, -23.3, -22.8);
        var silent = Measure(rate, new float[rate * 4]);
        Assert.True(double.IsNegativeInfinity(silent.Lufs));
        Assert.Equal(1, silent.Factor);
    }

    [Fact]
    public void Something_shorter_than_a_block_is_still_measured()
    {
        Assert.InRange(Measure(48000, Sine(48000, 0.25, -23)).Lufs, -24, -22);
    }

    [Fact]
    public void The_factor_moves_a_track_to_the_reference_without_clipping()
    {
        Assert.Equal(Math.Pow(10, 5 / 20.0), new LoudnessResult(-23, 0.1).Factor, 4);   // +5 dB to -18
        Assert.Equal(Math.Pow(10, -8 / 20.0), new LoudnessResult(-10, 1).Factor, 4);    // a loud one comes down
        Assert.Equal(1 / 0.9, new LoudnessResult(-30, 0.9).Factor, 4);                  // a boost stops at full scale
    }

    [Fact]
    public async Task The_service_measures_once_and_remembers_until_the_file_changes()
    {
        var file = Path.Combine(_folder, "a.mp3");
        File.WriteAllText(file, "x");
        var calls = 0;
        LoudnessService Make() => new(Path.Combine(_folder, "loudness.json"), (_, _) =>
        {
            Interlocked.Increment(ref calls);
            return new LoudnessResult(-20, 0.5);
        });

        async Task<string> Run(LoudnessService service)
        {
            var done = new TaskCompletionSource<string>();
            service.Measured += s => done.TrySetResult(s);
            service.Request(file);
            return await done.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }

        Assert.Equal(file, await Run(Make()));
        var again = Make();
        Assert.Equal(new LoudnessResult(-20, 0.5), again.Find(file));
        again.Request(file);
        Assert.False(again.Busy);
        Assert.Equal(1, calls);

        File.WriteAllText(file, "changed");
        Assert.Null(again.Find(file));
        await Run(again);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task The_player_levels_an_untagged_track_once_it_is_measured_and_prefers_tags()
    {
        var service = new LoudnessService(null, (_, _) => new LoudnessResult(-24, 0.1));
        var measured = new TaskCompletionSource();
        service.Measured += _ => measured.TrySetResult();
        var player = new MediaPlayer(new FakeEngine(), new FakeInfoReader()) { Loudness = service };
        var plain = new MediaItem("untagged.mp3");
        var tagged = new MediaItem("tagged.mp3");
        tagged.Attributes[ReplayGain.TrackGain] = "-6 dB";

        Assert.Equal(1, player.LevelFactorFor(plain)); // levelling off
        player.ReplayGain = ReplayGainMode.Track;
        player.OpenItems([plain], 0);
        await measured.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(Math.Pow(10, 6 / 20.0), player.LevelFactorFor(plain), 4);
        Assert.Equal(Math.Pow(10, -6 / 20.0), player.LevelFactorFor(tagged), 4);
        Assert.Equal(1, player.LevelFactorFor(new MediaItem("film.mp4"))); // films are not measured
    }

    [Fact]
    public void A_decoded_file_is_measured()
    {
        var path = Path.Combine(_folder, "tone.wav");
        using (var writer = new WaveFileWriter(path, new WaveFormat(44100, 16, 2)))
        {
            var samples = Sine(44100, 4, -23);
            writer.WriteSamples(samples, 0, samples.Length);
        }

        var result = NavaLoudness.Measure(path, TestContext.Current.CancellationToken);
        Assert.NotNull(result);
        Assert.InRange(result!.Lufs, -23.3, -22.8);
    }
}
