namespace Pardeh.UI.FullMode;

internal static partial class PardehLogoArt
{
    /// <summary>Colours of a glass sphere: body gradient, light, bounce light from below, a warm accent, and the glow around it.</summary>
    private sealed record Sphere(string Top, string Mid, string Bottom, string Edge, string Light, string Bounce, string Halo, string Ground, string TriGlow = "#B0A8E0FF");

    private static readonly Sphere Royal = new("#2F74F5", "#0F42CC", "#06197A", "#0A2A9C", "#8CC4FF", "#D93CE0FF", "#4A3CC0FF", "#AA38C8FF");

    /// <summary>Soft satin pleats hanging from a gathered point at the top: the curtain, only as a texture of light in the glass.</summary>
    private static LogoGroup Pleats(string clip)
    {
        static string Curve(double x) => $"C {F(50 + (x - 50) * 0.12)},34 {F(50 + (x - 50) * 0.9)},62 {F(x)},100";
        static string Back(double x) => $"C {F(50 + (x - 50) * 0.9)},62 {F(50 + (x - 50) * 0.12)},34 50,0";
        var shapes = new List<LogoShape>();
        for (var k = -3; k < 3; k++)
        {
            double x0 = 50 + 21 * k, x1 = 50 + 21 * (k + 1);
            var light = (k & 1) == 0;
            shapes.Add(new LogoShape($"M 50,0 {Curve(x0)} L {F(x1)},100 {Back(x1)} Z",
                light ? Lin(0, 10, 0, 96, (0, "#00FFFFFF"), (0.45, "#26BDE0FF"), (1, "#10BDE0FF")) : Lin(0, 10, 0, 96, (0, "#00000000"), (0.5, "#26001060"), (1, "#18001060"))));
        }

        return new LogoGroup(clip, shapes);
    }

    /// <summary>A gold line of light along the lower right edge of the glass (the footlight).</summary>
    private static List<LogoShape> GoldRimLight(double r)
    {
        const double A0 = 8 * Math.PI / 180, A1 = 108 * Math.PI / 180;
        var d = $"M {F(Cx + r * Math.Cos(A0))},{F(Cy + r * Math.Sin(A0))} A {F(r)},{F(r)} 0 0 1 {F(Cx + r * Math.Cos(A1))},{F(Cy + r * Math.Sin(A1))}";
        var grad = Lin(88, 60, 44, 92, (0, "#00FFC25A"), (0.5, "#FFFFD070"), (1, "#00FFC25A"));
        return [new LogoShape(d, null, Lin(88, 60, 44, 92, (0, "#00FFB040"), (0.5, "#59FFB040"), (1, "#00FFB040")), 5), new LogoShape(d, null, grad, 1.5)];
    }

    // Chrome: a band of white at the top, a dark horizon, and light bouncing back from below.
    private static LogoShape ChromeRing(double outer, double width) =>
        new(Circle(Cx, Cy, outer - width / 2), null,
            Lin(16, 6, 84, 92, (0, "#FFFFFF"), (0.16, "#B8CCEA"), (0.4, "#4A5C8C"), (0.5, "#141C44"), (0.58, "#4F70B4"), (0.8, "#CFE2FC"), (1, "#FFFFFF")), width);

    /// <summary>The glass sphere: ground glow, halo, chrome rim, deep glass body with light and bounce, the play triangle. <paramref name="extra"/> adds art above the triangle.</summary>
    private static List<LogoGroup> SphereGroups(Sphere p, Func<List<LogoGroup>>? extra = null)
    {
        const double Rim = 3.7;
        const double Rb = R - Rim;
        var body = Circle(Cx, Cy, Rb);
        var groups = new List<LogoGroup>
        {
            Glow(50, 93, 38, 1, 0.15, (0, p.Ground), (0.6, "#2E" + p.Ground[3..]), (1, "#00" + p.Ground[3..])),
            // Halo hugging the rim.
            new(null, [new LogoShape(Circle(Cx, Cy, 49.6), Rad(Cx, Cy, 49.6, (0, p.Halo), (0.88, p.Halo), (1, "#00" + p.Halo[3..])))]),
            // Chrome rim, with a hairline that holds it against a light background and a dark bevel between rim and glass.
            new(null,
            [
                new LogoShape(Circle(Cx, Cy, R - 0.3), null, Sol("#990A1850"), 0.7),
                ChromeRing(R, Rim),
                new LogoShape(Circle(Cx, Cy, Rb + 0.25), null, Sol("#B0050F3E"), 0.9),
            ]),
            // The glass: deep blue gradient, the light from the upper left, a darker edge (it rounds the sphere), light bounced from below.
            new(body,
            [
                new LogoShape(body, Lin(50, 8, 50, 88, (0, p.Top), (0.45, p.Mid), (0.82, p.Bottom), (1, p.Edge))),
                new LogoShape(Circle(34, 28, 38), Rad(34, 28, 38, (0, "#85" + p.Light[1..]), (0.5, "#33" + p.Light[1..]), (1, "#00" + p.Light[1..]))),
                new LogoShape(body, Rad(Cx, Cy, Rb, (0.5, "#00041050"), (0.85, "#4D041050"), (1, "#B3020A3C"))),
                new LogoShape(Circle(50, 94, 34), Rad(50, 94, 34, (0, p.Bounce), (0.5, "#55" + p.Bounce[3..]), (1, "#00" + p.Bounce[3..]))),
            ]),
            Pleats(body),
            new(body, GoldRimLight(Rb - 1.3)),
        };

        groups.AddRange(GlassTriangle(p.TriGlow));
        if (extra is not null)
            groups.AddRange(extra());

        groups.AddRange(GlassFinish(Rb));
        return groups;
    }

    /// <summary>The finishing light on any glass sphere: a valance-scalloped glassy cap (a nod to the curtain), a bright catch-light, an edge light.</summary>
    private static List<LogoGroup> GlassFinish(double rb)
    {
        var body = Circle(Cx, Cy, rb);
        // Cap: follows the glass edge, with a gently curved lower edge.
        const string Cap = "M 12.8,38 C 13.5,22 30,9.5 50,9.5 C 70,9.5 86.5,22 87.2,38 C 72,30 28,30 12.8,38 Z";
        return
        [
            new(body,
            [
                new LogoShape(Cap, Lin(50, 9, 50, 33, (0, "#EBFFFFFF"), (0.6, "#52FFFFFF"), (1, "#26FFFFFF"))),
                // The edge light: a bright line along the upper left, a cool one along the bottom.
                new LogoShape(Circle(Cx, Cy, rb - 0.5), null, Lin(30, 8, 70, 88, (0, "#F2FFFFFF"), (0.3, "#00FFFFFF"), (0.78, "#00A0E8FF"), (1, "#99A0E8FF")), 1.1),
            ]),
            new(null, [new LogoShape(Circle(31, 21, 9), Rad(31, 21, 9, (0, "#F2FFFFFF"), (1, "#00FFFFFF")))], 1.25, 0.62, 31, 21),
        ];
    }

    private static LogoPicture LauncherTile(string c1, string c2, string c3, string accent) => new(108,
    [
        new(null,
        [
            new LogoShape("M 0,0 L 108,0 L 108,108 L 0,108 Z", Rad(54, 40, 92, (0, c1), (0.5, c2), (1, c3))),
            new LogoShape("M -10,86 C 25,34 78,14 120,22 L 120,34 C 80,28 30,48 -10,100 Z", Lin(0, 20, 0, 90, (0, "#2EBFE4FF"), (1, "#00BFE4FF"))),
            new LogoShape("M -6,104 C 34,64 86,44 126,62", null, Sol(accent), 1.4),
        ]),
    ]);

    /// <summary>Ring and play triangle for the themed launcher layer (a faint inner disc and the scalloped cap give it some tone).</summary>
    private static LogoPicture OrbMono(bool tone)
    {
        const string Ink = "#FFFFFFFF";
        var shapes = new List<LogoShape>();
        if (tone)
        {
            shapes.Add(new LogoShape(Circle(Cx, Cy, R - 3), Sol("#33FFFFFF")));
            shapes.Add(new LogoShape("M 14,38 C 15,22 30,12 50,12 C 70,12 85,22 86,38 C 72,30 28,30 14,38 Z", Sol("#55FFFFFF")));
        }

        shapes.Add(new LogoShape(Circle(Cx, Cy, R - 2.4), null, Sol(Ink), tone ? 4.4 : 5.8));
        shapes.Add(new LogoShape(tone ? Triangle(0, 0, 4.5) : Rounded(3.4, (36, 31), (36, 73), (72, 52)), Sol(Ink), Sol(Ink), 1.2));
        return new LogoPicture(100, [new(null, shapes)]);
    }

    private static LogoSet BuildOrb() => new(
        new LogoPicture(100, SphereGroups(Royal)),
        OrbMono(true),
        OrbMono(false),
        LauncherTile("#2D5CD8", "#0F2A8A", "#050C38", "#26FFFFFF"));
}
