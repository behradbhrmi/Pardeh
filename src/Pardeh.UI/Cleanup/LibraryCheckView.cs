using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Pardeh.Core.Cleanup;
using Pardeh.Core.Library;
using Pardeh.Core.OnlineInfo;

namespace Pardeh.UI.CleanupViews;

/// <summary>What the "Check Library Online" screen works with and asks the app to do.</summary>
internal sealed record LibraryCheckContext(LibraryCheck Check, OnlineStore Store, MediaLibrary Library, CleanupScanner Cleanup)
{
    /// <summary>Explains what will be sent, asks, and starts the check of these songs.</summary>
    public Func<IReadOnlyList<SongToCheck>, Task> Start { get; init; } = _ => Task.CompletedTask;

    /// <summary>Opens Fix Info for one song.</summary>
    public Func<string, Task<ReviewResult>> Review { get; init; } = _ => Task.FromResult(new ReviewResult(ReviewAnswer.Cancel));

    public Func<Task> EditPatterns { get; init; } = () => Task.CompletedTask;

    /// <summary>The ticked songs, whether to add covers, and the pattern to rename by (null: keep names).</summary>
    public Action<IReadOnlyList<BatchPick>, bool, string?> Apply { get; init; } = (_, _, _) => { };

    // The user's ignored songs are left out. Whether a file can be changed is asked when its turn comes: on a phone that is a
    // query for each one.
    private IEnumerable<LibraryItem> Songs() => Library.Music.Where(i => !Cleanup.Store.IsIgnored(i.Path));

    public static SongToCheck ToCheck(LibraryItem item) => new(item.Path, item.FileSize, item.Modified, item.DurationSeconds);

    /// <summary>The songs worth asking about now. Those with something to clean up come first: they gain the most.</summary>
    public IReadOnlyList<SongToCheck> Due() =>
    [
        .. Songs().Where(i => Store.IsDue(i.Path, i.FileSize, i.Modified))
            .OrderByDescending(i => Cleanup.ProposalFor(i.Path) is { Level: not CleanupLevel.Clean })
            .Select(ToCheck)
            .Where(s => !Check.WasPassedOver(s)),
    ];

    /// <summary>The saved results of songs not yet dealt with.</summary>
    public IReadOnlyList<(LibraryItem Item, OnlineEntry Entry)> Results() =>
    [
        .. Songs().Select(i => (Item: i, Entry: Store.Get(i.Path, i.FileSize, i.Modified)))
            .Where(r => r.Entry is { Applied: false })
            .Select(r => (r.Item, r.Entry!)),
    ];
}

/// <summary>
/// "Check Library Online": starting, pausing and stopping the check, and what it has found so far in three groups. Strong
/// matches are ticked; the rest are looked at one at a time. Nothing is changed before a summary is shown and agreed to.
/// </summary>
internal sealed class LibraryCheckView : UserControl
{
    /// <summary>
    /// Roughly what is downloaded, rounded up, for the user. A famous song's search answer is the biggest, at about 90 KB
    /// compressed; most songs need one search, and some up to four.
    /// </summary>
    internal const int KilobytesPerSong = 100, KilobytesPerCover = 100;

    private sealed record Heading(string Text);

    private sealed record Line(string Source, OnlineEntry Entry, bool Reviewed, bool Covers);

    private readonly CleanupInk _ink;
    private readonly LibraryCheckContext _context;
    private readonly HashSet<string> _unticked = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (ReviewResult Result, OnlineEntry Entry)> _reviewed = new(StringComparer.Ordinal);
    private readonly ObservableCollection<object> _rows = [];
    private readonly ListBox _list = new() { Background = Avalonia.Media.Brushes.Transparent, Padding = new Thickness(0) };
    private readonly TextBlock _status;
    private readonly TextBlock _counts;
    private readonly ProgressBar _progress = new() { Height = 6, MinWidth = 0 };
    private readonly Button _start;
    private readonly Button _pause;
    private readonly Button _resume;
    private readonly Button _stop;
    private readonly CheckBox _covers;
    private readonly TextBlock _coverNote;
    private readonly CheckBox _rename;
    private readonly ComboBox _patterns;
    private readonly Button _applyButton;
    private readonly Grid _page = new() { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 10 };
    private readonly StackPanel _busy = new() { Spacing = 12, VerticalAlignment = VerticalAlignment.Center, IsVisible = false, Margin = new Thickness(20) };
    private IReadOnlyList<(LibraryItem Item, OnlineEntry Entry)> _results = [];
    private IReadOnlyList<SongToCheck> _due = [];

    public LibraryCheckView(CleanupInk ink, LibraryCheckContext context, Action close)
    {
        _ink = ink;
        _context = context;
        _status = ink.Label(string.Empty);
        _counts = ink.Label(string.Empty, 12, colour: ink.Sub);
        _start = ink.Button("Check...", () => _ = context.Start(_due));
        _pause = ink.Button("Pause", context.Check.Pause);
        _resume = ink.Button("Resume", context.Check.Resume);
        _stop = ink.Button("Stop", context.Check.Stop);
        var tools = new WrapPanel { Children = { _start, _pause, _resume, _stop } };
        foreach (var child in tools.Children)
            child.Margin = new Thickness(0, 0, 8, 4);
        var top = new StackPanel { Spacing = 6, Children = { _status, _progress, tools, _counts } };

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

        _covers = ink.Tick("Also add missing covers", false);
        _coverNote = ink.Label($"Downloaded when you press Apply, for the ticked songs only: about {KilobytesPerCover} KB each, from the Cover Art Archive. "
            + "Cover pictures belong to their owners.", 11, colour: ink.Sub);
        _covers.IsCheckedChanged += (_, _) => Fill();
        _rename = ink.Tick("Rename files too", context.Cleanup.Store.RenameFiles);
        _patterns = ink.Combo();
        FillPatterns();
        var patternButton = ink.Button("Patterns...", async () =>
        {
            await context.EditPatterns();
            FillPatterns();
        });
        var naming = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { _patterns, patternButton } };
        Grid.SetColumn(patternButton, 1);
        // The list needs the room on a phone: the pattern shows once renaming is wanted.
        naming.IsVisible = !ink.Narrow || _rename.IsChecked == true;
        _rename.IsCheckedChanged += (_, _) => naming.IsVisible = !ink.Narrow || _rename.IsChecked == true;
        _applyButton = ink.Button("Apply", () => context.Apply(Picks(), _covers.IsChecked == true,
            _rename.IsChecked == true ? _patterns.SelectedItem as string : null), primary: true);
        var closeButton = ink.Cancels(ink.Button("Close", close));
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions(ink.Narrow ? "*,2*" : "*,Auto,Auto"), ColumnSpacing = 8, Children = { closeButton, _applyButton } };
        Grid.SetColumn(closeButton, ink.Narrow ? 0 : 1);
        Grid.SetColumn(_applyButton, ink.Narrow ? 1 : 2);
        closeButton.HorizontalAlignment = _applyButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        var bottom = new StackPanel { Spacing = ink.Narrow ? 2 : 6, Children = { _covers, _coverNote, _rename, naming, buttons } };

        Grid.SetRow(_list, 1);
        Grid.SetRow(bottom, 2);
        _page.Children.Add(top);
        _page.Children.Add(_list);
        _page.Children.Add(bottom);
        Content = new Panel { Children = { _page, _busy } };
        Fill();
    }

    private void FillPatterns()
    {
        var all = _context.Cleanup.Store.AllPatterns.ToList();
        _patterns.ItemsSource = all;
        _patterns.SelectedItem = all.Contains(_context.Cleanup.Store.DefaultPattern) ? _context.Cleanup.Store.DefaultPattern : all[0];
    }

    private static string Count(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";

    /// <summary>"under a minute", "about 5 minutes", "about 1 hour 20 minutes".</summary>
    internal static string About(TimeSpan time)
    {
        if (time < TimeSpan.FromSeconds(50))
            return "under a minute";
        var minutes = (int)Math.Ceiling(time.TotalMinutes);
        if (minutes < 60)
            return "about " + Count(minutes, "minute", "minutes");
        var rest = minutes % 60;
        return "about " + Count(minutes / 60, "hour", "hours") + (rest > 0 ? " " + Count(rest, "minute", "minutes") : string.Empty);
    }

    /// <summary>"under a minute to about 4 minutes".</summary>
    internal static string Range(int songs)
    {
        var (fastest, slowest) = LibraryCheck.Estimate(songs);
        var (low, high) = (About(fastest), About(slowest));
        return low == high ? low : low + " to " + high;
    }

    /// <summary>A size rounded up to whole megabytes.</summary>
    internal static string Data(int kilobytes) => string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, Math.Ceiling(kilobytes / 1000.0)):0} MB");

    /// <summary>Takes the check's state and the newest results.</summary>
    public void Fill()
    {
        _results = _context.Results();
        _due = _context.Due();
        FillStatus();

        var covers = _covers.IsChecked == true;
        _coverNote.IsVisible = covers;
        var items = new List<object>();
        void Group(string title, Func<OnlineEntry, bool> belongs)
        {
            var lines = _results.Where(r => belongs(r.Entry)).OrderBy(r => MediaSources.DisplayName(r.Item.Path), StringComparer.CurrentCultureIgnoreCase)
                .Select(r => (object)new Line(r.Item.Path, r.Entry, Reviewed(r.Item.Path, r.Entry) is not null, covers)).ToList();
            if (lines.Count == 0)
                return;
            items.Add(new Heading($"{title} ({lines.Count})"));
            items.AddRange(lines);
        }

        Group("Strong: ticked", e => e.Group == OnlineGroup.Strong);
        if (covers)
            Group("Details are right: only a cover to add", e => e is { Group: OnlineGroup.Checked, WantsCover: true });
        Group("Needs review: look one at a time", e => e.Group == OnlineGroup.Review);
        Group("No match: clean up or search by hand", e => e.Group == OnlineGroup.NoMatch);
        Sync(items);

        var found = (Strong: _results.Count(r => r.Entry.Group == OnlineGroup.Strong), Review: _results.Count(r => r.Entry.Group == OnlineGroup.Review),
            None: _results.Count(r => r.Entry.Group == OnlineGroup.NoMatch), Right: _results.Count(r => r.Entry.Group == OnlineGroup.Checked));
        _counts.IsVisible = _results.Count > 0;
        _counts.Text = $"Found: {found.Strong} strong, {found.Review} to review, {found.None} with no match, {found.Right} already right.";
        UpdateApply();
    }

    private void FillStatus()
    {
        var progress = _context.Check.Progress;
        var active = progress.IsActive;
        _start.IsVisible = !active && _due.Count > 0;
        _start.Content = $"Check {Count(_due.Count, "song", "songs")}...";
        _pause.IsVisible = progress.State is CheckState.Running or CheckState.Waiting;
        _resume.IsVisible = progress.State == CheckState.Paused;
        _stop.IsVisible = active;
        _progress.IsVisible = active;
        _progress.Maximum = Math.Max(1, progress.Total);
        _progress.Value = progress.Done;

        var failed = progress.Failed > 0 ? $" {Count(progress.Failed, "song", "songs")} couldn't be asked about; they are tried next time." : string.Empty;
        var skipped = progress.Skipped > 0 ? $" {Count(progress.Skipped, "song has", "songs have")} no title to search for, or can't be read or changed." : string.Empty;
        var at = $"{progress.Done} of {progress.Total}";
        _status.Text = progress.State switch
        {
            CheckState.Running => $"Checking: {at}." + (progress.Remaining is { } left ? " " + Capital(About(left)) + " left." : string.Empty)
                + " You can review what is found meanwhile; closing this screen doesn't stop the check.",
            CheckState.Waiting => $"Checking: {at}. {progress.Message} Trying again shortly.",
            CheckState.Paused => $"Paused at {at}. {progress.Message}".TrimEnd(),
            _ => (progress.State switch
            {
                CheckState.Done => "Finished." + failed + skipped + " ",
                CheckState.Stopped => $"Stopped at {at}; what was found is kept. {progress.Message}".TrimEnd() + " ",
                _ => string.Empty,
            }) + (_due.Count > 0 ? $"{Count(_due.Count, "song has", "songs have")} not been checked. Checking takes {Range(_due.Count)}, with up to {Data(_due.Count * KilobytesPerSong)} of data."
                : _context.Library.Music.Any() ? "No other song needs checking now." : "There are no songs in the library."),
        };
    }

    private static string Capital(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    // Rows are moved in and out one by one: replacing the list would jump to the top while results arrive.
    private void Sync(List<object> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (i < _rows.Count && _rows[i].Equals(items[i]))
                continue;
            var later = -1;
            for (var j = i + 1; j < _rows.Count && later < 0; j++)
                if (_rows[j].Equals(items[i]))
                    later = j;
            if (later < 0)
            {
                _rows.Insert(i, items[i]);
                continue;
            }

            for (; later > i; later--)
                _rows.RemoveAt(i);
        }

        while (_rows.Count > items.Count)
            _rows.RemoveAt(_rows.Count - 1);
    }

    // A choice made on a review holds while the result it was made from does.
    private ReviewResult? Reviewed(string source, OnlineEntry entry) =>
        _reviewed.TryGetValue(source, out var chosen) && chosen.Entry.Size == entry.Size && chosen.Entry.Modified == entry.Modified ? chosen.Result : null;

    private static bool TickedByDefault(OnlineEntry entry, bool covers) =>
        entry.Group == OnlineGroup.Strong || covers && entry is { Group: OnlineGroup.Checked, WantsCover: true };

    private static string FieldName(CleanupField field) =>
        CleanupInk.FieldName(new CleanupSuggestion(field, null, string.Empty, string.Empty, CleanupReason.Online, false)).ToLowerInvariant();

    internal static string Describe(OnlineEntry entry, ReviewResult? reviewed)
    {
        if (reviewed?.Choice is { } choice)
            return choice.Accepted.Count > 0 ? "Chosen: " + string.Join(", ", choice.Accepted.Select(s => FieldName(s.Field)).Distinct()) : "Only the file name.";
        if (entry.Candidates.Count == 0)
            return "Nothing usable on MusicBrainz.";
        var best = entry.Candidates[0];
        // The invisible left-to-right mark keeps the parts in order when a name is Persian.
        var name = best.Title + "‎ - " + best.ArtistText + (best.FirstYear > 0 ? "‎ · " + best.FirstYear.ToString(CultureInfo.InvariantCulture) : string.Empty);
        return entry.Group switch
        {
            OnlineGroup.Strong => name + "‎\nChanges: " + string.Join(", ", entry.Fields.Select(FieldName)),
            OnlineGroup.Checked => name,
            _ => name + "‎\n" + entry.Why,
        };
    }

    private Control RowFor(Line line)
    {
        var reviewed = Reviewed(line.Source, line.Entry);
        var byDefault = TickedByDefault(line.Entry, line.Covers);
        var tick = _ink.Tick(string.Empty, !_unticked.Contains(line.Source) && (byDefault || reviewed is not null));
        tick.IsEnabled = byDefault || reviewed is not null;
        tick.IsCheckedChanged += (_, _) =>
        {
            if (tick.IsChecked == true)
                _unticked.Remove(line.Source);
            else
                _unticked.Add(line.Source);
            UpdateApply();
        };
        var detail = _ink.Label(Describe(line.Entry, reviewed), 12, colour: _ink.Sub, wrap: _ink.Narrow);
        detail.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        if (_ink.Narrow)
            detail.MaxLines = 3;
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _ink.Label(MediaSources.DisplayName(line.Source) + (reviewed is not null ? "  (reviewed)" : string.Empty), bold: true, wrap: false), detail },
        };
        var open = _ink.Button("Review", async () => await Review(line));
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8, Children = { tick, text, open } };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(open, 2);
        return row;
    }

    private async Task Review(Line line)
    {
        var result = await _context.Review(line.Source);
        if (result.Answer == ReviewAnswer.Apply && result.Choice is { } choice && (choice.Accepted.Count > 0 || choice.Pattern is not null))
        {
            // The review may have looked the song up again.
            var item = _context.Library.Find(line.Source);
            var entry = item is null ? line.Entry : _context.Store.Get(item.Path, item.FileSize, item.Modified) ?? line.Entry;
            _reviewed[line.Source] = (result, entry);
            _unticked.Remove(line.Source);
        }
        else if (result.Answer == ReviewAnswer.Ignore)
        {
            _reviewed.Remove(line.Source);
            _context.Cleanup.Store.SetIgnored(line.Source, true);
        }

        // The row is drawn again even when its result is the same.
        _rows.Clear();
        Fill();
    }

    /// <summary>The ticked songs: reviewed ones with the user's choice, the others to take what is safe.</summary>
    internal IReadOnlyList<BatchPick> Picks()
    {
        var covers = _covers.IsChecked == true;
        var picks = new List<BatchPick>();
        foreach (var (item, entry) in _results)
        {
            if (_unticked.Contains(item.Path))
                continue;
            if (Reviewed(item.Path, entry) is { Choice: { } choice } result)
                picks.Add(new BatchPick(item.Path, entry, choice, result.Used));
            else if (TickedByDefault(entry, covers))
                picks.Add(new BatchPick(item.Path, entry));
        }

        return picks;
    }

    private void UpdateApply()
    {
        var count = Picks().Count;
        _applyButton.Content = count == 0 ? "Apply" : $"Apply to {Count(count, "song", "songs")}...";
        _applyButton.IsEnabled = count > 0;
    }

    // Goes up each time the list comes back.
    private int _shown;

    /// <summary>
    /// Takes the progress of one piece of work and shows it instead of the list. Reports that arrive after the list is back
    /// are dropped: they are posted, so the last one can come late and would cover the list for good.
    /// </summary>
    public Action<(int Done, int Total)> Busy(string message, Action? stop, string stopLabel = "Stop")
    {
        var mine = _shown;
        return progress =>
        {
            if (mine == _shown)
                ShowBusy(message, progress.Done, progress.Total, stop, stopLabel);
        };
    }

    private void ShowBusy(string message, int done, int total, Action? stop, string stopLabel)
    {
        _page.IsVisible = false;
        _busy.IsVisible = true;
        _busy.Children.Clear();
        _busy.Children.Add(_ink.Label(message, 15, bold: true));
        _busy.Children.Add(new ProgressBar { Minimum = 0, Maximum = Math.Max(1, total), Value = done, Height = 12 });
        _busy.Children.Add(_ink.Label($"{done} of {total}", colour: _ink.Sub));
        if (stop is not null)
            _busy.Children.Add(_ink.Button(stopLabel, stop));
    }

    /// <summary>Back to the list.</summary>
    /// <param name="changed">The batch went ahead: choices made for it are done with.</param>
    public void ShowList(bool changed)
    {
        _shown++;
        _busy.IsVisible = false;
        _page.IsVisible = true;
        if (changed)
        {
            _reviewed.Clear();
            _unticked.Clear();
        }

        _rows.Clear();
        Fill();
    }
}
