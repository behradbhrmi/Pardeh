using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Pardeh.UI.Rendering;

/// <summary>
/// The Pardeh Glass Library backdrop (Collage "Library" phone): curved bright cyan-white light streaks and a big glossy translucent
/// 3D double note at the right, behind the rows. Vector, drawn at the device scale.
/// </summary>
internal sealed class GlassBackdrop : Control
{
    // 300 x 350: thick beam, two thick stems, two solid tilted oval heads (as in the Collage reference)
    private static readonly Geometry Note = new GeometryGroup
    {
        FillRule = FillRule.NonZero,
        Children =
        {
            Geometry.Parse("M 95,72 L 300,0 L 300,215 L 250,215 L 250,80 L 148,116 L 148,290 L 95,290 Z"),
            new EllipseGeometry(new Rect(-68, -42, 136, 84)) { Transform = new TransformGroup { Children = { new RotateTransform(-18), new TranslateTransform(76, 304) } } },
            new EllipseGeometry(new Rect(-62, -40, 124, 80)) { Transform = new TransformGroup { Children = { new RotateTransform(-18), new TranslateTransform(236, 248) } } },
        },
    };

    /// <summary>An ARGB colour of the backdrop through the skin colour setting (<see cref="GlassPalette"/>; white stays white).</summary>
    private static Color M(byte a, byte r, byte g, byte b) => GlassPalette.Map(Color.FromArgb(a, r, g, b));

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 40 || h < 40)
            return;

        // light streaks, fanning from the lower left towards the upper right
        (double X0, double Y0, double X1, double Y1, double X2, double Y2, double X3, double Y3, double Width, byte Alpha)[] streaks =
        [
            (0.20, 1.02, 0.45, 0.70, 0.72, 0.52, 1.06, 0.10, 3.5, 120),
            (0.30, 1.02, 0.52, 0.78, 0.80, 0.55, 1.06, 0.26, 1.6, 150),
            (0.42, 1.02, 0.60, 0.80, 0.84, 0.64, 1.06, 0.40, 5, 70),
            (0.10, 0.88, 0.40, 0.62, 0.70, 0.42, 1.06, 0.04, 1.4, 120),
            (0.55, 1.02, 0.70, 0.86, 0.90, 0.76, 1.06, 0.60, 2.4, 110),
            (0.00, 0.70, 0.30, 0.52, 0.60, 0.30, 1.00, 0.00, 22, 26),
            (0.25, 1.05, 0.60, 0.92, 0.85, 0.50, 1.06, 0.20, 30, 22),
        ];
        foreach (var s in streaks)
        {
            var figure = new PathFigure { StartPoint = new Point(s.X0 * w, s.Y0 * h), IsClosed = false };
            figure.Segments!.Add(new BezierSegment { Point1 = new Point(s.X1 * w, s.Y1 * h), Point2 = new Point(s.X2 * w, s.Y2 * h), Point3 = new Point(s.X3 * w, s.Y3 * h) });
            var geo = new PathGeometry();
            geo.Figures!.Add(figure);
            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(s.X0 * w, s.Y0 * h, RelativeUnit.Absolute), EndPoint = new RelativePoint(s.X3 * w, s.Y3 * h, RelativeUnit.Absolute),
                GradientStops = { new GradientStop(M(0, 190, 240, 255), 0), new GradientStop(M(s.Alpha, 200, 245, 255), 0.55), new GradientStop(M(0, 255, 255, 255), 1) },
            };
            context.DrawGeometry(null, new Pen(brush, s.Width, lineCap: PenLineCap.Round), geo);
        }

        // the note: a soft glow, the translucent glass body, a darker lower right, a lit upper left edge
        var noteW = Math.Min(w * 0.42, h * 0.46);
        var scale = noteW / 300;
        var noteH = 350 * scale;
        var origin = new Point(w - noteW - w * 0.05, h * 0.40 - noteH / 2);
        var center = new Point(origin.X + noteW / 2, origin.Y + noteH / 2);
        context.DrawEllipse(new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            GradientStops = { new GradientStop(M(120, 110, 210, 255), 0), new GradientStop(M(0, 110, 210, 255), 1) },
        }, null, center, noteW * 0.95, noteH * 0.8);

        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(origin.X, origin.Y)))
        {
            using (context.PushTransform(Matrix.CreateTranslation(6, 10)))
                context.DrawGeometry(new SolidColorBrush(M(55, 4, 30, 120)), null, Note); // soft drop shadow
            context.DrawGeometry(null, new Pen(new SolidColorBrush(M(28, 170, 225, 255)), 44, lineJoin: PenLineJoin.Round), Note); // outer glow
            context.DrawGeometry(null, new Pen(new SolidColorBrush(M(48, 170, 225, 255)), 22, lineJoin: PenLineJoin.Round), Note);
            // translucent glass body: light cyan-white at the top, clear blue below
            context.DrawGeometry(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0.2, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0.8, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(M(240, 214, 240, 255), 0), new GradientStop(M(225, 150, 208, 255), 0.5), new GradientStop(M(215, 96, 170, 248), 1) },
            }, null, Note);
            using (context.PushGeometryClip(Note))
            {
                // inner gradient: lighter at the top, a soft darker blue pooling at the bottom right
                context.DrawRectangle(new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(M(110, 255, 255, 255), 0), new GradientStop(M(0, 255, 255, 255), 0.55), new GradientStop(M(60, 30, 90, 220), 1) },
                }, null, new Rect(-20, -20, 340, 400));
                // lit top / left rim: a wide soft white edge that fades towards the lower right
                context.DrawGeometry(null, new Pen(new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(M(230, 255, 255, 255), 0), new GradientStop(M(70, 235, 250, 255), 0.55), new GradientStop(M(0, 235, 250, 255), 1) },
                }, 16, lineJoin: PenLineJoin.Round), Note);
                // faint darker rim at the lower right edges
                context.DrawGeometry(null, new Pen(new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(1, 1, RelativeUnit.Relative), EndPoint = new RelativePoint(0.2, 0.2, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(M(90, 30, 90, 215), 0), new GradientStop(M(0, 30, 90, 215), 0.6) },
                }, 8, lineJoin: PenLineJoin.Round), Note);
                // gloss on the heads
                context.DrawEllipse(new SolidColorBrush(M(70, 255, 255, 255)), null, new Point(66, 288), 46, 18);
                context.DrawEllipse(new SolidColorBrush(M(60, 255, 255, 255)), null, new Point(236, 232), 40, 16);
            }

            // crisp thin highlight line along the outline
            context.DrawGeometry(null, new Pen(new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(M(200, 255, 255, 255), 0), new GradientStop(M(60, 200, 235, 255), 0.6), new GradientStop(M(25, 200, 235, 255), 1) },
            }, 2, lineJoin: PenLineJoin.Round), Note);
        }
    }
}
