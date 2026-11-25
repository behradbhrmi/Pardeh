namespace Pardeh.UI.Rendering;

/// <summary>
/// The glossy pictorial icon of the lyrics (XP-era looks only): a karaoke microphone (silver grille ball on a navy handle, tilted) with a gold
/// music note in front. Picked from three candidates (upright studio mic, text page with a lit line and a note, tilted mic) because it
/// stays readable at 24 units on every skin background.
/// </summary>
internal static partial class PictorialIcons
{
    private static void Lyrics(Sheet s)
    {
        using (s.Rotated(35))
        {
            s.Obj(RR(40, 40, 20, 52, 9), Navy, 4);
            s.Obj(Ell(50, 26, 21, 21), Silver, 4);
            s.Line("M 33,22 L 67,22 M 31,30 L 69,30 M 50,6 L 50,46", "#6F8FC8", 2.6);
        }

        using (s.Placed(0.5, 0, 52))
            Note(s, Gold);
    }
}
