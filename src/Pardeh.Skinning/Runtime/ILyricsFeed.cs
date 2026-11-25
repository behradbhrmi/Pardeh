using Pardeh.Core.LyricsKit;

namespace Pardeh.Skinning.Runtime;

/// <summary>
/// What a LYRICS element needs from the host: the lyrics of the playing track and a few actions (the app implements it over its
/// <c>LyricsController</c> and settings; <see cref="SkinSession.Lyrics"/>). Everything that needs a dialog (paste, import, going online)
/// goes through <c>ISkinHost.RunCommand("lyrics", [action])</c> instead.
/// </summary>
public interface ILyricsFeed
{
    /// <summary>The lyrics of the playing track, or null (none found, still loading, off).</summary>
    Lyrics? Current { get; }

    /// <summary>The line to highlight (-1: none yet, or plain lyrics).</summary>
    int CurrentIndex { get; }

    bool IsLoading { get; }

    /// <summary>True while a track is open.</summary>
    bool HasTrack { get; }

    /// <summary>The look-up is switched on in the settings.</summary>
    bool LyricsEnabled { get; }

    /// <summary>The online look-up (LRCLIB) is switched on in the settings.</summary>
    bool OnlineEnabled { get; }

    /// <summary>Text size factor, 0.8 to 1.6.</summary>
    double TextSize { get; }

    /// <summary>The setting "keep the current line in view".</summary>
    bool Follow { get; }

    /// <summary>Seeks the player to the start of a line (synchronised lyrics only).</summary>
    bool SeekToLine(int index);

    /// <summary>Moves the timing correction (milliseconds) and saves it for the track.</summary>
    void NudgeOffset(int deltaMs);

    void SetOffset(int offsetMs);

    /// <summary>Changes the text size factor by <paramref name="delta"/> (clamped) and saves it.</summary>
    void ChangeTextSize(double delta);

    /// <summary>The setting "show chords above the words".</summary>
    bool ShowChords => true;

    /// <summary>How transposed chords are spelled (a setting).</summary>
    ChordSpelling ChordSpelling => ChordSpelling.Auto;

    /// <summary>Moves the chords by semitones and saves that for the track.</summary>
    void NudgeTranspose(int semitones)
    {
    }

    /// <summary>Sets the transposition (0 = as written).</summary>
    void SetTranspose(int semitones)
    {
    }

    /// <summary>Auto, then sharps, then flats.</summary>
    void CycleChordSpelling()
    {
    }

    void ToggleShowChords()
    {
    }

    /// <summary>The setting "show the guitar tab instead of the words" (a track with only a tab always shows it).</summary>
    bool ShowTab => false;

    void ToggleShowTab()
    {
    }
}

/// <summary>An element that scrolls by whole pixels under a touch drag (the lyrics), instead of by rows.</summary>
public interface IPixelScrollable
{
    /// <summary>Scrolls the content by <paramref name="dy"/> pixels (positive: further down the text).</summary>
    void ScrollByPixels(double dy);
}
