using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Pardeh.Core.Playback;

namespace Pardeh.UI.Rendering;

/// <summary>The colours of an <see cref="EqBank"/>: scale text, rail, the lit part of the rail and the knob (top, middle, bottom, rim).</summary>
internal sealed record EqLook(string Ink, string RailEdge, string RailMiddle, string LitTop, string LitBottom, string KnobTop, string KnobMiddle, string KnobBottom, string KnobRim, string Grip, bool Round)
{
    public static readonly EqLook Amber = new("#9AA8BF", "#010307", "#202A3B", "#FFB52E", "#8A4300", "#FFE08A", "#FF9D00", "#B44B00", "#FFE4A0", "#7A3A00", false);
    public static readonly EqLook Groove = new("#B7CCF2", "#0A2766", "#0A2766", "#A8E0FF", "#4C9BE8", "#FFFFFF", "#FFFFFF", "#E4F2FF", "#FFFFFF", "#FFFFFF", true);
}

/// <summary>
/// The equalizer bank of the art looks: ten vertical sliders (amber in sunk rails, or Groove's pale blue with round white thumbs) with a
/// +12 / 0 / -12 dB scale and the band frequencies underneath. The bands keep the player's full range
/// (<see cref="EqualizerSettings.MinGain"/> to MaxGain); the scale marks sit at their true heights. A press or drag moves the band
/// under the finger, in whole decibels.
/// </summary>
internal sealed class EqBank : Control
{
    private const double Axis = 40, Top = 14, Bottom = 24, KnobW = 20, KnobH = 14;
    private static readonly string[] Names = ["31", "62", "125", "250", "500", "1k", "2k", "4k", "8k", "16k"];

    private readonly EqualizerSettings _eq;
    private readonly Action<int, double> _set;
    private readonly EqLook _look;
    private int _dragging = -1;

    public EqBank(EqualizerSettings eq, Action<int, double> set, EqLook look)
    {
        _eq = eq;
        _set = set;
        _look = look;
        Height = 240;
    }

    private double BandWidth => Math.Max(1, (Bounds.Width - Axis - 6) / EqualizerSettings.BandCount);

    private double CenterOf(int band) => Axis + BandWidth * (band + 0.5);

    /// <summary>The height of a gain on the rail.</summary>
    private double YOf(double gain) => Top + (EqualizerSettings.MaxGain - gain) / (EqualizerSettings.MaxGain - EqualizerSettings.MinGain) * (Bounds.Height - Top - Bottom);

    private double GainAt(double y) =>
        Math.Round(Math.Clamp(EqualizerSettings.MaxGain - (y - Top) / Math.Max(1, Bounds.Height - Top - Bottom) * (EqualizerSettings.MaxGain - EqualizerSettings.MinGain), EqualizerSettings.MinGain, EqualizerSettings.MaxGain));

    private static IBrush Fade(string top, string middle, string bottom, bool across = false) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(across ? 1 : 0, across ? 0 : 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse(top), 0), new GradientStop(Color.Parse(middle), 0.5), new GradientStop(Color.Parse(bottom), 1) },
    };

    private static FormattedText Label(string text, Color color, double size = 11) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, UiTheme.Typeface(), size, new SolidColorBrush(color));

    public override void Render(DrawingContext context)
    {
        var grey = Color.Parse(_look.Ink);
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size)); // the whole bank takes the finger
        foreach (var (gain, text) in new[] { (12.0, "+12dB"), (0.0, "0dB"), (-12.0, "-12dB") })
        {
            var label = Label(text, grey);
            var y = YOf(gain);
            context.DrawText(label, new Point(Axis - 6 - label.Width, y - label.Height / 2));
            context.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(gain == 0 ? 60 : 34), 154, 168, 191)), new Rect(Axis, y, Math.Max(0, Bounds.Width - Axis - 6), 1));
        }

        var dim = _eq.Bypass ? 0.45 : 1.0;
        for (var band = 0; band < EqualizerSettings.BandCount; band++)
        {
            var x = CenterOf(band);
            var rail = new Rect(x - 3.5, Top - 4, 7, Bounds.Height - Top - Bottom + 8);
            context.DrawRectangle(Fade(_look.RailEdge, _look.RailMiddle, _look.RailEdge, across: true), _look.Round ? null : new Pen(new SolidColorBrush(Color.Parse("#4E5B72")), 1), new RoundedRect(rail, 3.5));
            var knob = YOf(_eq.GetGain(band));
            using (context.PushOpacity(dim))
            {
                // The lit part of the rail, from the bottom up to the knob.
                context.DrawRectangle(Fade(_look.LitTop, _look.LitTop, _look.LitBottom), null, new RoundedRect(new Rect(x - 1.5, knob, 3, Math.Max(0, rail.Bottom - 3 - knob)), 1.5));
                double width = _look.Round ? 16 : KnobW, height = _look.Round ? 16 : KnobH, corner = _look.Round ? 8 : 4;
                var cap = new Rect(x - width / 2, knob - height / 2, width, height);
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), null, new RoundedRect(cap.Translate(new Vector(0, 2)), corner));
                context.DrawRectangle(Fade(_look.KnobTop, _look.KnobMiddle, _look.KnobBottom), new Pen(new SolidColorBrush(Color.Parse(_look.KnobRim)), 1), new RoundedRect(cap, corner));
                context.FillRectangle(new SolidColorBrush(Color.Parse(_look.Grip)), new Rect(cap.X + 4, knob - 0.5, width - 8, 1));
            }

            var name = Label(Names[band], grey);
            context.DrawText(name, new Point(x - name.Width / 2, Bounds.Height - name.Height - 2));
        }
    }

    private void Move(Point at, int band)
    {
        var gain = GainAt(at.Y);
        if (Math.Abs(gain - _eq.GetGain(band)) >= 0.5)
            _set(band, gain);
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var at = e.GetPosition(this);
        if (at.X < Axis - 4)
            return;
        _dragging = Math.Clamp((int)((at.X - Axis) / BandWidth), 0, EqualizerSettings.BandCount - 1);
        e.Pointer.Capture(this);
        e.PreventGestureRecognition(); // a drag here moves the band, it does not scroll the page
        e.Handled = true;
        Move(at, _dragging);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging >= 0)
            Move(e.GetPosition(this), _dragging);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = -1;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = -1;
    }
}

/// <summary>
/// A VU-style needle meter that follows the playing audio's level with a slow, mechanical swing. It redraws about 30 times a second
/// only while it is on screen.
/// </summary>
internal sealed class VuMeter : Control
{
    private readonly MediaPlayer _player;
    private readonly DispatcherTimer _timer;
    private double _needle; // 0 (rest) to 1 (full scale)

    public VuMeter(MediaPlayer player)
    {
        _player = player;
        Height = 108;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Step();
        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    private void Step()
    {
        if (!AppVisibility.Foreground || !IsEffectivelyVisible)
            return;
        var feed = SpectrumFeed.For(_player);
        feed.Update(SpectrumFeed.Now);
        var target = feed.Playing ? Math.Clamp(feed.Bands3().Energy * 1.25, 0, 1) : 0;
        var next = _needle + (target - _needle) * (target > _needle ? 0.35 : 0.15);
        if (Math.Abs(next - _needle) < 0.002)
            return;
        _needle = next;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var face = new Rect(Bounds.Size).Deflate(1);
        context.DrawRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#241804"), 0), new GradientStop(Color.Parse("#4A2E05"), 0.65), new GradientStop(Color.Parse("#120C02"), 1) },
        }, new Pen(new SolidColorBrush(Color.Parse("#59678A")), 1.2), new RoundedRect(face, 8));
        using var clip = context.PushClip(new RoundedRect(face.Deflate(2), 6));

        var pivot = new Point(Bounds.Width / 2, Bounds.Height + 34);
        var radius = Bounds.Height + 8;
        const double sweep = 44; // degrees either side of straight up
        Point On(double t, double r)
        {
            var angle = (-sweep + 2 * sweep * t) * Math.PI / 180;
            return new Point(pivot.X + Math.Sin(angle) * r * 1.35, pivot.Y - Math.Cos(angle) * r);
        }

        var amber = new SolidColorBrush(Color.Parse("#FFC04A"));
        var red = new SolidColorBrush(Color.Parse("#FF5A36"));
        // The scale: -20 to +3 VU; the last stretch is the red zone.
        (double At, string Text)[] marks = [(0, "-20"), (0.2, "-10"), (0.38, "-7"), (0.52, "-5"), (0.66, "-3"), (0.8, "0"), (1, "+3")];
        for (var i = 0; i <= 40; i++)
        {
            var t = i / 40.0;
            context.DrawLine(new Pen(t > 0.8 ? red : amber, t > 0.8 ? 3 : 1.5), On(t, radius), On(Math.Min(1, t + 0.018), radius));
        }

        foreach (var (at, text) in marks)
        {
            context.DrawLine(new Pen(at > 0.79 ? red : amber, 1.5), On(at, radius), On(at, radius + 7));
            var label = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, UiTheme.Typeface(), 10, at > 0.79 ? red : amber);
            var where = On(at, radius + 15);
            context.DrawText(label, new Point(where.X - label.Width / 2, where.Y - label.Height / 2));
        }

        var title = new FormattedText("VU", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, UiTheme.Typeface(true), 18, amber);
        context.DrawText(title, new Point((Bounds.Width - title.Width) / 2, Bounds.Height - title.Height - 14));
        context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#FFF3D0")), 2, lineCap: PenLineCap.Round), pivot, On(_needle, radius + 4));
    }
}
