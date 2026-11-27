using Avalonia;
using Avalonia.Controls;
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

/// <summary>What the phone library needs from the window.</summary>
internal sealed record MobileLibraryHost(
    Func<TopLevel?> Top,
    Func<PlaylistStore?> Playlists,
    Action OpenNowPlaying,
    Action OpenRadio,
    Action<string> EditTags);

/// <summary>
/// The Library page of the phone skins, a touch library over the real library data (<see cref="MediaLibrary"/>, the
/// <see cref="PlaylistStore"/>): a home of category rows with counts, Music &gt; Songs / Albums (a two-column grid of cover
/// tiles) / Artists / Genres, playlists, auto playlists, recently added / played; touch song rows with a menu (the three
/// dots or a long press); search in the top bar; a mini now-playing strip. The back arrow and Android Back go up one level
/// (<see cref="GoBack"/>). Colours come from the phone skin's <see cref="MobileTheme"/>.
/// </summary>
internal sealed partial class MobileLibraryView : UserControl
{
    private const string NoteIcon = "M 8,15 L 8,5 L 15,3 L 15,13 M 5.5,15 a 2.5,2 0 1,0 5,0 a 2.5,2 0 1,0 -5,0 M 12.5,13 a 2.5,2 0 1,0 5,0 a 2.5,2 0 1,0 -5,0";

    private readonly MediaPlayer _player;
    private readonly MediaLibrary _library;
    private readonly MobileLibraryHost _host;
    private readonly MobileTheme _t;
    private readonly List<(string Title, Func<Control> Build)> _levels = [];
    private readonly ContentControl _body = new() { Background = Brushes.Transparent }; // transparent but hit-testable: a drag that starts on empty space scrolls
    private readonly Border _head;
    private readonly TextBlock _title;
    private readonly Border _back;
    private readonly Border _searchButton;
    private readonly TextBox _searchBox;
    private readonly Border _mini;
    private readonly TextBlock _miniTitle, _miniSub;
    private readonly Border _miniCover = new() { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private string? _miniCoverOf;
    private readonly Path _miniIcon;
    private readonly Grid _sheet = new() { IsVisible = false };
    private readonly StackPanel _sheetRows = new();
    private readonly TextBlock _sheetTitle;
    private bool _searching;
    private bool _compact; // short view (phone in landscape): slimmer header, no mini strip, so the list gets the room
    private int _homeTab; // Glass: 0 Music, 1 Videos, 2 Pictures
    private bool _albumGrid = true; // Glass: albums as a cover grid (the bottom bar's last pill switches to a list)
    private string? _pickedPlaylist; // Glass playlists page: the selected row
    private readonly Grid _tabStrip = new() { Background = new SolidColorBrush(GlassPalette.Parse("#0B2D88")), IsVisible = false }; // only Glass shows it
    private Border? _foot;
    private Border? _sheetPanel;
    private Control? _noteArt;

    protected override Size ArrangeOverride(Size finalSize)
    {
        var compact = finalSize.Height < 300;
        if (compact != _compact)
        {
            _compact = compact;
            _head.Height = compact ? 40 : 52;
            UpdateGlassChrome();
            UpdateMini();
        }

        return base.ArrangeOverride(finalSize);
    }

    public MobileLibraryView(MediaPlayer player, MediaLibrary library, MobileLibraryHost host, MobileTheme theme)
    {
        _player = player;
        _library = library;
        _host = host;
        _t = theme;
        Resources["SystemAccentColor"] = theme.Accent;
        theme.ApplyGlassControls(Resources);
        if (theme.Face is { } face)
            FontFamily = face;

        var headInk = Brush(_t.HeadText);
        _title = new TextBlock { FontSize = 19, FontWeight = _t.Royal || _t.Glass ? FontWeight.Bold : FontWeight.SemiBold, Foreground = headInk, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        _back = new Border { Width = 52, Height = 52, Background = Brushes.Transparent, IsVisible = false, Child = Icon("M 15,4 L 7,12 L 15,20", _t.HeadText, 24, 2.4, 24) };
        _back.Tapped += (_, _) => GoBack();
        _searchButton = new Border { Width = 52, Height = 52, Background = Brushes.Transparent, Child = _t.PictorialIcons ? _t.RowIcon("@search", 30) : Icon("M 9,9 m -5.5,0 a 5.5,5.5 0 1,0 11,0 a 5.5,5.5 0 1,0 -11,0 M 13,13 L 18,18", _t.HeadText, 24, 2.2, 24) };
        _searchButton.Tapped += (_, _) => SetSearching(true);
        _searchBox = new TextBox
        {
            PlaceholderText = "Search music and videos", FontSize = 16, MinHeight = 40, VerticalContentAlignment = VerticalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0), IsVisible = false,
            Background = Brush(_t.Glass ? _t.G("#06205E") : _t.Dark ? Color.FromArgb(60, 255, 255, 255) : Colors.White), Foreground = Brush(_t.Dark ? Colors.White : Color.Parse("#1A1F36")), CornerRadius = new CornerRadius(20),
        };
        if (_t.Glass)
        {
            // The hint on the dark glass field: pale blue (the default grey was barely readable on #06205E).
            var hint = Brush(_t.G("#C4DDFB"));
            _searchBox.PlaceholderForeground = hint;
            foreach (var key in new[] { "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused", "TextControlPlaceholderForegroundDisabled" })
                Resources[key] = hint;
            foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused" })
                Resources[key] = Brush(_t.G("#06205E"));
            _searchBox.BorderBrush = Brush(_t.G("#7DB7FB"));
            _searchBox.BorderThickness = new Thickness(1.3);
            if (GlassPalette.Active) // the focus ring is the system accent (blue); in another skin colour it follows the colour (Royal keeps its look)
                Resources["TextControlBorderBrushFocused"] = Brush(_t.G("#BBDCFF"));
        }

        _searchBox.TextChanged += (_, _) => { if (_searching) OnSearchText(); };
        var headGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var titleHolder = new Grid { Children = { _title, _searchBox } };
        Grid.SetColumn(titleHolder, 1);
        Grid.SetColumn(_searchButton, 2);
        headGrid.Children.Add(_back);
        headGrid.Children.Add(titleHolder);
        headGrid.Children.Add(_searchButton);
        _head = new Border { Height = 52, Background = _t.HeadBrush, Child = headGrid, BorderBrush = Brush(_t.Line), BorderThickness = new Thickness(0, 0, 0, 1) };

        _miniTitle = new TextBlock { FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Brush(_t.Ink), TextTrimming = TextTrimming.CharacterEllipsis };
        _miniSub = new TextBlock { FontSize = 13, Foreground = Brush(_t.Sub), TextTrimming = TextTrimming.CharacterEllipsis };
        _miniIcon = Icon("M 7,4 L 17,12 L 7,20 Z", MiniInk, 26, 2, 24, selected: true);
        var miniPlay = new Border { Width = 56, Height = 56, Background = Brushes.Transparent, Child = _miniIcon };
        miniPlay.Tapped += (_, _) =>
        {
            if (_player.PlayState == PlayState.Playing)
                _player.Pause();
            else
                _player.Play();
        };
        var miniText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 8, 0), Children = { _miniTitle, _miniSub } };
        var miniGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Children = { miniText, miniPlay } };
        Grid.SetColumn(miniPlay, 1);
        _mini = new Border { Height = 56, IsVisible = false, Background = _t.Glass ? _t.SelectedBrush : Brush(_t.Card), BorderBrush = Brush(_t.Glass ? _t.G("#BBDCFF") : _t.Accent), BorderThickness = new Thickness(0, _t.Glass ? 1.3 : 2, 0, 0), Child = miniGrid };
        _mini.Tapped += (_, _) => _host.OpenNowPlaying();
        if (_t.Winamp)
            _mini.Background = _t.HeadBrush;
        if (_t.Groove)
        {
            // Groove: the mini player carries the track's picture tile.
            _mini.Height = 60;
            _mini.Background = Brush(Color.Parse("#1A4AA6"));
            _mini.BorderBrush = Brush(Color.FromArgb(70, 170, 215, 255));
            _mini.BorderThickness = new Thickness(0, 1, 0, 0);
            miniGrid.ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto");
            Grid.SetColumn(miniText, 1);
            Grid.SetColumn(miniPlay, 2);
            miniText.Margin = new Thickness(10, 0, 8, 0);
            miniGrid.Children.Add(_miniCover);
        }

        _scroll = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, Content = _body };
        var scroll = BuildScrollArea();
        var main = new DockPanel { Children = { _head, _mini, scroll } };
        DockPanel.SetDock(_head, Dock.Top);
        DockPanel.SetDock(_mini, Dock.Bottom);
        if (_t.Glass)
        {
            // Glass: a Music / Videos / Pictures strip under the header and a bar of glass pills (back, search, list or grid) at the bottom.
            main.Children.Insert(1, _tabStrip);
            DockPanel.SetDock(_tabStrip, Dock.Top);
            _foot = BuildFoot();
            main.Children.Insert(2, _foot);
            DockPanel.SetDock(_foot, Dock.Bottom);
        }

        _sheetTitle = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Brush(_t.Ink), Margin = new Thickness(20, 16, 20, 8), TextTrimming = TextTrimming.CharacterEllipsis };
        var scrim = new Border { Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)) };
        scrim.Tapped += (_, _) => _sheet.IsVisible = false;
        Control sheetTitle = _sheetTitle;
        if (_t.Glass)
            sheetTitle = new Border { Background = _t.HeadBrush, CornerRadius = new CornerRadius(15, 15, 0, 0), Child = _sheetTitle };
        var panel = new Border
        {
            Background = _t.Glass ? _t.BodyBrush : Brush(_t.Card), CornerRadius = new CornerRadius(16, 16, 0, 0), VerticalAlignment = VerticalAlignment.Bottom, MaxHeight = 460,
            BorderBrush = _t.Glass ? Brush(_t.G("#7DB7FB")) : null, BorderThickness = new Thickness(_t.Glass ? 1.4 : 0),
            Child = new DockPanel { Children = { sheetTitle, new ScrollViewer { Content = _sheetRows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = _t.Glass ? Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto } } },
        };
        DockPanel.SetDock(sheetTitle, Dock.Top);
        _sheetPanel = panel;
        _sheet.Children.Add(scrim);
        _sheet.Children.Add(panel);

        var page = new Grid { Background = _t.BodyBrush };
        if (_t.Decor() is { } decor)
            page.Children.Add(decor);
        if (_t.Glass)
        {
            // A big glossy translucent 3D music note over light-swirl streaks behind the home list, as in the design.
            _noteArt = new GlassBackdrop { IsHitTestVisible = false };
            page.Children.Add(_noteArt);
        }

        page.Children.Add(main);
        page.Children.Add(_sheet);
        Content = new ThemeVariantScope
        {
            RequestedThemeVariant = theme.Dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Child = page,
        };

        _levels.Add((_t.Glass ? "Media Library" : string.Empty, Home)); // the skin's top bar already says "Library" (Glass shows its own title bar)
        Render();
        // Dev aid (dev.env): PARDEH_LIBRARY_LEVEL=Playlists|Albums opens that level first, for screenshots.
        switch (Environment.GetEnvironmentVariable("PARDEH_LIBRARY_LEVEL"))
        {
            case "Playlists":
                Push("Playlists", _t.Glass ? GlassPlaylistsPage : PlaylistsPage);
                break;
            case "Albums":
                Push("Albums", AlbumsPage);
                break;
            case "Songs":
                Push("All Music", () => SongList(Music(), "No songs yet."));
                break;
            case "Artists":
                Push("Artists", () => GroupPage(Music(), i => i.Artist, "Unknown artist", "@person"));
                break;
            case "Favourites":
                Push("Favourites", FavoritesPage);
                break;
            case "Recent":
                Push("Recently added", () => SongList(_library.Items.OrderByDescending(i => i.Modified).Take(60).ToList(), "Nothing added yet.", "recent-added"));
                break;
        }

        _library.Changed += (_, _) => Dispatcher.UIThread.Post(() => { if (!_searching && _levels.Count == 1) Render(_scroll.Offset.Y); });
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => UpdateMini();
        EventHandler changed = (_, _) => Dispatcher.UIThread.Post(RepaintPlayingRows);
        EventHandler favoritesChanged = (_, _) => Dispatcher.UIThread.Post(OnFavoritesChanged);
        AttachedToVisualTree += (_, _) => { timer.Start(); _player.CurrentMediaChanged += changed; _player.FavoritesChanged += favoritesChanged; RepaintPlayingRows(); };
        DetachedFromVisualTree += (_, _) => { timer.Stop(); _player.CurrentMediaChanged -= changed; _player.FavoritesChanged -= favoritesChanged; };
    }

    private readonly List<(string Path, Action<bool> Paint)> _rowPaint = [];

    /// <summary>Marks the playing track's row in the visible song lists (the player's track changed).</summary>
    private void RepaintPlayingRows()
    {
        var source = _player.CurrentMedia?.Source;
        foreach (var (path, paint) in _rowPaint.ToList())
            paint(string.Equals(path, source, StringComparison.OrdinalIgnoreCase));
    }

    private static IBrush Brush(Color c) => new SolidColorBrush(c);

    private Color MiniInk => _t.Glass ? Colors.White : _t.Accent;

    /// <summary>A line glyph; the modern looks draw its Fluent System Icon instead (the pictorial looks keep the hand-drawn stroke).</summary>
    private Path Icon(string data, Color color, double size = 22, double thickness = 1.7, double viewBox = 20, bool selected = false)
    {
        if (!_t.PictorialIcons && FluentIcons.ForGlyph(data) is { } name)
            return FluentIcons.Create(name, color, size is > 24 and <= 28 ? 24 : size, selected); // 24 dp is the icon's own size; bigger ones are art
        return LineIcon(data, color, size, thickness, viewBox);
    }

    private static Path LineIcon(string data, Color color, double size = 22, double thickness = 1.7, double viewBox = 20) => new()
    {
        Data = Geometry.Parse(data), Stroke = Brush(color), StrokeThickness = thickness, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
        Width = size, Height = size, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };

    private TextBlock Text(string text, double size, Color color, bool bold = false) => new()
    {
        Text = text, FontSize = size, Foreground = Brush(color), FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap,
    };

    // ---- Navigation ---------------------------------------------------------------------------

    /// <summary>True while a long-press / picker sheet covers the page (page swipes are ignored then).</summary>
    public bool SheetOpen => _sheet.IsVisible;

    /// <summary>Up one level: the menu, the search, then a sub-page. False at the home page.</summary>
    public bool GoBack()
    {
        if (_sheet.IsVisible)
        {
            _sheet.IsVisible = false;
            return true;
        }

        if (_searching)
        {
            SetSearching(false);
            return true;
        }

        if (_levels.Count <= 1)
            return false;
        _levels.RemoveAt(_levels.Count - 1);
        var back = _offsets.Count > _levels.Count ? _offsets[_levels.Count] : 0;
        _offsets.RemoveRange(_levels.Count, _offsets.Count - _levels.Count);
        Render(back);
        return true;
    }

    private void Push(string title, Func<Control> build)
    {
        RememberOffset();
        _levels.Add((title, build));
        _offsets.Add(0);
        Render();
    }

    // ---- Scroll position and the jump buttons -------------------------------------------------------

    private ScrollViewer _scroll = null!;
    private Border _jumpTop = null!, _jumpEnd = null!, _jumpCapsule = null!;
    private readonly List<double> _offsets = [0]; // the scroll offset each open level was left at (parallel to _levels)

    private void RememberOffset()
    {
        if (_offsets.Count == _levels.Count)
            _offsets[^1] = _scroll.Offset.Y;
    }

    /// <summary>Puts a page back where it was left (after Back from a playlist, an album ...): once the page has been laid out.</summary>
    private void RestoreOffset(double y)
    {
        if (y <= 1)
        {
            _scroll.Offset = default;
            return;
        }

        void Apply()
        {
            _scroll.UpdateLayout();
            _scroll.Offset = new Vector(0, Math.Min(y, Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height)));
        }

        Apply();
        // A long list with covers can still be growing after the first layout: once more when it has settled.
        Dispatcher.UIThread.Post(Apply, DispatcherPriority.Loaded);
    }

    /// <summary>The page's scroll viewer with two jump buttons floating at its right edge: "to the start" and "to the end" of a long list.</summary>
    private Control BuildScrollArea()
    {
        Border Jump(string glyph, string name, Action go)
        {
            // One segment of the capsule below: transparent, the capsule draws the glass.
            var disc = new Border
            {
                Width = 42, Height = 40, IsVisible = false, Background = Brushes.Transparent,
                Child = Icon(glyph, _t.Glass ? Colors.White : _t.Accent, 22, 2.2, 24),
            };
            ToolTip.SetTip(disc, name);
            disc.Tapped += (_, e) =>
            {
                e.Handled = true;
                go();
            };
            return disc;
        }

        _jumpTop = Jump("M 5,14 L 12,7 L 19,14 M 5,19 L 19,19", "Go to the first item", () => _scroll.Offset = default);
        _jumpEnd = Jump("M 5,5 L 19,5 M 5,10 L 12,17 L 19,10", "Go to the last item", () => _scroll.Offset = new Vector(0, Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height)));
        // A slim capsule (one glass pill with the two buttons in it), like the queue's jump buttons.
        _jumpCapsule = new Border
        {
            CornerRadius = new CornerRadius(21), IsVisible = false, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, _t.DrawnArt ? 4 : 12, 16), BorderThickness = new Thickness(1.2),
            Background = _t.Glass ? Brush(Color.FromArgb(225, 22, 84, 200)) : Brush(Color.FromArgb(225, _t.Card.R, _t.Card.G, _t.Card.B)),
            BorderBrush = _t.Glass ? Brush(_t.G("#BBDCFF")) : Brush(Color.FromArgb(70, 128, 128, 128)),
            Child = new StackPanel { Children = { _jumpTop, _jumpEnd } },
        };
        var buttons = _jumpCapsule;
        _scroll.ScrollChanged += (_, _) => UpdateJumpButtons();
        return new Grid { Children = { _scroll, buttons } };
    }

    private void UpdateJumpButtons()
    {
        var extent = _scroll.Extent.Height;
        var viewport = _scroll.Viewport.Height;
        var offset = _scroll.Offset.Y;
        var longList = viewport > 0 && extent > viewport * 2.2; // a short page needs no jumping
        _jumpTop.IsVisible = longList && offset > viewport * 0.6;
        _jumpEnd.IsVisible = longList && extent - viewport - offset > viewport * 0.6;
        _jumpCapsule.IsVisible = _jumpTop.IsVisible || _jumpEnd.IsVisible;
    }

    // Pages keep the songs they were opened with. These say where the app has since moved or removed their files.
    private readonly Dictionary<string, string> _renamed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _gone = new(StringComparer.Ordinal);

    /// <summary>The app changed, renamed, removed or restored files: the page on show is drawn again, where it was scrolled to.</summary>
    public void FilesChanged(IReadOnlyList<Pardeh.Core.Changes.ChangeResult> results)
    {
        foreach (var result in results.Where(r => r.Status == Pardeh.Core.Changes.ResultStatus.Done))
        {
            if (result.NewSource is not { } now)
            {
                _gone.Add(result.Source);
                continue;
            }

            _gone.Remove(now);
            _renamed.Remove(now); // renaming back must not lead round in a circle
            if (now != result.Source)
                _renamed[result.Source] = now;
        }

        if (!_searching)
            Render(_scroll.Offset.Y);
    }

    /// <summary>The songs a page was opened with, as they are in the library now.</summary>
    private List<LibraryItem> Current(List<LibraryItem> opened)
    {
        var library = new Dictionary<string, LibraryItem>(StringComparer.Ordinal);
        foreach (var item in _library.Items)
            library.TryAdd(item.Path, item);
        var now = new List<LibraryItem>(opened.Count);
        foreach (var item in opened)
        {
            var path = item.Path;
            for (var steps = 0; steps < _renamed.Count && _renamed.TryGetValue(path, out var next); steps++)
                path = next;
            if (!_gone.Contains(path))
                now.Add(library.GetValueOrDefault(path) ?? item); // not in the library (a favourite): as it was
        }

        return now;
    }

    private void Render(double restoreOffset = 0)
    {
        _applyFilter = null; // the page built below says which pages can be filtered live (see SongList / GroupPage)
        _activePage = null;
        var (title, build) = _levels[^1];
        _title.Text = title;
        _back.IsVisible = _levels.Count > 1;
        _title.Margin = new Thickness(_levels.Count > 1 ? 0 : 16, 0, 0, 0);
        _body.Content = build();
        RestoreOffset(restoreOffset);
        UpdateGlassChrome();
        UpdateMini();
    }

    private void SetSearching(bool on)
    {
        _searching = on;
        _searchBox.IsVisible = on;
        _title.IsVisible = !on;
        _searchButton.IsVisible = !on;
        _back.IsVisible = on || _levels.Count > 1;
        UpdateGlassChrome();
        if (on)
        {
            // Inside a list of songs, artists, albums ... the box filters that list as you type; elsewhere it searches the whole library.
            _searchLocal = _applyFilter is not null;
            _searchBox.PlaceholderText = _searchLocal ? "Filter this list" : "Search music and videos";
            _searchBox.Text = string.Empty;
            _searchBox.Focus();
            OnSearchText();
        }
        else
        {
            _searchLocal = false;
            Render();
        }
    }

    private void ShowSearch()
    {
        var text = (_searchBox.Text ?? string.Empty).Trim();
        _body.Content = text.Length == 0
            ? Hint("Type to search your library.")
            : SongList(_library.Items.Where(i => MatchesText(i, text)).ToList(), "No matches.", "search");
    }

    private void UpdateMini()
    {
        var media = _player.CurrentMedia;
        _mini.IsVisible = media is not null && !_compact;
        if (media is null)
            return;
        _miniTitle.Text = media.Name;
        if (_t.Groove && _miniCoverOf != media.Source)
        {
            _miniCoverOf = media.Source;
            var album = media.GetAttribute("Album") is { Length: > 0 } name ? name : media.Name;
            var item = _library.Items.FirstOrDefault(i => string.Equals(i.Path, media.Source, StringComparison.OrdinalIgnoreCase))
                ?? new LibraryItem(media.Source, media.Name, string.Empty, album, string.Empty, string.Empty, 0, 0, false, 0, DateTime.MinValue); // not in the library (yet)
            var tile = Cover(42, album, item, 112);
            _miniCover.Child = tile;
        }

        _miniSub.Text = media.GetAttribute("Author") is { Length: > 0 } artist ? artist : "Now playing";
        var playing = _player.PlayState == PlayState.Playing;
        _miniIcon.Data = _t.PictorialIcons
            ? Geometry.Parse(playing ? "M 6,4 L 10,4 L 10,20 L 6,20 Z M 14,4 L 18,4 L 18,20 L 14,20 Z" : "M 7,4 L 17,12 L 7,20 Z")
            : FluentIcons.Geo(playing ? "pause" : "play", filled: true);
        _miniIcon.Fill = Brush(MiniInk);
    }

    // ---- Pages ----------------------------------------------------------------------------------

    private List<LibraryItem> Music() => _library.Items.Where(i => !i.IsVideo).ToList();

    private Control Hint(string text) => new TextBlock { Text = text, FontSize = 15, Foreground = Brush(_t.Sub), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(20, 24) };

    private Control Card(params Control[] rows)
    {
        var stack = new StackPanel();
        if (_t.Glass)
        {
            // Glass: rows stand on the page by themselves (no card, border, separator or chevron), as in the Collage.
            stack.Margin = new Thickness(0, 10, 0, 4);
            stack.Children.AddRange(rows);
            return stack;
        }

        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0)
                stack.Children.Add(new Border { Height = 1, Background = Brush(_t.Line), Margin = new Thickness(_t.Glass ? 68 : 60, 0, 0, 0) });
            stack.Children.Add(rows[i]);
        }

        if (_t.TypeFirst)
        {
            stack.Children.Clear();
            return TypeRows.Group(rows, new Thickness(0, 8, 0, 6));
        }

        if (_t.ArtCard(stack, new Thickness(12, 8, 12, 6)) is { } art)
            return art;
        return new Border { Background = Brush(_t.Card), CornerRadius = new CornerRadius(_t.Royal ? 5 : 12), Margin = new Thickness(14, 8, 14, 6), ClipToBounds = true, Child = stack, BorderBrush = _t.Royal ? Brush(_t.Line) : null, BorderThickness = new Thickness(_t.Royal ? 1 : 0) };
    }

    /// <summary>A category row: icon, title, count, chevron.</summary>
    private Control CategoryRow(string icon, string title, int? count, Action open, string unit = "items", bool lit = false)
    {
        var ink = lit ? _t.SelectedInk : _t.Ink;
        var faint = lit ? _t.SelectedInk : _t.Sub;
        if (_t.Glass)
        {
            // Glass: a glossy blue tile with a pale glyph, the name over "(247 items)"; no card, separator or chevron.
            var tile = _t.TileIcon(icon, 58);
            tile.Margin = new Thickness(14, 0, 0, 0);
            var caption = count is { } n ? $"({n} {(n == 1 ? unit.TrimEnd('s') : unit)})" : null;
            var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(15, 8, 8, 8), Children = { Text(title, 19, _t.Ink, true) } };
            if (caption is not null)
                lines.Children.Add(Text(caption, 14.5, _t.Sub));
            var glassGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), MinHeight = 76, Background = Brushes.Transparent };
            Grid.SetColumn(lines, 1);
            glassGrid.Children.Add(tile);
            glassGrid.Children.Add(lines);
            glassGrid.Tapped += (_, _) => open();
            return glassGrid;
        }

        if (_t.TypeFirst) // lower case only for the fixed menu words of the home page, never for a user's own names
            return TypeRows.Category(_t, _t.Zune ? null : Icon(icon, Colors.White, 28, 2.0, selected: lit), title, count, unit, lit, lower: _t.Zune && _levels.Count <= 1, open);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), MinHeight = _t.Winamp ? 52 : 60, Background = lit ? _t.SelectedBrush : Brushes.Transparent };
        var holder = new Border { Width = 44, Margin = new Thickness(10, 0, 0, 0), Child = _t.PictorialIcons ? _t.RowIcon(icon, 34) : Icon(icon, ink, 24, 2.0, selected: lit) }; // modern looks: a neutral Fluent icon, no disc or tile
        var name = Text(title, 17, ink, lit);
        name.VerticalAlignment = VerticalAlignment.Center;
        name.Margin = new Thickness(6, 0, 8, 0);
        var number = Text(count?.ToString() ?? string.Empty, 15, faint);
        number.VerticalAlignment = VerticalAlignment.Center;
        var chevron = Icon("M 7,4 L 13,10 L 7,16", faint, _t.PictorialIcons ? 16 : 20, 2);
        chevron.Margin = new Thickness(10, 0, _t.PictorialIcons ? 16 : 14, 0);
        Grid.SetColumn(name, 1);
        Grid.SetColumn(number, 2);
        Grid.SetColumn(chevron, 3);
        grid.Children.Add(holder);
        grid.Children.Add(name);
        grid.Children.Add(number);
        grid.Children.Add(chevron);
        grid.Tapped += (_, _) => open();
        return grid;
    }

    private Control Home()
    {
        if (_t.Glass)
            return GlassHome();
        if (_t.Groove && !_grooveMore)
            return GrooveHome();
        var all = _library.Items;
        var music = all.Count(i => !i.IsVideo);
        if (all.Count == 0 && !_library.IsScanning)
            return EmptyState();
        if (_t.Winamp)
            return WinampHome();
        var names = _host.Playlists()?.Names ?? [];
        var month = DateTime.UtcNow.AddDays(-30);
        var panel = new StackPanel();
        var videosRow = CategoryRow("M 3,5 L 13,5 L 13,15 L 3,15 Z M 13,9 L 17,6 L 17,14 L 13,11", "Videos", all.Count(i => i.IsVideo), () => Push("Videos", () => SongList(all.Where(i => i.IsVideo).ToList(), "No videos yet.", "videos")));
        var picturesRow = CategoryRow("M 3,4 L 17,4 L 17,16 L 3,16 Z M 3,13 L 8,9 L 12,13 L 14,11 L 17,14", "Pictures and other media", 0, () => Push("Other media", () => Hint("No other media yet.")));
        if (_t.Glass)
        {
            // Glass: the music categories straight on the home page (All Music, Artists, Albums, Genres, Playlists), as in the design.
            var songs = Music();
            panel.Children.Add(Card(
                CategoryRow(NoteIcon, "All Music", music, () => Push("All Music", () => SongList(songs, "No songs yet."))),
                CategoryRow("M 10,7 m -3,0 a 3,3 0 1,0 6,0 a 3,3 0 1,0 -6,0 M 4,17 Q 4,12 10,12 Q 16,12 16,17", "Artists", songs.Select(i => i.Artist).Where(a => a.Length > 0).Distinct().Count(), () => Push("Artists", () => GroupPage(songs, i => i.Artist, "Unknown artist", "@person")), "artists"),
                CategoryRow("M 10,10 m -7,0 a 7,7 0 1,0 14,0 a 7,7 0 1,0 -14,0 M 10,10 m -2,0 a 2,2 0 1,0 4,0 a 2,2 0 1,0 -4,0", "Albums", songs.Select(i => i.Album).Where(a => a.Length > 0).Distinct().Count(), () => Push("Albums", AlbumsPage), "albums"),
                CategoryRow("M 4,6 L 16,6 M 4,10 L 16,10 M 4,14 L 11,14", "Genres", songs.Select(i => i.Genre).Where(a => a.Length > 0).Distinct().Count(), () => Push("Genres", () => GroupPage(songs, i => i.Genre, "Unknown genre", "@tag")), "genres"),
                CategoryRow("M 3,6 L 13,6 M 3,10 L 13,10 M 3,14 L 9,14 M 12,12 L 17,14.5 L 12,17 Z", "Playlists", names.Count, () => Push("Playlists", PlaylistsPage), "playlists")));
            panel.Children.Add(Card(videosRow, picturesRow));
        }
        else
        {
            panel.Children.Add(Card(CategoryRow(NoteIcon, "Music", music, () => Push("Music", MusicPage)), videosRow, picturesRow));
        }

        var addRows = new List<Control>
        {
            CategoryRow("M 3,5 L 8,5 L 9.5,7 L 17,7 L 17,15 L 3,15 Z M 10,9 L 10,13 M 8,11 L 12,11", "Add a folder...", null, () => _ = AddFolderAsync()),
        };
        if (PlatformServices.Current.HasDeviceMedia)
            addRows.Add(CategoryRow("@phone", "Device music", null, () => _ = PickedPaths.AddDeviceMedia(_library)));
        panel.Children.Add(Card(addRows.ToArray()));
        var autoRow = CategoryRow("M 10,3 L 12,8 L 17,8 L 13,11.5 L 14.5,17 L 10,13.5 L 5.5,17 L 7,11.5 L 3,8 L 8,8 Z", "Auto playlists", 3, () => Push("Auto playlists", AutoPage), "playlists");
        panel.Children.Add(_t.Glass ? Card(autoRow) : Card(
            CategoryRow("M 3,6 L 13,6 M 3,10 L 13,10 M 3,14 L 9,14 M 12,12 L 17,14.5 L 12,17 Z", "Playlists", names.Count, () => Push("Playlists", PlaylistsPage)),
            autoRow));
        panel.Children.Add(Card(
            FavoritesRow(),
            CategoryRow("M 10,4 L 10,10 L 14,12 M 10,10 m -7,0 a 7,7 0 1,0 14,0 a 7,7 0 1,0 -14,0", "Recently added", all.Count(i => i.Modified >= month),
                () => Push("Recently added", () => SongList(all.OrderByDescending(i => i.Modified).Take(60).ToList(), "Nothing added yet.", "recent-added"))),
            CategoryRow("M 7,4 L 17,10 L 7,16 Z", "Recently played", all.Count(i => i.LastPlayed >= DateTime.UtcNow.AddDays(-7)),
                () => Push("Recently played", () => SongList(all.Where(i => i.LastPlayed >= DateTime.UtcNow.AddDays(-7)).OrderByDescending(i => i.LastPlayed).ToList(), "Nothing played lately.", "recent-played"))),
            CategoryRow("M 10,10 m -1.2,0 a 1.2,1.2 0 1,0 2.4,0 a 1.2,1.2 0 1,0 -2.4,0 M 6.5,6.5 Q 3.5,10 6.5,13.5 M 13.5,6.5 Q 16.5,10 13.5,13.5", "Radio", null, _host.OpenRadio)));
        if (_library.IsScanning)
            panel.Children.Add(Hint("Searching your folders..."));
        return panel;
    }

    // ---- Glass: Music / Videos / Pictures strip, bottom pill bar, home, playlist manager ------------

    private static readonly string VideoGlyph = "M 3,5 L 13,5 L 13,15 L 3,15 Z M 13,9 L 17,6 L 17,14 L 13,11";
    private static readonly string PictureGlyph = "M 3,4 L 17,4 L 17,16 L 3,16 Z M 3,13 L 8,9 L 12,13 L 14,11 L 17,14";
    private const string ListGlyph = "M 3,5 L 3.6,5 M 7,5 L 17,5 M 3,10 L 3.6,10 M 7,10 L 17,10 M 3,15 L 3.6,15 M 7,15 L 17,15";
    private const string GridGlyph = "M 3,3 L 8,3 L 8,8 L 3,8 Z M 12,3 L 17,3 L 17,8 L 12,8 Z M 3,12 L 8,12 L 8,17 L 3,17 Z M 12,12 L 17,12 L 17,17 L 12,17 Z";

    private static IBrush Vertical(params (string Color, double At)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
        foreach (var (color, at) in stops)
            brush.GradientStops.Add(new GradientStop(GlassPalette.Parse(color), at)); // only the Glass chrome uses this
        return brush;
    }

    /// <summary>Shows or hides the Glass-only chrome for the current level: the tab strip and the note on the home page, the pill bar except while typing a search.</summary>
    private void UpdateGlassChrome()
    {
        if (!_t.Glass)
            return;
        var home = _levels.Count == 1 && !_searching;
        _tabStrip.IsVisible = home;
        if (_noteArt is not null)
            _noteArt.IsVisible = home && _homeTab == 0 && !_compact && !AppSettings.Current.BrowseGrid;
        if (_foot is not null)
        {
            // A short (landscape) view: the home page keeps all its room, deeper levels (Albums, lists) get a slim bar
            // instead of the bare strip of window frame under the list.
            _foot.IsVisible = !_searching && (!_compact || !home);
            _foot.Padding = _compact ? new Thickness(10, 3, 10, 3) : new Thickness(10, 7, 10, 9);
            _foot.Child = _levels[^1].Title == "Playlists" ? BuildPlaylistFoot() : BuildFootButtons();
        }

        if (home)
            BuildTabs();
    }

    private void BuildTabs()
    {
        _tabStrip.Children.Clear();
        _tabStrip.ColumnDefinitions = new ColumnDefinitions("*,*,*");
        (string Name, string Icon)[] tabs = [("Music", NoteIcon), ("Videos", VideoGlyph), ("Pictures", PictureGlyph)];
        for (var i = 0; i < tabs.Length; i++)
        {
            var index = i;
            var selected = i == _homeTab;
            var content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Children = { _t.PictorialIcons ? _t.RowIcon(tabs[i].Icon, 26) : Icon(tabs[i].Icon, selected ? Colors.White : _t.G("#D6E6FF"), 18, 1.5), Text(tabs[i].Name, 14, selected ? Colors.White : _t.G("#D6E6FF"), selected) },
            };
            var tab = new Border
            {
                Height = 40, Background = selected ? Vertical(("#A2D9FF", 0), ("#55A5F8", 0.45), ("#2F7CE8", 0.55), ("#2468D8", 1)) : Brushes.Transparent,
                BorderBrush = selected ? Brush(_t.G("#D3EDFF")) : Brush(GlassPalette.Map(Color.FromArgb(120, 75, 134, 230))), BorderThickness = selected ? new Thickness(1.2, 1.2, 1.2, 0) : new Thickness(0, 0, 1, 0),
                CornerRadius = selected ? new CornerRadius(9, 9, 0, 0) : new CornerRadius(0), Margin = selected ? new Thickness(2, 3, 2, 0) : new Thickness(0, 3, 0, 0), Child = content,
            };
            tab.Tapped += (_, _) =>
            {
                _homeTab = index;
                Render();
            };
            Grid.SetColumn(tab, i);
            _tabStrip.Children.Add(tab);
        }
    }

    /// <summary>A dark glass pill with a white glyph or word (the bottom bar).</summary>
    private Border FootPill(string? glyph, string? word, Action tap, bool selected = false)
    {
        var ink = Brush(Colors.White);
        Control content = word is not null
            ? new TextBlock { Text = word, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            : Icon(glyph!, Colors.White, _compact ? 15 : 20, 1.8);
        var pill = new Border
        {
            Height = _compact ? 28 : 36, CornerRadius = new CornerRadius(_compact ? 10 : 12), Margin = new Thickness(5, 0), Child = content, Background = selected ? Vertical(("#4C93F4", 0), ("#2563D6", 0.5), ("#1646B6", 1)) : Vertical(("#1D4FC2", 0), ("#0D3396", 0.5), ("#082675", 1)),
            BorderBrush = Brush(_t.G(selected ? "#BBDCFF" : "#6FA9F5")), BorderThickness = new Thickness(1.3),
        };
        pill.Tapped += (_, _) => tap();
        return pill;
    }

    private Control BuildFootButtons()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
        var back = FootPill("M 15,4 L 7,12 L 15,20", null, () => { if (!GoBack()) _host.OpenNowPlaying(); });
        var search = FootPill("M 9,9 m -5.5,0 a 5.5,5.5 0 1,0 11,0 a 5.5,5.5 0 1,0 -11,0 M 13,13 L 18,18", null, () => SetSearching(true));
        var onHome = _levels.Count == 1 && _homeTab == 0; // the home's Music tab toggles the Browse Media grid (remembered), Albums its cover grid
        var gridOn = onHome ? AppSettings.Current.BrowseGrid : _albumGrid;
        var view = FootPill(gridOn ? ListGlyph : GridGlyph, null, () =>
        {
            if (_levels.Count == 1 && _homeTab == 0)
            {
                AppSettings.Current.BrowseGrid = !AppSettings.Current.BrowseGrid;
                AppSettings.Current.Save();
            }
            else
            {
                _albumGrid = !_albumGrid;
            }

            Render();
        });
        Grid.SetColumn(search, 1);
        Grid.SetColumn(view, 2);
        grid.Children.Add(back);
        grid.Children.Add(search);
        grid.Children.Add(view);
        return grid;
    }

    private Border BuildFoot() => new()
    {
        Background = Vertical(("#3F82EC", 0), ("#2158D0", 0.45), ("#1648B8", 1)), BorderBrush = Brush(Color.FromArgb(160, 255, 255, 255)), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(10, 7, 10, 9),
        Child = BuildFootButtons(),
    };

    private Control GlassHome()
    {
        var all = _library.Items;
        if (_homeTab == 1)
            return SongList(all.Where(i => i.IsVideo).ToList(), "No videos yet.", "videos");
        if (_homeTab == 2)
            return Hint("No pictures or other media yet.");
        if (all.Count == 0 && !_library.IsScanning)
            return EmptyState();
        if (AppSettings.Current.BrowseGrid)
            return BrowseGrid();
        var panel = new StackPanel();
        panel.Children.Add(MusicRows());
        panel.Children.Add(OtherRows());
        if (_library.IsScanning)
            panel.Children.Add(Hint("Searching your folders..."));
        return panel;
    }

    /// <summary>Browse Media: a three-column grid of large icons with the label under each (the Collage's "Browse Media" screen).</summary>
    private Control BrowseGrid()
    {
        var grid = new Avalonia.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(6, 22, 6, 16) };
        void Cell(string iconKey, string label, Action open)
        {
            var tile = new StackPanel
            {
                Margin = new Thickness(0, 0, 0, 26), Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center,
                Children = { MobileTheme.BrowseIcon(iconKey, 84), Text(label, 17, _t.Ink, true) },
            };
            tile.Children[0].Margin = new Thickness(0, 0, 0, 10);
            tile.Children[1].HorizontalAlignment = HorizontalAlignment.Center;
            tile.Tapped += (_, _) => open();
            grid.Children.Add(tile);
        }

        var all = _library.Items;
        Cell("b_music", "Music", () => Push("Music", MusicListPage));
        Cell("b_video", "Videos", () => Push("Videos", () => SongList(all.Where(i => i.IsVideo).ToList(), "No videos yet.", "videos")));
        Cell("b_pictures", "Pictures", () => Push("Pictures", () => Hint("No pictures or other media yet.")));
        Cell("b_tv", "Recorded TV", () => Push("Recorded TV", () => Hint("No recorded TV yet.")));
        Cell("b_radio", "Radio", _host.OpenRadio);
        Cell("b_playlist", "Playlists", () => Push("Playlists", GlassPlaylistsPage));
        Cell("b_folder", "Others", () => Push("Others", () => new StackPanel { Children = { OtherRows() } }));
        if (_library.IsScanning)
            return new StackPanel { Children = { grid, Hint("Searching your folders...") } };
        return grid;
    }

    private Control MusicListPage()
    {
        var panel = new StackPanel();
        panel.Children.Add(MusicRows());
        return panel;
    }

    /// <summary>The five music rows of the Collage's Library: All Music, Artists, Albums, Genres, Playlists.</summary>
    private Control MusicRows()
    {
        var songs = Music();
        var names = _host.Playlists()?.Names ?? [];
        return Card(
            CategoryRow(NoteIcon, "All Music", songs.Count, () => Push("All Music", () => SongList(songs, "No songs yet."))),
            CategoryRow("M 10,7 m -3,0 a 3,3 0 1,0 6,0 a 3,3 0 1,0 -6,0 M 4,17 Q 4,12 10,12 Q 16,12 16,17", "Artists", songs.Select(i => i.Artist).Where(a => a.Length > 0).Distinct().Count(), () => Push("Artists", () => GroupPage(songs, i => i.Artist, "Unknown artist", "@person")), "artists"),
            CategoryRow("M 10,10 m -7,0 a 7,7 0 1,0 14,0 a 7,7 0 1,0 -14,0 M 10,10 m -2,0 a 2,2 0 1,0 4,0 a 2,2 0 1,0 -4,0", "Albums", songs.Select(i => i.Album).Where(a => a.Length > 0).Distinct().Count(), () => Push("Albums", AlbumsPage), "albums"),
            CategoryRow("M 4,6 L 16,6 M 4,10 L 16,10 M 4,14 L 11,14", "Genres", songs.Select(i => i.Genre).Where(a => a.Length > 0).Distinct().Count(), () => Push("Genres", () => GroupPage(songs, i => i.Genre, "Unknown genre", "@tag")), "genres"),
            CategoryRow("M 3,6 L 13,6 M 3,10 L 13,10 M 3,14 L 9,14 M 12,12 L 17,14.5 L 12,17 Z", "Playlists", names.Count, () => Push("Playlists", GlassPlaylistsPage), "playlists"));
    }

    /// <summary>The extra rows below the music ones: auto playlists, recently added / played, radio, add a folder, device music.</summary>
    private Control OtherRows()
    {
        var all = _library.Items;
        var month = DateTime.UtcNow.AddDays(-30);
        var panel = new StackPanel();
        panel.Children.Add(Card(
            FavoritesRow(),
            CategoryRow("M 10,3 L 12,8 L 17,8 L 13,11.5 L 14.5,17 L 10,13.5 L 5.5,17 L 7,11.5 L 3,8 L 8,8 Z", "Auto playlists", 3, () => Push("Auto playlists", AutoPage), "playlists"),
            CategoryRow("M 10,4 L 10,10 L 14,12 M 10,10 m -7,0 a 7,7 0 1,0 14,0 a 7,7 0 1,0 -14,0", "Recently added", all.Count(i => i.Modified >= month),
                () => Push("Recently added", () => SongList(all.OrderByDescending(i => i.Modified).Take(60).ToList(), "Nothing added yet.", "recent-added"))),
            CategoryRow("M 7,4 L 17,10 L 7,16 Z", "Recently played", all.Count(i => i.LastPlayed >= DateTime.UtcNow.AddDays(-7)),
                () => Push("Recently played", () => SongList(all.Where(i => i.LastPlayed >= DateTime.UtcNow.AddDays(-7)).OrderByDescending(i => i.LastPlayed).ToList(), "Nothing played lately.", "recent-played"))),
            CategoryRow("M 10,10 m -1.2,0 a 1.2,1.2 0 1,0 2.4,0 a 1.2,1.2 0 1,0 -2.4,0 M 6.5,6.5 Q 3.5,10 6.5,13.5 M 13.5,6.5 Q 16.5,10 13.5,13.5", "Radio", null, _host.OpenRadio)));
        var addRows = new List<Control> { CategoryRow("M 3,5 L 8,5 L 9.5,7 L 17,7 L 17,15 L 3,15 Z M 10,9 L 10,13 M 8,11 L 12,11", "Add a folder...", null, () => _ = AddFolderAsync()) };
        if (PlatformServices.Current.HasDeviceMedia)
            addRows.Add(CategoryRow("@phone", "Device music", null, () => _ = PickedPaths.AddDeviceMedia(_library)));
        panel.Children.Add(Card(addRows.ToArray()));
        return panel;
    }

    /// <summary>"Playlist Manager": rows with a note tile, the name and "(N items)"; a tap selects (glossy), the chevron opens. New / Rename / Delete are in the bottom bar.</summary>
    private Control GlassPlaylistsPage()
    {
        var store = _host.Playlists();
        var names = (store?.Names ?? []).ToList();
        if (_pickedPlaylist is not null && !names.Contains(_pickedPlaylist))
            _pickedPlaylist = null;
        var stack = new StackPanel { Margin = new Thickness(10, 8, 10, 10) };
        if (names.Count == 0)
            stack.Children.Add(Hint("No playlists yet. Tap New to make one."));
        foreach (var name in names)
        {
            var picked = name == _pickedPlaylist;
            var count = store!.Read(name).Count;
            var tile = _t.PictorialIcons ? _t.RowIcon("@note", 32) : Icon(NoteIcon, Colors.White, 24, 1.6);
            tile.Margin = new Thickness(12, 0, 0, 0);
            var label = Text(name, 17, picked ? Colors.White : _t.Ink, picked);
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(14, 0, 8, 0);
            var number = Text($"({count})", 15, picked ? Colors.White : _t.Sub);
            number.VerticalAlignment = VerticalAlignment.Center;
            var open = new Border { Width = 46, Background = Brushes.Transparent, Child = Icon("M 7,4 L 13,10 L 7,16", picked ? Colors.White : _t.Sub, 16, 2) };
            var captured = name;
            open.Tapped += (_, e) =>
            {
                e.Handled = true;
                Push(captured, () => SongList(PlaylistItems(captured), "This playlist is empty.", "playlist"));
            };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), MinHeight = 54, Background = Brushes.Transparent };
            Grid.SetColumn(label, 1);
            Grid.SetColumn(number, 2);
            Grid.SetColumn(open, 3);
            grid.Children.Add(tile);
            grid.Children.Add(label);
            grid.Children.Add(number);
            grid.Children.Add(open);
            var row = new Border
            {
                Child = grid, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 1),
                Background = picked ? _t.SelectedBrush : Brushes.Transparent, BorderBrush = picked ? Brush(_t.G("#9ED2FF")) : Brush(_t.Line), BorderThickness = picked ? new Thickness(1) : new Thickness(0, 0, 0, 1),
            };
            row.Tapped += (_, _) =>
            {
                _pickedPlaylist = captured;
                Render();
            };
            stack.Children.Add(row);
        }

        return stack;
    }

    private Control BuildPlaylistFoot()
    {
        var store = _host.Playlists();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
        var create = FootPill(null, "New", () =>
        {
            var next = 1;
            while (store?.Names.Contains("Playlist " + next) == true)
                next++;
            AskName("New playlist", "Playlist " + next, text =>
            {
                store?.Save(text, []);
                _pickedPlaylist = text;
                Render();
            });
        });
        var rename = FootPill(null, "Rename", () =>
        {
            if (_pickedPlaylist is not { } old || store is null)
                return;
            AskName("Rename playlist", old, text =>
            {
                if (string.Equals(text, old, StringComparison.Ordinal))
                    return;
                store.Save(text, store.Read(old));
                store.Delete(old);
                _pickedPlaylist = text;
                Render();
            });
        });
        var delete = FootPill(null, "Delete", () =>
        {
            if (_pickedPlaylist is not { } doomed || store is null)
                return;
            OpenSheet($"Delete \"{doomed}\"?", [("Delete", () =>
            {
                store.Delete(doomed);
                _pickedPlaylist = null;
                Render();
            })]);
        });
        Grid.SetColumn(rename, 1);
        Grid.SetColumn(delete, 2);
        grid.Children.Add(create);
        grid.Children.Add(rename);
        grid.Children.Add(delete);
        return grid;
    }

    /// <summary>A small sheet with a text box and OK / Close pills.</summary>
    private void AskName(string title, string initial, Action<string> accept)
    {
        _sheetTitle.Text = title;
        _sheetRows.Children.Clear();
        if (_sheetPanel is not null)
        {
            _sheetPanel.VerticalAlignment = VerticalAlignment.Top; // the keyboard covers the bottom: the name sheet sits near the top
            _sheetPanel.CornerRadius = new CornerRadius(0, 0, 16, 16);
            _sheetPanel.Margin = new Thickness(0, 4, 0, 0);
        }

        var box = new TextBox
        {
            Text = initial, FontSize = 16, MinHeight = 42, Margin = new Thickness(16, 10, 16, 4), CornerRadius = new CornerRadius(8),
            Background = Brush(_t.G("#06205E")), Foreground = Brushes.White, BorderBrush = Brush(_t.G("#7DB7FB")), BorderThickness = new Thickness(1.3),
        };
        foreach (var key in new[] { "TextControlBackgroundFocused", "TextControlBackgroundPointerOver" })
            box.Resources[key] = Brush(_t.G("#06205E"));
        box.Resources["TextControlBorderBrushFocused"] = Brush(_t.G("#BBDCFF"));
        _sheetRows.Children.Add(box);
        _sheetRows.Children.Add(_t.GlassPill("OK", () =>
        {
            var text = (box.Text ?? string.Empty).Trim();
            if (text.Length == 0)
                return;
            _sheet.IsVisible = false;
            accept(text);
        }));
        _sheetRows.Children.Add(_t.GlassPill("Close", () => _sheet.IsVisible = false));
        _sheet.IsVisible = true;
        box.Focus();
        box.SelectAll();
    }

    private async Task AddFolderAsync()
    {
        if (_host.Top() is not { } top)
            return;
        var picked = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Add a music folder" });
        if (PickedPaths.Of(picked.FirstOrDefault()) is { } path)
            _library.AddFolder(path);
    }

    private Control EmptyState()
    {
        var add = new Border
        {
            Background = _t.Glass ? _t.SelectedBrush : Brush(_t.Accent), CornerRadius = new CornerRadius(24), Height = 48, Padding = new Thickness(28, 0), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 20, 0, 0),
            Child = new TextBlock { Text = "Add a folder", FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Brush(_t.Dark && !_t.Glass ? Color.Parse("#101010") : Colors.White), VerticalAlignment = VerticalAlignment.Center },
        };
        add.Tapped += (_, _) => _ = AddFolderAsync();
        var device = new Border
        {
            BorderBrush = Brush(_t.Accent), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(24), Height = 48, Padding = new Thickness(28, 0), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0),
            IsVisible = PlatformServices.Current.HasDeviceMedia, Background = Brushes.Transparent,
            Child = new TextBlock { Text = "Device music", FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Brush(_t.Accent), VerticalAlignment = VerticalAlignment.Center },
        };
        device.Tapped += (_, _) => _ = PickedPaths.AddDeviceMedia(_library);
        return new StackPanel
        {
            Margin = new Thickness(24, 70, 24, 0),
            Children =
            {
                new Border { Width = 96, Height = 96, CornerRadius = new CornerRadius(20), Background = Brush(_t.Card), HorizontalAlignment = HorizontalAlignment.Center, Child = Icon(NoteIcon, _t.Accent, 52, 1.4) },
                new TextBlock { Text = "Your library is empty", FontSize = 20, FontWeight = FontWeight.SemiBold, Foreground = Brush(_t.Ink), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 6) },
                new TextBlock { Text = "Add a folder with your music and videos and Pardeh will keep the library up to date.", FontSize = 15, Foreground = Brush(_t.Sub), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
                add,
                device,
            },
        };
    }

    private Control MusicPage()
    {
        var music = Music();
        return new StackPanel
        {
            Children =
            {
                Card(
                    CategoryRow(NoteIcon, "Songs", music.Count, () => Push("Songs", () => SongList(music, "No songs yet."))),
                    CategoryRow("M 10,10 m -7,0 a 7,7 0 1,0 14,0 a 7,7 0 1,0 -14,0 M 10,10 m -2,0 a 2,2 0 1,0 4,0 a 2,2 0 1,0 -4,0", "Albums", music.Select(i => i.Album).Where(a => a.Length > 0).Distinct().Count(), () => Push("Albums", AlbumsPage), "albums"),
                    CategoryRow("M 10,7 m -3,0 a 3,3 0 1,0 6,0 a 3,3 0 1,0 -6,0 M 4,17 Q 4,12 10,12 Q 16,12 16,17", "Artists", music.Select(i => i.Artist).Where(a => a.Length > 0).Distinct().Count(), () => Push("Artists", () => GroupPage(music, i => i.Artist, "Unknown artist", "@person")), "artists"),
                    CategoryRow("M 4,6 L 16,6 M 4,10 L 16,10 M 4,14 L 11,14", "Genres", music.Select(i => i.Genre).Where(a => a.Length > 0).Distinct().Count(), () => Push("Genres", () => GroupPage(music, i => i.Genre, "Unknown genre", "@tag")), "genres")),
            },
        };
    }

    private Control PlaylistsPage()
    {
        var store = _host.Playlists();
        var names = store?.Names ?? [];
        if (names.Count == 0)
            return Hint("No playlists yet. Use a song's menu > Add to playlist.");
        return Card(names.Select(name => CategoryRow(_t.Glass ? NoteIcon : "M 3,6 L 13,6 M 3,10 L 13,10 M 3,14 L 9,14", name, store!.Read(name).Count, () => Push(name, () => SongList(PlaylistItems(name), "This playlist is empty.", "playlist")))).ToArray());
    }

    private List<LibraryItem> PlaylistItems(string name)
    {
        var known = _library.Items.GroupBy(i => i.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        return (_host.Playlists()?.Read(name) ?? []).Select(s => known.TryGetValue(s, out var item) ? item
            : new LibraryItem(s, System.IO.Path.GetFileNameWithoutExtension(s), string.Empty, string.Empty, string.Empty, string.Empty, 0, 0, false, 0, DateTime.MinValue)).ToList();
    }

    private Control AutoPage()
    {
        var music = Music();
        var month = DateTime.UtcNow.AddDays(-30);
        (string Name, Func<LibraryItem, bool> Filter)[] lists =
        [
            ("Favorites - 4 and 5 star rated", i => i.Rating >= 4),
            ("Fresh tracks", i => i.Modified >= month),
            ("Most played", i => i.PlayCount >= 3),
        ];
        const string star = "M 10,3 L 12,8 L 17,8 L 13,11.5 L 14.5,17 L 10,13.5 L 5.5,17 L 7,11.5 L 3,8 L 8,8 Z";
        return Card(lists.Select(l => CategoryRow(!_t.PictorialIcons ? star : l.Name.StartsWith("Most", StringComparison.Ordinal) ? "@trophy" : l.Name.StartsWith("Fresh", StringComparison.Ordinal) ? "@newnote" : "@star", l.Name, music.Count(l.Filter),
            () => Push(l.Name, () => SongList(music.Where(l.Filter).ToList(), "Nothing here yet.", "auto")))).ToArray());
    }

    private Control GroupPage(List<LibraryItem> music, Func<LibraryItem, string> key, string unknown, string pictorial = "@note")
    {
        var level = pictorial == "@person" ? "artists" : "genres";
        var entries = music.GroupBy(i => key(i), StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new GroupEntry(g.Key.Length == 0 ? unknown : g.Key, g.Key, g.ToList())).ToList();
        return GroupList(level, [SortField.Title, SortField.SongCount], entries, sorted => Card(sorted.Select(entry =>
            CategoryRow(_t.PictorialIcons ? pictorial : NoteIcon, entry.Label, entry.Items.Count, () => Push(entry.Label, () => SongList(
                entry.Items.OrderBy(i => i.Album, StringComparer.CurrentCultureIgnoreCase).ThenBy(i => i.TrackNumber).ToList(), "Nothing here.", level == "artists" ? "artist" : "genre")))).ToArray()),
            "Nothing here yet.");
    }

    /// <summary>Albums as a two-column grid of cover tiles.</summary>
    private Control AlbumsPage()
    {
        var entries = Music().GroupBy(i => i.Album, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new GroupEntry(g.Key.Length == 0 ? "Unknown album" : g.Key, g.Key, g.ToList())).ToList();
        return GroupList("albums", [SortField.Title, SortField.Artist, SortField.Year, SortField.SongCount], entries, AlbumTiles, "No albums yet.");
    }

    private Control AlbumTiles(List<GroupEntry> albums)
    {
        if (_t.Glass && !_albumGrid)
        {
            return Card(albums.Select(a =>
            {
                var songs = a.Items.OrderBy(i => i.TrackNumber).ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
                return CategoryRow("M 10,10 m -7,0 a 7,7 0 1,0 14,0 a 7,7 0 1,0 -14,0 M 10,10 m -2,0 a 2,2 0 1,0 4,0 a 2,2 0 1,0 -4,0", a.Label, songs.Count, () => Push(a.Label, () => SongList(songs, "Nothing here.", "album")));
            }).ToArray());
        }

        var grid = new Avalonia.Controls.Primitives.UniformGrid { Columns = _t.Glass ? 3 : 2, Margin = new Thickness(8, 8, 8, 16) };
        foreach (var entry in albums)
        {
            var label = entry.Label;
            var album = entry.Items;
            var artist = album.Select(i => i.Artist).FirstOrDefault(a => a.Length > 0) ?? string.Empty;
            var tile = new StackPanel
            {
                Margin = new Thickness(6), Background = Brushes.Transparent,
                Children = { Cover(0, label, album.First(), 300), Text(label, 15, _t.Ink, true), Text(artist, 13, _t.Sub) },
            };
            ((Border)tile.Children[0]).Height = _t.Glass ? 96 : 150;
            ((Border)tile.Children[0]).Margin = new Thickness(0, 0, 0, 6);
            if (_t.Glass)
            {
                // Glass: three columns, a square cover and centred captions (album over artist).
                ((Border)tile.Children[0]).Width = 96;
                foreach (var caption in tile.Children.OfType<TextBlock>())
                    caption.TextAlignment = TextAlignment.Center;
                tile.Children[1].SetValue(TextBlock.FontSizeProperty, 13.0);
                tile.Children[2].SetValue(TextBlock.FontSizeProperty, 12.0);
                tile.HorizontalAlignment = HorizontalAlignment.Stretch;
            }

            var items = album.OrderBy(i => i.TrackNumber).ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
            tile.Tapped += (_, _) => Push(label, () => SongList(items, "Nothing here.", "album"));
            grid.Children.Add(tile);
        }

        return grid;
    }

    /// <summary>The cover art tile: a drawn note on a gradient in the skin's colours (no art file is read here).</summary>
    private Border Cover(double size, string seed, LibraryItem? source = null, int decodeWidth = 100)
    {
        var hash = Math.Abs(seed.GetHashCode()) % 5;
        var top = Mix(_t.Accent, Colors.White, 0.12 + hash * 0.04);
        var bottom = Mix(_t.Accent, _t.Dark ? Colors.Black : Color.Parse("#0B1F5C"), 0.45 + hash * 0.05);
        if (_t.Glass)
        {
            top = _t.G("#5FB0FF");
            bottom = _t.G("#1546B8");
        }

        var border = new Border
        {
            CornerRadius = new CornerRadius(size > 0 ? Math.Min(10, size / 6) : 12), ClipToBounds = true,
            Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(top, 0), new GradientStop(bottom, 1) } },
            Child = _t.Glass ? _t.RowIcon("@note", size > 0 ? size * 0.66 : 60) : Icon(NoteIcon, Color.FromArgb(220, 255, 255, 255), size > 0 ? size * 0.55 : 56, 1.5),
        };
        if (_t.Glass)
        {
            border.BorderBrush = Brush(_t.G("#BBDCFF"));
            border.BorderThickness = new Thickness(1.2);
        }
        else if (_t.Groove)
        {
            border.Child = new GeneratedCover(seed.Length > 0 ? seed : source?.Title ?? string.Empty); // a "photo" tile until (unless) the track has a picture
        }
        else if (_t.Winamp)
        {
            border.Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse("#1A3A72"), 0), new GradientStop(Color.Parse("#081430"), 1) } };
            border.BorderBrush = Brush(Color.Parse("#4A566C"));
            border.BorderThickness = new Thickness(1);
            border.Child = Icon(NoteIcon, Color.Parse("#FFB52E"), size > 0 ? size * 0.55 : 56, 1.6);
        }

        if (source is not null)
            LoadArt(border, source, decodeWidth);
        if (size > 0)
        {
            border.Width = size;
            border.Height = size;
        }

        return border;
    }


    // ---- Cover art: the picture embedded in the file or a folder.jpg / cover.jpg, else the drawn tile ----

    private static readonly Dictionary<string, Avalonia.Media.Imaging.Bitmap?> ArtCache = new();
    private static readonly SemaphoreSlim ArtGate = new(2);

    private void LoadArt(Border tile, LibraryItem item, int decodeWidth)
    {
        var key = (item.Album.Length > 0 ? item.Album + "|" + item.Artist : item.Path) + "|" + decodeWidth;
        if (ArtCache.TryGetValue(key, out var cached))
        {
            if (cached is not null)
                ShowArt(tile, cached);
            return;
        }

        var media = item.ToMediaItem();
        _ = Task.Run(async () =>
        {
            await ArtGate.WaitAsync();
            Avalonia.Media.Imaging.Bitmap? bitmap = null;
            try
            {
                var data = _player.GetAlbumArt(media);
                if ((data is null || data.Length == 0) && item.IsVideo)
                    data = PlatformServices.Current.VideoThumbnail(item.Path, decodeWidth); // no embedded picture: a frame of the video
                if (data is { Length: > 0 })
                {
                    using var stream = new MemoryStream(data);
                    bitmap = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, decodeWidth);
                }
            }
            catch (Exception)
            {
                // Unreadable picture: the drawn tile stays.
            }
            finally
            {
                ArtGate.Release();
            }

            Dispatcher.UIThread.Post(() =>
            {
                ArtCache[key] = bitmap;
                if (bitmap is not null)
                    ShowArt(tile, bitmap);
            });
        });
    }

    private static void ShowArt(Border tile, Avalonia.Media.Imaging.Bitmap bitmap) =>
        tile.Child = new Image { Source = bitmap, Stretch = Stretch.UniformToFill };
    private static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>Touch song rows (56+ high): cover, title, artist - album, duration and a three-dot menu. Tap plays the list.</summary>
    private Control SongRows(List<LibraryItem> items)
    {
        var list = new ListBox
        {
            ItemsSource = items, Background = Brushes.Transparent, Padding = new Thickness(0), Margin = new Thickness(0, 4, 0, 12),
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<LibraryItem>((item, _) => item is null ? null : SongRow(item, items)),
        };
        if (_t.Glass || _t.DrawnArt)
            list.SelectionChanged += (_, _) => list.SelectedItem = null; // no stock blue selection: the glossy bar marks the playing row
        if (_t.DrawnArt && items.Count > 12)
            list.Margin = new Thickness(0, 4, 42, 12); // the art looks keep a gutter free for the jump capsule, clear of the times and menus
        ScrollViewer.SetVerticalScrollBarVisibility(list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        list.Styles.Add(new Style(x => Selectors.OfType<ListBoxItem>(x)) { Setters = { new Setter(Avalonia.Controls.Primitives.TemplatedControl.PaddingProperty, new Thickness(0)), new Setter(Layoutable.MinHeightProperty, 0.0) } });
        return list;
    }

    private Control SongRow(LibraryItem item, List<LibraryItem> context)
    {
        var playing = string.Equals(item.Path, _player.CurrentMedia?.Source, StringComparison.OrdinalIgnoreCase);
        var cover = Cover(_t.Groove ? 52 : 44, item.Album.Length > 0 ? item.Album : item.Title, item, 112);
        cover.Margin = new Thickness(_t.Groove ? 8 : 14, 6, 0, 6);
        var glossy = playing && _t.SelectedBrush is not null;
        var glossySub = _t.Winamp ? Color.Parse("#3A2600") : _t.G("#DCE6FF");
        var title = Text(item.Title, 16, glossy ? _t.SelectedInk : playing ? _t.Accent : _t.Ink, playing);
        var sub = Text(string.Join(" - ", new[] { item.Artist, item.Album }.Where(s => s.Length > 0)) is { Length: > 0 } s ? s : (item.IsVideo ? "Video" : "Unknown artist"), 13, glossy ? glossySub : _t.Sub);
        Border? shell = null; // Glass: the outer border that turns into the glossy playing bar; Groove: the playing card
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0), Children = { title, sub } };
        var time = Text(item.DurationSeconds > 0 ? MediaPlayer.FormatTime(item.DurationSeconds) : string.Empty, 13, _t.Sub);
        time.VerticalAlignment = VerticalAlignment.Center;
        // A favourite carries a small heart before its time (it follows the favourites at once, without rebuilding the list).
        var heart = HeartMark(16);
        heart.Margin = new Thickness(0, 0, 8, 0);
        heart.VerticalAlignment = VerticalAlignment.Center;
        heart.IsVisible = IsFavorite(item.Path);
        var timeBox = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { heart, time } };
        var heartEntry = (item.Path, (Action<bool>)(on => heart.IsVisible = on));
        _heartPaint.Add(heartEntry);
        var dots = new Border { Width = 48, Height = 56, Background = Brushes.Transparent, Child = Icon("M 10,4 m -1.4,0 a 1.4,1.4 0 1,0 2.8,0 a 1.4,1.4 0 1,0 -2.8,0 M 10,10 m -1.4,0 a 1.4,1.4 0 1,0 2.8,0 a 1.4,1.4 0 1,0 -2.8,0 M 10,16 m -1.4,0 a 1.4,1.4 0 1,0 2.8,0 a 1.4,1.4 0 1,0 -2.8,0", _t.Sub, 22, 1.4) };
        dots.Tapped += (_, e) =>
        {
            e.Handled = true;
            RowMenu(item);
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), MinHeight = 58, Background = _t.Glass || _t.Groove ? Brushes.Transparent : glossy ? _t.SelectedBrush : playing ? Brush(Color.FromArgb(40, _t.Accent.R, _t.Accent.G, _t.Accent.B)) : Brushes.Transparent };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(timeBox, 2);
        Grid.SetColumn(dots, 3);
        grid.Children.Add(cover);
        grid.Children.Add(text);
        grid.Children.Add(timeBox);
        grid.Children.Add(dots);
        grid.DetachedFromVisualTree += (_, _) => _heartPaint.Remove(heartEntry);
        grid.Tapped += (_, _) => _player.OpenItems(context.Select(t => t.ToMediaItem()), Math.Max(0, context.IndexOf(item)));
        grid.Holding += (_, e) =>
        {
            if (e.HoldingState == HoldingState.Started)
                RowMenu(item);
        };
        if (_t.Glass)
        {
            // Glass: thin separators, and the playing row is a glossy bright-blue rounded bar.
            cover.Margin = new Thickness(6, 6, 0, 6);
            shell = new Border { CornerRadius = new CornerRadius(5), Child = grid };
        }
        else if (_t.Groove)
        {
            shell = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Margin = new Thickness(8, 1), Child = grid };
        }

        // The playing row follows the player: the paint runs again when the track changes (no rebuild of the list).
        void Paint(bool now)
        {
            var gloss = now && _t.SelectedBrush is not null;
            title.Foreground = Brush(gloss ? _t.SelectedInk : now ? _t.Accent : _t.Ink);
            title.FontWeight = now ? FontWeight.SemiBold : FontWeight.Normal;
            sub.Foreground = Brush(gloss ? glossySub : _t.Sub);
            if (_t.Winamp)
                time.Foreground = Brush(gloss ? _t.SelectedInk : _t.Sub);
            if (_t.Groove)
            {
                shell!.Background = now ? Brush(Color.FromArgb(66, 150, 200, 255)) : Brushes.Transparent;
                shell.BorderBrush = now ? Brush(Color.FromArgb(120, 170, 215, 255)) : Brushes.Transparent;
            }
            else if (shell is not null)
            {
                shell.Background = now ? _t.SelectedBrush : null;
                shell.BorderBrush = Brush(now ? _t.G("#9ED2FF") : _t.Line);
                shell.BorderThickness = now ? new Thickness(1) : new Thickness(0, 0, 0, 1);
                shell.Margin = now ? new Thickness(8, 1) : new Thickness(14, 0);
            }
            else
            {
                grid.Background = gloss ? _t.SelectedBrush : now ? Brush(Color.FromArgb(40, _t.Accent.R, _t.Accent.G, _t.Accent.B)) : Brushes.Transparent;
            }
        }

        var entry = (item.Path, (Action<bool>)Paint);
        _rowPaint.Add(entry);
        grid.DetachedFromVisualTree += (_, _) => _rowPaint.Remove(entry);
        Paint(playing);
        return shell is not null ? shell : grid;
    }

    // ---- Menus (bottom sheet) -------------------------------------------------------------------

    private void OpenSheet(string title, IEnumerable<(string Label, Action Run)> entries)
    {
        if (_sheetPanel is not null)
        {
            _sheetPanel.VerticalAlignment = VerticalAlignment.Bottom;
            _sheetPanel.CornerRadius = new CornerRadius(16, 16, 0, 0);
            _sheetPanel.Margin = default;
        }

        _sheetTitle.Text = title;
        _sheetRows.Children.Clear();
        var first = true;
        foreach (var (label, run) in entries)
        {
            if (_t.Glass && !first)
                _sheetRows.Children.Add(new Border { Height = 1, Background = Brush(_t.Line), Margin = new Thickness(16, 0) });
            first = false;
            var text = Text(label, 16, _t.Ink);
            text.Margin = new Thickness(20, 0);
            text.VerticalAlignment = VerticalAlignment.Center;
            Control content = text;
            if (_t.Glass)
            {
                // Glass: a pale glyph in front of the label and a chevron after the ones that open another sheet (as in the Options sheet of the design).
                var glyph = _t.RowIcon(SheetIcon(label), 30);
                glyph.Margin = new Thickness(18, 0, 0, 0);
                text.Text = label.EndsWith("...", StringComparison.Ordinal) ? label[..^3] : label;
                text.Margin = new Thickness(14, 0);
                var chevron = Icon("M 7,4 L 13,10 L 7,16", Colors.White, 14, 2);
                chevron.Margin = new Thickness(0, 0, 18, 0);
                chevron.IsVisible = label.EndsWith("...", StringComparison.Ordinal);
                var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
                Grid.SetColumn(text, 1);
                Grid.SetColumn(chevron, 2);
                line.Children.Add(glyph);
                line.Children.Add(text);
                line.Children.Add(chevron);
                content = line;
            }
            else if (!_t.PictorialIcons && FluentIcons.ForGlyph(SheetIcon(label)) is { } fluent)
            {
                // The modern looks: a neutral Fluent icon in front of the label.
                var glyph = FluentIcons.Create(fluent, _t.Ink, 22);
                glyph.Margin = new Thickness(20, 0, 0, 0);
                text.Margin = new Thickness(16, 0, 20, 0);
                content = new StackPanel { Orientation = Orientation.Horizontal, Children = { glyph, text } };
            }

            var row = new Border { MinHeight = 54, Background = Brushes.Transparent, Child = content };
            row.Tapped += (_, _) =>
            {
                _sheet.IsVisible = false;
                run();
            };
            _sheetRows.Children.Add(row);
        }

        _sheetRows.Children.Add(_t.Glass ? _t.GlassPill("Close", () => _sheet.IsVisible = false) : new Border { Height = 12 });
        _sheet.IsVisible = true;
    }

    /// <summary>The pictorial icon of a menu row.</summary>
    private static string SheetIcon(string label) => label switch
    {
        "Play" => "@player",
        "Play next" => "@playnext",
        "Add to queue" => "@queue",
        "Add to playlist..." => "@list",
        "Edit info..." => "@pencil",
        "Add to favourites" => "@heartoff",
        "Remove from favourites" => "@heart",
        "Remove from library" => "@remove",
        "Delete" => "@remove",
        _ => "@note",
    };

    private void RowMenu(LibraryItem item) => OpenSheet(item.Title,
    [
        ("Play", () => _player.OpenItems([item.ToMediaItem()], 0)),
        ("Play next", () =>
        {
            var list = _player.CurrentPlaylist;
            list.Add(item.ToMediaItem());
            list.Move(list.Count - 1, Math.Min(list.Count - 1, list.CurrentIndex + 1));
        }),
        ("Add to queue", () => _player.CurrentPlaylist.Add(item.ToMediaItem())),
        ("Add to playlist...", () => PlaylistPicker(item)),
        (IsFavorite(item.Path) ? "Remove from favourites" : "Add to favourites", () => ToggleFavorite(item)),
        ("Edit info...", () => _host.EditTags(item.Path)),
        ("Remove from library", () => { _library.Remove([item.Path]); Render(_scroll.Offset.Y); }),
    ]);

    private void PlaylistPicker(LibraryItem item)
    {
        var store = _host.Playlists();
        var entries = new List<(string, Action)>();
        foreach (var name in store?.Names ?? [])
        {
            var captured = name;
            entries.Add((name, () => store!.Save(captured, store.Read(captured).Append(item.Path))));
        }

        entries.Add(("New playlist", () =>
        {
            var next = 1;
            while (store?.Names.Contains("Playlist " + next) == true)
                next++;
            store?.Save("Playlist " + next, [item.Path]);
        }));
        OpenSheet("Add to playlist", entries);
    }
}
