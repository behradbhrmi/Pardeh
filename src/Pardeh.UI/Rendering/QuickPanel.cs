using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Pardeh.Core.Playback;

namespace Pardeh.UI.Rendering;

/// <summary>
/// Ember's floating quick panel (after the VLC Redesign by Juxtopposed, CC BY 4.0): a dark rounded card over the stage
/// with Playback, Video, Audio and Subtitles tabs, an orange underline on the open tab, orange switches and sliders,
/// and "Show Preferences" at the foot. Opened by the bottom bar's settings, audio and captions buttons.
/// </summary>
internal sealed class QuickPanel : UserControl
{
    // The palette is Ember's; a Pardeh Glass phone sets the blue glass one when it builds its panel (only one panel exists at a time).
    private static IBrush Card = Brush("#141414");
    private static IBrush Line = Brush("#262626");
    private static IBrush Ink = Brush("#E6E6E6");
    private static IBrush Dim = Brush("#8C8C8C");
    private static IBrush Accent = Brush("#E5801F");
    private static bool GlassMode;
    private static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private readonly MediaPlayer _player;
    private readonly Action _loadAudio, _loadSubtitles, _showPreferences;
    private readonly Func<bool>? _vizShown;
    private readonly Action<bool>? _setViz;
    private readonly ContentControl _page = new();
    private readonly List<(string Key, Border Underline, TextBlock Label, TextBlock Icon)> _tabs = [];

    public string Tab { get; private set; } = "playback";

    /// <summary>Pardeh Glass phone: blue glass, full width at the bottom, a Close pill instead of "Show Preferences".</summary>
    public bool Glass { get; }

    public QuickPanel(MediaPlayer player, string tab, Action loadAudio, Action loadSubtitles, Action showPreferences,
        Func<bool>? vizShown = null, Action<bool>? setViz = null, bool glass = false, Action? close = null)
    {
        Glass = glass;
        GlassMode = glass;
        Card =Brush(glass ? GlassPalette.Map("#0A2569") : "#141414");
        Line = Brush(glass ? GlassPalette.Map("#4F89E6") : "#262626");
        Ink = Brush(glass ? "#FFFFFF" : "#E6E6E6");
        Dim = Brush(glass ? GlassPalette.Map("#B9D4FF") : "#8C8C8C");
        Accent = Brush(glass ? GlassPalette.Map("#6FC3FF") : "#E5801F");
        var theme = MobileTheme.ForGlass(); // (used only by the Glass look; the colours follow the skin colour setting)
        _player = player;
        _loadAudio = loadAudio;
        _loadSubtitles = loadSubtitles;
        _showPreferences = showPreferences;
        _vizShown = vizShown;
        _setViz = setViz;

        // Orange accents for the Fluent switches and sliders inside the card.
        foreach (var key in new[] { "ToggleSwitchFillOn", "ToggleSwitchFillOnPointerOver", "ToggleSwitchFillOnPressed",
                     "SliderTrackValueFill", "SliderTrackValueFillPointerOver", "SliderTrackValueFillPressed",
                     "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "SliderThumbBackgroundPressed" })
            Resources[key] = Accent;
        if (glass)
            theme.ApplyGlassControls(Resources);
        UiTheme.UseFluentScrolling(this);

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), Height = 50 };
        var tabs = new[] { ("playback", "", "Playback"), ("video", "", "Video"), ("audio", "", "Audio"), ("subtitles", "", "Subtitles") };
        for (var i = 0; i < tabs.Length; i++)
        {
            var (key, glyph, name) = tabs[i];
            var icon = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0), IsVisible = !glass };
            var label = new TextBlock { Text = name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            var underline = new Border { Height = 2, Background = Accent, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(14, 0) };
            var cell = new Grid
            {
                Background = Brushes.Transparent,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                Children =
                {
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { icon, label } },
                    underline,
                },
            };
            cell.PointerPressed += (_, _) => Show(key);
            Grid.SetColumn(cell, i);
            header.Children.Add(cell);
            _tabs.Add((key, underline, label, icon));
        }

        var preferences = new Button
        {
            Content = "Show Preferences",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Background = Brush("#1C1C1C"),
            Foreground = Ink,
            Margin = new Thickness(18, 8, 18, 16),
            Padding = new Thickness(0, 8),
        };
        preferences.Click += (_, _) => _showPreferences();
        Control headerHost = header;
        Control footer = preferences;
        if (glass)
        {
            headerHost = new Border { Background = theme.HeadBrush, Child = header };
            footer = theme.GlassPill("Close", close ?? (() => IsVisible = false));
        }

        DockPanel.SetDock(headerHost, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        var separator = new Border { Height = 1, Background = Line };
        DockPanel.SetDock(separator, Dock.Top);

        Content = new ThemeVariantScope
        {
            RequestedThemeVariant = ThemeVariant.Dark,
            Child = new Border
            {
                Background = glass ? theme.BodyBrush : Card,
                CornerRadius = new CornerRadius(glass ? 14 : 10),
                BorderBrush = glass ? Brush(GlassPalette.Map("#7DB7FB")) : Line,
                BorderThickness = new Thickness(glass ? 1.5 : 1),
                ClipToBounds = true,
                Child = new DockPanel
                {
                    Children =
                    {
                        headerHost,
                        separator,
                        footer,
                        new ScrollViewer { Content = _page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
                    },
                },
            },
        };
        Show(tab);
    }

    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));

    public void Show(string tab)
    {
        Tab = tab;
        foreach (var (key, underline, label, icon) in _tabs)
        {
            var on = key == tab;
            underline.IsVisible = on;
            label.Foreground = on ? Accent : Dim;
            icon.Foreground = on ? Accent : Dim;
        }

        _page.Content = tab switch
        {
            "video" => VideoPage(),
            "audio" => AudioPage(),
            "subtitles" => SubtitlesPage(),
            _ => PlaybackPage(),
        };
    }

    // ---- Building blocks --------------------------------------------------------------------

    private static StackPanel Page() => new() { Margin = new Thickness(24, 14, 24, 8), Spacing = 4 };

    private static TextBlock Caption(string text) => new() { Text = text, Foreground = Dim, FontSize = 13, Margin = new Thickness(0, 10, 0, 6) };

    private static Control Row(string title, Control control, bool enabled = true)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 46, IsEnabled = enabled };
        grid.Children.Add(new TextBlock { Text = title, Foreground = enabled ? Ink : Dim, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center });
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private static ToggleSwitch Switch(bool value, Action<bool> set)
    {
        var toggle = new ToggleSwitch { IsChecked = value, OnContent = "ON", OffContent = "OFF", MinWidth = 0 };
        toggle.IsCheckedChanged += (_, _) => set(toggle.IsChecked == true);
        return toggle;
    }

    /// <summary>A track or action row: an orange check for the current one (as in the redesign's track lists).</summary>
    private static Control Choice(string text, bool current, Action pick)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("34,*"),
            Height = 40,
            Background = Brushes.Transparent,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Children =
            {
                new TextBlock { Text = current && GlassMode ? "✓" : current ?"" : string.Empty, FontFamily = GlassMode ? FontFamily.Default : Icons, FontSize = 13, Foreground = Accent, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = text, Foreground = Ink, FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, [Grid.ColumnProperty] = 1 },
            },
        };
        var hover = new Border { CornerRadius = new CornerRadius(6), Child = row, Padding = new Thickness(4, 0) };
        hover.PointerEntered += (_, _) => hover.Background = Brush("#1E1E1E");
        hover.PointerExited += (_, _) => hover.Background = Brushes.Transparent;
        hover.PointerPressed += (_, _) => pick();
        return hover;
    }

    private static Control Divider() => new Border { Height = 1, Background = Line, Margin = new Thickness(-24, 10, -24, 4) };

    // ---- Pages ------------------------------------------------------------------------------

    private Control PlaybackPage()
    {
        var eq = _player.Equalizer;
        var page = Page();
        if (_vizShown is not null && _setViz is not null)
            page.Children.Add(Row("Audio Visualizer", Switch(_vizShown(), _setViz)));
        page.Children.Add(Row("Crossfading", Switch(eq.CrossFade, v => eq.CrossFade = v)));
        page.Children.Add(Row("Auto Volume Leveling", Switch(eq.AutoVolume, v => eq.AutoVolume = v)));
        page.Children.Add(Row("Bass and Width (SRS WOW)", Switch(eq.Enhanced, v => eq.Enhanced = v)));
        var quiet = new ComboBox { ItemsSource = new[] { "Off", "Medium difference", "Little difference" }, SelectedIndex = eq.QuietMode, MinWidth = 190 };
        quiet.SelectionChanged += (_, _) => { if (quiet.SelectedIndex >= 0) eq.QuietMode = quiet.SelectedIndex; };
        page.Children.Add(Row("Quiet Mode", quiet));

        // Volume levelling from the ReplayGain tags.
        var levelling = new ComboBox { ItemsSource = new[] { "Off", "Track", "Album" }, SelectedIndex = (int)_player.ReplayGain, MinWidth = 190 };
        levelling.SelectionChanged += (_, _) =>
        {
            if (levelling.SelectedIndex < 0)
                return;
            _player.ReplayGain = (ReplayGainMode)levelling.SelectedIndex;
            AppSettings.Current.ReplayGain = _player.ReplayGain.ToString();
            AppSettings.Current.Save();
        };
        page.Children.Add(Row("Volume Levelling", levelling));

        // Sleep timer: pauses after the chosen time (the volume fades out first) or at the end of the track.
        string[] sleepNames = ["Off", .. MediaPlayer.SleepPresetMinutes.Select(m => m + " minutes"), "End of this track"];
        var sleep = new ComboBox
        {
            ItemsSource = sleepNames,
            SelectedIndex = _player.SleepAtEndOfTrack ? sleepNames.Length - 1 : _player.SleepRemaining is { } left ? Math.Max(1, Array.FindIndex(MediaPlayer.SleepPresetMinutes, m => m >= left.TotalMinutes - 0.01) + 1) : 0,
            MinWidth = 190,
        };
        sleep.SelectionChanged += (_, _) =>
        {
            var index = sleep.SelectedIndex;
            if (index <= 0)
                _player.CancelSleepTimer();
            else if (index == sleepNames.Length - 1)
                _player.StartSleepAtEndOfTrack();
            else
                _player.StartSleepTimer(TimeSpan.FromMinutes(MediaPlayer.SleepPresetMinutes[index - 1]));
        };
        page.Children.Add(Row("Sleep Timer", sleep));

        // Speed: a slider with stops at the player's preset rates from 0.25× to 2×.
        var rates = MediaPlayer.RatePresets.Where(r => r <= 2).ToArray();
        var value = new TextBlock { Foreground = Ink, FontSize = 13.5, Text = _player.Rate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x", MinWidth = 60, TextAlignment = TextAlignment.Right };
        page.Children.Add(Row("Speed", new Border { BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 5), Child = value }));
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = rates.Length - 1,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            Value = Math.Max(0, Array.IndexOf(rates, _player.Rate)),
        };
        slider.ValueChanged += (_, _) =>
        {
            var rate = rates[(int)Math.Round(slider.Value)];
            _player.Rate = rate;
            value.Text = rate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x";
        };
        page.Children.Add(slider);
        var ticks = new Grid();
        for (var i = 0; i < rates.Length; i++)
            ticks.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var i = 0; i < rates.Length; i++)
        {
            var tick = new TextBlock
            {
                Text = rates[i].ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                Foreground = Dim,
                FontSize = 11,
                HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : i == rates.Length - 1 ? HorizontalAlignment.Right : HorizontalAlignment.Center,
            };
            Grid.SetColumn(tick, i);
            ticks.Children.Add(tick);
        }

        page.Children.Add(ticks);
        return page;
    }

    private Control VideoPage()
    {
        var video = _player.Video;
        var page = Page();
        Control Adjust(string name, Func<double> get, Action<double> set)
        {
            var slider = new Slider { Minimum = -127, Maximum = 127, Value = get(), MinWidth = 200 };
            slider.ValueChanged += (_, _) => set(slider.Value);
            return Row(name, slider);
        }

        page.Children.Add(Caption("Picture"));
        page.Children.Add(Adjust("Brightness", () => video.Brightness, v => video.Brightness = v));
        page.Children.Add(Adjust("Contrast", () => video.Contrast, v => video.Contrast = v));
        page.Children.Add(Adjust("Hue", () => video.Hue, v => video.Hue = v));
        page.Children.Add(Adjust("Saturation", () => video.Saturation, v => video.Saturation = v));
        page.Children.Add(Divider());
        page.Children.Add(Choice("Reset picture", false, () => { video.Reset(); Show("video"); }));
        if (!_player.HasVideo)
            page.Children.Add(Caption("These apply to video; nothing with a picture is playing now."));
        return page;
    }

    private Control AudioPage()
    {
        var page = Page();
        page.Children.Add(Caption("Embedded Tracks"));
        var tracks = _player.AudioTracks;
        if (tracks.Count == 0)
            page.Children.Add(Choice("None", true, () => { }));
        for (var i = 0; i < tracks.Count; i++)
        {
            var index = i;
            page.Children.Add(Choice(tracks[i].Label, i == _player.CurrentAudioTrack, () => { _player.SelectAudioTrack(index); Show("audio"); }));
        }

        page.Children.Add(Divider());
        page.Children.Add(Caption("External Tracks"));
        page.Children.Add(Choice("Load an audio file (another language or dub)...", false, _loadAudio));
        return page;
    }

    private Control SubtitlesPage()
    {
        var page = Page();
        page.Children.Add(Row("Show Subtitles", Switch(_player.ShowSubtitles, v => _player.ShowSubtitles = v)));
        page.Children.Add(Caption("Current Track"));
        page.Children.Add(Choice(_player.Subtitles is null ? "None" : "Loaded subtitles", true, () => { }));
        page.Children.Add(Divider());
        page.Children.Add(Caption("External Tracks"));
        page.Children.Add(Choice("Load a subtitle file (.srt, .vtt, .ass)...", false, _loadSubtitles));
        return page;
    }
}
