using Pardeh.Core.Playback;
using Pardeh.Nava.Ffmpeg;

namespace Pardeh.Nava;

/// <summary>Reads a whole audio file through its own decoder and measures its loudness (for <see cref="LoudnessService"/>).</summary>
public static class NavaLoudness
{
    /// <summary>Longer files (audiobooks, whole concerts) are not measured: it would cost minutes of work and battery.</summary>
    public static readonly TimeSpan MaxLength = TimeSpan.FromMinutes(30);

    public static LoudnessResult? Measure(string source, CancellationToken cancel)
    {
        using var reader = Open(source);
        if (reader is null || reader.TotalTime > MaxLength)
            return null;
        var rate = reader.WaveFormat.SampleRate;
        var channels = reader.WaveFormat.Channels;
        var meter = new LoudnessMeter(rate, channels);
        var buffer = new float[16384 * channels];
        long frames = 0;
        var most = (long)(MaxLength.TotalSeconds * rate); // a file that does not know its own length still stops
        var block = 0;
        int read;
        while (frames < most && (read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancel.ThrowIfCancellationRequested();
            meter.Add(buffer.AsSpan(0, read));
            frames += read / channels;
            if (++block % 32 == 0)
                Thread.Sleep(1); // leave room for the playing track's decoder on slow phones
        }

        return frames > 0 ? meter.Result() : null;
    }

    private static IAudioSource? Open(string source)
    {
        if (FfmpegLibrary.IsAvailable)
        {
            try
            {
                return new FfmpegAudioSource(source);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Try the platform's decoder.
            }
        }

        try
        {
            return PlatformAudio.OpenFallbackAudio(source);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }
}
