using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Pardeh.Core.Playback;
using Pardeh.Skinning.Runtime;

namespace Pardeh.UI.Rendering;

/// <summary>
/// Pardeh's own drawing for the classic visualization families (Ambience, Battery, Bars and Waves …): vector
/// drawing for bars, scopes and spikes; a small feedback frame buffer (zoom, rotate, fade, mirror) for the
/// flowing ones, scaled up smoothly. One instance per visualization surface.
/// </summary>
internal sealed class VisualizationRenderer : IDisposable
{
    private const int Bands = 48;
    private const int WaveLength = 512;

    private readonly float[] _spectrum = new float[Bands];
    private readonly float[] _levels = new float[Bands];
    private readonly float[] _peaks = new float[Bands];
    private readonly float[] _wave = new float[WaveLength];
    private readonly Random _random = new(7);
    private readonly List<Particle> _particles = [];

    private int _w, _h;
    private byte[] _frame = [];
    private byte[] _back = [];
    private WriteableBitmap? _bitmap;
    private double _time;
    private int _lastIndex = -1;
    private int _alchemyPick = -1;
    private double _alchemyUntil;

    private sealed class Particle
    {
        public double X, Y, Vx, Vy, Life;
        public (byte R, byte G, byte B) Color;
    }

    public void Dispose() => _bitmap?.Dispose();

    /// <summary>Opt-in (the glass phone look): the glass styles (segmented cyan / blue bars with a reflection by default) on deep blue, whatever the preset.</summary>
    public bool Glass { get; set; }

    /// <summary>The Glass look's style (see <see cref="VizStyles.Glass"/>); the segmented bars by default.</summary>
    public GlassVizStyle GlassStyle { get; set; }

    /// <summary>The current cover as a disc, for the circular style (null: the logo).</summary>
    public IImage? Cover { get; set; }

    private readonly GlassVisualizer _glass = new();

    // ---- Frame time governor and diagnostics ----------------------------------------------------

    private static readonly int[] BufferWidths = [320, 240, 160];
    private static readonly double[] Qualities = [1, 0.75, 0.5];
    private int _tier;
    private double _costMs, _frameMs, _goodSeconds, _badSeconds, _logSeconds;

    /// <summary>Receives a periodic "fps / ms / quality" line while a visualization draws (debug builds, or PARDEH_DIAG=1).</summary>
    public static Action<string>? Diagnostics { get; set; }

    /// <summary>The quality tier now (0 = full, 2 = lowest): lowered when frames take longer than 50 ms, raised again when there is room.</summary>
    public int QualityTier => _tier;

    /// <summary>Draws one frame of visualization <paramref name="index"/> in <paramref name="bounds"/>. False when idle.</summary>
    public bool Render(DrawingContext context, Rect bounds, int index, MediaPlayer? player, double elapsedSeconds)
    {
        if (Preview)
        {
            _glass.Quality = 0.5;
            return RenderFrame(context, bounds, index, player, elapsedSeconds); // a thumbnail: always the cheap settings, no governor
        }

        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var result = RenderFrame(context, bounds, index, player, elapsedSeconds);
        Govern((System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency, elapsedSeconds);
        return result;
    }

    private void Govern(double costMs, double elapsed)
    {
        if (elapsed is <= 0 or > 0.25)
            return; // the first frame, or a long pause (hidden): not a measure of the load
        _costMs += (costMs - _costMs) * 0.1;
        _frameMs += (elapsed * 1000 - _frameMs) * 0.1;
        _logSeconds += elapsed;
        // Frames slower than 50 ms (under 20 fps) for a second, or the drawing alone eating the budget: a lower tier. Frames under 40 ms
        // and cheap drawing for four seconds: a higher one.
        if (_frameMs > 50 || _costMs > 20)
        {
            _badSeconds += elapsed;
            _goodSeconds = 0;
        }
        else if (_frameMs < 40 && _costMs < 8)
        {
            _goodSeconds += elapsed;
            _badSeconds = 0;
        }
        else
        {
            _goodSeconds = _badSeconds = 0;
        }

        if (_badSeconds > 1 && _tier < Qualities.Length - 1)
        {
            _tier++;
            _badSeconds = 0;
            ApplyTier();
        }
        else if (_goodSeconds > 4 && _tier > 0)
        {
            _tier--;
            _goodSeconds = 0;
            ApplyTier();
        }

        if (_logSeconds >= 2 && Diagnostics is { } log)
        {
            log(FormattableString.Invariant($"viz {(Glass ? GlassStyle.ToString() : "classic")}: {1000 / Math.Max(1, _frameMs):0} fps, draw {_costMs:0.0} ms, quality tier {_tier}"));
            _logSeconds = 0;
        }
    }

    private void ApplyTier()
    {
        _glass.Quality = Qualities[_tier];
        if (_w != 0)
            _w = 0; // the frame buffer is made again at the tier's width
    }

    /// <summary>A small thumbnail (the picker): a 96 px wide frame buffer and thinned-out vector styles, no frame time governor.</summary>
    public bool Preview { get; set; }

    /// <summary>A clock in seconds for the audio feed (tests); the shared real clock when null.</summary>
    public Func<double>? Clock { get; set; }

    /// <summary>A synthetic audio source (the picker's thumbnails, tests) used instead of the player's.</summary>
    public ISpectrumTap? Source { get; set; }

    private SpectrumFeed? _ownFeed;
    private ISpectrumTap? _ownFeedSource;

    private SpectrumFeed FeedFor(MediaPlayer? player)
    {
        if (Source is null)
            return SpectrumFeed.For(player ?? throw new InvalidOperationException("A visualization needs a player or a Source."));
        if (!ReferenceEquals(_ownFeedSource, Source))
        {
            _ownFeedSource = Source;
            _ownFeed = SpectrumFeed.Over(Source);
        }

        return _ownFeed!;
    }

    private bool RenderFrame(DrawingContext context, Rect bounds, int index, MediaPlayer? player, double elapsedSeconds)
    {
        // The old-school Battery presets (the original player's own family) are offered by the Glass picker too: they use the classic drawing.
        if (Glass && GlassStyle >= GlassVizStyle.BatteryRandomization)
        {
            var title = GlassStyle switch
            {
                GlassVizStyle.BatteryRandomization => "Battery : Randomization",
                GlassVizStyle.BatteryEventHorizon => "Battery : Event Horizon",
                _ => "Battery : Cottonstar",
            };
            index = Math.Max(0, VisualizationCatalog.All.ToList().FindIndex(v => v.Title == title));
        }
        else if (Glass)
        {
            var feed = FeedFor(player);
            feed.Update(Clock?.Invoke() ?? SpectrumFeed.Now, wantWave: GlassStyle == GlassVizStyle.Ribbon);
            if (GlassStyle == GlassVizStyle.Bars)
                return RenderGlass(context, bounds, feed);
            _glass.Draw(context, bounds, GlassStyle, feed, elapsedSeconds, Cover);
            return !feed.Frozen;
        }

        context.FillRectangle(Brushes.Black, bounds);
        var playing = Source is { } tap ? tap.GetSpectrum(_spectrum) : player!.GetSpectrum(_spectrum);
        if (Source is { } tap2)
            tap2.GetWaveform(_wave);
        else
            player!.GetWaveform(_wave);
        var anything = false;
        for (var i = 0; i < Bands; i++)
        {
            var target = playing ? _spectrum[i] : 0f;
            _levels[i] = target > _levels[i] ? target : Math.Max(0, _levels[i] - 0.03f);
            _peaks[i] = _levels[i] > _peaks[i] ? _levels[i] : Math.Max(0, _peaks[i] - 0.008f);
            anything |= _levels[i] > 0.001f || _peaks[i] > 0.001f;
        }

        if (!playing && !anything)
        {
            Clear();
            return false; // idle: black, like the original
        }

        _time += Math.Clamp(elapsedSeconds, 0, 0.1);
        var viz = VisualizationCatalog.All[VisualizationCatalog.Wrap(index)];
        if (viz.Type == "Alchemy")
        {
            // Alchemy: another visualization every 15 seconds.
            if (_alchemyPick < 0 || _time > _alchemyUntil)
            {
                var others = VisualizationCatalog.All.Where(v => v.Type != "Alchemy").ToList();
                _alchemyPick = VisualizationCatalog.All.ToList().IndexOf(others[_random.Next(others.Count)]);
                _alchemyUntil = _time + 15;
                Clear();
            }

            viz = VisualizationCatalog.All[_alchemyPick];
        }

        if (index != _lastIndex)
        {
            _lastIndex = index;
            Clear();
        }

        switch (viz.Title)
        {
            case "Bars and Waves : Bars": DrawBars(context, bounds); return true;
            case "Bars and Waves : Scope": DrawScope(context, bounds); return true;
            case "Spikes : Bursts": DrawSpikes(context, bounds); return true;
        }

        EnsureBuffer(bounds);
        switch (viz.Title)
        {
            case "Ambience : Water": Water(); break;
            case "Ambience : Swirl": Swirl(); break;
            case "Ambience : Warp": Warp(); break;
            case "Bars and Waves : Ocean Mist": OceanMist(); break;
            case "Bars and Waves : Fire Storm": FireStorm(); break;
            case "Battery : Randomization": Randomization(); break;
            case "Battery : Event Horizon": EventHorizon(); break;
            case "Battery : Cottonstar": Cottonstar(); break;
            case "Musical Colors : Blend": MusicalColors(); break;
            case "Particle : Fountain": Fountain(); break;
            default: Rings(); break;
        }

        Present(context, bounds);
        return true;
    }

    // ---- Glass bars (opt-in) --------------------------------------------------------------------

    private bool RenderGlass(DrawingContext context, Rect bounds, SpectrumFeed feed)
    {
        // The shared live levels (one spectrum read per ~33 ms for every consumer), smoothed over time; the bars freeze when it all settles.
        var playing = feed.Playing;
        var anything = !feed.Frozen;
        feed.Levels.CopyTo(_levels, 0);
        feed.Peaks.CopyTo(_peaks, 0);

        double w = bounds.Width, h = bounds.Height;
        var back = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(GlassPalette.Parse("#020B30"), 0), new GradientStop(GlassPalette.Parse("#05185A"), 0.7), new GradientStop(GlassPalette.Parse("#0A2A8C"), 1) },
        };
        context.FillRectangle(back, bounds);
        // The skin colour (GlassPalette) re-colours the blues; the warm top segments stay amber / orange-red.
        Color peakColor = GlassPalette.Parse("#CFF4FF"), tipColor = GlassPalette.Parse("#A6EEFF"), lowColor = GlassPalette.Parse("#1E9BFF"), highColor = GlassPalette.Parse("#3FD8FF");
        var glow = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.74, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.5, 0.74, RelativeUnit.Relative), RadiusX = RelativeScalar.Parse("60%"), RadiusY = RelativeScalar.Parse("45%"),
            GradientStops = { new GradientStop(GlassPalette.Map(Color.FromArgb(120, 40, 140, 255)), 0), new GradientStop(GlassPalette.Map(Color.FromArgb(0, 40, 140, 255)), 1) },
        };
        context.FillRectangle(glow, bounds);

        // A soft glowing floor under the bars.
        context.DrawEllipse(new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), RadiusX = RelativeScalar.Parse("50%"), RadiusY = RelativeScalar.Parse("50%"),
            GradientStops = { new GradientStop(GlassPalette.Map(Color.FromArgb(150, 60, 170, 255)), 0), new GradientStop(GlassPalette.Map(Color.FromArgb(0, 60, 170, 255)), 1) },
        }, null, new Point(bounds.X + w / 2, h * 0.74 + bounds.Y + 4), w * 0.52, h * 0.13);

        const int Count = 38;
        const double Pitch = 4.6, Seg = 3.2;
        var baseline = bounds.Y + h * 0.74;
        var maxBar = h * 0.66;
        var slot = (w - 16) / Count;
        var barW = Math.Max(2, slot * 0.7);
        for (var k = 0; k < Count; k++)
        {
            float level = 0, peak = 0;
            for (var b = k * Bands / Count; b < (k + 1) * Bands / Count; b++)
            {
                level = Math.Max(level, _levels[b]);
                peak = Math.Max(peak, _peaks[b]);
            }

            var x = bounds.X + 8 + k * slot + (slot - barW) / 2;
            var lit = (int)Math.Round(level * maxBar / Pitch);
            var peakSeg = (int)Math.Round(peak * maxBar / Pitch);
            for (var s = 0; s <= Math.Max(lit, peakSeg); s++)
            {
                if (s >= lit && s != peakSeg)
                    continue;
                var fraction = Math.Min(1, s * Pitch / maxBar);
                var color = s == peakSeg && s >= lit ? peakColor : s == lit - 1 && lit > 0 ? tipColor : Mix(lowColor, highColor, fraction);
                // The tallest bars end in warm segments: amber, then orange-red.
                if (fraction > 0.84)
                    color = Color.Parse("#FF4A2A");
                else if (fraction > 0.7)
                    color = Color.Parse("#FF9A1F");
                var y = baseline - (s + 1) * Pitch + (Pitch - Seg);
                context.FillRectangle(new SolidColorBrush(color), new Rect(x, y, barW, Seg));
                // The reflection: the same segment mirrored below the baseline, fading out.
                var ry = baseline + 3 + s * Pitch;
                var fade = 1 - s * Pitch / (maxBar * 0.5);
                if (fade > 0 && ry < bounds.Bottom - 1)
                    context.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(90 * fade), color.R, color.G, color.B)), new Rect(x, ry, barW, Seg));
            }
        }

        context.FillRectangle(new SolidColorBrush(GlassPalette.Map(Color.FromArgb(150, 120, 200, 255))), new Rect(bounds.X + 6, baseline + 1, w - 12, 1));
        return playing || anything;
    }

    private static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    // ---- Audio helpers ------------------------------------------------------------------------

    private double Bass => (_levels[0] + _levels[1] + _levels[2] + _levels[3]) / 4;

    private double Mid => _levels.Skip(12).Take(16).Average();

    private double Treble => _levels.Skip(32).Average();

    private double Energy => _levels.Average();

    private static (byte R, byte G, byte B) Hue(double hue, double saturation = 1, double value = 1)
    {
        hue = (hue % 360 + 360) % 360 / 60;
        var c = value * saturation;
        var x = c * (1 - Math.Abs(hue % 2 - 1));
        var m = value - c;
        var (r, g, b) = (int)hue switch
        {
            0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x), 3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x),
        };
        return ((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    // ---- Vector visualizations ----------------------------------------------------------------

    private void DrawBars(DrawingContext context, Rect bounds)
    {
        double w = bounds.Width, h = bounds.Height;
        var barWidth = w / Bands;
        for (var i = 0; i < Bands; i++)
        {
            var x = bounds.X + Math.Floor(i * barWidth) + 1;
            var bw = Math.Max(1, Math.Floor(barWidth) - 2);
            var bh = Math.Round(_levels[i] * (h - 8));
            var color = Color.FromRgb((byte)(80 + 175 * _levels[i]), 230, (byte)(40 * (1 - _levels[i])));
            context.FillRectangle(new SolidColorBrush(color), new Rect(x, bounds.Bottom - bh, bw, bh));
            if (_peaks[i] > 0.01f)
                context.FillRectangle(Brushes.White, new Rect(x, bounds.Bottom - Math.Round(_peaks[i] * (h - 8)) - 3, bw, 2));
        }
    }

    private void DrawScope(DrawingContext context, Rect bounds)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            for (var i = 0; i < WaveLength; i += 2)
            {
                var p = new Point(bounds.X + i * bounds.Width / (WaveLength - 1), bounds.Center.Y - _wave[i] * bounds.Height * 0.45);
                if (i == 0)
                    g.BeginFigure(p, false);
                else
                    g.LineTo(p);
            }

            g.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromRgb(40, 255, 60)), 2), geometry);
    }

    private void DrawSpikes(DrawingContext context, Rect bounds)
    {
        var center = bounds.Center;
        var radius = Math.Min(bounds.Width, bounds.Height) * 0.48;
        var spin = _time * 0.4;
        for (var i = 0; i < Bands * 2; i++)
        {
            var level = _levels[i % Bands];
            var angle = spin + i * Math.PI / Bands;
            var inner = radius * 0.08;
            var outer = inner + radius * (0.15 + level * 0.85);
            var (r, g, b) = Hue(_time * 40 + i * 7.5);
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(r, g, b)), Math.Max(1.5, radius / 60));
            context.DrawLine(pen, new Point(center.X + Math.Cos(angle) * inner, center.Y + Math.Sin(angle) * inner),
                new Point(center.X + Math.Cos(angle) * outer, center.Y + Math.Sin(angle) * outer));
        }
    }

    // ---- Frame buffer --------------------------------------------------------------------------

    private void EnsureBuffer(Rect bounds)
    {
        // Low resolution, the aspect of the surface; scaled up smoothly (the originals were soft too).
        // A phone that struggles gets a smaller buffer (the governor's tier): 320, 240 or 160 wide.
        var w = Preview ? 96 : BufferWidths[_tier];
        var h = Math.Clamp((int)Math.Round(w * bounds.Height / Math.Max(1, bounds.Width)), Preview ? 24 : 60, w);
        if (w == _w && h == _h)
            return;
        _w = w;
        _h = h;
        _frame = new byte[w * h * 4];
        _back = new byte[w * h * 4];
        _bitmap?.Dispose();
        _bitmap = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
    }

    private void Clear()
    {
        Array.Clear(_frame);
        Array.Clear(_back);
        _particles.Clear();
    }

    private void Present(DrawingContext context, Rect bounds)
    {
        if (_bitmap is null)
            return;
        using (var buffer = _bitmap.Lock())
        {
            for (var y = 0; y < _h; y++)
                Marshal.Copy(_frame, y * _w * 4, buffer.Address + y * buffer.RowBytes, _w * 4);
        }

        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.HighQuality }))
            context.DrawImage(_bitmap, new Rect(0, 0, _w, _h), bounds);
    }

    /// <summary>The previous frame, zoomed / rotated / shifted about the centre and faded: the trails.</summary>
    private void Feedback(double zoom, double rotate, double fade, Func<double, double, (double Dx, double Dy)>? warp = null)
        => Feedback(zoom, rotate, (fade, fade, fade), warp);

    /// <summary>As above, fading red, green and blue at their own rates (fire cools from white through yellow to red).</summary>
    private void Feedback(double zoom, double rotate, (double R, double G, double B) fade, Func<double, double, (double Dx, double Dy)>? warp = null)
    {
        (_frame, _back) = (_back, _frame);
        double cx = _w / 2.0, cy = _h / 2.0, cos = Math.Cos(rotate) / zoom, sin = Math.Sin(rotate) / zoom;
        Span<int> keep = [(int)(fade.B * 256), (int)(fade.G * 256), (int)(fade.R * 256)];
        for (var y = 0; y < _h; y++)
        {
            for (var x = 0; x < _w; x++)
            {
                double dx = x - cx, dy = y - cy;
                var sx = cx + dx * cos + dy * sin;
                var sy = cy - dx * sin + dy * cos;
                if (warp is not null)
                {
                    var (wx, wy) = warp(sx, sy);
                    sx += wx;
                    sy += wy;
                }

                var o = (y * _w + x) * 4;
                int ix = (int)sx, iy = (int)sy;
                if (ix < 0 || iy < 0 || ix >= _w - 1 || iy >= _h - 1)
                {
                    _frame[o] = _frame[o + 1] = _frame[o + 2] = 0;
                    _frame[o + 3] = 255;
                    continue;
                }

                // Bilinear sample of the previous frame.
                double fx = sx - ix, fy = sy - iy;
                var i00 = (iy * _w + ix) * 4;
                var i10 = i00 + 4;
                var i01 = i00 + _w * 4;
                var i11 = i01 + 4;
                for (var c = 0; c < 3; c++)
                {
                    var v = (_back[i00 + c] * (1 - fx) + _back[i10 + c] * fx) * (1 - fy) + (_back[i01 + c] * (1 - fx) + _back[i11 + c] * fx) * fy;
                    _frame[o + c] = (byte)((int)v * keep[c] >> 8);
                }

                _frame[o + 3] = 255;
            }
        }
    }

    private void Plot(double x, double y, (byte R, byte G, byte B) color, double strength = 1)
    {
        int ix = (int)x, iy = (int)y;
        if (ix < 0 || iy < 0 || ix >= _w || iy >= _h)
            return;
        var o = (iy * _w + ix) * 4;
        _frame[o] = (byte)Math.Min(255, _frame[o] + color.B * strength);
        _frame[o + 1] = (byte)Math.Min(255, _frame[o + 1] + color.G * strength);
        _frame[o + 2] = (byte)Math.Min(255, _frame[o + 2] + color.R * strength);
    }

    private void Dot(double x, double y, (byte R, byte G, byte B) color, double radius = 1.2)
    {
        for (var dy = -radius; dy <= radius; dy++)
            for (var dx = -radius; dx <= radius; dx++)
                if (dx * dx + dy * dy <= radius * radius)
                    Plot(x + dx, y + dy, color, 1 - Math.Sqrt(dx * dx + dy * dy) / (radius + 1));
    }

    private void Line(double x0, double y0, double x1, double y1, (byte R, byte G, byte B) color, double radius = 0.8)
    {
        var steps = (int)Math.Max(1, Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)));
        for (var s = 0; s <= steps; s++)
        {
            var t = (double)s / steps;
            Dot(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, color, radius);
        }
    }

    /// <summary>The waveform drawn around a circle (radius grows with the signal).</summary>
    private void WaveRing(double radius, double amplitude, Func<int, (byte, byte, byte)> color, double spin = 0)
    {
        double cx = _w / 2.0, cy = _h / 2.0, px = 0, py = 0;
        const int points = 128;
        for (var i = 0; i <= points; i++)
        {
            var a = spin + i * 2 * Math.PI / points;
            var r = radius + _wave[i * (WaveLength - 1) / points] * amplitude;
            double x = cx + Math.Cos(a) * r, y = cy + Math.Sin(a) * r;
            if (i > 0)
                Line(px, py, x, y, color(i));
            (px, py) = (x, y);
        }
    }

    private void WaveLine(double y0, double amplitude, Func<int, (byte, byte, byte)> color)
    {
        double px = 0, py = 0;
        for (var i = 0; i < WaveLength; i += 4)
        {
            double x = i * (_w - 1.0) / (WaveLength - 1), y = y0 + _wave[i] * amplitude;
            if (i > 0)
                Line(px, py, x, y, color(i));
            (px, py) = (x, y);
        }
    }

    private void Mirror4()
    {
        for (var y = 0; y < _h / 2; y++)
        {
            for (var x = 0; x < _w / 2; x++)
            {
                var source = (y * _w + x) * 4;
                Buffer.BlockCopy(_frame, source, _frame, (y * _w + _w - 1 - x) * 4, 4);
                Buffer.BlockCopy(_frame, source, _frame, ((_h - 1 - y) * _w + x) * 4, 4);
                Buffer.BlockCopy(_frame, source, _frame, ((_h - 1 - y) * _w + _w - 1 - x) * 4, 4);
            }
        }
    }

    // ---- Frame-buffer visualizations ------------------------------------------------------------

    private void Water()
    {
        // Blue ripples drifting outwards.
        Feedback(1.02 + Bass * 0.03, 0.004, 0.94, (x, y) => (Math.Sin(y * 0.08 + _time * 2) * 0.8, Math.Cos(x * 0.06 + _time * 1.7) * 0.8));
        WaveRing(_h * 0.18 + Bass * _h * 0.1, _h * 0.25, i => Hue(195 + i * 0.3, 0.7, 1));
    }

    private void Swirl()
    {
        Feedback(1.0 - Mid * 0.02, 0.03 + Bass * 0.05, 0.95);
        WaveLine(_h / 2.0, _h * 0.4, i => Hue(_time * 60 + i * 0.4));
    }

    private void Warp()
    {
        Feedback(0.97, -0.01, 0.93, (x, y) =>
        {
            double dx = x - _w / 2.0, dy = y - _h / 2.0, d = Math.Sqrt(dx * dx + dy * dy) + 1;
            return (dx / d * Bass * 3, dy / d * Bass * 3);
        });
        for (var i = 0; i < Bands; i++)
        {
            var a = i * 2 * Math.PI / Bands + _time;
            var r = _h * 0.1 + _levels[i] * _h * 0.4;
            Dot(_w / 2.0 + Math.Cos(a) * r, _h / 2.0 + Math.Sin(a) * r, Hue(280 + i * 3, 0.8), 1.8);
        }
    }

    private void OceanMist()
    {
        // The spectrum rising as soft blue mist.
        Feedback(1.0, 0, 0.97, (_, _) => (0, 1.2));
        for (var i = 0; i < Bands; i++)
        {
            var x = (i + 0.5) * _w / Bands;
            var v = _levels[i];
            for (var dy = 0; dy < v * _h * 0.5; dy += 2)
                Dot(x + _random.NextDouble() * 4 - 2, _h - 2 - dy, Hue(200 + v * 40, 0.6, 0.35 + v * 0.4), 2);
        }
    }

    private void FireStorm()
    {
        // The waveform as a flame: heat rises and cools from white through yellow and red.
        Feedback(1.0, 0, (0.97, 0.9, 0.7), (x, y) => (Math.Sin(y * 0.15 + _time * 7) * 0.7, 1.6 + Bass * 1.5));
        // The spectrum feeds the flames along the bottom, the waveform licks above them.
        for (var i = 0; i < Bands; i++)
        {
            var x = (i + 0.5) * _w / Bands;
            for (var k = 0; k < 1 + _levels[i] * 6; k++)
                Dot(x + _random.NextDouble() * 6 - 3, _h - 2 - _random.NextDouble() * _levels[i] * _h * 0.25, (255, 220, 160), 2.2);
        }

        WaveLine(_h * 0.75, _h * 0.2, _ => (255, 200, 120));
    }

    private void Randomization()
    {
        // Kaleidoscope: trails pulled to the centre, mirrored four ways, colours cycling.
        Feedback(0.96 + Bass * 0.05, 0.02, 0.92);
        var hue = _time * 50;
        WaveLine(_h / 2.0 - _h * 0.2, _h * 0.3, i => Hue(hue + i * 0.5));
        WaveRing(_h * 0.15, _h * 0.12, i => Hue(hue + 180 + i * 2), _time);
        Mirror4();
    }

    private void EventHorizon()
    {
        // Falling into a tunnel: zoom in hard, rotating, rings of the waveform at the rim.
        Feedback(1.06 + Bass * 0.04, 0.02, 0.9);
        WaveRing(_h * 0.06 + Energy * _h * 0.06, _h * 0.08, i => Hue(_time * 90 + i * 3, 0.9));
    }

    private void Cottonstar()
    {
        // A star of the spectrum, puffed outwards.
        Feedback(1.03, -0.015, 0.91);
        double cx = _w / 2.0, cy = _h / 2.0;
        for (var i = 0; i < 12; i++)
        {
            var a = _time * 0.7 + i * Math.PI / 6;
            var r = _h * (0.05 + _levels[i * 3] * 0.35);
            Line(cx, cy, cx + Math.Cos(a) * r, cy + Math.Sin(a) * r, Hue(i * 30 + _time * 30, 0.4, 1));
        }
    }

    private void MusicalColors()
    {
        // Soft coloured clouds that swell with each part of the spectrum.
        Feedback(1.0, 0, 0.86);
        for (var k = 0; k < 6; k++)
        {
            var level = _levels[k * 7];
            var x = _w * (0.5 + 0.38 * Math.Sin(_time * (0.3 + k * 0.07) + k));
            var y = _h * (0.5 + 0.35 * Math.Cos(_time * (0.25 + k * 0.05) + k * 2));
            var radius = 6 + level * _h * 0.35;
            var color = Hue(k * 60 + _time * 10, 0.8, 0.35);
            for (var dy = -radius; dy <= radius; dy += 1)
                for (var dx = -radius; dx <= radius; dx += 1)
                {
                    var d = Math.Sqrt(dx * dx + dy * dy) / radius;
                    if (d <= 1)
                        Plot(x + dx, y + dy, color, (1 - d) * 0.5);
                }
        }
    }

    private void Fountain()
    {
        Feedback(1.0, 0, 0.8);
        var spawn = (int)(2 + Bass * 14);
        for (var i = 0; i < spawn; i++)
        {
            _particles.Add(new Particle
            {
                X = _w / 2.0, Y = _h - 4,
                Vx = (_random.NextDouble() - 0.5) * (2 + Treble * 6),
                Vy = -(2 + Bass * 6 + _random.NextDouble() * 2),
                Life = 1,
                Color = Hue(_time * 40 + _random.NextDouble() * 60),
            });
        }

        foreach (var p in _particles)
        {
            p.X += p.Vx;
            p.Y += p.Vy;
            p.Vy += 0.15;
            p.Life -= 0.012;
            Dot(p.X, p.Y, p.Color, 1.4);
        }

        _particles.RemoveAll(p => p.Life <= 0 || p.Y > _h + 4);
        if (_particles.Count > 1500)
            _particles.RemoveRange(0, _particles.Count - 1500);
    }

    private void Rings()
    {
        // Plenoptic: rings of the spectrum flowing outwards.
        Feedback(1.04, 0, 0.93);
        for (var i = 0; i < Bands; i += 6)
        {
            var radius = 4 + i * 0.6 + _levels[i] * 10;
            var color = Hue(i * 7 + _time * 70, 0.9, _levels[i]);
            for (var a = 0.0; a < Math.PI * 2; a += 0.05)
                Plot(_w / 2.0 + Math.Cos(a) * radius, _h / 2.0 + Math.Sin(a) * radius, color);
        }
    }
}
