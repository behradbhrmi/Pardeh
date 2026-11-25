namespace Pardeh.UI.FullMode;

internal static partial class PardehLogoArt
{
    // ---- Ring: a neon glass ring (cyan, through violet, to orange) around a deep blue lens -----------------------------------

    private static LogoSet BuildRing()
    {
        var disc = Circle(Cx, Cy, 39.2);
        var groups = new List<LogoGroup>
        {
            Glow(50, 93, 38, 1, 0.15, (0, "#AA38C8FF"), (0.6, "#2E38C8FF"), (1, "#0038C8FF")),
            // The glow of the ring: wide soft strokes, stacked.
            new(null,
            [
                new LogoShape(Circle(Cx, Cy, 41.3), null, Sol("#1A38C8FF"), 13),
                new LogoShape(Circle(Cx, Cy, 41.3), null, Sol("#2E38C8FF"), 9.5),
                new LogoShape(Circle(Cx, Cy, 41.3), null, Sol("#5050D0FF"), 7),
            ]),
            new(disc,
            [
                new LogoShape(disc, Rad(46, 38, 56, (0, "#1D5CE0"), (0.5, "#0A2A8E"), (1, "#020828"))),
                new LogoShape(Circle(50, 96, 36), Rad(50, 96, 36, (0, "#BB3CE0FF"), (1, "#003CE0FF"))),
                new LogoShape("M 8,42 C 10,18 30,8 50,8 C 70,8 90,18 92,42 C 74,31 26,31 8,42 Z", Lin(50, 8, 50, 30, (0, "#99FFFFFF"), (1, "#00FFFFFF"))),
            ]),
            Pleats(disc),
            new(null,
            [
                new LogoShape(Circle(Cx, Cy, 41.3), null, Lin(14, 10, 86, 88, (0, "#C8FAFF"), (0.35, "#30A8FF"), (0.7, "#7A6CFF"), (0.88, "#FF9A48"), (1, "#FF7418")), 5.6),
                new LogoShape(Circle(Cx, Cy, 43.9), null, Sol("#880A1850"), 0.6),
                new LogoShape(Circle(Cx, Cy, 41.3), null, Lin(30, 8, 70, 88, (0, "#F2FFFFFF"), (0.4, "#33FFFFFF"), (1, "#66FFFFFF")), 1.3),
            ]),
        };
        groups.InsertRange(5, GlassTriangle("#B0A8E8FF"));
        return new(new LogoPicture(100, groups), OrbMono(true), OrbMono(false), LauncherTile("#1B3FA8", "#0B1D66", "#030824", "#2638C8FF"));
    }

    // ---- Gel: the Luna-era glossy bead, bright aqua blue with a white gloss ---------------------------------------------------

    private static LogoSet BuildGel()
    {
        const double Rg = 43;
        var body = Circle(Cx, Cy, Rg);
        var groups = new List<LogoGroup>
        {
            Glow(50, 93, 38, 1, 0.15, (0, "#AA38C8FF"), (0.6, "#2E38C8FF"), (1, "#0038C8FF")),
            new(null, [new LogoShape(body, Lin(50, 5, 50, 91, (0, "#0A4ED8"), (0.5, "#1590F0"), (1, "#5CE8FF")), Sol("#CC0A2A8A"), 1.4)]),
            new(body,
            [
                new LogoShape(body, Rad(Cx, Cy, Rg, (0.6, "#00041050"), (1, "#66041050"))),
                new LogoShape(Circle(50, 90, 32), Rad(50, 90, 32, (0, "#F2FFFFFF"), (0.4, "#80C8F8FF"), (1, "#00C8F8FF"))),
            ]),
            Pleats(body),
            new(body, GoldRimLight(Rg - 1.3)),
            new(null, [new LogoShape(Circle(50, 28, 30), Lin(50, 0, 50, 56, (0, "#F7FFFFFF"), (1, "#26FFFFFF")))], 1.08, 0.74, 50, 28),
        };
        groups.AddRange(GlassTriangle("#80FFFFFF", gloss: false));
        groups.Add(new(null, [new LogoShape(Circle(Cx, Cy, Rg - 0.8), null, Lin(50, 5, 50, 91, (0, "#99FFFFFF"), (0.4, "#00FFFFFF"), (1, "#80FFFFFF")), 0.9)]));
        return new(new LogoPicture(100, groups), OrbMono(true), OrbMono(false), LauncherTile("#1E5CE0", "#0B2C9C", "#04104A", "#33FFFFFF"));
    }
}
