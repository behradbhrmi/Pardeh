using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Pardeh.Core.Playback;

namespace Pardeh.UI.Rendering;

/// <summary>
/// The phone looks' "Playback tools" sheet, a bottom panel with large touch chips: the sleep timer (presets and end of track), A-B
/// repeat (set A, set B, clear) and volume levelling. Opened from the app menu; the scrim, the close button and Back close it.
/// </summary>
internal sealed class PhoneToolsSheet : UserControl
{
    private readonly MediaPlayer _player;
    private readonly MobileTheme _t;
    private readonly Action _close;
    private readonly Border _panel;
    private readonly TextBlock _sleepState, _loopState;
    private readonly List<(Border Chip, TextBlock Text, Func<bool> Selected, Func<bool> Enabled)> _chips = [];
    private readonly DispatcherTimer _timer;
    private static int _minutes; // the preset last started here (the timer itself only knows the time left); kept when the sheet is rebuilt

    private readonly StackPanel _markers = new();
    private readonly Action<int?> _name; // add a named marker (null) or rename one
    private int _openMarker = -1;        // the marker whose actions are shown
    private string _markerKey = "?";

    public PhoneToolsSheet(MediaPlayer player, MobileTheme theme, Action close, Action<int?> name, bool markersFirst = false)
    {
        _player = player;
        _t = theme;
        _close = close;
        _name = name;

        var sleep = new WrapPanel();
        sleep.Children.Add(Chip("Off", () => { _minutes = 0; player.CancelSleepTimer(); }, () => !player.SleepTimerActive));
        foreach (var minutes in MediaPlayer.SleepPresetMinutes)
        {
            var m = minutes;
            sleep.Children.Add(Chip($"{m} min", () => { _minutes = m; player.StartSleepTimer(TimeSpan.FromMinutes(m)); }, () => player.SleepRemaining is not null && _minutes == m));
        }

        sleep.Children.Add(Chip("End of track", () => { _minutes = 0; player.StartSleepAtEndOfTrack(); }, () => player.SleepAtEndOfTrack));

        var loop = new WrapPanel();
        loop.Children.Add(Chip("Set A", () => player.SetSectionStart(), () => player.LoopStart is not null && !player.SectionLoopActive));
        loop.Children.Add(Chip("Set B", () => player.SetSectionEnd(), () => player.SectionLoopActive, () => player.LoopStart is not null));
        loop.Children.Add(Chip("Clear", player.ClearSectionLoop, () => false, () => player.LoopStart is not null));

        var level = new WrapPanel();
        foreach (var (mode, caption) in new[] { (ReplayGainMode.Off, "Off"), (ReplayGainMode.Track, "Track"), (ReplayGainMode.Album, "Album") })
        {
            var chosen = mode;
            level.Children.Add(Chip(caption, () =>
            {
                player.ReplayGain = chosen;
                AppSettings.Current.ReplayGain = chosen.ToString();
                AppSettings.Current.Save();
            }, () => player.ReplayGain == chosen));
        }

        _sleepState = Text(string.Empty, 13, _t.Sub);
        _loopState = Text(string.Empty, 13, _t.Sub);
        var title = Text("Playback tools", 18, _t.Ink, true);
        title.VerticalAlignment = VerticalAlignment.Center;
        var cross = new Border
        {
            Width = 48, Height = 44, Background = Brushes.Transparent,
            Child = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse("M 0,0 L 14,14 M 14,0 L 0,14"), Stroke = new SolidColorBrush(_t.Ink), StrokeThickness = 2, Width = 14, Height = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        cross.Tapped += (_, _) => _close();
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(18, 6, 6, 0), Children = { title, cross } };
        Grid.SetColumn(cross, 1);

        var body = new StackPanel
        {
            Margin = new Thickness(14, 0, 14, 12),
            Children =
            {
                Section("Sleep timer", _sleepState), sleep,
                Section("A-B repeat", _loopState), loop,
                Section("Volume levelling", Text("ReplayGain tags, else measured", 13, _t.Sub)), level,
            },
        };
        var markerPart = new StackPanel { Children = { Section("Markers", Text("Named spots in this song", 13, _t.Sub)), _markers } };
        body.Children.Insert(markersFirst ? 0 : body.Children.Count, markerPart);
        var dock = new DockPanel { Children = { head, new ScrollViewer { Content = body, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden } } };
        DockPanel.SetDock(head, Dock.Top);
        _panel = new Border
        {
            Background = _t.Glass || _t.Art is not null ? _t.BodyBrush : new SolidColorBrush(_t.Card),
            BorderBrush = new SolidColorBrush(_t.Line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16, 16, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom, MaxWidth = 560, Child = dock,
        };
        var scrim = new Border { Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)) };
        scrim.Tapped += (_, _) => _close();
        Content = new ThemeVariantScope { RequestedThemeVariant = theme.Dark ? ThemeVariant.Dark : ThemeVariant.Light, Child = new Grid { Children = { scrim, _panel } } };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Refresh();
        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
        Refresh();
    }

    private TextBlock Text(string text, double size, Color color, bool bold = false) => new()
    {
        Text = text, FontSize = size, Foreground = new SolidColorBrush(color), FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap,
    };

    private Control Section(string name, TextBlock state)
    {
        state.VerticalAlignment = VerticalAlignment.Bottom;
        state.TextAlignment = TextAlignment.Right;
        var label = Text(name, 15, _t.Ink, true);
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(4, 12, 4, 4), Children = { label, state } };
        Grid.SetColumn(state, 1);
        return line;
    }

    /// <summary>A chip of the markers part: no state to follow, rebuilt with the list.</summary>
    private Border Plain(string label, Action tap)
    {
        var text = Text(label, 14, _t.Ink);
        text.VerticalAlignment = VerticalAlignment.Center;
        var chip = new Border
        {
            Height = 42, Padding = new Thickness(12, 0), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1.2),
            Background = new SolidColorBrush(Color.FromArgb(18, _t.Ink.R, _t.Ink.G, _t.Ink.B)), BorderBrush = new SolidColorBrush(_t.Line), Child = text,
        };
        chip.Tapped += (_, _) =>
        {
            tap();
            Refresh();
        };
        return chip;
    }

    /// <summary>Rebuilds the list of markers when it has changed: a row jumps there, its "..." button opens the actions below it.</summary>
    private void RefreshMarkers()
    {
        var markers = _player.CurrentMarkers;
        var key = $"{_player.CurrentMedia?.Source}|{_openMarker}|{string.Join(';', markers.Select(m => $"{m.Seconds}:{m.Name}"))}";
        if (key == _markerKey)
            return;
        _markerKey = key;
        _markers.Children.Clear();
        for (var i = 0; i < markers.Count; i++)
        {
            var index = i;
            var time = Text(MediaPlayer.FormatTime(markers[i].Seconds), 14, _t.Sub);
            time.Width = 52;
            time.VerticalAlignment = VerticalAlignment.Center;
            var label = Text(MediaPlayer.MarkerLabel(markers[i], i), 15.5, _t.Ink);
            label.TextWrapping = TextWrapping.NoWrap;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.VerticalAlignment = VerticalAlignment.Center;
            var dots = Text("•••", 15, _t.Ink);
            dots.HorizontalAlignment = HorizontalAlignment.Center;
            dots.VerticalAlignment = VerticalAlignment.Center;
            var more = new Border { Width = 52, Background = Brushes.Transparent, Child = dots };
            more.Tapped += (_, e) =>
            {
                e.Handled = true;
                _openMarker = _openMarker == index ? -1 : index;
                Refresh();
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), MinHeight = 46, Margin = new Thickness(4, 0, 0, 0), Background = Brushes.Transparent, Children = { time, label, more } };
            Grid.SetColumn(label, 1);
            Grid.SetColumn(more, 2);
            row.Tapped += (_, _) => _player.JumpToMarker(index);
            _markers.Children.Add(row);
            if (_openMarker != index)
                continue;
            var actions = new WrapPanel();
            actions.Children.Add(Plain("Repeat section", () => _player.LoopMarkerSection(index)));
            actions.Children.Add(Plain("Set as A", () => _player.SetSectionPointAtMarker(index, end: false)));
            actions.Children.Add(Plain("Set as B", () => _player.SetSectionPointAtMarker(index, end: true)));
            actions.Children.Add(Plain("Rename", () => _name(index)));
            actions.Children.Add(Plain("Delete", () =>
            {
                _openMarker = -1;
                _player.RemoveMarker(index);
            }));
            _markers.Children.Add(actions);
        }

        var add = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        add.Children.Add(Plain("+ Mark this spot", () => _player.AddMarker()));
        add.Children.Add(Plain("+ Named marker...", () => _name(null)));
        _markers.Children.Add(add);
    }

    private Border Chip(string name, Action tap, Func<bool> selected, Func<bool>? enabled = null)
    {
        var text = Text(name, 14.5, _t.Ink);
        text.HorizontalAlignment = HorizontalAlignment.Center;
        text.VerticalAlignment = VerticalAlignment.Center;
        var chip = new Border { Height = 44, MinWidth = 74, Padding = new Thickness(12, 0), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1.2), Child = text };
        var can = enabled ?? (() => true);
        chip.Tapped += (_, _) =>
        {
            if (!can())
                return;
            tap();
            Refresh();
        };
        _chips.Add((chip, text, selected, can));
        return chip;
    }

    /// <summary>Shows the current state: which chip is chosen, the time left and the A and B marks.</summary>
    public void Refresh()
    {
        foreach (var (chip, text, selected, enabled) in _chips)
        {
            var on = selected();
            chip.Background = on ? _t.SelectedBrush ?? new SolidColorBrush(_t.Accent) : new SolidColorBrush(Color.FromArgb(18, _t.Ink.R, _t.Ink.G, _t.Ink.B));
            chip.BorderBrush = new SolidColorBrush(on ? _t.Accent : _t.Line);
            // On a plain accent fill (the modern looks) the text takes whichever of dark and white reads better.
            var ink = !on ? _t.Ink : _t.SelectedBrush is not null ? _t.SelectedInk : Luminance(_t.Accent) > 0.5 ? Color.Parse("#10141C") : Colors.White;
            text.Foreground = new SolidColorBrush(ink);
            chip.Opacity = enabled() ? 1 : 0.4;
        }

        RefreshMarkers();
        _sleepState.Text =_player.SleepAtEndOfTrack ? "Pauses after this track"
            : _player.SleepRemaining is { } left ? $"Pauses in {(int)left.TotalMinutes}:{left.Seconds:00}" : "Off";
        _loopState.Text = _player.LoopStart is not { } a ? "Off"
            : _player.LoopEnd is { } b ? $"Repeating {MediaPlayer.FormatTime(a)} to {MediaPlayer.FormatTime(b)}" : $"A {MediaPlayer.FormatTime(a)}, now set B";
    }

    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;

    /// <summary>The tallest the panel gets, so a strip of the page stays visible above it.</summary>
    public void Fit(double height) => _panel.MaxHeight = Math.Max(160, height - 40);
}
