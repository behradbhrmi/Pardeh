using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Pardeh.Core.Playback;

namespace Pardeh.UI.Rendering;

/// <summary>
/// The full-screen control bar: shown when the mouse moves, hidden after a few idle seconds (with the cursor).
/// Transport, seek, volume, the title and the time, and a button to leave full screen.
/// </summary>
internal sealed class FullScreenControls
{
    private const double BarHeight = 72;
    private static readonly TimeSpan HideAfter = TimeSpan.FromSeconds(3);

    private readonly Dictionary<string, Rect> _buttons = [];
    private Rect _seek, _volume, _bar;
    private DateTime _lastActivity = DateTime.UtcNow;
    private string? _dragging;

    /// <summary>Raised for the exit button.</summary>
    public event Action? ExitRequested;

    public bool IsShown => _dragging is not null || DateTime.UtcNow - _lastActivity < HideAfter;

    /// <summary>Mouse moved: show the bar (and the cursor) again.</summary>
    public void Activity() => _lastActivity = DateTime.UtcNow;

    public void Draw(DrawingContext context, Size size, MediaPlayer player)
    {
        if (!IsShown)
            return;

        _bar = new Rect(0, size.Height - BarHeight, size.Width, BarHeight);
        if (AppSettings.Current.ModernFullScreenBar)
        {
            DrawModern(context, size, player);
            return;
        }

        context.FillRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(200, 40, 60, 140), 0), new GradientStop(Color.FromArgb(235, 12, 20, 60), 1) },
        }, _bar);
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(160, 140, 170, 255)), 1), _bar.TopLeft, _bar.TopRight);

        // Seek bar across the top of the bar.
        _seek = new Rect(16, _bar.Y + 10, size.Width - 32, 6);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), null, _seek, 3, 3);
        var duration = player.Duration;
        var fraction = duration > 0 ? Math.Clamp(player.CurrentPosition / duration, 0, 1) : 0;
        context.DrawRectangle(new SolidColorBrush(Color.FromRgb(126, 200, 90)), null, new Rect(_seek.X, _seek.Y, _seek.Width * fraction, _seek.Height), 3, 3);
        context.DrawEllipse(Brushes.White, new Pen(new SolidColorBrush(Color.FromRgb(60, 120, 50)), 1),
            new Point(_seek.X + _seek.Width * fraction, _seek.Center.Y), 7, 7);

        // Transport buttons.
        _buttons.Clear();
        var y = _bar.Y + 26;
        var x = 16.0;
        var playing = player.PlayState == PlayState.Playing;
        Button(context, "playpause", new Rect(x, y, 38, 38), playing ? "pause" : "play"); x += 46;
        Button(context, "stop", new Rect(x, y + 5, 28, 28), "stop"); x += 34;
        if (OperatingSystem.IsAndroid())
        {
            Button(context, "back10", new Rect(x, y + 5, 28, 28), "back10"); x += 34; // Android only: skip 10 s buttons around previous / next
            Button(context, "previous", new Rect(x, y + 5, 28, 28), "previous"); x += 34;
            Button(context, "next", new Rect(x, y + 5, 28, 28), "next"); x += 34;
            Button(context, "forward10", new Rect(x, y + 5, 28, 28), "forward10"); x += 42;
        }
        else
        {
            Button(context, "previous", new Rect(x, y + 5, 28, 28), "previous"); x += 34;
            Button(context, "next", new Rect(x, y + 5, 28, 28), "next"); x += 42;
        }

        Button(context, "mute", new Rect(x, y + 5, 28, 28), player.Mute ? "muted" : "sound"); x += 34;

        // Title and time.
        var time = $"{MediaPlayer.FormatTime(player.CurrentPosition)} / {MediaPlayer.FormatTime(duration)}";
        var timeText = Text(time, 13, Brushes.White, 200);

        // On a narrow Android screen the extra skip buttons leave no room for the volume slider next to the time (the hardware keys set the volume): it is left out.
        if (OperatingSystem.IsAndroid() && x + 130 + timeText.Width + 16 + 32 + 16 > size.Width)
        {
            _volume = default;
        }
        else
        {
            _volume = new Rect(x, y + 16, 110, 6);
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)), null, _volume, 3, 3);
            var level = player.Mute ? 0 : player.Volume / 100;
            context.DrawRectangle(Brushes.White, null, new Rect(_volume.X, _volume.Y, _volume.Width * level, _volume.Height), 3, 3);
            x += 130;
        }

        var exit = new Rect(size.Width - 16 - 32, y + 3, 32, 32);
        Button(context, "exit", exit, "exit");
        context.DrawText(timeText, new Point(exit.X - 16 - timeText.Width, y + 10));
        var title = Text(player.CurrentMedia?.Name ?? string.Empty, 13, new SolidColorBrush(Color.FromRgb(210, 222, 255)), Math.Max(1, exit.X - 32 - timeText.Width - x));
        context.DrawText(title, new Point(x, y + 10));
    }

    private static readonly IBrush ModernAccent = new SolidColorBrush(Color.Parse("#4CC2FF"));
    private static readonly IBrush ModernSurface = new SolidColorBrush(Color.FromArgb(235, 32, 32, 32));
    private static readonly IBrush ModernTrack = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
    private static readonly IBrush ModernSubtle = new SolidColorBrush(Color.FromRgb(200, 200, 200));
    private static readonly Typeface GlyphFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>Windows 11 (dark): flat surface, thin accent seek bar, Segoe Fluent Icons; same layout and hit areas as the classic bar.</summary>
    private void DrawModern(DrawingContext context, Size size, MediaPlayer player)
    {
        context.FillRectangle(ModernSurface, _bar);
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 1), _bar.TopLeft, _bar.TopRight);

        _seek = new Rect(16, _bar.Y + 11, size.Width - 32, 4);
        context.DrawRectangle(ModernTrack, null, _seek, 2, 2);
        var duration = player.Duration;
        var fraction = duration > 0 ? Math.Clamp(player.CurrentPosition / duration, 0, 1) : 0;
        context.DrawRectangle(ModernAccent, null, new Rect(_seek.X, _seek.Y, _seek.Width * fraction, _seek.Height), 2, 2);
        var knob = new Point(_seek.X + _seek.Width * fraction, _seek.Center.Y);
        context.DrawEllipse(new SolidColorBrush(Color.FromRgb(69, 69, 69)), null, knob, 8, 8);
        context.DrawEllipse(ModernAccent, null, knob, 4.5, 4.5);

        _buttons.Clear();
        var y = _bar.Y + 26;
        var x = 16.0;
        var playing = player.PlayState == PlayState.Playing;

        // Play / pause: a white disc with a dark glyph, like the Windows 11 media player.
        var play = new Rect(x, y, 38, 38);
        _buttons["playpause"] = play;
        context.DrawEllipse(Brushes.White, null, play.Center, 19, 19);
        Glyph(context, playing ? "" : "", play, 14,new SolidColorBrush(Color.FromRgb(32, 32, 32)));
        x += 46;
        ModernButton(context, "stop", new Rect(x, y + 5, 28, 28), ""); x += 34;
        ModernButton(context, "previous", new Rect(x, y + 5, 28, 28), ""); x += 34;
        ModernButton(context, "next", new Rect(x, y + 5, 28, 28), ""); x += 42;
        ModernButton(context, "mute", new Rect(x, y + 5, 28, 28), player.Mute ? "" : ""); x += 34;

        _volume = new Rect(x, y + 17, 110, 4);
        context.DrawRectangle(ModernTrack, null, _volume, 2, 2);
        var level = player.Mute ? 0 : player.Volume / 100;
        context.DrawRectangle(ModernAccent, null, new Rect(_volume.X, _volume.Y, _volume.Width * level, _volume.Height), 2, 2);
        context.DrawEllipse(new SolidColorBrush(Color.FromRgb(69, 69, 69)), null, new Point(_volume.X + _volume.Width * level, _volume.Center.Y), 7, 7);
        context.DrawEllipse(ModernAccent, null, new Point(_volume.X + _volume.Width * level, _volume.Center.Y), 4, 4);
        x += 130;

        var time = $"{MediaPlayer.FormatTime(player.CurrentPosition)} / {MediaPlayer.FormatTime(duration)}";
        var timeText = Text(time, 13, ModernSubtle, 200);
        var exit = new Rect(size.Width - 16 - 32, y + 3, 32, 32);
        ModernButton(context, "exit", exit, "");
        context.DrawText(timeText, new Point(exit.X - 16 - timeText.Width, y + 10));
        var title = Text(player.CurrentMedia?.Name ?? string.Empty, 13, Brushes.White, Math.Max(1, exit.X - 32 - timeText.Width - x));
        context.DrawText(title, new Point(x, y + 10));
    }

    private void ModernButton(DrawingContext context, string key, Rect r, string glyph)
    {
        _buttons[key] = r;
        Glyph(context, glyph, r, 15, Brushes.White);
    }

    private static void Glyph(DrawingContext context, string glyph, Rect r, double size, IBrush brush)
    {
        var text = new FormattedText(glyph, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, GlyphFont, size, brush);
        context.DrawText(text, new Point(r.Center.X - text.Width / 2, r.Center.Y - text.Height / 2));
    }

    private void Button(DrawingContext context, string key, Rect r, string glyph)
    {
        _buttons[key] = r;
        context.DrawEllipse(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromRgb(250, 252, 255), 0), new GradientStop(Color.FromRgb(180, 196, 232), 1) },
        }, new Pen(new SolidColorBrush(Color.FromRgb(90, 110, 170)), 1), r.Center, r.Width / 2, r.Height / 2);
        var ink = new SolidColorBrush(Color.FromRgb(30, 50, 110));
        var c = r.Center;
        var s = r.Width / 4;
        switch (glyph)
        {
            case "play":
                context.DrawGeometry(ink, null, Triangle(new Point(c.X - s * 0.6, c.Y - s), new Point(c.X + s, c.Y), new Point(c.X - s * 0.6, c.Y + s)));
                break;
            case "pause":
                context.FillRectangle(ink, new Rect(c.X - s * 0.8, c.Y - s, s * 0.6, s * 2));
                context.FillRectangle(ink, new Rect(c.X + s * 0.2, c.Y - s, s * 0.6, s * 2));
                break;
            case "stop":
                context.FillRectangle(ink, new Rect(c.X - s * 0.7, c.Y - s * 0.7, s * 1.4, s * 1.4));
                break;
            case "previous":
                context.FillRectangle(ink, new Rect(c.X - s, c.Y - s * 0.8, 2, s * 1.6));
                context.DrawGeometry(ink, null, Triangle(new Point(c.X + s * 0.8, c.Y - s * 0.8), new Point(c.X - s * 0.6, c.Y), new Point(c.X + s * 0.8, c.Y + s * 0.8)));
                break;
            case "next":
                context.FillRectangle(ink, new Rect(c.X + s - 2, c.Y - s * 0.8, 2, s * 1.6));
                context.DrawGeometry(ink, null, Triangle(new Point(c.X - s * 0.8, c.Y - s * 0.8), new Point(c.X + s * 0.6, c.Y), new Point(c.X - s * 0.8, c.Y + s * 0.8)));
                break;
            case "sound" or "muted":
                context.FillRectangle(ink, new Rect(c.X - s, c.Y - s * 0.4, s * 0.6, s * 0.8));
                context.DrawGeometry(ink, null, Triangle(new Point(c.X - s * 0.5, c.Y - s * 0.4), new Point(c.X + s * 0.3, c.Y - s), new Point(c.X + s * 0.3, c.Y + s)));
                if (glyph == "muted")
                    context.DrawLine(new Pen(Brushes.Firebrick, 2), new Point(c.X - s, c.Y + s), new Point(c.X + s, c.Y - s));
                break;
            case "back10" or "forward10":
                SkipGlyph(context, r, glyph == "forward10", ink);
                break;
            case "exit":
                var pen = new Pen(ink, 2);
                context.DrawRectangle(null, pen, new Rect(c.X - s, c.Y - s * 0.7, s * 2, s * 1.4));
                context.DrawLine(pen, new Point(c.X - s * 0.3, c.Y + s * 0.2), new Point(c.X + s * 0.4, c.Y - s * 0.4));
                break;
        }
    }

    /// <summary>The Fluent "Skip Back 10" / "Skip Forward 10" icon filling about 80 % of <paramref name="r"/> (Android's full-screen bar only).</summary>
    private static void SkipGlyph(DrawingContext context, Rect r, bool forward, IBrush brush)
    {
        var s = r.Width * 0.85 / 24;
        using (context.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation(r.Center.X - 12 * s, r.Center.Y - 12 * s)))
            context.DrawGeometry(brush, null, FluentIcons.Geo(forward ? "skip_forward_10" : "skip_back_10"));
    }

    private static StreamGeometry Triangle(Point a, Point b, Point c)
    {
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        g.BeginFigure(a, true);
        g.LineTo(b);
        g.LineTo(c);
        g.EndFigure(true);
        return geometry;
    }

    private static FormattedText Text(string text, double size, IBrush brush, double maxWidth) => new(
        text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush)
    {
        MaxTextWidth = Math.Max(1, maxWidth),
        MaxLineCount = 1,
        Trimming = TextTrimming.CharacterEllipsis,
    };

    /// <summary>A press: handles the bar's buttons and sliders. False when the press was on the picture.</summary>
    public bool Press(Point p, MediaPlayer player)
    {
        Activity();
        if (!_bar.Contains(p))
            return false;
        if (Inflate(_seek).Contains(p))
        {
            _dragging = "seek";
            Drag(p, player);
            return true;
        }

        if (Inflate(_volume).Contains(p))
        {
            _dragging = "volume";
            Drag(p, player);
            return true;
        }

        foreach (var (key, rect) in _buttons)
        {
            if (!rect.Contains(p))
                continue;
            switch (key)
            {
                case "playpause":
                    if (player.PlayState == PlayState.Playing) player.Pause(); else player.Play();
                    break;
                case "stop": player.Stop(); break;
                case "previous": player.Previous(); break;
                case "next": player.Next(); break;
                case "back10": player.SeekBy(-10); break;
                case "forward10": player.SeekBy(10); break;
                case "mute": player.Mute = !player.Mute; break;
                case "exit": ExitRequested?.Invoke(); break;
            }
        }

        return true; // clicks on the bar never fall through to the picture
    }

    public void Drag(Point p, MediaPlayer player)
    {
        Activity();
        switch (_dragging)
        {
            case "seek" when player.Duration > 0:
                player.CurrentPosition = Math.Clamp((p.X - _seek.X) / _seek.Width, 0, 1) * player.Duration;
                break;
            case "volume":
                player.Volume = Math.Clamp((p.X - _volume.X) / _volume.Width, 0, 1) * 100;
                player.Mute = false;
                break;
        }
    }

    public void Release() => _dragging = null;

    private static Rect Inflate(Rect r) => r.Inflate(new Thickness(0, 8));
}
