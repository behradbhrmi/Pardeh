using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Pardeh.Skinning.Runtime;

namespace Pardeh.UI.Rendering;

/// <summary>
/// One live thumbnail of the visualization picker: a style drawn small, on made-up music (<see cref="DemoTap"/>), moving only while the
/// picker is open. Glass styles use <see cref="GlassVisualizer"/>; classic presets the frame-buffer renderer at a tiny size.
/// </summary>
internal sealed class VizThumb : Control
{
    private readonly VisualizationRenderer _renderer = new();
    private readonly int _index;
    private readonly bool _glass;
    private double _last = double.NaN;

    public VizThumb(DemoTap demo, bool glass, int index)
    {
        _glass = glass;
        _index = index;
        _renderer.Source = demo;
        _renderer.Preview = true;
        _renderer.Glass = glass;
        if (glass)
            _renderer.GlassStyle = VizStyles.GlassStyle(index);
        ClipToBounds = true;
        IsHitTestVisible = false;
    }

    public override void Render(DrawingContext context)
    {
        var now = SpectrumFeed.Now;
        var dt = double.IsNaN(_last) ? 1.0 / 20 : now - _last;
        _last = now;
        _renderer.Render(context, new Rect(Bounds.Size), _glass ? 0 : _index, null, dt);
    }
}

/// <summary>
/// The visualization picker of the phone looks: a sheet of live thumbnails (the eight Glass styles, or the classic presets on the other
/// looks) with a tick on the one showing, and the automatic modes (keep, change on every track, auto-cycle every 15 s). A tap on a
/// thumbnail shows it at once (the visualization stays visible above or beside the sheet) and remembers it as the default.
/// </summary>
internal sealed class VizPicker : UserControl
{
    private readonly MobileTheme _t;
    private readonly bool _glass;
    private readonly DemoTap _demo = new();
    private readonly UniformGrid _grid = new() { Columns = 2 };
    private readonly StackPanel _auto = new();
    private readonly List<(Border Tile, int Index)> _tiles = [];
    private readonly List<Control> _thumbs = [];
    private readonly DispatcherTimer _timer;

    public VizPicker(MobileTheme theme, bool glass, Action close)
    {
        _t = theme;
        _glass = glass;
        if (theme.Face is { } face)
            FontFamily = face;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(66), DispatcherPriority.Render, (_, _) => Tick());

        var title = new TextBlock { Text = "Visualization", FontSize = 17, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(_t.HeadText), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0) };
        var header = new Border
        {
            Height = 48,
            Background = _t.Glass || _t.Royal ? _t.HeadBrush : new SolidColorBrush(_t.Card),
            CornerRadius = new CornerRadius(16, 16, 0, 0),
            Child = title,
        };
        DockPanel.SetDock(header, Dock.Top);

        Control footer = _t.Glass ? _t.GlassPill("Close", close) : CloseButton(close);
        DockPanel.SetDock(footer, Dock.Bottom);

        BuildTiles();
        BuildAuto();
        var body = new StackPanel
        {
            Margin = new Thickness(10, 10, 10, 4),
            Children = { _grid, Heading("Change automatically"), _auto, Note("Your pick is kept as the default; the automatic modes change the style that shows without changing it.") },
        };
        var scroller = new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };

        Content = new Border
        {
            Background = _t.Glass ? _t.BodyBrush : new SolidColorBrush(_t.Back),
            BorderBrush = _t.Glass ? new SolidColorBrush(_t.G("#7DB7FB")) : new SolidColorBrush(_t.Line),
            BorderThickness = new Thickness(_t.Glass ? 1.4 : 1),
            CornerRadius = new CornerRadius(16),
            ClipToBounds = true,
            Child = new DockPanel { Children = { header, footer, scroller } },
        };
        Refresh();
    }

    public bool Glass => _glass;

    /// <summary>How many thumbnails sit side by side (2 in a portrait sheet, 3 or more when it is wide).</summary>
    public void SetColumns(int columns) => _grid.Columns = Math.Max(1, columns);

    private Control CloseButton(Action close)
    {
        var label = new TextBlock { Text = "Close", FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(_t.Accent), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var row = new Border { Height = 52, Background = Brushes.Transparent, Child = label };
        row.Tapped += (_, _) => close();
        return row;
    }

    private TextBlock Heading(string text) =>
        new() { Text = text, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(_t.Sub), Margin = new Thickness(6, 14, 6, 6) };

    private TextBlock Note(string text) =>
        new() { Text = text, FontSize = 12, Foreground = new SolidColorBrush(_t.Sub), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 6, 6, 8) };

    private void BuildTiles()
    {
        var entries = _glass
            ? VizStyles.Glass.Select((s, i) => (Index: i, Name: s.Name, Sub: (string?)null)).ToList()
            : VisualizationCatalog.All.Select((v, i) => (Index: i, Name: v.Preset, Sub: (string?)v.Type)).ToList();
        foreach (var (index, name, sub) in entries)
        {
            var thumb = new VizThumb(_demo, _glass, index) { Height = 74 };
            _thumbs.Add(thumb);
            var caption = new StackPanel { Margin = new Thickness(8, 5, 8, 6), Spacing = 0 };
            caption.Children.Add(new TextBlock { Text = name, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(_t.Ink), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
            if (sub is not null)
                caption.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = new SolidColorBrush(_t.Sub), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 });
            var tile = new Border
            {
                Margin = new Thickness(4),
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(2),
                ClipToBounds = true,
                Background = _t.Glass ? _t.CardBrush : new SolidColorBrush(_t.Card),
                Child = new StackPanel { Children = { thumb, caption } },
            };
            var captured = index;
            tile.Tapped += (_, _) =>
            {
                VizDirector.Pick(_glass, captured);
                Refresh();
            };
            _grid.Children.Add(tile);
            _tiles.Add((tile, index));
        }
    }

    private void BuildAuto()
    {
        _auto.Children.Clear();
        for (var i = 0; i < VizStyles.AutoModes.Length; i++)
        {
            var (key, name) = VizStyles.AutoModes[i];
            var on = string.Equals(AppSettings.Current.VizAuto, key, StringComparison.OrdinalIgnoreCase);
            var check = new TextBlock { Text = on ? "✓" : string.Empty, FontSize = 18, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(_t.Glass ? Colors.White : _t.Accent), Width = 28, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
            var label = new TextBlock { Text = name, FontSize = 15, Foreground = new SolidColorBrush(_t.Ink), VerticalAlignment = VerticalAlignment.Center };
            var row = new Border
            {
                MinHeight = 46,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 1),
                Background = on && _t.SelectedBrush is { } selected ? selected : Brushes.Transparent,
                Child = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0), Children = { check, label } },
            };
            var captured = key;
            row.Tapped += (_, _) =>
            {
                AppSettings.Current.VizAuto = captured;
                AppSettings.Current.Save();
                VizDirector.Reload();
                Refresh();
            };
            _auto.Children.Add(row);
        }
    }

    /// <summary>Redraws the selection ring and the automatic mode ticks (after a pick or a settings change).</summary>
    public void Refresh()
    {
        var current = _glass ? VizDirector.GlassCurrent : VizDirector.ClassicCurrent;
        foreach (var (tile, index) in _tiles)
            tile.BorderBrush = index == current ? new SolidColorBrush(_t.Glass ? _t.G("#CDF1FF") : _t.Accent) : Brushes.Transparent;
        BuildAuto();
    }

    // ---- The thumbnails move only while the sheet shows -----------------------------------------------

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
        {
            if (IsVisible)
            {
                Refresh();
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void Tick()
    {
        if (!IsVisible || !IsEffectivelyVisible)
        {
            _timer.Stop();
            return;
        }

        foreach (var thumb in _thumbs)
            thumb.InvalidateVisual();
    }
}
