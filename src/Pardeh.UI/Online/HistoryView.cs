using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Pardeh.Core.OnlineInfo;
using Pardeh.UI.CleanupViews;

namespace Pardeh.UI.OnlineViews;

/// <summary>What the "Looked-Up Songs" screens work with and ask the app to do.</summary>
internal sealed record HistoryContext(LookupHistory History)
{
    /// <summary>Whether the app keeps a history, and the switch for it. Turning it off clears the history.</summary>
    public Func<bool> Keeps { get; init; } = () => true;

    public Func<bool, Task> SetKeeps { get; init; } = _ => Task.CompletedTask;

    /// <summary>Asks, then clears.</summary>
    public Func<Task> Clear { get; init; } = () => Task.CompletedTask;

    /// <summary>Shows one look-up.</summary>
    public Func<LookupHistoryRow, Task> Open { get; init; } = _ => Task.CompletedTask;

    /// <summary>What became of the row's file, or null when it is there (or the row has none).</summary>
    public Func<LookupHistoryRow, string?> FileState { get; init; } = _ => null;

    public Func<string, bool> CanFixInfo { get; init; } = _ => false;

    public Func<string, Task> FixInfo { get; init; } = _ => Task.CompletedTask;

    /// <summary>Asks the service again. Returns what to tell the user.</summary>
    public Func<LookupHistoryRow, CancellationToken, Task<string>> LookAgain { get; init; } = (_, _) => Task.FromResult(string.Empty);

    /// <summary>Shows the host, asks, and opens the page in the browser.</summary>
    public Func<Uri, string, Task> OpenPage { get; init; } = (_, _) => Task.CompletedTask;
}

/// <summary>How a look-up reads on screen.</summary>
internal static class HistoryText
{
    // The invisible left-to-right mark keeps the parts in order when some are Persian.
    public const string Dot = "‎ · ";

    public static string Outcome(HistoryOutcome outcome) => outcome switch
    {
        HistoryOutcome.Strong => "Strong match",
        HistoryOutcome.Review => "Needs review",
        HistoryOutcome.Right => "Already right",
        HistoryOutcome.Found => "Found",
        _ => "No match",
    };

    public static string Song(LookupHistoryRow row) =>
        row.Title.Length > 0 ? Pair(row.Title, row.Artist) : Pair(row.SearchedTitle, row.SearchedArtist ?? string.Empty);

    public static string Pair(string title, string artist) => artist.Length > 0 ? title + "‎ - " + artist : title;

    public static string Sent(LookupHistoryRow row) => "Sent: " + Pair(row.SearchedTitle, row.SearchedArtist ?? string.Empty)
        + (row.SearchedArtist is null ? "‎ (title only)" : string.Empty);

    public static string When(DateTime when) => when.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture);

    public static string Facts(LookupHistoryRow row) =>
        When(row.When) + Dot + row.Service + Dot + Outcome(row.Outcome) + (row.Source is null ? Dot + "search by hand" : string.Empty);

    public static string Describe(HistoryMatch match)
    {
        var parts = new List<string> { Pair(match.Title, match.Artist) };
        if (match.Album.Length > 0)
            parts.Add(match.Album);
        if (match.Year > 0)
            parts.Add(match.Year.ToString(CultureInfo.InvariantCulture));
        if (match.LengthMs > 0)
            parts.Add(TimeSpan.FromMilliseconds(match.LengthMs).ToString(@"m\:ss", CultureInfo.InvariantCulture));
        return string.Join(Dot, parts);
    }
}

/// <summary>"Looked-Up Songs": the history of look-ups, searched and filtered. Opening it sends nothing.</summary>
internal sealed class HistoryView : UserControl
{
    private static readonly (string Text, HistoryOutcome? Value)[] Outcomes =
    [
        ("Any result", null), ("Strong match", HistoryOutcome.Strong), ("Needs review", HistoryOutcome.Review),
        ("Already right", HistoryOutcome.Right), ("Found by hand", HistoryOutcome.Found), ("No match", HistoryOutcome.NoMatch),
    ];

    private const string AnyService = "Any service";

    private readonly CleanupInk _ink;
    private readonly HistoryContext _context;
    private readonly TextBox _search;
    private readonly ComboBox _outcome;
    private readonly ComboBox _service;
    private readonly TextBlock _status;
    private readonly ObservableCollection<LookupHistoryRow> _rows = [];
    private readonly ListBox _list = new() { Background = Avalonia.Media.Brushes.Transparent, Padding = new Thickness(0) };
    private readonly CheckBox _keep;
    private readonly Button _clear;
    private bool _filling;

    public HistoryView(CleanupInk ink, HistoryContext context, Action close)
    {
        _ink = ink;
        _context = context;
        _search = new TextBox { PlaceholderText = "Search the history", MinHeight = ink.Narrow ? 42 : 24, VerticalContentAlignment = VerticalAlignment.Center };
        _search.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                Fill();
        };
        _outcome = ink.Combo();
        _outcome.ItemsSource = Outcomes.Select(o => o.Text).ToList();
        _outcome.SelectedIndex = 0;
        _outcome.SelectionChanged += (_, _) => Fill();
        _service = ink.Combo();
        _service.SelectionChanged += (_, _) =>
        {
            if (!_filling)
                Fill();
        };
        var filters = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 8, Children = { _outcome, _service } };
        Grid.SetColumn(_service, 1);
        _status = ink.Label(string.Empty, 12, colour: ink.Sub);
        var top = new StackPanel { Spacing = 6, Children = { _search, filters, _status } };

        // Only once shown: a list that follows the rows belongs to the screen's thread from then on.
        _list.AttachedToVisualTree += (_, _) => _list.ItemsSource ??= _rows;
        _list.ItemTemplate = new FuncDataTemplate<LookupHistoryRow>((row, _) => row is null ? null : RowFor(row));
        if (ink.Narrow)
            ScrollViewer.SetVerticalScrollBarVisibility(_list, Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden);

        _keep = ink.Tick("Keep this history (on this device only)", context.Keeps());
        _keep.IsCheckedChanged += async (_, _) =>
        {
            if (_filling)
                return;
            await context.SetKeeps(_keep.IsChecked == true);
            Fill();
        };
        _clear = ink.Button("Clear history...", async () =>
        {
            await context.Clear();
            Fill();
        });
        var closeButton = ink.Cancels(ink.Button("Close", close));
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions(ink.Narrow ? "*,*" : "Auto,*,Auto"), ColumnSpacing = 8, Children = { _clear, closeButton } };
        Grid.SetColumn(closeButton, ink.Narrow ? 1 : 2);
        _clear.HorizontalAlignment = closeButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        var bottom = new StackPanel { Spacing = 6, Children = { _keep, buttons } };

        var page = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 10, Children = { top, _list, bottom } };
        Grid.SetRow(_list, 1);
        Grid.SetRow(bottom, 2);
        Content = page;
        Fill();
    }

    internal IReadOnlyList<LookupHistoryRow> Shown => _rows;

    internal string Status => _status.Text ?? string.Empty;

    internal TextBox SearchBox => _search;

    public void Fill()
    {
        if (_filling)
            return;
        _filling = true;
        try
        {
            var all = _context.History.Rows;
            var chosen = _service.SelectedItem as string ?? AnyService;
            var services = new List<string> { AnyService };
            services.AddRange(all.Select(r => r.Service).Distinct().Order(StringComparer.Ordinal));
            if (_service.ItemsSource is not List<string> listed || !listed.SequenceEqual(services))
            {
                _service.ItemsSource = services;
                _service.SelectedItem = services.Contains(chosen) ? chosen : AnyService;
            }

            var service = _service.SelectedItem as string is { } one && one != AnyService ? one : null;
            var found = LookupHistory.Filter(all, _search.Text, Outcomes[Math.Max(0, _outcome.SelectedIndex)].Value, service).ToList();
            if (!found.SequenceEqual(_rows))
            {
                _rows.Clear();
                foreach (var row in found)
                    _rows.Add(row);
            }

            _keep.IsChecked = _context.Keeps();
            _clear.IsEnabled = all.Count > 0;
            _status.Text = all.Count == 0
                ? _context.Keeps()
                    ? "Nothing yet. Songs you look up online are listed here. Opening this screen sends nothing."
                    : "No history is kept."
                : found.Count == all.Count ? Count(all.Count) + "." : $"{found.Count} of {Count(all.Count)}.";
        }
        finally
        {
            _filling = false;
        }
    }

    private static string Count(int count) => count == 1 ? "1 look-up" : $"{count} look-ups";

    private Control RowFor(LookupHistoryRow row)
    {
        var lines = new List<string> { HistoryText.Facts(row) };
        if (_context.FileState(row) is { } state)
            lines.Add(state);
        lines.Add(row.Matches.Count > 0 ? HistoryText.Describe(row.Matches[0]) : HistoryText.Sent(row));
        var detail = _ink.Label(string.Join("\n", lines), 12, colour: _ink.Sub, wrap: _ink.Narrow);
        detail.TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis;
        if (_ink.Narrow)
            detail.MaxLines = 5;
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _ink.Label(HistoryText.Song(row), bold: true, wrap: false), detail },
        };
        var open = _ink.Button("Open", async () =>
        {
            await _context.Open(row);
            Fill();
        });
        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8, Children = { text, open } };
        Grid.SetColumn(open, 1);
        return line;
    }
}

/// <summary>One look-up: what was sent, what came back, and what can be done with it.</summary>
internal sealed class HistoryEntryView : UserControl
{
    private readonly CleanupInk _ink;
    private readonly HistoryContext _context;
    private readonly TextBlock _note;
    private CancellationTokenSource? _asking;

    public HistoryEntryView(CleanupInk ink, HistoryContext context, LookupHistoryRow row, Action close)
    {
        _ink = ink;
        _context = context;
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(ink.Label(HistoryText.Song(row), 15, bold: true));
        body.Children.Add(ink.Label(HistoryText.Facts(row), 12, colour: ink.Sub));
        body.Children.Add(ink.Label(HistoryText.Sent(row), 12, colour: ink.Sub));
        if (row.Source is not null)
            body.Children.Add(ink.Label("File: " + row.FileName + (context.FileState(row) is { } state ? "\n" + state : string.Empty), 12, colour: ink.Sub));

        body.Children.Add(ink.Label(row.Matches.Count switch { 0 => "Nothing was found.", 1 => "Found:", var n => $"Found ({n}):" }, bold: true));
        foreach (var match in row.Matches)
            body.Children.Add(MatchBox(row, match));

        _note = ink.Label(string.Empty);
        _note.IsVisible = false;
        body.Children.Add(_note);

        var actions = new WrapPanel();
        void Action(Button button)
        {
            button.Margin = new Thickness(0, 0, 8, 6);
            actions.Children.Add(button);
        }

        if (row.Source is { } source && context.FileState(row) is null && context.CanFixInfo(source))
            Action(ink.Button("Fix Info...", async () =>
            {
                close();
                await context.FixInfo(source);
            }));
        Button again = null!;
        again = ink.Button("Look up again", async () =>
        {
            again.IsEnabled = false;
            Say("Asking " + row.Service + "...");
            var asking = _asking = new CancellationTokenSource();
            var said = await context.LookAgain(row, asking.Token);
            if (!asking.IsCancellationRequested)
                Say(said);
            again.IsEnabled = true;
        });
        Action(again);
        Action(ink.Button("Delete", () =>
        {
            context.History.Delete(row.Id);
            close();
        }));
        DetachedFromVisualTree += (_, _) => _asking?.Cancel();

        var closeButton = ink.Cancels(ink.Button("Close", close));
        closeButton.HorizontalAlignment = ink.Narrow ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        var bottom = new StackPanel { Spacing = 2, Children = { actions, closeButton } };
        var scroller = ink.Scroller(body);
        var page = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 10, Children = { scroller, bottom } };
        Grid.SetRow(bottom, 1);
        Content = page;
    }

    private void Say(string text)
    {
        _note.Text = text;
        _note.IsVisible = text.Length > 0;
    }

    private Control MatchBox(LookupHistoryRow row, HistoryMatch match)
    {
        var inside = new StackPanel { Spacing = 6, Children = { _ink.Label(HistoryText.Describe(match)) } };
        var buttons = new WrapPanel();
        if (LookupHistory.PageOf(row.Service, match) is { } page)
        {
            var open = _ink.Button("Open on " + row.Service, () => _ = _context.OpenPage(page, "The song's page on " + row.Service + "."));
            open.Margin = new Thickness(0, 0, 8, 0);
            buttons.Children.Add(open);
        }

        if (buttons.Children.Count > 0)
            inside.Children.Add(buttons);
        return _ink.Boxed(inside);
    }
}
