using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Pardeh.Core.Library;
using Pardeh.Core.Playback;
using Path = Avalonia.Controls.Shapes.Path;

namespace Pardeh.UI.Rendering;

/// <summary>
/// The sorting, filtering and favourites side of the phone Library: a sort button (opens a sheet with the fields and an ascending /
/// descending switch; the choice is remembered per level), chips (favourites only, audio, video, play all), a filter that narrows the
/// open list as you type, the Favourites level and the heart marks. Sorting is stable and culture-safe (<see cref="TrackSort"/>).
/// </summary>
internal sealed partial class MobileLibraryView
{
    /// <summary>The open page's way to take a live filter text (null when the page cannot be filtered: the search then covers the whole library).</summary>
    private Action<string>? _applyFilter;

    /// <summary>What to do when a favourite changes while a list is open (the favourites level and the favourites-only chip drop rows).</summary>
    private Action? _activePage;

    private bool _searchLocal;

    private readonly Dictionary<string, (bool Favorites, int Kind)> _chips = [];
    private readonly List<(string Path, Action<bool> Set)> _heartPaint = [];

    private static readonly Color HeartPink = Color.Parse("#FF6B8A");

    private static bool MatchesText(LibraryItem i, string text) =>
        QueueFilter.Contains(i.Title, text) || QueueFilter.Contains(i.Artist, text) || QueueFilter.Contains(i.Album, text) || QueueFilter.Contains(i.Genre, text);

    private void OnSearchText()
    {
        var text = (_searchBox.Text ?? string.Empty).Trim();
        if (_searchLocal)
            _applyFilter?.Invoke(text);
        else
            ShowSearch();
    }

    // ---- Favourites -----------------------------------------------------------------------------

    private bool IsFavorite(string path) => _player.Favorites?.IsFavorite(path) == true;

    /// <summary>The favourite tracks, oldest favourite first: the library's own item when it is in the library, else one made from what was stored (a file opened by hand).</summary>
    private List<LibraryItem> FavoriteItems()
    {
        if (_player.Favorites is not { } store)
            return [];
        var known = new Dictionary<string, LibraryItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _library.Items)
            known.TryAdd(FavoritesStore.KeyOf(item.Path), item);
        var result = new List<LibraryItem>();
        foreach (var entry in store.Entries)
        {
            if (known.TryGetValue(entry.Key, out var item))
                result.Add(item);
            else
                result.Add(new LibraryItem(entry.Path, entry.Title.Length > 0 ? entry.Title : System.IO.Path.GetFileNameWithoutExtension(MediaSources.DisplayName(entry.Path)),
                    entry.Artist, string.Empty, string.Empty, string.Empty, 0, 0, false, 0, entry.Added));
        }

        return result;
    }

    /// <summary>The Library home's "Favourites" row (a glossy heart tile on Glass).</summary>
    private Control FavoritesRow() => CategoryRow("@heart", "Favourites", _player.Favorites?.Count ?? 0, () => Push("Favourites", FavoritesPage), "favourites");

    private void OnFavoritesChanged()
    {
        foreach (var (path, set) in _heartPaint.ToList())
            set(IsFavorite(path));
        _activePage?.Invoke();
    }

    private void ToggleFavorite(LibraryItem item)
    {
        if (_player.Favorites is not { } store)
            return;
        store.Toggle(item.Path, item.Title, item.Artist);
    }

    /// <summary>The heart of a favourite row (a glossy one on the pictorial looks, a Fluent one on the modern looks).</summary>
    private Control HeartMark(double size) => _t.PictorialIcons ? _t.RowIcon("@heart", size + 2) : FluentIcons.Create("heart", HeartPink, size, filled: true);

    // ---- The bar above a list: sort button, chips, play all ----------------------------------------

    private Border Chip(string label, Control? icon, bool on, Action tap)
    {
        var ink = on ? (_t.Glass || _t.Royal ? Colors.White : _t.Accent) : _t.Ink;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (icon is not null)
            row.Children.Add(icon);
        row.Children.Add(Text(label, 14, ink, on));
        row.Children[^1].VerticalAlignment = VerticalAlignment.Center;
        IBrush background;
        IBrush border;
        if (_t.Glass)
        {
            background = on ? _t.SelectedBrush! : Vertical(("#1D4FC2", 0), ("#0D3396", 0.5), ("#082675", 1));
            border = Brush(_t.G(on ? "#BBDCFF" : "#6FA9F5"));
        }
        else if (_t.Royal)
        {
            background = on ? _t.SelectedBrush! : Brush(_t.Card);
            border = Brush(on ? Color.Parse("#1D3C9C") : _t.Line);
        }
        else
        {
            background = on ? Brush(Color.FromArgb(48, _t.Accent.R, _t.Accent.G, _t.Accent.B)) : Brushes.Transparent;
            border = Brush(on ? _t.Accent : _t.Line);
        }

        var chip = new Border
        {
            Height = 36, MinWidth = 44, CornerRadius = new CornerRadius(_t.Royal ? 6 : 18), Padding = new Thickness(12, 0), Margin = new Thickness(0, 0, 8, 8),
            Background = background, BorderBrush = border, BorderThickness = new Thickness(1.2), Child = row,
        };
        chip.Tapped += (_, _) => tap();
        return chip;
    }

    private Control SortGlyph(double size = 20) => _t.PictorialIcons ? _t.RowIcon("@sort", size + 4) : FluentIcons.Create("arrow_sort", _t.Ink, size);

    private Control HeartGlyph(bool on, double size = 18) => _t.PictorialIcons ? _t.RowIcon(on ? "@heart" : "@heartoff", size + 4) : FluentIcons.Create("heart", on ? _t.Accent : _t.Ink, size, filled: on);

    /// <summary>The compact sort button: icon, the field's name and a small arrow for the direction.</summary>
    private Border SortButton(SortSpec sort, Action tap)
    {
        var up = !sort.Descending;
        var arrow = new Path
        {
            Data = Geometry.Parse(up ? "M 0,6 L 4.5,0 L 9,6 Z" : "M 0,0 L 4.5,6 L 9,0 Z"), Fill = Brush(_t.Glass || _t.Royal ? Colors.White : _t.Accent), Width = 9, Height = 6,
            VerticalAlignment = VerticalAlignment.Center, Stretch = Stretch.Uniform,
        };
        var label = Text(SortSpec.Label(sort.Field), 14, _t.Glass || _t.Royal ? Colors.White : _t.Ink, true);
        label.VerticalAlignment = VerticalAlignment.Center;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center, Children = { SortGlyph(), label, arrow } };
        var button = new Border
        {
            Height = 36, CornerRadius = new CornerRadius(_t.Royal ? 6 : 18), Padding = new Thickness(12, 0), Margin = new Thickness(0, 0, 8, 8), Child = row, BorderThickness = new Thickness(1.2),
            Background = _t.Glass || _t.Royal ? _t.SelectedBrush : Brush(Color.FromArgb(34, _t.Ink.R, _t.Ink.G, _t.Ink.B)),
            BorderBrush = _t.Glass ? Brush(_t.G("#BBDCFF")) : _t.Royal ? Brush(Color.Parse("#1D3C9C")) : Brush(_t.Line),
        };
        button.Tapped += (_, _) => tap();
        return button;
    }

    // ---- Song lists -----------------------------------------------------------------------------

    /// <summary>
    /// A list of songs with the sort button and the chips above it. <paramref name="level"/> names the place ("songs", "videos", "playlist" ...)
    /// whose sort choice is remembered. The given order is the list's "Default order" (a playlist's order, newest first ...); "songs" and
    /// "videos" default to Title. The search box filters this list while it is open.
    /// </summary>
    private Control SongList(List<LibraryItem> source, string empty, string level = "songs")
    {
        var hasDefault = level is not ("songs" or "videos");
        var fallback = new SortSpec(hasDefault ? SortField.Default : SortField.Title);
        var sort = AppSettings.Current.SortOf(level, fallback);
        if (!hasDefault && sort.Field is SortField.Default or SortField.SongCount)
            sort = fallback;
        var (favoritesOnly, kind) = _chips.GetValueOrDefault(level);
        var text = string.Empty;
        var shown = new List<LibraryItem>();
        var bar = new ContentControl { Margin = new Thickness(14, 10, 14, 0) };
        var host = new ContentControl { Background = Brushes.Transparent };
        var isFavoritesLevel = level == "favorites";
        var page = new StackPanel { Children = { bar, host } };

        void Refresh()
        {
            var all = isFavoritesLevel ? FavoriteItems() : source;
            var query = all.AsEnumerable();
            if (favoritesOnly && !isFavoritesLevel)
                query = query.Where(i => IsFavorite(i.Path));
            if (kind == 1)
                query = query.Where(i => !i.IsVideo);
            else if (kind == 2)
                query = query.Where(i => i.IsVideo);
            if (text.Length > 0)
                query = query.Where(i => MatchesText(i, text));
            shown = sort.Field == SortField.QueueOrder ? InQueueOrder(query, sort.Descending) : TrackSort.Sort(query, sort);

            // The bar: sort button, favourites-only / audio / video chips (the kind chips only when the list has both), play all, and the count.
            var chips = new WrapPanel();
            chips.Children.Add(SortButton(sort, () => OpenSortSheet("Sort by", [.. TrackSort.Fields(hasDefault), SortField.QueueOrder], sort, spec =>
            {
                sort = spec;
                AppSettings.Current.RememberSort(level, sort);
                Refresh();
            })));
            if (!isFavoritesLevel)
                chips.Children.Add(Chip("Favourites", HeartGlyph(favoritesOnly), favoritesOnly, () =>
                {
                    favoritesOnly = !favoritesOnly;
                    _chips[level] = (favoritesOnly, kind);
                    Refresh();
                }));
            if (all.Any(i => i.IsVideo) && all.Any(i => !i.IsVideo))
            {
                chips.Children.Add(Chip("Audio", null, kind == 1, () => { kind = kind == 1 ? 0 : 1; _chips[level] = (favoritesOnly, kind); Refresh(); }));
                chips.Children.Add(Chip("Video", null, kind == 2, () => { kind = kind == 2 ? 0 : 2; _chips[level] = (favoritesOnly, kind); Refresh(); }));
            }

            if (shown.Count > 0)
            {
                var play = new Path { Data = Geometry.Parse("M 0,0 L 9,5 L 0,10 Z"), Fill = Brush(_t.Glass ? Colors.White : _t.Royal ? Color.Parse("#2E55C4") : _t.Ink), Width = 9, Height = 10, VerticalAlignment = VerticalAlignment.Center };
                var snapshot = shown;
                chips.Children.Add(Chip("Play all", play, false, () => _player.OpenItems(snapshot.Select(t => t.ToMediaItem()), 0)));
                var shuffleIcon = new Path { Data = Geometry.Parse("M 0,8 L 3,8 L 7,2 L 10,2 M 8,0 L 10,2 L 8,4 M 0,2 L 3,2 L 7,8 L 10,8 M 8,6 L 10,8 L 8,10"), Stroke = Brush(_t.Glass ? Colors.White : _t.Royal ? Color.Parse("#2E55C4") : _t.Ink), StrokeThickness = 1.4, Width = 11, Height = 10, VerticalAlignment = VerticalAlignment.Center };
                chips.Children.Add(Chip("Shuffle", shuffleIcon, false, () =>
                {
                    // Random start without picking a song: shuffle mode on, the queue in a random order, play from its first item.
                    var order = snapshot.Select(t => t.ToMediaItem()).OrderBy(_ => Random.Shared.Next()).ToList();
                    _player.Shuffle = true;
                    _player.OpenItems(order, 0);
                }));
            }

            var filtered = shown.Count != all.Count;
            var count = Text(all.Count == 0 ? string.Empty : filtered ? $"{shown.Count} of {all.Count} songs" : all.Count == 1 ? "1 song" : $"{all.Count} songs", 13, _t.Sub);
            count.VerticalAlignment = VerticalAlignment.Center;
            chips.Children.Add(new Border { Height = 36, Margin = new Thickness(2, 0, 0, 8), Child = count }); // the count follows the chips on their last line
            bar.Content = chips;
            host.Content = all.Count == 0 ? Hint(empty) : shown.Count == 0 ? Hint(text.Length > 0 ? "Nothing matches the filter." : favoritesOnly ? "No favourites in this list yet." : "Nothing here.") : SongRows(shown);
        }

        Refresh();
        _applyFilter = value =>
        {
            text = value;
            Refresh();
        };
        _activePage = () =>
        {
            if (favoritesOnly || isFavoritesLevel)
                Refresh();
        };
        return page;
    }

    /// <summary>The Favourites level: the hearted tracks, with play all and the usual sort, filter and chips.</summary>
    private Control FavoritesPage() => SongList([], "No favourites yet.\nTap the heart on Now Playing, or use a song's menu > Add to favourites.", "favorites");

    // ---- Group lists (artists, genres, albums) ----------------------------------------------------

    private sealed record GroupEntry(string Label, string NameKey, List<LibraryItem> Items)
    {
        public string Artist => Items.Select(i => i.Artist).FirstOrDefault(a => a.Length > 0) ?? string.Empty;

        public int Year => Items.Select(i => int.TryParse(i.Year.Length >= 4 ? i.Year[..4] : i.Year, out var y) ? y : 0).Where(y => y > 0).DefaultIfEmpty(0).Min();
    }

    /// <summary>A list of artists / genres / albums with a sort button (name, number of songs, and for albums artist and year); the search box filters it by name.</summary>
    private Control GroupList(string level, IReadOnlyList<SortField> fields, List<GroupEntry> entries, Func<List<GroupEntry>, Control> build, string empty)
    {
        if (entries.Count == 0)
            return Hint(empty);
        var sort = AppSettings.Current.SortOf(level, new SortSpec(SortField.Title));
        if (!fields.Contains(sort.Field))
            sort = new SortSpec(SortField.Title);
        var text = string.Empty;
        var bar = new ContentControl { Margin = new Thickness(14, 10, 14, 0) };
        var host = new ContentControl { Background = Brushes.Transparent };

        void Refresh()
        {
            var query = entries.AsEnumerable();
            if (text.Length > 0)
                query = query.Where(e => QueueFilter.Contains(e.Label, text));
            var list = query.ToList();
            var keys = list.Select(e => new TrackKey(e.NameKey, e.Artist, string.Empty, string.Empty, e.Year, e.Items.Count, DateTime.MinValue, 0, 0, 0)).ToArray();
            var spec = sort.Field switch
            {
                SortField.SongCount => new SortSpec(SortField.Duration, sort.Descending), // the key's Duration slot carries the song count
                SortField.Title or SortField.Artist or SortField.Year => sort,
                _ => new SortSpec(SortField.Title, sort.Descending),
            };
            var indexes = Enumerable.Range(0, list.Count).OrderBy(i => i, Comparer<int>.Create((x, y) => TrackSort.Compare(keys[x], keys[y], spec))).ToList();
            var sorted = indexes.Select(i => list[i]).ToList();
            var chips = new WrapPanel();
            chips.Children.Add(SortButton(sort, () => OpenSortSheet("Sort by", fields, sort, chosen =>
            {
                sort = chosen;
                AppSettings.Current.RememberSort(level, sort);
                Refresh();
            })));
            var count = Text(sorted.Count == entries.Count ? $"{entries.Count} {(level == "albums" ? "albums" : level == "genres" ? "genres" : "artists")}" : $"{sorted.Count} of {entries.Count}", 13, _t.Sub);
            count.VerticalAlignment = VerticalAlignment.Center;
            chips.Children.Add(new Border { Height = 36, Margin = new Thickness(2, 0, 0, 8), Child = count });
            bar.Content = chips;
            host.Content = sorted.Count == 0 ? Hint("Nothing matches the filter.") : build(sorted);
        }

        Refresh();
        _applyFilter = value =>
        {
            text = value;
            Refresh();
        };
        return new StackPanel { Children = { bar, host } };
    }

    /// <summary>The songs in the order of the current playlist queue; songs that are not queued follow, in the order given. Reversed flips the whole result.</summary>
    private List<LibraryItem> InQueueOrder(IEnumerable<LibraryItem> items, bool descending)
    {
        var position = new Dictionary<string, int>(StringComparer.Ordinal);
        var queue = _player.CurrentPlaylist.Items;
        for (var i = 0; i < queue.Count; i++)
            position.TryAdd(TrackSort.IdentityOf(queue[i].Source), i);
        var list = items.ToList();
        var ordered = Enumerable.Range(0, list.Count)
            .OrderBy(i => position.TryGetValue(TrackSort.IdentityOf(list[i].Path), out var p) ? p : int.MaxValue)
            .ThenBy(i => i)
            .Select(i => list[i])
            .ToList();
        if (descending)
            ordered.Reverse();
        return ordered;
    }

    // ---- The sort sheet ---------------------------------------------------------------------------

    private static (string Ascending, string Descending) DirectionLabels(SortField field) => field switch
    {
        SortField.Title or SortField.Artist or SortField.Album or SortField.Genre => ("A to Z", "Z to A"),
        SortField.DateAdded => ("Oldest first", "Newest first"),
        SortField.Default => ("As listed", "Reversed"),
        SortField.QueueOrder => ("As queued", "Reversed"),
        SortField.Rating or SortField.PlayCount or SortField.SongCount => ("Fewest first", "Most first"),
        _ => ("Smallest first", "Largest first"),
    };

    /// <summary>A bottom sheet: Ascending / Descending, then the fields (a tap applies at once and closes the sheet; the direction switch applies and stays).</summary>
    private void OpenSortSheet(string title, IReadOnlyList<SortField> fields, SortSpec current, Action<SortSpec> apply)
    {
        if (_sheetPanel is not null)
        {
            _sheetPanel.VerticalAlignment = VerticalAlignment.Bottom;
            _sheetPanel.CornerRadius = new CornerRadius(16, 16, 0, 0);
            _sheetPanel.Margin = default;
        }

        void Build(SortSpec spec)
        {
            _sheetTitle.Text = title;
            _sheetRows.Children.Clear();

            // Direction: two halves of one control.
            var (ascending, descending) = DirectionLabels(spec.Field);
            Border Half(string label, bool on, bool right, Action tap)
            {
                var ink = on ? (_t.Glass || _t.Royal ? Colors.White : _t.Accent) : _t.Ink;
                var half = new Border
                {
                    Height = 42, Child = new TextBlock { Text = label, FontSize = 15, FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal, Foreground = Brush(ink), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                    Background = on ? (_t.Glass || _t.Royal ? _t.SelectedBrush : Brush(Color.FromArgb(48, _t.Accent.R, _t.Accent.G, _t.Accent.B))) : Brushes.Transparent,
                    BorderBrush = _t.Glass ? Brush(_t.G(on ? "#BBDCFF" : "#6FA9F5")) : _t.Royal ? Brush(on ? Color.Parse("#1D3C9C") : _t.Line) : Brush(on ? _t.Accent : _t.Line),
                    BorderThickness = new Thickness(1.2),
                    CornerRadius = right ? new CornerRadius(0, 21, 21, 0) : new CornerRadius(21, 0, 0, 21),
                };
                if (_t.Royal)
                    half.CornerRadius = right ? new CornerRadius(0, 6, 6, 0) : new CornerRadius(6, 0, 0, 6);
                half.Tapped += (_, _) => tap();
                return half;
            }

            var direction = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(16, 4, 16, 8) };
            var asc = Half(ascending, !spec.Descending, false, () => { spec = spec with { Descending = false }; apply(spec); Build(spec); });
            var desc = Half(descending, spec.Descending, true, () => { spec = spec with { Descending = true }; apply(spec); Build(spec); });
            Grid.SetColumn(desc, 1);
            direction.Children.Add(asc);
            direction.Children.Add(desc);
            _sheetRows.Children.Add(direction);

            foreach (var field in fields)
            {
                var selected = field == spec.Field;
                var name = Text(SortSpec.Label(field), 16, _t.Ink, selected);
                name.Margin = new Thickness(20, 0);
                name.VerticalAlignment = VerticalAlignment.Center;
                // The mark: a radio button on the pictorial (XP era) looks, a Fluent check mark on the modern ones.
                Control mark;
                if (_t.PictorialIcons)
                {
                    var ring = new Avalonia.Controls.Shapes.Ellipse { Width = 20, Height = 20, Stroke = Brush(_t.Glass ? _t.G("#BBDCFF") : _t.Royal ? Color.Parse("#2E55C4") : _t.Sub), StrokeThickness = 1.8 };
                    var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 10, Height = 10, Fill = Brush(_t.Glass ? _t.G("#8FE3FF") : _t.Accent), IsVisible = selected };
                    mark = new Grid { Width = 20, Height = 20, Children = { ring, dot } };
                }
                else
                {
                    mark = selected ? FluentIcons.Create("checkmark", _t.Accent, 22) : new Border { Width = 22, Height = 22 };
                }

                mark.Margin = new Thickness(0, 0, 20, 0);
                mark.VerticalAlignment = VerticalAlignment.Center;
                var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
                Grid.SetColumn(mark, 1);
                line.Children.Add(name);
                line.Children.Add(mark);
                var row = new Border { MinHeight = 48, Background = Brushes.Transparent, Child = line };
                var chosen = field;
                row.Tapped += (_, _) =>
                {
                    _sheet.IsVisible = false;
                    apply(new SortSpec(chosen, spec.Descending));
                };
                if (_t.Glass)
                    _sheetRows.Children.Add(new Border { Height = 1, Background = Brush(_t.Line), Margin = new Thickness(16, 0) });
                _sheetRows.Children.Add(row);
            }

            _sheetRows.Children.Add(_t.Glass ? _t.GlassPill("Close", () => _sheet.IsVisible = false) : new Border { Height = 12 });
        }

        Build(current);
        _sheet.IsVisible = true;
    }
}
