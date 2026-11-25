using Pardeh.Core.LyricsKit;
using Pardeh.Nava;
using Pardeh.Skinning.Runtime;

namespace Pardeh.UI.LyricsKit;

/// <summary>The skins' view of the lyrics (<see cref="ILyricsFeed"/>): the <see cref="LyricsController"/> plus the lyrics settings.</summary>
internal sealed class LyricsFeed(LyricsController controller, AppSettings settings) : ILyricsFeed
{
    public LyricsController Controller => controller;

    public Lyrics? Current => controller.Current;

    public int CurrentIndex => controller.CurrentIndex;

    public bool IsLoading => controller.IsLoading;

    public bool HasTrack => controller.Item is not null;

    public bool LyricsEnabled => settings.LyricsEnabled;

    public bool OnlineEnabled => settings.LyricsOnline;

    public double TextSize => settings.LyricsTextSize;

    public bool Follow => settings.LyricsFollow;

    public bool ShowChords => settings.LyricsShowChords;

    public ChordSpelling ChordSpelling => Enum.TryParse<ChordSpelling>(settings.ChordSpelling, true, out var spelling) ? spelling : ChordSpelling.Auto;

    public void NudgeTranspose(int semitones) => controller.SetTranspose((controller.Current?.Transpose ?? 0) + semitones);

    public void SetTranspose(int semitones) => controller.SetTranspose(semitones);

    public void CycleChordSpelling()
    {
        settings.ChordSpelling = ((ChordSpelling)(((int)ChordSpelling + 1) % 3)).ToString();
        settings.Save();
    }

    public void ToggleShowChords()
    {
        settings.LyricsShowChords = !settings.LyricsShowChords;
        settings.Save();
    }

    public bool ShowTab => settings.LyricsShowTab;

    public void ToggleShowTab()
    {
        settings.LyricsShowTab = !settings.LyricsShowTab;
        settings.Save();
    }

    public bool SeekToLine(int index) => controller.SeekToLine(index);

    public void NudgeOffset(int deltaMs) => controller.NudgeOffset(deltaMs);

    public void SetOffset(int offsetMs) => controller.SetOffset(offsetMs);

    public void ChangeTextSize(double delta)
    {
        var next = Math.Round(Math.Clamp(settings.LyricsTextSize + delta, AppSettings.MinLyricsTextSize, AppSettings.MaxLyricsTextSize), 2);
        if (Math.Abs(next - settings.LyricsTextSize) < 0.001)
            return;
        settings.LyricsTextSize = next;
        settings.Save();
    }
}
