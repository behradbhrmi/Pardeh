using Pardeh.Nava;
using Pardeh.Nava.Ffmpeg;

namespace Pardeh.Skinning.Tests;

/// <summary>Dubbed audio: separate audio files next to a video are tracks; switching keeps the position.</summary>
public sealed class AudioTrackTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("pardeh-dub-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static string? Media(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "media", name);
            if (File.Exists(candidate))
                return candidate;
        }

        return Environment.GetEnvironmentVariable("PARDEH_TEST_MEDIA") is { } folder && File.Exists(Path.Combine(folder, name)) ? Path.Combine(folder, name) : null;
    }

    [Fact]
    public void Sidecar_dub_is_listed_and_switching_keeps_the_position()
    {
        Assert.SkipWhen(!FfmpegLibrary.IsAvailable, "FFmpeg not available");
        var video = Media("clip3s.mp4");
        var tone = Media("Pardeh Test Tone A.wav");
        Assert.SkipWhen(video is null || tone is null, "test media not available (PARDEH_TEST_MEDIA)");

        var movie = Path.Combine(_folder, "movie.mp4");
        File.Copy(video!, movie);
        File.Copy(tone!, Path.Combine(_folder, "movie.fa.wav"));

        using var engine = new NavaEngine { Muted = true };
        engine.Open(movie);
        Assert.Contains(engine.AudioTracks, t => t.Label == "File: movie.fa.wav");

        engine.Play();
        engine.Position = TimeSpan.FromSeconds(1.5);
        var dub = engine.AudioTracks.ToList().FindIndex(t => t.Label == "File: movie.fa.wav");
        engine.SelectAudioTrack(dub);
        Assert.Equal(dub, engine.CurrentAudioTrack);
        Assert.InRange(engine.Position.TotalSeconds, 1.3, 2.2);

        var other = Path.Combine(_folder, "other.wav");
        File.Copy(tone!, other);
        Assert.True(engine.AddAudioFile(other));
        Assert.Equal("File: other.wav", engine.AudioTracks[engine.CurrentAudioTrack].Label);
    }
}
