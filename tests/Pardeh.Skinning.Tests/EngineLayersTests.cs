using Pardeh.Engine;
using Pardeh.Ffmpeg;
using Pardeh.Nama;
using Pardeh.Nava;

namespace Pardeh.Skinning.Tests;

/// <summary>Nava is the sound and Nama the picture; only the engine knows both.</summary>
public sealed class EngineLayersTests
{
    private static string[] PardehReferences(Type anyTypeOfIt) =>
        [.. anyTypeOfIt.Assembly.GetReferencedAssemblies().Select(a => a.Name!).Where(n => n.StartsWith("Pardeh.", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

    [Fact]
    public void Sound_knows_nothing_about_pictures() =>
        Assert.Equal(["Pardeh.Core", "Pardeh.Ffmpeg"], PardehReferences(typeof(NavaPlayer)));

    [Fact]
    public void Pictures_know_nothing_about_the_sound_chain() =>
        Assert.Equal(["Pardeh.Core", "Pardeh.Ffmpeg"], PardehReferences(typeof(NamaPlayer)));

    [Fact]
    public void The_shared_FFmpeg_part_knows_neither() =>
        Assert.Equal(["Pardeh.Core"], PardehReferences(typeof(FfmpegLibrary)));

    [Fact]
    public void The_engine_puts_the_two_together()
    {
        var references = PardehReferences(typeof(MediaEngine));
        Assert.Contains("Pardeh.Nava", references);
        Assert.Contains("Pardeh.Nama", references);
    }
}
