using System.Globalization;
using Pardeh.Core.Playback;
using Pardeh.Skinning.Runtime;

namespace Pardeh.UI;

/// <summary>Keeps the equalizer, the other audio enhancements and the video settings between runs.</summary>
internal static class AudioSettingsStore
{
    private const string Id = "playback";

    public static void Attach(MediaPlayer player, IPreferenceStore store)
    {
        var eq = player.Equalizer;
        var video = player.Video;
        string? Load(string key) => store.Load(Id, key);
        double Number(string key, double fallback) =>
            double.TryParse(Load(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        bool Flag(string key, bool fallback) => bool.TryParse(Load(key), out var value) ? value : fallback;

        if (Load("preset") is { } preset && int.TryParse(preset, out var index))
            eq.CurrentPreset = index;
        if (Load("gains") is { } gains)
        {
            var values = gains.Split(',');
            for (var band = 0; band < Math.Min(values.Length, EqualizerSettings.BandCount); band++)
            {
                if (double.TryParse(values[band], NumberStyles.Float, CultureInfo.InvariantCulture, out var gain))
                    eq.SetGain(band, gain);
            }
        }

        eq.Bypass = Flag("bypass", eq.Bypass);
        eq.CrossFade = Flag("crossFade", false);
        eq.CrossFadeSeconds = Number("crossFadeSeconds", eq.CrossFadeSeconds);
        eq.AutoVolume = Flag("autoVolume", false);
        eq.QuietMode = (int)Number("quietMode", 0);
        eq.Enhanced = Flag("enhanced", false);
        eq.WowLevel = Number("wow", eq.WowLevel);
        eq.TruBassLevel = Number("truBass", eq.TruBassLevel);
        eq.SpeakerSize = (int)Number("speakerSize", eq.SpeakerSize);
        video.Brightness = Number("brightness", 0);
        video.Contrast = Number("contrast", 0);
        video.Hue = Number("hue", 0);
        video.Saturation = Number("saturation", 0);

        string Text(double value) => value.ToString(CultureInfo.InvariantCulture);
        eq.Changed += (_, _) =>
        {
            store.Save(Id, "preset", eq.CurrentPreset.ToString(CultureInfo.InvariantCulture));
            store.Save(Id, "gains", string.Join(",", Enumerable.Range(0, EqualizerSettings.BandCount).Select(b => Text(eq.GetGain(b)))));
            store.Save(Id, "bypass", eq.Bypass.ToString());
            store.Save(Id, "crossFade", eq.CrossFade.ToString());
            store.Save(Id, "crossFadeSeconds", Text(eq.CrossFadeSeconds));
            store.Save(Id, "autoVolume", eq.AutoVolume.ToString());
            store.Save(Id, "quietMode", eq.QuietMode.ToString(CultureInfo.InvariantCulture));
            store.Save(Id, "enhanced", eq.Enhanced.ToString());
            store.Save(Id, "wow", Text(eq.WowLevel));
            store.Save(Id, "truBass", Text(eq.TruBassLevel));
            store.Save(Id, "speakerSize", eq.SpeakerSize.ToString(CultureInfo.InvariantCulture));
        };
        video.Changed += (_, _) =>
        {
            store.Save(Id, "brightness", Text(video.Brightness));
            store.Save(Id, "contrast", Text(video.Contrast));
            store.Save(Id, "hue", Text(video.Hue));
            store.Save(Id, "saturation", Text(video.Saturation));
        };
    }
}
