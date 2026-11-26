using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Pardeh.Core.Playback;
using Pardeh.UI;

namespace Pardeh.App;

/// <summary>
/// The Windows desktop widget: a small borderless mini player that floats over the desktop (always on top unless turned off),
/// with the cover, title, artist, a seekbar, and shuffle / back 10 s / previous / play-pause / next / forward 10 s / repeat / heart.
/// Customize it with a right-click: theme, opacity, parts, always on top. Drag it anywhere; it remembers its place.
/// Show or hide it with Ctrl+Shift+W in the player; it also reopens at start when it was open when you quit.
/// </summary>
internal sealed class MiniWidget : Window
{
    private static MiniWidget? _instance;
    private static MediaPlayer? _player;

    /// <summary>Called once by the platform services when the main window is up.</summary>
    public static IDisposable Attach(MediaPlayer player)
    {
        _player = player;
        if (Environment.GetEnvironmentVariable("PARDEH_WIDGET_SNAPSHOT") is { Length: > 0 } snapshot)
        {
            // Development aid (like PARDEH_SNAPSHOT): open the widget, render it to a PNG a few seconds later, leave the settings alone.
            if (int.TryParse(Environment.GetEnvironmentVariable("PARDEH_WIDGET_THEME"), out var theme))
                AppSettings.Current.MiniWidgetTheme = theme;
            if (int.TryParse(Environment.GetEnvironmentVariable("PARDEH_WIDGET_STYLE"), out var style))
                AppSettings.Current.MiniWidgetStyle = style;
            Dispatcher.UIThread.Post(Open, DispatcherPriority.Background);
            DispatcherTimer.RunOnce(() =>
            {
                if (_instance?.Content is Control root)
                {
                    var size = new PixelSize((int)Math.Ceiling(root.Bounds.Width), (int)Math.Ceiling(root.Bounds.Height));
                    using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
                    bitmap.Render(root);
                    bitmap.Save(snapshot, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                }

                Environment.Exit(0);
            }, TimeSpan.FromSeconds(5));
            return new Detach();
        }

        if (AppSettings.Current.MiniWidgetOpen)
            Dispatcher.UIThread.Post(Open, DispatcherPriority.Background);
        return new Detach(new WidgetBridge(player));
    }

    public static void Toggle()
    {
        if (_instance is not null)
        {
            _instance.Close();
            AppSettings.Current.MiniWidgetOpen = false;
        }
        else
        {
            Open();
            AppSettings.Current.MiniWidgetOpen = true;
        }

        AppSettings.Current.Save();
    }

    private static void Open()
    {
        if (_player is null || _instance is not null)
            return;
        _instance = new MiniWidget(_player);
        _instance.Show();
    }

    private sealed class Detach(IDisposable? bridge = null) : IDisposable
    {
        public void Dispose()
        {
            bridge?.Dispose();
            _instance?.Close();
            _player = null;
        }
    }

    private readonly MediaPlayer _p;
    private readonly AppSettings _s = AppSettings.Current;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private Bitmap? _cover;
    private string? _coverFor;
    private bool _dragSeek;

    private MiniWidget(MediaPlayer player)
    {
        _p = player;
        Title = "Pardeh widget";
        WindowDecorations = WindowDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        CanResize = false;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = _s.MiniWidgetTopmost;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = _s.MiniWidgetX >= 0 && _s.MiniWidgetY >= 0
            ? new PixelPoint((int)_s.MiniWidgetX, (int)_s.MiniWidgetY)
            : new PixelPoint(80, 80);
        _p.PlayStateChanged += OnPlayerChanged;
        _p.CurrentMediaChanged += OnPlayerChanged;
        _p.FavoritesChanged += OnPlayerChanged;
        PositionChanged += (_, e) =>
        {
            _s.MiniWidgetX = e.Point.X;
            _s.MiniWidgetY = e.Point.Y;
        };
        _tick.Tick += (_, _) => UpdateSeek();
        _tick.Start();
        Build();
    }

    protected override void OnClosed(EventArgs e)
    {
        _tick.Stop();
        _p.PlayStateChanged -= OnPlayerChanged;
        _p.CurrentMediaChanged -= OnPlayerChanged;
        _p.FavoritesChanged -= OnPlayerChanged;
        _instance = null;
        base.OnClosed(e);
    }

    private void OnPlayerChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Build);

    private void OnPlayerChanged(object? sender, PlayState state) => Dispatcher.UIThread.Post(Build);


    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _s.Save(); // the place after a drag
    }

    // ---- Content ----

    private TextBlock? _pos, _dur;
    private Border? _fill;
    private Border? _track;
    private Ellipse? _knob;

    private void Build()
    {
        if (_s.MiniWidgetStyle == 0)
        {
            BuildStrip();
            return;
        }

        var media = _p.CurrentMedia;
        var active = media is not null && _p.PlayState != PlayState.Stopped;
        var light = _s.MiniWidgetTheme == 3;
        var text = light ? Color.Parse("#14213D") : Colors.White;
        var sub = light ? Color.Parse("#4A5675") : Color.FromArgb(0xB3, 255, 255, 255);
        var accent = Accent();
        var barFill = _s.MiniWidgetTheme == 1 ? Colors.White : light ? accent : Color.Parse("#5CC8FF");
        var dim = light ? Color.FromArgb(0x80, 0x14, 0x21, 0x3D) : Color.FromArgb(0x80, 255, 255, 255);

        LoadCover(media);
        var playing = _p.PlayState is PlayState.Playing or PlayState.ScanForward or PlayState.ScanReverse;

        var column = new StackPanel { Spacing = 6, Margin = new Thickness(12) };

        // Top row: cover, title / artist, heart.
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var coverBox = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(6), ClipToBounds = true, Margin = new Thickness(0, 0, 12, 0) };
        coverBox.Child = _cover is not null && active
            ? new Image { Source = _cover, Stretch = Stretch.UniformToFill }
            : new Image { Source = Logo(), Stretch = Stretch.Uniform };
        var titleBlock = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children =
        {
            new TextBlock { Text = active ? media!.Name : "Pardeh", FontSize = 15, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(text), TextTrimming = TextTrimming.CharacterEllipsis },
            new TextBlock { Text = active ? Artist(media!) : "Nothing playing", FontSize = 13, Foreground = new SolidColorBrush(sub), TextTrimming = TextTrimming.CharacterEllipsis },
        } };
        Grid.SetColumn(titleBlock, 1);
        top.Children.Add(coverBox);
        top.Children.Add(titleBlock);
        if (_s.MiniWidgetFavorite && active)
        {
            var fav = _p.IsFavorite(media);
            var heart = IconButton(fav ? WidgetIcons.HeartOn : WidgetIcons.HeartOff, fav ? Color.Parse("#FF5C7A") : sub, 20, () => _p.ToggleFavorite(media));
            Grid.SetColumn(heart, 2);
            top.Children.Add(heart);
        }

        column.Children.Add(top);

        // Controls.
        var controls = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 4 };
        if (_s.MiniWidgetShuffle)
            controls.Children.Add(IconButton(WidgetIcons.Shuffle, _p.Shuffle ? barFill : dim, 20, () => _p.Shuffle = !_p.Shuffle));
        if (_s.MiniWidgetSkip)
            controls.Children.Add(IconButton(WidgetIcons.Back10, text, 24, () => _p.SeekBy(-10)));
        controls.Children.Add(IconButton(WidgetIcons.Prev, text, 24, () => _p.Previous()));
        controls.Children.Add(IconButton(playing ? WidgetIcons.Pause : WidgetIcons.Play, text, 30, () =>
        {
            if (playing)
                _p.Pause();
            else
                _p.Play();
        }));
        controls.Children.Add(IconButton(WidgetIcons.Next, text, 24, () => _p.Next()));
        if (_s.MiniWidgetSkip)
            controls.Children.Add(IconButton(WidgetIcons.Fwd10, text, 24, () => _p.SeekBy(10)));
        if (_s.MiniWidgetRepeat)
            controls.Children.Add(IconButton(WidgetIcons.Repeat, _p.Loop ? barFill : dim, 20, () => _p.Loop = !_p.Loop));
        column.Children.Add(controls);

        // Seekbar and times; a click or drag on the bar jumps.
        _pos = _dur = null;
        _fill = _track = null;
        _knob = null;
        if (_s.MiniWidgetSeek)
        {
            _pos = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(sub), Width = 40, VerticalAlignment = VerticalAlignment.Center };
            _dur = new TextBlock { FontSize = 11, Foreground = new SolidColorBrush(sub), Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            _track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(light ? Color.FromArgb(0x33, 0x14, 0x21, 0x3D) : Color.FromArgb(0x40, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center };
            _fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(barFill), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Width = 0 };
            _knob = new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(barFill), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var bar = new Grid { Height = 24, Background = Brushes.Transparent, Children = { _track, _fill, _knob } };
            bar.PointerPressed += (_, e) =>
            {
                _dragSeek = true;
                e.Pointer.Capture(bar);
                SeekTo(bar, e);
                e.Handled = true;
            };
            bar.PointerMoved += (_, e) =>
            {
                if (_dragSeek)
                    SeekTo(bar, e);
            };
            bar.PointerReleased += (_, e) =>
            {
                _dragSeek = false;
                e.Pointer.Capture(null);
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 2, 0, 0) };
            Grid.SetColumn(bar, 1);
            Grid.SetColumn(_dur, 2);
            row.Children.Add(_pos);
            row.Children.Add(bar);
            row.Children.Add(_dur);
            column.Children.Add(row);
        }

        var card = new Grid { Width = 340, ClipToBounds = true };
        card.Children.Add(BackgroundLayer(active));
        card.Children.Add(column);
        var root = new Border { CornerRadius = new CornerRadius(16), ClipToBounds = true, Child = card, ContextFlyout = BuildMenu() };
        root.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(root).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
        Content = root;
        UpdateSeek();
    }

    // ---- Mini player strip (the Windows Media Player 9 taskbar mini player look) ----

    private const string StopPath = "M6,6h12v12H6z";
    private const string VolumePath = "M3,9v6h4l5,5V4L7,9H3zM16.5,12c0,-1.77 -1.02,-3.29 -2.5,-4.03v8.05c1.48,-0.73 2.5,-2.25 2.5,-4.02z";

    private static LinearGradientBrush Vertical(params (string Color, double At)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
        foreach (var (color, at) in stops)
            brush.GradientStops.Add(new GradientStop(Color.Parse(color), at));
        return brush;
    }

    private static Control Glossy(string path, double size, bool green, Action click)
    {
        var face = green ? Vertical(("#9BE88A", 0), ("#2E9A2A", 1)) : Vertical(("#A9C8FA", 0), ("#2A5DCB", 1));
        var icon = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(path), Fill = Brushes.White, Stretch = Stretch.Uniform, Width = size * 0.5, Height = size * 0.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var disc = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = face, Child = icon, Cursor = new Cursor(StandardCursorType.Hand),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xC0, 255, 255, 255)), BorderThickness = new Thickness(1), Margin = new Thickness(2, 0),
        };
        disc.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(disc).Properties.IsLeftButtonPressed)
            {
                e.Handled = true;
                click();
            }
        };
        return disc;
    }

    private void BuildStrip()
    {
        var media = _p.CurrentMedia;
        var active = media is not null && _p.PlayState != PlayState.Stopped;
        var playing = _p.PlayState is PlayState.Playing or PlayState.ScanForward or PlayState.ScanReverse;
        var white = Brushes.White;
        var soft = new SolidColorBrush(Color.Parse("#CFE3FF"));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 6, 0) };
        buttons.Children.Add(Glossy(StopPath, 24, false, () => _p.Stop()));
        buttons.Children.Add(Glossy(WidgetIcons.Prev, 24, false, () => _p.Previous()));
        buttons.Children.Add(Glossy(playing ? WidgetIcons.Pause : WidgetIcons.Play, 30, true, () =>
        {
            if (playing)
                _p.Pause();
            else
                _p.Play();
        }));
        buttons.Children.Add(Glossy(WidgetIcons.Next, 24, false, () => _p.Next()));

        // Display: the title, then the position, the seek bar and the length.
        var name = active ? media!.Name + (Artist(media) is { Length: > 0 } a ? " - " + a : string.Empty) : "Pardeh";
        var title = new TextBlock { Text = name, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = white, TextTrimming = TextTrimming.CharacterEllipsis };
        _pos = new TextBlock { FontSize = 10, Foreground = soft, Width = 34, VerticalAlignment = VerticalAlignment.Center };
        _dur = new TextBlock { FontSize = 10, Foreground = soft, Width = 34, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        _track = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), Background = new SolidColorBrush(Color.Parse("#0E2A6B")), BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 255, 255, 255)), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        _fill = new Border { Height = 5, CornerRadius = new CornerRadius(2.5), Background = soft, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Width = 0 };
        _knob = new Ellipse { Width = 11, Height = 11, Fill = white, Stroke = new SolidColorBrush(Color.Parse("#0A2A78")), StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var bar = new Grid { Height = 16, Background = Brushes.Transparent, Children = { _track, _fill, _knob } };
        bar.PointerPressed += (_, e) =>
        {
            _dragSeek = true;
            e.Pointer.Capture(bar);
            SeekTo(bar, e);
            e.Handled = true;
        };
        bar.PointerMoved += (_, e) =>
        {
            if (_dragSeek)
                SeekTo(bar, e);
        };
        bar.PointerReleased += (_, e) =>
        {
            _dragSeek = false;
            e.Pointer.Capture(null);
        };
        var seekRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(bar, 1);
        Grid.SetColumn(_dur, 2);
        seekRow.Children.Add(_pos);
        seekRow.Children.Add(bar);
        seekRow.Children.Add(_dur);
        var display = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 190, Children = { title, seekRow } };

        // Mute and volume.
        var volIcon = new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(VolumePath), Fill = _p.Mute ? new SolidColorBrush(Color.FromArgb(0x70, 255, 255, 255)) : white, Stretch = Stretch.Uniform, Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand), Margin = new Thickness(8, 0, 4, 0) };
        volIcon.PointerPressed += (_, e) =>
        {
            _p.Mute = !_p.Mute;
            e.Handled = true;
            Dispatcher.UIThread.Post(Build);
        };
        var volTrack = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Color.Parse("#0E2A6B")), VerticalAlignment = VerticalAlignment.Center };
        var volFill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = soft, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Width = 56 * _p.Volume / 100 };
        var volBar = new Grid { Width = 56, Height = 16, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, Children = { volTrack, volFill }, Cursor = new Cursor(StandardCursorType.Hand) };
        var volDrag = false;
        void SetVolume(PointerEventArgs e)
        {
            var v = Math.Clamp(e.GetPosition(volBar).X / 56, 0, 1) * 100;
            _p.Volume = v;
            volFill.Width = 56 * v / 100;
        }

        volBar.PointerPressed += (_, e) =>
        {
            volDrag = true;
            e.Pointer.Capture(volBar);
            SetVolume(e);
            e.Handled = true;
        };
        volBar.PointerMoved += (_, e) =>
        {
            if (volDrag)
                SetVolume(e);
        };
        volBar.PointerReleased += (_, e) =>
        {
            volDrag = false;
            e.Pointer.Capture(null);
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 6, 12, 8) };
        row.Children.Add(buttons);
        row.Children.Add(display);
        row.Children.Add(volIcon);
        row.Children.Add(volBar);

        // Luna blue, a gloss over the upper half and a dark frame, like the taskbar mini player of Windows Media Player 9.
        var luna = new Grid();
        luna.Children.Add(new Border { Background = Vertical(("#4B83E8", 0), ("#1F4FB6", 0.55), ("#143A92", 1)) });
        luna.Children.Add(new Border { Height = 20, VerticalAlignment = VerticalAlignment.Top, Background = Vertical(("#55FFFFFF", 0), ("#08FFFFFF", 1)) });
        luna.Children.Add(row);
        var root = new Border { CornerRadius = new CornerRadius(7), ClipToBounds = true, Child = luna, BorderBrush = new SolidColorBrush(Color.Parse("#0A2A78")), BorderThickness = new Thickness(1), ContextFlyout = BuildMenu() };
        root.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(root).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
        Content = root;
        UpdateSeek();
    }

    private Control BackgroundLayer(bool active)
    {
        var layer = new Grid { Opacity = Math.Clamp(_s.MiniWidgetOpacity, 20, 100) / 100.0 };
        switch (_s.MiniWidgetTheme)
        {
            case 3:
                layer.Background = new SolidColorBrush(Color.Parse("#F1F4FB"));
                break;
            case 4:
                break;
            case 1:
                var a = Accent();
                layer.Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(a, 0), new GradientStop(Color.FromRgb((byte)(a.R * 0.55), (byte)(a.G * 0.55), (byte)(a.B * 0.55)), 1) },
                };
                break;
            case 2 when _cover is not null && active:
                var blur = new Image { Source = _cover, Stretch = Stretch.UniformToFill };
                RenderOptions.SetBitmapInterpolationMode(blur, BitmapInterpolationMode.HighQuality);
                layer.Children.Add(blur);
                layer.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)) });
                break;
            default:
                layer.Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.Parse("#1B2A55"), 0), new GradientStop(Color.Parse("#0B1020"), 1) },
                };
                break;
        }

        return layer;
    }

    private static Color Accent()
    {
        var s = AppSettings.Current;
        var hsv = new HsvColor(1, ((s.GlassTintHue % 360) + 360) % 360, Math.Clamp(s.GlassTintSaturation * 0.8, 0, 1), Math.Clamp(s.GlassTintBrightness * 0.6, 0.25, 0.9));
        return hsv.ToRgb();
    }

    private static string Artist(MediaItem media)
    {
        var artist = media.GetAttribute("Author");
        return artist.Length > 0 ? artist : media.GetAttribute("Album");
    }

    private void LoadCover(MediaItem? media)
    {
        if (media is null)
        {
            _cover = null;
            _coverFor = null;
            return;
        }

        if (_coverFor == media.Source)
            return;
        _coverFor = media.Source;
        _cover = null;
        try
        {
            if (_p.GetAlbumArt(media) is { Length: > 0 } data)
            {
                using var stream = new MemoryStream(data);
                _cover = Bitmap.DecodeToWidth(stream, 256);
            }
        }
        catch (Exception)
        {
            // Unreadable picture: the logo stays.
        }
    }

    private static Bitmap? _logo;

    private static Bitmap? Logo()
    {
        try
        {
            if (_logo is null && typeof(MiniWidget).Assembly.GetManifestResourceStream("pardeh.ico") is { } stream)
                _logo = new Bitmap(stream);
        }
        catch (Exception)
        {
            // No logo: an empty box.
        }

        return _logo;
    }

    private static Button IconButton(string path, Color color, double size, Action click)
    {
        var icon = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(path), Fill = new SolidColorBrush(color), Stretch = Stretch.Uniform, Width = size * 0.8, Height = size * 0.8,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var button = new Button
        {
            Content = icon, Width = size + 14, Height = size + 10, Padding = new Thickness(0), Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), Cursor = new Cursor(StandardCursorType.Hand),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        button.Click += (_, _) => click();
        return button;
    }

    // ---- Seek ----

    private void SeekTo(Control bar, PointerEventArgs e)
    {
        var width = bar.Bounds.Width;
        if (width <= 0 || _p.Duration <= 0)
            return;
        var fraction = Math.Clamp(e.GetPosition(bar).X / width, 0, 1);
        _p.CurrentPosition = _p.Duration * fraction;
        UpdateSeek();
    }

    private void UpdateSeek()
    {
        if (_pos is null || _dur is null || _fill is null || _track is null || _knob is null)
            return;
        var duration = _p.Duration;
        var position = _p.CurrentPosition;
        _pos.Text = Format(position);
        _dur.Text = Format(duration);
        var width = _track.Bounds.Width;
        var fraction = duration > 0 ? Math.Clamp(position / duration, 0, 1) : 0;
        _fill.Width = width * fraction;
        _knob.Margin = new Thickness(Math.Clamp(width * fraction - 6, 0, Math.Max(0, width - 12)), 0, 0, 0);
    }

    private static string Format(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    // ---- Customize (right-click) ----

    private MenuFlyout BuildMenu()
    {
        var flyout = new MenuFlyout();

        MenuItem Check(string header, bool on, Action<bool> set)
        {
            var item = new MenuItem { Header = header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = on };
            item.Click += (_, _) =>
            {
                set(!on);
                _s.Save();
                Dispatcher.UIThread.Post(Build);
            };
            return item;
        }

        var theme = new MenuItem { Header = "Theme" };
        var names = new[] { "Navy", "Skin colour", "Blurred cover", "Light", "Transparent" };
        for (var i = 0; i < names.Length; i++)
        {
            var index = i;
            var item = new MenuItem { Header = names[i], ToggleType = MenuItemToggleType.Radio, IsChecked = _s.MiniWidgetTheme == i, GroupName = "theme" };
            item.Click += (_, _) =>
            {
                _s.MiniWidgetTheme = index;
                _s.Save();
                Dispatcher.UIThread.Post(Build);
            };
            theme.Items.Add(item);
        }

        var opacity = new MenuItem { Header = "Opacity" };
        foreach (var value in new[] { 100, 80, 60, 40 })
        {
            var v = value;
            var item = new MenuItem { Header = v + "%", ToggleType = MenuItemToggleType.Radio, IsChecked = _s.MiniWidgetOpacity == v, GroupName = "opacity" };
            item.Click += (_, _) =>
            {
                _s.MiniWidgetOpacity = v;
                _s.Save();
                Dispatcher.UIThread.Post(Build);
            };
            opacity.Items.Add(item);
        }

        var style = new MenuItem { Header = "Style" };
        var styles = new[] { "Mini player (Windows Media Player)", "Card" };
        for (var i = 0; i < styles.Length; i++)
        {
            var index = i;
            var item = new MenuItem { Header = styles[i], ToggleType = MenuItemToggleType.Radio, IsChecked = _s.MiniWidgetStyle == i, GroupName = "style" };
            item.Click += (_, _) =>
            {
                _s.MiniWidgetStyle = index;
                _s.Save();
                Dispatcher.UIThread.Post(Build);
            };
            style.Items.Add(item);
        }

        flyout.Items.Add(style);
        flyout.Items.Add(theme);
        flyout.Items.Add(opacity);
        flyout.Items.Add(new Separator());
        flyout.Items.Add(Check("Seekbar and time", _s.MiniWidgetSeek, v => _s.MiniWidgetSeek = v));
        flyout.Items.Add(Check("Back / forward 10 seconds", _s.MiniWidgetSkip, v => _s.MiniWidgetSkip = v));
        flyout.Items.Add(Check("Shuffle", _s.MiniWidgetShuffle, v => _s.MiniWidgetShuffle = v));
        flyout.Items.Add(Check("Repeat", _s.MiniWidgetRepeat, v => _s.MiniWidgetRepeat = v));
        flyout.Items.Add(Check("Favourite heart", _s.MiniWidgetFavorite, v => _s.MiniWidgetFavorite = v));
        flyout.Items.Add(new Separator());
        flyout.Items.Add(Check("Always on top", _s.MiniWidgetTopmost, v =>
        {
            _s.MiniWidgetTopmost = v;
            Topmost = v;
        }));
        var close = new MenuItem { Header = "Close widget" };
        close.Click += (_, _) => Toggle();
        flyout.Items.Add(close);
        return flyout;
    }
}
