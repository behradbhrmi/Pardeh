using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Pardeh.UI.Rendering;

/// <summary>
/// The Library home of the two art looks: Winamp Phone lists the music categories with the first on an amber bar; Groove Phone has pill
/// tabs (Songs, Albums, Artists, and More with everything else). Also Groove's picture tiles.
/// </summary>
internal sealed partial class MobileLibraryView
{
    private int _grooveTab; // 0 Songs, 1 Albums, 2 Artists, 3 More
    private bool _grooveMore; // building the More tab: Home() falls through to the standard rows

    private Control WinampHome()
    {
        var all = _library.Items;
        var songs = Music();
        var names = _host.Playlists()?.Names ?? [];
        var panel = new StackPanel();
        panel.Children.Add(Card(
            CategoryRow(NoteIcon, "All Music", songs.Count, () => Push("All Music", () => SongList(songs, "No songs yet.")), lit: true),
            CategoryRow("M 10,7 m -3,0 a 3,3 0 1,0 6,0 a 3,3 0 1,0 -6,0 M 4,17 Q 4,12 10,12 Q 16,12 16,17", "Artists", songs.Select(i => i.Artist).Where(a => a.Length > 0).Distinct().Count(), () => Push("Artists", () => GroupPage(songs, i => i.Artist, "Unknown artist", "@person")), "artists"),
            CategoryRow("M 10,10 m -7,0 a 7,7 0 1,0 14,0 a 7,7 0 1,0 -14,0 M 10,10 m -2,0 a 2,2 0 1,0 4,0 a 2,2 0 1,0 -4,0", "Albums", songs.Select(i => i.Album).Where(a => a.Length > 0).Distinct().Count(), () => Push("Albums", AlbumsPage), "albums"),
            CategoryRow("M 4,6 L 16,6 M 4,10 L 16,10 M 4,14 L 11,14", "Genres", songs.Select(i => i.Genre).Where(a => a.Length > 0).Distinct().Count(), () => Push("Genres", () => GroupPage(songs, i => i.Genre, "Unknown genre", "@tag")), "genres"),
            CategoryRow("M 3,6 L 13,6 M 3,10 L 13,10 M 3,14 L 9,14 M 12,12 L 17,14.5 L 12,17 Z", "Playlists", names.Count, () => Push("Playlists", PlaylistsPage), "playlists"),
            CategoryRow(VideoGlyph, "Videos", all.Count(i => i.IsVideo), () => Push("Videos", () => SongList(all.Where(i => i.IsVideo).ToList(), "No videos yet.", "videos")))));
        panel.Children.Add(OtherRows());
        if (_library.IsScanning)
            panel.Children.Add(Hint("Searching your folders..."));
        return panel;
    }

    private Control GrooveHome()
    {
        if (_library.Items.Count == 0 && !_library.IsScanning)
            return EmptyState();

        var tabs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), Margin = new Thickness(9, 4, 9, 2) };
        (string Name, string? Icon)[] items = [("Songs", NoteIcon), ("Albums", "M 10,10 m -7,0 a 7,7 0 1,0 14,0 a 7,7 0 1,0 -14,0 M 10,10 m -2,0 a 2,2 0 1,0 4,0 a 2,2 0 1,0 -4,0"), ("Artists", "M 10,7 m -3,0 a 3,3 0 1,0 6,0 a 3,3 0 1,0 -6,0 M 4,17 Q 4,12 10,12 Q 16,12 16,17"), ("More", null)];
        for (var i = 0; i < items.Length; i++)
        {
            var index = i;
            var selected = i == _grooveTab;
            var ink = selected ? Color.Parse("#0B2E78") : Color.Parse("#E4EFFF");
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (items[i].Icon is { } glyph)
                content.Children.Add(Icon(glyph, ink, 16, 1.8, selected: selected));
            content.Children.Add(Text(items[i].Name, 13.5, ink, selected));
            var pill = new Border
            {
                Height = 38, CornerRadius = new CornerRadius(12), Margin = new Thickness(3, 0),
                Background = Brush(selected ? Color.Parse("#B4E0FF") : Color.FromArgb(30, 160, 205, 255)), Child = content,
            };
            pill.Tapped += (_, _) =>
            {
                _grooveTab = index;
                Render();
            };
            Grid.SetColumn(pill, i);
            tabs.Children.Add(pill);
        }

        var songs = Music();
        Control body;
        switch (_grooveTab)
        {
            case 0:
                body = SongList(songs, "No songs yet.");
                break;
            case 1:
                body = AlbumsPage();
                break;
            case 2:
                body = GroupPage(songs, item => item.Artist, "Unknown artist", "@person");
                break;
            default:
                _grooveMore = true;
                try
                {
                    body = Home();
                }
                finally
                {
                    _grooveMore = false;
                }

                break;
        }

        return new StackPanel { Children = { tabs, body } };
    }
}

/// <summary>A square (or tile-shaped) generated "photo" for a track or album without a picture (the Groove look).</summary>
internal sealed class GeneratedCover(string seed) : Control
{
    public override void Render(DrawingContext context) => CoverThumbs.Draw(context, new Rect(Bounds.Size), 0, null, seed);
}
