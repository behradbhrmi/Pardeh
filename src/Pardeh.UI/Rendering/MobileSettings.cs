using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Pardeh.Core.Library;
using Pardeh.Core.Playback;
using Path = Avalonia.Controls.Shapes.Path;

namespace Pardeh.UI.Rendering;

/// <summary>Colours of the phone settings: dark or light with the active phone skin's accent.</summary>
internal sealed record MobileTheme(bool Dark, Color Back, Color Card, Color Accent, Color? HeadTop = null, Color? HeadBottom = null, Color? HeadInk = null, bool Royal = false, bool Glass = false, string? Art = null)
{
    /// <summary>Winamp Phone: brushed-metal cards and amber highlights (see MobileTheme.Art).</summary>
    public bool Winamp => Art == "winamp";

    /// <summary>Groove Phone: acrylic cards, pill tabs and picture tiles.</summary>
    public bool Groove => Art == "groove";

    /// <summary>Zune HD: menus of large lower-case words (see <see cref="TypeRows"/>).</summary>
    public bool Zune => Art == "zune";

    /// <summary>Windows Phone 7 and Zune HD: type-first rows without cards.</summary>
    public bool TypeFirst => Zune || Art == "metro";

    /// <summary>The looks with their own drawn cards and equalizer (Winamp, Groove).</summary>
    public bool DrawnArt => Winamp || Groove;

    private static IBrush Fade(params (string Color, double At)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
        foreach (var (color, at) in stops)
            brush.GradientStops.Add(new GradientStop(Color.Parse(color), at));
        return brush;
    }

    /// <summary>The card of a group of rows on the art looks (acrylic or raised metal), or null on the others.</summary>
    public Border? ArtCard(Control child, Thickness margin) => !DrawnArt ? null : new Border
    {
        Background = Winamp ? Fade(("#1E2739", 0), ("#141B29", 0.4), ("#10161F", 1)) : Fade(("#3A8CC8FF", 0), ("#228CC8FF", 1)),
        BorderBrush = Winamp ? Fade(("#59678A", 0), ("#2B3548", 0.5), ("#2B3548", 1)) : Fade(("#66CFE6FF", 0), ("#22CFE6FF", 1)),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(Winamp ? 9 : 14), Margin = margin, ClipToBounds = true, Child = child,
    };

    /// <summary>Text on the highlighted (selected / playing) bar: dark on amber, white elsewhere.</summary>
    public Color SelectedInk => Winamp ? Color.Parse("#16100A") : Colors.White;

    /// <summary>The top bar of the phone library: a glossy blue gradient on the XP-era looks, the card colour on flat ones.</summary>
    /// <summary>
    /// Pardeh Glass' theme, in the colour of the skin colour setting (<see cref="GlassPalette"/>): the blue colours below and every
    /// <c>G(...)</c> / <see cref="GlassPalette"/> call in the phone views follow it.
    /// </summary>
    public static MobileTheme ForGlass() => new(true, GlassPalette.Parse("#071C58"), GlassPalette.Parse("#12307F"), GlassPalette.Parse("#6FC3FF"),
        GlassPalette.Parse("#2563D6"), GlassPalette.Parse("#143FA6"), Colors.White, Glass: true);

    /// <summary>A colour for the themes' own widgets: on Glass through the skin colour setting, else as written.</summary>
    public Color G(string hex) => Glass ? GlassPalette.Parse(hex) : Color.Parse(hex);

    public IBrush HeadBrush => Winamp
        ? Fade(("#2A3447", 0), ("#19202E", 0.5), ("#0F141F", 1))
        : Groove
        ? Fade(("#1552B4", 0), ("#124AA8", 1))
        : Glass
        ? Gloss(G("#4F93F5"), G("#2E6BE0"), G("#2161D3"), G("#163FAE"))
        : Royal
        ? Gloss(Color.Parse("#A9BEF2"), Color.Parse("#6C8DE0"), Color.Parse("#3A5FCD"), Color.Parse("#17327F"))
        : HeadTop is { } top && HeadBottom is { } bottom
        ? new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(top, 0), new GradientStop(bottom, 1) } }
        : new SolidColorBrush(Card);

    /// <summary>The Luna "selected" look: a glossy blue gradient (null on the other looks). Glass: a bright glossy blue bar.</summary>
    public IBrush? SelectedBrush => Winamp
        ? Fade(("#FFD36A", 0), ("#FFA414", 0.45), ("#F08C00", 1))
        : Glass
        ? Gloss(G("#7CC4FF"), G("#3C93F6"), G("#1D6BE6"), G("#1A5FD8"))
        : Royal ? Gloss(Color.Parse("#7F9FEA"), Color.Parse("#4F78DA"), Color.Parse("#2E55C4"), Color.Parse("#1D3C9C")) : null;

    public FontFamily? Face => Royal ? new FontFamily("Tahoma, Trebuchet MS, Roboto, sans-serif") : null;

    /// <summary>What a card of rows is painted with: the card colour, or on Glass translucent deep blue so the light sweeps show through.</summary>
    public IBrush CardBrush => Glass ? new SolidColorBrush(GlassPalette.Map(Color.FromArgb(150, 9, 33, 100))) : new SolidColorBrush(Card);

    /// <summary>The page body: the back colour, or on Glass a royal-blue gradient.</summary>
    public IBrush BodyBrush => Winamp
        ? Fade(("#0E1420", 0), ("#070B12", 1))
        : Groove
        ? Fade(("#124AA8", 0), ("#0D388C", 0.4), ("#0B2E78", 0.72), ("#08225C", 1))
        : Glass
        ? new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(G("#1D56CB"), 0), new GradientStop(G("#1342B3"), 0.3), new GradientStop(G("#0E3599"), 0.62), new GradientStop(G("#0A2A82"), 1) },
        }
        : new SolidColorBrush(Back);

    /// <summary>Glass only: soft light sweeps laid over the body (null on the other looks).</summary>
    public Control? Decor()
    {
        if (!Glass)
            return null;
        Path Sweep(string data, byte alpha) => new()
        {
            Data = Geometry.Parse(data), Stretch = Stretch.Fill,
            Fill = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(0, 255, 255, 255), 0), new GradientStop(Color.FromArgb(alpha, 255, 255, 255), 0.5), new GradientStop(Color.FromArgb(0, 255, 255, 255), 1) },
            },
        };
        return new Grid
        {
            IsHitTestVisible = false,
            Children =
            {
                new Border { Background = new RadialGradientBrush { Center = new RelativePoint(0.2, 0.05, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.2, 0.05, RelativeUnit.Relative), RadiusX = RelativeScalar.Parse("75%"), RadiusY = RelativeScalar.Parse("40%"), GradientStops = { new GradientStop(GlassPalette.Map(Color.FromArgb(95, 143, 200, 255)), 0), new GradientStop(GlassPalette.Map(Color.FromArgb(0, 143, 200, 255)), 1) } } },
                Sweep("M 0,0.02 C 0.35,0.1 0.7,0.3 1,0.34 L 1,0.52 C 0.7,0.46 0.35,0.26 0,0.2 Z", 30),
                Sweep("M 0,0.55 C 0.35,0.62 0.7,0.8 1,0.84 L 1,0.98 C 0.7,0.94 0.35,0.76 0,0.7 Z", 20),
            },
        };
    }

    /// <summary>
    /// The XP-era looks (Glass, Royal/Luna) draw the icons of the Library and Settings rows as small glossy pictorial objects
    /// (<see cref="Rendering.PictorialIcons"/>, no disc behind them); the modern looks keep their thin line glyphs. This is the one switch.
    /// </summary>
    public bool PictorialIcons => Glass || Royal;

    /// <summary>The pictorial icon of a row (the row's line glyph or an "@key" says which one), drawn vector at the device scale.</summary>
    public Control RowIcon(string glyphOrKey, double size = 40) =>
        Rendering.PictorialIcons.Create(Glass ? Rendering.PictorialIcons.ForGlass(Rendering.PictorialIcons.Resolve(glyphOrKey)) : glyphOrKey, size, Glass);

    /// <summary>Glass Settings rows: the pale-blue glossy family (play triangle, video frame, speaker, appearance wheel, "i" ball ...).</summary>
    public Control SettingIcon(string glyphOrKey, double size = 40) =>
        Rendering.PictorialIcons.Create(Glass ? Rendering.PictorialIcons.ForGlass(Rendering.PictorialIcons.Resolve(glyphOrKey), settings: true) : glyphOrKey, size, Glass);

    /// <summary>Glass Library rows: a glossy blue tile holding a pale glyph.</summary>
    public Control TileIcon(string glyphOrKey, double size = 56) => Rendering.PictorialIcons.Create(Rendering.PictorialIcons.TileOf(glyphOrKey), size, Glass);

    /// <summary>Glass: a large Browse icon.</summary>
    public static Control BrowseIcon(string key, double size) => Rendering.PictorialIcons.Create(key, size, glass: true);

    /// <summary>Glass: a glossy blue pill button with a light rim (Close, Reset, Add a folder ...).</summary>
    public Control GlassPill(string text, Action tap)
    {
        var pill = new Border
        {
            Tag = "pill", Height = 44, CornerRadius = new CornerRadius(22), Margin = new Thickness(16, 8), Background = Gloss(G("#7CC4FF"), G("#3C93F6"), G("#1D6BE6"), G("#1A5FD8")),
            BorderBrush = new SolidColorBrush(G("#BBDCFF")), BorderThickness = new Thickness(1.4),
            Child = new TextBlock { Text = text, FontSize = 16, FontWeight = FontWeight.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        pill.Tapped += (_, _) => tap();
        return pill;
    }

    /// <summary>Glass: blue glass switches and sliders with round silver thumbs (the controls read these theme resources).</summary>
    public void ApplyGlassControls(IResourceDictionary r)
    {
        if (!Glass)
            return;
        IBrush silver = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(G("#FFFFFF"), 0), new GradientStop(G("#DCE8F8"), 0.5), new GradientStop(G("#9DB6E2"), 1) } };
        IBrush lit = Gloss(G("#CDF1FF"), G("#58C0FF"), G("#2A93F4"), G("#1F6FE0"));
        IBrush dark = new SolidColorBrush(G("#06205E"));
        IBrush rim = new SolidColorBrush(G("#7DB7FB"));
        foreach (var key in new[] { "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed" })
            r[key] = silver;
        foreach (var key in new[] { "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed" })
            r[key] = lit;
        foreach (var key in new[] { "SliderTrackFill", "SliderTrackFillPointerOver", "SliderTrackFillPressed" })
            r[key] = dark;
        foreach (var key in new[] { "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed" })
            r[key] = lit;
        foreach (var key in new[] { "ToggleSwitchFillOff", "ToggleSwitchFillOffPointerOver", "ToggleSwitchFillOffPressed" })
            r[key] = dark;
        foreach (var key in new[] { "ToggleSwitchStrokeOn", "ToggleSwitchStrokeOnPointerOver", "ToggleSwitchStrokeOnPressed", "ToggleSwitchStrokeOff", "ToggleSwitchStrokeOffPointerOver", "ToggleSwitchStrokeOffPressed" })
            r[key] = rim;
        foreach (var key in new[] { "ToggleSwitchKnobFillOn", "ToggleSwitchKnobFillOnPointerOver", "ToggleSwitchKnobFillOnPressed", "ToggleSwitchKnobFillOff", "ToggleSwitchKnobFillOffPointerOver", "ToggleSwitchKnobFillOffPressed" })
            r[key] = silver;
    }

    private static IBrush Gloss(Color top, Color upper, Color lower, Color bottom) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(top, 0), new GradientStop(upper, 0.48), new GradientStop(lower, 0.5), new GradientStop(bottom, 1) },
    };

    public Color HeadText => HeadInk ?? Ink;

    public Color Ink => Dark ? Color.Parse("#FFFFFF") : Royal ? Color.Parse("#0B1F5C") : Color.Parse("#1A1F36");

    public Color Sub => Glass ? G("#B9D4FF") : Dark ? Color.FromArgb(170, 255, 255, 255) : Royal ? Color.Parse("#4A5C8F") : Color.Parse("#5A6280");

    public Color Line => Glass ? GlassPalette.Map(Color.FromArgb(150, 79, 137, 230)) : Dark ? Color.FromArgb(40, 255, 255, 255) : Royal ? Color.Parse("#BCCBEC") : Color.FromArgb(40, 20, 30, 80);
}

/// <summary>
/// The Settings page on a phone: a list of categories (icon, title, one-line summary, chevron); each opens a full-screen
/// sub-page of grouped cards (switches, value rows that open a picker sheet, sliders, buttons). The Android Back key steps
/// back one level (<see cref="GoBack"/>). Values come from the same <see cref="AppSettings"/> and player models as the
/// desktop Options; desktop-only settings (title bar, file types, always on top ...) are left out.
/// </summary>
internal sealed partial class MobileSettingsView : UserControl
{
    private readonly TopLevel _owner;
    private readonly MediaPlayer _player;
    private readonly MediaLibrary _library;
    private readonly ISettingsHost _host;
    private readonly MobileTheme _t;
    private readonly AppSettings _settings = AppSettings.Current;
    private readonly Stack<string> _stack = new();
    private readonly Border _header;
    private readonly TextBlock _title;
    private readonly ContentControl _body = new();
    private readonly ScrollViewer _scroller;
    private readonly Grid _sheet = new() { IsVisible = false };
    private readonly StackPanel _sheetRows = new();
    private readonly TextBlock _sheetTitle;

    public MobileSettingsView(TopLevel owner, MediaPlayer player, MediaLibrary library, ISettingsHost host, MobileTheme theme)
    {
        _owner = owner;
        _player = player;
        _library = library;
        _host = host;
        _t = theme;
        Resources["SystemAccentColor"] = theme.Accent;
        Resources["SystemAccentColorLight1"] = theme.Accent;
        Resources["SystemAccentColorDark1"] = theme.Accent;
        Resources["SliderThumbBackground"] = Brush(theme.Accent);
        Resources["SliderThumbBackgroundPointerOver"] = Brush(theme.Accent);
        Resources["SliderThumbBackgroundPressed"] = Brush(theme.Accent);
        Resources["SliderTrackValueFill"] = Brush(theme.Accent);
        Resources["SliderTrackValueFillPointerOver"] = Brush(theme.Accent);
        Resources["SliderTrackValueFillPressed"] = Brush(theme.Accent);
        Resources["ToggleSwitchFillOn"] = Brush(theme.Accent);
        Resources["ToggleSwitchFillOnPointerOver"] = Brush(theme.Accent);
        theme.ApplyGlassControls(Resources);
        if (theme.Face is { } face)
            FontFamily = face;

        _title = new TextBlock { FontSize = 18, FontWeight = _t.Royal || _t.Glass ? FontWeight.Bold : FontWeight.SemiBold, Foreground = Brush(_t.HeadText), VerticalAlignment = VerticalAlignment.Center };
        var back = new Border { Width = 56, Height = 52, Background = Brushes.Transparent, Child = Icon("M 15,4 L 7,12 L 15,20", _t.HeadText, 24, 2.4, 24) };
        back.Tapped += (_, _) => GoBack();
        var band = _t.Royal || _t.Glass || (!_t.Dark && _t.HeadInk == Colors.White); // a light theme with white header text needs its coloured band
        _header = new Border { Height = 52, IsVisible = false, Background = band ? _t.HeadBrush : Brush(_t.Back), Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { back, _title } } };

        _scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, // touch: no desktop scrollbar
        };
        // A transparent background makes the empty space between rows and lines hit-testable, so a touch drag that
        // starts there scrolls too (the About page is mostly empty space and did not scroll by touch in landscape).
        _scroller.Content = new Grid { Background = Brushes.Transparent, Children = { new Border { MaxWidth = 620, Padding = new Thickness(14, 6, 14, 24), Child = _body, HorizontalAlignment = HorizontalAlignment.Stretch } } };

        var main = new DockPanel { Children = { _header, _scroller } };
        DockPanel.SetDock(_header, Dock.Top);

        // The picker sheet: a scrim and a panel rising from the bottom.
        _sheetTitle = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Brush(_t.Ink), Margin = new Thickness(20, 16, 20, 8) };
        var scrim = new Border { Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)) };
        scrim.Tapped += (_, _) => CloseSheet();
        Control sheetTitle = _sheetTitle;
        if (_t.Glass)
            sheetTitle = new Border { Background = _t.HeadBrush, CornerRadius = new CornerRadius(15, 15, 0, 0), Child = _sheetTitle };
        var panel = new Border
        {
            Background = _t.Glass ? _t.BodyBrush : Brush(_t.Card),
            BorderBrush = _t.Glass ? Brush(_t.G("#7DB7FB")) : null,
            BorderThickness = new Thickness(_t.Glass ? 1.4 : 0),
            CornerRadius = new CornerRadius(16, 16, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
            MaxHeight = 420,
            Child = new DockPanel { Children = { sheetTitle, new ScrollViewer { Content = _sheetRows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = _t.Glass ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto } } },
        };
        DockPanel.SetDock(sheetTitle, Dock.Top);
        _sheet.Children.Add(scrim);
        _sheet.Children.Add(panel);

        var page = new Grid { Background = _t.BodyBrush };
        if (_t.Decor() is { } decor)
            page.Children.Add(decor);
        page.Children.Add(main);
        page.Children.Add(_sheet);
        Content = new ThemeVariantScope
        {
            RequestedThemeVariant = theme.Dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Child = page,
        };
        ShowRoot();
        // Dev aid (dev.env): PARDEH_SETTINGS_SUB=About opens that sub-page first, for screenshots.
        var sub = PendingPage ?? Environment.GetEnvironmentVariable("PARDEH_SETTINGS_SUB");
        PendingPage = null;
        if (sub == "About")
            Open("About", AboutPage);
        else if (sub == "Appearance")
            Open("Appearance", AppearancePage);
        else if (sub == "Equalizer")
            Open("Equalizer", EqualizerPage);
        else if (sub == "Visualization")
            Open("Visualization", VisualizationPage);
        else if (sub == "Skins")
            Open("Skins", SkinsPage);
        else if (sub == "Lyrics")
            Open("Lyrics", LyricsPage);
    }

    private static IBrush Brush(Color c) => new SolidColorBrush(c);

    /// <summary>True while a picker sheet covers the page (page swipes are ignored then).</summary>
    public bool SheetOpen => _sheet.IsVisible;

    /// <summary>Back one level: the picker, then a sub-page, then nothing (false = already at the category list).</summary>
    public bool GoBack()
    {
        if (_sheet.IsVisible)
        {
            CloseSheet();
            return true;
        }

        if (_stack.Count == 0)
            return false;
        _stack.Clear();
        ShowRoot();
        return true;
    }

    /// <summary>Goes straight to the Equalizer sub-page (the Winamp Phone's EQ key).</summary>
    public void OpenEqualizer()
    {
        CloseSheet();
        _stack.Clear();
        Open("Equalizer", EqualizerPage);
    }

    private void ShowRoot()
    {
        _header.IsVisible = false;
        Show(Root());
    }

    private void Open(string title, Func<Control> page)
    {
        _stack.Push(title);
        _title.Text = title;
        _header.IsVisible = true;
        Show(page());
    }

    private void Show(Control content)
    {
        _body.Content = content;
        _scroller.Offset = default;
    }

    // ---- Building blocks --------------------------------------------------------------------

    /// <summary>
    /// A line glyph. The modern looks draw its Fluent System Icon instead (<paramref name="selected"/> picks the filled variant);
    /// the pictorial looks keep the hand-drawn stroke.
    /// </summary>
    private Path Icon(string data, Color color, double size = 22, double thickness = 1.6, double viewBox = 20, bool selected = false)
    {
        if (!_t.PictorialIcons && FluentIcons.ForGlyph(data) is { } name)
            return FluentIcons.Create(name, color, size is > 24 and <= 28 ? 24 : size, selected);
        return LineIcon(data, color, size, thickness, viewBox);
    }

    private static Path LineIcon(string data, Color color, double size = 22, double thickness = 1.6, double viewBox = 20) => new()
    {
        Data = Geometry.Parse(data),
        Stroke = Brush(color),
        StrokeThickness = thickness * 20 / viewBox * (viewBox / 20),
        StrokeLineCap = PenLineCap.Round,
        StrokeJoin = PenLineJoin.Round,
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private TextBlock Text(string text, double size, Color color, bool bold = false) => new()
    {
        Text = text,
        FontSize = size,
        Foreground = Brush(color),
        FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        TextWrapping = TextWrapping.Wrap,
    };

    /// <summary>A rounded card holding rows separated by thin lines.</summary>
    private Control Card(params Control[] rows)
    {
        if (_t.Glass && rows.Length > 0 && rows.All(r => r.Tag as string == "pill"))
        {
            // Glass: buttons stand on the page by themselves, not inside a card.
            var pills = new StackPanel { Margin = new Thickness(-16, 0, -16, 6) };
            pills.Children.AddRange(rows);
            return pills;
        }

        if (_t.TypeFirst)
            return TypeRows.Group(rows, new Thickness(0, 0, 0, 14));

        var stack = new StackPanel();
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0)
                stack.Children.Add(new Border { Height = 1, Background = Brush(_t.Line), Margin = new Thickness(_t.Glass ? 0 : 16, 0, 0, 0) });
            stack.Children.Add(rows[i]);
        }

        if (_t.Glass) // flat: thin separators, no bordered card (a hairline top and bottom closes each group)
            return new Border { Margin = new Thickness(-14, 0, -14, 14), BorderBrush = Brush(_t.Line), BorderThickness = new Thickness(0, 1), Child = stack };
        if (_t.ArtCard(stack, new Thickness(0, 0, 0, 14)) is { } art)
            return art;
        return new Border { Background = Brush(_t.Card), CornerRadius = new CornerRadius(_t.Royal ? 5 : 12), Margin = new Thickness(0, 0, 0, 14), ClipToBounds = true, Child = stack, BorderBrush = _t.Royal ? Brush(_t.Line) : null, BorderThickness = new Thickness(_t.Royal ? 1 : 0) };
    }

    private Control Heading(string text) => _t.TypeFirst ? TypeRows.Heading(_t, text) : new TextBlock
    {
        Text = _t.Royal || _t.Glass ? text : text.ToUpperInvariant(), FontSize = 13, FontWeight = _t.Royal || _t.Glass ? FontWeight.Bold : FontWeight.SemiBold, Foreground = Brush(_t.Royal || _t.Glass ? _t.Ink : _t.Accent), Margin = new Thickness(6, 8, 0, 6),
    };

    private Control Note(string text)
    {
        var note = Text(text, 13, _t.Sub);
        note.Margin = new Thickness(6, 0, 6, 12);
        return note;
    }

    /// <summary>A touch row (at least 56 high): optional icon, title, summary, a trailing control; tapping runs <paramref name="tap"/>.</summary>
    private Control Row(string title, string? summary, Control? trailing, Action? tap, string? icon = null, double minHeight = 58)
    {
        if (_t.TypeFirst)
            return TypeRows.Setting(_t, title, summary, trailing, tap, menu: icon is not null, minHeight); // the menu rows are the ones that carry an icon elsewhere

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), MinHeight = minHeight, Background = Brushes.Transparent };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 8, 8, 8), Spacing = 2 };
        text.Children.Add(Text(title, 16, _t.Ink));
        if (!string.IsNullOrEmpty(summary))
            text.Children.Add(Text(summary, 13, _t.Sub));
        if (icon is not null)
        {
            var holder = _t.PictorialIcons
                ? new Border { Width = 46, Margin = new Thickness(10, 0, 0, 0), Child = _t.Glass ? _t.SettingIcon(icon, 34) : _t.RowIcon(icon, 36) }
                : new Border { Width = 40, Margin = new Thickness(12, 0, 0, 0), Child = Icon(icon, _t.Ink, 24, 2.0) }; // modern looks: a neutral Fluent icon, left aligned, no disc or tile
            grid.Children.Add(holder);
            text.Margin = new Thickness(4, 8, 8, 8);
        }

        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        if (trailing is not null)
        {
            trailing.VerticalAlignment = VerticalAlignment.Center;
            trailing.Margin = new Thickness(0, 0, 14, 0);
            Grid.SetColumn(trailing, 2);
            grid.Children.Add(trailing);
        }

        if (tap is not null)
            grid.Tapped += (_, _) => tap();
        return grid;
    }

    private Control Chevron() => _t.TypeFirst ? new Border() : Icon("M 7,4 L 13,10 L 7,16", _t.Sub, _t.PictorialIcons ? 16 : 20, 2);

    private Control Toggle(string title, string? summary, bool value, Action<bool> set)
    {
        // The switch is only a picture: the whole row (switch included) is one tap target. A live switch would toggle on its own
        // AND let the tap bubble to the row, which toggles it back (so a tap on the switch itself changed nothing).
        var toggle = new ToggleSwitch { IsChecked = value, OnContent = null, OffContent = null, MinWidth = 0, Margin = new Thickness(0), IsHitTestVisible = false, Focusable = false };
        toggle.IsCheckedChanged += (_, _) => set(toggle.IsChecked == true);
        return Row(title, summary, toggle, () => toggle.IsChecked = toggle.IsChecked != true);
    }

    /// <summary>A row showing the current value; tapping opens the picker sheet.</summary>
    private Control Choice(string title, string? summary, IReadOnlyList<string> options, Func<int> get, Action<int> set)
    {
        var value = Text(options.Count > 0 && get() is var i and >= 0 && i < options.Count ? options[i] : string.Empty, 14, _t.Sub);
        value.TextAlignment = TextAlignment.Right;
        value.MaxWidth = 130;
        value.TextTrimming = TextTrimming.CharacterEllipsis;
        value.TextWrapping = TextWrapping.NoWrap;
        var trailing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { value, Chevron() } };
        return Row(title, summary, trailing, () => OpenSheet(title, options, get(), index =>
        {
            set(index);
            value.Text = options[index];
        }));
    }

    private Control SliderRow(string title, double min, double max, double value, Func<double, string> format, Action<double> set)
    {
        var label = Text(format(value), 14, _t.Sub);
        label.TextAlignment = TextAlignment.Right;
        var slider = new Slider { Minimum = min, Maximum = max, Value = value, Margin = new Thickness(0, 0, 0, 0), MinHeight = 36 };
        slider.ValueChanged += (_, _) =>
        {
            label.Text = format(slider.Value);
            set(slider.Value);
        };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { Text(title, 16, _t.Ink), label } };
        Grid.SetColumn(label, 1);
        return new StackPanel { Margin = new Thickness(16, 12, 16, 8), Children = { top, slider } };
    }

    private Control ButtonRow(string text, Action action, bool accent = false)
    {
        if (_t.Glass)
            return _t.GlassPill(text, action);
        var label = Text(text, 16, accent ? _t.Accent : _t.Ink, true);
        label.TextAlignment = TextAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        var row = new Border { MinHeight = 54, Background = Brushes.Transparent, Child = label };
        row.Tapped += (_, _) => action();
        return row;
    }

    // ---- Picker sheet -----------------------------------------------------------------------

    private void OpenSheet(string title, IReadOnlyList<string> options, int current, Action<int> pick)
    {
        _sheetTitle.Text = title;
        _sheetRows.Children.Clear();
        for (var i = 0; i < options.Count; i++)
        {
            var index = i;
            var check = Icon("M 4,10 L 8,14 L 16,5", _t.Accent, 20, 2.2, selected: true);
            check.IsVisible = i == current;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 54, Background = _t.Glass && i == current ? _t.SelectedBrush : Brushes.Transparent };
            var label = Text(options[i], 16, _t.Glass ? Colors.White : i == current ? _t.Accent : _t.Ink);
            if (_t.Glass)
            {
                check.Stroke = Brushes.White;
                if (i > 0)
                    _sheetRows.Children.Add(new Border { Height = 1, Background = Brush(_t.Line), Margin = new Thickness(16, 0) });
            }
            label.Margin = new Thickness(20, 0, 8, 0);
            label.VerticalAlignment = VerticalAlignment.Center;
            check.Margin = new Thickness(0, 0, 20, 0);
            Grid.SetColumn(check, 1);
            row.Children.Add(label);
            row.Children.Add(check);
            row.Tapped += (_, _) =>
            {
                pick(index);
                CloseSheet();
            };
            _sheetRows.Children.Add(row);
        }

        _sheetRows.Children.Add(_t.Glass ? _t.GlassPill("Close", CloseSheet) : new Border { Height = 12 });
        _sheet.IsVisible = true;
    }

    private void CloseSheet() => _sheet.IsVisible = false;

    // ---- Pages ------------------------------------------------------------------------------

    private string SkinName() =>
        _host.SkinEntries().FirstOrDefault(e => e.Key == _host.CurrentSkinEntry()).Name?.Replace(" (full mode)", string.Empty) ?? "Automatic";

    private Control Root()
    {
        var eq = _player.Equalizer;
        var panel = new StackPanel();
        panel.Children.Add(Card(
            Row("Skins", SkinName(), Chevron(), () => Open("Skins", SkinsPage), "M 3,17 L 3,13 L 13,3 L 17,7 L 7,17 Z M 11,5 L 15,9"),
            Row("Pages", $"{AppSettings.Pages.Count(p => p.Key is not ("cdrip" or "burn") && !_settings.HiddenPages.Contains(p.Key)) + 2} pages shown", Chevron(), () => Open("Pages", PagesPage),
                "M 3,3 L 9,3 L 9,9 L 3,9 Z M 11,3 L 17,3 L 17,9 L 11,9 Z M 3,11 L 9,11 L 9,17 L 3,17 Z M 11,11 L 17,11 L 17,17 L 11,17 Z"),
            Row("Player", _settings.StartMode switch { "full" => "Opens in full mode", "skin" => "Opens in skin mode", _ => "Opens as you left it" }, Chevron(), () => Open("Player", PlayerPage),
                "M 4,4 L 16,4 L 16,16 L 4,16 Z M 8.5,7.5 L 12.5,10 L 8.5,12.5 Z"),
            Row("Appearance", "Fonts, menus and controls", Chevron(), () => Open("Appearance", AppearancePage),
                "M 10,3 A 7,7 0 1 0 10,17 Q 12,17 12,15 Q 12,13 14,13 L 15,13 A 7,7 0 0 0 10,3 Z"),
            Row("Visualization", VisualizationSummary(), Chevron(), () => Open("Visualization", VisualizationPage),
                "M 4,16 L 4,11 M 8,16 L 8,6 M 12,16 L 12,9 M 16,16 L 16,3")));
        panel.Children.Add(Card(
            Row("Audio", eq.CrossFade ? "Crossfade on" : "Crossfade off", Chevron(), () => Open("Audio", AudioPage),
                "M 3.5,8 L 6.5,8 L 10,5 L 10,15 L 6.5,12 L 3.5,12 Z M 13,8 Q 14.5,10 13,12 M 15,6.5 Q 17.5,10 15,13.5"),
            Row("Video", _player.Video.IsDefault ? "Default picture" : "Picture adjusted", Chevron(), () => Open("Video", VideoPage),
                "M 3,5 L 13,5 L 13,15 L 3,15 Z M 13,9 L 17,6 L 17,14 L 13,11"),
            Row("Lyrics", LyricsSummary(), Chevron(), () => Open("Lyrics", LyricsPage), "@lyrics"),
            Row("Library", $"{_library.Folders.Count} monitored folders", Chevron(), () => Open("Library", LibraryPage), "M 3,5 L 8,5 L 9.5,7 L 17,7 L 17,15 L 3,15 Z")));
        panel.Children.Add(Card(
            Row("About", AppSettings.Current.ClassicBranding ? "Windows Media Player 9.0 Mobile" : "Pardeh " + VersionText(), Chevron(), () => Open("About", AboutPage),
                "M 10,3 A 7,7 0 1 0 10.01,3 Z M 10,9 L 10,14 M 10,6 L 10,6.4")));
        return panel;
    }

    private Control SkinsPage()
    {
        var entries = _host.SkinEntries().ToList();
        var current = _host.CurrentSkinEntry();
        var panel = new StackPanel();
        var thumbs = new List<(Image Image, string Key, int Tries)>();
        var rows = new List<(string Key, Action<bool> Paint)>();
        Control SkinRow((string Key, string Name) entry)
        {
            var portrait = entry.Key.EndsWith("-phone", StringComparison.Ordinal); // phone looks are tall: show the whole screen, not a crop of its middle
            var picture = new Image { Width = portrait ? 34 : 72, Height = portrait ? 66 : 46, Stretch = portrait ? Stretch.Uniform : Stretch.UniformToFill };
            var frame = new Border { Width = portrait ? 34 : 72, Height = portrait ? 66 : 46, CornerRadius = new CornerRadius(6), ClipToBounds = true, Margin = new Thickness(portrait ? 33 : 14, 8, portrait ? 19 : 0, 8), Background = Brush(Color.FromArgb(40, 128, 128, 128)), Child = picture };
            thumbs.Add((picture, entry.Key, 0));
            var mark = Icon("M 4,10 L 8,14 L 16,5", _t.Accent, 22, 2.4, selected: true);
            var name = Text(entry.Name.Replace(" (full mode)", string.Empty), 16, _t.Ink);
            name.VerticalAlignment = VerticalAlignment.Center;
            name.Margin = new Thickness(14, 0, 8, 0);
            mark.Margin = new Thickness(0, 0, 16, 0);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), MinHeight = 62, Background = Brushes.Transparent };
            Grid.SetColumn(name, 1);
            Grid.SetColumn(mark, 2);
            row.Children.Add(frame);
            row.Children.Add(name);
            row.Children.Add(mark);
            void Paint(bool selected)
            {
                mark.IsVisible = selected;
                if (_t.SelectedBrush is { } gloss)
                {
                    row.Background = selected ? gloss : Brushes.Transparent;
                    name.Foreground = Brush(selected ? Colors.White : _t.Ink);
                    mark.Stroke = Brush(selected ? Colors.White : _t.Accent);
                }
            }

            Paint(entry.Key == current);
            rows.Add((entry.Key, Paint));
            row.Tapped += (_, _) =>
            {
                foreach (var (key, paint) in rows)
                    paint(key == entry.Key);
                _host.ApplySkinEntry(entry.Key); // applies at once: no Apply button
            };
            return row;
        }

        var phone = entries.Where(e => e.Key.EndsWith("-phone", StringComparison.Ordinal)).ToList();
        var others = entries.Where(e => !e.Key.EndsWith("-phone", StringComparison.Ordinal) && e.Key.StartsWith("fullmode:", StringComparison.Ordinal)).ToList();
        var files = entries.Where(e => !e.Key.StartsWith("fullmode:", StringComparison.Ordinal)).ToList();
        panel.Children.Add(Heading("Phone looks"));
        panel.Children.Add(Card(phone.Select(SkinRow).ToArray()));
        if (others.Count > 0)
        {
            panel.Children.Add(Heading("Other full modes"));
            panel.Children.Add(Card(others.Select(SkinRow).ToArray()));
        }

        if (files.Count > 0)
        {
            panel.Children.Add(Heading("Skins"));
            panel.Children.Add(Card(files.Select(SkinRow).ToArray()));
        }

        panel.Children.Add(Card(ButtonRow("Add a skin...", _host.AddSkin, accent: true)));

        // Thumbnails one at a time (each is a full skin being drawn); a not-yet-ready one is asked for again.
        var queue = new Queue<(Image Image, string Key, int Tries)>(thumbs);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        timer.Tick += (_, _) =>
        {
            if (queue.Count == 0 || !panel.IsVisible)
            {
                timer.Stop();
                return;
            }

            var (image, key, tries) = queue.Dequeue();
            if (_host.SkinPicture(key) is { } bitmap)
                image.Source = bitmap;
            else if (tries < 12)
                queue.Enqueue((image, key, tries + 1));
        };
        panel.AttachedToVisualTree += (_, _) => timer.Start();
        panel.DetachedFromVisualTree += (_, _) => timer.Stop();
        return panel;
    }

    private Control PagesPage()
    {
        var descriptions = new Dictionary<string, string>
        {
            ["library"] = "Browse and search your music and videos",
            ["cdrip"] = "Copy music from audio CDs",
            ["burn"] = "Copy the playlist to a drive, phone or folder",
            ["radio"] = "Internet radio stations",
            ["info"] = "Details and cover of what's playing",
        };
        var rows = AppSettings.Pages.Where(p => p.Key is not ("cdrip" or "burn")).Select(page => Toggle(page.Name, descriptions.GetValueOrDefault(page.Key), !_settings.HiddenPages.Contains(page.Key), shown =>
        {
            _settings.HiddenPages.Remove(page.Key);
            if (!shown)
                _settings.HiddenPages.Add(page.Key);
            _settings.Save();
        })).ToArray();
        return new StackPanel { Children = { Note("Turn a page off to hide it from the bottom bar. Now Playing and Settings are always shown."), Card(rows) } };
    }

    private Control PlayerPage()
    {
        var looks = PlayerView.FullModeLooks.Where(l => l.Key is "auto" || l.Key.EndsWith("-phone", StringComparison.Ordinal)).ToArray();
        string[] modes = ["The mode I used last", "Full mode", "Skin mode"];
        return new StackPanel
        {
            Children =
            {
                Card(
                    Choice("Start in", "The mode Pardeh opens in", modes, () => _settings.StartMode switch { "full" => 1, "skin" => 2, _ => 0 },
                        i => { _settings.StartMode = i switch { 1 => "full", 2 => "skin", _ => "last" }; _settings.Save(); }),
                    Choice("Full mode look", "The look of the player", looks.Select(l => l.Name).ToList(), () => Math.Max(0, Array.FindIndex(looks, l => l.Key == _settings.FullModeLook)),
                        i => { _settings.FullModeLook = looks[i].Key; _settings.Save(); })),
            },
        };
    }

    private Control AppearancePage()
    {
        Control Look(string title, string description, bool value, Action<bool> set) => Toggle(title, description, value, v => { set(v); _settings.Save(); });
        var page = new StackPanel
        {
            Children =
            {
                Note("Each item is independent. Items left off keep the classic Windows XP look."),
                Card(
                    Look("Fonts", "Segoe UI instead of Tahoma in menus and panes", _settings.ModernFont, v => _settings.ModernFont = v),
                    Look("Menus and popups", "Long-press menus and skin popups", _settings.ModernMenus, v => _settings.ModernMenus = v),
                    Look("Scrollbars and tooltips", "Classic dialogs and panes", _settings.ModernControls, v => _settings.ModernControls = v),
                    Look("Toolbars", "Media Library, Radio and other panes", _settings.ModernToolbars, v => _settings.ModernToolbars = v)),
                Note("Pardeh Glass is drawn after a classic handheld player design."),
                Card(Look("Classic branding", "The flag logo and \"Windows Media Player\" name of that design (off: Pardeh's own)", _settings.ClassicBranding, v => _settings.ClassicBranding = v)),
            },
        };
        if (_t.Glass) // the skin colour only exists on Pardeh Glass; the other looks' page stays as it was
        {
            page.Children.Insert(0, Heading("Skin color"));
            page.Children.Insert(1, SkinColorPicker());
            page.Children.Insert(2, Card(Look("Logo follows skin colour", "The Pardeh logo takes the colour chosen above (shown when Classic branding is off)", _settings.LogoFollowsSkinColor, v => _settings.LogoFollowsSkinColor = v)));
            page.Children.Insert(3, Heading("Look"));
        }

        return page;
    }

    private string VisualizationSummary() =>
        (_t.Glass ? VizStyles.Glass[VizStyles.GlassIndex(_settings.GlassVizStyle)].Name : Pardeh.Skinning.Runtime.VisualizationCatalog.All[VizStyles.ClassicIndex(_settings.PhoneVizPreset)].Title.Replace(" : ", " - "))
        + (_settings.VizAuto == "off" ? string.Empty : ", " + VizStyles.AutoModes[VizStyles.AutoIndex(_settings.VizAuto)].Name.ToLowerInvariant());

    /// <summary>Settings > Visualization: the default style (Glass: its eight styles; the other looks: the classic presets) and the automatic modes.</summary>
    private Control VisualizationPage()
    {
        var names = _t.Glass
            ? VizStyles.Glass.Select(s => s.Name).ToList()
            : Pardeh.Skinning.Runtime.VisualizationCatalog.All.Select(v => v.Title.Replace(" : ", " - ")).ToList();
        return new StackPanel
        {
            Children =
            {
                Note("The picture on the Now Playing stage. Tap the small dotted chip on the visualization (or swipe it on Pardeh Glass) to try styles with live previews."),
                Card(
                    Choice(_t.Glass ? "Default style" : "Default visualization", null, names,
                        () => _t.Glass ? VizStyles.GlassIndex(_settings.GlassVizStyle) : VizStyles.ClassicIndex(_settings.PhoneVizPreset),
                        i => VizDirector.Pick(_t.Glass, i, announce: false)),
                    Choice("Change automatically", null, VizStyles.AutoModes.Select(m => m.Name).ToList(), () => VizStyles.AutoIndex(_settings.VizAuto),
                        i => { _settings.VizAuto = VizStyles.AutoModes[i].Key; _settings.Save(); VizDirector.Reload(); })),
                Note("Automatic changes show another style without changing your default. They run only while a visualization is on screen and music plays."),
            },
        };
    }

    /// <summary>The page to open when this view is created (set before a change that rebuilds the skin, so the user lands where they were).</summary>
    public static string? PendingPage { get; set; }

    /// <summary>
    /// Pardeh Glass' skin colour: swatches (Royal blue, Teal, Emerald ...), a hue slider and saturation / brightness tweaks. A swatch applies at once;
    /// a slider applies when the finger is lifted (the skin is built again in the new colour, which takes about half a second).
    /// </summary>
    private Control SkinColorPicker()
    {
        var tint = GlassPalette.From(_settings);
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        panel.Children.Add(Note("Colors the glass chrome. Text, cover art, stars and the logo keep their colors."));

        // The swatches: round glossy discs, the chosen one ringed in white.
        var grid = new WrapPanel { Margin = new Thickness(2, 0, 2, 4), HorizontalAlignment = HorizontalAlignment.Center };
        var current = GlassPalette.PresetOf(tint);
        foreach (var preset in GlassPalette.Presets)
        {
            var color = Color.Parse(preset.Swatch);
            var selected = preset == current;
            var disc = new Border
            {
                Width = 46, Height = 46, CornerRadius = new CornerRadius(23),
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Mix(color, Colors.White, 0.45), 0), new GradientStop(color, 0.5), new GradientStop(Mix(color, Colors.Black, 0.45), 1) },
                },
                BorderBrush = Brush(selected ? Colors.White : Color.FromArgb(120, 255, 255, 255)), BorderThickness = new Thickness(selected ? 3 : 1.2),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var label = Text(preset.Name, 12, selected ? Colors.White : _t.Sub, selected);
            label.TextAlignment = TextAlignment.Center;
            label.TextWrapping = TextWrapping.NoWrap;
            var cell = new StackPanel { Width = 104, Margin = new Thickness(0, 6), Spacing = 4, Background = Brushes.Transparent, Children = { disc, label } };
            cell.Tapped += (_, _) =>
            {
                _settings.GlassTintHue = preset.Tint.Hue;
                _settings.GlassTintSaturation = preset.Tint.Saturation;
                _settings.GlassTintBrightness = preset.Tint.Brightness;
                ApplySkinColor();
            };
            grid.Children.Add(cell);
        }

        panel.Children.Add(grid);

        // A strip showing the colour the sliders make, live while dragging.
        var preview = new Border { Height = 26, CornerRadius = new CornerRadius(13), Margin = new Thickness(16, 6, 16, 4), BorderThickness = new Thickness(1.2), BorderBrush = Brush(Color.FromArgb(150, 255, 255, 255)) };
        void ShowPreview(GlassPalette.Tint t) => preview.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(GlassPalette.Transform(Color.Parse("#4F93F5"), t), 0), new GradientStop(GlassPalette.Transform(Color.Parse("#1D56CB"), t), 0.5), new GradientStop(GlassPalette.Transform(Color.Parse("#0A2A82"), t), 1) },
        };
        ShowPreview(tint);

        Control TintSlider(string title, double min, double max, double value, Func<double, string> format, Action<double> store)
        {
            var label = Text(format(value), 14, _t.Sub);
            label.TextAlignment = TextAlignment.Right;
            var slider = new Slider { Minimum = min, Maximum = max, Value = value, MinHeight = 36 };
            var dragging = false;
            slider.ValueChanged += (_, _) =>
            {
                label.Text = format(slider.Value);
                store(slider.Value);
                ShowPreview(GlassPalette.From(_settings));
            };
            // Apply when the finger is lifted: rebuilding the skin during the drag would drop the slider.
            slider.AddHandler(PointerPressedEvent, (_, _) => dragging = true, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            void Release()
            {
                if (!dragging)
                    return;
                dragging = false;
                ApplySkinColor();
            }

            slider.AddHandler(PointerReleasedEvent, (_, _) => Release(), Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            slider.AddHandler(PointerCaptureLostEvent, (_, _) => Release(), Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { Text(title, 16, _t.Ink), label } };
            Grid.SetColumn(label, 1);
            return new StackPanel { Margin = new Thickness(16, 10, 16, 4), Children = { top, slider } };
        }

        var sliders = Card(
            TintSlider("Hue", 0, 360, tint.Hue, v => FormattableString.Invariant($"{v:0}°"), v => _settings.GlassTintHue = Math.Round(v)),
            TintSlider("Saturation", 0, 160, tint.Saturation * 100, v => FormattableString.Invariant($"{v:0}%"), v => _settings.GlassTintSaturation = Math.Round(v) / 100),
            TintSlider("Brightness", 50, 150, tint.Brightness * 100, v => FormattableString.Invariant($"{v:0}%"), v => _settings.GlassTintBrightness = Math.Round(v) / 100));
        panel.Children.Add(preview);
        panel.Children.Add(sliders);
        panel.Children.Add(Card(
            Toggle("Pardeh notification card", "Replaces the system player card with Pardeh own design (blurred cover, shuffle, repeat, 10 s). Restart playback to apply", _settings.NotificationCustomCard, v => { _settings.NotificationCustomCard = v; _settings.Save(); }),
            Toggle("Colour the notification","Use this skin colour for the playback notification instead of the cover's colour", _settings.NotificationUsesAccent, v => { _settings.NotificationUsesAccent = v; _settings.Save(); })));
        panel.Children.Add(_t.GlassPill("Reset to Royal blue", () =>
        {
            _settings.GlassTintHue = GlassPalette.RoyalHue;
            _settings.GlassTintSaturation = 1;
            _settings.GlassTintBrightness = 1;
            ApplySkinColor();
        }));
        return panel;
    }

    /// <summary>Saves the skin colour; the window builds Glass again in it, so this page is reopened (Appearance) in the new view.</summary>
    private void ApplySkinColor()
    {
        if (GlassPalette.From(_settings) != GlassPalette.Current)
            PendingPage = "Appearance"; // only when the skin is going to be built again (see PlayerView.ApplySettings)
        _settings.Save();
    }

    private static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    private Control AudioPage()
    {
        var eq = _player.Equalizer;
        return new StackPanel
        {
            Children =
            {
                Heading("Playback"),
                Card(
                    Toggle("Gapless playback", "No silence between songs of the same sample rate (not used while crossfading)", _player.Gapless, v =>
                    {
                        _player.Gapless = v;
                        _settings.Gapless = v;
                        _settings.Save();
                    }),
                    Toggle("Crossfading","Blend the end of one track into the next", eq.CrossFade, v => eq.CrossFade = v),
                    SliderRow("Crossfade overlap", 0, 10, eq.CrossFadeSeconds, v => $"{v:0} s", v => eq.CrossFadeSeconds = Math.Round(v)),
                    Choice("Play speed", "Slower or faster without changing pitch", MediaPlayer.RatePresets.Select(r => r.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "×").ToList(),
                        () => Array.IndexOf(MediaPlayer.RatePresets, _player.Rate), i => _player.Rate = MediaPlayer.RatePresets[i])),
                Heading("Timer and loudness"),
                Card(
                    Choice("Sleep timer", "Pauses playback; the last seconds fade out", SleepChoices(), SleepChoice, SetSleepChoice),
                    Choice("Volume levelling", "Uses ReplayGain tags; tracks without them are measured on this device", ["Off", "Track", "Album"], () => (int)_player.ReplayGain, i =>
                    {
                        _player.ReplayGain = (ReplayGainMode)i;
                        _settings.ReplayGain = _player.ReplayGain.ToString();
                        _settings.Save();
                    })),
                Heading("Network"),
                Card(
                    Choice("Buffer", "Sound read ahead for streams and files on a server", NetworkOptions.BufferChoices.Select(s => $"{s} s").ToList(),
                        () => Math.Max(0, Array.IndexOf(NetworkOptions.BufferChoices, _settings.NetworkBufferSeconds)),
                        i => { _settings.NetworkBufferSeconds = NetworkOptions.BufferChoices[i]; _settings.Save(); }),
                    Choice("Online video quality", "For streams in several sizes: the largest picture to take", NetworkOptions.VideoHeightChoices.Select(h => $"Up to {h}p").ToList(),
                        () => Math.Max(0, Array.IndexOf(NetworkOptions.VideoHeightChoices, _settings.NetworkVideoHeight)),
                        i => { _settings.NetworkVideoHeight = NetworkOptions.VideoHeightChoices[i]; _settings.Save(); }),
                    Toggle("Reconnect after a drop", "Radio and files on a server are connected again", _settings.NetworkReconnect, v => { _settings.NetworkReconnect = v; _settings.Save(); })),
                Heading("Sound"),
                Card(
                    Row("Equalizer", eq.Bypass ? "Off" : EqualizerSettings.Presets[eq.CurrentPreset].Name, Chevron(), () => Open("Equalizer", EqualizerPage)),
                    Toggle("Auto Volume Leveling", "Keep loud and quiet tracks at a similar volume", eq.AutoVolume, v => eq.AutoVolume = v),
                    Toggle("Bass and width enhancement", "The SRS WOW effects", eq.Enhanced, v => eq.Enhanced = v),
                    Choice("Quiet Mode", "Narrow the difference between loud and soft sounds", ["Off", "Medium difference", "Little difference"], () => eq.QuietMode, i => eq.QuietMode = i)),
            },
        };
    }

    // The sleep timer as one picker: Off, the preset minutes, end of track (A-B repeat lives in the Playback tools sheet).
    private static List<string> SleepChoices() => ["Off", .. MediaPlayer.SleepPresetMinutes.Select(m => $"{m} minutes"), "End of this track"];

    private int SleepChoice()
    {
        if (_player.SleepAtEndOfTrack)
            return MediaPlayer.SleepPresetMinutes.Length + 1;
        if (_player.SleepRemaining is not { } left)
            return 0;
        // The shortest preset that still covers the time left (the timer only knows what remains).
        var at = Array.FindIndex(MediaPlayer.SleepPresetMinutes, m => left.TotalMinutes <= m);
        return at < 0 ? MediaPlayer.SleepPresetMinutes.Length : at + 1;
    }

    private void SetSleepChoice(int index)
    {
        if (index == 0)
            _player.CancelSleepTimer();
        else if (index <= MediaPlayer.SleepPresetMinutes.Length)
            _player.StartSleepTimer(TimeSpan.FromMinutes(MediaPlayer.SleepPresetMinutes[index - 1]));
        else
            _player.StartSleepAtEndOfTrack();
    }

    private Control VideoPage()
    {
        var video = _player.Video;
        return new StackPanel
        {
            Children =
            {
                Card(Toggle("Resume videos", "Carry on where a video was left (videos of a minute or more)", _player.ResumeVideos, v =>
                {
                    _player.ResumeVideos = v;
                    _settings.ResumeVideos = v;
                    _settings.Save();
                })),
                Card(
                    SliderRow("Brightness", -127, 127, video.Brightness, v => $"{v:0}", v => video.Brightness = v),
                    SliderRow("Contrast", -127, 127, video.Contrast, v => $"{v:0}", v => video.Contrast = v),
                    SliderRow("Hue", -127, 127, video.Hue, v => $"{v:0}", v => video.Hue = v),
                    SliderRow("Saturation", -127, 127, video.Saturation, v => $"{v:0}", v => video.Saturation = v)),
                Card(ButtonRow("Reset to default", () => { video.Reset(); Open("Video", VideoPage); _stack.Pop(); }, accent: true)),
            },
        };
    }

    private Control LibraryPage()
    {
        var folders = new StackPanel();
        void Refresh()
        {
            folders.Children.Clear();
            var items = _library.Folders.ToList();
            if (items.Count == 0)
                folders.Children.Add(Row("No monitored folders", "Add a folder to build your library", null, null));
            foreach (var folder in items)
            {
                var remove = Icon("M 5,5 L 15,15 M 15,5 L 5,15", _t.Sub, 18, 2);
                var removeBox = new Border { Width = 44, Height = 44, Background = Brushes.Transparent, Child = remove };
                removeBox.Tapped += (_, _) =>
                {
                    _library.RemoveFolder(folder);
                    Refresh();
                };
                var row = Row(MediaSources.FolderLabel(folder), MediaSources.IsContent(folder) || folder.StartsWith("mediastore:", StringComparison.Ordinal) ? "Chosen on this device" : folder, removeBox, null);
                folders.Children.Add(row);
            }
        }

        Refresh();
        async Task AddDevice()
        {
            await PickedPaths.AddDeviceMedia(_library);
            Refresh();
        }

        async void Add()
        {
            var picked = await _owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Add a folder to watch" });
            if (PickedPaths.Of(picked.FirstOrDefault()) is { } path)
            {
                _library.AddFolder(path);
                Refresh();
            }
        }

        return new StackPanel
        {
            Children =
            {
                Heading("Monitored folders"),
                Note("Pardeh keeps the library up to date with these folders."),
                new Border { Background = _t.CardBrush, CornerRadius = new CornerRadius(_t.Glass ? 10 : 12), Margin = new Thickness(0, 0, 0, 14), ClipToBounds = true, Child = folders },
                Card(PlatformServices.Current.HasDeviceMedia ? [ButtonRow("Add a folder...", Add, accent: true), ButtonRow("Device music", () => _ = AddDevice(), accent: true), ButtonRow("Search folders now", () => _ = _library.ScanAsync())] : [ButtonRow("Add a folder...", Add, accent: true), ButtonRow("Search folders now", () => _ = _library.ScanAsync())]),
            },
        };
    }

    /// <summary>Glass: the About panel of the design (big logo, name, version, a few lines, an OK pill), with Pardeh's own name and logo.</summary>
    /// <summary>The app version as major.minor.build (the head's &lt;Version&gt; / ApplicationDisplayVersion).</summary>
    private static string VersionText()
    {
        var v = (System.Reflection.Assembly.GetEntryAssembly() ?? typeof(MobileSettingsView).Assembly).GetName().Version;
        return v is null ? "" : FormattableString.Invariant($"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}");
    }

    private Control GlassAbout()
    {
        var version = VersionText();
        TextBlock Line(string text, double size, Color color, bool bold = false) => new()
        {
            Text = text, FontSize = size, Foreground = Brush(color), FontWeight = bold ? FontWeight.Bold : FontWeight.Normal, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2),
        };
        var ink = _t.G("#E6F0FF");
        return new StackPanel
        {
            Margin = new Thickness(0, 18, 0, 0),
            Children =
            {
                Logo(150, new Thickness(0, 0, 0, 14)),
                Line(AppSettings.Current.ClassicBranding ? "Windows Media Player" : "Pardeh", 22, Colors.White, true),
                Line(AppSettings.Current.ClassicBranding ? "Version 9.0 Mobile Edition" : "Version " + version, 14, ink),
                Line(AppSettings.Current.ClassicBranding ? "© 2006 Microsoft Corporation. All rights reserved." : "Open-source software. Decoding by FFmpeg (LGPL).", 13, ink),
                Line(AppSettings.Current.ClassicBranding ? "A personal recreation (Pardeh " + version + "). Decoding by FFmpeg (LGPL)." : "An independent tribute to the classic media players.", 13, _t.Sub),
                new Border { Height = 18 },
                _t.GlassPill("OK", () => GoBack()),
            },
        };
    }

    private Control AboutPage() => _t.Glass ? GlassAbout() : PlainAbout();

    /// <summary>The 10-band equalizer: an enable switch, the preset, vertical band sliders with dB and frequency labels, Reset and Close.</summary>
    private Control EqualizerPage()
    {
        var eq = _player.Equalizer;
        var custom = false;
        var syncing = false;
        var sliders = new List<Slider>();
        var panel = new StackPanel();

        var enable = new ToggleSwitch { IsChecked = !eq.Bypass, OnContent = null, OffContent = null, MinWidth = 0, Margin = new Thickness(0), IsHitTestVisible = false, Focusable = false }; // the row toggles it
        enable.IsCheckedChanged += (_, _) => { if (!syncing) eq.Bypass = enable.IsChecked != true; };
        panel.Children.Add(Card(Row("Enable Equalizer", null, enable, () => enable.IsChecked = enable.IsChecked != true)));

        var presetText = Text(string.Empty, 15, _t.Glass ? Colors.White : _t.Accent, true);
        presetText.VerticalAlignment = VerticalAlignment.Center;
        void ShowPreset() => presetText.Text = custom ? "Custom" : EqualizerSettings.Presets[eq.CurrentPreset].Name;
        ShowPreset();
        EqBank? bank = null; // the art looks: their own drawn sliders instead of the stock ones
        var presetNames = EqualizerSettings.Presets.Select(p => p.Name).ToList();
        void SyncSliders()
        {
            syncing = true;
            for (var band = 0; band < sliders.Count; band++)
                sliders[band].Value = eq.GetGain(band);
            enable.IsChecked = !eq.Bypass;
            syncing = false;
            bank?.InvalidateVisual();
        }

        Control PresetRow() => Row("Preset", null, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { presetText, Chevron() } }, () =>
            OpenSheet("Preset", presetNames, custom ? -1 : eq.CurrentPreset, index =>
            {
                custom = false;
                eq.CurrentPreset = index;
                eq.Bypass = false;
                ShowPreset();
                SyncSliders();
            }));
        void Reset()
        {
            custom = false;
            eq.Reset();
            ShowPreset();
            SyncSliders();
        }

        if (_t.DrawnArt)
        {
            bank = new EqBank(eq, (band, gain) =>
            {
                custom = true;
                eq.SetGain(band, gain);
                eq.Bypass = false;
                syncing = true;
                enable.IsChecked = true;
                syncing = false;
                ShowPreset();
            }, _t.Winamp ? EqLook.Amber : EqLook.Groove);
            enable.IsCheckedChanged += (_, _) => bank.InvalidateVisual();
        }

        if (_t.Winamp)
        {
            panel.Children.Add(_t.ArtCard(new Border { Padding = new Thickness(2, 4), Child = bank }, new Thickness(0, 0, 0, 10))!);

            // < preset >: the arrows step through the presets, the name opens the list.
            void Step(int by)
            {
                custom = false;
                var count = EqualizerSettings.Presets.Count;
                eq.CurrentPreset = (eq.CurrentPreset + by + count) % count;
                eq.Bypass = false;
                ShowPreset();
                SyncSliders();
            }

            Border Key(Control content, Action tap)
            {
                var key = new Border
                {
                    Height = 46, MinWidth = 52, CornerRadius = new CornerRadius(8), Background = _t.HeadBrush, BorderBrush = Brush(Color.Parse("#4A566C")), BorderThickness = new Thickness(1), Child = content,
                };
                key.Tapped += (_, _) => tap();
                return key;
            }

            presetText.HorizontalAlignment = HorizontalAlignment.Center;
            presetText.Foreground = Brush(_t.Ink);
            var back = Key(LineIcon("M 13,4 L 7,10 L 13,16", _t.Accent, 18, 2.4), () => Step(-1));
            var name = Key(presetText, () => OpenSheet("Preset", presetNames, custom ? -1 : eq.CurrentPreset, index =>
            {
                custom = false;
                eq.CurrentPreset = index;
                eq.Bypass = false;
                ShowPreset();
                SyncSliders();
            }));
            name.Margin = new Thickness(6, 0);
            var forward = Key(LineIcon("M 7,4 L 13,10 L 7,16", _t.Accent, 18, 2.4), () => Step(1));
            var stepper = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 0, 0, 12), Children = { back, name, forward } };
            Grid.SetColumn(name, 1);
            Grid.SetColumn(forward, 2);
            panel.Children.Add(stepper);
            panel.Children.Add(new Border { Child = new VuMeter(_player), Margin = new Thickness(0, 0, 0, 10) });
            panel.Children.Add(Card(SliderRow("Balance", -100, 100, _player.Balance, v => Math.Abs(v) < 0.5 ? "Centre" : v < 0 ? $"{-v:0} left" : $"{v:0} right", v => _player.Balance = Math.Round(v))));
            panel.Children.Add(Card(ButtonRow("Reset", Reset, accent: true)));
            return panel;
        }

        panel.Children.Add(Card(PresetRow()));

        if (_t.Groove)
        {
            panel.Children.Add(_t.ArtCard(new Border { Padding = new Thickness(2, 6), Child = bank }, new Thickness(0, 0, 0, 14))!);
            panel.Children.Add(Card(ButtonRow("Reset", Reset, accent: true), ButtonRow("Close", () => GoBack(), accent: true)));
            return panel;
        }

        // The bands: -14 / 0 / +14 dB on the left, ten vertical sliders, the frequencies underneath.
        string[] names = ["31", "62", "125", "250", "500", "1k", "2k", "4k", "8k", "16k"];
        var bands = new Grid { Height = 232 };
        bands.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var band = 0; band < EqualizerSettings.BandCount; band++)
            bands.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        var axis = new Grid { Width = 50, Margin = new Thickness(0, 0, 4, 0) };
        axis.Children.Add(new TextBlock { Text = "+14 dB", FontSize = 11, Foreground = Brush(_t.Sub), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) });
        axis.Children.Add(new TextBlock { Text = "0 dB", FontSize = 11, Foreground = Brush(_t.Sub), VerticalAlignment = VerticalAlignment.Center });
        axis.Children.Add(new TextBlock { Text = "-14 dB", FontSize = 11, Foreground = Brush(_t.Sub), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) });
        bands.Children.Add(axis);
        var labels = new Grid();
        labels.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (var band = 0; band < EqualizerSettings.BandCount; band++)
            labels.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        labels.Children.Add(new Border { Width = 54 });
        for (var band = 0; band < EqualizerSettings.BandCount; band++)
        {
            var index = band;
            var slider = new Slider
            {
                Orientation = Orientation.Vertical, Minimum = EqualizerSettings.MinGain, Maximum = EqualizerSettings.MaxGain, Value = eq.GetGain(band), TickFrequency = 1, IsSnapToTickEnabled = true,
                HorizontalAlignment = HorizontalAlignment.Center, Width = _t.Glass ? 40 : 26, Height = 220, VerticalAlignment = VerticalAlignment.Center,
            };
            slider.ValueChanged += (_, _) =>
            {
                if (syncing)
                    return;
                custom = true;
                eq.SetGain(index, slider.Value);
                eq.Bypass = false;
                syncing = true;
                enable.IsChecked = true;
                syncing = false;
                ShowPreset();
            };
            sliders.Add(slider);
            Grid.SetColumn(slider, band + 1);
            bands.Children.Add(slider);
            var caption = new TextBlock { Text = names[band], FontSize = 11, Foreground = Brush(_t.Sub), HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(caption, band + 1);
            labels.Children.Add(caption);
        }

        var bandCard = new Border
        {
            Background = _t.CardBrush, CornerRadius = new CornerRadius(_t.Glass ? 10 : _t.Royal ? 5 : 12), Margin = new Thickness(0, 0, 0, 14), Padding = new Thickness(10, 10, 10, 8),
            BorderBrush = _t.Glass ? Brush(_t.G("#5C95EA")) : _t.Royal ? Brush(_t.Line) : null, BorderThickness = new Thickness(_t.Glass ? 1.2 : _t.Royal ? 1 : 0),
            Child = new StackPanel { Children = { bands, labels } },
        };
        panel.Children.Add(bandCard);

        if (_t.Glass)
        {
            var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(-16, 0) };
            var reset = _t.GlassPill("Reset", Reset);
            var close = _t.GlassPill("Close", () => GoBack());
            Grid.SetColumn(close, 1);
            buttons.Children.Add(reset);
            buttons.Children.Add(close);
            panel.Children.Add(buttons);
        }
        else
        {
            panel.Children.Add(Card(ButtonRow("Reset", Reset, accent: true), ButtonRow("Close", () => GoBack(), accent: true)));
        }

        return panel;
    }

    private readonly List<LogoMark> _logos = [];
    private bool _short;

    private LogoMark Logo(double size, Thickness margin)
    {
        var logo = new LogoMark(size) { Margin = margin };
        logo.Fit(_short);
        _logos.Add(logo);
        return logo;
    }

    /// <summary>A short view (a phone in landscape) shrinks the About logo; pages built later start from the same state.</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var isShort = finalSize.Height < 300;
        if (isShort != _short)
        {
            _short = isShort;
            foreach (var logo in _logos)
                logo.Fit(isShort);
        }

        return base.ArrangeOverride(finalSize);
    }

    private Control PlainAbout() => new StackPanel
    {
        Children =
        {
            AppSettings.Current.ClassicBranding
                ? Logo(96, new Thickness(0, 12, 0, 8))
                : new Border { Height = 0 },
            Card(
                Row(AppSettings.Current.ClassicBranding ? "Windows Media Player" : "Pardeh", AppSettings.Current.ClassicBranding ? "Version 9.0 Mobile Edition" : "Every era on screen.", null, null),
                Row("Version", VersionText(), null, null),
                Row("Decoders", Pardeh.Nava.Ffmpeg.FfmpegLibrary.IsAvailable ? "FFmpeg " + Pardeh.Nava.Ffmpeg.FfmpegLibrary.Version : "Not available yet", null, null)),
            Note(AppSettings.Current.ClassicBranding ? "A personal recreation of the classic player, drawn in code. Pardeh is open source." : "An open-source tribute to the classic media players. Not affiliated with Microsoft."),
        },
    };
}

/// <summary>Pardeh's logo as a control (the Glass About page).</summary>
internal sealed class LogoMark : Control
{
    private readonly double _full;

    public LogoMark(double size)
    {
        _full = size;
        Width = Height = size;
        HorizontalAlignment = HorizontalAlignment.Center;
    }

    /// <summary>A short (landscape) view gets a smaller logo, so the page text is not pushed below the fold.</summary>
    public void Fit(bool compact) => Width = Height = compact ? Math.Round(_full * 0.5) : _full;

    public override void Render(DrawingContext context)
    {
        if (AppSettings.Current.ClassicBranding)
            Pardeh.UI.FullMode.ClassicFlag.Draw(context, new Rect(Bounds.Size));
        else
            Pardeh.UI.FullMode.PardehLogo.Draw(context, new Rect(Bounds.Size));
    }
}
