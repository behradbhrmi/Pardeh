using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Pardeh.Core.Playback;
using Path = Avalonia.Controls.Shapes.Path;

namespace Pardeh.UI.Rendering;

/// <summary>
/// Pardeh Glass phone: the volume popup that opens above the transport pill's speaker button. A small blue-glass pill with a
/// round speaker button (tap = mute), a glossy slider (cyan fill, silver thumb) and the percentage. Dragging sets the player
/// volume live. A transparent sheet behind it closes it on a tap outside; it also closes after three quiet seconds
/// (the host closes it on Back). Opt-in: only the Glass skin script asks for it (<c>pardeh.command('volumepopup', x, y)</c>).
/// </summary>
internal sealed class VolumePopup : UserControl
{
    private const double PopupWidth = 300, PopupHeight = 50, Gap = 6, Caret = 7;
    private static readonly TimeSpan IdleClose = TimeSpan.FromSeconds(3);

    private readonly MediaPlayer _player;
    private readonly Action _close;
    private readonly DispatcherTimer _idle = new() { Interval = IdleClose };
    private readonly Slider _slider;
    private readonly TextBlock _percent;
    private readonly Path _speaker, _waves, _cross;
    private readonly Border _card;
    private readonly Path _caret;
    private bool _syncing;

    public VolumePopup(MediaPlayer player, Action close)
    {
        _player = player;
        _close = close;
        var theme = MobileTheme.ForGlass();
        theme.ApplyGlassControls(Resources);

        _speaker = new Path { Data = Geometry.Parse("M 3.5,8 L 6.5,8 L 10,5 L 10,15 L 6.5,12 L 3.5,12 Z"), Fill = Brushes.White };
        _waves = new Path { Data = Geometry.Parse("M 12.6,7.6 Q 14.4,10 12.6,12.4 M 14.8,5.8 Q 17.6,10 14.8,14.2"), Stroke = Brushes.White, StrokeThickness = 1.5, StrokeLineCap = PenLineCap.Round };
        _cross = new Path { Data = Geometry.Parse("M 12.5,7.5 L 17,12.5 M 17,7.5 L 12.5,12.5"), Stroke = Brushes.White, StrokeThickness = 1.5, StrokeLineCap = PenLineCap.Round };
        var glyph = new Canvas { Width = 20, Height = 20, Children = { _speaker, _waves, _cross } };
        var button = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Margin = new Thickness(6, 0, 0, 0), Background = Brushes.Transparent,
            Child = new Viewbox { Width = 26, Height = 26, Child = glyph, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        button.Tapped += (_, _) =>
        {
            _player.Mute = !_player.Mute;
            Sync();
            Poke();
        };

        _slider = new Slider { Minimum = 0, Maximum = 100, Value = player.Volume, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0), MinHeight = 36 };
        _slider.ValueChanged += (_, _) =>
        {
            if (_syncing)
                return;
            _player.Volume = Math.Round(_slider.Value);
            if (_player.Mute && _player.Volume > 0)
                _player.Mute = false; // moving the level up from muted means "I want to hear it"
            Sync();
            Poke();
        };
        _slider.AddHandler(PointerPressedEvent, (_, _) => Poke(), Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        _slider.AddHandler(PointerMovedEvent, (_, _) => Poke(), Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);

        _percent = new TextBlock { FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White, Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(_slider, 1);
        Grid.SetColumn(_percent, 2);
        row.Children.Add(button);
        row.Children.Add(_slider);
        row.Children.Add(_percent);

        _card = new Border
        {
            Width = PopupWidth, Height = PopupHeight, CornerRadius = new CornerRadius(PopupHeight / 2), Child = row,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(theme.G("#2F74E8"), 0), new GradientStop(theme.G("#1646B8"), 0.5), new GradientStop(theme.G("#0C2F94"), 1) },
            },
            BorderBrush = new SolidColorBrush(theme.G("#9CCBFF")), BorderThickness = new Thickness(1.4),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 3, Blur = 12, Color = GlassPalette.Map(Color.FromArgb(150, 0, 0, 20)) }),
        };
        _caret = new Path
        {
            Data = Geometry.Parse(FormattableString.Invariant($"M 0,0 L {Caret * 2},0 L {Caret},{Caret} Z")), Width = Caret * 2, Height = Caret, Stretch = Stretch.Fill,
            Fill = new SolidColorBrush(theme.G("#0C2F94")), Stroke = new SolidColorBrush(theme.G("#9CCBFF")), StrokeThickness = 1,
        };

        // The sheet behind: any tap on it (outside the popup) closes the popup.
        var sheet = new Border { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
        sheet.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            _close();
        };
        var host = new Canvas();
        Content = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Dark, Child = new Panel { Children = { sheet, host } } };
        _host = host;
        host.Children.Add(_card);
        host.Children.Add(_caret);

        _idle.Tick += (_, _) => _close();
        IsVisible = false;
        Sync();
    }

    private readonly Canvas _host;

    /// <summary>Shows the popup above the anchor (the speaker button's centre top, in skin pixels) inside a root of the given size.</summary>
    public void Open(double rootWidth, double rootHeight, double anchorX, double anchorY)
    {
        Width = rootWidth;
        Height = rootHeight;
        var left = Math.Clamp(anchorX - 22, 8, Math.Max(8, rootWidth - PopupWidth - 8));
        var top = Math.Max(4, anchorY - Gap - Caret - PopupHeight);
        Canvas.SetLeft(_card, left);
        Canvas.SetTop(_card, top);
        Canvas.SetLeft(_caret, Math.Clamp(anchorX - Caret, left + 18, left + PopupWidth - 18 - Caret * 2));
        Canvas.SetTop(_caret, top + PopupHeight - 1);
        _syncing = true;
        _slider.Value = _player.Volume;
        _syncing = false;
        Sync();
        IsVisible = true;
        Poke();
    }

    public void Close()
    {
        _idle.Stop();
        IsVisible = false;
    }

    /// <summary>Restarts the three-second countdown (any touch on the popup).</summary>
    private void Poke()
    {
        _idle.Stop();
        _idle.Start();
    }

    private void Sync()
    {
        var muted = _player.Mute || _player.Volume <= 0;
        _waves.IsVisible = !muted;
        _cross.IsVisible = muted;
        _percent.Text = muted && _player.Mute ? "Muted" : ((int)Math.Round(_player.Volume)).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
        _percent.FontSize = muted && _player.Mute ? 11 : 13;
    }
}
