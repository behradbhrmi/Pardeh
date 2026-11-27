using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Pardeh.Core.Duplicates;
using Pardeh.UI.CleanupViews;

namespace Pardeh.UI.DuplicateViews;

/// <summary>What the "Find Duplicates" screen works with and asks the app to do.</summary>
internal sealed record DuplicatesContext(DuplicateScanner Scanner)
{
    /// <summary>The newest groups, or null before they have been worked out.</summary>
    public Func<DuplicateReport?> Report { get; init; } = () => null;

    public Func<DuplicateGroup, Task> Open { get; init; } = _ => Task.CompletedTask;

    /// <summary>"Keep the best in each group", for groups whose copies have the same sound.</summary>
    public Func<IReadOnlyList<GroupChoice>, Task> KeepBest { get; init; } = _ => Task.CompletedTask;

    public Func<Task> ShowRemoved { get; init; } = () => Task.CompletedTask;

    /// <summary>What the app knows that speaks for a copy: playlists and lyrics.</summary>
    public Func<SongCopy, int> Extra { get; init; } = _ => 0;

    /// <summary>The groups the user passed over this time.</summary>
    public HashSet<string> Skipped { get; init; } = new(StringComparer.Ordinal);

    public CopyAdvice Advise(DuplicateGroup group) => CopyAdvisor.Advise(group, i => Extra(group.Copies[i]));

    /// <summary>What "keep the best of each" would do: exact groups with a suggestion, not passed over.</summary>
    public IReadOnlyList<GroupChoice> Best(DuplicateReport report)
    {
        var choices = new List<GroupChoice>();
        foreach (var group in report.Groups.Where(g => g.IsExact && !Skipped.Contains(g.Id)))
        {
            if (Advise(group).Keep is not { } keep)
                continue;
            var remove = Enumerable.Range(0, group.Copies.Count).Where(i => i != keep && group.Copies[i].KnownFile).ToList();
            if (remove.Count > 0)
                choices.Add(new GroupChoice(group, keep, remove, CopyAdvisor.DefaultSources(group.Copies, keep)));
        }

        return choices;
    }
}

/// <summary>
/// "Find Duplicates": starting, pausing and stopping the check, and the groups it has found, the surest first. Nothing is
/// changed from the list itself except by "keep the best of each", which shows all it would do before doing it.
/// </summary>
internal sealed class DuplicatesView : UserControl
{
    private sealed record Heading(string Text);

    private sealed record Line(DuplicateGroup Group, bool Passed);

    private readonly CleanupInk _ink;
    private readonly DuplicatesContext _context;
    private readonly ObservableCollection<object> _rows = [];
    private readonly ListBox _list = new() { Background = Avalonia.Media.Brushes.Transparent, Padding = new Thickness(0) };
    private readonly TextBlock _status;
    private readonly ProgressBar _progress = new() { Height = 6, MinWidth = 0 };
    private readonly Button _start, _pause, _resume, _stop, _best;
    private readonly Grid _page = new() { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 10 };
    private readonly StackPanel _busy = new() { Spacing = 12, VerticalAlignment = VerticalAlignment.Center, IsVisible = false, Margin = new Thickness(20) };

    public DuplicatesView(CleanupInk ink, DuplicatesContext context, Action close)
    {
        _ink = ink;
        _context = context;
        _status = ink.Label(string.Empty);
        _start = ink.Button("Check", () => _ = context.Scanner.Start());
        _pause = ink.Button("Pause", context.Scanner.Pause);
        _resume = ink.Button("Resume", () => _ = context.Scanner.Start());
        _stop = ink.Button("Stop", context.Scanner.Stop);
        var removed = ink.Button("Removed files...", () => _ = context.ShowRemoved());
        var top = new StackPanel { Spacing = 6, Children = { _status, _progress, DuplicateText.Buttons(_start, _pause, _resume, _stop, removed) } };

        // Only once shown: a list that follows the rows belongs to the screen's thread from then on.
        _list.AttachedToVisualTree += (_, _) => _list.ItemsSource ??= _rows;
        _list.ItemTemplate = new FuncDataTemplate<object>((item, _) => item switch
        {
            Heading heading => ink.Label(heading.Text, 14, bold: true),
            Line line => RowFor(line),
            _ => null,
        });
        if (ink.Narrow)
            ScrollViewer.SetVerticalScrollBarVisibility(_list, Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden);

        _best = ink.Button("Keep the best of each...", () =>
        {
            if (_context.Report() is { } report)
                _ = context.KeepBest(context.Best(report));
        });
        var closeButton = ink.Cancels(ink.Button("Close", close));
        closeButton.HorizontalAlignment = _best.HorizontalAlignment = HorizontalAlignment.Stretch;
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions(ink.Narrow ? "*,2*" : "*,Auto,Auto"), ColumnSpacing = 8, Children = { closeButton, _best } };
        Grid.SetColumn(closeButton, ink.Narrow ? 0 : 1);
        Grid.SetColumn(_best, ink.Narrow ? 1 : 2);

        Grid.SetRow(_list, 1);
        Grid.SetRow(buttons, 2);
        _page.Children.Add(top);
        _page.Children.Add(_list);
        _page.Children.Add(buttons);
        Content = new Panel { Children = { _page, _busy } };
        Fill();
    }

    /// <summary>Takes the check's state and the newest groups.</summary>
    public void Fill()
    {
        var progress = _context.Scanner.Progress;
        _start.IsVisible = !progress.IsActive;
        _pause.IsVisible = progress.State == DuplicateScanState.Running;
        _resume.IsVisible = progress.State == DuplicateScanState.Paused;
        _stop.IsVisible = progress.IsActive;
        _progress.IsVisible = progress.IsActive;
        _progress.Maximum = Math.Max(1, progress.Total);
        _progress.Value = progress.Done;

        var report = _context.Report();
        _status.Text = Status(progress, report);
        _start.Content = report is { NotRead: > 0 } ? "Check" : "Check again";

        var items = new List<object>();
        if (report is not null)
        {
            foreach (var kind in report.Groups.Where(g => !_context.Skipped.Contains(g.Id)).GroupBy(g => g.Kind))
            {
                items.Add(new Heading($"{DuplicateText.Kind(kind.Key)} ({kind.Count()})"));
                items.AddRange(kind.Select(g => new Line(g, false)));
            }

            var passed = report.Groups.Where(g => _context.Skipped.Contains(g.Id)).ToList();
            if (passed.Count > 0)
            {
                items.Add(new Heading($"Skipped for now ({passed.Count})"));
                items.AddRange(passed.Select(g => new Line(g, true)));
            }
        }

        Sync(items);
        var best = report is null ? 0 : _context.Best(report).Count;
        _best.Content = best == 0 ? "Keep the best of each..." : $"Keep the best of {DuplicateText.Count(best, "group", "groups")}...";
        _best.IsEnabled = best > 0 && !progress.IsActive;
    }

    private static string Status(DuplicateProgress progress, DuplicateReport? report)
    {
        if (progress.State == DuplicateScanState.Paused)
            return $"Paused. {progress.Stage}: {progress.Done} of {progress.Total}.";
        if (progress.IsActive)
            return $"{progress.Stage}: {progress.Done} of {progress.Total}. What is found so far is below.";
        if (report is null)
            return "Looking at what is known...";
        var notRead = report.NotRead > 0 ? $" {DuplicateText.Count(report.NotRead, "song has", "songs have")} not been checked yet: press Check." : string.Empty;
        var broken = report.Unreadable > 0 ? $" {DuplicateText.Count(report.Unreadable, "file", "files")} can't be read." : string.Empty;
        if (progress.State == DuplicateScanState.Stopped)
            return "Stopped. What was read is kept; checking again goes on from there." + broken;
        if (report.Copies.Count == 0)
            return "The check reads your songs on this device. Nothing is changed or sent anywhere. Reading a large library takes a while the first time.";
        return (report.Groups.Count == 0 ? "No duplicates found." : $"{DuplicateText.Count(report.Groups.Count, "group", "groups")} of copies found. Open one to choose.")
            + notRead + broken;
    }

    // Rows that did not change stay, so the list keeps its place.
    private void Sync(List<object> items)
    {
        static string KeyOf(object row) => row is Line line ? $"{line.Passed}\n{line.Group.Kind}\n{line.Group.Id}" : ((Heading)row).Text;
        for (var i = 0; i < items.Count; i++)
        {
            if (i >= _rows.Count)
                _rows.Add(items[i]);
            else if (KeyOf(_rows[i]) != KeyOf(items[i]))
                _rows[i] = items[i];
        }

        while (_rows.Count > items.Count)
            _rows.RemoveAt(_rows.Count - 1);
    }

    private Control RowFor(Line line)
    {
        var group = line.Group;
        var advice = _context.Advise(group);
        var song = group.Copies.Select(c => c).OrderByDescending(c => c.Facts.Tags.Title?.Length > 0).First();
        var title = _ink.Label(DuplicateText.Song(song), bold: true, wrap: _ink.Narrow);
        title.MaxLines = 2;
        var detail = _ink.Label(DuplicateText.Count(group.Copies.Count, "copy", "copies") + DuplicateText.Dot
            + (advice.Keep is { } keep ? "Suggested: keep " + group.Copies[keep].Facts.FileName : "No suggestion"), 12, colour: _ink.Sub, wrap: _ink.Narrow);
        detail.MaxLines = 2;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title, detail } };
        var open = _ink.Button("Look...", async () =>
        {
            _context.Skipped.Remove(group.Id);
            await _context.Open(group);
        });
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { text, open } };
        Grid.SetColumn(open, 1);
        return row;
    }

    // ---- while files are changed --------------------------------------------------------------------

    // Goes up each time the list comes back: a progress report that was posted can arrive after that, and is dropped.
    private int _shown;

    public Action<(int Done, int Total)> Busy(string message, Action? stop)
    {
        var mine = _shown;
        return progress =>
        {
            if (mine != _shown)
                return;
            _page.IsVisible = false;
            _busy.IsVisible = true;
            _busy.Children.Clear();
            _busy.Children.Add(_ink.Label(message, 15, bold: true));
            _busy.Children.Add(new ProgressBar { Minimum = 0, Maximum = Math.Max(1, progress.Total), Value = progress.Done, Height = 12 });
            _busy.Children.Add(_ink.Label($"{progress.Done} of {progress.Total}", colour: _ink.Sub));
            if (stop is not null)
                _busy.Children.Add(_ink.Button("Stop after this file", stop));
        };
    }

    public void ShowList()
    {
        _shown++;
        _busy.IsVisible = false;
        _page.IsVisible = true;
        Fill();
    }
}
