using System.Text;
using Avalonia;
using Avalonia.Media;
using static Pardeh.UI.FullMode.LunaArt;

namespace Pardeh.UI.FullMode;

/// <summary>
/// The "classic branding" logo of the Glass look and the Android shell: a glossy four-pane waving flag (red-orange, green,
/// blue, yellow), drawn in code as vector art (gradients, a gloss, a soft shadow) after the design the look is modelled on.
/// Switched by <see cref="AppSettings.ClassicBranding"/>. Path data is written with the invariant culture.
/// </summary>
internal static class ClassicFlag
{
    private static (double X, double Y) At(double u, double v) =>
        (10 + 80 * u - 7 * v + 1.5 * Math.Sin(u * 6.2), 18 + 66 * v + 9 * Math.Sin(u * 5.2 + 0.4) - 9 * u + 4 * v * Math.Sin(u * 3));

    private static Geometry Pane(double u0, double u1, double v0, double v1)
    {
        var sb = new StringBuilder();
        void P(double u, double v, bool first = false)
        {
            var (x, y) = At(u, v);
            sb.Append(first ? "M " : "L ").Append(FormattableString.Invariant($"{x:0.##},{y:0.##} "));
        }

        const int Steps = 10;
        for (var i = 0; i <= Steps; i++)
            P(u0 + (u1 - u0) * i / Steps, v0, i == 0);
        for (var i = 1; i <= 4; i++)
            P(u1, v0 + (v1 - v0) * i / 4);
        for (var i = 1; i <= Steps; i++)
            P(u1 - (u1 - u0) * i / Steps, v1);
        for (var i = 1; i < 4; i++)
            P(u0, v1 - (v1 - v0) * i / 4);
        return Geo(sb.Append('Z').ToString());
    }

    /// <summary>Draws the flag into <paramref name="r"/> (a square is best).</summary>
    public static void Draw(DrawingContext g, Rect r)
    {
        using (g.PushTransform(Matrix.CreateScale(r.Width / 100, r.Height / 100) * Matrix.CreateTranslation(r.X, r.Y)))
        {
            const double Gap = 0.035;
            var panes = new (double U0, double U1, double V0, double V1, string Top, string Bottom, string Rim)[]
            {
                (0, 0.5 - Gap, 0, 0.5 - Gap, "#FF8A3C", "#E8381E", "#9E2410"),
                (0.5 + Gap, 1, 0, 0.5 - Gap, "#A6E05A", "#35A12B", "#1E6F1C"),
                (0, 0.5 - Gap, 0.5 + Gap, 1, "#4FC0FF", "#0F62CF", "#0A3E92"),
                (0.5 + Gap, 1, 0.5 + Gap, 1, "#FFE75C", "#F5A700", "#B07400"),
            };
            g.DrawGeometry(new SolidColorBrush(Color.FromArgb(70, 0, 10, 50)), null, Geo("M 14,92 Q 50,100 90,86 L 90,92 Q 50,106 14,98 Z"));
            foreach (var (u0, u1, v0, v1, top, bottom, rim) in panes)
            {
                var geo = Pane(u0, u1, v0, v1);
                g.DrawGeometry(Vertical((Color.Parse(top), 0), (Color.Parse(bottom), 1)), Pen(Color.Parse(rim), 1.1), geo);
                var b = geo.Bounds;
                using (g.PushGeometryClip(geo))
                    g.DrawRectangle(Vertical((WithAlpha(Colors.White, 150), 0), (WithAlpha(Colors.White, 0), 1)), null, new Rect(b.X, b.Y, b.Width, b.Height * 0.5));
            }
        }
    }
}
