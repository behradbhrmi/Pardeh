using Avalonia;
using Avalonia.Controls;
using Pardeh.Core.Duplicates;
using Pardeh.UI.CleanupViews;

namespace Pardeh.UI.DuplicateViews;

/// <summary>What the "Copies and similar" screen of one song works with.</summary>
internal sealed record RelatedContext(DuplicateScanner Scanner, string Source)
{
    public Func<DuplicateReport?> Report { get; init; } = () => null;

    public SongActions Actions { get; init; } = new();

    /// <summary>Opens the song's group of copies.</summary>
    public Func<DuplicateGroup, Task> Manage { get; init; } = _ => Task.CompletedTask;
}

/// <summary>
/// The copies of a song, other versions of it, and songs with close names, each saying why it is there. It reads only what
/// the duplicate check has read before, so it opens at once; when the check is unfinished it says so.
/// </summary>
internal sealed class RelatedView : UserControl
{
    private readonly CleanupInk _ink;
    private readonly RelatedContext _context;
    private readonly Action _close;
    private readonly StackPanel _content = new() { Spacing = 8 };
    private readonly Button _check;

    public RelatedView(CleanupInk ink, RelatedContext context, Action close)
    {
        _ink = ink;
        _context = context;
        _close = close;
        _check = ink.Button("Check the library", () => _ = context.Scanner.Start());
        var closeButton = ink.Cancels(ink.Button("Close", close));
        var buttons = DuplicateText.Buttons(_check, closeButton);
        var page = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 8, Children = { ink.Scroller(_content), buttons } };
        Grid.SetRow(buttons, 1);
        Content = page;
        Fill();
    }

    private string? _filled;

    public void Fill()
    {
        var progress = _context.Scanner.Progress;
        var report = _context.Report();
        var song = report?.CopyOf(_context.Source);
        var related = song is null || report is null ? [] : DuplicateFinder.Related(song, report.Copies, report.Groups, _context.Scanner.Store);
        var state = progress.IsActive ? $"Still checking: {progress.Stage.ToLowerInvariant()}, {progress.Done} of {progress.Total}. This list may grow."
            : report is null ? "Looking at what is known..."
            : report.NotRead > 0 ? $"The check is unfinished: {DuplicateText.Count(report.NotRead, "song has", "songs have")} not been read, so this list may be missing some."
            : string.Empty;
        _check.IsVisible = !progress.IsActive && report is { NotRead: > 0 };

        // Built again only when something changed, so that the page keeps its place.
        var signature = state + "|" + song?.Source + "|" + string.Join(';', related.Select(r => r.Copy.Source + r.List + r.Reason));
        if (signature == _filled)
            return;
        _filled = signature;
        _content.Children.Clear();
        if (state.Length > 0)
            _content.Children.Add(_ink.Label(state, 12, colour: _ink.Sub));
        if (song is null)
        {
            if (report is not null)
                _content.Children.Add(_ink.Label("This song has not been read by the check yet. Check the library to find its copies."));
            return;
        }

        _content.Children.Add(_ink.Label(DuplicateText.Song(song), 15, bold: true));
        _content.Children.Add(_ink.Label(song.Facts.FileName + DuplicateText.Dot + DuplicateText.Facts(song), 12, colour: _ink.Sub));
        if (related.Count == 0)
        {
            _content.Children.Add(_ink.Label("No copies, other versions or similar songs were found."));
            return;
        }

        Section(RelatedList.Copies, "Copies", related);
        if (report!.Groups.FirstOrDefault(g => g.Copies.Any(c => c.Source == song.Source)) is { } group)
        {
            _content.Children.Add(_ink.Button("Manage these duplicates...", () =>
            {
                _close();
                _ = _context.Manage(group);
            }));
        }

        Section(RelatedList.OtherVersions, "Other versions", related);
        Section(RelatedList.Similar, "Similar", related);
    }

    private void Section(RelatedList list, string heading, IReadOnlyList<RelatedSong> related)
    {
        var rows = related.Where(r => r.List == list).ToList();
        if (rows.Count == 0)
            return;
        _content.Children.Add(_ink.Label($"{heading} ({rows.Count})", 14, bold: true));
        foreach (var row in rows.Take(100))
        {
            var details = DuplicateText.Details(_ink, row.Copy, tags: false);
            details.Children.Insert(1, _ink.Label(row.Reason, 12));
            var buttons = _context.Actions.For(_ink, row.Copy, _close).ToList();
            if (list != RelatedList.Copies)
            {
                buttons.Add(_ink.Button("Not related", () =>
                {
                    if (_context.Report()?.CopyOf(_context.Source) is { } self)
                        _context.Scanner.Store.Unrelate(self, row.Copy);
                    Fill();
                }));
            }

            _content.Children.Add(_ink.Boxed(new StackPanel { Spacing = 4, Children = { details, DuplicateText.Buttons([.. buttons]) } }));
        }

        if (rows.Count > 100)
            _content.Children.Add(_ink.Label($"and {rows.Count - 100} more.", 12, colour: _ink.Sub));
    }
}
