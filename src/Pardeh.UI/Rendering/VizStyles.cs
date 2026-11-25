using System.Runtime.CompilerServices;
using Pardeh.Core.Playback;
using Pardeh.Skinning.Runtime;

namespace Pardeh.UI.Rendering;

/// <summary>The visualization styles of Pardeh Glass (original art drawn in code, tinted by the skin colour).</summary>
internal enum GlassVizStyle
{
    Bars,
    Mirror,
    Circular,
    Ribbon,
    Particles,
    Rings,
    Aurora,
    Led,
    BatteryRandomization,
    BatteryEventHorizon,
    BatteryCottonstar,
}

/// <summary>The style lists of the phone looks' visualization picker.</summary>
internal static class VizStyles
{
    /// <summary>The Glass styles in picker order: the setting's key, the name shown.</summary>
    public static readonly (string Key, string Name)[] Glass =
    [
        ("bars", "Segmented bars"),
        ("mirror", "Mirrored bars"),
        ("circular", "Circular spectrum"),
        ("ribbon", "Waveform ribbon"),
        ("particles", "Glass beads"),
        ("rings", "Pulse rings"),
        ("aurora", "Aurora"),
        ("led", "LED matrix"),
        ("battery-random", "Battery: Randomization"),
        ("battery-horizon", "Battery: Event Horizon"),
        ("battery-cotton", "Battery: Cottonstar"),
    ];

    /// <summary>Index into <see cref="Glass"/> of a saved key (the segmented bars when unknown).</summary>
    public static int GlassIndex(string? key)
    {
        var i = Array.FindIndex(Glass, s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
        return i < 0 ? 0 : i;
    }

    public static GlassVizStyle GlassStyle(int index) => (GlassVizStyle)Math.Clamp(index, 0, Glass.Length - 1);

    /// <summary>Index into <see cref="VisualizationCatalog.All"/> of a saved title ("Ambience : Water"); the player's default when empty or unknown.</summary>
    public static int ClassicIndex(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return VisualizationCatalog.Default;
        for (var i = 0; i < VisualizationCatalog.All.Count; i++)
        {
            if (string.Equals(VisualizationCatalog.All[i].Title, title, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return VisualizationCatalog.Default;
    }

    /// <summary>The automatic modes of the Settings entry: key, name.</summary>
    public static readonly (string Key, string Name)[] AutoModes =
    [
        ("off", "Keep my style"),
        ("track", "Change on every track"),
        ("cycle", "Auto-cycle every 15 seconds"),
    ];

    public static int AutoIndex(string? key)
    {
        var i = Array.FindIndex(AutoModes, m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));
        return i < 0 ? 0 : i;
    }
}

/// <summary>
/// The current style of the phone looks' visualization: the saved default (the picker, Settings), the style showing now (an automatic mode
/// moves it without touching the default) and the time of the last change (for the name that fades in over the picture). UI thread only; it
/// is only ticked while a visualization is drawn, so no timer runs while nothing shows (screen off, app in the background, another page).
/// </summary>
internal static class VizDirector
{
    public const double CycleSeconds = 15;

    private static readonly ConditionalWeakTable<MediaPlayer, object> Attached = new();
    private static readonly Random Random = new();
    private static volatile bool _trackChanged;
    private static double _cycle;
    private static int _glass, _classic;
    private static bool _loaded;

    /// <summary>Index into <see cref="VizStyles.Glass"/> showing now.</summary>
    public static int GlassCurrent
    {
        get { EnsureLoaded(); return _glass; }
    }

    /// <summary>Index into <see cref="VisualizationCatalog.All"/> showing now (phone looks other than Glass).</summary>
    public static int ClassicCurrent
    {
        get { EnsureLoaded(); return _classic; }
    }

    /// <summary>The name shown over the picture after a change, and when it stops being shown (<see cref="SpectrumFeed.Now"/>).</summary>
    public static string FlashText { get; private set; } = string.Empty;

    public static double FlashUntil { get; private set; }

    public static event Action? Changed;

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;
        _loaded = true;
        Reload();
    }

    /// <summary>Takes the saved default again (the settings page changed it).</summary>
    public static void Reload()
    {
        _loaded = true;
        _glass = VizStyles.GlassIndex(AppSettings.Current.GlassVizStyle);
        _classic = VizStyles.ClassicIndex(AppSettings.Current.PhoneVizPreset);
        _cycle = 0;
        Changed?.Invoke();
    }

    /// <summary>The picker: shows this style and remembers it as the default.</summary>
    public static void Pick(bool glass, int index, bool announce = true)
    {
        EnsureLoaded();
        var settings = AppSettings.Current;
        if (glass)
        {
            _glass = Math.Clamp(index, 0, VizStyles.Glass.Length - 1);
            settings.GlassVizStyle = VizStyles.Glass[_glass].Key;
        }
        else
        {
            _classic = VisualizationCatalog.Wrap(index);
            settings.PhoneVizPreset = VisualizationCatalog.All[_classic].Title;
        }

        _cycle = 0;
        if (announce)
            Flash(Name(glass));
        settings.Save();
        Changed?.Invoke();
    }

    /// <summary>A swipe on the picture: the next (+1) or previous (-1) style, remembered as the default.</summary>
    public static void Step(bool glass, int direction)
    {
        EnsureLoaded();
        var count = glass ? VizStyles.Glass.Length : VisualizationCatalog.All.Count;
        var current = glass ? _glass : _classic;
        Pick(glass, ((current + direction) % count + count) % count);
    }

    /// <summary>An automatic change (every track / every 15 s): another style at random; the saved default stays.</summary>
    public static void Advance(bool glass)
    {
        EnsureLoaded();
        var count = glass ? VizStyles.Glass.Length : VisualizationCatalog.All.Count;
        if (count < 2)
            return;
        var current = glass ? _glass : _classic;
        int next;
        do
        {
            next = Random.Next(count);
        }
        while (next == current);

        if (glass)
            _glass = next;
        else
            _classic = next;
        Flash(Name(glass));
        Changed?.Invoke();
    }

    public static string Name(bool glass) =>
        glass ? VizStyles.Glass[_glass].Name : VisualizationCatalog.All[_classic].Title.Replace(" : ", " - ");

    private static void Flash(string text)
    {
        FlashText = text;
        FlashUntil = SpectrumFeed.Now + 1.8;
    }

    /// <summary>Starts watching a player's track changes (once per player); the "every track" mode uses it.</summary>
    public static void Attach(MediaPlayer player)
    {
        if (Attached.TryGetValue(player, out _))
            return;
        Attached.Add(player, new object());
        player.CurrentMediaChanged += (_, _) => _trackChanged = true; // maybe off the UI thread: only a flag
    }

    /// <summary>Called by a visualization each time it draws: applies the automatic modes (the 15 s cycle counts playing time only).</summary>
    public static void Tick(double seconds, bool playing, bool glass)
    {
        EnsureLoaded();
        var mode = AppSettings.Current.VizAuto;
        var changed = _trackChanged;
        _trackChanged = false;
        if (mode == "track")
        {
            if (changed)
                Advance(glass);
        }
        else if (mode == "cycle" && playing)
        {
            _cycle += Math.Clamp(seconds, 0, 0.25);
            if (_cycle >= CycleSeconds)
            {
                _cycle = 0;
                Advance(glass);
            }
        }
    }
}
