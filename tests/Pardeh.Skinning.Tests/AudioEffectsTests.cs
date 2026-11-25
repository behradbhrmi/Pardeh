using Pardeh.Core.Playback;
using Pardeh.Nava;

namespace Pardeh.Skinning.Tests;

public class AudioEffectsTests
{
    private const int Rate = 44100;

    private static float[] Tone(double seconds, double amplitude)
    {
        var samples = new float[(int)(seconds * Rate) * 2];
        for (var i = 0; i < samples.Length / 2; i++)
            samples[2 * i] = samples[2 * i + 1] = (float)(amplitude * Math.Sin(2 * Math.PI * 440 * i / Rate));
        return samples;
    }

    private static double Rms(float[] samples, int from, int to)
    {
        double sum = 0;
        for (var i = from; i < to; i++)
            sum += samples[i] * samples[i];
        return Math.Sqrt(sum / (to - from));
    }

    [Fact]
    public void Auto_volume_leveling_lifts_a_quiet_track()
    {
        var effects = new AudioEffects(new EqualizerSettings { AutoVolume = true }, 2, Rate);
        var quiet = Tone(8, 0.02); // about -37 dBFS
        var before = Rms(quiet, quiet.Length - Rate, quiet.Length);
        effects.Process(quiet, 0, quiet.Length);
        Assert.True(Rms(quiet, quiet.Length - Rate, quiet.Length) > before * 2.5);
    }

    [Fact]
    public void Quiet_mode_narrows_the_gap_between_soft_and_loud()
    {
        var settings = new EqualizerSettings { QuietMode = 2 };
        var soft = Tone(1, 0.01);
        var loud = Tone(1, 0.9);
        new AudioEffects(settings, 2, Rate).Process(soft, 0, soft.Length);
        new AudioEffects(settings, 2, Rate).Process(loud, 0, loud.Length);
        var half = soft.Length / 2;
        var ratioAfter = Rms(loud, half, loud.Length) / Rms(soft, half, soft.Length);
        Assert.True(ratioAfter < 90 / 4.0, $"ratio {ratioAfter}"); // was 90:1
    }

    [Fact]
    public void Crossfade_gain_ramps_over_the_window()
    {
        var effects = new AudioEffects(new EqualizerSettings(), 2, Rate);
        effects.FadeTo(0, 0);
        effects.FadeTo(1, 1);
        var samples = Tone(0.5, 0.5);
        effects.Process(samples, 0, samples.Length);
        Assert.InRange(effects.Fade, 0.45, 0.55);
    }

    [Fact]
    public void Crossfade_window_is_milliseconds_in_the_skin_object_model()
    {
        var package = Pardeh.Skinning.Wmp.WmzLoader.FromFiles(new()
        {
            ["t.wms"] = System.Text.Encoding.UTF8.GetBytes("""<THEME><VIEW width="10" height="10"><EQUALIZERSETTINGS id="eq"/></VIEW></THEME>"""),
        }, "memory://t");
        var (session, player, _, _) = TestSkins.Start(package);
        session.CurrentView!.Script.Execute("eq.crossFade = true; eq.crossFadeWindow = 3000; eq.enhancedAudio = true; eq.wowLevel = 80;", "test");
        Assert.True(player.Equalizer.CrossFade);
        Assert.Equal(3, player.Equalizer.CrossFadeSeconds);
        Assert.True(player.Equalizer.Enhanced);
        Assert.Equal(80, player.Equalizer.WowLevel);
    }
}
