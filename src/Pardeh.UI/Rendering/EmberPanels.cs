using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Pardeh.Core.Library;
using Pardeh.Core.Playback;

namespace Pardeh.UI.Rendering;

/// <summary>What Ember's panels need from the main window.</summary>
internal interface IEmberHost
{
    MediaPlayer Player { get; }

    MediaLibrary Library { get; }

    PlaylistStore Playlists { get; }

    void OpenFiles();

    void OpenUrl();

    void AddFolder();

    /// <summary>Opens a full-mode page (library, radio …) in the stage.</summary>
    void ShowPage(string pane);

    /// <summary>Asks for a line of text (a playlist name); null when cancelled.</summary>
    Task<string?> Ask(string question, string initial);

    /// <summary>
    /// A panel's edge was dragged to a width in skin pixels, or double-clicked (null: the automatic width).
    /// <paramref name="done"/>: the drag ended, so the width is worth keeping.
    /// </summary>
    void ResizePanel(bool queue, double? width, bool done);
}

/// <summary>Shared look of Ember's panels (after the VLC Redesign by Juxtopposed, CC BY 4.0).</summary>
internal static class EmberStyle
{
    public static readonly IBrush Surface = Brush("#141414");
    public static readonly IBrush Raised = Brush("#1C1C1C");
    public static readonly IBrush Hover = Brush("#222222");
    public static readonly IBrush Line = Brush("#262626");
    public static readonly IBrush Ink = Brush("#E6E6E6");
    public static readonly IBrush Soft = Brush("#B4B4B4");
    public static readonly IBrush Dim = Brush("#8C8C8C");
    public static readonly IBrush Accent = Brush("#E5801F");
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));

    public static TextBlock Glyph(string glyph, double size = 15, IBrush? brush = null) =>
        new() { Text = glyph, FontFamily = Icons, FontSize = size, Foreground = brush ?? Soft, VerticalAlignment = VerticalAlignment.Center };

    /// <summary>A borderless icon button with a soft hover square.</summary>
    public static Border IconButton(string glyph, string tip, Action click, IBrush? brush = null)
    {
        var button = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(6),
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 15, Foreground = brush ?? Soft, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        ToolTip.SetTip(button, tip);
        button.PointerEntered += (_, _) => button.Background = Hover;
        button.PointerExited += (_, _) => button.Background = Brushes.Transparent;
        button.PointerPressed += (_, e) => { e.Handled = true; click(); };
        return button;
    }

    /// <summary>A list row: a small tile with an icon, the text, an optional right-hand note; click / double-click.</summary>
    public static Control Row(string glyph, string text, Action? click, Action? doubleClick = null, string? note = null, bool current = false)
    {
        var tile = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(4),
            Background = Raised,
            Child = new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 14, Foreground = current ? Accent : Soft, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        var label = new TextBlock { Text = text, FontSize = 13.5, Foreground = current ? Accent : Soft, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(10, 0, 8, 0) };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Children = { tile, label } };
        Grid.SetColumn(label, 1);
        if (note is not null)
        {
            var right = new TextBlock { Text = note, FontSize = 12.5, Foreground = current ? Accent : Dim, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);
        }

        var row = new Border { Height = 42, CornerRadius = new CornerRadius(6), Padding = new Thickness(4), Background = Brushes.Transparent, Child = grid, Cursor = new Cursor(StandardCursorType.Hand) };
        row.PointerEntered += (_, _) => row.Background = Hover;
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        row.PointerPressed += (_, e) =>
        {
            if (e.ClickCount >= 2)
                (doubleClick ?? click)?.Invoke();
            else
                click?.Invoke();
        };
        return row;
    }

    /// <summary>A plain "+ Add" style action row.</summary>
    public static Control ActionRow(string glyph, string text, Action click)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("32,*"),
            Children =
            {
                new TextBlock { Text = glyph, FontFamily = Icons, FontSize = 15, Foreground = Soft, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = text, FontSize = 13.5, Foreground = Soft, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), [Grid.ColumnProperty] = 1 },
            },
        };
        var row = new Border { Height = 40, CornerRadius = new CornerRadius(6), Padding = new Thickness(4), Background = Brushes.Transparent, Child = grid, Cursor = new Cursor(StandardCursorType.Hand) };
        row.PointerEntered += (_, _) => row.Background = Hover;
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        row.PointerPressed += (_, _) => click();
        return row;
    }

    /// <summary>A home-page section heading with a chevron that opens the full list.</summary>
    public static Control SectionHeader(string title, Action? open)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Height = 40,
            Background = Brushes.Transparent,
            Cursor = open is null ? null : new Cursor(StandardCursorType.Hand),
            Margin = new Thickness(0, 6, 0, 0),
            Children =
            {
                new TextBlock { Text = title, FontSize = 15, Foreground = Soft, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        if (open is not null)
        {
            var chevron = Glyph("", 12);
            Grid.SetColumn(chevron, 1);
            grid.Children.Add(chevron);
            grid.PointerPressed += (_, _) => open();
        }

        return grid;
    }

    public static TextBlock Hint(string text) =>
        new() { Text = text, FontSize = 13, Foreground = Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };

    public static string Time(double seconds) => seconds <= 0 ? string.Empty
        : seconds >= 3600 ? TimeSpan.FromSeconds(seconds).ToString(@"h\:mm\:ss") : TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss");

    /// <summary>Dark Fluent controls with orange accents inside the panel.</summary>
    public static Control Wrap(Control content, bool card)
    {
        var scope = new ThemeVariantScope
        {
            RequestedThemeVariant = ThemeVariant.Dark,
            Child = new Border
            {
                Background = Surface,
                CornerRadius = card ? new CornerRadius(10) : default,
                BorderBrush = card ? Line : null,
                BorderThickness = card ? new Thickness(1) : default,
                ClipToBounds = true,
                Child = content,
            },
        };
        foreach (var key in new[] { "ToggleSwitchFillOn", "SliderTrackValueFill", "SliderThumbBackground", "TextControlBorderBrushFocused" })
            scope.Resources[key] = Accent;
        UiTheme.UseFluentScrolling(scope);
        return scope;
    }

    /// <summary>Puts a drag handle on a panel's inner edge: its right edge, or its left one for the queue.</summary>
    public static Control Resizable(Control panel, Control content, IEmberHost host, bool queue)
    {
        var grip = new EdgeGrip(leftEdge: queue, Accent, () => panel.Parent as Visual, () => panel.Width,
            (width, done) => host.ResizePanel(queue, width, done))
        {
            HorizontalAlignment = queue ? HorizontalAlignment.Left : HorizontalAlignment.Right,
        };
        return new Grid { Children = { content, grip } };
    }

    public static ScrollViewer Scroll(Control content) =>
        new() { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
}

/// <summary>
/// Ember's library side panel (after the VLC Redesign): back / forward, a breadcrumb, "N items" with sort, view and
/// search, an Add row, and rows with tiles. Pages: Home (continue watching, listen again, folders, playlists,
/// artists), Videos, Music (songs / albums / artists / genres and their contents), Network streams, Playlists and
/// Recent files. Single click selects, double-click (or click on a media row) plays.
/// </summary>
internal sealed class EmberLibraryPanel : UserControl
{
    private readonly IEmberHost _host;
    private readonly List<string> _back = [], _forward = [];
    private readonly TextBlock _crumb = new() { FontSize = 15, Foreground = EmberStyle.Soft, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _count = new() { FontSize = 13, Foreground = EmberStyle.Soft, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _list = new() { Spacing = 2 };
    private readonly TextBox _search = new() { PlaceholderText = "Search", IsVisible = false, Margin = new Thickness(0, 0, 0, 8) };
    private bool _sortByDate;

    public string Page { get; private set; } = "home";

    public EmberLibraryPanel(IEmberHost host, string page)
    {
        _host = host;
        _search.TextChanged += (_, _) => Render();

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"), Height = 44 };
        header.Children.Add(EmberStyle.IconButton("", "Back", GoBack));
        var forward = EmberStyle.IconButton("", "Forward", GoForward);
        Grid.SetColumn(forward, 1);
        Grid.SetColumn(_crumb, 2);
        _crumb.Margin = new Thickness(8, 0, 0, 0);
        var more = EmberStyle.IconButton("", "More", () => { });
        more.PointerPressed += (_, _) => MoreMenu(more);
        Grid.SetColumn(more, 3);
        var popOut = EmberStyle.IconButton("", "Open as a full page", () => _host.ShowPage(Page is "network" ? "radio" : "library"));
        Grid.SetColumn(popOut, 4);
        header.Children.AddRange([forward, _crumb, more, popOut]);

        var tools = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), Height = 40 };
        tools.Children.Add(_count);
        var sort = EmberStyle.IconButton("", "Sort by name or date", () => { _sortByDate = !_sortByDate; Render(); });
        var view = EmberStyle.IconButton("", "List view", () => { });
        var find = EmberStyle.IconButton("", "Search", () => { _search.IsVisible = !_search.IsVisible; if (_search.IsVisible) _search.Focus(); else _search.Text = string.Empty; });
        Grid.SetColumn(sort, 1);
        Grid.SetColumn(view, 2);
        Grid.SetColumn(find, 3);
        tools.Children.AddRange([sort, view, find]);

        var top = new StackPanel { Margin = new Thickness(18, 12, 18, 0), Children = { header, tools, new Border { Height = 1, Background = EmberStyle.Line, Margin = new Thickness(0, 6, 0, 10) }, _search } };
        DockPanel.SetDock(top, Dock.Top);
        Content = EmberStyle.Resizable(this, EmberStyle.Wrap(new DockPanel { Children = { top, new ScrollViewer { Content = _list, Margin = new Thickness(18, 0, 10, 12), Padding = new Thickness(0, 0, 8, 0), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } } }, card: false), host, queue: false);
        Open(page, remember: false);
    }

    /// <summary>Shows a page (home, videos, music, network, playlists, recent; or music/albums … drill-downs).</summary>
    public void Open(string page, bool remember = true)
    {
        if (remember && page != Page)
        {
            _back.Add(Page);
            _forward.Clear();
        }

        Page = page;
        _search.Text = string.Empty;
        _search.IsVisible = false;
        Render();
    }

    /// <summary>The library or playlists changed: show the current page again.</summary>
    public void Refresh() => Dispatcher.UIThread.Post(Render);

    private void GoBack()
    {
        if (_back.Count == 0)
            return;
        _forward.Add(Page);
        Page = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        Render();
    }

    private void GoForward()
    {
        if (_forward.Count == 0)
            return;
        _back.Add(Page);
        Page = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        Render();
    }

    private void MoreMenu(Control anchor)
    {
        var menu = new ContextMenu
        {
            ItemsSource = new object[]
            {
                new MenuItem { Header = "Open File(s)...", Command = new Relay(_host.OpenFiles) },
                new MenuItem { Header = "Add Folder to Library...", Command = new Relay(_host.AddFolder) },
                new MenuItem { Header = "Search Library Again", Command = new Relay(() => _ = _host.Library.ScanAsync()) },
                new MenuItem { Header = "Open Network Stream...", Command = new Relay(_host.OpenUrl) },
            },
        };
        menu.Open(anchor);
    }

    private sealed class Relay(Action action) : System.Windows.Input.ICommand
    {
#pragma warning disable CS0067
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }

    // ---- Pages ------------------------------------------------------------------------------

    private string Filter => _search.Text?.Trim() ?? string.Empty;

    private IEnumerable<LibraryItem> Matching(IEnumerable<LibraryItem> items)
    {
        if (Filter.Length > 0)
            items = items.Where(i => $"{i.Title} {i.Artist} {i.Album}".Contains(Filter, StringComparison.OrdinalIgnoreCase));
        return _sortByDate ? items.OrderByDescending(i => i.Modified) : items.OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase);
    }

    private void Play(IReadOnlyList<LibraryItem> items, int index) =>
        _host.Player.OpenItems(items.Select(i => i.ToMediaItem()), index);

    private void AddItems(IReadOnlyList<LibraryItem> items, string glyph, Func<LibraryItem, string?>? note = null)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var index = i;
            var current = string.Equals(_host.Player.CurrentMedia?.Source, items[i].Path, StringComparison.OrdinalIgnoreCase);
            _list.Children.Add(EmberStyle.Row(glyph, items[i].Title, () => Play(items, index), note: note?.Invoke(items[i]) ?? EmberStyle.Time(items[i].DurationSeconds), current: current));
        }
    }

    private void Render()
    {
        _list.Children.Clear();
        var library = _host.Library;
        var all = library.Music.Concat(library.Video).ToList();
        string title;
        var count = 0;

        switch (Page)
        {
            case "videos":
            {
                title = "Videos";
                var videos = Matching(library.Video).ToList();
                count = videos.Count;
                _list.Children.Add(EmberStyle.ActionRow("", "Add", _host.AddFolder));
                if (videos.Count == 0)
                    _list.Children.Add(EmberStyle.Hint("Add a folder to your library to see your media here."));
                AddItems(videos, "");
                break;
            }

            case "music":
                title = "Music";
                count = 4;
                _list.Children.Add(EmberStyle.ActionRow("", "Add", _host.AddFolder));
                _list.Children.Add(EmberStyle.Row("", "Songs", () => Open("music/songs")));
                _list.Children.Add(EmberStyle.Row("", "Albums", () => Open("music/albums")));
                _list.Children.Add(EmberStyle.Row("", "Artists", () => Open("music/artists")));
                _list.Children.Add(EmberStyle.Row("", "Genres", () => Open("music/genres")));
                break;

            case "music/songs":
            {
                title = "Music › Songs";
                var songs = Matching(library.Music).ToList();
                count = songs.Count;
                AddItems(songs, "");
                break;
            }

            case "music/albums" or "music/artists" or "music/genres":
            {
                var kind = Page["music/".Length..];
                title = "Music › " + char.ToUpperInvariant(kind[0]) + kind[1..];
                Func<LibraryItem, string> key = kind switch { "albums" => i => i.Album, "artists" => i => i.Artist, _ => i => i.Genre };
                var groups = library.Music.GroupBy(i => string.IsNullOrWhiteSpace(key(i)) ? "Unknown" : key(i))
                    .Where(g => Filter.Length == 0 || g.Key.Contains(Filter, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase).ToList();
                count = groups.Count;
                var glyph = kind switch { "albums" => "", "artists" => "", _ => "" };
                foreach (var group in groups)
                {
                    var name = group.Key;
                    _list.Children.Add(EmberStyle.Row(glyph, name, () => Open($"{Page}/{name}"), note: $"{group.Count()} songs"));
                }

                break;
            }

            case var drill when drill.StartsWith("music/", StringComparison.Ordinal) && drill.Count(c => c == '/') >= 2:
            {
                var parts = drill.Split('/', 3);
                var name = parts[2];
                title = name;
                Func<LibraryItem, string> key = parts[1] switch { "albums" => i => i.Album, "artists" => i => i.Artist, _ => i => i.Genre };
                var songs = library.Music.Where(i => (string.IsNullOrWhiteSpace(key(i)) ? "Unknown" : key(i)) == name)
                    .OrderBy(i => i.TrackNumber).ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
                count = songs.Count;
                AddItems(songs, "");
                break;
            }

            case "network":
                title = "Network streams";
                _list.Children.Add(EmberStyle.ActionRow("", "Add Network Stream", _host.OpenUrl));
                _list.Children.Add(EmberStyle.Row("", "Radio Tuner", () => _host.ShowPage("radio"), note: "Stations"));
                _list.Children.Add(EmberStyle.Hint("Open a network stream (a URL, .m3u or radio station) to play it."));
                count = 1;
                break;

            case "playlists":
            {
                title = "Playlists";
                var names = _host.Playlists.Names.Where(n => Filter.Length == 0 || n.Contains(Filter, StringComparison.OrdinalIgnoreCase)).ToList();
                count = names.Count;
                _list.Children.Add(EmberStyle.ActionRow("", "Create playlist", CreatePlaylist));
                if (names.Count == 0)
                    _list.Children.Add(EmberStyle.Hint("Create a playlist to collect your favorite media. It saves what's in the queue now."));
                foreach (var name in names)
                {
                    var playlist = name;
                    var size = _host.Playlists.Read(playlist).Count;
                    _list.Children.Add(EmberStyle.Row("", playlist, () => _host.Player.OpenFiles(_host.Playlists.Read(playlist)), note: size == 1 ? "1 item" : $"{size} items"));
                }

                break;
            }

            case "recent":
            {
                title = "Recent files";
                var recent = all.Where(i => i.PlayCount > 0).OrderByDescending(i => i.LastPlayed)
                    .Where(i => Filter.Length == 0 || i.Title.Contains(Filter, StringComparison.OrdinalIgnoreCase)).Take(60).ToList();
                count = recent.Count;
                if (recent.Count == 0)
                    _list.Children.Add(EmberStyle.Hint("Things you play from the library show up here."));
                AddItems(recent, "", i => i.LastPlayed.ToLocalTime().ToString("d MMM"));
                break;
            }

            default:
            {
                title = "Home";
                var continueWatching = all.Where(i => i.IsVideo && i.PlayCount > 0).OrderByDescending(i => i.LastPlayed).Take(3).ToList();
                var listenAgain = all.Where(i => !i.IsVideo && i.PlayCount > 0).OrderByDescending(i => i.LastPlayed).Take(3).ToList();
                var artists = library.Music.Where(i => !string.IsNullOrWhiteSpace(i.Artist)).GroupBy(i => i.Artist)
                    .OrderByDescending(g => g.Sum(i => i.PlayCount)).ThenByDescending(g => g.Count()).Take(3).ToList();
                count = 5;

                _list.Children.Add(EmberStyle.SectionHeader("Continue Watching", () => Open("videos")));
                if (continueWatching.Count == 0)
                    _list.Children.Add(EmberStyle.Hint("Watch a video to see it here."));
                AddItems(continueWatching, "");

                _list.Children.Add(EmberStyle.SectionHeader("Listen Again", () => Open("recent")));
                if (listenAgain.Count == 0)
                    _list.Children.Add(EmberStyle.Hint("Play music to see it here."));
                AddItems(listenAgain, "");

                _list.Children.Add(EmberStyle.SectionHeader("Library Folders", null));
                foreach (var folder in library.Folders.Take(4))
                    _list.Children.Add(EmberStyle.Row("", Path.GetFileName(folder.TrimEnd('\\', '/')) is { Length: > 0 } leaf ? leaf : folder, () => Open("videos"), note: null));
                if (library.Folders.Count == 0)
                    _list.Children.Add(EmberStyle.ActionRow("", "Add a folder", _host.AddFolder));

                _list.Children.Add(EmberStyle.SectionHeader("Playlists", () => Open("playlists")));
                var names = _host.Playlists.Names.Take(3).ToList();
                if (names.Count == 0)
                    _list.Children.Add(EmberStyle.Hint("Open a playlist to see it here."));
                foreach (var name in names)
                {
                    var playlist = name;
                    _list.Children.Add(EmberStyle.Row("", playlist, () => _host.Player.OpenFiles(_host.Playlists.Read(playlist))));
                }

                _list.Children.Add(EmberStyle.SectionHeader("Frequently Listened Artists", () => Open("music/artists")));
                if (artists.Count == 0)
                    _list.Children.Add(EmberStyle.Hint("Play music to see artists here."));
                foreach (var group in artists)
                {
                    var name = group.Key;
                    _list.Children.Add(EmberStyle.Row("", name, () => Open($"music/artists/{name}"), note: $"{group.Count()} songs"));
                }

                break;
            }
        }

        _crumb.Text = title;
        _count.Text = count == 1 ? "1 item" : $"{count} items";
    }

    private async void CreatePlaylist()
    {
        var name = await _host.Ask("Name of the new playlist:", "My playlist");
        if (string.IsNullOrWhiteSpace(name))
            return;
        _host.Playlists.Save(name.Trim(), _host.Player.CurrentPlaylist.Items.Select(i => i.Source));
        Render();
    }
}

/// <summary>
/// Ember's floating queue card (after the VLC Redesign): Queue, Bookmarks and Chapters tabs with an orange underline.
/// Queue: "N items - length", repeat / shuffle (orange when on), remove and add; the playing item in orange with its
/// length. Bookmarks: saved times in the current item (+ adds the current time). Chapters: the item's chapters.
/// </summary>
internal sealed class EmberQueuePanel : UserControl
{
    private readonly IEmberHost _host;
    private readonly ContentControl _page = new();
    private readonly List<(string Key, Border Underline, TextBlock Label, TextBlock Icon)> _tabs = [];
    private int _selected = -1;

    /// <summary>Bookmarks per item (source → seconds), saved in %APPDATA%\Pardeh\bookmarks.json.</summary>
    private static BookmarkStore Bookmarks => AppBookmarks.Store; // shared with the seek bar markers

    public string Tab { get; private set; } = "queue";

    public EmberQueuePanel(IEmberHost host)
    {
        _host = host;
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), Height = 50, Margin = new Thickness(12, 0) };
        var tabs = new[] { ("queue", "", "Queue"), ("bookmarks", "", "Bookmarks"), ("chapters", "", "Chapters") };
        for (var i = 0; i < tabs.Length; i++)
        {
            var (key, glyph, name) = tabs[i];
            var icon = EmberStyle.Glyph(glyph, 13);
            icon.Margin = new Thickness(0, 0, 7, 0);
            var label = new TextBlock { Text = name, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            var underline = new Border { Height = 2, Background = EmberStyle.Accent, VerticalAlignment = VerticalAlignment.Bottom };
            var cell = new Grid
            {
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Children = { new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { icon, label } }, underline },
            };
            cell.PointerPressed += (_, _) => Show(key);
            Grid.SetColumn(cell, i);
            header.Children.Add(cell);
            _tabs.Add((key, underline, label, icon));
        }

        DockPanel.SetDock(header, Dock.Top);
        Content = EmberStyle.Resizable(this, EmberStyle.Wrap(new DockPanel { Children = { header, _page } }, card: true), host, queue: true);
        _host.Player.CurrentPlaylist.Changed += (_, _) => Refresh();
        _host.Player.CurrentMediaChanged += (_, _) => Refresh();
        Show("queue");
        var follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        follow.Tick += (_, _) => FollowChapter();
        follow.Start();
    }

    public void Refresh() => Dispatcher.UIThread.Post(() => { if (IsVisible) Show(Tab); });

    public void Show(string tab)
    {
        Tab = tab;
        foreach (var (key, underline, label, icon) in _tabs)
        {
            var on = key == tab;
            underline.IsVisible = on;
            label.Foreground = on ? EmberStyle.Accent : EmberStyle.Dim;
            icon.Foreground = on ? EmberStyle.Accent : EmberStyle.Dim;
        }

        _page.Content = tab switch
        {
            "bookmarks" => BookmarksPage(),
            "chapters" => ChaptersPage(),
            _ => QueuePage(),
        };
    }

    private static Grid Toolbar(string text, params Control[] buttons)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*" + string.Concat(buttons.Select(_ => ",Auto"))), Height = 44, Margin = new Thickness(26, 8, 14, 4) };
        grid.Children.Add(new TextBlock { Text = text, FontSize = 13.5, Foreground = EmberStyle.Soft, VerticalAlignment = VerticalAlignment.Center });
        for (var i = 0; i < buttons.Length; i++)
        {
            Grid.SetColumn(buttons[i], i + 1);
            grid.Children.Add(buttons[i]);
        }

        return grid;
    }

    private Control QueuePage()
    {
        var player = _host.Player;
        var items = player.CurrentPlaylist.Items;
        var total = items.Sum(i => i.Duration.TotalSeconds);
        var summary = (items.Count == 1 ? "1 item" : $"{items.Count} items") + (total > 0 ? " - " + Length(total) : string.Empty);
        var toolbar = Toolbar(summary,
            EmberStyle.IconButton("", "Sort by name", () =>
            {
                var sorted = items.Select((item, index) => (item, index)).OrderBy(p => p.item.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                for (var target = 0; target < sorted.Count; target++)
                {
                    var from = player.CurrentPlaylist.Items.ToList().IndexOf(sorted[target].item);
                    if (from != target)
                        player.CurrentPlaylist.Move(from, target);
                }
            }),
            EmberStyle.IconButton("", "Repeat", () => { player.Loop = !player.Loop; Show("queue"); }, player.Loop ? EmberStyle.Accent : null),
            EmberStyle.IconButton("", "Shuffle", () => { player.Shuffle = !player.Shuffle; Show("queue"); }, player.Shuffle ? EmberStyle.Accent : null),
            EmberStyle.IconButton("", "Remove the selected item", () =>
            {
                if (_selected >= 0 && _selected < player.CurrentPlaylist.Count)
                    player.CurrentPlaylist.RemoveAt(_selected);
                _selected = -1;
            }),
            EmberStyle.IconButton("", "Add files", _host.OpenFiles));

        var list = new StackPanel { Spacing = 2, Margin = new Thickness(14, 4, 14, 14) };
        if (items.Count == 0)
            list.Children.Add(new TextBlock { Text = "The queue is empty. Add files with +, or drop them on the player.", FontSize = 13, Foreground = EmberStyle.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 4) });
        for (var i = 0; i < items.Count; i++)
        {
            var index = i;
            var current = i == player.CurrentPlaylist.CurrentIndex;
            var row = QueueRow(items[i].Name, items[i].Duration.TotalSeconds > 0 ? EmberStyle.Time(items[i].Duration.TotalSeconds) : string.Empty, current, index == _selected);
            row.PointerPressed += (_, e) =>
            {
                if (e.ClickCount >= 2)
                {
                    player.PlayItem(index);
                    return;
                }

                _selected = index;
                Show("queue");
            };
            list.Children.Add(row);
        }

        DockPanel.SetDock(toolbar, Dock.Top);
        return new DockPanel { Children = { toolbar, EmberStyle.Scroll(list) } };
    }

    private static Border QueueRow(string name, string length, bool current, bool selected)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("26,*,Auto"),
            Children =
            {
                new TextBlock { Text = current ? "" : string.Empty, FontFamily = EmberStyle.Icons, FontSize = 12, Foreground = EmberStyle.Accent, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = name, FontSize = 13.5, Foreground = current ? EmberStyle.Accent : EmberStyle.Soft, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, [Grid.ColumnProperty] = 1 },
                new TextBlock { Text = length, FontSize = 13, Foreground = current ? EmberStyle.Ink : EmberStyle.Dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0), [Grid.ColumnProperty] = 2 },
            },
        };
        var row = new Border { Height = 40, CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 0), Background = selected ? EmberStyle.Raised : Brushes.Transparent, Child = grid, Cursor = new Cursor(StandardCursorType.Hand) };
        row.PointerEntered += (_, _) => { if (!selected) row.Background = EmberStyle.Hover; };
        row.PointerExited += (_, _) => { if (!selected) row.Background = Brushes.Transparent; };
        return row;
    }

    private Control BookmarksPage()
    {
        var player = _host.Player;
        var source = player.CurrentMedia?.Source;
        var marks = source is null ? [] : Bookmarks.Get(source);
        var toolbar = Toolbar(marks.Count == 1 ? "1 bookmark" : $"{marks.Count} bookmarks",
            EmberStyle.IconButton("", "Remove the selected bookmark", () =>
            {
                if (source is not null && _selected >= 0 && _selected < marks.Count)
                    Bookmarks.RemoveAt(source, _selected);
                _selected = -1;
                Show("bookmarks");
            }),
            EmberStyle.IconButton("", "Bookmark the current time", () =>
            {
                if (source is null || Pardeh.Core.Playback.NetworkMedia.HasSecret(source))
                    return;
                Bookmarks.Add(source, Math.Round(player.CurrentPosition, 1));
                Show("bookmarks");
            }));
        var page = new StackPanel { Spacing = 2, Margin = new Thickness(14, 4, 14, 14) };
        if (marks.Count == 0)
            page.Children.Add(new TextBlock { Text = source is null ? "Play something to bookmark times in it." : "Select + to save the current time.", FontSize = 13, Foreground = EmberStyle.Dim, Margin = new Thickness(12, 4) });
        for (var i = 0; i < marks.Count; i++)
        {
            var index = i;
            var seconds = marks[i];
            var row = QueueRow($"Bookmark {i + 1}", EmberStyle.Time(seconds) is { Length: > 0 } t ? t : "00:00", false, i == _selected);
            row.PointerPressed += (_, e) =>
            {
                _selected = index;
                player.CurrentPosition = seconds;
                if (e.ClickCount < 2)
                    Show("bookmarks");
            };
            page.Children.Add(row);
        }

        DockPanel.SetDock(toolbar, Dock.Top);
        return new DockPanel { Children = { toolbar, EmberStyle.Scroll(page) } };
    }

    private static int ChapterIndexAt(IReadOnlyList<Chapter> chapters, double seconds)
    {
        var index = -1;
        for (var i = 0; i < chapters.Count; i++)
            if (chapters[i].Start.TotalSeconds <= seconds + 0.05)
                index = i;
        return index;
    }

    private Control ChaptersPage()
    {
        var player = _host.Player;
        var chapters = player.CurrentMedia is null ? [] : player.Chapters;
        if (chapters.Count == 0)
            return new TextBlock { Text = "This media has no chapters.", FontSize = 13, Foreground = EmberStyle.Dim, Margin = new Thickness(26, 18) };

        var current = ChapterIndexAt(chapters, player.CurrentPosition);
        _shownChapter = current;
        var toolbar = Toolbar(chapters.Count == 1 ? "1 chapter" : $"{chapters.Count} chapters");
        var page = new StackPanel { Spacing = 2, Margin = new Thickness(14, 4, 14, 14) };
        for (var i = 0; i < chapters.Count; i++)
        {
            var chapter = chapters[i];
            var row = QueueRow(chapter.Title, EmberStyle.Time(chapter.Start.TotalSeconds) is { Length: > 0 } t ? t : "00:00", i == current, false);
            row.PointerPressed += (_, _) => player.CurrentPosition = chapter.Start.TotalSeconds;
            page.Children.Add(row);
        }

        DockPanel.SetDock(toolbar, Dock.Top);
        return new DockPanel { Children = { toolbar, EmberStyle.Scroll(page) } };
    }

    private int _shownChapter = -2;

    /// <summary>Re-renders the Chapters tab when playback moves into another chapter.</summary>
    private void FollowChapter()
    {
        if (!IsVisible || Tab != "chapters" || _host.Player.CurrentMedia is null)
            return;
        var chapters = _host.Player.Chapters;
        if (chapters.Count > 0 && ChapterIndexAt(chapters, _host.Player.CurrentPosition) != _shownChapter)
            Show("chapters");
    }

    private static string Length(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds}s" : $"{t.Seconds}s";
    }
}
