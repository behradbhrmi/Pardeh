namespace Pardeh.UI.Rendering;

/// <summary>The glossy pictorial icons of the Library sort / filter bar and the favourite heart (XP-era looks only).</summary>
internal static partial class PictorialIcons
{
    private const string HeartShape = "M 50,91 C 22,70 5,52 5,33 C 5,19 16,9 30,9 C 39,9 46,14 50,21 C 54,14 61,9 70,9 C 84,9 95,19 95,33 C 95,52 78,70 50,91 Z";

    /// <summary>A red glossy heart: a favourite.</summary>
    private static void Heart(Sheet s)
    {
        s.Obj(Geo(HeartShape), Red, 4.6);
    }

    /// <summary>A silver-blue glossy heart: not (yet) a favourite.</summary>
    private static void HeartOff(Sheet s)
    {
        s.Obj(Geo(HeartShape), Steel, 4.2);
    }

    /// <summary>Three bars of falling length and a blue arrow pointing down: sort.</summary>
    private static void Sort(Sheet s)
    {
        s.Obj(RR(5, 15, 58, 16, 5), Silver, 3.6);
        s.Obj(RR(5, 42, 42, 16, 5), Silver, 3.6);
        s.Obj(RR(5, 69, 26, 16, 5), Silver, 3.6);
        s.Obj(Geo("M 76,10 L 90,10 L 90,52 L 99,52 L 83,90 L 67,52 L 76,52 Z"), Blue, 3.8);
    }

    /// <summary>A funnel: filter.</summary>
    private static void Funnel(Sheet s)
    {
        s.Obj(Geo("M 6,12 Q 6,6 13,6 L 87,6 Q 94,6 94,12 Q 94,17 89,22 L 62,50 L 62,90 L 38,97 L 38,50 L 11,22 Q 6,17 6,12 Z"), Blue, 4.2);
    }
}
