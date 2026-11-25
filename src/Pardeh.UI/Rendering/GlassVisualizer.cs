using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Pardeh.UI.FullMode;

namespace Pardeh.UI.Rendering;

/// <summary>
/// The Pardeh Glass visualization styles other than the segmented bars (those stay in <see cref="VisualizationRenderer"/>): mirrored bars, a
/// circular spectrum around a glass orb, a waveform ribbon, glass beads, pulse rings, an aurora and an LED matrix. Original art, all vector
/// drawing from <see cref="SpectrumFeed"/> levels (no bitmaps), coloured through <see cref="GlassPalette"/> so the skin colour tints it.
/// One instance per visualization surface keeps the little state the moving styles need (beads, ripples, rotation, trails).
/// </summary>
internal sealed class GlassVisualizer
{
    private sealed class Bead
    {
        public double X, Y, Vx, Vy, Radius, Life, Shade;
    }

    private sealed class Ripple
    {
        public double Age, Strength;
    }

    private const int TrailCount = 5;

    private readonly Random _random = new(23);
    private readonly List<Bead> _beads = [];
    private readonly List<Ripple> _ripples = [];
    private readonly Queue<float[]> _trails = new();
    private double _time, _spin, _lastBass, _sinceRipple;
    private double _waveGain = 1;
    private long _lastUpdates = -1;

    /// <summary>1 = full detail; lower values (0.6, 0.35) thin out bars, beads, trails and points when frames take too long.</summary>
    public double Quality { get; set; } = 1;

    // ---- Colours (the skin colour is applied by GlassPalette.Parse / Map) ---------------------------

    private static Color Cyan => GlassPalette.Parse("#3FD8FF");
    private static Color Blue => GlassPalette.Parse("#1E9BFF");
    private static Color Pale => GlassPalette.Parse("#CFF4FF");
    private static Color Tip => GlassPalette.Parse("#A6EEFF");

    private static Color Alpha(Color c, double a) => Color.FromArgb((byte)Math.Clamp(a, 0, 255), c.R, c.G, c.B);

    private static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }

    private static IBrush Fill(Color c) => LiveBars.Fill(c);

    private static LinearGradientBrush Vertical(params (Color Color, double At)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
        foreach (var (color, at) in stops)
            brush.GradientStops.Add(new GradientStop(color, at));
        return brush;
    }

    private static RadialGradientBrush Radial(double cx, double cy, double radius, params (Color Color, double At)[] stops)
    {
        var brush = new RadialGradientBrush
        {
            Center = new RelativePoint(cx, cy, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(cx, cy, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(radius, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(radius, RelativeUnit.Relative),
        };
        foreach (var (color, at) in stops)
            brush.GradientStops.Add(new GradientStop(color, at));
        return brush;
    }

    /// <summary>The deep blue glass backdrop with a soft glow at <paramref name="glowY"/> (a fraction of the height).</summary>
    public static void Backdrop(DrawingContext context, Rect bounds, double glowY)
    {
        context.FillRectangle(Vertical((GlassPalette.Parse("#020B30"), 0), (GlassPalette.Parse("#05185A"), 0.7), (GlassPalette.Parse("#0A2A8C"), 1)), bounds);
        var glow = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, glowY, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.5, glowY, RelativeUnit.Relative),
            RadiusX = RelativeScalar.Parse("60%"), RadiusY = RelativeScalar.Parse("45%"),
            GradientStops = { new GradientStop(GlassPalette.Map(Color.FromArgb(110, 40, 140, 255)), 0), new GradientStop(GlassPalette.Map(Color.FromArgb(0, 40, 140, 255)), 1) },
        };
        context.FillRectangle(glow, bounds);
    }

    /// <summary>Draws one frame of <paramref name="style"/> (not <see cref="GlassVizStyle.Bars"/>) in <paramref name="bounds"/>.</summary>
    /// <param name="dt">Seconds since the last frame.</param>
    /// <param name="cover">The current cover as a disc (or null): the centre of the circular spectrum; the Pardeh logo otherwise.</param>
    public void Draw(DrawingContext context, Rect bounds, GlassVizStyle style, SpectrumFeed feed, double dt, IImage? cover = null)
    {
        // Nothing plays, every level has settled and no bead or ripple is left: time stops, so the picture is still (and costs nothing new).
        if (feed.Frozen && _beads.Count == 0 && _ripples.Count == 0)
            dt = 0;
        dt = Math.Clamp(dt, 0, 0.1);
        _time += dt;
        var (bass, _, _, _) = feed.Bands3();
        using (context.PushClip(bounds))
        {
            switch (style)
            {
                case GlassVizStyle.Mirror: Mirror(context, bounds, feed); break;
                case GlassVizStyle.Circular: Circular(context, bounds, feed, dt, bass, cover); break;
                case GlassVizStyle.Ribbon: Ribbon(context, bounds, feed); break;
                case GlassVizStyle.Particles: Particles(context, bounds, feed, dt, bass); break;
                case GlassVizStyle.Rings: Rings(context, bounds, feed, dt, bass); break;
                case GlassVizStyle.Aurora: Aurora(context, bounds, feed); break;
                case GlassVizStyle.Led: Led(context, bounds, feed); break;
                default: Mirror(context, bounds, feed); break;
            }
        }

        _lastBass = bass;
    }

    private static double Level(SpectrumFeed feed, int band) => SpectrumFeed.Contrast(feed.Levels[Math.Clamp(band, 0, SpectrumFeed.Bands - 1)]);

    private static double PeakLevel(SpectrumFeed feed, int band) => SpectrumFeed.Contrast(feed.Peaks[Math.Clamp(band, 0, SpectrumFeed.Bands - 1)]);

    // ---- 2. Mirrored bars ---------------------------------------------------------------------------

    private void Mirror(DrawingContext context, Rect b, SpectrumFeed feed)
    {
        Backdrop(context, b, 0.5);
        double w = b.Width, h = b.Height, cy = b.Y + h * 0.5;
        var n = Math.Max(10, (int)(36 * Quality) / 2 * 2);
        var slot = (w - 20) / n;
        var barW = Math.Max(2.5, slot * 0.64);
        var maxHalf = h * 0.42;
        var half = (n - 1) / 2.0;
        var up = Vertical((Tip, 0), (Cyan, 0.45), (Blue, 1));
        var down = Vertical((Blue, 0), (Cyan, 0.55), (Tip, 1)); // the same bar mirrored: brightest at its far end
        for (var k = 0; k < n; k++)
        {
            // Bass in the middle, treble at both edges: a butterfly.
            var d = Math.Abs(k - half) / half;
            var band = 1 + (int)(d * 40);
            var hh = Math.Max(2.5, Level(feed, band) * maxHalf);
            var x = b.X + 10 + k * slot + (slot - barW) / 2;
            var r = barW / 2;
            context.DrawRectangle(up, null, new RoundedRect(new Rect(x, cy - hh - 1.5, barW, hh), r));
            context.DrawRectangle(down, null, new RoundedRect(new Rect(x, cy + 1.5, barW, hh), r));
            var peak = Math.Max(hh, PeakLevel(feed, band) * maxHalf);
            if (peak > hh + 3)
            {
                context.FillRectangle(Fill(Alpha(Pale, 220)), new Rect(x, cy - peak - 5, barW, 2));
                context.FillRectangle(Fill(Alpha(Pale, 220)), new Rect(x, cy + peak + 3, barW, 2));
            }
        }

        // The centre line, lit.
        context.FillRectangle(Fill(Alpha(Pale, 170)), new Rect(b.X + 8, cy - 0.5, w - 16, 1));
        context.FillRectangle(Fill(Alpha(Cyan, 40)), new Rect(b.X + 8, cy - 3, w - 16, 6));
    }

    // ---- 3. Circular spectrum -----------------------------------------------------------------------

    private void Circular(DrawingContext context, Rect b, SpectrumFeed feed, double dt, double bass, IImage? cover)
    {
        Backdrop(context, b, 0.5);
        _spin += dt * 0.22; // slow rotation, only while frames are drawn (it freezes with the music)
        var c = b.Center;
        var radius = Math.Min(b.Width, b.Height) * 0.5;
        var orb = radius * 0.36 * (1 + bass * 0.05);
        var maxLen = radius * 0.5;
        var count = Math.Max(24, (int)(72 * Quality) / 2 * 2);

        // The halo behind the orb breathes with the bass.
        context.DrawEllipse(Radial(0.5, 0.5, 0.5, (Alpha(Cyan, 70 + bass * 120), 0), (Alpha(Cyan, 0), 1)), null, c, orb * 2.1, orb * 2.1);

        var thickness = Math.Max(2, radius * 0.028);
        for (var i = 0; i < count; i++)
        {
            // Mirrored left and right: the bass at the top and bottom, the treble at the sides.
            var m = i < count / 2 ? i : count - 1 - i;
            var band = 1 + m * 42 / (count / 2);
            var level = Level(feed, band);
            var angle = _spin + i * 2 * Math.PI / count - Math.PI / 2;
            var r0 = orb + radius * 0.05;
            var r1 = r0 + radius * 0.025 + level * maxLen;
            var (cos, sin) = (Math.Cos(angle), Math.Sin(angle));
            var color = level > 0.86 ? Color.Parse("#FF9A1F") : Mix(Blue, Tip, level);
            context.DrawLine(new Pen(Fill(color), thickness, lineCap: PenLineCap.Round),
                new Point(c.X + cos * r0, c.Y + sin * r0), new Point(c.X + cos * r1, c.Y + sin * r1));
        }

        // A thin ring around the bars.
        context.DrawEllipse(null, new Pen(Fill(Alpha(Pale, 60)), 1), c, radius * 0.97, radius * 0.97);

        // The glossy glass orb with the cover (or the logo) in it.
        context.DrawEllipse(Radial(0.5, 0.55, 0.5, (GlassPalette.Parse("#0F3A9F"), 0), (GlassPalette.Parse("#05185A"), 1)), new Pen(Fill(Alpha(GlassPalette.Parse("#9ED2FF"), 230)), Math.Max(1.5, orb * 0.05)), c, orb, orb);
        var inner = orb * 0.86;
        var innerBox = new Rect(c.X - inner, c.Y - inner, inner * 2, inner * 2);
        if (cover is not null)
        {
            using (context.PushGeometryClip(new EllipseGeometry(innerBox)))
                context.DrawImage(cover, new Rect(0, 0, cover.Size.Width, cover.Size.Height), innerBox);
        }
        else
        {
            // The logo (a picture, not a disc) sits on the dark glass, a little smaller than the glass so it does not touch the rim.
            Pardeh.UI.FullMode.PardehLogo.Draw(context, innerBox.Deflate(inner * 0.14));
        }

        // The sheen: a soft highlight over the upper left of the glass.
        context.DrawEllipse(Vertical((Color.FromArgb(120, 255, 255, 255), 0), (Color.FromArgb(0, 255, 255, 255), 1)), null,
            new Point(c.X - orb * 0.18, c.Y - orb * 0.52), orb * 0.66, orb * 0.36);
    }

    // ---- 4. Waveform ribbon -------------------------------------------------------------------------

    private void Ribbon(DrawingContext context, Rect b, SpectrumFeed feed)
    {
        Backdrop(context, b, 0.5);
        var points = Math.Max(32, (int)(96 * Quality));
        var wave = feed.Wave;
        // Auto gain: the waveform of quiet music is small; scale it so the ribbon always has presence.
        double peak = 0.05;
        for (var i = 0; i < wave.Length; i += 4)
            peak = Math.Max(peak, Math.Abs(wave[i]));
        _waveGain += (Math.Clamp(0.8 / peak, 1, 6) - _waveGain) * 0.08;

        var snapshot = new float[points];
        for (var i = 0; i < points; i++)
        {
            // A 4-sample mean smooths the line.
            var centre = i * (wave.Length - 8) / (points - 1) + 4;
            float sum = 0;
            for (var k = -2; k < 2; k++)
                sum += wave[centre + k];
            snapshot[i] = sum / 4;
        }

        // The trails: the last few shapes, fading; refreshed only when the audio really moved on.
        if (feed.Updates != _lastUpdates)
        {
            _lastUpdates = feed.Updates;
            if (feed.Playing || _trails.Count > 0)
                _trails.Enqueue(snapshot);
            var keep = Math.Max(2, (int)(TrailCount * Quality));
            while (_trails.Count > keep || (!feed.Playing && _trails.Count > 0))
                _trails.Dequeue();
        }

        double cy = b.Center.Y, amp = b.Height * 0.38 * _waveGain * 0.55, w = b.Width;
        var index = 0;
        var total = _trails.Count;
        foreach (var older in _trails)
        {
            index++;
            var fade = index / (double)(total + 1);
            DrawRibbon(context, b, older, cy, amp, w, Alpha(Cyan, 20 + 60 * fade), 1.2 + 1.2 * fade);
        }

        DrawRibbon(context, b, snapshot, cy, amp, w, Alpha(Cyan, 46), 11);
        DrawRibbon(context, b, snapshot, cy, amp, w, Alpha(Tip, 110), 4.5);
        DrawRibbon(context, b, snapshot, cy, amp, w, Pale, 2);
    }

    private static void DrawRibbon(DrawingContext context, Rect b, float[] samples, double cy, double amp, double width, Color color, double thickness)
    {
        var n = samples.Length;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            Point P(int i) => new(b.X + 6 + i * (width - 12) / (n - 1), cy - Math.Clamp(samples[i], -1f, 1f) * amp);
            var p0 = P(0);
            g.BeginFigure(p0, false);
            for (var i = 1; i < n - 1; i++)
            {
                var p1 = P(i);
                var p2 = P(i + 1);
                g.QuadraticBezierTo(p1, new Point((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2)); // smooth through the midpoints
            }

            g.LineTo(P(n - 1));
            g.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(Fill(color), thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
    }

    // ---- 5. Glass beads -----------------------------------------------------------------------------

    private void Particles(DrawingContext context, Rect b, SpectrumFeed feed, double dt, double bass)
    {
        Backdrop(context, b, 0.85);
        double w = b.Width, h = b.Height, floor = b.Bottom - h * 0.08;
        var cap = Math.Max(24, (int)(90 * Quality));
        var gravity = h * 3.4;

        if (feed.Playing)
        {
            // A steady trickle, and a burst on every bass hit.
            var rate = 4 + bass * 46;
            var spawn = (int)(rate * dt + _random.NextDouble());
            if (bass > 0.5 && bass - _lastBass > 0.07)
                spawn += 5 + _random.Next(5);
            for (var s = 0; s < spawn && _beads.Count < cap; s++)
            {
                var radius = Math.Max(3, h * (0.012 + _random.NextDouble() * 0.022));
                _beads.Add(new Bead
                {
                    X = b.X + w * (0.12 + _random.NextDouble() * 0.76),
                    Y = floor - radius,
                    Vx = (_random.NextDouble() - 0.5) * w * 0.35,
                    Vy = -h * (1.15 + bass * 0.9 + _random.NextDouble() * 0.5),
                    Radius = radius,
                    Life = 1,
                    Shade = _random.NextDouble(),
                });
            }
        }

        // A glossy bead: a pale highlight, a cyan body, a blue rim. One brush, drawn relative to each bead's own box.
        var bead = Radial(0.38, 0.32, 0.75, (Color.FromArgb(255, 245, 252, 255), 0), (Cyan, 0.38), (Blue, 0.85), (GlassPalette.Parse("#0A3FB8"), 1));
        for (var i = _beads.Count - 1; i >= 0; i--)
        {
            var p = _beads[i];
            p.Vy += gravity * dt;
            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;
            if (p.X < b.X + p.Radius || p.X > b.Right - p.Radius)
            {
                p.Vx = -p.Vx * 0.8;
                p.X = Math.Clamp(p.X, b.X + p.Radius, b.Right - p.Radius);
            }

            if (p.Y > floor - p.Radius)
            {
                p.Y = floor - p.Radius;
                p.Vy = -p.Vy * 0.5;
                p.Vx *= 0.9;
                if (Math.Abs(p.Vy) < h * 0.12)
                    p.Vy = 0;
            }

            // Beads rest on the floor for a while, then fade; when the music stops they all fade away.
            var resting = p.Vy == 0;
            p.Life -= dt * (feed.Playing ? (resting ? 0.35 : 0.04) : 0.9);
            if (p.Life <= 0)
            {
                _beads.RemoveAt(i);
                continue;
            }

            var box = new Rect(p.X - p.Radius, p.Y - p.Radius, p.Radius * 2, p.Radius * 2);
            if (p.Life < 1)
            {
                using (context.PushOpacity(Math.Clamp(p.Life * 1.6, 0, 1)))
                    context.DrawEllipse(bead, null, box.Center, p.Radius, p.Radius);
            }
            else
            {
                context.DrawEllipse(bead, null, box.Center, p.Radius, p.Radius);
            }

            // Reflection on the floor.
            var below = floor + (floor - p.Y) * 0.18 + 2;
            if (below < b.Bottom - 2)
                context.DrawEllipse(Fill(Alpha(Cyan, 30 * Math.Clamp(p.Life, 0, 1))), null, new Point(p.X, below), p.Radius * 0.9, p.Radius * 0.28);
        }

        context.FillRectangle(Fill(Alpha(GlassPalette.Map(Color.FromArgb(150, 120, 200, 255)), 150)), new Rect(b.X + 6, floor + 1, w - 12, 1));
    }

    // ---- 6. Pulse rings -----------------------------------------------------------------------------

    private void Rings(DrawingContext context, Rect b, SpectrumFeed feed, double dt, double bass)
    {
        Backdrop(context, b, 0.5);
        var c = b.Center;
        var radius = Math.Min(b.Width, b.Height) * 0.5;
        var maxR = Math.Max(b.Width, b.Height) * 0.62;

        _sinceRipple += dt;
        if (feed.Playing)
        {
            var beat = bass > 0.45 && bass - _lastBass > 0.06 && _sinceRipple > 0.18;
            if ((beat || _sinceRipple > 1.2) && _ripples.Count < Math.Max(4, (int)(9 * Quality)))
            {
                _ripples.Add(new Ripple { Strength = Math.Clamp(0.35 + bass, 0.3, 1) });
                _sinceRipple = 0;
            }
        }

        for (var i = _ripples.Count - 1; i >= 0; i--)
        {
            var ripple = _ripples[i];
            ripple.Age += dt;
            var life = ripple.Age / 2.4;
            if (life >= 1)
            {
                _ripples.RemoveAt(i);
                continue;
            }

            var r = radius * 0.16 + life * maxR;
            var alpha = Math.Pow(1 - life, 1.6) * 230 * ripple.Strength;
            var width = (2 + ripple.Strength * 5) * (1 - life * 0.6);
            context.DrawEllipse(null, new Pen(Fill(Alpha(Cyan, alpha * 0.35)), width * 2.6), c, r, r);
            context.DrawEllipse(null, new Pen(Fill(Alpha(Mix(Cyan, Pale, 1 - life), alpha)), width), c, r, r);
        }

        // The glass bead in the middle, swelling with the bass.
        var core = radius * 0.13 * (1 + bass * 0.55);
        context.DrawEllipse(Radial(0.5, 0.5, 0.5, (Alpha(Cyan, 150), 0), (Alpha(Cyan, 0), 1)), null, c, core * 3.2, core * 3.2);
        context.DrawEllipse(Radial(0.38, 0.32, 0.75, (Color.FromArgb(255, 245, 252, 255), 0), (Cyan, 0.4), (Blue, 1)), new Pen(Fill(Alpha(Pale, 200)), 1.2), c, core, core);
    }

    // ---- 7. Aurora ----------------------------------------------------------------------------------

    private static readonly (string Color, double Speed, double Freq)[] AuroraLayers =
    [
        ("#1E9BFF", 0.35, 2.2),
        ("#2FE0C0", 0.5, 3.1),
        ("#3FD8FF", 0.42, 1.6),
        ("#7A6BFF", 0.28, 2.7),
        ("#8FF0FF", 0.6, 3.6),
    ];

    private void Aurora(DrawingContext context, Rect b, SpectrumFeed feed)
    {
        Backdrop(context, b, 0.3);
        double w = b.Width, h = b.Height;
        var points = Math.Max(14, (int)(44 * Quality));
        var (bass, mid, treble, energy) = feed.Bands3();

        // A few stars that twinkle with the treble.
        for (var i = 0; i < 14; i++)
        {
            double sx = b.X + (i * 0.6180339 % 1) * w, sy = b.Y + (i * 0.7548776 % 1) * h * 0.55;
            var twinkle = 0.35 + 0.65 * Math.Abs(Math.Sin(_time * (0.7 + i * 0.13) + i));
            context.DrawEllipse(Fill(Alpha(Pale, 60 + 150 * twinkle * (0.4 + treble))), null, new Point(sx, sy), 0.9 + (i % 3) * 0.4, 0.9 + (i % 3) * 0.4);
        }

        var layers = Quality >= 0.99 ? AuroraLayers.Length : Quality >= 0.7 ? 4 : 3; // big translucent fills: the costly style, so fewer layers when frames are slow
        for (var j = 0; j < layers; j++)
        {
            var (hex, speed, freq) = AuroraLayers[j];
            var color = GlassPalette.Parse(hex);
            var baseY = b.Y + h * (0.30 + 0.09 * j);
            var swing = h * (0.045 + 0.07 * energy);
            var thickness = h * (0.30 + 0.06 * (j % 3));
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                Point Top(int i)
                {
                    var u = i / (double)(points - 1);
                    var band = 1 + (int)(u * 40);
                    var lift = Level(feed, band) * h * 0.22;
                    var y = baseY
                        + Math.Sin(u * freq * Math.PI * 2 + _time * speed * 2 + j * 1.7) * swing
                        + Math.Sin(u * freq * 1.9 * Math.PI * 2 - _time * speed * 1.3 + j) * swing * 0.5
                        - lift;
                    return new Point(b.X + u * w, y);
                }

                g.BeginFigure(Top(0), true);
                for (var i = 1; i < points; i++)
                    g.LineTo(Top(i));
                for (var i = points - 1; i >= 0; i--)
                {
                    var t = Top(i);
                    g.LineTo(new Point(t.X, t.Y + thickness));
                }

                g.EndFigure(true);
            }

            var strength = 55 + 95 * (j % 2 == 0 ? mid : bass) + 25 * energy;
            context.DrawGeometry(Vertical((Alpha(color, 0), 0), (Alpha(color, strength), 0.28), (Alpha(color, strength * 0.35), 0.62), (Alpha(color, 0), 1)), null, geometry);
        }
    }

    // ---- 8. LED matrix ------------------------------------------------------------------------------

    private void Led(DrawingContext context, Rect b, SpectrumFeed feed)
    {
        Backdrop(context, b, 0.6);
        var cols = Math.Max(10, (int)(26 * Quality));
        var rows = Math.Max(6, (int)(14 * Quality));
        double w = b.Width - 20, h = b.Height * 0.86;
        var cell = Math.Min(w / cols, h / rows);
        var dot = cell * 0.36;
        double x0 = b.X + (b.Width - cell * cols) / 2, y0 = b.Y + (b.Height - cell * rows) / 2;
        var off = Fill(Alpha(GlassPalette.Parse("#6FB5FF"), 30));
        for (var c = 0; c < cols; c++)
        {
            var band = 1 + c * 42 / cols;
            var lit = (int)Math.Round(Level(feed, band) * rows);
            var peak = (int)Math.Round(PeakLevel(feed, band) * rows);
            for (var r = 0; r < rows; r++)
            {
                var fromBottom = rows - 1 - r;
                var fraction = fromBottom / (double)(rows - 1);
                var on = fromBottom < lit;
                var isPeak = !on && fromBottom == peak - 1 && peak > lit;
                IBrush brush;
                if (on)
                {
                    var color = fraction > 0.84 ? Color.Parse("#FF4A2A") : fraction > 0.7 ? Color.Parse("#FF9A1F") : Mix(Cyan, Tip, fraction);
                    brush = Fill(color);
                }
                else
                {
                    brush = isPeak ? Fill(Alpha(Pale, 230)) : off;
                }

                context.DrawEllipse(brush, null, new Point(x0 + (c + 0.5) * cell, y0 + (r + 0.5) * cell), dot, dot);
            }
        }
    }
}
