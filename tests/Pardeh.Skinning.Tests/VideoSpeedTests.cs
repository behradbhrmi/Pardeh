using System.Text;
using Pardeh.Nava;

namespace Pardeh.Skinning.Tests;

/// <summary>
/// Plays a generated video (raw frames whose colour encodes the frame number, plus a tone) through the real engine and checks that the
/// picture keeps following the position before and after a play speed change. Runs only with PARDEH_VIDEO_TESTS=1 (it opens the audio device, muted).
/// </summary>
public sealed class VideoSpeedTests(ITestOutputHelper output) : IDisposable
{
    private const int W = 160, H = 120, Fps = 25, Seconds = 24, SampleRate = 44100;
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"pardeh-speed-{Guid.NewGuid():N}.avi");

    public void Dispose()
    {
        try { File.Delete(_file); } catch (IOException) { }
    }

    private static bool Enabled => Environment.GetEnvironmentVariable("PARDEH_VIDEO_TESTS") == "1" && FfmpegAvailable();

    private static bool FfmpegAvailable() => Pardeh.Nava.Ffmpeg.FfmpegLibrary.IsAvailable;

    /// <summary>An AVI of uncompressed BGR frames (pixel colour = frame number) and 16-bit stereo PCM.</summary>
    private void WriteAvi()
    {
        var frames = Fps * Seconds;
        var frameBytes = W * H * 3;
        var audioPerFrame = SampleRate / Fps * 4;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.ASCII);
        void Tag(string t) => w.Write(Encoding.ASCII.GetBytes(t));
        long Begin(string id, string? form = null)
        {
            Tag(id);
            var at = ms.Position;
            w.Write(0);
            if (form is not null)
                Tag(form);
            return at;
        }

        void End(long at)
        {
            var end = ms.Position;
            ms.Position = at;
            w.Write((int)(end - at - 4));
            ms.Position = end;
            if (((end - at - 4) & 1) != 0)
                w.Write((byte)0);
        }

        var riff = Begin("RIFF", "AVI ");
        var hdrl = Begin("LIST", "hdrl");
        var avih = Begin("avih");
        w.Write(1_000_000 / Fps); w.Write(0); w.Write(0); w.Write(0x10); w.Write(frames); w.Write(0); w.Write(2); w.Write(frameBytes); w.Write(W); w.Write(H);
        w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        End(avih);

        var strlV = Begin("LIST", "strl");
        var strhV = Begin("strh");
        Tag("vids"); Tag("DIB "); w.Write(0); w.Write((short)0); w.Write((short)0); w.Write(0); w.Write(1); w.Write(Fps); w.Write(0); w.Write(frames); w.Write(frameBytes); w.Write(-1); w.Write(0);
        w.Write((short)0); w.Write((short)0); w.Write((short)W); w.Write((short)H);
        End(strhV);
        var strfV = Begin("strf");
        w.Write(40); w.Write(W); w.Write(H); w.Write((short)1); w.Write((short)24); w.Write(0); w.Write(frameBytes); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        End(strfV);
        End(strlV);

        var strlA = Begin("LIST", "strl");
        var strhA = Begin("strh");
        Tag("auds"); w.Write(1); w.Write(0); w.Write((short)0); w.Write((short)0); w.Write(0); w.Write(4); w.Write(SampleRate * 4); w.Write(0); w.Write(frames * audioPerFrame / 4); w.Write(audioPerFrame); w.Write(-1); w.Write(4);
        w.Write((short)0); w.Write((short)0); w.Write((short)0); w.Write((short)0);
        End(strhA);
        var strfA = Begin("strf");
        w.Write((short)1); w.Write((short)2); w.Write(SampleRate); w.Write(SampleRate * 4); w.Write((short)4); w.Write((short)16);
        End(strfA);
        End(strlA);
        End(hdrl);

        var movi = Begin("LIST", "movi");
        var offsets = new List<(string Id, int Offset, int Size)>();
        var phase = 0.0;
        for (var f = 0; f < frames; f++)
        {
            var at = (int)(ms.Position - movi - 4);
            Tag("00db");
            w.Write(frameBytes);
            var row = new byte[W * 3];
            for (var x = 0; x < W; x++)
            {
                row[x * 3] = (byte)(f % 256);       // B: the frame number
                row[x * 3 + 1] = (byte)(f / 256 * 40); // G: its high part
                row[x * 3 + 2] = (byte)(x * 255 / W);  // R: a gradient so it is not a flat colour
            }

            for (var y = 0; y < H; y++)
                w.Write(row);
            offsets.Add(("00db", at, frameBytes));

            at = (int)(ms.Position - movi - 4);
            Tag("01wb");
            w.Write(audioPerFrame);
            for (var s = 0; s < audioPerFrame / 4; s++)
            {
                var v = (short)(Math.Sin(phase) * 3000);
                phase += 2 * Math.PI * 330 / SampleRate;
                w.Write(v); w.Write(v);
            }

            offsets.Add(("01wb", at, audioPerFrame));
        }

        End(movi);
        var idx = Begin("idx1");
        foreach (var (id, offset, size) in offsets)
        {
            Tag(id); w.Write(id == "00db" ? 0x10 : 0x10); w.Write(offset); w.Write(size);
        }

        End(idx);
        End(riff);
        File.WriteAllBytes(_file, ms.ToArray());
    }

    private static int FrameNumber(Pardeh.Core.Playback.VideoFrame frame)
    {
        var i = (frame.Height / 2 * frame.Width + 2) * 4; // BGRA of a pixel near the left edge, mid height
        return frame.Pixels[i] + frame.Pixels[i + 1] / 40 * 256;
    }

    private static string Debug(NavaEngine engine)
    {
        var t = typeof(NavaEngine);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var reader = (Pardeh.Nava.IAudioSource?)t.GetField("_reader", flags)!.GetValue(engine);
        var stretcher = t.GetField("_stretcher", flags)!.GetValue(engine);
        var pending = stretcher?.GetType().GetProperty("PendingSourceSeconds")?.GetValue(stretcher);
        var output = t.GetField("_output", flags)!.GetValue(engine);
        var state = output?.GetType().GetProperty("PlaybackState")?.GetValue(output);
        string F(string n) => stretcher is null ? "-" : stretcher.GetType().GetField(n, flags)!.GetValue(stretcher)?.ToString() ?? "";
        return $"reader={reader?.CurrentTime.TotalSeconds:0.00} pending={pending} output={state} in={F("_inputFrames")} out={F("_outputFrames")} ended={F("_ended")} debt={F("_skipDebt")}";
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(2.0)]
    [InlineData(0.5)]
    public void The_picture_keeps_following_the_position_after_a_speed_change(double speed)
    {
        if (!Enabled)
            return;
        WriteAvi();
        using var engine = new NavaEngine { Muted = true };
        engine.Open(_file);
        engine.PlaybackFailed += (_, ex) => output.WriteLine("PLAYBACK FAILED: " + ex);
        engine.PlaybackEnded += (_, _) => output.WriteLine("PLAYBACK ENDED at " + engine.Position);
        Assert.True(engine.VideoSize.Width > 0, "the video opened");
        engine.Play();
        var log = new List<string>();
        var worst = 0.0;
        var stuck = 0;
        var lastSeq = -1L;
        var sinceChange = 0;
        for (var tick = 0; tick < 80; tick++)
        {
            if (tick == 30)
                engine.Rate = speed;
            Thread.Sleep(100);
            var position = engine.Position.TotalSeconds;
            var frame = engine.CurrentVideoFrame;
            var shown = frame is null ? -1 : FrameNumber(frame);
            var expected = (int)(position * Fps);
            var lag = Math.Abs(shown - expected) / (double)Fps;
            if (tick > 5 && tick % 5 == 0)
                log.Add($"t={tick / 10.0:0.0}s rate={engine.Rate:0.##} position={position:0.00}s frame={shown} expected~{expected} lag={lag:0.00}s seq={frame?.Sequence} {Debug(engine)}");
            if (tick > 35)
            {
                worst = Math.Max(worst, lag);
                sinceChange++;
                if (frame is not null && frame.Sequence == lastSeq)
                    stuck++;
            }

            lastSeq = frame?.Sequence ?? -1;
        }

        output.WriteLine(string.Join("\n", log));
        output.WriteLine($"worst lag after the change {worst:0.00}s, ticks with no new picture {stuck}/{sinceChange}");
        Assert.True(worst < 0.6, $"the picture lagged the position by {worst:0.00}s");
        Assert.True(stuck < sinceChange * (speed < 1 ? 0.7 : 0.3), $"the picture stood still in {stuck} of {sinceChange} samples");
    }
}
