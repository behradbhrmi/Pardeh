using Pardeh.Core.Library;
using Pardeh.Core.Playback;
using Pardeh.Skinning.Model;

namespace Pardeh.Skinning.Runtime.Elements;

/// <summary>A node of the library tree (All Music → Artist → "Some Artist" …).</summary>
public sealed class LibraryNode(string key, string title, int depth, Func<LibraryItem, bool>? filter, bool hasChildren, string icon = "")
{
    public string Key { get; } = key;
    public string Title { get; } = title;
    public int Depth { get; } = depth;
    public Func<LibraryItem, bool>? Filter { get; } = filter;
    public bool HasChildren { get; } = hasChildren;

    /// <summary>Which small icon the tree shows ("nowplaying", "music", "video", "playlist" …; "" for none).</summary>
    public string Icon { get; } = icon;
}

/// <summary>A toolbar button of the library pane.</summary>
public sealed record LibraryTool(string Key, string Label, bool ShowLabel = true, bool IsMenu = false);

/// <summary>
/// LIBRARY: the Media Library pane, laid out like the classic player's: a toolbar (Playlists, Queue-It-Up,
/// Search, Add, Delete, Move Up / Down, View Album Info), a category tree with icons, the track list and a footer.
/// </summary>
public sealed class LibraryElement(SkinView view, SkinNode node, SkinElement? parent) : SkinElement(view, node, parent)
{
    public static double RowHeight { get; set; } = 15;
    public const double ToolbarHeight = 26;
    public const double HeaderHeight = 18;
    public const double FooterHeight = 18;
    public const double SplitterWidth = 4;

    public static readonly string[] Columns = ["Title", "Artist", "Album", "Rating", "Genre", "Length"];
    public const int RatingColumn = 3;
    private static readonly double[] ColumnWeights = [3, 2.2, 2.2, 1.1, 1.2];
    private const double LengthColumnWidth = 52;

    public static readonly IReadOnlyList<LibraryTool> Tools =
    [
        new("playlists", "Playlists", IsMenu: true),
        new("queue", "Queue-It-Up"),
        new("search", "Search"),
        new("add", "Add", IsMenu: true),
        new("delete", "Delete", ShowLabel: false),
        new("up", "Move Up", ShowLabel: false),
        new("down", "Move Down", ShowLabel: false),
        new("albuminfo", "View Album Info"),
    ];

    private readonly HashSet<string> _expanded = new(StringComparer.Ordinal) { "playlists", "auto" };
    private List<LibraryNode> _nodes = [];
    private List<LibraryItem> _tracks = [];
    private IReadOnlyList<LibraryItem>? _source;
    private bool _dirty = true;

    public string SelectedNodeKey { get; private set; } = "music";

    public int SelectedTrack { get; private set; } = -1;

    public int TreeScroll { get; private set; }

    public int TrackScroll { get; private set; }

    /// <summary>The search text ("" = no search).</summary>
    public string SearchText { get; private set; } = string.Empty;

    /// <summary>Sort column (index into <see cref="Columns"/>, -1 = artist / album / track) and direction.</summary>
    public int SortColumn { get; private set; } = -1;

    public bool SortDescending { get; private set; }

    /// <summary>Toolbar button rectangles (x, width), laid out by the renderer, in <see cref="Tools"/> order.</summary>
    public List<(double X, double Width)> ButtonBounds { get; } = [];

    public int PressedButton { get; private set; } = -1;

    public IReadOnlyList<LibraryNode> Nodes
    {
        get
        {
            Refresh();
            return _nodes;
        }
    }

    public IReadOnlyList<LibraryItem> Tracks
    {
        get
        {
            Refresh();
            return _tracks;
        }
    }

    public bool IsExpanded(string key) => _expanded.Contains(key);

    /// <summary>Opt-in light palette (attribute <c>light="true"</c>): white tree and list, grey headers, dark text.</summary>
    public bool Light => GetBool("light");

    /// <summary>
    /// Opt-in phone layout (attribute <c>phone="true"</c>): one column, drill-down. The category tree OR the track list
    /// shows (tapping a category opens its list, the toolbar's back arrow returns), rows are touch-sized, the toolbar is
    /// compact (back / title, search, add) and a tap on a track plays it. Colours come from the opt-in attributes
    /// <c>libBack</c>, <c>libInk</c>, <c>libDim</c>, <c>libBar</c>, <c>libBarInk</c>, <c>libSel</c>, <c>libAccent</c>.
    /// </summary>
    public bool Phone => GetBool("phone");

    public const double PhoneRowHeight = 48, PhoneBarHeight = 48, PhoneFooterHeight = 24;

    /// <summary>Phone: whether the track list (true) or the category tree (false) is showing.</summary>
    public bool ShowList { get; private set; }

    /// <summary>The toolbar's height (0 when the skin sets <c>toolbar="false"</c>).</summary>
    public double TopBarHeight => GetBool("toolbar", true) ? (Phone ? PhoneBarHeight : ToolbarHeight) : 0;

    /// <summary>The footer's height (0 when the skin sets <c>footer="false"</c>).</summary>
    public double BottomBarHeight => GetBool("footer", true) ? (Phone ? PhoneFooterHeight : FooterHeight) : 0;

    /// <summary>Row height: the shared (static) one, or the touch height in the phone layout.</summary>
    public double RowH => Phone ? PhoneRowHeight : RowHeight;

    public double TreeWidth => Phone ? (ShowList ? 0 : Width) : Math.Clamp(Math.Round(Width * 0.24), 130, 200);

    public double ListLeft => Phone ? 0 : TreeWidth + SplitterWidth;

    private double ListTop => TopBarHeight + (Phone ? 0 : HeaderHeight);

    public int VisibleRows => Math.Max(1, (int)((Height - ListTop - BottomBarHeight) / RowH));

    /// <summary>Phone: back from the track list to the category tree. False when already at the tree.</summary>
    public bool GoBack()
    {
        if (!Phone || !ShowList)
            return false;
        ShowList = false;
        View.Invalidate();
        return true;
    }

    private int _phoneKind = -1, _phoneIndex = -1; // phone press: 0 back, 1 tree row, 2 track row, 3 expander, 4 tool

    /// <summary>Track list columns in element coordinates: Title, Artist, Album, Genre, Length.</summary>
    public IReadOnlyList<(double X, double Width)> ColumnBounds
    {
        get
        {
            var listW = Math.Max(1, Width - ListLeft);
            var flexible = Math.Max(1, listW - LengthColumnWidth);
            var total = ColumnWeights.Sum();
            var result = new List<(double, double)>();
            var x = ListLeft;
            foreach (var weight in ColumnWeights)
            {
                var w = Math.Floor(flexible * weight / total);
                result.Add((x, w));
                x += w;
            }

            result.Add((x, Width - x));
            return result;
        }
    }

    /// <summary>Whether a toolbar button does anything right now (greyed out otherwise).</summary>
    public bool IsToolEnabled(string key) => key switch
    {
        "queue" => Tracks.Count > 0 && SelectedNodeKey != "nowplaying",
        "delete" => SelectedTrack >= 0,
        "up" => SelectedTrack > 0 && IsOrdered,
        "down" => SelectedTrack >= 0 && SelectedTrack < Tracks.Count - 1 && IsOrdered,
        "albuminfo" => false, // the online album information service closed
        _ => true,
    };

    /// <summary>Lists whose order belongs to the user (Now Playing, saved playlists) can be rearranged.</summary>
    private bool IsOrdered => SelectedNodeKey == "nowplaying" || SelectedNodeKey.StartsWith("playlist:", StringComparison.Ordinal);

    public override bool IsInteractive => true;

    public override bool HitTest(double x, double y) => Visible && base.HitTest(x, y);

    protected override IEnumerable<string> KnownProperties =>
        ["selectedNode", "itemCount", "totalTime", "light", "toolbar", "footer", "phone", "libBack", "libInk", "libDim", "libBar", "libBarInk", "libSel", "libAccent"];

    protected override IEnumerable<string> KnownMethods => ["search", "searchPrompt", "back"];

    public override object? GetMember(string name) => name.ToLowerInvariant() switch
    {
        "selectednode" => SelectedNodeKey,
        "itemcount" => (double)Tracks.Count,
        "totaltime" => TotalTimeText,
        _ => base.GetMember(name),
    };

    public override object? InvokeMember(string name, object?[] args)
    {
        switch (name.ToLowerInvariant())
        {
            case "search":
                Search(SkinValue.ToText(args.Length > 0 ? args[0] : string.Empty));
                return null;
            case "searchprompt":
                Press("search");
                return null;
            case "back":
                return GoBack(); // phone: true when it stepped from the list back to the tree
            default:
                return base.InvokeMember(name, args);
        }
    }

    private PlaylistStore? Playlists => View.Session.Playlists;

    /// <summary>Marks the view stale (call when the library changes).</summary>
    public void Invalidate()
    {
        _dirty = true;
        View.Invalidate();
    }

    private void Refresh()
    {
        var library = View.Session.Library;
        var items = library?.Items ?? [];
        // Now Playing changes outside the library, so it is always re-read.
        if (!_dirty && ReferenceEquals(items, _source) && SelectedNodeKey != "nowplaying")
            return;
        _source = items;
        _dirty = false;

        var music = items.Where(i => !i.IsVideo).ToList();
        var nodes = new List<LibraryNode>();
        if (SearchText.Length > 0)
            nodes.Add(new LibraryNode("search", $"Search Results: \"{SearchText}\"", 0, Matches, false, "search"));
        nodes.Add(new LibraryNode("nowplaying", "Now Playing", 0, null, false, "nowplaying"));
        nodes.Add(new LibraryNode("music", "All Music", 0, i => !i.IsVideo, true, "music"));
        if (_expanded.Contains("music"))
        {
            AddGroup(nodes, "artist", "Artist", music, i => i.Artist);
            AddGroup(nodes, "album", "Album", music, i => i.Album);
            AddGroup(nodes, "genre", "Genre", music, i => i.Genre);
            AddGroup(nodes, "year", "Year", music, i => i.Year);
            nodes.Add(new LibraryNode("rated", "Rated Songs", 1, i => !i.IsVideo && i.Rating > 0, false));
        }

        nodes.Add(new LibraryNode("video", "All Video", 0, i => i.IsVideo, false, "video"));
        nodes.Add(new LibraryNode("other", "Other Media", 0, _ => false, false, "other"));

        var playlists = Playlists?.Names ?? [];
        nodes.Add(new LibraryNode("playlists", "My Playlists", 0, null, playlists.Count > 0, "playlist"));
        if (_expanded.Contains("playlists"))
        {
            foreach (var name in playlists)
                nodes.Add(new LibraryNode("playlist:" + name, name, 1, null, false, "playlist"));
        }

        nodes.Add(new LibraryNode("auto", "Auto Playlists", 0, null, true, "auto"));
        if (_expanded.Contains("auto"))
        {
            var month = DateTime.UtcNow.AddDays(-30);
            nodes.Add(new LibraryNode("auto:favorites", "Favorites - 4 and 5 star rated", 1, i => !i.IsVideo && i.Rating >= 4, false, "auto"));
            nodes.Add(new LibraryNode("auto:fresh", "Fresh tracks", 1, i => !i.IsVideo && i.Modified >= month, false, "auto"));
            nodes.Add(new LibraryNode("auto:most", "Most played", 1, i => !i.IsVideo && i.PlayCount >= 3, false, "auto"));
            nodes.Add(new LibraryNode("auto:recent", "Recently played", 1, i => !i.IsVideo && i.LastPlayed >= DateTime.UtcNow.AddDays(-7), false, "auto"));
            nodes.Add(new LibraryNode("auto:unrated", "Music I have not rated", 1, i => !i.IsVideo && i.Rating == 0, false, "auto"));
            nodes.Add(new LibraryNode("auto:long", "Long tracks (over 10 minutes)", 1, i => !i.IsVideo && i.DurationSeconds > 600, false, "auto"));
        }

        nodes.Add(new LibraryNode("radio", "Radio", 0, null, false, "radio"));
        nodes.Add(new LibraryNode("premium", "Premium Services", 0, null, false, "premium"));
        _nodes = nodes;

        var selected = nodes.FirstOrDefault(n => n.Key == SelectedNodeKey) ?? nodes.First(n => n.Key == "music");
        SelectedNodeKey = selected.Key;
        IEnumerable<LibraryItem> tracks = selected.Key switch
        {
            "nowplaying" => View.Session.Player.CurrentPlaylist.Items.Select(ToLibraryItem),
            _ when selected.Key.StartsWith("playlist:", StringComparison.Ordinal) => PlaylistItems(selected.Key["playlist:".Length..], items),
            _ when selected.Filter is { } filter => Sort(items.Where(filter)),
            _ => [],
        };
        _tracks = tracks.ToList();
        SelectedTrack = Math.Min(SelectedTrack, _tracks.Count - 1);
    }

    /// <summary>What the list says when it is empty.</summary>
    public string EmptyText => SelectedNodeKey switch
    {
        "radio" => "Find stations in Radio Tuner.",
        "premium" => "Premium Services are no longer offered.",
        "other" => "No other media.",
        "nowplaying" => "Nothing is playing. Double-click a track to play it.",
        "playlists" => "Save Now Playing as a playlist with Playlists ▾.",
        _ when View.Session.Library is { IsScanning: true } => "Searching your Music and Videos folders…",
        _ when SearchText.Length > 0 && SelectedNodeKey == "search" => "No items match the search.",
        _ => "No media here yet. Add music to your Music folder, or use Add ▾ › Add Folder.",
    };

    private bool Matches(LibraryItem item) =>
        item.Title.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)
        || item.Artist.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)
        || item.Album.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase)
        || item.Genre.Contains(SearchText, StringComparison.CurrentCultureIgnoreCase);

    private IEnumerable<LibraryItem> Sort(IEnumerable<LibraryItem> items)
    {
        var comparer = StringComparer.CurrentCultureIgnoreCase;
        IOrderedEnumerable<LibraryItem> sorted = SortColumn switch
        {
            0 => SortDescending ? items.OrderByDescending(i => i.Title, comparer) : items.OrderBy(i => i.Title, comparer),
            1 => SortDescending ? items.OrderByDescending(i => i.Artist, comparer) : items.OrderBy(i => i.Artist, comparer),
            2 => SortDescending ? items.OrderByDescending(i => i.Album, comparer) : items.OrderBy(i => i.Album, comparer),
            3 => SortDescending ? items.OrderByDescending(i => i.Rating) : items.OrderBy(i => i.Rating),
            4 => SortDescending ? items.OrderByDescending(i => i.Genre, comparer) : items.OrderBy(i => i.Genre, comparer),
            5 => SortDescending ? items.OrderByDescending(i => i.DurationSeconds) : items.OrderBy(i => i.DurationSeconds),
            _ => items.OrderBy(i => i.Artist, comparer).ThenBy(i => i.Album, comparer).ThenBy(i => i.TrackNumber),
        };
        return sorted.ThenBy(i => i.Title, comparer);
    }

    private IEnumerable<LibraryItem> PlaylistItems(string name, IReadOnlyList<LibraryItem> library)
    {
        var known = library.GroupBy(i => i.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var source in Playlists?.Read(name) ?? [])
        {
            yield return known.TryGetValue(source, out var item)
                ? item
                : new LibraryItem(source, Path.GetFileNameWithoutExtension(source), string.Empty, string.Empty, string.Empty, string.Empty, 0, 0,
                    MediaLibrary.VideoExtensions.Contains(Path.GetExtension(source)), 0, DateTime.MinValue);
        }
    }

    private void AddGroup(List<LibraryNode> nodes, string key, string title, List<LibraryItem> music, Func<LibraryItem, string> field)
    {
        nodes.Add(new LibraryNode(key, title, 1, i => !i.IsVideo, true));
        if (!_expanded.Contains(key))
            return;
        foreach (var value in music.Select(field).Distinct(StringComparer.CurrentCultureIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase))
        {
            var label = value.Length == 0 ? "Unknown" : value;
            var captured = value;
            nodes.Add(new LibraryNode($"{key}:{value}", label, 2,
                i => !i.IsVideo && string.Equals(field(i), captured, StringComparison.CurrentCultureIgnoreCase), false));
        }
    }

    private static LibraryItem ToLibraryItem(MediaItem item) => new(
        item.Source, item.Name, item.GetAttribute("Author"), item.GetAttribute("Album"), item.GetAttribute("Genre"),
        item.GetAttribute("Year"), 0, item.Duration.TotalSeconds, item.VideoWidth > 0, 0, DateTime.MinValue);

    /// <summary>Total duration of the listed tracks, e.g. "1:02:03".</summary>
    public string TotalTimeText => MediaPlayer.FormatTime(Tracks.Sum(t => t.DurationSeconds));

    /// <summary>"Total Time: 6:23 / 4.64MB", as the footer shows.</summary>
    public string FooterTotals
    {
        get
        {
            var bytes = Tracks.Sum(t => t.FileSize);
            return bytes > 0 ? $"Total Time: {TotalTimeText} / {bytes / 1048576.0:0.00}MB" : $"Total Time: {TotalTimeText}";
        }
    }

    // ---- Input --------------------------------------------------------------------------------

    private double _phonePressY;

    private void PhonePress(double x, double y)
    {
        _phoneKind = -1;
        _phoneIndex = -1;
        _phonePressY = y;
        PressedButton = -1;
        if (y < TopBarHeight)
        {
            if (ShowList && x < 56)
            {
                _phoneKind = 0;
            }
            else if (ButtonBounds.FindIndex(b => b.Width > 0 && x >= b.X && x < b.X + b.Width) is var tool and >= 0)
            {
                _phoneKind = 4;
                _phoneIndex = tool;
                PressedButton = tool;
            }
        }
        else if (y >= ListTop && y < Height - BottomBarHeight)
        {
            var row = (int)((y - ListTop) / RowH);
            if (!ShowList)
            {
                var index = TreeScroll + row;
                if (index < Nodes.Count)
                {
                    _phoneIndex = index;
                    _phoneKind = Nodes[index].HasChildren && x >= Width - 56 ? 3 : 1;
                }
            }
            else if (TrackScroll + row is var track && track < Tracks.Count)
            {
                _phoneIndex = track;
                _phoneKind = 2;
                SelectedTrack = track;
            }
        }

        View.Invalidate();
    }

    private void PhoneActivate(int kind, int index)
    {
        switch (kind)
        {
            case 0:
                GoBack();
                break;
            case 1 when index < Nodes.Count:
                if (Nodes[index].Key == "radio")
                    View.Session.App.SetMember("currentTask", "RadioTuner");
                else
                    Select(Nodes[index].Key);
                break;
            case 3 when index < Nodes.Count:
                Toggle(Nodes[index].Key);
                break;
            case 2:
                PlayFrom(index);
                break;
            case 4:
                Press(Tools[index].Key);
                break;
        }
    }

    public override void OnPointerPressed(double x, double y)
    {
        base.OnPointerPressed(x, y);
        if (Phone)
        {
            PhonePress(x, y);
            return;
        }

        if (y < TopBarHeight)
        {
            PressedButton = ButtonBounds.FindIndex(b => x >= b.X && x < b.X + b.Width);
            if (PressedButton >= 0 && !IsToolEnabled(Tools[PressedButton].Key))
                PressedButton = -1;
            View.Invalidate();
            return;
        }

        if (y < ListTop)
        {
            // Column headers sort the track list; clicking again reverses.
            if (x >= ListLeft && ColumnAt(x) is var column && column >= 0)
            {
                SortDescending = SortColumn == column && !SortDescending;
                SortColumn = column;
                _dirty = true;
            }

            View.Invalidate();
            return;
        }

        if (y >= Height - BottomBarHeight)
            return;
        var row = (int)((y - ListTop) / RowHeight);
        if (x < TreeWidth)
        {
            var index = TreeScroll + row;
            if (index >= _nodes.Count)
                return;
            var node = _nodes[index];
            var expander = 4 + node.Depth * 14;
            if (node.HasChildren && x >= expander && x < expander + 11)
                Toggle(node.Key);
            else
                Select(node.Key);
        }
        else if (x >= ListLeft)
        {
            var index = TrackScroll + row;
            SelectedTrack = index < _tracks.Count ? index : -1;

            if (BadgeAt(x, y) >= 0)
                FixInfo();

            // Clicking the stars rates the track; clicking the current rating clears it.
            if (SelectedTrack >= 0 && ColumnAt(x) == RatingColumn)
            {
                var stars = Math.Clamp((int)((x - ColumnBounds[RatingColumn].X - 4) / StarWidth) + 1, 1, 5);
                var track = _tracks[SelectedTrack];
                View.Session.Library?.SetRating(track.Path, track.Rating == stars ? 0 : stars);
                _dirty = true;
            }
        }

        View.Invalidate();
    }

    public override void OnPointerReleased(double x, double y, bool inside)
    {
        if (Phone)
        {
            var (kind, index) = (_phoneKind, _phoneIndex);
            _phoneKind = -1;
            PressedButton = -1;
            base.OnPointerReleased(x, y, inside);
            // A tap: finger lifted on the same row / button it went down on (a drag scrolls instead).
            if (inside && kind >= 0 && Math.Abs(y - _phonePressY) < 14)
                PhoneActivate(kind, index);
            View.Invalidate();
            return;
        }

        var pressed = PressedButton;
        PressedButton = -1;
        base.OnPointerReleased(x, y, inside);
        if (pressed >= 0 && y < TopBarHeight && pressed == ButtonBounds.FindIndex(b => x >= b.X && x < b.X + b.Width))
            Press(Tools[pressed].Key);
        View.Invalidate();
    }

    public override void OnDoubleClick(double x, double y)
    {
        base.OnDoubleClick(x, y);
        if (Phone || y < ListTop || y >= Height - BottomBarHeight)
            return;
        var row = (int)((y - ListTop) / RowHeight);
        if (x < TreeWidth)
        {
            var index = TreeScroll + row;
            if (index >= _nodes.Count)
                return;
            var node = _nodes[index];
            if (node.HasChildren)
                Toggle(node.Key);
            else if (node.Key == "radio")
                View.Session.App.SetMember("currentTask", "RadioTuner");
            else
                PlayFrom(0);
            return;
        }

        var track = TrackScroll + row;
        if (x >= ListLeft && track < _tracks.Count)
            PlayFrom(track);
    }

    /// <summary>Right-click on a track: Play, Add to Now Playing, Delete, Open File Location.</summary>
    public override bool OnContextMenu(double x, double y)
    {
        if (x < ListLeft || y < ListTop || y >= Height - BottomBarHeight || (Phone && !ShowList))
            return false;
        var index = TrackScroll + (int)((y - ListTop) / RowH);
        if (index >= _tracks.Count)
            return false;
        SelectedTrack = index;
        View.Invalidate();
        List<string> items = ["Play", "Add to Now Playing", "Rate", "Edit Info...", RemoveLabel, "Open File Location"];
        var fixable = View.Session.CanFixInfo?.Invoke(_tracks[index].Path) == true;
        if (fixable)
            items.Add("Fix Info...");
        View.Session.Host.ShowPopup(View, items, -1, choice =>
        {
            switch (choice)
            {
                case 0: PlayFrom(SelectedTrack); break;
                case 1: Press("queue"); break;
                case 2: ShowRateMenu(); break;
                case 3: EditInfo(); break;
                case 4: Press("delete"); break;
                case 5: Press("reveal"); break;
                case 6 when fixable: FixInfo(); break;
            }
        });
        return true;
    }

    /// <summary>Width of one star in the Rating column (the renderer draws them this wide).</summary>
    public const double StarWidth = 11;

    private void ShowRateMenu()
    {
        if (SelectedTrack < 0 || SelectedTrack >= _tracks.Count)
            return;
        var path = _tracks[SelectedTrack].Path;
        string[] ratings = ["★★★★★  5 stars", "★★★★  4 stars", "★★★  3 stars", "★★  2 stars", "★  1 star", "Unrated"];
        View.Session.Host.ShowPopup(View, ratings, -1, choice =>
        {
            if (choice >= 0)
                View.Session.Library?.SetRating(path, choice == 5 ? 0 : 5 - choice);
            _dirty = true;
            View.Invalidate();
        });
    }

    /// <summary>Edit Info: the host's tag editor for the selected track; the library rescans it afterwards.</summary>
    private void EditInfo()
    {
        if (SelectedTrack < 0 || SelectedTrack >= _tracks.Count)
            return;
        View.Session.Host.RunCommand("editTags", [_tracks[SelectedTrack].Path]);
    }

    private void FixInfo()
    {
        if (SelectedTrack >= 0 && SelectedTrack < _tracks.Count)
            View.Session.Host.RunCommand("fixInfo", [_tracks[SelectedTrack].Path]);
    }

    /// <summary>Width of the "Needs cleanup" mark at the start of a title (the renderer draws it this wide).</summary>
    public const double BadgeWidth = 14;

    /// <summary>The row whose "Needs cleanup" mark is at a point of the desktop list, or -1.</summary>
    private int BadgeAt(double x, double y)
    {
        if (Phone || x < ListLeft || y < ListTop || y >= Height - BottomBarHeight || ColumnBounds.Count == 0
            || x < ColumnBounds[0].X || x >= ColumnBounds[0].X + 4 + BadgeWidth)
            return -1;
        var index = TrackScroll + (int)((y - ListTop) / RowHeight);
        return index < _tracks.Count && View.Session.NeedsCleanup?.Invoke(_tracks[index].Path) == true ? index : -1;
    }

    public override string? ToolTipAt(double x, double y) =>
        BadgeAt(x, y) >= 0 ? "Needs cleanup. Click to look at it." : base.ToolTipAt(x, y);

    private string RemoveLabel => SelectedNodeKey == "nowplaying" ? "Remove from Now Playing"
        : SelectedNodeKey.StartsWith("playlist:", StringComparison.Ordinal) ? "Remove from Playlist"
        : "Delete from Library";

    public override double TouchScrollStep => Phone ? PhoneRowHeight : base.TouchScrollStep;

    public override void OnTouchScroll(double step)
    {
        if (Phone)
            ScrollRows(-(int)Math.Sign(step));
        else
            base.OnTouchScroll(step);
    }

    /// <summary>Phone: scroll the visible column (tree or list) by whole rows.</summary>
    private void ScrollRows(int rows)
    {
        if (ShowList)
            TrackScroll = Math.Clamp(TrackScroll + rows, 0, Math.Max(0, Tracks.Count - VisibleRows));
        else
            TreeScroll = Math.Clamp(TreeScroll + rows, 0, Math.Max(0, Nodes.Count - VisibleRows));
        View.Invalidate();
    }

    public override void OnWheel(double delta)
    {
        if (Phone)
        {
            ScrollRows(-Math.Sign(delta) * 3);
            return;
        }

        // The canvas doesn't pass the pointer position for wheel events, so scroll the track list.
        TrackScroll = Math.Clamp(TrackScroll - Math.Sign(delta) * 3, 0, Math.Max(0, _tracks.Count - VisibleRows));
        View.Invalidate();
    }

    private int ColumnAt(double x)
    {
        var columns = ColumnBounds;
        for (var i = 0; i < columns.Count; i++)
        {
            if (x >= columns[i].X && x < columns[i].X + columns[i].Width)
                return i;
        }

        return -1;
    }

    // ---- Commands -----------------------------------------------------------------------------

    private void Press(string command)
    {
        var session = View.Session;
        var player = session.Player;
        var selected = SelectedTrack >= 0 && SelectedTrack < _tracks.Count ? _tracks[SelectedTrack] : null;
        switch (command)
        {
            case "playlists":
                ShowPlaylistsMenu();
                break;
            case "queue":
                // Queue-It-Up: add to Now Playing (the selected track, or the whole list).
                foreach (var item in selected is null ? _tracks : [selected])
                    player.CurrentPlaylist.Add(item.ToMediaItem());
                if (player.CurrentMedia is null && player.CurrentPlaylist.Count > 0)
                    player.PlayItem(0);
                break;
            case "search":
                session.Host.RunCommand("prompt", ["Search the library", SearchText, (Action<string>)Search]);
                break;
            case "add":
                string[] adds = ["Add Folder...", "Add File...", "Search Folders Again"];
                session.Host.ShowPopup(View, adds, -1, choice =>
                {
                    switch (choice)
                    {
                        case 0: session.Host.RunCommand("libraryAddFolder", []); break;
                        case 1: session.Host.RunCommand("libraryAddFiles", []); break;
                        case 2: _ = session.Library?.ScanAsync(); break;
                    }
                });
                break;
            case "delete":
                Remove(selected);
                break;
            case "up" or "down":
                Move(command == "up" ? -1 : 1);
                break;
            case "reveal" when selected is not null:
                session.Host.RunCommand("reveal", [selected.Path]);
                break;
        }

        _dirty = true;
        View.Invalidate();
    }

    private void ShowPlaylistsMenu()
    {
        var session = View.Session;
        var names = Playlists?.Names ?? [];
        var items = new List<string> { "New Playlist from Now Playing...", "Import Playlist..." };
        items.AddRange(names);
        var deletable = SelectedNodeKey.StartsWith("playlist:", StringComparison.Ordinal);
        if (deletable)
            items.Add("Delete \"" + SelectedNodeKey["playlist:".Length..] + "\"");
        session.Host.ShowPopup(View, items, -1, choice =>
        {
            if (choice == 0)
            {
                if (session.Player.CurrentPlaylist.Count == 0)
                    session.Host.ShowMessage("Now Playing is empty: play or add some items first.");
                else
                    session.Host.RunCommand("prompt", ["Save Now Playing as a playlist", session.Player.CurrentPlaylist.Name, (Action<string>)SavePlaylist]);
            }
            else if (choice == 1)
            {
                session.Host.RunCommand("libraryImportPlaylist", []);
            }
            else if (choice > 1 && choice <= names.Count + 1)
            {
                _expanded.Add("playlists");
                Select("playlist:" + names[choice - 2]);
            }
            else if (deletable && choice == items.Count - 1)
            {
                Playlists?.Delete(SelectedNodeKey["playlist:".Length..]);
                Select("playlists");
            }
        });
    }

    private void Remove(LibraryItem? item)
    {
        var session = View.Session;
        if (item is null)
            return;
        if (SelectedNodeKey == "nowplaying")
        {
            var list = session.Player.CurrentPlaylist;
            var index = list.Items.ToList().FindIndex(i => string.Equals(i.Source, item.Path, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
                list.RemoveAt(index);
        }
        else if (SelectedNodeKey.StartsWith("playlist:", StringComparison.Ordinal) && Playlists is { } store)
        {
            var name = SelectedNodeKey["playlist:".Length..];
            var sources = store.Read(name).ToList();
            var at = sources.FindIndex(s => string.Equals(s, item.Path, StringComparison.OrdinalIgnoreCase));
            if (at >= 0)
                sources.RemoveAt(at);
            store.Save(name, sources);
        }
        else
        {
            session.Library?.Remove([item.Path]);
        }
    }

    /// <summary>Move Up / Move Down within Now Playing or a saved playlist.</summary>
    private void Move(int direction)
    {
        var from = SelectedTrack;
        var to = from + direction;
        if (from < 0 || to < 0 || to >= _tracks.Count)
            return;
        if (SelectedNodeKey == "nowplaying")
        {
            View.Session.Player.CurrentPlaylist.Move(from, to);
        }
        else if (SelectedNodeKey.StartsWith("playlist:", StringComparison.Ordinal) && Playlists is { } store)
        {
            var name = SelectedNodeKey["playlist:".Length..];
            var sources = store.Read(name).ToList();
            if (to >= sources.Count)
                return;
            (sources[from], sources[to]) = (sources[to], sources[from]);
            store.Save(name, sources);
        }
        else
        {
            return;
        }

        SelectedTrack = to;
        _dirty = true;
    }

    public void Search(string text)
    {
        SearchText = text.Trim();
        Select(SearchText.Length > 0 ? "search" : "music");
    }

    private void SavePlaylist(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Playlists is not { } store)
            return;
        store.Save(name.Trim(), View.Session.Player.CurrentPlaylist.Items.Select(i => i.Source));
        _expanded.Add("playlists");
        Select("playlist:" + name.Trim());
    }

    public void Select(string key)
    {
        ShowList = true; // phone layout: open the category's list
        SelectedNodeKey = key;
        SelectedTrack = -1;
        TrackScroll = 0;
        _dirty = true;
        View.Invalidate();
    }

    public void Toggle(string key)
    {
        if (!_expanded.Remove(key))
            _expanded.Add(key);
        _dirty = true;
        View.Invalidate();
    }

    /// <summary>Plays the listed tracks, starting with <paramref name="index"/>.</summary>
    public void PlayFrom(int index)
    {
        var tracks = Tracks;
        if (index < 0 || index >= tracks.Count)
            return;
        if (SelectedNodeKey == "nowplaying")
            View.Session.Player.PlayItem(index);
        else
            View.Session.Player.OpenItems(tracks.Select(t => t.ToMediaItem()), index);
    }
}
