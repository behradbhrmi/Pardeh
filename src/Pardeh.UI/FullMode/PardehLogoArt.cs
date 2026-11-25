using System.Globalization;

namespace Pardeh.UI.FullMode;

/// <summary>
/// The named logo designs. Switch the app's logo with <see cref="PardehLogoArt.Default"/> and run scripts/export-logo-image.ps1
/// The first is a picture (a PNG master in src/Pardeh.UI/Assets, see <see cref="LogoImage"/>),
/// the rest are the earlier code-drawn vector designs, kept as alternatives.
/// </summary>
public enum LogoVariant
{
    /// <summary>Two glossy blue ribbon curtains (a pardeh) around a white play triangle on a navy tile (AI-generated art; the default).</summary>
    Curtain,

    /// <summary>A deep royal-blue glass sphere in a chrome rim, a glassy cap, a gold footlight, a bright glass play symbol (vector).</summary>
    Orb,

    /// <summary>A neon glass ring (cyan, through violet, to orange) around a dark blue lens with a glass play symbol.</summary>
    Ring,

    /// <summary>The Luna-era glossy gel bead: bright aqua blue, a white gloss, a white play symbol.</summary>
    Gel,
}

/// <summary>One logo design: the full-colour picture, the one-tone launcher layer, the status-bar silhouette and the launcher tile.</summary>
internal sealed record LogoSet(LogoPicture Full, LogoPicture Monochrome, LogoPicture Silhouette, LogoPicture LauncherBackground, double LauncherScale = 0.76);

/// <summary>
/// The artwork of Pardeh's logo. Everything is data (<see cref="LogoPicture"/>): <see cref="PardehLogo"/> draws it in
/// Avalonia at any size, and the test <c>LogoExport</c> writes the same shapes as the Android vector drawables, the .ico and PNGs.
/// Each design is a <see cref="LogoVariant"/> in its own partial file; <see cref="Default"/> is
/// the one the app ships (change it, run <c>scripts/export-logo-image.ps1</c>). The code-drawn designs below are the vector alternatives;
/// the shipped default is a picture.
/// </summary>
internal static partial class PardehLogoArt
{
    /// <summary>The logo the app uses. To switch: change this, then run scripts/export-logo-image.ps1 (regenerates Android drawables, .ico, PNG).</summary>
    public const LogoVariant Default = LogoVariant.Curtain;

    /// <summary>True when <paramref name="variant"/> is a picture (a PNG master), not one of the vector designs.</summary>
    public static bool IsImage(LogoVariant variant) => variant is LogoVariant.Curtain;

    /// <summary>
    /// The vector design behind the vector-only accessors below (<see cref="Full"/>, <see cref="Monochrome"/>, <see cref="Silhouette"/>,
    /// <see cref="LauncherBackground"/>): the default when it is a vector, otherwise the Orb, which also stands in if a picture cannot be decoded.
    /// </summary>
    public static LogoVariant VectorDesign => IsImage(Default) ? LogoVariant.Orb : Default;

    // The orb: centre (50, 48), radius 44.3 in a 100 x 100 space with room below for the ground glow. Every variant keeps to it, so the
    // launcher placement (orb diameter 67 dp inside the 66 dp safe zone) is shared.
    private const double Cx = 50, Cy = 48, R = 44.3;

    private static readonly Dictionary<LogoVariant, Lazy<LogoSet>> Sets = new()
    {
        [LogoVariant.Orb] = new(BuildOrb),
        [LogoVariant.Ring] = new(BuildRing),
        [LogoVariant.Gel] = new(BuildGel),
    };

    /// <summary>The pictures of one design.</summary>
    public static LogoSet For(LogoVariant variant) => Sets[variant].Value;

    /// <summary>The full-colour vector logo of <see cref="VectorDesign"/>.</summary>
    public static LogoPicture Full => For(VectorDesign).Full;

    /// <summary>One-tone vector version for the Android themed (monochrome) launcher icon (only the alpha channel matters there).</summary>
    public static LogoPicture Monochrome => For(VectorDesign).Monochrome;

    /// <summary>White vector silhouette (ring and play triangle) for a status-bar small icon (24 dp).</summary>
    public static LogoPicture Silhouette => For(VectorDesign).Silhouette;

    /// <summary>The vector launcher tile behind the logo (108 x 108 dp adaptive-icon background).</summary>
    public static LogoPicture LauncherBackground => For(VectorDesign).LauncherBackground;

    /// <summary>Where the vector logo sits inside the 108 dp adaptive-icon layers: scale and offset (diameter 67 dp, centred; the safe zone is 66 dp).</summary>
    public static double LauncherScale => For(VectorDesign).LauncherScale;

    public static double LauncherOffsetX => 54 - Cx * LauncherScale;

    public static double LauncherOffsetY => 53 - Cy * LauncherScale;

    // ---- geometry helpers ------------------------------------------------------------------------------------------

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Circle(double cx, double cy, double r) =>
        $"M {F(cx - r)},{F(cy)} A {F(r)},{F(r)} 0 1 1 {F(cx + r)},{F(cy)} A {F(r)},{F(r)} 0 1 1 {F(cx - r)},{F(cy)} Z";

    /// <summary>A polygon whose corners are rounded with quadratic curves.</summary>
    private static string Rounded(double radius, params (double X, double Y)[] p)
    {
        var sb = new System.Text.StringBuilder();
        var n = p.Length;
        for (var i = 0; i < n; i++)
        {
            var a = p[(i + n - 1) % n];
            var b = p[i];
            var c = p[(i + 1) % n];
            var (x1, y1) = Toward(b, a, radius);
            var (x2, y2) = Toward(b, c, radius);
            sb.Append(i == 0 ? "M " : "L ").Append(F(x1)).Append(',').Append(F(y1));
            sb.Append(" Q ").Append(F(b.X)).Append(',').Append(F(b.Y)).Append(' ').Append(F(x2)).Append(',').Append(F(y2)).Append(' ');
        }

        return sb.Append('Z').ToString();
    }

    private static (double X, double Y) Toward((double X, double Y) from, (double X, double Y) to, double radius)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        var t = Math.Min(radius, len / 2) / len;
        return (from.X + dx * t, from.Y + dy * t);
    }

    private static LogoFill Lin(double x1, double y1, double x2, double y2, params (double, string)[] stops) => LogoFill.Linear(x1, y1, x2, y2, stops);

    private static LogoFill Rad(double cx, double cy, double r, params (double, string)[] stops) => LogoFill.Radial(cx, cy, r, stops);

    private static LogoFill Sol(string color) => LogoFill.Solid(color);

    /// <summary>A soft ellipse of light: a circular radial gradient squashed about its centre (the only way to get an elliptical gradient).</summary>
    private static LogoGroup Glow(double cx, double cy, double r, double sx, double sy, params (double, string)[] stops) =>
        new(null, [new LogoShape(Circle(cx, cy, r), Rad(cx, cy, r, stops))], sx, sy, cx, cy);

    /// <summary>The play triangle (pointing right), its corners rounded; <paramref name="dx"/> / <paramref name="dy"/> move it (for a shadow).</summary>
    private static string Triangle(double dx = 0, double dy = 0, double radius = 4.2) =>
        Rounded(radius, (40 + dx, 35 + dy), (40 + dx, 67 + dy), (68 + dx, 51 + dy));

    /// <summary>Soft light from the top-left, a pearly blue-white play triangle, the focal point of most designs.</summary>
    private static List<LogoGroup> GlassTriangle(string glow, bool gloss = true)
    {
        var tri = Triangle();
        var groups = new List<LogoGroup>
        {
            new(null,
            [
                new LogoShape(Circle(52, 51, 25), Rad(52, 51, 25, (0, glow), (1, "#00FFFFFF"))),
                new LogoShape(Triangle(0.3, 1.5), null, Sol("#4D000C50"), 2.6),
                new LogoShape(tri, Lin(40, 35, 58, 70, (0, "#FFFFFF"), (0.55, "#D4EEFF"), (1, "#7CC8FF")), Sol("#B3FFFFFF"), 0.8),
            ]),
        };
        if (gloss)
        {
            groups.Add(new(tri,
            [
                // The classic glass split: a brighter upper half with a curved lower edge, and a deeper bounce along the bottom.
                new LogoShape("M 36,30 L 74,30 L 74,52 C 62,49 50,52 36,58 Z", Lin(40, 32, 40, 56, (0, "#FFFFFFFF"), (1, "#80FFFFFF"))),
                new LogoShape("M 36,70 L 74,70 L 74,60 C 60,62 50,66 36,66 Z", Lin(0, 60, 0, 70, (0, "#003A9CE8"), (1, "#663A9CE8"))),
            ]));
        }

        return groups;
    }
}
