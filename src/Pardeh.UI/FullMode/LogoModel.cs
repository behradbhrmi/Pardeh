using Avalonia;
using Avalonia.Media;

namespace Pardeh.UI.FullMode;

/// <summary>
/// A vector picture made only of what an Android VectorDrawable can also express (filled / stroked paths, linear and circular
/// radial gradients with alpha, a path clip), so one definition renders in Avalonia at any size and exports to the Android
/// drawables, the .ico and the PNGs without drift. Coordinates are in a square user space (see <see cref="LogoPicture.Size"/>).
/// </summary>
internal abstract record LogoFill
{
    /// <summary>A colour as <c>#RRGGBB</c> or <c>#AARRGGBB</c>.</summary>
    public static LogoFill Solid(string color) => new SolidLogoFill(color);

    public static LogoFill Linear(double x1, double y1, double x2, double y2, params (double Offset, string Color)[] stops) => new LinearLogoFill(x1, y1, x2, y2, stops);

    public static LogoFill Radial(double cx, double cy, double radius, params (double Offset, string Color)[] stops) => new RadialLogoFill(cx, cy, radius, stops);

    public abstract IBrush ToBrush();
}

internal sealed record SolidLogoFill(string Color) : LogoFill
{
    public override IBrush ToBrush() => new SolidColorBrush(Avalonia.Media.Color.Parse(Color));
}

internal sealed record LinearLogoFill(double X1, double Y1, double X2, double Y2, (double Offset, string Color)[] Stops) : LogoFill
{
    public override IBrush ToBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(X1, Y1, RelativeUnit.Absolute),
            EndPoint = new RelativePoint(X2, Y2, RelativeUnit.Absolute),
        };
        foreach (var (offset, color) in Stops)
            brush.GradientStops.Add(new GradientStop(Avalonia.Media.Color.Parse(color), offset));
        return brush;
    }
}

internal sealed record RadialLogoFill(double Cx, double Cy, double Radius, (double Offset, string Color)[] Stops) : LogoFill
{
    public override IBrush ToBrush()
    {
        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(Cx, Cy, RelativeUnit.Absolute),
            GradientOrigin = new RelativePoint(Cx, Cy, RelativeUnit.Absolute),
            RadiusX = new RelativeScalar(Radius, RelativeUnit.Absolute),
            RadiusY = new RelativeScalar(Radius, RelativeUnit.Absolute),
        };
        foreach (var (offset, color) in Stops)
            brush.GradientStops.Add(new GradientStop(Avalonia.Media.Color.Parse(color), offset));
        return brush;
    }
}

/// <summary>One path with an optional fill and an optional stroke.</summary>
internal sealed record LogoShape(string Path, LogoFill? Fill = null, LogoFill? Stroke = null, double StrokeWidth = 0, bool EvenOdd = false, bool RoundCaps = true);

/// <summary>
/// Shapes drawn together, all clipped by <see cref="Clip"/> (a path) when it is set. A non-unit scale about a pivot squashes the
/// group (the only way to get an elliptical radial gradient, as in the soft ground shadow); do not combine it with a clip.
/// </summary>
internal sealed record LogoGroup(string? Clip, IReadOnlyList<LogoShape> Shapes, double ScaleX = 1, double ScaleY = 1, double PivotX = 0, double PivotY = 0);

/// <summary>A square vector picture: groups in drawing order. Geometry and brushes are parsed once and reused.</summary>
internal sealed class LogoPicture(double size, IReadOnlyList<LogoGroup> groups)
{
    private sealed record Prepared(Geometry Geometry, IBrush? Fill, IPen? Pen);

    private sealed record PreparedGroup(LogoGroup Group, Geometry? Clip, IReadOnlyList<Prepared> Shapes);

    private readonly Lazy<PreparedGroup[]> _prepared = new(() => Prepare(groups));

    public double Size { get; } = size;

    public IReadOnlyList<LogoGroup> Groups { get; } = groups;

    private static PreparedGroup[] Prepare(IReadOnlyList<LogoGroup> groups) =>
    [
        .. groups.Select(group => new PreparedGroup(
            group,
            group.Clip is { } clip ? Geometry.Parse(clip) : null,
            [.. group.Shapes.Select(shape => new Prepared(
                Geometry.Parse(shape.EvenOdd ? "F1 " + shape.Path : shape.Path),
                shape.Fill?.ToBrush(),
                shape.Stroke is { } stroke && shape.StrokeWidth > 0
                    ? new Pen(stroke.ToBrush(), shape.StrokeWidth, lineCap: shape.RoundCaps ? PenLineCap.Round : PenLineCap.Flat, lineJoin: PenLineJoin.Round)
                    : null))])),
    ];

    /// <summary>Draws the picture so its square fits (centred) in <paramref name="r"/>; vector, so crisp at any size or art density.</summary>
    public void Draw(DrawingContext g, Rect r)
    {
        var side = Math.Min(r.Width, r.Height);
        if (side <= 0)
            return;
        var scale = side / Size;
        var origin = new Point(r.X + (r.Width - side) / 2, r.Y + (r.Height - side) / 2);
        using (g.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(origin.X, origin.Y)))
        {
            foreach (var group in _prepared.Value)
            {
                if (group.Group.ScaleX != 1 || group.Group.ScaleY != 1)
                {
                    var l = group.Group;
                    using (g.PushTransform(Matrix.CreateTranslation(-l.PivotX, -l.PivotY) * Matrix.CreateScale(l.ScaleX, l.ScaleY) * Matrix.CreateTranslation(l.PivotX, l.PivotY)))
                        DrawGroup(g, group);
                }
                else
                {
                    DrawGroup(g, group);
                }
            }
        }
    }

    private static void DrawGroup(DrawingContext g, PreparedGroup group)
    {
        if (group.Clip is { } clip)
        {
            using (g.PushGeometryClip(clip))
                DrawShapes(g, group.Shapes);
        }
        else
        {
            DrawShapes(g, group.Shapes);
        }
    }

    private static void DrawShapes(DrawingContext g, IReadOnlyList<Prepared> shapes)
    {
        foreach (var shape in shapes)
            g.DrawGeometry(shape.Fill, shape.Pen, shape.Geometry);
    }
}
