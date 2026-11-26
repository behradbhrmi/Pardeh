using Pardeh.Engine;
using NAudio.Wave;
using Pardeh.Core.Playback;
using Pardeh.Nava;
using Pardeh.Ffmpeg;

namespace Pardeh.Skinning.Tests;

public sealed class GaplessTests
{
    /// <summary>A reader over samples in memory that, like a decoder, may hand out less than asked before its end.</summary>
    private sealed class Memory(float[] samples, int rate = 44100, int chunk = int.MaxValue) : IAudioSource
    {
        private int _at;

        public bool Disposed { get; private set; }
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(rate, 2);
        public TimeSpan TotalTime => TimeSpan.FromSeconds(samples.Length / 2.0 / rate);
        public TimeSpan CurrentTime { get => TimeSpan.FromSeconds(_at / 2.0 / rate); set => _at = (int)(value.TotalSeconds * rate) * 2; }
        public string DecoderName => "memory";
        public void Dispose() => Disposed = true;

        public int Read(float[] buffer, int offset, int count)
        {
            var n = Math.Min(Math.Min(count, chunk), samples.Length - _at);
            Array.Copy(samples, _at, buffer, offset, n);
            _at += n;
            return n;
        }
    }

    private static float[] Ramp(int length, int from) => Enumerable.Range(from, length).Select(i => i / 100000f).ToArray();

    private static float[] Drain(IAudioSource source, int block)
    {
        var all = new List<float>();
        var buffer = new float[block];
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            all.AddRange(buffer.Take(read));
        return [.. all];
    }

    [Theory]
    [InlineData(1024, int.MaxValue)]
    [InlineData(998, 254)] // block and decoder chunk sizes that do not line up with the join
    public void The_tracks_are_joined_sample_exactly(int block, int chunk)
    {
        var first = new Memory(Ramp(3000, 0), chunk: chunk);
        var second = new Memory(Ramp(2000, 3000), chunk: chunk);
        var chain = new GaplessSource(first);
        var started = 0;
        Assert.True(chain.SetNext(second, 1f, () => started++));

        var buffer = new float[block];
        var all = new List<float>();
        int read;
        while ((read = chain.Read(buffer, 0, block)) > 0)
        {
            if (all.Count + read < 5000)
                Assert.Equal(block, read); // no short read at the join: the output would take one as the end
            all.AddRange(buffer.Take(read));
        }

        Assert.Equal(Ramp(5000, 0), all);
        Assert.Equal(1, started);
        Assert.True(first.Disposed);
        Assert.Equal(second.TotalTime, chain.TotalTime);
    }

    [Fact]
    public void A_different_sample_rate_is_refused_and_a_dropped_track_never_plays()
    {
        var chain = new GaplessSource(new Memory(Ramp(100, 0)));
        Assert.False(chain.SetNext(new Memory(Ramp(100, 0), rate: 48000), 1f, () => { }));

        var dropped = new Memory(Ramp(100, 0));
        Assert.True(chain.SetNext(dropped, 1f, () => Assert.Fail("dropped")));
        chain.DropNext();
        Assert.True(dropped.Disposed);
        Assert.Equal(100, Drain(chain, 64).Length);
    }

    [Fact]
    public void The_next_tracks_loudness_applies_from_its_first_sample_until_the_output_takes_over()
    {
        var chain = new GaplessSource(new Memory([1f, 1f, 1f, 1f]));
        chain.SetNext(new Memory(Enumerable.Repeat(1f, 8).ToArray()), 0.5f, () => { });
        var buffer = new float[8];
        chain.Read(buffer, 0, 8);
        Assert.Equal([1f, 1f, 1f, 1f, 0.5f, 0.5f, 0.5f, 0.5f], buffer);
        chain.ResetGain();
        chain.Read(buffer, 0, 4);
        Assert.Equal(1f, buffer[0]);
    }

    [Fact]
    public void Two_decoded_files_make_one_unbroken_tone()
    {
        Assert.SkipWhen(!FfmpegLibrary.IsAvailable, $"FFmpeg not available: {FfmpegLibrary.LoadError}");
        const int rate = 44100, frames = rate / 2;
        var folder = Directory.CreateTempSubdirectory("pardeh-gapless-").FullName;
        try
        {
            // One 440 Hz tone cut in two files: the join must not show.
            string Write(string name, int from)
            {
                var path = Path.Combine(folder, name);
                using var writer = new WaveFileWriter(path, new WaveFormat(rate, 16, 2));
                for (var i = from; i < from + frames; i++)
                {
                    var v = (float)Math.Sin(2 * Math.PI * 440 * i / rate) * 0.5f;
                    writer.WriteSample(v);
                    writer.WriteSample(v);
                }

                return path;
            }

            var chain = new GaplessSource(new FfmpegAudioSource(Write("a.wav", 0)));
            Assert.True(chain.SetNext(new FfmpegAudioSource(Write("b.wav", frames)), 1f, () => { }));
            var samples = Drain(chain, 4096);
            chain.Dispose();

            Assert.Equal(frames * 4, samples.Length);
            var largest = 0f;
            for (var i = 2; i < samples.Length; i += 2)
                largest = Math.Max(largest, Math.Abs(samples[i] - samples[i - 2]));
            // The steepest step of this sine is 2π·440/44100·0.5 ≈ 0.0314; a gap or a click would be far larger.
            Assert.InRange(largest, 0.03f, 0.033f);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>The real engine over the real audio output, silent (volume 0). Opt in with PARDEH_AUDIO_TESTS=1.</summary>
    [Fact]
    public async Task The_real_engine_runs_on_into_the_prepared_track()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("PARDEH_AUDIO_TESTS") != "1" || !FfmpegLibrary.IsAvailable, "set PARDEH_AUDIO_TESTS=1 (needs an audio device and FFmpeg)");
        var folder = Directory.CreateTempSubdirectory("pardeh-gapless-live-").FullName;
        try
        {
            string Write(string name, double seconds)
            {
                var path = Path.Combine(folder, name);
                using var writer = new WaveFileWriter(path, new WaveFormat(44100, 16, 2));
                for (var i = 0; i < (int)(seconds * 44100) * 2; i++)
                    writer.WriteSample(0);
                return path;
            }

            var a = Write("a.wav", 1.5);
            var b = Write("b.wav", 1.0);
            using var engine = new MediaEngine { Volume = 0, Muted = true };
            var advanced = new TaskCompletionSource<string>();
            var ended = new TaskCompletionSource<bool>();
            engine.AdvancedToNext += (_, source) => advanced.TrySetResult(source);
            engine.PlaybackEnded += (_, _) => ended.TrySetResult(advanced.Task.IsCompleted);
            engine.Open(a);
            engine.PrepareNext(b, 1f);
            engine.Play();

            Assert.Equal(b, await advanced.Task.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken));
            Assert.InRange(engine.Duration.TotalSeconds, 0.95, 1.05); // the new track's length
            Assert.InRange(engine.Position.TotalSeconds, 0, 0.8);
            Assert.True(await ended.Task.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken), "ended only after running on");
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>A crossfade on the real engine: the next track takes over at once and plays to its own end. Opt in with PARDEH_AUDIO_TESTS=1.</summary>
    [Fact]
    public async Task The_real_engine_crossfades_into_another_track()
    {
        Assert.SkipWhen(Environment.GetEnvironmentVariable("PARDEH_AUDIO_TESTS") != "1" || !FfmpegLibrary.IsAvailable, "set PARDEH_AUDIO_TESTS=1 (needs an audio device and FFmpeg)");
        var folder = Directory.CreateTempSubdirectory("pardeh-crossfade-live-").FullName;
        try
        {
            string Write(string name, double seconds)
            {
                var path = Path.Combine(folder, name);
                using var writer = new WaveFileWriter(path, new WaveFormat(44100, 16, 2));
                for (var i = 0; i < (int)(seconds * 44100) * 2; i++)
                    writer.WriteSample(0);
                return path;
            }

            var a = Write("a.wav", 4.0);
            var b = Write("b.wav", 1.5);
            using var engine = new MediaEngine { Volume = 0, Muted = true };
            var ends = 0;
            var ended = new TaskCompletionSource();
            engine.PlaybackEnded += (_, _) =>
            {
                Interlocked.Increment(ref ends);
                ended.TrySetResult();
            };
            engine.Open(a);
            engine.Play();
            await Task.Delay(600, TestContext.Current.CancellationToken);

            engine.OpenCrossfade(b, 0.5);
            engine.Play();
            Assert.InRange(engine.Duration.TotalSeconds, 1.45, 1.55); // the new track's
            Assert.InRange(engine.Position.TotalSeconds, 0, 0.6);

            await ended.Task.WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken);
            await Task.Delay(500, TestContext.Current.CancellationToken);
            Assert.Equal(1, ends); // only the new track's end counts, not the faded one's
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    // ---- The player's side ---------------------------------------------------------------------------

    private sealed class GaplessEngine : IPlaybackEngine
    {
        public List<string> Opened { get; } = [];
        public string? Prepared { get; private set; }
        public int Drops { get; private set; }
        public bool IsOpen { get; private set; }
        public TimeSpan Position { get; set; }
        public TimeSpan Duration => TimeSpan.FromMinutes(3);
        public float Volume { get; set; }
        public float Balance { get; set; }
        public bool Muted { get; set; }
        public EqualizerSettings Equalizer { get; } = new();
        public (int Width, int Height) VideoSize => default;
        public VideoFrame? CurrentVideoFrame => null;
        public event EventHandler? PlaybackEnded;
        public event EventHandler<string>? AdvancedToNext;

        public void Open(string source)
        {
            IsOpen = true;
            Opened.Add(source);
            Position = TimeSpan.Zero;
        }

        public void PrepareNext(string source, float levelFactor) => Prepared = source;

        public void DropNext()
        {
            Drops++;
            Prepared = null;
        }

        public void Advance()
        {
            var next = Prepared!;
            Prepared = null;
            Position = TimeSpan.Zero;
            AdvancedToNext?.Invoke(this, next);
        }

        public void Close() => IsOpen = false;
        public void Play() { }
        public void Pause() { }
        public void Stop() { }
        public bool GetSpectrum(Span<float> bins) => false;
        public void Dispose() => PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    private static (MediaPlayer, GaplessEngine) Player(params string[] names)
    {
        var engine = new GaplessEngine();
        var player = new MediaPlayer(engine, new FakeInfoReader());
        player.OpenItems(names.Select(n => new MediaItem(n)), 0);
        return (player, engine);
    }

    private static void NearTheEnd(MediaPlayer player, GaplessEngine engine)
    {
        engine.Position = TimeSpan.FromSeconds(175);
        player.Tick(TimeSpan.FromMilliseconds(30));
    }

    [Fact]
    public void The_next_track_is_prepared_near_the_end_and_adopted_without_opening_it()
    {
        var (player, engine) = Player("a.mp3", "b.mp3", "c.mp3");
        player.Tick(TimeSpan.FromMilliseconds(30));
        Assert.Null(engine.Prepared); // far from the end

        NearTheEnd(player, engine);
        Assert.Equal("b.mp3", engine.Prepared);
        var changes = 0;
        player.CurrentMediaChanged += (_, _) => changes++;

        engine.Advance();

        Assert.Equal("b.mp3", player.CurrentMedia!.Source);
        Assert.Equal(1, player.CurrentPlaylist.CurrentIndex);
        Assert.Equal(PlayState.Playing, player.PlayState);
        Assert.Equal(1, changes);
        Assert.Equal(["a.mp3"], engine.Opened); // the engine ran on; nothing was opened again
    }

    [Fact]
    public void A_changed_queue_prepares_again_and_the_tools_that_end_a_track_hold_gapless_back()
    {
        var (player, engine) = Player("a.mp3", "b.mp3", "c.mp3");
        NearTheEnd(player, engine);
        player.CurrentPlaylist.Move(2, 1);
        player.Tick(TimeSpan.FromMilliseconds(30));
        Assert.Equal("c.mp3", engine.Prepared);

        player.StartSleepAtEndOfTrack();
        player.Tick(TimeSpan.FromMilliseconds(30));
        Assert.Null(engine.Prepared);
        player.CancelSleepTimer();

        player.Equalizer.CrossFade = true;
        player.Equalizer.CrossFadeSeconds = 2;
        engine.Position = TimeSpan.FromSeconds(171);
        player.Tick(TimeSpan.FromMilliseconds(30));
        Assert.Null(engine.Prepared);
        player.Equalizer.CrossFade = false;

        player.Gapless = false;
        NearTheEnd(player, engine);
        Assert.Null(engine.Prepared);
    }

    [Fact]
    public void Videos_and_the_last_track_are_not_prepared()
    {
        var (player, engine) = Player("a.mp3", "film.mp4");
        NearTheEnd(player, engine);
        Assert.Null(engine.Prepared);

        var (single, other) = Player("a.mp3");
        NearTheEnd(single, other);
        Assert.Null(other.Prepared);
        single.Loop = true; // repeating one song: it follows itself
        single.Tick(TimeSpan.FromMilliseconds(30));
        Assert.Equal("a.mp3", other.Prepared);
    }
}
